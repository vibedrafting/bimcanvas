"""SVG parsing tools for extracting furniture module information"""

import os
import re
from pathlib import Path
from typing import Any
from xml.etree import ElementTree as ET


def list_modules(project_path: str) -> list[dict[str, Any]]:
    """
    List all available furniture modules from the modules directory.

    Args:
        project_path: Root path of the project

    Returns:
        List of module info dictionaries with keys:
        - id: Module filename (without extension)
        - name: Display name
        - file: Full filename
        - width: Module width in mm (if available)
        - height: Module height in mm (if available)
    """
    modules_dir = Path(project_path) / "modules"

    if not modules_dir.exists():
        return []

    modules = []

    for svg_file in modules_dir.glob("*.svg"):
        module_info = parse_svg_metadata(svg_file)
        modules.append(module_info)

    return modules


def parse_svg_metadata(svg_path: Path) -> dict[str, Any]:
    """
    Parse metadata from an SVG file.

    Args:
        svg_path: Path to the SVG file

    Returns:
        Dictionary with module metadata
    """
    module_id = svg_path.stem
    module_name = svg_path.stem.replace("_", " ").title()

    result = {
        "id": module_id,
        "name": module_name,
        "file": svg_path.name,
        "width": None,
        "height": None,
    }

    try:
        tree = ET.parse(svg_path)
        root = tree.getroot()

        # Extract dimensions from viewBox or width/height attributes
        viewbox = root.get("viewBox")
        if viewbox:
            parts = viewbox.split()
            if len(parts) >= 4:
                result["width"] = float(parts[2])
                result["height"] = float(parts[3])

        # Try width/height attributes as fallback
        if result["width"] is None:
            width_attr = root.get("width")
            if width_attr:
                result["width"] = parse_dimension(width_attr)

        if result["height"] is None:
            height_attr = root.get("height")
            if height_attr:
                result["height"] = parse_dimension(height_attr)

    except ET.ParseError:
        # If SVG parsing fails, just return basic info
        pass

    return result


def parse_dimension(value: str) -> float | None:
    """
    Parse a dimension value (e.g., "100mm", "50px", "25").

    Args:
        value: Dimension string

    Returns:
        Numeric value (assumes mm if no unit), or None if unparseable
    """
    if not value:
        return None

    # Remove units and extract numeric value
    match = re.match(r"^([\d.]+)", value.strip())
    if match:
        return float(match.group(1))

    return None
