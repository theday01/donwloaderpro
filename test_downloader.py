import unittest
import subprocess
import os

class TestDownloader(unittest.TestCase):
    def test_help_command(self):
        result = subprocess.run(['python3', 'downloader.py', '--help'], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0)
        self.assertIn("Multi-platform Video Downloader Engine", result.stdout)

    def test_invalid_url(self):
        # With YOUTUBE_ONLY=True, invalid/non-YT URLs yield the restriction message
        result = subprocess.run(['python3', 'downloader.py', 'not-a-url'], capture_output=True, text=True)
        self.assertNotEqual(result.returncode, 0)
        restriction_msg = "يجب عليك شراء النسخة الكاملة من البرنامج لتستفيد من جميع مميزات نظام باكمله"
        self.assertTrue(restriction_msg in result.stdout or "[ERROR]" in result.stdout)

if __name__ == '__main__':
    unittest.main()
