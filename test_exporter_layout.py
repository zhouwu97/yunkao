import unittest

from modules.exporter import (
    MAX_IMAGE_HEIGHT_PT,
    MAX_IMAGE_WIDTH_PT,
    calculate_contained_point_size,
    calculate_contained_image_size,
    normalize_export_text_run,
    should_add_space_before_inline_image,
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

    def test_svg_point_size_is_constrained_without_distortion(self):
        width, height = calculate_contained_point_size(900, 300)
        self.assertLessEqual(width, MAX_IMAGE_WIDTH_PT)
        self.assertLessEqual(height, MAX_IMAGE_HEIGHT_PT + 1e-6)
        self.assertAlmostEqual(width / height, 3, places=3)

    def test_text_normalization_keeps_formula_spacing(self):
        self.assertEqual(
            normalize_export_text_run("z < 0.5\u00a0f(n)"),
            "z < 0.5 f(n)",
        )
        self.assertEqual(
            normalize_export_text_run("公式\u00a0\u00a0\u00a0(3分)"),
            "公式 (3分)",
        )

    def test_inline_math_gets_separator_after_numeric_text(self):
        self.assertTrue(should_add_space_before_inline_image("z < 0.5"))
        self.assertFalse(should_add_space_before_inline_image("信号"))
        self.assertFalse(should_add_space_before_inline_image("0.5 "))


if __name__ == "__main__":
    unittest.main()
