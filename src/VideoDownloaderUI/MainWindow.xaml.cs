using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

            string quality = (QualityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "best";
            string format = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "mp4";

            DownloadButton.IsEnabled = false;
            LogTextBlock.Text = "";
            DownloadProgressBar.Value = 0;
            StatusTextBlock.Text = "Starting...";

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
                StatusTextBlock.Text = "Ready";
            }
        }

        private string GetPythonExecutable()
        {
            // Try common python names
            string[] names = { "python", "python3", "py" };
            foreach (var name in names)
            {
                try
                {
                    using (Process p = new Process())
                    {
                        p.StartInfo.FileName = name;
                        p.StartInfo.Arguments = "--version";
                        p.StartInfo.UseShellExecute = false;
                        p.StartInfo.RedirectStandardOutput = true;
                        p.StartInfo.CreateNoWindow = true;
                        p.Start();
                        p.WaitForExit();
                        if (p.ExitCode == 0) return name;
                    }
                }
                catch { }
            }
            return "python"; // Default fallback
        }

        private void RunDownloader(string url, string quality, string format)
        {
            string pythonExe = GetPythonExecutable();

            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = pythonExe;
            start.ArgumentList.Add("downloader.py");
            start.ArgumentList.Add(url);
            start.ArgumentList.Add("--quality");
            start.ArgumentList.Add(quality);
            start.ArgumentList.Add("--format");
            start.ArgumentList.Add(format);
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
                    PercentageTextBlock.Text = $"{percent:F1}%";
                }
            }
            else if (data.StartsWith("[STATUS]"))
            {
                StatusTextBlock.Text = data.Replace("[STATUS]", "").Trim();
                LogTextBlock.Text += data + "\n";
            }
            else if (data.StartsWith("[WARNING]"))
            {
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
