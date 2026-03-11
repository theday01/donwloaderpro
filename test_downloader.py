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

if __name__ == '__main__':
    unittest.main()
