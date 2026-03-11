using System;
using System.Diagnostics;
using System.IO;

namespace IntegrationTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Testing integration with downloader.py...");
            
            string root = FindProjectRoot(AppContext.BaseDirectory);

            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = "python3";
            start.Arguments = "downloader.py --help";
            start.WorkingDirectory = root;
            start.UseShellExecute = false;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.CreateNoWindow = true;

            using (Process? process = Process.Start(start))
            {
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    Console.WriteLine("Output from downloader.py:");
                    Console.WriteLine(output);

                    if (!string.IsNullOrEmpty(error))
                    {
                        Console.WriteLine("Error from downloader.py:");
                        Console.WriteLine(error);
                    }

                    if (output.Contains("Multi-platform Video Downloader Engine"))
                    {
                        Console.WriteLine("SUCCESS: Integration works!");
                    }
                    else
                    {
                        Console.WriteLine("FAILURE: Integration failed!");
                        Environment.Exit(1);
                    }
                }
                else
                {
                    Console.WriteLine("FAILURE: Could not start process!");
                    Environment.Exit(1);
                }
            }
        }

        static string FindProjectRoot(string currentDir)
        {
            var dir = new DirectoryInfo(currentDir);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "downloader.py")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return currentDir;
        }
    }
}
