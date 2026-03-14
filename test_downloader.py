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

    def test_all_platforms_allowed(self):
        # Test that non-YouTube URLs are NOT blocked in the full version
        # Using a URL that might fail extraction but shouldn't show the "buy full version" message
        result = subprocess.run(['python3', 'downloader.py', 'https://vimeo.com/123456', '--check-only'], capture_output=True, text=True)
        self.assertNotIn("يجب عليك شراء النسخة الكاملة من البرنامج لتستفيد من جميع مميزات نظام باكمله", result.stdout)

    def test_youtube_allowed(self):
        # Test that YouTube URLs are allowed
        result = subprocess.run(['python3', 'downloader.py', 'https://www.youtube.com/watch?v=dQw4w9WgXcQ', '--check-only'], capture_output=True, text=True)
        self.assertNotIn("يجب عليك شراء النسخة الكاملة من البرنامج لتستفيد من جميع مميزات نظام باكمله", result.stdout)

if __name__ == '__main__':
    unittest.main()
