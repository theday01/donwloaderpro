# Jules Pro Video Downloader Setup Guide

A professional, multi-platform video downloader (YouTube, Facebook, Instagram, Telegram, etc.) built with Python and C# WPF.

## Prerequisites

To run this application, you need:

1.  **OS:** Windows 7 or later.
2.  **Python:** Version 3.7 or later.
3.  **Runtime:** .NET 6.0 Runtime (or later).

## Installation Steps

### 1. Install Python and Dependencies
- Download and install Python from [python.org](https://www.python.org/).
- **Important:** Make sure to check "Add Python to PATH" during installation.
- Open Command Prompt (CMD) and install the core library:
  ```bash
  pip install yt-dlp
  ```
- **For High Quality (4K):** It is recommended to download and install `ffmpeg` and add it to your PATH.

### 2. Build the Application and Prepare Files
- Open the project directory in your terminal.
- Build the project to generate the executable:
  ```bash
  dotnet build src/VideoDownloaderUI/VideoDownloaderUI.csproj -c Release
  ```
- The executable will be generated in: `src/VideoDownloaderUI/bin/Release/net6.0-windows/`.
- Ensure `downloader.py` is copied to the same directory as `VideoDownloaderUI.exe`.

### 3. Run the Application
- Navigate to the output directory and launch `VideoDownloaderUI.exe`.
- Paste the video URL.
- Click "Download".
- Monitor progress via the progress bar and log window.

## Technical Details
- Frontend: C# WPF (Modern Dark Theme).
- Backend: Python with `yt-dlp` engine.
- Communication: Standard I/O redirection for real-time updates.

## Troubleshooting

### "Python was not found" Error
If you see a message saying Python was not found even after installing it, please follow these steps:
1.  Open the Start Menu and search for "Manage app execution aliases".
2.  Turn OFF the aliases for `python.exe` and `python3.exe`.
3.  Restart the application.
4.  If the issue persists, reinstall Python and make sure to check "Add Python to PATH".

---
Developed by Jules.
