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
- **IMPORTANT for High Quality (4K):** You must download and install `ffmpeg` and add it to your PATH. Without it, the program will fallback to a lower quality single-file format. (See the detailed FFmpeg installation section below).

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

## Detailed FFmpeg Installation Guide

To avoid errors and ensure the highest video quality, follow these steps exactly:

### 1. Download the Correct Version
1.  Go to [gyan.dev/ffmpeg/builds/](https://www.gyan.dev/ffmpeg/builds/).
2.  Scroll down to the **release builds** section.
3.  Download the file ending in `ffmpeg-release-essentials.zip`.

### 2. Extraction and Setup
1.  Right-click the downloaded ZIP and select **Extract All**.
2.  Rename the resulting folder to just `ffmpeg`.
3.  Move this `ffmpeg` folder to the root of your `C:\` drive, so the path is `C:\ffmpeg`.
4.  Inside `C:\ffmpeg`, verify there is a `bin` folder. The final path we need is `C:\ffmpeg\bin`.

### 3. Add FFmpeg to System PATH
This is the most critical step for the program to detect the tool:
1.  Open the Start Menu and search for **"Edit the system environment variables"**.
2.  Click the **Environment Variables** button.
3.  In the **System variables** section (the bottom half), find and double-click the **Path** variable.
4.  Click **New** on the right side.
5.  Type or paste: `C:\ffmpeg\bin`.
6.  Click **OK** on all windows to save the changes.

### 4. Verify Installation
1.  **VERY IMPORTANT:** After adding to PATH, you MUST close all application windows and the Command Prompt, then reopen them for the changes to take effect.
2.  Open a new Command Prompt (CMD).
3.  Type `ffmpeg -version` and press Enter.
4.  If you see version information, you are ready to go!

### The Easy Alternative (If PATH doesn't work)
If you find the PATH setup difficult:
1.  Copy the `ffmpeg.exe` file (found inside the `bin` folder you downloaded).
2.  Paste it directly into the same folder as our application `VideoDownloaderUI.exe` and `downloader.py`.
3.  The program will now detect it automatically.

## Troubleshooting

### "Python was not found" Error
If you see a message saying Python was not found even after installing it, please follow these steps:
1.  Open the Start Menu and search for "Manage app execution aliases".
2.  Turn OFF the aliases for `python.exe` and `python3.exe`.
3.  Restart the application.
4.  If the issue persists, reinstall Python and make sure to check "Add Python to PATH".

---
Developed by Jules.
