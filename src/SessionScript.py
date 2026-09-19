from src.BeatHandler import Segment


class SessionScript:
    """A saved session, handed out to the parts that would otherwise be drawing dice.

    Replaying is not a second engine. BeatHandler already reads its next segment off a
    plan, ClimaxHandler already places the climax on a clock, and GoonerApp already asks
    one method for the next media gap - a replay just answers those three questions from
    the file instead of from random.

    Consumed in order and never rewound: each consumer pops what it needs as the session
    runs, and when the script is spent the live behaviour takes over again.
    """

    def __init__(self, saved, ignore_paths=False):
        self._segments = list(saved.get("segments", []))
        self._media = list(saved.get("media", []))
        self._duration = saved.get("duration_sec") or 0.0
        self._ignore_paths = ignore_paths
        self.custom_patterns = dict(saved.get("custom_patterns") or {})

        climax = saved.get("climax") or {}
        self.climax_offset = climax.get("at_sec")
        self.climax_outcome = climax.get("outcome")
        self.fake_offsets = list(saved.get("fake_climaxes", []))

        self._segment_pos = 0
        # Two cursors, not one: own-library mode consumes the gaps and ignores the paths,
        # so they have to advance independently.
        self._media_pos = 0
        self._path_pos = 0

    # --- segments ---

    @property
    def has_segments_left(self) -> bool:
        return self._segment_pos < len(self._segments)

    @property
    def segment_count(self) -> int:
        """How many segments were recorded, cursor or no cursor.

        BeatHandler numbers a session's segments from 0 and hands the script those indices,
        so anything numbered at or above this was drawn rather than replayed - which is how
        the climax tells the recorded stretch from the improvised tail behind it.
        """
        return len(self._segments)

    @property
    def duration(self) -> float:
        """How long the recorded session ran."""
        return self._duration

    def next_segment(self, index):
        """The next recorded segment as a real Segment, or None once the script is spent.

        The index comes from BeatHandler's own monotonic counter rather than the file: a
        replay may be the second session of the run, and the counter has to keep climbing.
        """
        if not self.has_segments_left:
            return None
        data = self._segments[self._segment_pos]
        self._segment_pos += 1
        return Segment(
            data["kind"],
            data["duration_sec"],
            data.get("freq"),
            data.get("pattern"),
            index,
        )

    # --- media ---

    @property
    def has_media_paths(self) -> bool:
        if self._ignore_paths:
            return False
        return any("path" in entry for entry in self._media)

    def next_media_gap(self):
        """Seconds the medium now on screen should stay up, or None once spent.

        The last medium runs to the end of the recorded session. Gaps are handed out even
        when the paths are being ignored: the pacing is part of what was saved, and it is
        most of what "the same session" means when the pictures are someone else's.
        """
        if self._media_pos >= len(self._media):
            return None
        current = self._media[self._media_pos]["at_sec"]
        following = self._media_pos + 1
        end = self._media[following]["at_sec"] if following < len(self._media) else self._duration
        self._media_pos += 1
        return max(0.0, end - current)

    def next_media_path(self):
        """The path of the next recorded medium, or None when there is none to give.

        Skips entries that carry no path at all, which is what a partially stripped file
        would look like.
        """
        if self._ignore_paths:
            return None
        while self._path_pos < len(self._media):
            entry = self._media[self._path_pos]
            self._path_pos += 1
            if "path" in entry:
                return entry["path"]
        return None
