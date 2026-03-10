using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace VideoDownloaderUI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            string url = UrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Please enter a valid URL.");
                return;
            }

            DownloadButton.IsEnabled = false;
            LogTextBlock.Text = "";
            DownloadProgressBar.Value = 0;
            StatusTextBlock.Text = "Starting...";

            try
            {
                await Task.Run(() => RunDownloader(url));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                DownloadButton.IsEnabled = true;
                StatusTextBlock.Text = "Ready";
            }
        }

        private void RunDownloader(string url)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = "python3"; // Use python3 for consistency
            start.Arguments = $"downloader.py \"{url}\"";
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.CreateNoWindow = true;

            using (Process? process = Process.Start(start))
            {
                if (process != null)
                {
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                ProcessOutput(e.Data);
                            });
                        }
                    };
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                LogTextBlock.Text += "[ERR] " + e.Data + "\n";
                            });
                        }
                    };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                }
            }
        }

        private void ProcessOutput(string data)
        {
            if (data.StartsWith("[PROGRESS]"))
            {
                string percentStr = data.Replace("[PROGRESS]", "").Trim();
                if (double.TryParse(percentStr, out double percent))
                {
                    DownloadProgressBar.Value = percent;
                }
            }
            else if (data.StartsWith("[STATUS]"))
            {
                StatusTextBlock.Text = data.Replace("[STATUS]", "").Trim();
                LogTextBlock.Text += data + "\n";
            }
            else if (data.StartsWith("[ERROR]"))
            {
                LogTextBlock.Text += data + "\n";
            }
            else
            {
                LogTextBlock.Text += data + "\n";
            }
        }
    }
}
