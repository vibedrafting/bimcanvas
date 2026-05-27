"""平台几何原语（domain-agnostic），供插件校验脚本使用。

设计来历（包A · 2026-05-27 决议）：validation 的"合理性判断"是 per-profession 的
domain 代码（在各插件 validators/ 脚本里）；但"几何事实"（重叠/包含/穿透/朝向）是
通用原语，应由平台一次提供、所有 domain 共用。本模块即该原语层的 Python 实现，
作者通过 `from bimcanvas_plugin_sdk import geometry` 使用，不应触达底层引擎细节。

实现镜像主仓 C# 端 `BIMCanvas.Core.Algorithms.Spatial.CollisionDetector` /
`FacingHelper` 的算子契约（rounding / 噪声地板 / 方位约定 / buffer 容差逐位照搬），
以 shapely(GEOS) 为底层引擎。与 C# 的 NTS(JTS) 同出 JTS 血统，对常规多边形结果
一致到很多位；指挥部已将本包"行为不变"硬线放宽为"功能等价 + 用户手测兜底"，仅
Buffer 边的包含判定、1mm/1e-6 噪声地板等极少边界个例可能与 NTS 不同。引擎藏在本
helper 缝后——若日后真撞 parity，可把实现换成回调 C# 而不动任何插件脚本。

坐标系：Y-Up 笛卡尔，单位 mm（与项目一致）。多边形以顶点序列 [(x, y), ...] 表示。
"""

from __future__ import annotations

import math
from typing import Optional, Sequence

from shapely.geometry import Polygon

# 多边形入参：可为简单外环 [[x,y], ...]，或带孔形态 {"shell": [[x,y],...], "holes": [[[x,y],...],...]}
# （与 C# Polygon2D / Polygon2DConverter 的两种 JSON 形态一一对应）
Vertices = Sequence[Sequence[float]]

# 与 C# CollisionDetector / ComputeOverlapInfo 常量逐位对齐
_AREA_FLOOR_MM2 = 1e-6        # 交集面积噪声地板（mm²）
_DEPTH_FLOOR_MM = 1.0         # 穿透深度噪声地板（mm）；< 此值视为数值噪声不可操作
_DEFAULT_TOLERANCE_MM = 10.0  # IsWithinTolerant 默认膨胀容差

# FacingHelper.TrySemanticToVector：语义罗盘方向 → 向量（对角线归一前的原始向量）
_SEMANTIC_VECTORS = {
    "north": (0.0, 1.0),
    "south": (0.0, -1.0),
    "east": (1.0, 0.0),
    "west": (-1.0, 0.0),
    "northeast": (1.0, 1.0),
    "northwest": (-1.0, 1.0),
    "southeast": (1.0, -1.0),
    "southwest": (-1.0, -1.0),
}


def semantic_to_vector(semantic: Optional[str]) -> Optional[tuple]:
    """语义罗盘方向 → 单位向量（镜像 C# FacingHelper.TrySemanticToVector）。

    返回归一化单位向量 (x, y)；无法识别返回 None。对角线方向按 ÷√2 归一，
    四正方向本身即单位向量。大小写不敏感、首尾空白忽略。
    """
    if semantic is None:
        return None
    vec = _SEMANTIC_VECTORS.get(semantic.strip().lower())
    if vec is None:
        return None
    return _normalize(vec)


def within_tolerant(
    inner: Vertices,
    outer: Vertices,
    tolerance_mm: float = _DEFAULT_TOLERANCE_MM,
) -> bool:
    """outer 膨胀 tolerance_mm 后是否包含 inner（镜像 CollisionDetector.IsWithinTolerant）。

    先精确包含快路径；否则 outer.buffer(tolerance) 再判包含。几何异常按 False
    （与 C# TopologyException 分支一致）。
    """
    try:
        p_inner = _to_polygon(inner)
        p_outer = _to_polygon(outer)
        if p_outer.contains(p_inner):
            return True
        return p_outer.buffer(tolerance_mm).contains(p_inner)
    except Exception:
        return False


def overlap_info(subject: Vertices, obstacle: Vertices) -> dict:
    """计算重叠详情（镜像 CollisionDetector.ComputeOverlapInfo）。

    返回 dict：
      has_overlap : bool
      area_mm2    : float  交集面积，round(_, 1)
      depth_mm    : float  穿透深度，round(_, 1)；= 各交集子几何 min(包络W, H) 的 max
      direction   : str|None  障碍物相对 subject 中心的方位 ∈ {east,west,north,south}
                              （Agent 应朝相反方向移动）
    无真实重叠（含面积/深度低于噪声地板、几何异常）时 has_overlap=False。
    """
    none_result = {"has_overlap": False, "area_mm2": 0.0, "depth_mm": 0.0, "direction": None}
    try:
        p_sub = _to_polygon(subject)
        p_obs = _to_polygon(obstacle)

        if not p_sub.intersects(p_obs):
            return none_result

        inter = p_sub.intersection(p_obs)
        if inter.is_empty or inter.area < _AREA_FLOOR_MM2:
            return none_result

        # 穿透深度：遍历交集所有子几何，每个取 min(包络W, H)，整体取 max
        max_depth = 0.0
        for part in _iter_geometries(inter):
            if part.area < _AREA_FLOOR_MM2:
                continue
            minx, miny, maxx, maxy = part.bounds
            part_depth = min(maxx - minx, maxy - miny)
            if part_depth > max_depth:
                max_depth = part_depth

        if max_depth < _DEPTH_FLOOR_MM:  # < 1mm 穿透为数值噪声，不可操作
            return none_result

        # 穿透方向：交集质心相对 subject 质心的方位
        sub_c = p_sub.centroid
        ov_c = inter.centroid
        dx = ov_c.x - sub_c.x
        dy = ov_c.y - sub_c.y
        if abs(dx) >= abs(dy):
            direction = "east" if dx >= 0 else "west"
        else:
            direction = "north" if dy >= 0 else "south"

        return {
            "has_overlap": True,
            "area_mm2": round(inter.area, 1),
            "depth_mm": round(max_depth, 1),
            "direction": direction,
        }
    except Exception:
        return none_result


def aabb_intersects(a: Vertices, b: Vertices) -> bool:
    """轴对齐包围盒快速预检（供脚本两两检测前过滤；不影响结果，仅省算力）。

    对应 SchemeValidator 在调用 ComputeOverlapInfo 前的 AABB.Intersects 预检。
    """
    axmin, aymin, axmax, aymax = _aabb(a)
    bxmin, bymin, bxmax, bymax = _aabb(b)
    return not (axmax < bxmin or bxmax < axmin or aymax < bymin or bymax < aymin)


# ----------------------------------------------------------------------------
# 内部 helper
# ----------------------------------------------------------------------------

def _normalize(v: tuple) -> tuple:
    length = math.hypot(v[0], v[1])
    if length == 0:
        return (0.0, 0.0)
    return (v[0] / length, v[1] / length)


def _coerce_rings(poly):
    """把多边形入参规整为 (shell, holes)。

    接受简单外环 [[x,y],...] 或带孔 dict {"shell":[...], "holes":[[...],...]}。
    """
    if isinstance(poly, dict):
        return poly.get("shell") or [], poly.get("holes") or []
    return poly, []


def _to_polygon(poly: Vertices) -> Polygon:
    shell, holes = _coerce_rings(poly)
    return Polygon(
        [(float(p[0]), float(p[1])) for p in shell],
        [[(float(p[0]), float(p[1])) for p in h] for h in holes],
    )


def _iter_geometries(geom):
    """遍历几何的子几何；单体几何返回自身（对应 NTS NumGeometries / GetGeometryN）。"""
    if hasattr(geom, "geoms"):
        return list(geom.geoms)
    return [geom]


def _aabb(poly: Vertices) -> tuple:
    shell, _ = _coerce_rings(poly)
    xs = [float(p[0]) for p in shell]
    ys = [float(p[1]) for p in shell]
    return min(xs), min(ys), max(xs), max(ys)
