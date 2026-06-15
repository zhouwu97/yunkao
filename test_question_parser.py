import unittest

from modules.question_parser import parse_active_question


class QuestionParserTests(unittest.TestCase):
    def test_judgment_answer_is_normalized_to_readable_text(self):
        html = """
        <div class="swiper-slide swiper-slide-active">
          <div class="practice_slide_content" data-questionid="42">
            <div class="practice_slide_title clearfix">
              <b class="type">判断题</b>
              <span class="title">UDP 没有拥塞控制。</span>
            </div>
            <ul class="option_content">
              <li><div class="txt">对</div></li>
              <li><div class="txt">错</div></li>
            </ul>
            <div class="practice_analysis">
              <div class="answer">正确答案：<span class="answer-text">B</span></div>
              <div class="analysis-content"><div class="desc">判断题示例解析</div></div>
            </div>
          </div>
          <span class="swiper-pagination-current">12</span>
          <span id="swiper-total">186</span>
        </div>
        """

        question = parse_active_question(html)
        self.assertIsNotNone(question)
        self.assertEqual(question["answer"], "错")
        self.assertEqual(question["question_id"], "42")
        self.assertEqual(question["page_info"], "12/186")

    def test_fill_question_does_not_generate_fake_choice_options(self):
        html = """
        <div class="swiper-slide swiper-slide-active">
          <div class="practice_slide_content" data-questionid="121">
            <div class="practice_slide_title clearfix">
              <b class="type">填空题</b>
              <span class="title">若某通信链路的数据传输速率为2400 b/s，采用4相位调制，则该链路的波特率是（ ）Baud？。</span>
            </div>
            <ul class="option_content">
              <li><div class="txt">空1：略</div></li>
              <li><div class="txt">空2：略</div></li>
            </ul>
            <div class="practice_analysis">
              <div class="answer">正确答案：<span class="answer-text">略</span></div>
            </div>
          </div>
        </div>
        """

        question = parse_active_question(html)
        self.assertIsNotNone(question)
        self.assertEqual(question["question_type"], "填空题")
        self.assertEqual(question["options"], [])
        self.assertNotIn("answer", question)


if __name__ == "__main__":
    unittest.main()
