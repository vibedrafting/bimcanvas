"""Tools module"""

from .file_tools import read_json, write_json, file_exists
from .svg_parser import list_modules as list_svg_modules
from .zone_tools import (
    get_zone,
    get_all_zones,
    list_modules_by_zone,
    get_exclusions,
    list_modules,
    get_module_info
)
from .placement_tools import (
    write_modules,
    validate_module_data,
    check_overlap_simple,
    check_point_in_polygon,
    create_module_bounds,
    load_existing_modules,
    PlacedModule
)

__all__ = [
    # file_tools
    "read_json",
    "write_json",
    "file_exists",
    # svg_parser
    "list_svg_modules",
    # zone_tools
    "get_zone",
    "get_all_zones",
    "list_modules_by_zone",
    "get_exclusions",
    "list_modules",
    "get_module_info",
    # placement_tools
    "write_modules",
    "validate_module_data",
    "check_overlap_simple",
    "check_point_in_polygon",
    "create_module_bounds",
    "load_existing_modules",
    "PlacedModule"
]
