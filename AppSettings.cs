using System;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace VideoDownloaderUI
{
    public class AppSettings
    {
        public string  DownloadPath          { get; set; } = "";
        public string  DefaultQuality        { get; set; } = "best";
        public string  DefaultFormat         { get; set; } = "mp4";
        public string  AccentTheme           { get; set; } = "teal";   // teal | purple | pink | blue | orange
        public bool    AutoOpenFolder        { get; set; } = false;
        public bool    ShowCompletionNotify  { get; set; } = true;
        public bool    ClearLogEachDownload  { get; set; } = false;
        public bool    SkipDuplicateCheck    { get; set; } = false;
        public string  ProxyAddress          { get; set; } = "";
        public int     MaxRetries            { get; set; } = 3;
    }

    public static class SettingsManager
    {
        private static readonly string _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProVideoDownloader",
            "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    string json = File.ReadAllText(_settingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        // Returns the resolved download directory (falls back to exe folder)
        public static string GetDownloadDirectory(AppSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.DownloadPath) &&
                Directory.Exists(settings.DownloadPath))
                return settings.DownloadPath;

            return Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
        }

        public static (Color primary, Color secondary) GetThemeColors(string theme) => theme switch
        {
            "purple" => (Color.FromRgb(0xBB, 0x86, 0xFC), Color.FromRgb(0x7C, 0x4D, 0xFF)),
            "pink"   => (Color.FromRgb(0xFF, 0x6B, 0x9D), Color.FromRgb(0xFF, 0x3D, 0x7F)),
            "blue"   => (Color.FromRgb(0x42, 0xA5, 0xF5), Color.FromRgb(0x21, 0x96, 0xF3)),
            "orange" => (Color.FromRgb(0xFF, 0xB7, 0x4D), Color.FromRgb(0xFF, 0x98, 0x00)),
            "green"  => (Color.FromRgb(0x66, 0xBB, 0x6A), Color.FromRgb(0x43, 0xA0, 0x47)),
            _        => (Color.FromRgb(0x03, 0xDA, 0xC6), Color.FromRgb(0x00, 0xB0, 0xA0)), // teal (default)
        };
    }
}
