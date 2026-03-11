using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace VideoDownloaderUI
{
    public partial class MainWindow : Window
    {
        // Current progress 0–100
        private double _currentProgress = 0;

        public MainWindow()
        {
            InitializeComponent();
        }

        // Called when the track Grid changes size (window resize etc.)
        // Re-applies current progress so the fill width stays correct.
        private void ProgressTrack_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SetProgressFillWidth(_currentProgress, animate: false);
        }

        /// <summary>
        /// Updates the custom progress bar fill width.
        /// trackWidth * (percent / 100) = fill pixel width.
        /// </summary>
        private void SetProgressFillWidth(double percent, bool animate = true)
        {
            _currentProgress = Math.Max(0, Math.Min(100, percent));

            double trackWidth = ProgressTrack.ActualWidth;
            if (trackWidth <= 0) return;

            double targetWidth = trackWidth * (_currentProgress / 100.0);

            // Show/hide the fill
            ProgressFill.Visibility = _currentProgress > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (animate && _currentProgress > 0)
            {
                var anim = new DoubleAnimation
                {
                    To       = targetWidth,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                ProgressFill.BeginAnimation(WidthProperty, anim);
            }
            else
            {
                ProgressFill.BeginAnimation(WidthProperty, null); // stop running anim
                ProgressFill.Width = targetWidth;
            }

            PercentageTextBlock.Text =
                _currentProgress.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        // ─────────────────────────────────────────────
        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Please enter a valid URL.");
                return;
            }

            string quality = (QualityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "best";
            string format  = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()  ?? "mp4";

            DownloadButton.IsEnabled = false;
            LogTextBlock.Text        = "";
            StatusTextBlock.Text     = "Starting...";
            SetProgressFillWidth(0, animate: false);

            try
            {
                await Task.Run(() => RunDownloader(url, quality, format));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                DownloadButton.IsEnabled = true;
                StatusTextBlock.Text     = "Ready";
            }
        }

        private string GetPythonExecutable()
        {
            foreach (var name in new[] { "python", "python3", "py" })
            {
                try
                {
                    using var p = new Process();
                    p.StartInfo.FileName               = name;
                    p.StartInfo.Arguments              = "--version";
                    p.StartInfo.UseShellExecute        = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.CreateNoWindow         = true;
                    p.Start();
                    p.WaitForExit();
                    if (p.ExitCode == 0) return name;
                }
                catch { }
            }
            return "python";
        }

        private void RunDownloader(string url, string quality, string format)
        {
            string pythonExe = GetPythonExecutable();

            var start = new ProcessStartInfo
            {
                FileName               = pythonExe,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            start.ArgumentList.Add("downloader.py");
            start.ArgumentList.Add(url);
            start.ArgumentList.Add("--quality");
            start.ArgumentList.Add(quality);
            start.ArgumentList.Add("--format");
            start.ArgumentList.Add(format);

            using var process = Process.Start(start);
            if (process == null) return;

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Dispatcher.Invoke(() => ProcessOutput(e.Data));
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Dispatcher.Invoke(() => AppendLog("[ERR] " + e.Data));
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
        }

        private void ProcessOutput(string data)
        {
            // ── Progress update ──────────────────────────────────────────
            if (data.Contains("[PROGRESS]"))
            {
                int    idx        = data.IndexOf("[PROGRESS]") + "[PROGRESS]".Length;
                string percentStr = data.Substring(idx).Trim().Replace(",", ".");

                var match = System.Text.RegularExpressions.Regex.Match(percentStr, @"\d+(\.\d+)?");
                if (match.Success &&
                    double.TryParse(match.Value,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double pct))
                {
                    SetProgressFillWidth(pct);
                }

                // Don't echo pure progress lines to the log
                if (data.TrimStart().StartsWith("[PROGRESS]")) return;
            }

            // ── Status / warning / error ──────────────────────────────────
            if (data.StartsWith("[STATUS]"))
                StatusTextBlock.Text = data.Replace("[STATUS]", "").Trim();

            AppendLog(data);
        }

        private void AppendLog(string line)
        {
            LogTextBlock.Text += line + "\n";
            LogScrollViewer.ScrollToEnd();
        }
    }
}
