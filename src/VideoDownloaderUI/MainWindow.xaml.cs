using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VideoDownloaderUI
{
    // ── Download states ───────────────────────────────────────────────────
    internal enum DownloadState { Idle, Downloading, Paused }

    public partial class MainWindow : Window
    {
        private double       _currentProgress = 0;
        private DownloadState _state          = DownloadState.Idle;

        // Saved session so Resume can replay the same command
        private string _savedUrl     = "";
        private string _savedQuality = "best";
        private string _savedFormat  = "mp4";

        private Process?              _activeProcess = null;
        private CancellationTokenSource? _cts        = null;
        private readonly object       _processLock   = new object();

        public MainWindow() => InitializeComponent();

        // ── UI helpers ────────────────────────────────────────────────────

        private void ApplyState(DownloadState s)
        {
            _state = s;

            DownloadButton.Visibility = s == DownloadState.Idle        ? Visibility.Visible : Visibility.Collapsed;
            StopButton.Visibility     = s == DownloadState.Downloading  ? Visibility.Visible : Visibility.Collapsed;
            ResumeButton.Visibility   = s == DownloadState.Paused       ? Visibility.Visible : Visibility.Collapsed;
            CancelButton.Visibility   = s != DownloadState.Idle         ? Visibility.Visible : Visibility.Collapsed;

            // Status dot colour: teal=ready, orange=downloading, purple=paused
            StatusDot.Fill = s switch
            {
                DownloadState.Downloading => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                DownloadState.Paused      => new SolidColorBrush(Color.FromRgb(0xBB, 0x86, 0xFC)),
                _                         => (SolidColorBrush)FindResource("SecondaryColor")
            };
        }

        private void ProgressTrack_SizeChanged(object sender, SizeChangedEventArgs e)
            => SetProgressFillWidth(_currentProgress, animate: false);

        private void SetProgressFillWidth(double percent, bool animate = true)
        {
            _currentProgress = Math.Max(0, Math.Min(100, percent));
            double trackWidth  = ProgressTrack.ActualWidth;
            if (trackWidth <= 0) return;

            double targetWidth = trackWidth * (_currentProgress / 100.0);
            ProgressFill.Visibility = _currentProgress > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (animate && _currentProgress > 0)
            {
                ProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation
                {
                    To             = targetWidth,
                    Duration       = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });
            }
            else
            {
                ProgressFill.BeginAnimation(WidthProperty, null);
                ProgressFill.Width = targetWidth;
            }

            PercentageTextBlock.Text =
                _currentProgress.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        // ── Download ──────────────────────────────────────────────────────

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) { MessageBox.Show("Please enter a valid URL."); return; }

            _savedUrl     = url;
            _savedQuality = (QualityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "best";
            _savedFormat  = (FormatComboBox.SelectedItem  as ComboBoxItem)?.Tag?.ToString()  ?? "mp4";

            // Full reset only when starting fresh (not resuming)
            LogTextBlock.Text    = "";
            StatusTextBlock.Text = "Starting...";
            SetProgressFillWidth(0, animate: false);

            await StartDownloadAsync();
        }

        // ── Stop (pause) ──────────────────────────────────────────────────

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            // Kill the process — yt-dlp has already flushed the .part file to disk
            KillActiveProcess();

            ApplyState(DownloadState.Paused);
            AppendLog("\n[PAUSED] Download paused — press ▶ RESUME to continue.");
            StatusTextBlock.Text = "Paused";
        }

        // ── Resume ────────────────────────────────────────────────────────

        private async void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_savedUrl)) return;

            AppendLog("\n[RESUMING] Continuing download from where it stopped...");
            StatusTextBlock.Text = "Resuming...";

            await StartDownloadAsync();      // yt-dlp + continuedl:True picks up the .part file
        }

        // ── Cancel ────────────────────────────────────────────────────────

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            KillActiveProcess();

            // Full UI reset
            LogTextBlock.Text        = "";
            StatusTextBlock.Text     = "Cancelled";
            SetProgressFillWidth(0, animate: false);
            PercentageTextBlock.Text = "0%";
            _savedUrl = "";

            ApplyState(DownloadState.Idle);
        }

        // ── Shared launch helper ──────────────────────────────────────────

        /// <summary>
        /// Starts (or resumes) the download using _savedUrl / _savedQuality / _savedFormat.
        /// yt-dlp's continuedl=True inside downloader.py makes resume transparent.
        /// </summary>
        private async Task StartDownloadAsync()
        {
            ApplyState(DownloadState.Downloading);
            _cts = new CancellationTokenSource();

            try
            {
                await Task.Run(() => RunDownloader(_savedUrl, _savedQuality, _savedFormat, _cts.Token));
            }
            catch (OperationCanceledException)
            {
                // Handled by Stop / Cancel buttons — do not reset state here
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
                ApplyState(DownloadState.Idle);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;

                // Only reset to Idle if we finished normally (not paused/cancelled)
                if (_state == DownloadState.Downloading)
                    ApplyState(DownloadState.Idle);
            }
        }

        // ── Kill helper ───────────────────────────────────────────────────

        private void KillActiveProcess()
        {
            lock (_processLock)
            {
                try
                {
                    if (_activeProcess != null && !_activeProcess.HasExited)
                        _activeProcess.Kill(entireProcessTree: true);
                }
                catch { }
                finally { _cts?.Cancel(); }
            }
        }

        // ── Python process ────────────────────────────────────────────────

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
                    p.Start(); p.WaitForExit();
                    if (p.ExitCode == 0) return name;
                }
                catch { }
            }
            return "python";
        }

        private void RunDownloader(string url, string quality, string format, CancellationToken ct)
        {
            string pythonExe = GetPythonExecutable();

            var si = new ProcessStartInfo
            {
                FileName               = pythonExe,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            si.ArgumentList.Add("downloader.py");
            si.ArgumentList.Add(url);
            si.ArgumentList.Add("--quality"); si.ArgumentList.Add(quality);
            si.ArgumentList.Add("--format");  si.ArgumentList.Add(format);

            using var process = Process.Start(si);
            if (process == null) return;

            lock (_processLock) _activeProcess = process;

            // Auto-kill if token is cancelled (Stop / Cancel buttons)
            using var reg = ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
            });

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

            lock (_processLock) _activeProcess = null;

            ct.ThrowIfCancellationRequested();
        }

        // ── Output parsing ────────────────────────────────────────────────

        private void ProcessOutput(string data)
        {
            if (data.Contains("[PROGRESS]"))
            {
                int    idx  = data.IndexOf("[PROGRESS]") + "[PROGRESS]".Length;
                string pStr = data.Substring(idx).Trim().Replace(",", ".");

                var m = System.Text.RegularExpressions.Regex.Match(pStr, @"\d+(\.\d+)?");
                if (m.Success &&
                    double.TryParse(m.Value,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double pct))
                    SetProgressFillWidth(pct);

                if (data.TrimStart().StartsWith("[PROGRESS]")) return;
            }

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
