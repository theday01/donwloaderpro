using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinForms = System.Windows.Forms;

namespace VideoDownloaderUI
{
    internal enum DownloadState { Idle, Downloading, Paused, WaitingConfirm }

    public partial class MainWindow : Window
    {
        private static readonly string[] AudioFormats = { "mp3", "m4a", "wav" };

        private double        _currentProgress = 0;
        private DownloadState _state           = DownloadState.Idle;

        private string[] _savedUrls    = Array.Empty<string>();
        private string _savedQuality  = "best";
        private string _savedFormat   = "mp4";
        private bool   _overwrite     = false;
        private string _detectedFile  = "";

        private Process?                 _activeProcess = null;
        private CancellationTokenSource? _cts           = null;
        private readonly object          _processLock   = new object();

        // Network state
        private bool _networkWasLost     = false;
        private bool _autoResumePending  = false;

        // Settings
        private AppSettings _settings         = new AppSettings();
        private string      _selectedTheme    = "teal";
        private string      _selectedAppTheme = "dark";

        // About tab
        private bool _aboutLoaded = false;

        // ── Constructor ───────────────────────────────────────────────────

        public MainWindow()
        {
            InitializeComponent();
            _settings         = SettingsManager.Load();
            _selectedAppTheme = _settings.AppTheme;

            ApplyLanguage(_settings.Language);
            ApplySettingsToUI();
            ApplyThemeColors(_settings.AccentTheme);
            ApplyAppTheme(_settings.AppTheme);
            UpdateDownloadPathLabel();

            // Subscribe to network availability changes
            NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

            ShowWelcomeMessage();
        }

        protected override void OnClosed(EventArgs e)
        {
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            base.OnClosed(e);
        }

        // ── Network availability handler ──────────────────────────────────

        private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (!e.IsAvailable && _state == DownloadState.Downloading)
                {
                    _networkWasLost = true;
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                    StatusTextBlock.Text = SafeResource("StatusNetworkLost", "Connection lost — waiting…");
                }
                else if (e.IsAvailable)
                {
                    if (_networkWasLost)
                    {
                        _networkWasLost = false;
                        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                        StatusTextBlock.Text = SafeResource("StatusNetworkRestored", "Connection restored — resuming…");
                    }
                    
                    if (_autoResumePending)
                    {
                        _autoResumePending = false;
                        AppendLog("[✔] " + SafeResource("LogAutoResuming", "Network restored — automatically resuming download…"));
                        ResumeButton_Click(null, null);
                    }
                }
            });
        }

        /// <summary>Returns the resource string, or <paramref name="fallback"/> if the key is missing.</summary>
        private string SafeResource(string key, string fallback)
        {
            try { return FindResource(key)?.ToString() ?? fallback; }
            catch { return fallback; }
        }

        // ── Welcome / helpers ─────────────────────────────────────────────

        private void ShowWelcomeMessage()
        {
            LogTextBlock.Text = "";
            AppendLog("╔" + new string('═', 50) + "╗");
            AppendLog("║" + PadCenter(FindResource("WelcomeTitle").ToString()!, 50) + "║");
            AppendLog("║" + PadCenter(FindResource("WelcomeDev").ToString()!, 50) + "║");
            AppendLog("║" + PadCenter("", 50) + "║");
            AppendLog("║" + PadCenter(FindResource("WelcomeReady").ToString()!, 50) + "║");
            AppendLog("╚" + new string('═', 50) + "╝\n");
        }

        private static string PadCenter(string text, int length)
        {
            if (text.Length >= length) return text.Substring(0, length);
            int leftPad  = (length - text.Length) / 2;
            int rightPad = length - text.Length - leftPad;
            return new string(' ', leftPad) + text + new string(' ', rightPad);
        }

        private void ApplyLanguage(string lang)
        {
            try
            {
                var dict = new ResourceDictionary
                {
                    Source = new Uri($"Resources/Strings.{lang}.xaml", UriKind.Relative)
                };

                var oldDict = Application.Current.Resources.MergedDictionaries
                    .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Strings."));

                if (oldDict != null)
                    Application.Current.Resources.MergedDictionaries.Remove(oldDict);

                Application.Current.Resources.MergedDictionaries.Add(dict);
                this.FlowDirection = (lang == "ar") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

                if (HistoryContentGrid?.Visibility == Visibility.Visible)
                    LoadHistoryData();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error applying language: " + ex.Message);
                if (lang != "en")
                {
                    try
                    {
                        var fallback = new ResourceDictionary
                        {
                            Source = new Uri("Resources/Strings.en.xaml", UriKind.Relative)
                        };
                        Application.Current.Resources.MergedDictionaries.Add(fallback);
                        this.FlowDirection = FlowDirection.LeftToRight;
                    }
                    catch { }
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  TAB NAVIGATION
        // ════════════════════════════════════════════════════════════════

        private void Tab_Downloader_Click(object sender, RoutedEventArgs e)
        {
            MainContentGrid.Visibility    = Visibility.Visible;
            HistoryContentGrid.Visibility = Visibility.Collapsed;
            AboutContentGrid.Visibility   = Visibility.Collapsed;

            TabDownloaderBtn.Tag = "active";
            TabHistoryBtn.Tag    = "inactive";
            TabAboutBtn.Tag      = "inactive";
            RefreshTabButtonState(TabDownloaderBtn);
            RefreshTabButtonState(TabHistoryBtn);
            RefreshTabButtonState(TabAboutBtn);
            SyncTabButtonForegrounds();
        }

        private void Tab_History_Click(object sender, RoutedEventArgs e)
        {
            MainContentGrid.Visibility    = Visibility.Collapsed;
            HistoryContentGrid.Visibility = Visibility.Visible;
            AboutContentGrid.Visibility   = Visibility.Collapsed;

            TabDownloaderBtn.Tag = "inactive";
            TabHistoryBtn.Tag    = "active";
            TabAboutBtn.Tag      = "inactive";
            RefreshTabButtonState(TabDownloaderBtn);
            RefreshTabButtonState(TabHistoryBtn);
            RefreshTabButtonState(TabAboutBtn);
            SyncTabButtonForegrounds();

            LoadHistoryData();
        }

        private void Tab_About_Click(object sender, RoutedEventArgs e)
        {
            MainContentGrid.Visibility    = Visibility.Collapsed;
            HistoryContentGrid.Visibility = Visibility.Collapsed;
            AboutContentGrid.Visibility   = Visibility.Visible;

            TabDownloaderBtn.Tag = "inactive";
            TabHistoryBtn.Tag    = "inactive";
            TabAboutBtn.Tag      = "active";
            RefreshTabButtonState(TabDownloaderBtn);
            RefreshTabButtonState(TabHistoryBtn);
            RefreshTabButtonState(TabAboutBtn);
            SyncTabButtonForegrounds();

            if (!_aboutLoaded)
            {
                _aboutLoaded = true;
                _ = LoadSystemInfoAsync();
            }
        }

        private static void RefreshTabButtonState(Button btn)
        {
            btn.InvalidateVisual();
            btn.UpdateLayout();
        }

        // ════════════════════════════════════════════════════════════════
        //  HYPERLINK
        // ════════════════════════════════════════════════════════════════

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        // ════════════════════════════════════════════════════════════════
        //  SYSTEM INFO
        // ════════════════════════════════════════════════════════════════

        private async Task LoadSystemInfoAsync()
        {
            SysOsText.Text     = GetFriendlyOsName();
            SysDotnetText.Text = $".NET {Environment.Version}";
            SysArchText.Text   = RuntimeInformation.OSArchitecture.ToString();

            string coreLabel = (Environment.ProcessorCount > 1)
                ? FindResource("LabelLogicalCores").ToString()!
                : FindResource("LabelLogicalCore").ToString()!;
            SysCpuText.Text = $"{Environment.ProcessorCount} {coreLabel}";

            SysAppDataText.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EagleVStream");
            SysExePathText.Text = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "—";

            await Task.CompletedTask;
        }

        private (bool found, string version, string desc) ProbePython()
        {
            string py  = GetPythonExecutable();
            string? raw = RunSysCommand(py, "--version", timeoutMs: 5000);
            if (raw == null) return (false, FindResource("StatusNotFoundDep").ToString()!.Replace("●", "").Trim(),
                                           string.Format(FindResource("LabelInstallFrom").ToString()!, "python.org"));
            string ver = raw.Trim();
            if (!ver.StartsWith("Python", StringComparison.OrdinalIgnoreCase))
                ver = "Python " + ver;
            return (true, ver, string.Format(FindResource("LabelExecutable").ToString()!, py));
        }

        private (bool found, string version, string desc) ProbeYtDlp()
        {
            string? raw = RunSysCommand("yt-dlp", "--version", timeoutMs: 8000);
            if (raw == null) return (false, FindResource("StatusNotFoundDep").ToString()!.Replace("●", "").Trim(),
                                           string.Format(FindResource("LabelPipInstall").ToString()!, "yt-dlp"));
            string ver = raw.Trim().Split('\n')[0];
            return (true, $"yt-dlp  {ver}", string.Format(FindResource("LabelPipUpdate").ToString()!, "yt-dlp"));
        }

        private (bool found, string version, string desc) ProbeFfmpeg()
        {
            string? raw = RunSysCommand("ffmpeg", "-version", timeoutMs: 6000);
            if (raw == null)
            {
                string local = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".",
                    "ffmpeg.exe");
                if (File.Exists(local))
                    return (true, $"ffmpeg  ({FindResource("LabelLocalBundle")})",
                                   string.Format(FindResource("LabelFoundAt").ToString()!, local));
                return (false, FindResource("StatusNotFoundDep").ToString()!.Replace("●", "").Trim(),
                               FindResource("LabelFfmpegDesc").ToString()!);
            }
            string firstLine = raw.Trim().Split('\n')[0];
            string shortVer  = firstLine.Replace("ffmpeg version ", "").Split(' ')[0];
            return (true, $"ffmpeg  {shortVer}", FindResource("LabelFoundInPath").ToString()!);
        }

        private static string? RunSysCommand(string exe, string args, int timeoutMs = 6000)
        {
            try
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo(exe, args)
                    {
                        UseShellExecute        = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true,
                        CreateNoWindow         = true
                    }
                };
                p.Start();
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                bool exited   = p.WaitForExit(timeoutMs);
                if (!exited) { try { p.Kill(); } catch { } return null; }
                if (p.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout)) stdout = stderr;
                return string.IsNullOrWhiteSpace(stdout) ? null : stdout;
            }
            catch { return null; }
        }

        private void SetDepChecking(TextBlock statusTb, Border badge, TextBlock versionTb)
        {
            versionTb.Text       = FindResource("StatusDetecting").ToString();
            versionTb.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x44));
            statusTb.Text        = FindResource("StatusCheckingDep").ToString();
            statusTb.Foreground  = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x55));
            badge.Background     = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x18));
        }

        private void UpdateDepRow(TextBlock versionTb, TextBlock statusTb, Border badge,
                                  bool found, string version, string desc)
        {
            var okGreen = Color.FromRgb(0x4C, 0xAF, 0x50);
            var errRed  = Color.FromRgb(0xF4, 0x43, 0x36);
            var okBg    = Color.FromRgb(0x0B, 0x1C, 0x0B);
            var errBg   = Color.FromRgb(0x1C, 0x08, 0x08);

            versionTb.Text       = version;
            versionTb.Foreground = new SolidColorBrush(found
                ? Color.FromRgb(0x66, 0x88, 0x66) : Color.FromRgb(0x66, 0x33, 0x33));

            statusTb.Text       = found ? FindResource("StatusInstalled").ToString() : FindResource("StatusNotFoundDep").ToString();
            statusTb.Foreground = new SolidColorBrush(found ? okGreen : errRed);
            badge.Background    = new SolidColorBrush(found ? okBg    : errBg);

            if (versionTb.Parent is StackPanel sp && sp.Children.Count > 2 &&
                sp.Children[2] is TextBlock descTb)
            {
                descTb.Text       = desc;
                descTb.Foreground = new SolidColorBrush(found
                    ? Color.FromRgb(0x33, 0x55, 0x33) : Color.FromRgb(0x66, 0x33, 0x22));
            }
        }

        private static string GetFriendlyOsName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var v = Environment.OSVersion.Version;
                string name = v.Build >= 22000 ? "Windows 11" : "Windows 10";
                return $"{name}  (Build {v.Build})";
            }
            return RuntimeInformation.OSDescription;
        }

        private void RefreshSysInfo_Click(object sender, RoutedEventArgs e)
        {
            _aboutLoaded = false;
            _aboutLoaded = true;
            _ = LoadSystemInfoAsync();
        }

        // ════════════════════════════════════════════════════════════════
        //  SETTINGS OPEN / CLOSE
        // ════════════════════════════════════════════════════════════════

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            LoadSettingsIntoPanel();
            SettingsOverlay.Visibility = Visibility.Visible;
            AnimateSettingsIn();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => ApplySettingsPanelTheme(_settings.AppTheme == "light")));
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e) => CloseSettingsPanel();
        private void SettingsBackdrop_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => CloseSettingsPanel();

        private void CloseSettingsPanel()
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }

        private void AnimateSettingsIn()
        {
            SettingsCard.RenderTransform = new TranslateTransform(420, 0);
            var anim = new DoubleAnimation(420, 0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            SettingsCard.RenderTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        // ════════════════════════════════════════════════════════════════
        //  SETTINGS: LOAD / SAVE / RESET
        // ════════════════════════════════════════════════════════════════

        private void LoadSettingsIntoPanel()
        {
            DownloadPathBox.Text            = _settings.DownloadPath;
            AutoOpenFolderCheck.IsChecked   = _settings.AutoOpenFolder;
            ShowNotificationCheck.IsChecked = _settings.ShowCompletionNotify;
            ClearLogCheck.IsChecked         = _settings.ClearLogEachDownload;
            SkipDuplicateCheck.IsChecked    = _settings.SkipDuplicateCheck;

            _selectedTheme    = _settings.AccentTheme;
            _selectedAppTheme = _settings.AppTheme;
            UpdateThemeSwatchBorders(_selectedTheme);
            UpdateAppThemeCards(_selectedAppTheme);

            SelectComboByTag(DefaultQualityBox, _settings.DefaultQuality);
            SelectComboByTag(DefaultFormatBox,  _settings.DefaultFormat);
            SelectComboByTag(LanguageComboBox,  _settings.Language);
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            string newLang    = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "en";
            bool langChanged  = newLang != _settings.Language;
            bool themeChanged = _selectedAppTheme != _settings.AppTheme;

            _settings.DownloadPath         = DownloadPathBox.Text.Trim();
            _settings.AutoOpenFolder       = AutoOpenFolderCheck.IsChecked == true;
            _settings.ShowCompletionNotify = ShowNotificationCheck.IsChecked == true;
            _settings.ClearLogEachDownload = ClearLogCheck.IsChecked == true;
            _settings.SkipDuplicateCheck   = SkipDuplicateCheck.IsChecked == true;
            _settings.AccentTheme          = _selectedTheme;
            _settings.AppTheme             = _selectedAppTheme;
            _settings.Language             = newLang;

            _settings.DefaultQuality = (DefaultQualityBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "best";
            _settings.DefaultFormat  = (DefaultFormatBox.SelectedItem  as ComboBoxItem)?.Tag?.ToString() ?? "mp4";

            SettingsManager.Save(_settings);

            if (langChanged || themeChanged)
            {
                System.Windows.MessageBox.Show(FindResource("MsgRestartRequired").ToString()!,
                    FindResource("SettingsTitle").ToString()!.Replace("⚙", "").Trim());
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath)) Process.Start(exePath);
                Application.Current.Shutdown();
                return;
            }

            ApplyLanguage(_settings.Language);
            ApplyThemeColors(_selectedTheme);
            ApplyAppTheme(_selectedAppTheme);
            UpdateDownloadPathLabel();
            ApplySettingsToUI();

            CloseSettingsPanel();
            AppendLog(FindResource("LogSettingsSaved").ToString()!);
        }

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            _settings         = new AppSettings();
            _selectedTheme    = "teal";
            _selectedAppTheme = "dark";
            LoadSettingsIntoPanel();
            AppendLog(FindResource("LogSettingsReset").ToString()!);
        }

        private void BrowsePath_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description            = FindResource("LabelSelectFolder").ToString()!,
                UseDescriptionForTitle = true,
                ShowNewFolderButton    = true,
                SelectedPath           = string.IsNullOrWhiteSpace(DownloadPathBox.Text)
                                            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                                            : DownloadPathBox.Text
            };
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                DownloadPathBox.Text = dlg.SelectedPath;
        }

        // ════════════════════════════════════════════════════════════════
        //  THEME
        // ════════════════════════════════════════════════════════════════

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                _selectedTheme = tag;
                UpdateThemeSwatchBorders(tag);
            }
        }

        private void UpdateThemeSwatchBorders(string selected)
        {
            var swatches = new[] { ThemeTeal, ThemePurple, ThemePink, ThemeBlue, ThemeOrange, ThemeGreen };
            foreach (var b in swatches)
                b.BorderBrush = (b.Tag?.ToString() == selected) ? Brushes.White : Brushes.Transparent;
        }

        private void DarkThemeCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _selectedAppTheme = "dark";
            UpdateAppThemeCards("dark");
        }

        private void LightThemeCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _selectedAppTheme = "light";
            UpdateAppThemeCards("light");
        }

        private void UpdateAppThemeCards(string selected)
        {
            var accent = (SolidColorBrush)FindResource("SecondaryColor");
            if (DarkThemeCard != null)
            {
                DarkThemeCard.BorderBrush = selected == "dark"  ? accent : Brushes.Transparent;
                DarkThemeCard.Background  = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x2E));
            }
            if (LightThemeCard != null)
            {
                LightThemeCard.BorderBrush = selected == "light" ? accent : Brushes.Transparent;
                LightThemeCard.Background  = new SolidColorBrush(
                    selected == "light" ? Color.FromRgb(0xE0, 0xE3, 0xF0) : Color.FromRgb(0x2C, 0x2E, 0x3E));
            }
        }

        private void ApplyAppTheme(string appTheme)
        {
            bool isLight = appTheme == "light";

            var windowBg   = isLight ? Color.FromRgb(0xED, 0xEF, 0xFA) : Color.FromRgb(0x0F, 0x11, 0x1A);
            var cardBg     = isLight ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1E, 0x20, 0x2E);
            var inputBg    = isLight ? Color.FromRgb(0xF3, 0xF4, 0xFD) : Color.FromRgb(0x2C, 0x2E, 0x3E);
            var logBg      = isLight ? Color.FromRgb(0xF8, 0xF9, 0xFF) : Color.FromRgb(0x14, 0x16, 0x22);
            var logBorder  = isLight ? Color.FromRgb(0xD8, 0xDB, 0xF0) : Color.FromRgb(0x2C, 0x2E, 0x3E);
            var cardBorder = isLight ? Color.FromRgb(0xE0, 0xE3, 0xF5) : Color.FromRgb(0x1E, 0x20, 0x2E);
            var headingFg  = isLight ? Color.FromRgb(0x4A, 0x1A, 0x8C) : Color.FromRgb(0xBB, 0x86, 0xFC);
            var subHeadFg  = isLight ? Color.FromRgb(0x55, 0x55, 0x88) : Color.FromRgb(0x88, 0x88, 0x88);
            var inputFg    = isLight ? Color.FromRgb(0x1A, 0x1A, 0x2E) : Colors.White;
            var labelFg    = isLight ? Color.FromRgb(0x66, 0x66, 0x99) : Color.FromRgb(0x88, 0x88, 0x88);
            var logFg      = isLight ? Color.FromRgb(0x0A, 0x6B, 0x5A) : Color.FromRgb(0x03, 0xDA, 0xC6);
            var percentFg  = isLight ? Color.FromRgb(0x77, 0x22, 0xCC) : Color.FromRgb(0xBB, 0x86, 0xFC);
            var pathFg     = isLight ? Color.FromRgb(0x99, 0x99, 0xBB) : Color.FromRgb(0x44, 0x44, 0x55);
            var tabBarBg   = isLight ? Color.FromRgb(0xE0, 0xE3, 0xF5) : Color.FromRgb(0x08, 0x09, 0x0F);
            var tabBarBord = isLight ? Color.FromRgb(0xCC, 0xCE, 0xE8) : Color.FromRgb(0x15, 0x17, 0x2A);
            var dotReady   = isLight ? Color.FromRgb(0x03, 0xB0, 0xA0) : Color.FromRgb(0x03, 0xDA, 0xC6);

            Background = new SolidColorBrush(windowBg);
            Resources["CardColor"] = new SolidColorBrush(cardBg);

            if (FindName("TabBarBorder") is Border tbBar)
            {
                tbBar.Background  = new SolidColorBrush(tabBarBg);
                tbBar.BorderBrush = new SolidColorBrush(tabBarBord);
            }

            if (FindName("AppTitleText")    is TextBlock titleTb) titleTb.Foreground = new SolidColorBrush(headingFg);
            if (FindName("AppSubtitleText") is TextBlock subTb)   subTb.Foreground   = new SolidColorBrush(subHeadFg);

            if (FindName("InputCard") is Border inputCard)
            {
                inputCard.Background      = new SolidColorBrush(cardBg);
                inputCard.BorderBrush     = new SolidColorBrush(cardBorder);
                inputCard.BorderThickness = new Thickness(isLight ? 1 : 0);
                inputCard.Effect = isLight
                    ? new System.Windows.Media.Effects.DropShadowEffect
                      { BlurRadius = 18, ShadowDepth = 2, Color = Color.FromRgb(0xC0, 0xC4, 0xE8), Opacity = 0.45 }
                    : null;
            }
            if (UrlTextBox != null)
            {
                UrlTextBox.Background = new SolidColorBrush(inputBg);
                UrlTextBox.Foreground = new SolidColorBrush(inputFg);
                UrlTextBox.CaretBrush = new SolidColorBrush(inputFg);
            }

            if (FindName("QualityLabel") is TextBlock ql) ql.Foreground = new SolidColorBrush(labelFg);
            if (FindName("FormatLabel")  is TextBlock fl) fl.Foreground = new SolidColorBrush(labelFg);

            if (FindName("ProgressLogCard") is Border plCard)
            {
                plCard.Background      = new SolidColorBrush(cardBg);
                plCard.BorderBrush     = new SolidColorBrush(cardBorder);
                plCard.BorderThickness = new Thickness(isLight ? 1 : 0);
                plCard.Effect = isLight
                    ? new System.Windows.Media.Effects.DropShadowEffect
                      { BlurRadius = 18, ShadowDepth = 2, Color = Color.FromRgb(0xC0, 0xC4, 0xE8), Opacity = 0.45 }
                    : null;
            }
            if (FindName("ProgressLabel") is TextBlock progressLabelTb)
                progressLabelTb.Foreground = new SolidColorBrush(labelFg);
            if (PercentageTextBlock != null)
                PercentageTextBlock.Foreground = new SolidColorBrush(percentFg);

            if (FindName("ProgressTrackBg") is Border trackBg)
                trackBg.Background = new SolidColorBrush(
                    isLight ? Color.FromRgb(0xDD, 0xDF, 0xF5) : Color.FromRgb(0x1C, 0x1D, 0x2B));

            if (HistoryTitleText    != null) HistoryTitleText.Foreground    = new SolidColorBrush(headingFg);
            if (HistorySubtitleText != null) HistorySubtitleText.Foreground = new SolidColorBrush(subHeadFg);
            if (HistoryCard != null)
            {
                HistoryCard.Background      = new SolidColorBrush(cardBg);
                HistoryCard.BorderBrush     = new SolidColorBrush(cardBorder);
                HistoryCard.BorderThickness = new Thickness(isLight ? 1 : 0);
            }
            if (HistoryListView != null)
                HistoryListView.Foreground = isLight ? new SolidColorBrush(inputFg) : Brushes.White;

            if (LogScrollViewer != null)
            {
                LogScrollViewer.Background      = new SolidColorBrush(logBg);
                LogScrollViewer.BorderBrush     = new SolidColorBrush(logBorder);
                LogScrollViewer.BorderThickness = new Thickness(1);
            }
            if (LogTextBlock   != null) LogTextBlock.Foreground   = new SolidColorBrush(logFg);
            if (OpenFolderBtn  != null) OpenFolderBtn.Foreground  = new SolidColorBrush(isLight ? Color.FromRgb(0x44,0x44,0x88) : Color.FromRgb(0x88,0x88,0x88));
            if (ClearLogButton != null) ClearLogButton.Foreground = new SolidColorBrush(isLight ? Color.FromRgb(0x99,0x44,0x44) : Color.FromRgb(0x88,0x88,0x88));

            ApplyAboutTabTheme(isLight, cardBg);

            if (StatusTextBlock   != null) StatusTextBlock.Foreground   = new SolidColorBrush(labelFg);
            if (DownloadPathLabel != null) DownloadPathLabel.Foreground = new SolidColorBrush(pathFg);
            if (StatusDot != null && _state == DownloadState.Idle)
                StatusDot.Fill = new SolidColorBrush(dotReady);

            ApplySettingsPanelTheme(isLight);

            Resources["TabInactiveFg"] = new SolidColorBrush(
                isLight ? Color.FromRgb(0x55, 0x55, 0x88) : Color.FromRgb(0xAA, 0xAA, 0xAA));
            SyncTabButtonForegrounds();

            if (FindName("SettingsButton") is Button gearBtn)
                gearBtn.Foreground = new SolidColorBrush(
                    isLight ? Color.FromRgb(0x55, 0x55, 0x88) : Color.FromRgb(0x88, 0x88, 0x88));

            UpdateChangelogIconWrappers(isLight,
                isLight ? Color.FromRgb(0xE8, 0xEB, 0xFF) : Color.FromRgb(0x1A, 0x0D, 0x2E));
        }

        private void ApplyAboutTabTheme(bool isLight, Color cardBg)
        {
            var cardBrush = new SolidColorBrush(cardBg);
            var descFg    = isLight ? Color.FromRgb(0x55, 0x55, 0x88) : Color.FromRgb(0x66, 0x66, 0x66);
            var descBrush = new SolidColorBrush(descFg);

            if (AboutDesc1 != null) AboutDesc1.Foreground = descBrush;
            if (AboutDesc2 != null) AboutDesc2.Foreground = descBrush;
            if (AboutDesc3 != null) AboutDesc3.Foreground = descBrush;

            if (FindName("AboutIdentityCard") is Border ic)
            {
                if (isLight) ic.Background = cardBrush;
                else ic.Background = new LinearGradientBrush(new GradientStopCollection {
                    new GradientStop(Color.FromRgb(0x12,0x14,0x2A), 0.0),
                    new GradientStop(Color.FromRgb(0x1A,0x0E,0x2E), 0.5),
                    new GradientStop(Color.FromRgb(0x0A,0x1F,0x22), 1.0)
                }, new Point(0,0), new Point(1,1));
            }

            if (FindName("AboutLogoCard")      is Border lc)  lc.Background  = cardBrush;
            if (FindName("AboutSysCard")       is Border sc)  sc.Background  = cardBrush;
            if (FindName("AboutChangelogCard") is Border clc) clc.Background = cardBrush;

            if (FindName("AboutContactCard") is Border contactCard)
            {
                contactCard.Background = cardBrush;
                var header = VisualTreeHelper.GetChild(VisualTreeHelper.GetChild(contactCard, 0), 0) as Border;
                if (header != null)
                {
                    if (isLight) header.Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF2, 0xFB));
                    else header.Background = new LinearGradientBrush(new GradientStopCollection {
                        new GradientStop(Color.FromRgb(0x12,0x14,0x2A), 0),
                        new GradientStop(Color.FromRgb(0x0A,0x1F,0x22), 1)
                    }, new Point(0,0), new Point(1,0));
                }
            }
        }

        private void SyncTabButtonForegrounds()
        {
            bool isLight       = _settings.AppTheme == "light";
            var  activeBrush   = new SolidColorBrush(isLight ? Color.FromRgb(0x1A,0x1A,0x2E) : Colors.White);
            var  inactiveBrush = new SolidColorBrush(isLight ? Color.FromRgb(0x55,0x55,0x88) : Color.FromRgb(0xAA,0xAA,0xAA));

            if (TabDownloaderBtn != null)
                TabDownloaderBtn.Foreground = TabDownloaderBtn.Tag?.ToString() == "active" ? activeBrush : inactiveBrush;
            if (TabHistoryBtn != null)
                TabHistoryBtn.Foreground = TabHistoryBtn.Tag?.ToString() == "active" ? activeBrush : inactiveBrush;
            if (TabAboutBtn != null)
                TabAboutBtn.Foreground = TabAboutBtn.Tag?.ToString() == "active" ? activeBrush : inactiveBrush;
        }

        private void UpdateChangelogIconWrappers(bool isLight, Color wrapperBg)
        {
            if (!isLight) return;
            if (FindName("AboutChangelogCard") is not Border root) return;
            var brush = new SolidColorBrush(wrapperBg);
            void Walk(DependencyObject parent)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is Border b && b.Width == 32 && b.Height == 32) b.Background = brush;
                    Walk(child);
                }
            }
            Walk(root);
        }

        private void ApplySettingsPanelTheme(bool isLight)
        {
            var sBg          = isLight ? Color.FromRgb(0xFF,0xFF,0xFF) : Color.FromRgb(0x13,0x15,0x2A);
            var sHeaderBg    = isLight ? Color.FromRgb(0xF0,0xF2,0xFB) : Color.FromRgb(0x0F,0x11,0x1A);
            var sFooterBg    = isLight ? Color.FromRgb(0xF0,0xF2,0xFB) : Color.FromRgb(0x0F,0x11,0x1A);
            var sScrollBg    = isLight ? Color.FromRgb(0xFF,0xFF,0xFF) : Color.FromRgb(0x13,0x15,0x2A);
            var sBorderColor = isLight ? Color.FromRgb(0xE0,0xE3,0xF5) : Color.FromRgb(0x2A,0x2D,0x45);
            var sBehaviorBg  = isLight ? Color.FromRgb(0xF3,0xF4,0xFD) : Color.FromRgb(0x1A,0x1C,0x2E);
            var sInputBg     = isLight ? Color.FromRgb(0xF3,0xF4,0xFD) : Color.FromRgb(0x2C,0x2E,0x3E);
            var sBorderInput = isLight ? Color.FromRgb(0xD8,0xDB,0xF0) : Color.FromRgb(0x2C,0x2E,0x3E);
            var sTitleFg     = isLight ? Color.FromRgb(0x22,0x10,0x55) : Colors.White;
            var sSubtitleFg  = isLight ? Color.FromRgb(0x66,0x66,0x99) : Color.FromRgb(0x88,0x88,0x88);
            var sInputFg     = isLight ? Color.FromRgb(0x1A,0x1A,0x2E) : Colors.White;
            var sHintFg      = isLight ? Color.FromRgb(0x99,0x99,0xBB) : Color.FromRgb(0x55,0x55,0x55);
            var sResetFg     = isLight ? Color.FromRgb(0x88,0x88,0xAA) : Color.FromRgb(0x66,0x66,0x66);

            if (SettingsCard != null)
            {
                SettingsCard.Background  = new SolidColorBrush(sBg);
                SettingsCard.BorderBrush = new SolidColorBrush(sBorderColor);
                SettingsCard.Effect = isLight
                    ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=40, ShadowDepth=0, Color=Color.FromRgb(0xB0,0xB8,0xE0), Opacity=0.5 }
                    : new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=30, ShadowDepth=0, Color=Colors.Black,                  Opacity=0.6 };
            }
            if (FindName("SettingsHeaderBorder") is Border hdr) hdr.Background  = new SolidColorBrush(sHeaderBg);
            if (FindName("SettingsTitleText")    is TextBlock t) t.Foreground    = new SolidColorBrush(sTitleFg);
            if (FindName("SettingsSubtitleText") is TextBlock s) s.Foreground    = new SolidColorBrush(sSubtitleFg);
            if (FindName("SettingsScrollViewer") is ScrollViewer sv) sv.Background = new SolidColorBrush(sScrollBg);

            if (DownloadPathBox != null)
            {
                DownloadPathBox.Background      = new SolidColorBrush(sInputBg);
                DownloadPathBox.Foreground      = new SolidColorBrush(sInputFg);
                DownloadPathBox.CaretBrush      = new SolidColorBrush(sInputFg);
                DownloadPathBox.BorderBrush     = new SolidColorBrush(sBorderInput);
                DownloadPathBox.BorderThickness = new Thickness(isLight ? 1 : 0);
            }
            if (DownloadPathHint != null) DownloadPathHint.Foreground = new SolidColorBrush(sHintFg);
            if (ThemeRestartHint != null) ThemeRestartHint.Foreground = new SolidColorBrush(sHintFg);
            if (FindName("BehaviorCard") is Border bc) bc.Background  = new SolidColorBrush(sBehaviorBg);

            if (FindName("BehaviorCard") is Border bcWalk)
                SetChildCheckBoxForeground(bcWalk, isLight
                    ? new SolidColorBrush(Color.FromRgb(0x22,0x22,0x44))
                    : new SolidColorBrush(Color.FromRgb(0xCC,0xCC,0xCC)));

            if (DarkThemeCard != null)
            {
                DarkThemeCard.Background = new SolidColorBrush(
                    isLight ? Color.FromRgb(0xE8,0xEA,0xF8) : Color.FromRgb(0x1A,0x1C,0x2E));
                SetChildTextForeground(DarkThemeCard, isLight
                    ? new SolidColorBrush(Color.FromRgb(0x1A,0x1A,0x2E)) : new SolidColorBrush(Colors.White), skipFirst: true);
                SetChildTextForeground(DarkThemeCard, isLight
                    ? new SolidColorBrush(Color.FromRgb(0x88,0x88,0xAA)) : new SolidColorBrush(Color.FromRgb(0x88,0x88,0x88)), skipFirst: false, labelOnly: true);
            }
            if (LightThemeCard != null)
            {
                LightThemeCard.Background = new SolidColorBrush(
                    isLight ? Color.FromRgb(0xFF,0xFF,0xFF) : Color.FromRgb(0x2C,0x2E,0x3E));
                SetChildTextForeground(LightThemeCard, isLight
                    ? new SolidColorBrush(Color.FromRgb(0x1A,0x1A,0x2E)) : new SolidColorBrush(Colors.White), skipFirst: true);
                SetChildTextForeground(LightThemeCard, isLight
                    ? new SolidColorBrush(Color.FromRgb(0x88,0x88,0xAA)) : new SolidColorBrush(Color.FromRgb(0x88,0x88,0x88)), skipFirst: false, labelOnly: true);
            }
            UpdateAppThemeCards(_selectedAppTheme);

            if (FindName("SettingsFooterBorder") is Border ftr) ftr.Background = new SolidColorBrush(sFooterBg);
            if (FindName("ResetButton")          is Button rb)  rb.Foreground  = new SolidColorBrush(sResetFg);
        }

        private static void SetChildCheckBoxForeground(DependencyObject parent, Brush brush)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is CheckBox cb) cb.Foreground = brush;
                SetChildCheckBoxForeground(child, brush);
            }
        }

        private static void SetChildTextForeground(DependencyObject parent, Brush brush,
            bool skipFirst = false, bool labelOnly = false)
        {
            bool firstSeen = false;
            SetChildTextForegroundInner(parent, brush, skipFirst, labelOnly, ref firstSeen);
        }

        private static void SetChildTextForegroundInner(DependencyObject parent, Brush brush,
            bool skipFirst, bool labelOnly, ref bool firstSeen)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock tb)
                {
                    if (skipFirst && !firstSeen) { firstSeen = true; continue; }
                    if (!labelOnly || (tb.FontSize <= 12)) tb.Foreground = brush;
                }
                SetChildTextForegroundInner(child, brush, skipFirst, labelOnly, ref firstSeen);
            }
        }

        private void ApplyThemeColors(string theme)
        {
            var (primary, secondary) = SettingsManager.GetThemeColors(theme);
            Resources["SecondaryColor"] = new SolidColorBrush(primary);

            if (ProgressFill != null && !AudioFormats.Contains(_savedFormat))
            {
                ProgressFill.Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0xFF, (byte)(primary.R/2), (byte)(primary.G/2), (byte)(primary.B/2)), 0.0),
                    new GradientStop(primary,   0.5),
                    new GradientStop(secondary, 1.0)
                }, new Point(0,0), new Point(1,0));
            }
        }

        private void ApplySettingsToUI()
        {
            if (!string.IsNullOrEmpty(_settings.DefaultQuality))
                SelectComboByTag(QualityComboBox, _settings.DefaultQuality);
            if (!string.IsNullOrEmpty(_settings.DefaultFormat))
                SelectComboByTag(FormatComboBox, _settings.DefaultFormat);
        }

        private void UpdateDownloadPathLabel()
        {
            if (DownloadPathLabel == null) return;
            string dir = SettingsManager.GetDownloadDirectory(_settings);
            DownloadPathLabel.Text    = dir.Length > 55 ? "📁 …" + dir[^52..] : "📁 " + dir;
            DownloadPathLabel.ToolTip = dir;
        }

        // ════════════════════════════════════════════════════════════════
        //  FORMAT COMBO
        // ════════════════════════════════════════════════════════════════

        private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FormatComboBox == null) return;
            string fmt   = GetSelectedFormat();
            bool isAudio = AudioFormats.Contains(fmt);

            if (AudioBadge      != null) AudioBadge.Visibility      = isAudio ? Visibility.Visible : Visibility.Collapsed;
            if (QualityComboBox != null) { QualityComboBox.IsEnabled = !isAudio; QualityComboBox.Opacity = isAudio ? 0.4 : 1.0; }
            if (DownloadButton  != null && _state == DownloadState.Idle)
                DownloadButton.Content = isAudio ? FindResource("ExtractAudioButtonText") : FindResource("DownloadButtonText");

            if (ProgressFill != null)
                ProgressFill.Background = isAudio
                    ? new LinearGradientBrush(new GradientStopCollection {
                          new GradientStop(Color.FromRgb(0xAA,0x00,0x55), 0.0),
                          new GradientStop(Color.FromRgb(0xFF,0x6B,0x6B), 0.5),
                          new GradientStop(Color.FromRgb(0xCC,0x00,0x44), 1.0) },
                          new Point(0,0), new Point(1,0))
                    : BuildThemeGradient(_settings.AccentTheme);
        }

        private LinearGradientBrush BuildThemeGradient(string theme)
        {
            var (primary, secondary) = SettingsManager.GetThemeColors(theme);
            return new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0xFF, (byte)(primary.R/2), (byte)(primary.G/2), (byte)(primary.B/2)), 0.0),
                new GradientStop(primary,   0.5),
                new GradientStop(secondary, 1.0)
            }, new Point(0,0), new Point(1,0));
        }

        // ════════════════════════════════════════════════════════════════
        //  DOWNLOAD LOGIC
        // ════════════════════════════════════════════════════════════════

        private string GetSelectedFormat()
            => (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

        private static void SelectComboByTag(ComboBox box, string tag)
        {
            foreach (ComboBoxItem item in box.Items)
                if (item.Tag?.ToString() == tag) { box.SelectedItem = item; return; }
        }

        private void ApplyState(DownloadState s)
        {
            _state = s;
            DownloadButton.Visibility = s == DownloadState.Idle            ? Visibility.Visible : Visibility.Collapsed;
            StopButton.Visibility     = s == DownloadState.Downloading     ? Visibility.Visible : Visibility.Collapsed;
            ResumeButton.Visibility   = s == DownloadState.Paused          ? Visibility.Visible : Visibility.Collapsed;
            CancelButton.Visibility   = (s == DownloadState.Downloading || s == DownloadState.Paused)
                                            ? Visibility.Visible : Visibility.Collapsed;
            ConfirmPanel.Visibility   = s == DownloadState.WaitingConfirm  ? Visibility.Visible : Visibility.Collapsed;

            if (s == DownloadState.Idle)
                DownloadButton.Content = AudioFormats.Contains(GetSelectedFormat())
                    ? FindResource("ExtractAudioButtonText") : FindResource("DownloadButtonText");

            StatusDot.Fill = s switch
            {
                DownloadState.Downloading    => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                DownloadState.Paused         => new SolidColorBrush(Color.FromRgb(0xBB, 0x86, 0xFC)),
                DownloadState.WaitingConfirm => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                _                            => (SolidColorBrush)FindResource("SecondaryColor")
            };

            if (ClearLogButton != null)
                ClearLogButton.IsEnabled = (s != DownloadState.Downloading);

            if (s != DownloadState.Downloading)
                _networkWasLost = false;
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

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            var urls = UrlTextBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(u => u.Trim())
                                     .Where(u => !string.IsNullOrEmpty(u))
                                     .ToArray();

            if (urls.Length == 0) { System.Windows.MessageBox.Show(FindResource("MsgEnterUrl").ToString()); return; }

            string chosenFormat = GetSelectedFormat();
            if (string.IsNullOrEmpty(chosenFormat))
            {
                LogTextBlock.Text = "";
                AppendLog("╔══════════════════════════════════════════════════╗");
                AppendLog("║" + FindResource("FormatSelectionWarningTitle").ToString() + "║");
                AppendLog("║                                                  ║");
                AppendLog("║" + FindResource("VideoFormatsLabel").ToString() + "║");
                AppendLog("║" + FindResource("AudioFormatsLabel").ToString() + "║");
                AppendLog("╚══════════════════════════════════════════════════╝");
                StatusTextBlock.Text = FindResource("StatusNoFormat").ToString();
                return;
            }

            _savedUrls    = urls;
            _savedQuality = (QualityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "best";
            _savedFormat  = chosenFormat;
            _overwrite    = false;

            if (_settings.ClearLogEachDownload) LogTextBlock.Text = "";
            StatusTextBlock.Text = FindResource("StatusChecking").ToString();
            SetProgressFillWidth(0, animate: false);

            if (!_settings.SkipDuplicateCheck && urls.Length == 1)
            {
                AppendLog(FindResource("LogDuplicateCheck").ToString()!);
                string checkResult = await Task.Run(() => RunCheckOnly(_savedUrls[0], _savedFormat));
                
                if (!string.IsNullOrEmpty(checkResult))
                {
                    if (checkResult.StartsWith("PARTIAL:"))
                    {
                        string partialPath = checkResult.Substring("PARTIAL:".Length);
                        AppendLog("");
                        AppendLog("[i] " + SafeResource("LogPartialFound", "Partial download found, resuming from last position…"));
                        AppendLog($"    {partialPath}");
                        AppendLog("");
                        await BeginDownload();
                        return;
                    }

                    if (File.Exists(checkResult))
                    {
                        _detectedFile = checkResult;
                        ConfirmFileNameText.Text = $"📄  {Path.GetFileName(checkResult)}";
                        AppendLog(""); AppendLog(FindResource("LogDuplicateFound").ToString()!);
                        AppendLog($"    {checkResult}"); AppendLog("");
                        AppendLog(FindResource("LogChoicePanel").ToString()!);
                        StatusTextBlock.Text = FindResource("StatusWaitingChoice").ToString();
                        ApplyState(DownloadState.WaitingConfirm);
                        return;
                    }
                }
            }
            await BeginDownload();
        }

        private async void ConfirmYes_Click(object sender, RoutedEventArgs e)
        {
            _overwrite = true;
            AppendLog(""); AppendLog(FindResource("LogOverwriteConfirmed").ToString()!);
            AppendLog(string.Format(FindResource("LogNewConfig").ToString()!, _savedQuality, _savedFormat.ToUpper()));
            AppendLog("");
            await BeginDownload();
        }

        private void ConfirmNo_Click(object sender, RoutedEventArgs e)
        {
            AppendLog(""); AppendLog(FindResource("LogCancelled").ToString()!);
            StatusTextBlock.Text = FindResource("StatusCancelled").ToString();
            _savedUrls = Array.Empty<string>(); _detectedFile = "";
            ApplyState(DownloadState.Idle);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            KillActiveProcess();
            _autoResumePending = false;
            ApplyState(DownloadState.Paused);
            AppendLog("");
            AppendLog(SafeResource("LogPaused",     "⏸  Download paused by user."));
            AppendLog(SafeResource("LogPausedHint", "    Press RESUME to continue from where it stopped."));
            StatusTextBlock.Text = SafeResource("StatusPaused", "Download paused");
        }

        private async void ResumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_savedUrls.Length == 0) return;
            AppendLog(SafeResource("LogResuming", "▶  Resuming download from last position…"));
            StatusTextBlock.Text = SafeResource("StatusResuming", "Resuming download…");
            _overwrite = false;
            await StartDownloadAsync();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            KillActiveProcess();
            _autoResumePending = false;
            LogTextBlock.Text        = "";
            StatusTextBlock.Text     = FindResource("StatusCancelled").ToString();
            SetProgressFillWidth(0, animate: false);
            PercentageTextBlock.Text = "0%";
            _savedUrls = Array.Empty<string>(); _savedFormat = ""; _overwrite = false;
            ApplyState(DownloadState.Idle);
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = SettingsManager.GetDownloadDirectory(_settings);
                if (Directory.Exists(dir)) Process.Start("explorer.exe", dir);
            }
            catch (Exception ex)
            {
                AppendLog($"[ERR] Could not open folder: {ex.Message}");
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  HISTORY
        // ════════════════════════════════════════════════════════════════

        private void AddToHistory(string[] urls, string format, string quality, bool success)
        {
            if (urls == null || urls.Length == 0) return;
            foreach (var url in urls)
            {
                var entry = new HistoryEntry
                {
                    Timestamp = DateTime.Now,
                    Url       = url,
                    Format    = format.ToUpper(),
                    Quality   = quality,
                    IsSuccess = success
                };
                _settings.History.Insert(0, entry);
            }
            while (_settings.History.Count > 100) _settings.History.RemoveAt(_settings.History.Count - 1);
            SettingsManager.Save(_settings);
        }

        private void LoadHistoryData()
        {
            if (HistoryListView == null) return;
            HistoryListView.ItemsSource = null;
            HistoryListView.ItemsSource = _settings.History;
            NoHistoryPanel.Visibility = (_settings.History.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show(
                FindResource("MsgClearHistoryConfirm").ToString()!,
                FindResource("MsgClearHistoryTitle").ToString()!,
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _settings.History.Clear();
                SettingsManager.Save(_settings);
                LoadHistoryData();
            }
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            if (_state == DownloadState.Downloading) return;
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (_, __) =>
            {
                LogTextBlock.Text = "";
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                LogTextBlock.BeginAnimation(OpacityProperty, fadeIn);
            };
            LogTextBlock.BeginAnimation(OpacityProperty, fadeOut);
        }

        private async Task BeginDownload()
        {
            bool isAudio = AudioFormats.Contains(_savedFormat);
            if (isAudio)
            {
                AppendLog(string.Format(FindResource("LogAudioMode").ToString()!, _savedFormat.ToUpper()));
                AppendLog(FindResource("LogAudioQuality").ToString()!);
                AppendLog("");
            }
            await StartDownloadAsync();
        }

        private async Task StartDownloadAsync()
        {
            ApplyState(DownloadState.Downloading);
            _cts = new CancellationTokenSource();
            bool success = false;
            try
            {
                await Task.Run(() => RunDownloader(_savedUrls, _savedQuality, _savedFormat, _overwrite, _cts.Token));
                if (_state == DownloadState.Downloading)
                {
                    success = true;
                    if (_settings.ShowCompletionNotify)
                    {
                        string title = FindResource("NotifyTitle").ToString()!;
                        string msg   = string.Format(FindResource("NotifyMessage").ToString()!, _savedFormat.ToUpper());
                        ShowToastNotification(title, msg);
                    }
                    if (_settings.AutoOpenFolder)
                        Process.Start("explorer.exe", SettingsManager.GetDownloadDirectory(_settings));
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Windows.MessageBox.Show("Error: " + ex.Message); ApplyState(DownloadState.Idle); }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                AddToHistory(_savedUrls, _savedFormat, _savedQuality, success);
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

        private void ShowToastNotification(string title, string message)
        {
            try
            {
                using var icon = new WinForms.NotifyIcon
                {
                    Icon            = System.Drawing.SystemIcons.Information,
                    Visible         = true,
                    BalloonTipTitle = title,
                    BalloonTipText  = message,
                    BalloonTipIcon  = WinForms.ToolTipIcon.Info
                };
                icon.ShowBalloonTip(4000);
                Thread.Sleep(4500);
            }
            catch { }
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
                        UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
                    };
                    p.Start(); p.WaitForExit();
                    if (p.ExitCode == 0) return name;
                }
                catch { }
            }
            return "python";
        }

        private string RunCheckOnly(string url, string format)
        {
            string python    = GetPythonExecutable();
            string outputDir = SettingsManager.GetDownloadDirectory(_settings);
            var si = new ProcessStartInfo
            {
                FileName               = python,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding  = System.Text.Encoding.UTF8,
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
                p.WaitForExit();
                foreach (var line in stdout.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("[FILECHECK_DEBUG]"))    Dispatcher.Invoke(() => AppendLog(trimmed));
                    else if (trimmed.StartsWith("[FILECHECK_ERROR]")) Dispatcher.Invoke(() => AppendLog($"[WARN] Check failed: {trimmed}"));
                }
                foreach (var line in stdout.Split('\n'))
                {
                    string l = line.TrimStart();
                    if (l.StartsWith("[FILECHECK] "))
                        return l.Substring("[FILECHECK] ".Length).Trim();
                    if (l.StartsWith("[FILECHECK_PARTIAL] "))
                        return "PARTIAL:" + l.Substring("[FILECHECK_PARTIAL] ".Length).Trim();
                }
            }
            catch (Exception ex) { Dispatcher.Invoke(() => AppendLog($"[WARN] RunCheckOnly exception: {ex.Message}")); }
            return "";
        }

        private void RunDownloader(string[] urls, string quality, string format, bool overwrite, CancellationToken ct)
        {
            string python    = GetPythonExecutable();
            string outputDir = SettingsManager.GetDownloadDirectory(_settings);
            var si = new ProcessStartInfo
            {
                FileName               = python,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding  = System.Text.Encoding.UTF8,
            };
            si.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            si.EnvironmentVariables["PYTHONUTF8"]       = "1";
            si.ArgumentList.Add("downloader.py");
            foreach (var url in urls) si.ArgumentList.Add(url);
            si.ArgumentList.Add("--output");  si.ArgumentList.Add(outputDir);
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
                if (!string.IsNullOrEmpty(e.Data)) Dispatcher.Invoke(() => ProcessOutput(e.Data));
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) Dispatcher.Invoke(() => AppendLog("[ERR] " + e.Data));
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            lock (_processLock) _activeProcess = null;
            ct.ThrowIfCancellationRequested();
        }

        // ════════════════════════════════════════════════════════════════
        //  OUTPUT PARSING  ←  ✅ UPDATED: Professional network messages
        // ════════════════════════════════════════════════════════════════

        private void ProcessOutput(string data)
        {
            // ── [NETWORK_LOST] ────────────────────────────────────────────────────
            if (data.StartsWith("[NETWORK_LOST]"))
            {
                _networkWasLost    = true;
                _autoResumePending = true;
                StatusDot.Fill       = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                StatusTextBlock.Text = SafeResource("StatusNetworkLost", "⚡ Connection lost — waiting…");

                AppendLog("");
                AppendLog(SafeResource("LogNetworkLost_L1",
                    "╔══════════════════════════════════════════════════╗"));
                AppendLog(SafeResource("LogNetworkLost_L2",
                    "║  ⚡  CONNECTION LOST                              ║"));
                AppendLog(SafeResource("LogNetworkLost_L3",
                    "╠══════════════════════════════════════════════════╣"));
                AppendLog(SafeResource("LogNetworkLost_L4",
                    "║  Your internet connection has been interrupted.  ║"));
                AppendLog(SafeResource("LogNetworkLost_L5",
                    "║  The download is paused — no data has been lost. ║"));
                AppendLog(SafeResource("LogNetworkLost_L6",
                    "║  Monitoring the connection in the background...  ║"));
                AppendLog(SafeResource("LogNetworkLost_L7",
                    "║  Download will resume automatically once the     ║"));
                AppendLog(SafeResource("LogNetworkLost_L8",
                    "║  connection is restored. No action required.     ║"));
                AppendLog(SafeResource("LogNetworkLost_L9",
                    "╚══════════════════════════════════════════════════╝"));
                AppendLog("");
                return;
            }

            // ── [NETWORK_WAITING] ─────────────────────────────────────────────────
            if (data.StartsWith("[NETWORK_WAITING]"))
            {
                _autoResumePending = true;
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                var m = System.Text.RegularExpressions.Regex.Match(data, @"\((\d+)s elapsed\)");
                string elapsed = m.Success ? $" ({m.Groups[1].Value}s)" : "";
                StatusTextBlock.Text = SafeResource("StatusNetworkWaiting", "Waiting for internet connection…") + elapsed;
                return;
            }

            // ── [NETWORK_RESTORED] ────────────────────────────────────────────────
            if (data.StartsWith("[NETWORK_RESTORED]"))
            {
                _networkWasLost    = false;
                _autoResumePending = false;
                StatusDot.Fill       = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                StatusTextBlock.Text = SafeResource("StatusNetworkRestored", "✔ Connection restored — resuming…");

                AppendLog(SafeResource("LogNetworkRestored_L1",
                    "╔══════════════════════════════════════════════════╗"));
                AppendLog(SafeResource("LogNetworkRestored_L2",
                    "║  ✔  CONNECTION RESTORED                          ║"));
                AppendLog(SafeResource("LogNetworkRestored_L3",
                    "╠══════════════════════════════════════════════════╣"));
                AppendLog(SafeResource("LogNetworkRestored_L4",
                    "║  Internet is back — resuming your download now.  ║"));
                AppendLog(SafeResource("LogNetworkRestored_L5",
                    "║  Continuing from the exact byte it stopped at.   ║"));
                AppendLog(SafeResource("LogNetworkRestored_L6",
                    "╚══════════════════════════════════════════════════╝"));
                AppendLog("");
                return;
            }

            // ── [RESTRICTED] ──────────────────────────────────────────────────────
            if (data.StartsWith("[RESTRICTED]"))
            {
                string msg = SafeResource("MsgRestricted", data.Substring("[RESTRICTED]".Length).Trim());
                AppendLog($"\n[⚠] {msg}");
                StatusTextBlock.Text = msg;
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
                return;
            }

            // ── [NETWORK_ERROR] ───────────────────────────────────────────────────
            if (data.StartsWith("[NETWORK_ERROR]"))
            {
                AppendLog("[⚠ NETWORK] " + data.Substring("[NETWORK_ERROR]".Length).Trim());
                return;
            }

            // ── [ITEM_PROGRESS] ───────────────────────────────────────────────────
            if (data.StartsWith("[ITEM_PROGRESS]"))
            {
                string progress = data.Substring("[ITEM_PROGRESS]".Length).Trim();
                StatusTextBlock.Text = $"{SafeResource("BatchProgressLabel", "Downloading")} {progress}";
                return;
            }

            // ── [NETWORK_TIMEOUT] ─────────────────────────────────────────────────
            if (data.StartsWith("[NETWORK_TIMEOUT]"))
            {
                _autoResumePending = false;
                StatusDot.Fill = (SolidColorBrush)FindResource("SecondaryColor");
                AppendLog("[⚠ NETWORK] " + data.Substring("[NETWORK_TIMEOUT]".Length).Trim());
                return;
            }

            // ── [PROGRESS] ────────────────────────────────────────────────────────
            if (data.Contains("[PROGRESS]"))
            {
                int    idx  = data.IndexOf("[PROGRESS]") + "[PROGRESS]".Length;
                string pStr = data.Substring(idx).Trim().Replace(",", ".");
                var    m    = System.Text.RegularExpressions.Regex.Match(pStr, @"\d+(\.\d+)?");
                if (m.Success &&
                    double.TryParse(m.Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double pct))
                    SetProgressFillWidth(pct);
                if (data.TrimStart().StartsWith("[PROGRESS]")) return;
            }

            // ── [STATUS] ──────────────────────────────────────────────────────────
            if (data.StartsWith("[STATUS]"))
            {
                string status = data.Replace("[STATUS]", "").Trim();
                if (status == "Success")
                    status = FindResource("StatusSuccess").ToString()!;
                else if (status.StartsWith("Starting download"))
                    status = FindResource("StatusStarting").ToString()!;
                else if (status.StartsWith("Retrying"))
                    status = SafeResource("StatusRetrying", "Reconnecting and resuming…");

                StatusTextBlock.Text = status;
                if (!_networkWasLost)
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));

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
