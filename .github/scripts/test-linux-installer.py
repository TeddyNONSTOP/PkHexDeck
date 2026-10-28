#!/usr/bin/env python3
"""Execute the production installer in disposable homes with deterministic release responses."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest

from gi.repository import Gio

INSTALLER = Path(__file__).resolve().parents[2] / "Scripts/install.sh"
PAYLOAD = b'#!/bin/sh\nprintf started > "$0.launched"\n'


class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix="pkhex-installer-test-")
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        curl = self.bin / "curl"
        curl.write_text('''#!/usr/bin/python3
import os, pathlib, sys
args = sys.argv[1:]
target = pathlib.Path(args[args.index('-o') + 1])
url = next(a for a in args if a.startswith('https://'))
if 'api.github.com' in url:
    target.write_bytes(pathlib.Path(os.environ['TEST_METADATA']).read_bytes())
elif 'releases/download' in url:
    target.write_bytes(pathlib.Path(os.environ['TEST_PAYLOAD']).read_bytes())
else:
    target.write_bytes(b'icon fixture')
''')
        curl.chmod(0o755)
        self.payload = self.root / "payload"
        self.payload.write_bytes(PAYLOAD)
        self.metadata = self.root / "release.json"
        self.release = {
            "tag_name": "v1.88.0",
            "assets": [{"name": "PKHeX-Avalonia-1.88.0-x86_64.AppImage",
                        "digest": "sha256:" + hashlib.sha256(PAYLOAD).hexdigest(), "size": len(PAYLOAD)}],
        }

    def run_installer(self, home, *args):
        # Compact JSON deliberately differs from GitHub's current presentation format.
        self.metadata.write_text(json.dumps(self.release))
        env = {**os.environ, "HOME": str(home), "XDG_DATA_HOME": str(home / "data"),
               "PATH": str(self.bin) + ":" + os.environ["PATH"], "NO_COLOR": "1",
               "TEST_METADATA": str(self.metadata), "TEST_PAYLOAD": str(self.payload)}
        return subprocess.run(["bash", str(INSTALLER), *args], env=env, text=True,
                              capture_output=True, timeout=30)

    @staticmethod
    def installed(home):
        return home / ".local/opt/PKHeX-Avalonia/PKHeX-Avalonia.AppImage"

    def test_install_launch_and_uninstall_special_paths(self):
        for name in ("normal", "space name", "percent%name", "dollar$name", "back\\slash", 'quote"name', "tick`name", "apostrophe'name"):
            with self.subTest(name=name):
                home = self.root / name
                result = self.run_installer(home)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertNotIn("\x1b", result.stdout)
                app = self.installed(home)
                self.assertEqual(PAYLOAD, app.read_bytes())
                self.assertTrue(os.access(app, os.X_OK))
                entry = home / "data/applications/io.pkhex.avalonia.desktop"
                subprocess.run(["desktop-file-validate", str(entry)], check=True, capture_output=True)
                desktop = Gio.DesktopAppInfo.new_from_filename(str(entry))
                self.assertTrue(desktop.launch([], None))
                marker = Path(str(app) + ".launched")
                for _ in range(100):
                    if marker.exists() and marker.read_text() == "started":
                        break
                    time.sleep(.02)
                self.assertEqual("started", marker.read_text())
                settings = home / "data/PKHeX-Avalonia/settings.json"
                settings.parent.mkdir()
                settings.write_text("preserve")
                result = self.run_installer(home, "--uninstall")
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertFalse(app.exists())
                self.assertFalse(entry.exists())
                self.assertEqual("preserve", settings.read_text())

    def test_bad_or_absent_checksum_preserves_existing_installation(self):
        home = self.root / "home"
        app = self.installed(home)
        app.parent.mkdir(parents=True)
        app.write_bytes(b"original")
        for digest in ("sha256:" + "0" * 64, None):
            with self.subTest(digest=digest):
                self.release["assets"][0]["digest"] = digest
                result = self.run_installer(home)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("checksum" if digest else "verified", result.stderr.lower())
                self.assertEqual(b"original", app.read_bytes())

    def test_failed_copy_preserves_existing_installation(self):
        home = self.root / "home"
        app = self.installed(home)
        app.parent.mkdir(parents=True)
        app.write_bytes(b"original")
        command = self.bin / "cp"
        command.write_text("#!/bin/sh\nexit 1\n")
        command.chmod(0o755)
        result = self.run_installer(home)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("Installing to", result.stdout)
        self.assertEqual(b"original", app.read_bytes())
        self.assertEqual([app], list(app.parent.iterdir()))

    def test_directory_destination_is_not_modified(self):
        home = self.root / "home"
        app = self.installed(home)
        app.mkdir(parents=True)
        result = self.run_installer(home)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("directory or symlink", result.stderr)
        self.assertEqual([], list(app.iterdir()))

    def test_uninstall_does_not_need_network_dependencies(self):
        home = self.root / "home"
        app = self.installed(home)
        app.parent.mkdir(parents=True)
        app.write_bytes(b"original")
        (self.bin / "curl").write_text("#!/bin/sh\nexit 99\n")
        result = self.run_installer(home, "--uninstall")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse(app.exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
