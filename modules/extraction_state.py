class ExtractionRunState:
    """Tracks one active extraction run and rejects callbacks from older runs."""

    def __init__(self):
        self._generation = 0
        self._active_run_id = None

    @property
    def active_run_id(self):
        return self._active_run_id

    @property
    def is_active(self):
        return self._active_run_id is not None

    def start(self):
        self._generation += 1
        self._active_run_id = self._generation
        return self._active_run_id

    def matches(self, run_id):
        return self.is_active and run_id == self._active_run_id

    def stop(self, run_id=None):
        if run_id is not None and not self.matches(run_id):
            return False
        if not self.is_active:
            return False
        self._active_run_id = None
        return True
