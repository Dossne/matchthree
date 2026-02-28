#!/usr/bin/env python3
"""Validate Assets/Resources/Levels/level_registry.json structure."""

from __future__ import annotations

import json
import pathlib
import sys
from typing import Any

REGISTRY_PATH = pathlib.Path("Assets/Resources/Levels/level_registry.json")


def fail(message: str) -> None:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def is_int_not_bool(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def main() -> None:
    if not REGISTRY_PATH.is_file():
        fail(f"Missing registry file: {REGISTRY_PATH}")

    try:
        data = json.loads(REGISTRY_PATH.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        fail(f"Invalid JSON in {REGISTRY_PATH}: {exc}")

    if not isinstance(data, dict):
        fail("Top-level JSON value must be an object.")

    levels = data.get("levels")
    if not isinstance(levels, list):
        fail('Top-level key "levels" must exist and be an array.')

    for idx, level in enumerate(levels):
        if not isinstance(level, dict):
            fail(f"levels[{idx}] must be an object.")

        level_path = level.get("levelPath") or level.get("levelResourcePath")
        if not isinstance(level_path, str) or not level_path.strip():
            fail(
                f'levels[{idx}] must include a non-empty "levelPath" '
                'or "levelResourcePath" string.'
            )

        max_moves = level.get("maxMoves")
        if not is_int_not_bool(max_moves):
            fail(f'levels[{idx}].maxMoves must be an integer.')

        goals = level.get("goals")
        if not isinstance(goals, list):
            fail(f'levels[{idx}].goals must be an array.')

    print(f"Registry JSON is valid: {REGISTRY_PATH}")


if __name__ == "__main__":
    main()
