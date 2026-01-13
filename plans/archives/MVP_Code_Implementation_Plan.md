# BIMCanvas MVP 代码修改方案

> 基于项目当前状态（v3.1）的深度分析
> 生成日期：2025-01-09

---

## 一、项目当前状态总结

### 1.1 已完成模块

| 层 | 状态 | 说明 |
|----|------|------|
| **Core 层** | ✅ 已完成 | 数据模型 v3.0 完整定义（Zone、Module、ZoneTag、RoomType 等） |
| **Server 层** | 🔶 部分完成 | ProjectService、ComputedDataService、StrategyService 已实现 |
| **Agent 层** | 🟡 框架就位 | MainAgent + file_tools 基础功能已有，布置工具待开发 |

### 1.2 关键差距

**computed/room_zones.json**：
- 当前 `Tags` 字段为空（硬编码 `new List<ZoneTag>()`）
- 需要根据 `RoomType` 自动分配功能标签

---

## 二、需要新增/修改的文件清单

### 2.1 Server 端（.NET C#）

#### 新增文件

| 文件路径 | 类名 | 职责 |
|---------|------|------|
| `Services/RoomTypeTagMappingService.cs` | `RoomTypeTagMappingService` | RoomType → ZoneTag[] 映射管理 |
| `Services/PlacementService.cs` | `PlacementValidator` | 模块标签兼容性验证 |

#### 修改文件

| 文件路径 | 修改位置 | 修改内容 |
|---------|---------|----------|
| `Services/ComputedDataService.cs` | `CalculateRoomZones()` 第 308 行 | `Tags = new List<ZoneTag>()` → `Tags = GetTagsForRoomType(room.Type)` |

### 2.2 Agent 端（Python）

#### 新增文件

| 文件路径 | 模块名 | 职责 |
|---------|--------|------|
| `src/tools/zone_tools.py` | `zone_tools` | get_zone、list_modules_by_zone、get_exclusions |
| `src/tools/placement_tools.py` | `placement_tools` | write_modules、validate_modules |

---

## 三、核心代码设计

### 3.1 RoomType → ZoneTag 映射表

```csharp
// Server 端：RoomTypeTagMappingService.cs
private static readonly Dictionary<RoomType, List<ZoneTag>> RoomTypeTagMapping = 
    new()
    {
        { RoomType.LivingRoom, new() { ZoneTag.TvMedia, ZoneTag.Rest, ZoneTag.Display } },
        { RoomType.DiningRoom, new() { ZoneTag.Dining } },
        { RoomType.MasterBedroom, new() { ZoneTag.Sleep, ZoneTag.WardrobeStorage, ZoneTag.Vanity } },
        { RoomType.Bedroom, new() { ZoneTag.Sleep, ZoneTag.WardrobeStorage } },
        { RoomType.Study, new() { ZoneTag.Work, ZoneTag.Study, ZoneTag.Reading } },
        { RoomType.Kitchen, new() { ZoneTag.Cooking, ZoneTag.FoodPrep, ZoneTag.Bar } },
        { RoomType.Bathroom, new() { ZoneTag.Shower, ZoneTag.Toilet, ZoneTag.Washing, ZoneTag.Vanity } },
        { RoomType.Entrance, new() { ZoneTag.Entry } },
        { RoomType.Balcony, new() { ZoneTag.Rest, ZoneTag.Plants, ZoneTag.Display } },
        { RoomType.Corridor, new() { ZoneTag.Passage } },
        { RoomType.Storage, new() { ZoneTag.GeneralStorage, ZoneTag.WardrobeStorage, ZoneTag.ShoeStorage } }
    };
```

### 3.2 Agent 工具函数签名

```python
# zone_tools.py
def get_zone(project_path: str, zone_id: str) -> dict:
    """获取单个 Zone 的完整信息（含 tags、已有模块、禁区）"""

def list_modules_by_zone(project_path: str, zone_id: str) -> dict:
    """根据 Zone 的 Tags 查询兼容模块列表"""

def get_exclusions(project_path: str, zone_id: str) -> dict:
    """获取某个 Zone 内的所有禁区"""

# placement_tools.py
def write_modules(project_path: str, scheme_id: str, modules: list) -> tuple[bool, str]:
    """将模块列表写入 schemes/{scheme_id}/modules.json"""

def validate_module_data(modules: list) -> list[str]:
    """验证模块数据有效性，返回错误列表"""
```

---

## 四、数据流

```
【Server 预计算阶段】
ProjectService.LoadProject()
    └─ ComputedDataService.GenerateComputedData()
        └─ CalculateRoomZones()
            └─ 【新增】Tags 分配 via RoomTypeTagMappingService
                → computed/room_zones.json（含 tags 字段）

【Agent 决策阶段】
Agent.run()
    ├─ list_modules_by_zone(zone_id)
    │   └─ 根据 zone.tags 过滤兼容模块
    ├─ get_exclusions(zone_id)
    │   └─ 获取禁区用于碰撞检测
    ├─ AI 推理：选择模块 + 规划位置
    └─ write_modules(scheme_id, modules)
        → schemes/{scheme_id}/modules.json

【Server 验证阶段】
PlacementService.ValidateModules()
    ├─ 检查 moduleId 有效性
    ├─ 检查 tags 兼容性（module.tags ∩ zone.tags）
    └─ 检查空间约束（bounds 在边界内、不与禁区重叠）
```

---

## 五、实施步骤

### Phase 1：Server Tags 分配（0.5-1 天）
1. 新增 `RoomTypeTagMappingService.cs`
2. 修改 `ComputedDataService.CalculateRoomZones()`
3. 验证 `computed/room_zones.json` 成功包含 tags

### Phase 2：Server 验证服务（0.5-1 天）
4. 新增 `PlacementService.cs`
5. 可选：创建 `PlacementController` API 端点
6. 单元测试

### Phase 3：Agent 工具实现（1-2 天）
7. 实现 `zone_tools.py`（get_zone、list_modules_by_zone、get_exclusions）
8. 实现 `placement_tools.py`（write_modules）
9. 集成到 MainAgent

### Phase 4：集成测试（0.5 天）
10. E2E 测试
11. 文档更新

---

## 六、工作量评估

| 任务 | 时间 |
|------|------|
| Server 端实现 | 2-4 小时 |
| Agent 端实现 | 4-5 小时 |
| E2E 测试 | 2-3 小时 |
| 文档更新 | 1 小时 |
| **总计** | **9-13 小时** |

---

## 七、风险与缓解

| 风险 | 缓解方案 |
|------|----------|
| 几何碰撞检测不准确 | MVP 阶段使用 AABB 简化检测 |
| 标签映射不完整 | 使用默认标签，支持后续配置化扩展 |
| Agent 生成无效 JSON | PlacementService 写入前做数据验证 |

---

## 八、相关文档

- `plans/Server_Agent_Collaboration_Plan.md` - Server-Agent 协作流程详细设计
- `docs/Agent_Design_Spec.md` §8.7 - 协作规范摘要
- `docs/Schema-JSON-v3.md` - 数据模型定义
