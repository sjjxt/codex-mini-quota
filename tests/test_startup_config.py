import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import tomllib
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('startup', ROOT / 'enable-client-startup.py')
startup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(startup)


class StartupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.home = self.root / 'custom-codex'
        self.home.mkdir()
        self.config = self.home / 'config.toml'
        self.config.write_text('model = "test"\n', encoding='utf-8')
        self.env = patch.dict(os.environ, {'CODEX_HOME': str(self.home), 'LOCALAPPDATA': str(self.root / 'local')})
        self.env.start()

    def tearDown(self):
        self.env.stop()
        self.temp.cleanup()

    def run_action(self, action):
        with contextlib.redirect_stdout(io.StringIO()):
            startup.manage(action)

    def flag(self):
        return startup.read_config(self.config)[2]

    def test_custom_home(self):
        self.assertEqual(startup.config_path(), self.config)

    def test_marketplace_metadata_has_no_writes(self):
        self.config.write_text('[marketplaces.mini-quota-local]\nsource_type="local"\nsource="missing-folder"\n', encoding='utf-8')
        raw = self.config.read_bytes()
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            startup.manage('marketplace')
        self.assertEqual(json.loads(output.getvalue())['root'], 'missing-folder')
        self.assertEqual(raw, self.config.read_bytes())
        self.assertFalse(startup.state_path(self.config).exists())

    def test_marketplace_nonlocal_source_is_preserved(self):
        self.config.write_text('[marketplaces.mini-quota-local]\nsource_type="git"\nsource="example/repo"\n', encoding='utf-8')
        raw = self.config.read_bytes()
        with self.assertRaises(ValueError):
            self.run_action('marketplace')
        self.assertEqual(raw, self.config.read_bytes())

    def test_preflight_has_no_writes(self):
        raw = self.config.read_bytes()
        self.run_action('check')
        self.assertEqual(raw, self.config.read_bytes())
        self.assertFalse(startup.state_path(self.config).exists())

    def test_missing_config_fails_before_write(self):
        self.config.unlink()
        with self.assertRaises(FileNotFoundError):
            self.run_action('check')

    def test_enable_restore_absent(self):
        self.run_action('enable')
        self.assertIs(self.flag(), True)
        self.run_action('restore')
        self.assertIsNone(self.flag())
        self.assertEqual(tomllib.loads(self.config.read_text())['model'], 'test')

    def test_false_restore_and_repeated_install(self):
        self.config.write_text('[desktop]\nmcp_startup_authentication = false # keep me\n', encoding='utf-8')
        self.run_action('enable')
        self.run_action('enable')
        self.run_action('restore')
        self.assertIs(self.flag(), False)
        self.assertIn('# keep me', self.config.read_text())

    def test_true_stays_true(self):
        self.config.write_text('[desktop]\nmcp_startup_authentication = true\n', encoding='utf-8')
        self.run_action('enable')
        self.run_action('restore')
        self.assertIs(self.flag(), True)

    def test_user_edit_is_preserved(self):
        self.run_action('enable')
        self.config.write_text('[desktop]\nmcp_startup_authentication = false\n', encoding='utf-8')
        raw = self.config.read_bytes()
        self.run_action('restore')
        self.assertEqual(raw, self.config.read_bytes())

    def test_bom_crlf_and_other_tables_preserved(self):
        self.config.write_bytes(b'\xef\xbb\xbf[desktop]\r\nmcp_startup_authentication = false\r\n[other]\r\nvalue = "keep"\r\n')
        original = self.config.read_bytes()
        self.run_action('enable')
        self.assertEqual(original.replace(b'= false', b'= true'), self.config.read_bytes())
        self.run_action('restore')
        self.assertEqual(original, self.config.read_bytes())

    def test_supported_toml_layouts(self):
        for text in ('desktop.mcp_startup_authentication = false\n', '[desktop]\n', '[desktop]', '[ "desktop" ]\n "mcp_startup_authentication" = false\n', '[desktop]\n[other]\na=1\n'):
            with self.subTest(text=text):
                changed = startup.change_flag(text, True)
                self.assertIs(tomllib.loads(changed)['desktop']['mcp_startup_authentication'], True)

    def test_invalid_config_not_written(self):
        self.config.write_text('[desktop\n', encoding='utf-8')
        original = self.config.read_bytes()
        with self.assertRaises(tomllib.TOMLDecodeError):
            self.run_action('check')
        self.assertEqual(original, self.config.read_bytes())

    def test_unsupported_inline_change_does_not_write(self):
        self.config.write_text('desktop = { mcp_startup_authentication = false }\n', encoding='utf-8')
        original = self.config.read_bytes()
        with self.assertRaises((ValueError, tomllib.TOMLDecodeError)):
            self.run_action('check')
        self.assertEqual(original, self.config.read_bytes())

    def test_concurrent_edit_is_preserved(self):
        original = self.config.read_bytes()
        self.config.write_text('model="edited"\n', encoding='utf-8')
        with self.assertRaises(RuntimeError):
            startup.atomic_write(self.config, b'changed', expected=original)
        self.assertEqual(self.config.read_text(), 'model="edited"\n')
        self.assertFalse(list(self.home.glob('.config.toml.*')))


if __name__ == '__main__':
    unittest.main()
