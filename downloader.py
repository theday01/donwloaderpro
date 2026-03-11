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

def download_video(url, output_path):
    ffmpeg_available = shutil.which('ffmpeg') is not None

    if not ffmpeg_available:
        print("[WARNING] ffmpeg not found. Downloading single file 'best' quality (no merge).")
        sys.stdout.flush()

    ydl_opts = {
        'format': 'bestvideo+bestaudio/best' if ffmpeg_available else 'best',
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

    args = parser.parse_args()

    download_video(args.url, args.output)
