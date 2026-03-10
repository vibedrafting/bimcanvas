"""Zone data access tools for Agent decision making

提供读取 Zone 信息、过滤兼容模块、获取禁区等功能
支持嵌套分区（SubZones）的递归查找和叶子 zone 过滤
"""

from pathlib import Path
from typing import Any
from .file_tools import read_json


def _find_zone_recursive(zones: list[dict], zone_id: str) -> dict | None:
    """在 zone 列表中递归查找（支持 subZones 嵌套）"""
    for zone in zones:
        if zone.get("id") == zone_id:
            return zone
        sub_zones = zone.get("subZones")
        if sub_zones:
            found = _find_zone_recursive(sub_zones, zone_id)
            if found:
                return found
    return None


def _collect_leaves(zones: list[dict], result: list[dict]):
    """递归收集叶子 zone（subZones 为空或不存在的 zone）"""
    for zone in zones:
        sub_zones = zone.get("subZones")
        if sub_zones and len(sub_zones) > 0:
            _collect_leaves(sub_zones, result)
        else:
            result.append(zone)


def get_zone(project_path: str, zone_id: str) -> dict | None:
    """
    获取单个 Zone 的完整信息（含 tags、边界）
    支持在 subZones 嵌套结构中递归查找

    Args:
        project_path: 项目根路径
        zone_id: Zone ID (如 "rz_1" 或 "dz_1")

    Returns:
        Zone 数据字典，如果不存在则返回 None
    """
    zones = get_all_zones(project_path)
    return _find_zone_recursive(zones, zone_id)


def get_all_zones(project_path: str) -> list[dict]:
    """
    获取所有 Zone 列表
    优先读取 schemes/zones.json（含 subZones），回退到 computed/room_zones.json

    Args:
        project_path: 项目根路径

    Returns:
        Zone 数据列表（顶层，可能包含 subZones）
    """
    # 优先 schemes/zones.json（含 subZones 信息）
    scheme_zones = read_json(project_path, "schemes/zones.json")
    if scheme_zones and isinstance(scheme_zones, list) and len(scheme_zones) > 0:
        return scheme_zones

    # 回退 computed/room_zones.json
    zones = read_json(project_path, "computed/room_zones.json")
    if not zones:
        return []

    return zones if isinstance(zones, list) else zones.get("zones", [])


def get_leaf_zones(project_path: str) -> list[dict]:
    """
    只返回可放置家具的叶子 zone（展平嵌套结构）

    Args:
        project_path: 项目根路径

    Returns:
        叶子 Zone 列表（无 subZones 的 zone）
    """
    zones = get_all_zones(project_path)
    leaves: list[dict] = []
    _collect_leaves(zones, leaves)
    return leaves


def list_modules_by_zone(project_path: str, zone_id: str) -> dict:
    """
    根据 Zone 的 Tags 查询兼容模块列表

    Args:
        project_path: 项目根路径
        zone_id: Zone ID

    Returns:
        包含 zone_tags 和 modules 的字典
    """
    # 获取 Zone
    zone = get_zone(project_path, zone_id)
    if not zone:
        return {
            "zone_id": zone_id,
            "zone_tags": [],
            "modules": [],
            "error": f"Zone {zone_id} not found"
        }

    zone_tags = zone.get("tags", [])
    optional_tags = zone.get("optionalTags", [])
    all_tags = zone_tags + optional_tags
    if not all_tags:
        return {
            "zone_id": zone_id,
            "zone_tags": [],
            "optional_tags": [],
            "modules": [],
            "error": "Zone has no tags"
        }

    # 标准化 zone tags（转小写进行比较，必备+建议标签并集用于模块匹配）
    all_tags_lower = {str(t).lower() for t in all_tags}

    # 加载模块库
    library = read_json(project_path, "modules/module_library.json")
    if not library:
        return {
            "zone_id": zone_id,
            "zone_tags": zone_tags,
            "modules": [],
            "error": "Module library not found"
        }

    all_modules = library.get("modules", [])

    # 过滤兼容模块（模块的任一 tag 与 zone 的任一 tag 匹配）
    compatible_modules = []
    for module in all_modules:
        module_tags = module.get("tags", [])
        module_tags_lower = {str(t).lower() for t in module_tags}

        if module_tags_lower & all_tags_lower:  # 交集非空
            compatible_modules.append(module)

    return {
        "zone_id": zone_id,
        "zone_tags": zone_tags,
        "optional_tags": optional_tags,
        "modules": compatible_modules
    }


def get_exclusions(project_path: str, zone_id: str | None = None) -> list[dict]:
    """
    获取禁区数据

    Args:
        project_path: 项目根路径
        zone_id: 可选，指定 Zone ID 时只返回该 Zone 相关的禁区

    Returns:
        禁区列表，每个禁区包含 id、reason、rawBoundary 等字段
    """
    exclusions = read_json(project_path, "computed/exclusions.json")
    if not exclusions:
        return []

    # 支持列表格式和带 version 字段的对象格式
    exclusion_list = exclusions if isinstance(exclusions, list) else exclusions.get("exclusions", [])

    if zone_id is None:
        return exclusion_list

    # 过滤与指定 Zone 相关的禁区
    # 注意：当前版本禁区没有 zoneId 字段，直接返回全部
    # Agent 通过坐标匹配判断禁区归属
    return exclusion_list


def list_modules(
    project_path: str,
    tags: list[str] | None = None
) -> list[dict]:
    """
    查询模块库，可按标签过滤

    Args:
        project_path: 项目根路径
        tags: 可选，功能标签列表（如 ["sleep", "storage"]）

    Returns:
        匹配的模块列表
    """
    library = read_json(project_path, "modules/module_library.json")
    if not library:
        return []

    all_modules = library.get("modules", [])

    if not tags:
        return all_modules

    # 按标签过滤
    tags_lower = {t.lower() for t in tags}
    return [
        m for m in all_modules
        if any(t.lower() in tags_lower for t in m.get("tags", []))
    ]


def get_module_info(project_path: str, module_id: str) -> dict | None:
    """
    根据 moduleId 获取模块详情

    Args:
        project_path: 项目根路径
        module_id: 模块 ID（如 "mod_bed_001"）

    Returns:
        模块详情字典，如果不存在则返回 None
    """
    library = read_json(project_path, "modules/module_library.json")
    if not library:
        return None

    for module in library.get("modules", []):
        if module.get("id") == module_id:
            return module

    return None
