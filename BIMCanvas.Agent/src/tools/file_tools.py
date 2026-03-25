"""File operation tools for reading and writing JSON files"""

import json
import os
from pathlib import Path
from typing import Any


def read_json(project_path: str, relative_path: str) -> dict | list | None:
    """
    Read a JSON file from the project.

    Args:
        project_path: Root path of the project
        relative_path: Relative path to the JSON file

    Returns:
        Parsed JSON data, or None if file doesn't exist
    """
    file_path = Path(project_path) / relative_path

    if not file_path.exists():
        return None

    try:
        with open(file_path, "r", encoding="utf-8-sig") as f:
            return json.load(f)
    except json.JSONDecodeError as e:
        raise ValueError(f"Invalid JSON in {file_path}: {e}")


def write_json(project_path: str, relative_path: str, data: Any) -> None:
    """
    Write data to a JSON file in the project.

    Args:
        project_path: Root path of the project
        relative_path: Relative path to the JSON file
        data: Data to write (must be JSON serializable)
    """
    file_path = Path(project_path) / relative_path

    # Ensure parent directory exists
    file_path.parent.mkdir(parents=True, exist_ok=True)

    with open(file_path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def file_exists(project_path: str, relative_path: str) -> bool:
    """
    Check if a file exists in the project.

    Args:
        project_path: Root path of the project
        relative_path: Relative path to the file

    Returns:
        True if the file exists
    """
    file_path = Path(project_path) / relative_path
    return file_path.exists()
