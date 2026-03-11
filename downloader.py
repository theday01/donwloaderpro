import os
import sys
import argparse
import shutil
import yt_dlp

def progress_hook(d):
    if d['status'] == 'downloading':
        p = d.get('_percent_str', '0%').replace('%', '').strip()
        print(f"[PROGRESS] {p}")
        sys.stdout.flush()
    elif d['status'] == 'finished':
        print("[STATUS] Download finished, now converting...")
        sys.stdout.flush()

def download_video(url, output_path, quality="best"):
    # Check in PATH and in current script directory for FFmpeg
    ffmpeg_available = (shutil.which('ffmpeg') is not None) or \
                       (os.path.exists(os.path.join(os.path.dirname(__file__), 'ffmpeg.exe'))) or \
                       (os.path.exists(os.path.join(os.getcwd(), 'ffmpeg.exe')))

    if not ffmpeg_available:
        print("[WARNING] ffmpeg not found in PATH or app directory. Downloading single file 'best' quality (no merge).")
        sys.stdout.flush()
        format_str = 'best'
    else:
        if quality == "1080":
            format_str = 'bestvideo[height<=1080]+bestaudio/best[height<=1080]'
        elif quality == "720":
            format_str = 'bestvideo[height<=720]+bestaudio/best[height<=720]'
        elif quality == "480":
            format_str = 'bestvideo[height<=480]+bestaudio/best[height<=480]'
        else:
            format_str = 'bestvideo+bestaudio/best'

    ydl_opts = {
        'format': format_str,
        'outtmpl': os.path.join(output_path, '%(title)s.%(ext)s'),
        'progress_hooks': [progress_hook],
        'quiet': True,
        'no_warnings': True,
    }

    try:
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            print(f"[STATUS] Starting download for: {url}")
            sys.stdout.flush()
            ydl.download([url])
            print("[STATUS] Success")
            sys.stdout.flush()
    except Exception as e:
        print(f"[ERROR] {str(e)}")
        sys.stdout.flush()
        sys.exit(1)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Multi-platform Video Downloader Engine")
    parser.add_argument("url", help="URL of the video to download")
    parser.add_argument("--output", "-o", default=".", help="Output directory")
    parser.add_argument("--quality", "-q", default="best", help="Quality (best, 1080, 720, 480)")

    args = parser.parse_args()

    download_video(args.url, args.output, args.quality)
