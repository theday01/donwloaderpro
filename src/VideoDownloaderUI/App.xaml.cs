using System;
using System.Threading;
using System.Windows;

namespace VideoDownloaderUI
{
    public partial class App : Application
    {
        private static Mutex? _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "EagleVStream_SingleInstance_Mutex";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // App is already running
                _mutex.Close();
                _mutex = null;

                ShowAlreadyRunningMessage();
                Application.Current.Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        private void ShowAlreadyRunningMessage()
        {
            try
            {
                var settings = SettingsManager.Load();
                string lang = settings.Language ?? "en";

                var dict = new ResourceDictionary
                {
                    Source = new Uri($"Resources/Strings.{lang}.xaml", UriKind.Relative)
                };

                string message = dict["MsgAppAlreadyRunning"]?.ToString() ?? "EagleVStream is already running.";
                string title = dict["MsgAppAlreadyRunningTitle"]?.ToString() ?? "Already Running";

                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                MessageBox.Show("EagleVStream is already running.", "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch { }
                _mutex.Close();
            }
            base.OnExit(e);
        }
    }
}
