import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('catalog', Path(__file__).resolve().parents[1] / 'scripts/update_catalog.py')
catalog = importlib.util.module_from_spec(spec)
spec.loader.exec_module(catalog)

def plugin(*versions):
    return [{'guid': 'test', 'versions': [{'version': v, 'sourceUrl': v} for v in versions]}]

class CatalogTests(unittest.TestCase):
    def test_preserves_versions_and_numeric_order(self):
        result = catalog.merge_catalog(plugin('0.1.9.0'), plugin('0.1.10.0'))
        self.assertEqual([v['version'] for v in result[0]['versions']], ['0.1.10.0', '0.1.9.0'])

    def test_delayed_old_release_cannot_downgrade_newest(self):
        result = catalog.merge_catalog(plugin('0.1.4.0'), plugin('0.1.3.0'))
        self.assertEqual(result[0]['versions'][0]['version'], '0.1.4.0')

    def test_repeat_is_idempotent(self):
        value = plugin('0.1.3.0')
        self.assertEqual(catalog.merge_catalog(value, value), value)

    def test_empty_or_bad_version_rejected(self):
        for value in [[], plugin(), plugin('latest')]:
            with self.assertRaises(ValueError):
                catalog.merge_catalog([], value)

if __name__ == '__main__':
    unittest.main()
