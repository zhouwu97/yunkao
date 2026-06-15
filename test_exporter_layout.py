import unittest

from modules.exporter import (
    MAX_IMAGE_HEIGHT_PT,
    MAX_IMAGE_WIDTH_PT,
    calculate_contained_image_size,
)


class ExporterLayoutTests(unittest.TestCase):
    def test_large_image_is_constrained_to_page(self):
        width, height = calculate_contained_image_size(962, 598, MAX_IMAGE_WIDTH_PT)
        self.assertLessEqual(width, MAX_IMAGE_WIDTH_PT)
        self.assertLessEqual(height, MAX_IMAGE_HEIGHT_PT + 1e-6)
        self.assertAlmostEqual(width / height, 962 / 598, places=3)

    def test_tall_image_is_constrained_without_distortion(self):
        width, height = calculate_contained_image_size(600, 1600)
        self.assertLessEqual(width, MAX_IMAGE_WIDTH_PT)
        self.assertLessEqual(height, MAX_IMAGE_HEIGHT_PT + 1e-6)
        self.assertAlmostEqual(width / height, 600 / 1600, places=3)


if __name__ == "__main__":
    unittest.main()
