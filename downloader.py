import os
import sys
import argparse
import shutil
import socket
import time
import yt_dlp
import re

# Set to True to restrict downloads to YouTube URLs only
YOUTUBE_ONLY = True

# Force UTF-8 output on Windows
if sys.stdout.encoding and sys.stdout.encoding.lower() != 'utf-8':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
if sys.stderr.encoding and sys.stderr.encoding.lower() != 'utf-8':
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

AUDIO_FORMATS = {'mp3', 'm4a', 'wav'}
VIDEO_FORMATS = {'mp4', 'webm', 'avi', 'wmv'}

# ── Network helpers ────────────────────────────────────────────────────────────

NETWORK_ERROR_KEYWORDS = [
    'network', 'connection', 'timed out', 'timeout', 'unreachable',
    'unable to connect', 'connection reset', 'broken pipe', 'name resolution',
    'temporary failure', 'urlopen error', 'remotedisconnected',
    'connectionreseterror', 'econnreset', 'etimedout', 'ehostunreach',
    'no route to host', 'ssl', 'handshake', 'read timeout',
    'connect timeout', 'socket', 'incomplete read', 'connection aborted',
    'connection timed', 'network is unreachable', 'failed to connect',
    'http error 5', 'http error 429', 'http error 403', 'getaddrinfo failed',
    'connection reset by peer', 'remote end closed connection',
]

def is_internet_available(hosts=None, timeout=3):
    """Quick connectivity check via DNS port — tries 3 well-known servers."""
    if hosts is None:
        hosts = [("8.8.8.8", 53), ("1.1.1.1", 53), ("208.67.222.222", 53)]
    for host, port in hosts:
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.settimeout(timeout)
            s.connect((host, port))
            s.close()
            return True
        except OSError:
            continue
    return False

def is_network_error(exc):
    """Returns True if the exception is likely a connectivity issue."""
    msg = str(exc).lower()
    return any(kw in msg for kw in NETWORK_ERROR_KEYWORDS)

def wait_for_internet(check_interval=5, max_wait=86400):
    """
    Block until internet is available.
    Prints [NETWORK_WAITING] tags every check_interval seconds.
    Returns True when restored, False on timeout.
    """
    print("[NETWORK_LOST] Internet connection lost — download paused.")
    sys.stdout.flush()

    elapsed = 0
    while True:
        if max_wait is not None and elapsed >= max_wait:
            print(f"[NETWORK_TIMEOUT] Gave up waiting after {elapsed}s — aborting.")
            sys.stdout.flush()
            return False

        if is_internet_available():
            if elapsed > 0:
                print("[NETWORK_RESTORED] Internet connection restored — resuming download...")
                sys.stdout.flush()
            return True

        print(f"[NETWORK_WAITING] No internet — checking again in {check_interval}s... ({elapsed}s elapsed)")
        sys.stdout.flush()
        time.sleep(check_interval)
        elapsed += check_interval

# ── Progress / postprocess hooks ───────────────────────────────────────────────

def progress_hook(d):
    if d['status'] == 'downloading':
        p_str = d.get('_percent_str', '0%')
        p_clean = re.sub(r'\x1b\[[0-9;]*m', '', p_str).replace('%', '').strip()
        print(f"\n[PROGRESS] {p_clean}")

        # Playlist / Batch progress info
        # yt-dlp provides 'playlist_index' (1-based) and 'n_entries'
        idx = d.get('playlist_index')
        total = d.get('n_entries')
        if idx is not None and total is not None:
            print(f"[ITEM_PROGRESS] {idx}/{total}")

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

# ── yt-dlp runner with network-aware retry loop ────────────────────────────────

def _run_ydl(urls, ydl_opts, max_reconnect_attempts=30):
    """
    Execute yt-dlp with an outer reconnect loop.

    On network errors  → waits for internet → retries with continuedl=True
    On other errors    → prints [ERROR] and returns False immediately
    On success         → returns True
    """
    if isinstance(urls, str):
        urls = [urls]

    for attempt in range(max_reconnect_attempts):
        if attempt == 0:
            print(f"[STATUS] Starting download for {len(urls)} item(s)...")
        else:
            print(f"[STATUS] Retrying download (attempt {attempt + 1}/{max_reconnect_attempts})...")
        sys.stdout.flush()

        try:
            with yt_dlp.YoutubeDL(ydl_opts) as ydl:
                ydl.download(urls)
            print("[STATUS] Success")
            sys.stdout.flush()
            return True

        except KeyboardInterrupt:
            print("\n[STATUS] Download interrupted by user.")
            sys.stdout.flush()
            return False

        except Exception as e:
            err_msg = str(e)

            if is_network_error(e):
                print(f"[NETWORK_ERROR] {err_msg[:180]}")
                sys.stdout.flush()

                if not wait_for_internet():
                    print("[ERROR] Timed out waiting for internet connection.")
                    sys.stdout.flush()
                    return False

                # Force resume from partial download on next attempt
                ydl_opts = dict(ydl_opts)
                ydl_opts['continuedl'] = True
                ydl_opts['overwrites'] = False
                print("[STATUS] Preparing to resume from last position...")
                sys.stdout.flush()

            else:
                print(f"[ERROR] {err_msg}")
                sys.stdout.flush()
                return False

    print(f"[ERROR] Failed after {max_reconnect_attempts} reconnect attempts.")
    sys.stdout.flush()
    return False

# ── Pre-download duplicate check ───────────────────────────────────────────────

def check_existing(url, output_path, ext):
    ydl_opts = {
        'quiet': True,
        'no_warnings': True,
        'outtmpl': os.path.join(output_path, '%(title)s.%(ext)s'),
    }
    try:
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            info     = ydl.extract_info(url, download=False)
            raw_path = ydl.prepare_filename(info)
            base     = os.path.splitext(raw_path)[0]
            dir_path  = os.path.dirname(base) or "."
            base_name = os.path.basename(base)

            safe_base = base_name.encode('utf-8', errors='replace').decode('utf-8')
            safe_dir  = dir_path.encode('utf-8',  errors='replace').decode('utf-8')
            print(f"[FILECHECK_DEBUG] looking for base: {safe_base!r} in dir: {safe_dir!r}")
            sys.stdout.flush()

            exact = os.path.join(dir_path, f"{base_name}.{ext.lower()}")
            if os.path.isfile(exact):
                print(f"[FILECHECK] {exact}")
                sys.stdout.flush()
                return

            if os.path.isdir(dir_path):
                # First check for exact/complete matches
                for fname in sorted(os.listdir(dir_path)):
                    name_no_ext, fext = os.path.splitext(fname)
                    if name_no_ext == base_name and fext.lower() == f".{ext.lower()}":
                        found = os.path.join(dir_path, fname)
                        print(f"[FILECHECK] {found}")
                        sys.stdout.flush()
                        return

                # Then check for same-name matches (different extension or partials)
                for fname in sorted(os.listdir(dir_path)):
                    # Check for partial files: title.ext.part or title.ext.ytdl
                    if fname.startswith(base_name) and (fname.endswith(".part") or fname.endswith(".ytdl")):
                        found = os.path.join(dir_path, fname)
                        print(f"[FILECHECK_PARTIAL] {found}")
                        sys.stdout.flush()
                        return

                    name_no_ext, _ = os.path.splitext(fname)
                    if name_no_ext == base_name:
                        found = os.path.join(dir_path, fname)
                        print(f"[FILECHECK] {found}")
                        sys.stdout.flush()
                        return

            print("[FILECHECK_NONE]")
            sys.stdout.flush()

    except Exception as e:
        print(f"[FILECHECK_ERROR] {str(e)}")
        sys.stdout.flush()

# ── Main download ───────────────────────────────────────────────────────────────

def download_video(urls, output_path, quality="best", ext="mp4", overwrite=False):
    ffmpeg_available = (
        shutil.which('ffmpeg') is not None or
        os.path.exists(os.path.join(os.path.dirname(__file__), 'ffmpeg.exe')) or
        os.path.exists(os.path.join(os.getcwd(), 'ffmpeg.exe'))
    )

    is_audio = ext.lower() in AUDIO_FORMATS

    # ── Common resilience options (added to all downloads) ──────────────────
    base_resilience = {
        'socket_timeout': 30,
        'retries': 3,
        'fragment_retries': 3,
        'retry_sleep_functions': {
            'http':     lambda n: min(2 ** n, 30),
            'fragment': lambda n: min(2 ** n, 15),
        },
    }

    # ── AUDIO ────────────────────────────────────────────────────────────────
    if is_audio:
        if not ffmpeg_available:
            print("[ERROR] Audio conversion requires FFmpeg. Please install FFmpeg and add it to PATH.")
            sys.stdout.flush()
            sys.exit(1)

        print(f"[STATUS] Downloading best audio for {ext.upper()} conversion...")
        sys.stdout.flush()

        ydl_opts = {
            **base_resilience,
            'format': 'bestaudio/best',
            'outtmpl': os.path.join(output_path, '%(title)s.%(ext)s'),
            'progress_hooks': [progress_hook],
            'postprocessor_hooks': [postprocess_hook],
            'postprocessors': [{
                'key': 'FFmpegExtractAudio',
                'preferredcodec': ext.lower(),
                'preferredquality': '192' if ext.lower() == 'mp3' else '0',
            }],
            'quiet': True,
            'no_warnings': True,
            'continuedl': not overwrite,
            'overwrites': overwrite,
        }

    # ── VIDEO ────────────────────────────────────────────────────────────────
    else:
        if not ffmpeg_available:
            print("[WARNING] FFmpeg not found. Downloading single-file best quality.")
            sys.stdout.flush()
            format_str = 'best'
            merge_fmt  = None
        else:
            quality_map = {
                "2160": 2160, "1440": 1440, "1080": 1080,
                "720":   720, "480":   480, "360":   360,
                "240":   240, "144":   144,
            }
            if quality in quality_map:
                h = quality_map[quality]
                # Multi-level fallback — prevents "Requested format not available"
                format_str = (
                    f'bestvideo[height<={h}][ext=mp4]+bestaudio[ext=m4a]/'
                    f'bestvideo[height<={h}]+bestaudio/'
                    f'best[height<={h}]/'
                    f'bestvideo+bestaudio/'
                    f'best'
                )
                print(f"[STATUS] Target quality: {h}p (picks closest if exact not found)")
                sys.stdout.flush()
            else:
                format_str = 'bestvideo[ext=mp4]+bestaudio[ext=m4a]/bestvideo+bestaudio/best'

            merge_fmt = ext.lower()

            if ext.lower() in ('avi', 'wmv'):
                print(f"[STATUS] Will convert to {ext.upper()} after download...")
                sys.stdout.flush()

        postprocessors = []
        if ffmpeg_available and ext.lower() in ('avi', 'wmv'):
            postprocessors.append({
                'key': 'FFmpegVideoConvertor',
                'preferedformat': ext.lower(),
            })

        ydl_opts = {
            **base_resilience,
            'format': format_str,
            'outtmpl': os.path.join(output_path, '%(title)s.%(ext)s'),
            'progress_hooks': [progress_hook],
            'postprocessor_hooks': [postprocess_hook],
            'quiet': True,
            'no_warnings': True,
            'continuedl': not overwrite,
            'overwrites': overwrite,
        }
        if ffmpeg_available:
            ydl_opts['merge_output_format'] = merge_fmt
        if postprocessors:
            ydl_opts['postprocessors'] = postprocessors

    # ── Execute with reconnect loop ──────────────────────────────────────────
    success = _run_ydl(urls, ydl_opts)
    sys.exit(0 if success else 1)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Multi-platform Video Downloader Engine")
    parser.add_argument("urls",            nargs="+",      help="URL(s) of the video(s) to download")
    parser.add_argument("--output",  "-o", default=".",    help="Output directory")
    parser.add_argument("--quality", "-q", default="best", help="Quality: best/2160/1440/1080/720/480/360/240/144")
    parser.add_argument("--format",  "-f", default="mp4",  help="Format: mp4/webm/avi/wmv/mp3/m4a/wav")
    parser.add_argument("--check-only",    action="store_true", help="Only check filename, do not download")
    parser.add_argument("--overwrite",     action="store_true", help="Overwrite existing file")

    args = parser.parse_args()

    # YouTube-only restriction check
    if YOUTUBE_ONLY:
        for url in args.urls:
            if 'youtube.com' not in url.lower() and 'youtu.be' not in url.lower():
                print("يجب عليك شراء النسخة الكاملة من البرنامج لتستفيد من جميع مميزات نظام باكمله")
                sys.exit(1)

    if args.check_only:
        # Check only first URL if multiple provided for filecheck
        check_existing(args.urls[0], args.output, args.format)
    else:
        download_video(args.urls, args.output, args.quality, args.format, args.overwrite)
