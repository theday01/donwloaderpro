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
using WinForms = System.Windows.Forms;   // alias to avoid ComboBox ambiguity

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
        private bool   _overwrite     = false;
        private string _detectedFile  = "";

        private Process?                 _activeProcess = null;
        private CancellationTokenSource? _cts           = null;
        private readonly object          _processLock   = new object();

        // Settings
        private AppSettings _settings      = new AppSettings();
        private string      _selectedTheme    = "teal";
        private string      _selectedAppTheme = "dark";   // "dark" | "light" 

        // ── Constructor ───────────────────────────────────────────────────

        public MainWindow()
        {
            InitializeComponent();
            _settings        = SettingsManager.Load();
            _selectedAppTheme = _settings.AppTheme;
            ApplySettingsToUI();
            ApplyThemeColors(_settings.AccentTheme);
            ApplyAppTheme(_settings.AppTheme);
            UpdateDownloadPathLabel();
        }

        // ── Settings: open / close ────────────────────────────────────────

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            LoadSettingsIntoPanel();
            SettingsOverlay.Visibility = Visibility.Visible;
            AnimateSettingsIn();
            // Ensure settings panel reflects the current theme immediately
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => ApplySettingsPanelTheme(_settings.AppTheme == "light")));
        }

        private void CloseSettings_Click(object sender, RoutedEventArgs e)  => CloseSettingsPanel();
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

        // ── Settings: load values into panel controls ─────────────────────

        private void LoadSettingsIntoPanel()
        {
            DownloadPathBox.Text        = _settings.DownloadPath;
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
        }

        // ── Settings: save ────────────────────────────────────────────────

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _settings.DownloadPath         = DownloadPathBox.Text.Trim();
            _settings.AutoOpenFolder       = AutoOpenFolderCheck.IsChecked == true;
            _settings.ShowCompletionNotify = ShowNotificationCheck.IsChecked == true;
            _settings.ClearLogEachDownload = ClearLogCheck.IsChecked == true;
            _settings.SkipDuplicateCheck   = SkipDuplicateCheck.IsChecked == true;
            _settings.AccentTheme          = _selectedTheme;
            _settings.AppTheme             = _selectedAppTheme;

            _settings.DefaultQuality = (DefaultQualityBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "best";
            _settings.DefaultFormat  = (DefaultFormatBox.SelectedItem  as ComboBoxItem)?.Tag?.ToString() ?? "mp4";

            SettingsManager.Save(_settings);
            ApplyThemeColors(_selectedTheme);
            ApplyAppTheme(_selectedAppTheme);
            UpdateDownloadPathLabel();
            ApplySettingsToUI();

            CloseSettingsPanel();
            AppendLog("[⚙] Settings saved successfully.");
        }

        // ── Settings: reset ───────────────────────────────────────────────

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            _settings         = new AppSettings();
            _selectedTheme    = "teal";
            _selectedAppTheme = "dark";
            LoadSettingsIntoPanel();
            AppendLog("[⚙] Settings reset to defaults.");
        }

        // ── Browse folder ─────────────────────────────────────────────────

        private void BrowsePath_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog
            {
                Description            = "Select download folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton    = true,
                SelectedPath           = string.IsNullOrWhiteSpace(DownloadPathBox.Text)
                                            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                                            : DownloadPathBox.Text
            };
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                DownloadPathBox.Text = dlg.SelectedPath;
        }

        // ── Theme swatch ──────────────────────────────────────────────────

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
                b.BorderBrush = (b.Tag?.ToString() == selected)
                    ? Brushes.White
                    : Brushes.Transparent;
        }

        // ── App theme (dark / light) cards ───────────────────────────────

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
                DarkThemeCard.BorderBrush  = selected == "dark"  ? accent : Brushes.Transparent;
                DarkThemeCard.Background   = new SolidColorBrush(Color.FromRgb(0x1A,0x1C,0x2E));
            }
            if (LightThemeCard != null)
            {
                LightThemeCard.BorderBrush = selected == "light" ? accent : Brushes.Transparent;
                LightThemeCard.Background  = new SolidColorBrush(
                    selected == "light"
                        ? Color.FromRgb(0xE0,0xE3,0xF0)
                        : Color.FromRgb(0x2C,0x2E,0x3E));
            }
        }

        private void ApplyAppTheme(string appTheme)
        {
            bool isLight = appTheme == "light";

            // ── Palette ──────────────────────────────────────────────────
            // Window & layout
            var windowBg   = isLight ? Color.FromRgb(0xED, 0xEF, 0xFA) : Color.FromRgb(0x0F, 0x11, 0x1A);
            var cardBg     = isLight ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1E, 0x20, 0x2E);
            var inputBg    = isLight ? Color.FromRgb(0xF3, 0xF4, 0xFD) : Color.FromRgb(0x2C, 0x2E, 0x3E);
            var logBg      = isLight ? Color.FromRgb(0xF8, 0xF9, 0xFF) : Color.FromRgb(0x14, 0x16, 0x22);
            var logBorder  = isLight ? Color.FromRgb(0xD8, 0xDB, 0xF0) : Color.FromRgb(0x2C, 0x2E, 0x3E);
            var cardBorder = isLight ? Color.FromRgb(0xE0, 0xE3, 0xF5) : Color.FromRgb(0x1E, 0x20, 0x2E);

            // Text
            var headingFg  = isLight ? Color.FromRgb(0x4A, 0x1A, 0x8C) : Color.FromRgb(0xBB, 0x86, 0xFC);
            var subHeadFg  = isLight ? Color.FromRgb(0x55, 0x55, 0x88) : Color.FromRgb(0x88, 0x88, 0x88);
            var inputFg    = isLight ? Color.FromRgb(0x1A, 0x1A, 0x2E) : Colors.White;
            var labelFg    = isLight ? Color.FromRgb(0x66, 0x66, 0x99) : Color.FromRgb(0x88, 0x88, 0x88);
            var logFg      = isLight ? Color.FromRgb(0x0A, 0x6B, 0x5A) : Color.FromRgb(0x03, 0xDA, 0xC6);
            var progressLabelFg = isLight ? Color.FromRgb(0x44, 0x44, 0x88) : Color.FromRgb(0x88, 0x88, 0x88);
            var percentFg  = isLight ? Color.FromRgb(0x77, 0x22, 0xCC) : Color.FromRgb(0xBB, 0x86, 0xFC);
            var pathFg     = isLight ? Color.FromRgb(0x99, 0x99, 0xBB) : Color.FromRgb(0x44, 0x44, 0x55);

            // Status dot
            var dotReady = isLight ? Color.FromRgb(0x03, 0xB0, 0xA0) : Color.FromRgb(0x03, 0xDA, 0xC6);

            // ── Window & root ─────────────────────────────────────────────
            Background = new SolidColorBrush(windowBg);
            Resources["CardColor"] = new SolidColorBrush(cardBg);

            // ── Header texts ──────────────────────────────────────────────
            if (FindName("AppTitleText") is TextBlock titleTb)
                titleTb.Foreground = new SolidColorBrush(headingFg);
            if (FindName("AppSubtitleText") is TextBlock subTb)
                subTb.Foreground = new SolidColorBrush(subHeadFg);

            // ── URL input card ────────────────────────────────────────────
            if (FindName("InputCard") is Border inputCard)
            {
                inputCard.Background    = new SolidColorBrush(cardBg);
                inputCard.BorderBrush   = new SolidColorBrush(cardBorder);
                inputCard.BorderThickness = new Thickness(isLight ? 1 : 0);
                if (isLight)
                    inputCard.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    { BlurRadius = 18, ShadowDepth = 2, Color = Color.FromRgb(0xC0,0xC4,0xE8), Opacity = 0.45 };
                else
                    inputCard.Effect = null;
            }
            if (UrlTextBox != null)
            {
                UrlTextBox.Background = new SolidColorBrush(inputBg);
                UrlTextBox.Foreground = new SolidColorBrush(inputFg);
                UrlTextBox.CaretBrush = new SolidColorBrush(inputFg);
            }

            // Quality / Format labels
            if (FindName("QualityLabel") is TextBlock ql)
                ql.Foreground = new SolidColorBrush(labelFg);
            if (FindName("FormatLabel") is TextBlock fl)
                fl.Foreground = new SolidColorBrush(labelFg);

            // ── Progress + Log card ───────────────────────────────────────
            if (FindName("ProgressLogCard") is Border plCard)
            {
                plCard.Background    = new SolidColorBrush(cardBg);
                plCard.BorderBrush   = new SolidColorBrush(cardBorder);
                plCard.BorderThickness = new Thickness(isLight ? 1 : 0);
                if (isLight)
                    plCard.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    { BlurRadius = 18, ShadowDepth = 2, Color = Color.FromRgb(0xC0,0xC4,0xE8), Opacity = 0.45 };
                else
                    plCard.Effect = null;
            }
            if (FindName("ProgressLabel") is TextBlock progressLabelTb)
                progressLabelTb.Foreground = new SolidColorBrush(progressLabelFg);
            if (PercentageTextBlock != null)
                PercentageTextBlock.Foreground = new SolidColorBrush(percentFg);

            // Progress track background
            if (FindName("ProgressTrackBg") is Border trackBg)
                trackBg.Background = new SolidColorBrush(
                    isLight ? Color.FromRgb(0xDD,0xDF,0xF5) : Color.FromRgb(0x1C,0x1D,0x2B));

            // Log ScrollViewer
            if (LogScrollViewer != null)
            {
                LogScrollViewer.Background    = new SolidColorBrush(logBg);
                LogScrollViewer.BorderBrush   = new SolidColorBrush(logBorder);
                LogScrollViewer.BorderThickness = new Thickness(1);
            }
            if (LogTextBlock != null)
                LogTextBlock.Foreground = new SolidColorBrush(logFg);

            // Clear button colours
            if (ClearLogButton != null)
            {
                ClearLogButton.Foreground = new SolidColorBrush(
                    isLight ? Color.FromRgb(0x99, 0x44, 0x44) : Color.FromRgb(0x88, 0x88, 0x88));
            }

            // ── Status bar ────────────────────────────────────────────────
            if (StatusTextBlock != null)
                StatusTextBlock.Foreground = new SolidColorBrush(labelFg);
            if (DownloadPathLabel != null)
                DownloadPathLabel.Foreground = new SolidColorBrush(pathFg);
            if (StatusDot != null && _state == DownloadState.Idle)
                StatusDot.Fill = new SolidColorBrush(dotReady);

            // ── Settings panel ────────────────────────────────────────────
            ApplySettingsPanelTheme(isLight);
        }

        private void ApplySettingsPanelTheme(bool isLight)
        {
            // ── Settings panel palette ────────────────────────────────────
            var sBg          = isLight ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x13, 0x15, 0x2A);
            var sHeaderBg    = isLight ? Color.FromRgb(0xF0, 0xF2, 0xFB) : Color.FromRgb(0x0F, 0x11, 0x1A);
            var sFooterBg    = isLight ? Color.FromRgb(0xF0, 0xF2, 0xFB) : Color.FromRgb(0x0F, 0x11, 0x1A);
            var sScrollBg    = isLight ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x13, 0x15, 0x2A);
            var sBorderColor = isLight ? Color.FromRgb(0xE0, 0xE3, 0xF5) : Color.FromRgb(0x2A, 0x2D, 0x45);
            var sBehaviorBg  = isLight ? Color.FromRgb(0xF3, 0xF4, 0xFD) : Color.FromRgb(0x1A, 0x1C, 0x2E);
            var sInputBg     = isLight ? Color.FromRgb(0xF3, 0xF4, 0xFD) : Color.FromRgb(0x2C, 0x2E, 0x3E);
            var sBorderInput = isLight ? Color.FromRgb(0xD8, 0xDB, 0xF0) : Color.FromRgb(0x2C, 0x2E, 0x3E);

            // Text
            var sTitleFg     = isLight ? Color.FromRgb(0x22, 0x10, 0x55) : Colors.White;
            var sSubtitleFg  = isLight ? Color.FromRgb(0x66, 0x66, 0x99) : Color.FromRgb(0x88, 0x88, 0x88);
            var sInputFg     = isLight ? Color.FromRgb(0x1A, 0x1A, 0x2E) : Colors.White;
            var sHintFg      = isLight ? Color.FromRgb(0x99, 0x99, 0xBB) : Color.FromRgb(0x55, 0x55, 0x55);
            var sResetFg     = isLight ? Color.FromRgb(0x88, 0x88, 0xAA) : Color.FromRgb(0x66, 0x66, 0x66);

            // ── SettingsCard (outer) ──────────────────────────────────────
            if (SettingsCard != null)
            {
                SettingsCard.Background   = new SolidColorBrush(sBg);
                SettingsCard.BorderBrush  = new SolidColorBrush(sBorderColor);
                SettingsCard.Effect = isLight
                    ? new System.Windows.Media.Effects.DropShadowEffect
                      { BlurRadius = 40, ShadowDepth = 0, Color = Color.FromRgb(0xB0,0xB8,0xE0), Opacity = 0.5 }
                    : new System.Windows.Media.Effects.DropShadowEffect
                      { BlurRadius = 30, ShadowDepth = 0, Color = Colors.Black, Opacity = 0.6 };
            }

            // ── Header ───────────────────────────────────────────────────
            if (FindName("SettingsHeaderBorder") is Border hdr)
                hdr.Background = new SolidColorBrush(sHeaderBg);
            if (FindName("SettingsTitleText") is TextBlock ttl)
                ttl.Foreground = new SolidColorBrush(sTitleFg);
            if (FindName("SettingsSubtitleText") is TextBlock sub)
                sub.Foreground = new SolidColorBrush(sSubtitleFg);

            // ── Scrollable body ───────────────────────────────────────────
            if (FindName("SettingsScrollViewer") is ScrollViewer sv)
                sv.Background = new SolidColorBrush(sScrollBg);

            // ── TextBox: Download path ────────────────────────────────────
            if (DownloadPathBox != null)
            {
                DownloadPathBox.Background  = new SolidColorBrush(sInputBg);
                DownloadPathBox.Foreground  = new SolidColorBrush(sInputFg);
                DownloadPathBox.CaretBrush  = new SolidColorBrush(sInputFg);
                DownloadPathBox.BorderBrush = new SolidColorBrush(sBorderInput);
                DownloadPathBox.BorderThickness = new Thickness(isLight ? 1 : 0);
            }

            // ── Hints ─────────────────────────────────────────────────────
            if (DownloadPathHint != null)
                DownloadPathHint.Foreground = new SolidColorBrush(sHintFg);
            if (ThemeRestartHint != null)
                ThemeRestartHint.Foreground = new SolidColorBrush(sHintFg);

            // ── Behavior toggles card ─────────────────────────────────────
            if (FindName("BehaviorCard") is Border bc)
                bc.Background = new SolidColorBrush(sBehaviorBg);

            // CheckBox foreground (walk children)
            if (FindName("BehaviorCard") is Border bcWalk)
                SetChildCheckBoxForeground(bcWalk, isLight
                    ? new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x44))
                    : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)));

            // ── Theme cards (dark/light selector) ────────────────────────
            if (DarkThemeCard != null)
            {
                DarkThemeCard.Background = new SolidColorBrush(
                    isLight ? Color.FromRgb(0xE8, 0xEA, 0xF8) : Color.FromRgb(0x1A, 0x1C, 0x2E));
                // title inside
                SetChildTextForeground(DarkThemeCard, isLight
                    ? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E))
                    : new SolidColorBrush(Colors.White), skipFirst: true);
                SetChildTextForeground(DarkThemeCard, isLight
                    ? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xAA))
                    : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), skipFirst: false, labelOnly: true);
            }
            if (LightThemeCard != null)
            {
                LightThemeCard.Background = new SolidColorBrush(
                    isLight ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x2C, 0x2E, 0x3E));
                SetChildTextForeground(LightThemeCard, isLight
                    ? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E))
                    : new SolidColorBrush(Colors.White), skipFirst: true);
                SetChildTextForeground(LightThemeCard, isLight
                    ? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xAA))
                    : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), skipFirst: false, labelOnly: true);
            }
            // Re-apply selection border after theme card background changes
            UpdateAppThemeCards(_selectedAppTheme);

            // ── Footer ────────────────────────────────────────────────────
            if (FindName("SettingsFooterBorder") is Border ftr)
                ftr.Background = new SolidColorBrush(sFooterBg);
            if (FindName("ResetButton") is Button resetBtn)
            {
                // The template renders a TextBlock with Foreground binding — update directly
                resetBtn.Foreground = new SolidColorBrush(sResetFg);
            }
        }

        // ── Helpers: walk visual tree to re-colour children ───────────────

        private static void SetChildCheckBoxForeground(DependencyObject parent, Brush brush)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
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
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock tb)
                {
                    if (skipFirst && !firstSeen) { firstSeen = true; continue; } // skip emoji
                    if (!labelOnly || (tb.FontSize <= 12))
                        tb.Foreground = brush;
                }
                SetChildTextForegroundInner(child, brush, skipFirst, labelOnly, ref firstSeen);
            }
        }

        // ── Apply theme colors to the main window ─────────────────────────

        private void ApplyThemeColors(string theme)
        {
            var (primary, secondary) = SettingsManager.GetThemeColors(theme);
            Resources["SecondaryColor"] = new SolidColorBrush(primary);

            // Update progress bar gradient to match theme
            if (ProgressFill != null && !AudioFormats.Contains(_savedFormat))
            {
                ProgressFill.Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0xFF, (byte)(primary.R / 2), (byte)(primary.G / 2), (byte)(primary.B / 2)), 0.0),
                    new GradientStop(primary, 0.5),
                    new GradientStop(secondary, 1.0)
                }, new Point(0, 0), new Point(1, 0));
            }
        }

        // ── Apply saved preferences to main controls ──────────────────────

        private void ApplySettingsToUI()
        {
            // Set default quality/format combos if they match a tag
            if (!string.IsNullOrEmpty(_settings.DefaultQuality))
                SelectComboByTag(QualityComboBox, _settings.DefaultQuality);

            if (!string.IsNullOrEmpty(_settings.DefaultFormat))
                SelectComboByTag(FormatComboBox, _settings.DefaultFormat);
        }

        private void UpdateDownloadPathLabel()
        {
            if (DownloadPathLabel == null) return;
            string dir = SettingsManager.GetDownloadDirectory(_settings);
            // Show truncated path in status bar
            DownloadPathLabel.Text = dir.Length > 55
                ? "📁 …" + dir[^52..]
                : "📁 " + dir;
            DownloadPathLabel.ToolTip = dir;
        }

        // ── Format ComboBox ───────────────────────────────────────────────

        private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FormatComboBox == null) return;
            string fmt   = GetSelectedFormat();
            bool isAudio = AudioFormats.Contains(fmt);

            if (AudioBadge     != null) AudioBadge.Visibility    = isAudio ? Visibility.Visible : Visibility.Collapsed;
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
                    : BuildThemeGradient(_settings.AccentTheme);
        }

        private LinearGradientBrush BuildThemeGradient(string theme)
        {
            var (primary, secondary) = SettingsManager.GetThemeColors(theme);
            return new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0xFF, (byte)(primary.R / 2), (byte)(primary.G / 2), (byte)(primary.B / 2)), 0.0),
                new GradientStop(primary,  0.5),
                new GradientStop(secondary,1.0)
            }, new Point(0,0), new Point(1,0));
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private string GetSelectedFormat()
            => (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

        private static void SelectComboByTag(ComboBox box, string tag)
        {
            foreach (ComboBoxItem item in box.Items)
            {
                if (item.Tag?.ToString() == tag)
                {
                    box.SelectedItem = item;
                    return;
                }
            }
        }

        private void ApplyState(DownloadState s)
        {
            _state = s;

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

            // Clear button: enabled only when log is not being written
            if (ClearLogButton != null)
                ClearLogButton.IsEnabled = (s != DownloadState.Downloading);
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
            if (string.IsNullOrEmpty(url)) { System.Windows.MessageBox.Show("Please enter a valid URL."); return; }

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

            if (_settings.ClearLogEachDownload) LogTextBlock.Text = "";

            StatusTextBlock.Text = "Checking...";
            SetProgressFillWidth(0, animate: false);

            // Skip duplicate check if user opted out
            if (!_settings.SkipDuplicateCheck)
            {
                AppendLog("[INFO] Checking if file was previously downloaded...");
                string existingFile = await Task.Run(() => RunCheckOnly(_savedUrl, _savedFormat));

                if (!string.IsNullOrEmpty(existingFile) && File.Exists(existingFile))
                {
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
            }

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
            StatusTextBlock.Text = "Cancelled";
            _savedUrl            = "";
            _detectedFile        = "";
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

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            // Only allowed when nothing is actively writing to the log
            if (_state == DownloadState.Downloading) return;

            // Fade out → clear → fade in
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (_, __) =>
            {
                LogTextBlock.Text = "";
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                LogTextBlock.BeginAnimation(OpacityProperty, fadeIn);
            };
            LogTextBlock.BeginAnimation(OpacityProperty, fadeOut);
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

                // Post-download actions
                if (_state == DownloadState.Downloading)
                {
                    if (_settings.ShowCompletionNotify)
                        ShowToastNotification("Download Complete", $"{_savedFormat.ToUpper()} file saved successfully!");

                    if (_settings.AutoOpenFolder)
                    {
                        string dir = SettingsManager.GetDownloadDirectory(_settings);
                        Process.Start("explorer.exe", dir);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Windows.MessageBox.Show("Error: " + ex.Message); ApplyState(DownloadState.Idle); }
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

        private void ShowToastNotification(string title, string message)
        {
            try
            {
                using var icon = new WinForms.NotifyIcon
                {
                    Icon    = System.Drawing.SystemIcons.Information,
                    Visible = true,
                    BalloonTipTitle = title,
                    BalloonTipText  = message,
                    BalloonTipIcon  = WinForms.ToolTipIcon.Info
                };
                icon.ShowBalloonTip(4000);
                System.Threading.Thread.Sleep(4500);
            }
            catch { /* notification not critical */ }
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
                    if (trimmed.StartsWith("[FILECHECK_DEBUG]"))
                        Dispatcher.Invoke(() => AppendLog(trimmed));
                    else if (trimmed.StartsWith("[FILECHECK_ERROR]"))
                        Dispatcher.Invoke(() => AppendLog($"[WARN] Check failed: {trimmed}"));
                }

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
