import time


class SessionRecorder:
    """Keeps an ordered record of what the session actually did - which rhythm was playing
    when, and which media were on screen while it played.

    Separate from ScoreTracker on purpose: that one aggregates numbers (counters, averages,
    personal records, the persisted history), and an ordered recording is not a score. It
    also means the file ScoreTracker writes to disk cannot pick any of this up.

    **Nothing here is ever logged, and nothing is persisted on its own.** The timeline holds
    media file paths, which is exactly the thing this app promises to keep on the user's
    machine and out of its own data directory - see the logging rules in CLAUDE.md. It lives
    for one session, feeds the Session Explorer, and is dropped when the next session starts
    or the app closes.

    The single exception is the user pressing Save in the Explorer: src/session_files.py
    then turns this timeline into a saved session, paths and all, so it can be replayed
    later. That is a deliberate, disclosed, deletable exception (Help > Privacy & Data) and
    it is the only route out of here - this class still writes nothing itself.

    A plain class rather than a QObject: it emits nothing, and Qt happily connects a signal
    to any callable, the way ScoreTracker.beat is already connected.

    Every method takes an optional `at` so the whole thing can be tested by handing it
    timestamps instead of sleeping.
    """

    def __init__(self):
        self._started_at = None
        self._ended_at = None
        self._segments = []  # (start_time, Segment)
        self._media = []  # (start_time, path)
        self._climax_at = None
        self._climax_outcome = None
        self._fake_climaxes = []

    # --- recording ---

    def session_started(self, at=None):
        self._started_at = time.time() if at is None else at
        self._ended_at = None
        self._segments = []
        self._media = []
        self._climax_at = None
        self._climax_outcome = None
        self._fake_climaxes = []

    def session_ended(self, at=None):
        self._ended_at = time.time() if at is None else at

    def segment_started(self, segment, at=None):
        self._segments.append((time.time() if at is None else at, segment))

    def media_shown(self, path, at=None):
        self._media.append((time.time() if at is None else at, str(path)))

    def fake_climax_recorded(self, at=None):
        self._fake_climaxes.append(time.time() if at is None else at)

    def climax_recorded(self, outcome, at=None):
        self._climax_at = time.time() if at is None else at
        self._climax_outcome = outcome

    # --- reading it back ---

    def timeline(self, now=None):
        """The recording, stitched into spans the Session Explorer can render.

        Each segment runs until the next one starts, the last until the session ended.
        Media are placed into *every* segment they overlap rather than only the one they
        started in: a picture that stayed up across a rhythm change really was on screen
        for both, and dropping it from the second would hide the very one someone is
        scrolling back to find. The later appearances are flagged `carried_over` so they
        read as a continuation instead of a duplicate.
        """
        end_of_session = self._session_end(now)
        media_spans = self._media_spans(end_of_session)

        segments = []
        for index, (start, segment) in enumerate(self._segments):
            is_last = index == len(self._segments) - 1
            end = end_of_session if is_last else self._segments[index + 1][0]
            segments.append(
                {
                    "kind": segment.kind,
                    "pattern": segment.pattern_name,
                    "freq": segment.freq,
                    "start": start,
                    "end": end,
                    # What the planner asked for, next to what it measured. They differ:
                    # a segment ends at the first note *past* its planned end, and the
                    # final one is held open until the session stops. Saving a session
                    # replays the planned figure - see src/session_files.py.
                    "planned_sec": segment.duration_sec,
                    "media": self._media_within(media_spans, start, end, is_first=index == 0),
                }
            )

        return {
            "started_at": self._started_at,
            "ended_at": end_of_session,
            "climax_at": self._climax_at,
            "climax_outcome": self._climax_outcome,
            "fake_climaxes": list(self._fake_climaxes),
            "segments": segments,
        }

    def _session_end(self, now):
        if self._ended_at is not None:
            return self._ended_at
        return time.time() if now is None else now

    def _media_spans(self, end_of_session):
        """(start, end, path) per medium - each runs until the next one is shown."""
        spans = []
        for index, (start, path) in enumerate(self._media):
            is_last = index == len(self._media) - 1
            end = end_of_session if is_last else self._media[index + 1][0]
            spans.append((start, end, path))
        return spans

    @staticmethod
    def _media_within(spans, segment_start, segment_end, is_first):
        """Media overlapping [segment_start, segment_end).

        The first segment reaches back before its own start: start() loads the first medium
        before BeatHandler has planned anything, so that medium would otherwise belong to
        no segment at all.
        """
        lower_bound = float("-inf") if is_first else segment_start
        inside = []
        for start, end, path in spans:
            if end <= segment_start or start >= segment_end:
                continue
            inside.append(
                {
                    "path": path,
                    "start": start,
                    "end": end,
                    "carried_over": start < lower_bound,
                }
            )
        return inside
