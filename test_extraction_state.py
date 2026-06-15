import unittest

from modules.extraction_state import ExtractionRunState


class ExtractionRunStateTests(unittest.TestCase):
    def test_stale_finish_cannot_stop_new_range(self):
        state = ExtractionRunState()
        first_run = state.start()
        self.assertTrue(state.stop(first_run))

        second_run = state.start()
        self.assertFalse(state.stop(first_run))
        self.assertTrue(state.matches(second_run))

    def test_manual_stop_invalidates_delayed_callbacks(self):
        state = ExtractionRunState()
        run_id = state.start()

        self.assertTrue(state.stop())
        self.assertFalse(state.matches(run_id))
        self.assertFalse(state.is_active)


if __name__ == "__main__":
    unittest.main()
