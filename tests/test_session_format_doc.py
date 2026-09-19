"""The session format is documented for people (and LLMs) outside this repo to write against.

A spec that has drifted from the code is worse than no spec: it produces files the app
refuses, and the reader has no way to tell which of the two is wrong. These tests are what
keep the two honest.
"""
import re

import pytest

from src.BeatHandler import BeatHandler
from src.session_files import CLIMAX_OUTCOMES, FORMAT_VERSION, SEGMENT_KINDS
from src.utils import get_project_root

SKILL_DIR = get_project_root() / "skills" / "gooner-session-builder"


@pytest.fixture(scope="module")
def spec():
    return (SKILL_DIR / "SESSION_FORMAT.md").read_text(encoding="utf-8")


def test_the_skill_carries_the_spec_it_points_at():
    """The folder is meant to be dropped into somebody's own assistant whole - a SKILL.md
    referring to a file that stayed behind in the repo would be useless there."""
    skill = (SKILL_DIR / "SKILL.md").read_text(encoding="utf-8")

    assert "SESSION_FORMAT.md" in skill
    assert (SKILL_DIR / "SESSION_FORMAT.md").exists()


def test_the_spec_lists_every_built_in_rhythm(spec):
    """Naming a rhythm that does not exist is the mistake this whole document is trying to
    prevent, so the list it gives has to be the real one."""
    for name in BeatHandler.BEAT_PATTERNS_MAP:
        assert name in spec, f"{name} is playable but undocumented"


def test_the_spec_invents_no_rhythms_of_its_own(spec):
    """A name in the document that the app cannot play is worse than an omission - it reads
    as permission."""
    quoted = set(re.findall(r"`([A-Z][A-Za-z0-9/ ]+)`", spec))
    plausible = {name for name in quoted if " " in name and not name.startswith(("Sessions", "What"))}

    assert plausible <= set(BeatHandler.BEAT_PATTERNS_MAP)


def test_the_spec_states_the_current_format_version(spec):
    assert f"`{FORMAT_VERSION}`" in spec


def test_the_spec_names_every_segment_kind_and_outcome(spec):
    for kind in SEGMENT_KINDS:
        assert f'"{kind}"' in spec
    for outcome in CLIMAX_OUTCOMES:
        assert f'"{outcome}"' in spec


def test_the_spec_example_is_a_session_the_app_would_accept(spec):
    """The first fenced block is what a reader copies. If the app refuses it, the document
    has taught them the wrong thing."""
    import json

    from src import session_files

    block = re.search(r"```json\n(\{.*?\n\})\n```", spec, re.DOTALL)
    assert block, "the spec has no complete example object"

    assert session_files.validate_session(json.loads(block.group(1))) == []
