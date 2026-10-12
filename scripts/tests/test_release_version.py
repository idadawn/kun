import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile


SCRIPT = Path(__file__).resolve().parents[1] / "release_version.py"
SPEC = importlib.util.spec_from_file_location("release_version", SCRIPT)
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)


class ReleaseVersionTests(unittest.TestCase):
    def test_channels_and_calendar(self):
        self.assertEqual("stable", release.release_info("2026.10.12")["kind"])
        self.assertEqual({"version": "2026.10.12-hotfix.2", "kind": "hotfix", "prerelease": False}, release.release_info("v2026.10.12-hotfix.2", tag=True))
        self.assertTrue(release.release_info("2026.10.12-beta.1")["prerelease"])
        self.assertEqual("2028.2.29", release.release_info("2028.2.29")["version"])
        for invalid in ["2026.10.12-2", "2026.10.12-02", "2026.10.12-hotfix.02", "2026.10.12-hotfix.0", "2026.02.1", "2026.2.29", "2026.13.1", "2026.10.12\nversion=bad", "1.2.3", "v2026.10.12"]:
            with self.subTest(invalid=invalid), self.assertRaises(ValueError):
                release.release_info(invalid)

    @staticmethod
    def package(root, name, version, dependencies="", suffix=".nupkg"):
        with zipfile.ZipFile(root / (name + suffix), "w") as archive:
            archive.writestr(name + ".nuspec", f'<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>{name}</id><version>{version}</version><authors>Kun</authors><description>Fixture</description><dependencies><group targetFramework="net10.0">{dependencies}</group></dependencies></metadata></package>')

    def test_complete_cohort_and_symbols(self):
        version = "2026.10.12-hotfix.2"
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            self.package(root, "Kun.Core", version)
            self.package(root, "Kun.Core", version, suffix=".snupkg")
            self.package(root, "Kun.Provider", version, f'<dependency id="kun.core" version="[{version}, )" /><dependency id="Microsoft.Other" version="10.0.0" />')
            self.assertEqual(2, release.validate_artifacts(root, version))

    def test_version_drift_missing_dependency_and_empty_output_are_rejected(self):
        version = "2026.10.12-hotfix.2"
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with self.assertRaises(ValueError):
                release.validate_artifacts(root, version)
            self.package(root, "Kun.Core", "2026.10.12-2")
            with self.assertRaisesRegex(ValueError, "包版本"):
                release.validate_artifacts(root, version)
            self.package(root, "Kun.Core", version, '<dependency id="Kun.Missing" version="2026.10.12-hotfix.2" />')
            with self.assertRaisesRegex(ValueError, "缺少本次发行物"):
                release.validate_artifacts(root, version)
            self.package(root, "Kun.Core", version, '<dependency id="Kun.Core" version="2026.10.12-2" />')
            with self.assertRaisesRegex(ValueError, "未锁步"):
                release.validate_artifacts(root, version)

    def test_cli_outputs_safe_github_values_and_rejects_invalid_tag(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / "output"
            result = subprocess.run([sys.executable, str(SCRIPT), "--tag", "v2026.10.12-hotfix.2", "--github-output", str(output)], capture_output=True, text=True, check=True)
            self.assertFalse(json.loads(result.stdout)["prerelease"])
            before = output.read_text()
            self.assertEqual("version=2026.10.12-hotfix.2\nprerelease=false\n", before)
            failed = subprocess.run([sys.executable, str(SCRIPT), "--tag", "v2026.10.12-2", "--github-output", str(output)], capture_output=True, text=True)
            self.assertNotEqual(0, failed.returncode)
            self.assertEqual(before, output.read_text())


if __name__ == "__main__":
    unittest.main()
