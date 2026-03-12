import os
import sys
import argparse
import shutil
import yt_dlp
import re

# Force UTF-8 output on Windows (avoids charmap codec errors with non-ASCII titles)
if sys.stdout.encoding and sys.stdout.encoding.lower() != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if sys.stderr.encoding and sys.stderr.encoding.lower() != 'utf-8':
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

AUDIO_FORMATS = {'mp3', 'm4a', 'wav'}
VIDEO_FORMATS = {'mp4', 'webm', 'avi', 'wmv'}

def progress_hook(d):
    if d['status'] == 'downloading':
        p_str = d.get('_percent_str', '0%')
        p_clean = re.sub(r'\x1b\[[0-9;]*m', '', p_str).replace('%', '').strip()
        print(f"\n[PROGRESS] {p_clean}")
        sys.stdout.flush()
    elif d['status'] == 'finished':
        print("[STATUS] Download finished, now processing...")
        sys.stdout.flush()

def postprocess_hook(d):
    if d['status'] == 'started':
        pp_name = d.get('postprocessor', '')
        if 'ExtractAudio' in pp_name:
            print("[STATUS] Converting to audio format...")
            print("\n[PROGRESS] 95")
            sys.stdout.flush()
        elif 'Merger' in pp_name or 'Convert' in pp_name:
            print("[STATUS] Converting video format...")
            print("\n[PROGRESS] 95")
            sys.stdout.flush()
    elif d['status'] == 'finished':
        pp_name = d.get('postprocessor', '')
        if 'ExtractAudio' in pp_name:
            print("[STATUS] Audio conversion complete!")
            print("\n[PROGRESS] 100")
            sys.stdout.flush()
        elif 'Merger' in pp_name or 'Convert' in pp_name:
            print("[STATUS] Video conversion complete!")
            print("\n[PROGRESS] 100")
            sys.stdout.flush()

# ── Pre-download check ─────────────────────────────────────────────────────────
def check_existing(url, output_path, ext):
    """
    Fetch video title WITHOUT downloading, then scan the output directory
    for any existing file that shares the same sanitized base-name (any extension).
    Prints:
      [FILECHECK] <full_path>   — if a matching file was found
      [FILECHECK_NONE]          — if nothing was found
      [FILECHECK_ERROR] <msg>   — if info-fetch itself failed
    """
    ydl_opts = {
        'quiet': True,
        'no_warnings': True,
        'outtmpl': os.path.join(output_path, '%(title)s.%(ext)s'),
    }
    try:
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            info     = ydl.extract_info(url, download=False)
            raw_path = ydl.prepare_filename(info)     # uses source ext — may be wrong
            base     = os.path.splitext(raw_path)[0]  # strip source ext → sanitized title path
            dir_path  = os.path.dirname(base)  or "."
            base_name = os.path.basename(base)

            safe_base = base_name.encode('utf-8', errors='replace').decode('utf-8')
            safe_dir  = dir_path.encode('utf-8',  errors='replace').decode('utf-8')
            print(f"[FILECHECK_DEBUG] looking for base: {safe_base!r} in dir: {safe_dir!r}")
            sys.stdout.flush()

            # ── Priority 1: exact requested extension ──────────────────────
            exact = os.path.join(dir_path, f"{base_name}.{ext.lower()}")
            if os.path.isfile(exact):
                print(f"[FILECHECK] {exact}")
                sys.stdout.flush()
                return

            # ── Priority 2: any extension with same sanitized base-name ───
            # This catches cases where the merged file has a different ext
            # than what prepare_filename predicted.
            if os.path.isdir(dir_path):
                for fname in sorted(os.listdir(dir_path)):
                    name_no_ext, _ = os.path.splitext(fname)
                    if name_no_ext == base_name:
                        found = os.path.join(dir_path, fname)
                        print(f"[FILECHECK] {found}")
                        sys.stdout.flush()
                        return

            # ── Nothing found ──────────────────────────────────────────────
            print("[FILECHECK_NONE]")
            sys.stdout.flush()

    except Exception as e:
        print(f"[FILECHECK_ERROR] {str(e)}")
        sys.stdout.flush()

# ── Main download ──────────────────────────────────────────────────────────────
def download_video(url, output_path, quality="best", ext="mp4", overwrite=False):
    ffmpeg_available = (shutil.which('ffmpeg') is not None) or \
                       (os.path.exists(os.path.join(os.path.dirname(__file__), 'ffmpeg.exe'))) or \
                       (os.path.exists(os.path.join(os.getcwd(), 'ffmpeg.exe')))

    is_audio = ext.lower() in AUDIO_FORMATS

    # ── AUDIO ─────────────────────────────────────────────────────────────────
    if is_audio:
        if not ffmpeg_available:
            print("[ERROR] Audio conversion requires FFmpeg. Please install FFmpeg and add it to PATH.")
            sys.stdout.flush()
            sys.exit(1)

        print(f"[STATUS] Downloading best audio for {ext.upper()} conversion...")
        sys.stdout.flush()

        ydl_opts = {
            'format': 'bestaudio/best',
            'outtmpl': os.path.join(output_path, '%(title)s.%(ext)s'),
            'progress_hooks': [progress_hook],
            'postprocessor_hooks': [postprocess_hook],
            'postprocessors': [
                {
                    'key': 'FFmpegExtractAudio',
                    'preferredcodec': ext.lower(),
                    'preferredquality': '192' if ext.lower() == 'mp3' else '0',
                }
            ],
            'quiet': True,
            'no_warnings': True,
            'continuedl': not overwrite,
            'overwrites': overwrite,
        }

    # ── VIDEO ─────────────────────────────────────────────────────────────────
    else:
        if not ffmpeg_available:
            print("[WARNING] FFmpeg not found. Downloading single-file best quality (no merge/convert).")
            sys.stdout.flush()
            format_str = 'best'
            merge_fmt  = None
        else:
            quality_map = {
                "2160": 2160,  # 4K
                "1440": 1440,  # 2K
                "1080": 1080,  # Full HD
                "720":   720,  # HD
                "480":   480,  # SD
                "360":   360,  # Low
                "240":   240,  # Very low
                "144":   144,  # Minimum
            }
            if quality in quality_map:
                h = quality_map[quality]
                format_str = f'bestvideo[height<={h}]+bestaudio/best[height<={h}]'
                print(f"[STATUS] Target quality: {h}p (picks closest available if exact height not found)")
                sys.stdout.flush()
            else:
                format_str = 'bestvideo+bestaudio/best'

            merge_fmt = ext.lower()

            if ext.lower() in ('avi', 'wmv'):
                print(f"[STATUS] Will convert to {ext.upper()} after download (this may take a while)...")
                sys.stdout.flush()

        postprocessors = []
        if ffmpeg_available and ext.lower() in ('avi', 'wmv'):
            postprocessors.append({
                'key': 'FFmpegVideoConvertor',
                'preferedformat': ext.lower(),
            })

        ydl_opts = {
            'format': format_str,
            'merge_output_format': merge_fmt if ffmpeg_available else None,
            'outtmpl': os.path.join(output_path, '%(title)s.%(ext)s'),
            'progress_hooks': [progress_hook],
            'postprocessor_hooks': [postprocess_hook],
            'postprocessors': postprocessors if postprocessors else None,
            'quiet': True,
            'no_warnings': True,
            'continuedl': not overwrite,
            'overwrites': overwrite,
        }
        ydl_opts = {k: v for k, v in ydl_opts.items() if v is not None}

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
    parser.add_argument("url",          help="URL of the video to download")
    parser.add_argument("--output",  "-o", default=".",    help="Output directory")
    parser.add_argument("--quality", "-q", default="best", help="Quality: best/2160/1440/1080/720/480/360/240/144")
    parser.add_argument("--format",  "-f", default="mp4",  help="Format: mp4/webm/avi/wmv/mp3/m4a/wav")
    parser.add_argument("--check-only",    action="store_true", help="Only check expected filename, do not download")
    parser.add_argument("--overwrite",     action="store_true", help="Overwrite existing file")

    args = parser.parse_args()

    if args.check_only:
        check_existing(args.url, args.output, args.format)
    else:
        download_video(args.url, args.output, args.quality, args.format, args.overwrite)
