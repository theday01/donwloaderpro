import unittest
import subprocess
import os

class TestDownloader(unittest.TestCase):
    def test_help_command(self):
        result = subprocess.run(['python3', 'downloader.py', '--help'], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0)
        self.assertIn("Multi-platform Video Downloader Engine", result.stdout)

    def test_invalid_url(self):
        # We expect it to fail gracefully with an error message
        result = subprocess.run(['python3', 'downloader.py', 'not-a-url'], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("[ERROR]", result.stdout)

    def test_youtube_restriction(self):
        # Test that non-YouTube URLs are blocked with the specific Arabic message
        result = subprocess.run(['python3', 'downloader.py', 'https://vimeo.com/123456'], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("يجب عليك شراء النسخة الكاملة من البرنامج لتستفيد من جميع مميزات نظام باكمله", result.stdout)

    def test_youtube_allowed(self):
        # Test that YouTube URLs are NOT blocked by the restriction (they might still fail for other reasons, but not the restriction)
        # Using --check-only to avoid actual download and just see if it passes validation
        result = subprocess.run(['python3', 'downloader.py', 'https://www.youtube.com/watch?v=dQw4w9WgXcQ', '--check-only'], capture_output=True, text=True)
        # We don't necessarily expect success if the video doesn't exist or no internet,
        # but we expect it NOT to show the "buy full version" message.
        self.assertNotIn("يجب عليك شراء النسخة الكاملة من البرنامج لتستفيد من جميع مميزات نظام باكمله", result.stdout)

if __name__ == '__main__':
    unittest.main()
