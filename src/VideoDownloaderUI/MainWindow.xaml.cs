using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VideoDownloaderUI
{
    internal enum DownloadState { Idle, Downloading, Paused, WaitingConfirm }

    public partial class MainWindow : Window
    {
        private static readonly string[] AudioFormats = { "mp3", "m4a", "wav" };

        private double        _currentProgress = 0;
        private DownloadState _state           = DownloadState.Idle;

        private string _savedUrl      = "";
        private string _savedQuality  = "best";
        private string _savedFormat   = "mp4";
        private bool   _overwrite     = false;          // set to true when user confirms re-download
        private string _detectedFile  = "";             // path found by --check-only

        private Process?                 _activeProcess = null;
        private CancellationTokenSource? _cts           = null;
        private readonly object          _processLock   = new object();

        public MainWindow() => InitializeComponent();

        // ── Format ComboBox ───────────────────────────────────────────────

        private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FormatComboBox == null) return;
            string fmt    = GetSelectedFormat();
            bool isAudio  = AudioFormats.Contains(fmt);

            if (AudioBadge    != null) AudioBadge.Visibility  = isAudio ? Visibility.Visible : Visibility.Collapsed;
            if (QualityComboBox != null) { QualityComboBox.IsEnabled = !isAudio; QualityComboBox.Opacity = isAudio ? 0.4 : 1.0; }
            if (DownloadButton  != null && _state == DownloadState.Idle)
                DownloadButton.Content = isAudio ? "🎵 EXTRACT AUDIO" : "⬇ DOWNLOAD NOW";

            if (ProgressFill != null)
                ProgressFill.Background = isAudio
                    ? new LinearGradientBrush(new GradientStopCollection {
                          new GradientStop(Color.FromRgb(0xAA,0x00,0x55),0.0),
                          new GradientStop(Color.FromRgb(0xFF,0x6B,0x6B),0.5),
                          new GradientStop(Color.FromRgb(0xCC,0x00,0x44),1.0) },
                          new Point(0,0), new Point(1,0))
                    : new LinearGradientBrush(new GradientStopCollection {
                          new GradientStop(Color.FromRgb(0x1A,0x6B,0x00),0.0),
                          new GradientStop(Color.FromRgb(0x39,0xFF,0x14),0.5),
                          new GradientStop(Color.FromRgb(0x1D,0xB3,0x00),1.0) },
                          new Point(0,0), new Point(1,0));
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private string GetSelectedFormat()
            => (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

        private void ApplyState(DownloadState s)
        {
            _state = s;
            bool isAudio = AudioFormats.Contains(_savedFormat.Length > 0 ? _savedFormat : GetSelectedFormat());

            DownloadButton.Visibility = s == DownloadState.Idle           ? Visibility.Visible : Visibility.Collapsed;
            StopButton.Visibility     = s == DownloadState.Downloading     ? Visibility.Visible : Visibility.Collapsed;
            ResumeButton.Visibility   = s == DownloadState.Paused          ? Visibility.Visible : Visibility.Collapsed;
            CancelButton.Visibility   = (s == DownloadState.Downloading || s == DownloadState.Paused)
                                            ? Visibility.Visible : Visibility.Collapsed;
            ConfirmPanel.Visibility   = s == DownloadState.WaitingConfirm  ? Visibility.Visible : Visibility.Collapsed;

            if (s == DownloadState.Idle)
                DownloadButton.Content = AudioFormats.Contains(GetSelectedFormat()) ? "🎵 EXTRACT AUDIO" : "⬇ DOWNLOAD NOW";

            StatusDot.Fill = s switch
            {
                DownloadState.Downloading    => new SolidColorBrush(Color.FromRgb(0xFF,0x98,0x00)),
                DownloadState.Paused         => new SolidColorBrush(Color.FromRgb(0xBB,0x86,0xFC)),
                DownloadState.WaitingConfirm => new SolidColorBrush(Color.FromRgb(0xFF,0x98,0x00)),
                _                            => (SolidColorBrush)FindResource("SecondaryColor")
            };
        }

        private void ProgressTrack_SizeChanged(object sender, SizeChangedEventArgs e)
            => SetProgressFillWidth(_currentProgress, animate: false);

        private void SetProgressFillWidth(double percent, bool animate = true)
        {
            _currentProgress = Math.Max(0, Math.Min(100, percent));
            double trackWidth = ProgressTrack.ActualWidth;
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

        // ── Download button ───────────────────────────────────────────────

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) { MessageBox.Show("Please enter a valid URL."); return; }

            string chosenFormat = GetSelectedFormat();
            if (string.IsNullOrEmpty(chosenFormat))
            {
                LogTextBlock.Text = "";
                AppendLog("╔══════════════════════════════════════════════════╗");
                AppendLog("║  ⚠  Please select a download format first!      ║");
                AppendLog("║                                                  ║");
                AppendLog("║  VIDEO formats  →  MP4 · WebM · AVI · WMV       ║");
                AppendLog("║  AUDIO formats  →  MP3 · M4A · WAV              ║");
                AppendLog("╚══════════════════════════════════════════════════╝");
                StatusTextBlock.Text = "⚠ No format selected";
                return;
            }

            _savedUrl     = url;
            _savedQuality = (QualityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "best";
            _savedFormat  = chosenFormat;
            _overwrite    = false;

            LogTextBlock.Text    = "";
            StatusTextBlock.Text = "Checking...";
            SetProgressFillWidth(0, animate: false);

            // ── Step 1: check if file already exists ──
            AppendLog("[INFO] Checking if file was previously downloaded...");
            string existingFile = await Task.Run(() => RunCheckOnly(_savedUrl, _savedFormat));

            if (!string.IsNullOrEmpty(existingFile) && File.Exists(existingFile))
            {
                // Show the confirm panel — wait for user's choice
                _detectedFile = existingFile;
                ConfirmFileNameText.Text = $"📄  {Path.GetFileName(existingFile)}";
                AppendLog("");
                AppendLog($"[⚠ DUPLICATE] Found existing file:");
                AppendLog($"    {existingFile}");
                AppendLog("");
                AppendLog("[?] Choose an action using the panel above ↑");
                StatusTextBlock.Text = "⚠ File already exists — awaiting your choice";
                ApplyState(DownloadState.WaitingConfirm);
                return;
            }

            // No duplicate — proceed normally
            await BeginDownload();
        }

        // ── Confirm panel: YES ────────────────────────────────────────────

        private async void ConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            _overwrite = true;
            AppendLog("");
            AppendLog("[INFO] Re-download confirmed — existing file will be replaced.");
            AppendLog($"[INFO] New quality: {_savedQuality}  |  Format: {_savedFormat.ToUpper()}");
            AppendLog("");
            await BeginDownload();
        }

        // ── Confirm panel: NO ─────────────────────────────────────────────

        private void ConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            AppendLog("");
            AppendLog("[CANCELLED] Download cancelled — existing file kept.");
            StatusTextBlock.Text     = "Cancelled";
            _savedUrl                = "";
            _detectedFile            = "";
            ApplyState(DownloadState.Idle);
        }

        // ── Stop / Resume / Cancel ────────────────────────────────────────

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            KillActiveProcess();
            ApplyState(DownloadState.Paused);
            AppendLog("\n[PAUSED] Download paused — press ▶ RESUME to continue.");
            StatusTextBlock.Text = "Paused";
        }

        private async void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_savedUrl)) return;
            AppendLog("\n[RESUMING] Continuing download from where it stopped...");
            StatusTextBlock.Text = "Resuming...";
            await StartDownloadAsync();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            KillActiveProcess();
            LogTextBlock.Text        = "";
            StatusTextBlock.Text     = "Cancelled";
            SetProgressFillWidth(0, animate: false);
            PercentageTextBlock.Text = "0%";
            _savedUrl                = "";
            _savedFormat             = "";
            _overwrite               = false;
            ApplyState(DownloadState.Idle);
        }

        // ── Shared helpers ────────────────────────────────────────────────

        private async Task BeginDownload()
        {
            bool isAudio = AudioFormats.Contains(_savedFormat);
            if (isAudio)
            {
                AppendLog($"[INFO] Audio mode — video will be downloaded then converted to {_savedFormat.ToUpper()}.");
                AppendLog($"[INFO] Quality selector is disabled for audio (always uses best audio stream).");
                AppendLog("");
            }
            await StartDownloadAsync();
        }

        private async Task StartDownloadAsync()
        {
            ApplyState(DownloadState.Downloading);
            _cts = new CancellationTokenSource();

            try
            {
                await Task.Run(() => RunDownloader(_savedUrl, _savedQuality, _savedFormat, _overwrite, _cts.Token));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { MessageBox.Show("Error: " + ex.Message); ApplyState(DownloadState.Idle); }
            finally
            {
                _cts?.Dispose(); _cts = null;
                if (_state == DownloadState.Downloading) ApplyState(DownloadState.Idle);
            }
        }

        private void KillActiveProcess()
        {
            lock (_processLock)
            {
                try { if (_activeProcess != null && !_activeProcess.HasExited) _activeProcess.Kill(entireProcessTree: true); }
                catch { }
                finally { _cts?.Cancel(); }
            }
        }

        // ── Python helpers ────────────────────────────────────────────────

        private string GetPythonExecutable()
        {
            foreach (var name in new[] { "python", "python3", "py" })
            {
                try
                {
                    using var p = new Process();
                    p.StartInfo = new ProcessStartInfo(name, "--version")
                    {
                        UseShellExecute        = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow         = true
                    };
                    p.Start(); p.WaitForExit();
                    if (p.ExitCode == 0) return name;
                }
                catch { }
            }
            return "python";
        }

        /// <summary>Runs downloader.py --check-only and returns the found file path, or "".</summary>
        private string RunCheckOnly(string url, string format)
        {
            string python    = GetPythonExecutable();
            string outputDir = System.IO.Path.GetDirectoryName(
                                   System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";

            var si = new ProcessStartInfo
            {
                FileName                = python,
                UseShellExecute         = false,
                RedirectStandardOutput  = true,
                RedirectStandardError   = true,
                CreateNoWindow          = true,
                StandardOutputEncoding  = System.Text.Encoding.UTF8,
                StandardErrorEncoding   = System.Text.Encoding.UTF8,
            };
            si.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            si.EnvironmentVariables["PYTHONUTF8"]       = "1";
            si.ArgumentList.Add("downloader.py");
            si.ArgumentList.Add(url);
            si.ArgumentList.Add("--output");     si.ArgumentList.Add(outputDir);
            si.ArgumentList.Add("--format");     si.ArgumentList.Add(format);
            si.ArgumentList.Add("--check-only");

            try
            {
                using var p = Process.Start(si);
                if (p == null) return "";
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();

                // Echo debug lines to the log so we can verify behaviour
                foreach (var line in stdout.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("[FILECHECK_DEBUG]"))
                        Dispatcher.Invoke(() => AppendLog(trimmed));
                    else if (trimmed.StartsWith("[FILECHECK_ERROR]"))
                        Dispatcher.Invoke(() => AppendLog($"[WARN] Check failed: {trimmed}"));
                }

                // Return the found path
                foreach (var line in stdout.Split('\n'))
                {
                    if (line.TrimStart().StartsWith("[FILECHECK] "))
                        return line.Trim().Substring("[FILECHECK] ".Length).Trim();
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLog($"[WARN] RunCheckOnly exception: {ex.Message}"));
            }
            return "";
        }

        private void RunDownloader(string url, string quality, string format, bool overwrite, CancellationToken ct)
        {
            string python = GetPythonExecutable();
            var si = new ProcessStartInfo
            {
                FileName                = python,
                UseShellExecute         = false,
                RedirectStandardOutput  = true,
                RedirectStandardError   = true,
                CreateNoWindow          = true,
                StandardOutputEncoding  = System.Text.Encoding.UTF8,
                StandardErrorEncoding   = System.Text.Encoding.UTF8,
            };
            si.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            si.EnvironmentVariables["PYTHONUTF8"]       = "1";
            string dlOutputDir = System.IO.Path.GetDirectoryName(
                                      System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            si.ArgumentList.Add("downloader.py");
            si.ArgumentList.Add(url);
            si.ArgumentList.Add("--output");  si.ArgumentList.Add(dlOutputDir);
            si.ArgumentList.Add("--quality"); si.ArgumentList.Add(quality);
            si.ArgumentList.Add("--format");  si.ArgumentList.Add(format);
            if (overwrite) si.ArgumentList.Add("--overwrite");

            using var process = Process.Start(si);
            if (process == null) return;

            lock (_processLock) _activeProcess = process;

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
            {
                string status = data.Replace("[STATUS]", "").Trim();
                StatusTextBlock.Text = status;
                bool isConvert = status.Contains("Converting") || status.Contains("conversion") || status.Contains("processing");
                AppendLog(isConvert ? $"[⚙ PROCESSING] {status}" : data);
                return;
            }

            AppendLog(data);
        }

        private void AppendLog(string line)
        {
            LogTextBlock.Text += line + "\n";
            LogScrollViewer.ScrollToEnd();
        }
    }
}
