"""Placement tools for Agent to write module layouts

提供写入布置结果、验证模块数据等功能
MVP 版本：直接写入文件，Server 事后验证
"""

import json
from pathlib import Path
from typing import Any
from .file_tools import read_json, write_json


class PlacedModule:
    """已放置模块的数据结构"""

    def __init__(
        self,
        id: str,
        module_id: str,
        module_name: str,
        bounds: list[list[float]],
        facing: str | list[float],
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
    zone_id: str | None = None
) -> tuple[bool, str]:
    """
    将模块列表写入文件系统
    v3.3: 支持按分区子目录写入

    Args:
        project_path: 项目根路径
        modules: 模块列表（字典或 PlacedModule 对象）
        zone_id: 可选的分区 ID
            - 如果指定，写入到 schemes/{zone_id}/modules.json
            - 如果不指定，按模块的 zoneId 自动分组写入分区子目录

    Returns:
        (success, message) 元组
    """
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
        # 如果指定了 zone_id，只写入该分区
        if zone_id:
            relative_path = f"schemes/{zone_id}/modules.json"
            write_json(project_path, relative_path, module_dicts)
            return True, f"成功写入 {len(module_dicts)} 个模块到 {relative_path}"

        # 按 zoneId 分组
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

        # 按分区写入
        for z_id, zone_modules in grouped.items():
            relative_path = f"schemes/{z_id}/modules.json"
            write_json(project_path, relative_path, zone_modules)

        return True, f"成功写入 {len(module_dicts)} 个模块到 {len(grouped)} 个分区"

    except Exception as e:
        return False, f"写入失败: {str(e)}"


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
            valid_directions = [
                "north", "south", "east", "west",
                "northeast", "northwest", "southeast", "southwest"
            ]
            if isinstance(facing, str):
                if facing.lower() not in valid_directions:
                    errors.append(
                        f"模块 {module_id}: facing '{facing}' 不是有效的方向"
                    )
            elif isinstance(facing, list):
                if len(facing) != 2:
                    errors.append(f"模块 {module_id}: facing 向量必须是 [x, y] 格式")
            else:
                errors.append(f"模块 {module_id}: facing 必须是方向字符串或向量")

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
    facing: str = "north"
) -> list[list[float]]:
    """
    根据中心点、尺寸和朝向创建包围盒

    Args:
        center: 中心点 [x, y]
        width: 宽度 (mm)
        depth: 深度 (mm)
        facing: 朝向（north/south/east/west）

    Returns:
        4 个顶点的包围盒 [[x1,y1], [x2,y2], [x3,y3], [x4,y4]]
    """
    cx, cy = center
    half_w = width / 2
    half_d = depth / 2

    # 根据朝向调整宽深
    if facing.lower() in ["east", "west"]:
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

    # 如果指定了 zone_id，只读取该分区
    if zone_id:
        relative_path = f"schemes/{zone_id}/modules.json"
        modules = read_json(project_path, relative_path)
        return modules if isinstance(modules, list) else []

    # 尝试从分区子目录读取
    all_modules = []
    if schemes_path.exists():
        zone_dirs = [
            d for d in schemes_path.iterdir()
            if d.is_dir() and (d.name.startswith("rz_") or d.name.startswith("dz_"))
        ]

        if zone_dirs:
            # 新格式：从分区子目录读取
            for zone_dir in zone_dirs:
                modules_file = zone_dir / "modules.json"
                if modules_file.exists():
                    relative_path = f"schemes/{zone_dir.name}/modules.json"
                    modules = read_json(project_path, relative_path)
                    if isinstance(modules, list):
                        # 确保每个模块有正确的 zoneId
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
