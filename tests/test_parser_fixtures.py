"""真实活动题容器的解析回归用例。"""

from __future__ import annotations

from pathlib import Path
import unittest

from modules.question_parser import parse_active_question


FIXTURES = Path(__file__).with_name("fixtures")


class ParserFixtureTests(unittest.TestCase):
    def parse(self, name: str, base_url: str = "https://www.cctrcloud.net/practice/") -> dict:
        question = parse_active_question(
            (FIXTURES / name).read_text(encoding="utf-8"),
            base_url,
        )
        self.assertIsNotNone(question)
        return question

    def test_single_choice_fixture(self):
        question = self.parse("single.html")
        self.assertEqual(question["question_id"], "single-001")
        self.assertEqual(question["page_info"], "1/6")
        self.assertEqual(question["answer"], "A")
        self.assertEqual(question["options"], ["A. Content-Length", "B. 标准输出日志"])

    def test_multiple_choice_fixture(self):
        question = self.parse("multiple.html")
        self.assertEqual(question["question_type"], "多选题")
        self.assertEqual(question["answer"], "AB")

    def test_judgment_fixture(self):
        question = self.parse("judgment.html")
        self.assertEqual(question["question_type"], "判断题")
        self.assertEqual(question["answer"], "对")

    def test_image_fixture_resolves_relative_urls(self):
        question = self.parse("image.html")
        self.assertIn("![img]<https://www.cctrcloud.net/assets/icon.png|w:48px|h:24px>", question["title"])
        self.assertEqual(question["options"][0], "A. ![img]<https://www.cctrcloud.net/practice/images/a.png>工作台")

    def test_mathjax_fixture_preserves_tex_source(self):
        question = self.parse("mathjax.html")
        self.assertIn("x^2 + y^2", question["title"])
        self.assertNotIn("渲染预览", question["title"])
        self.assertEqual(question["answer"], "x^2 + y^2")

    def test_long_subjective_fixture_keeps_full_content(self):
        question = self.parse("long.html")
        self.assertEqual(question["question_type"], "简答题")
        self.assertGreater(len(question["title"]), 90)
        self.assertIn("导出使用独立快照", question["answer"])
        self.assertIn("长题干不得被截断", question["analysis"])


if __name__ == "__main__":
    unittest.main()
