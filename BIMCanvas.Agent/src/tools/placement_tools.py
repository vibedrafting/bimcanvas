"""Placement tools for Agent to write module layouts

提供写入布置结果、验证模块数据等功能
支持嵌套分区路径（schemes/{parentZoneId}/{childZoneId}/modules.json）
"""

import json
from pathlib import Path
from typing import Any
from .file_tools import read_json, write_json

VALID_FACING_DIRECTIONS = [
    "north", "south", "east", "west",
    "northeast", "northwest", "southeast", "southwest"
]


def _resolve_facing_semantic(facing: dict[str, Any] | None) -> str:
    """从 facing 对象中推断正交语义方向，供简化辅助函数使用。"""
    if isinstance(facing, dict):
        semantic = facing.get("semantic")
        if isinstance(semantic, str):
            lowered = semantic.lower()
            if lowered in VALID_FACING_DIRECTIONS:
                return lowered

        value = facing.get("value")
        if isinstance(value, list) and len(value) == 2 and all(isinstance(v, (int, float)) for v in value):
            vx, vy = float(value[0]), float(value[1])
            if abs(vx) >= abs(vy):
                return "east" if vx >= 0 else "west"
            return "north" if vy >= 0 else "south"

    return "north"


_VALID_VARIANT_NAME_CHARS = set(
    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
)


def _ensure_safe_variant_name(variant_name: str) -> None:
    """校验 variant_name 仅含安全字符（字母/数字/下划线/连字符），防止路径穿越。"""
    if not variant_name:
        raise ValueError("variant_name 不能为空")
    for ch in variant_name:
        if ch not in _VALID_VARIANT_NAME_CHARS:
            raise ValueError(
                f"variant_name 包含非法字符 '{ch}'，仅允许字母/数字/下划线/连字符"
            )


def _build_module_filename(variant_name: str | None) -> str:
    """构建叶子分区的 modules JSON 文件名。variant_name 为空 → canonical。"""
    if variant_name is None or variant_name == "":
        return "modules.json"
    _ensure_safe_variant_name(variant_name)
    return f"modules-{variant_name}.json"


def _resolve_module_path(
    project_path: str,
    zone_id: str,
    variant_name: str | None = None,
) -> str:
    """
    解析 zone_id 到正确的 modules JSON 相对路径（支持嵌套分区 + 变体文件名）。

    variant_name 为空 → 写入 canonical "modules.json"；
    非空 → 写入同目录下的 "modules-{variant_name}.json"。
    """
    filename = _build_module_filename(variant_name)
    schemes_path = Path(project_path) / "schemes"

    # 1. 检查一级目录
    if (schemes_path / zone_id).exists():
        return f"schemes/{zone_id}/{filename}"

    # 2. 搜索嵌套目录（在父 zone 目录下查找）
    if schemes_path.exists():
        for parent_dir in schemes_path.iterdir():
            if parent_dir.is_dir() and (parent_dir / zone_id).exists():
                return f"schemes/{parent_dir.name}/{zone_id}/{filename}"

    # 3. 回退到一级目录（新建场景，由 file_tools.write_json 自动创建目录）
    return f"schemes/{zone_id}/{filename}"


class PlacedModule:
    """已放置模块的数据结构"""

    def __init__(
        self,
        id: str,
        module_id: str,
        module_name: str,
        bounds: list[list[float]],
        facing: dict[str, Any],
        zone_id: str,
        placement_reason: str = "",
        dependency_group: str | None = None
    ):
        self.id = id
        self.module_id = module_id
        self.module_name = module_name
        self.bounds = bounds
        self.facing = facing
        self.zone_id = zone_id
        self.placement_reason = placement_reason
        self.dependency_group = dependency_group

    def to_dict(self) -> dict:
        """转换为字典格式"""
        result = {
            "id": self.id,
            "moduleId": self.module_id,
            "moduleName": self.module_name,
            "bounds": self.bounds,
            "facing": self.facing,
            "zoneId": self.zone_id
        }
        if self.placement_reason:
            result["placementReason"] = self.placement_reason
        if self.dependency_group:
            result["dependencyGroup"] = self.dependency_group
        return result


def write_modules(
    project_path: str,
    modules: list[dict | PlacedModule],
    zone_id: str | None = None,
    variant_name: str | None = None,
) -> tuple[bool, str]:
    """
    将模块列表写入文件系统
    v3.3: 支持按分区子目录写入
    v3.6: 支持 module-relocation-agent 的变体文件名（modules-{variant_name}.json）

    Args:
        project_path: 项目根路径
        modules: 模块列表（字典或 PlacedModule 对象）
        zone_id: 可选的分区 ID
            - 如果指定，写入到 schemes/{zone_id}/modules.json（或变体文件）
            - 如果不指定，按模块的 zoneId 自动分组写入分区子目录
        variant_name: 可选的变体名称（如 "alt-1"）
            - 为空（默认）→ 写入 canonical "modules.json"
            - 非空 → 写入同目录下的 "modules-{variant_name}.json"
            - 必须与显式 zone_id 同时指定，不允许变体走自动分组分支

    Returns:
        (success, message) 元组
    """
    if variant_name and not zone_id:
        return False, "variant_name 非空时必须显式指定 zone_id；不允许变体写入走自动分组路径"

    if variant_name:
        try:
            _ensure_safe_variant_name(variant_name)
        except ValueError as exc:
            return False, str(exc)

    # 验证模块数据
    errors = validate_module_data(modules)
    if errors:
        return False, f"验证失败: {'; '.join(errors)}"

    # 转换为字典格式
    module_dicts = []
    for m in modules:
        if isinstance(m, PlacedModule):
            module_dicts.append(m.to_dict())
        elif isinstance(m, dict):
            module_dicts.append(m)
        else:
            return False, f"无效的模块类型: {type(m)}"

    try:
        # 如果指定了 zone_id（含 variant 模式），只写入该分区（支持嵌套路径）
        if zone_id:
            relative_path = _resolve_module_path(project_path, zone_id, variant_name)
            write_json(project_path, relative_path, module_dicts)
            return True, f"成功写入 {len(module_dicts)} 个模块到 {relative_path}"

        # 按 zoneId 分组（仅 canonical 模式走到这里，variant_name 已在前面拒绝）
        grouped: dict[str, list[dict]] = {}
        for m in module_dicts:
            z_id = m.get("zoneId", "")
            if z_id:
                if z_id not in grouped:
                    grouped[z_id] = []
                grouped[z_id].append(m)

        # 如果没有有效的 zoneId，使用旧格式（向后兼容）
        if not grouped:
            relative_path = "schemes/modules.json"
            write_json(project_path, relative_path, module_dicts)
            return True, f"成功写入 {len(module_dicts)} 个模块到 {relative_path}（向后兼容模式）"

        # 按分区写入（支持嵌套路径）
        for z_id, zone_modules in grouped.items():
            relative_path = _resolve_module_path(project_path, z_id)
            write_json(project_path, relative_path, zone_modules)

        return True, f"成功写入 {len(module_dicts)} 个模块到 {len(grouped)} 个分区"

    except Exception as e:
        return False, f"写入失败: {str(e)}"


def write_variant_metadata(
    project_path: str,
    zone_id: str,
    variant_name: str,
    metadata: dict[str, Any],
) -> tuple[bool, str]:
    """
    写入变体的 sidecar metadata 文件（modules-{variant_name}.meta.json）。

    路径解析复用 _resolve_module_path 的目录推断，仅替换文件名后缀。
    Args:
        project_path: 项目根路径
        zone_id: 叶子分区 ID（必须）
        variant_name: 变体名（如 "alt-1"，必须）
        metadata: sidecar 字典；schema 详见 module-relocation-agent.md
    Returns:
        (success, message)
    """
    if not zone_id:
        return False, "write_variant_metadata 必须指定 zone_id"
    try:
        _ensure_safe_variant_name(variant_name)
    except ValueError as exc:
        return False, str(exc)

    # 复用 _resolve_module_path 推断的目录，把文件名换成 sidecar
    canonical_relative = _resolve_module_path(project_path, zone_id, variant_name)
    sidecar_relative = canonical_relative[:-len(".json")] + ".meta.json"

    try:
        write_json(project_path, sidecar_relative, metadata)
        return True, f"成功写入 sidecar metadata 到 {sidecar_relative}"
    except Exception as e:
        return False, f"sidecar 写入失败: {str(e)}"


def write_zone_modules(
    project_path: str,
    zone_id: str,
    modules: list[dict | PlacedModule]
) -> tuple[bool, str]:
    """
    将模块写入指定分区（便捷方法）

    Args:
        project_path: 项目根路径
        zone_id: 分区 ID
        modules: 模块列表

    Returns:
        (success, message) 元组
    """
    return write_modules(project_path, modules, zone_id=zone_id)


def validate_module_data(modules: list[dict | PlacedModule]) -> list[str]:
    """
    验证模块数据有效性

    Args:
        modules: 模块列表

    Returns:
        错误列表（空列表表示验证通过）
    """
    errors = []

    for i, m in enumerate(modules):
        # 转换为字典用于验证
        if isinstance(m, PlacedModule):
            m = m.to_dict()

        module_id = m.get("id", f"module_{i}")

        # 检查必需字段
        required_fields = ["id", "moduleId", "bounds", "zoneId"]
        for field in required_fields:
            if field not in m or m[field] is None:
                errors.append(f"模块 {module_id}: 缺少必需字段 '{field}'")

        # 检查 bounds 格式
        bounds = m.get("bounds")
        if bounds is not None:
            if not isinstance(bounds, list):
                errors.append(f"模块 {module_id}: bounds 必须是数组")
            elif len(bounds) != 4:
                errors.append(f"模块 {module_id}: bounds 必须包含 4 个顶点，当前有 {len(bounds)} 个")
            else:
                for j, vertex in enumerate(bounds):
                    if not isinstance(vertex, list) or len(vertex) != 2:
                        errors.append(f"模块 {module_id}: bounds[{j}] 必须是 [x, y] 格式")

        # 检查 facing 格式
        facing = m.get("facing")
        if facing is not None:
            if not isinstance(facing, dict):
                errors.append(f"模块 {module_id}: facing 必须是 {{value, semantic}} 对象")
            else:
                value = facing.get("value")
                semantic = facing.get("semantic")

                if value is not None:
                    if not isinstance(value, list) or len(value) != 2:
                        errors.append(f"模块 {module_id}: facing.value 必须是 [x, y] 或 null")
                    elif not all(isinstance(v, (int, float)) for v in value):
                        errors.append(f"模块 {module_id}: facing.value 必须包含数值")

                if semantic is not None:
                    if not isinstance(semantic, str):
                        errors.append(f"模块 {module_id}: facing.semantic 必须是字符串或 null")
                    elif semantic.lower() not in VALID_FACING_DIRECTIONS:
                        errors.append(f"模块 {module_id}: facing.semantic '{semantic}' 不是有效方向")

                if value is None and semantic is None:
                    errors.append(f"模块 {module_id}: facing.value 和 facing.semantic 不能同时为 null")

    return errors


def check_overlap_simple(bounds1: list[list[float]], bounds2: list[list[float]]) -> bool:
    """
    简单矩形重叠检测（AABB）

    Args:
        bounds1: 第一个包围盒的 4 个顶点
        bounds2: 第二个包围盒的 4 个顶点

    Returns:
        True 如果重叠，False 如果不重叠
    """
    def compute_aabb(bounds):
        xs = [v[0] for v in bounds]
        ys = [v[1] for v in bounds]
        return min(xs), min(ys), max(xs), max(ys)

    min_x1, min_y1, max_x1, max_y1 = compute_aabb(bounds1)
    min_x2, min_y2, max_x2, max_y2 = compute_aabb(bounds2)

    # 分离轴测试
    return not (max_x1 < min_x2 or min_x1 > max_x2 or
                max_y1 < min_y2 or min_y1 > max_y2)


def check_point_in_polygon(point: list[float], polygon: list[list[float]]) -> bool:
    """
    检查点是否在多边形内（射线法）

    Args:
        point: 点坐标 [x, y]
        polygon: 多边形顶点列表

    Returns:
        True 如果点在多边形内
    """
    x, y = point
    n = len(polygon)
    inside = False

    j = n - 1
    for i in range(n):
        xi, yi = polygon[i]
        xj, yj = polygon[j]

        if ((yi > y) != (yj > y)) and (x < (xj - xi) * (y - yi) / (yj - yi) + xi):
            inside = not inside

        j = i

    return inside


def create_module_bounds(
    center: list[float],
    width: float,
    depth: float,
    facing: dict[str, Any] | None = None
) -> list[list[float]]:
    """
    根据中心点、尺寸和 facing 对象创建包围盒。
    这是一个简化辅助函数，只处理正交朝向；最终项目文件仍应以 bounds 为几何真理。

    Args:
        center: 中心点 [x, y]
        width: 宽度 (mm)
        depth: 深度 (mm)
        facing: {value, semantic}；优先读取 semantic，否则从 value 推断最近正交方向

    Returns:
        4 个顶点的包围盒 [[x1,y1], [x2,y2], [x3,y3], [x4,y4]]
    """
    cx, cy = center
    half_w = width / 2
    half_d = depth / 2

    facing_semantic = _resolve_facing_semantic(facing)

    # 根据正交朝向调整宽深
    if facing_semantic in ["east", "west"]:
        # 旋转 90 度，宽深交换
        half_w, half_d = half_d, half_w

    # 返回顺时针顶点（左下开始）
    return [
        [cx - half_w, cy - half_d],  # 左下
        [cx + half_w, cy - half_d],  # 右下
        [cx + half_w, cy + half_d],  # 右上
        [cx - half_w, cy + half_d],  # 左上
    ]


def load_existing_modules(project_path: str, zone_id: str | None = None) -> list[dict]:
    """
    加载已有的模块
    v3.3: 支持从分区子目录读取

    Args:
        project_path: 项目根路径
        zone_id: 可选的分区 ID
            - 如果指定，只读取该分区的模块
            - 如果不指定，读取所有分区的模块

    Returns:
        已放置模块列表
    """
    import os

    schemes_path = Path(project_path) / "schemes"

    # 如果指定了 zone_id，只读取该分区（支持嵌套路径）
    if zone_id:
        relative_path = _resolve_module_path(project_path, zone_id)
        modules = read_json(project_path, relative_path)
        return modules if isinstance(modules, list) else []

    # 尝试从分区子目录读取（支持嵌套分区）
    all_modules = []
    if schemes_path.exists():
        # 收集所有叶子 zone 目录（含嵌套子目录）
        leaf_zone_dirs = []
        for d in schemes_path.iterdir():
            if not d.is_dir() or not (d.name.startswith("rz_") or d.name.startswith("dz_")):
                continue
            # 检查是否有嵌套子分区目录
            sub_dirs = [sd for sd in d.iterdir() if sd.is_dir() and sd.name.startswith("dz_")]
            if sub_dirs:
                # 容器 zone → 收集子目录
                leaf_zone_dirs.extend(sub_dirs)
            elif (d / "modules.json").exists():
                # 叶子 zone → 直接收集
                leaf_zone_dirs.append(d)

        if leaf_zone_dirs:
            for zone_dir in leaf_zone_dirs:
                modules_file = zone_dir / "modules.json"
                if modules_file.exists():
                    modules = read_json(project_path, str(modules_file.relative_to(Path(project_path))))
                    if isinstance(modules, list):
                        for m in modules:
                            if not m.get("zoneId"):
                                m["zoneId"] = zone_dir.name
                        all_modules.extend(modules)
            return all_modules

    # 旧格式：从单一文件读取（向后兼容）
    relative_path = "schemes/modules.json"
    modules = read_json(project_path, relative_path)
    return modules if isinstance(modules, list) else []


def load_zone_modules(project_path: str, zone_id: str) -> list[dict]:
    """
    加载指定分区的模块（便捷方法）

    Args:
        project_path: 项目根路径
        zone_id: 分区 ID

    Returns:
        该分区的模块列表
    """
    return load_existing_modules(project_path, zone_id=zone_id)
