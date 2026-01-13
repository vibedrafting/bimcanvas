# Canvas-MCP 工具接口规范

> 版本：v1.1
> 更新日期：2026-01-13
> 状态：已定稿
>
> **相关文档**：
> - [Schema.md](./Schema.md) - JSON 数据模型规范
> - [Architecture.md](./Architecture.md) - 系统架构总设计

---

## 1. 概述

### 1.1 文档目的

本文档详细定义 Canvas-MCP 提供的 MCP 工具接口，供 AI Agent（Claude Code）调用，实现对 BIMCanvas 画布的智能操作。

### 1.2 设计原则

| 原则 | 说明 |
|------|------|
| **JSON 核心** | 所有数据操作基于 JSON，不涉及 SVG 渲染细节 |
| **乐观锁** | 修改操作需携带 `expectedVersion`，防止并发冲突 |
| **意图声明** | AI 修改时必须说明 `intent`，便于追溯和用户理解 |
| **变更感知** | 每次响应附带 `pendingCommits`，让 AI 感知用户修改 |
| **幂等设计** | 查询类工具幂等，可安全重复调用 |

### 1.3 坐标与单位

> 详见 [Architecture.md §7 坐标系统](./Architecture.md#7-坐标系统)

**MCP 工具坐标约定**：
- **坐标单位**：毫米 (mm)，Y-up 坐标系（CAD 标准）
- **角度单位**：度 (°)，逆时针为正
- **原点**：画布左下角

### 1.4 通用响应结构

所有工具响应遵循统一格式：

```json
{
  "success": true,
  "data": { ... },
  "version": 43,
  "pendingCommits": [
    {
      "changeSetId": "cs_001",
      "summary": "用户移动了沙发",
      "timestamp": "2025-12-02T15:30:00Z",
      "changesCount": 1
    }
  ]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `success` | boolean | 操作是否成功 |
| `data` | object | 工具特定的返回数据 |
| `version` | number | 当前画布版本号 |
| `pendingCommits` | array | 待 AI 确认的用户变更 |

### 1.5 错误响应结构

```json
{
  "success": false,
  "error": "VERSION_CONFLICT",
  "message": "版本冲突：期望 42，实际 43",
  "currentVersion": 43,
  "hint": "请调用 canvas_describe() 获取最新状态后重试"
}
```

---

## 2. 画布管理工具

### 2.1 canvas_create

**创建新画布**

从 Revit 数据或空白模板创建画布。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `name` | string | 是 | 画布名称 |
| `width` | number | 是 | 画布宽度 (mm) |
| `height` | number | 是 | 画布高度 (mm) |
| `revitData` | object | 否 | Revit 导出的建筑数据 |
| `projectConfig` | object | 否 | 项目配置（风格、家庭成员等） |
| `designIntent` | string | 否 | 设计诉求描述 |

#### 请求示例

```json
{
  "tool": "canvas_create",
  "params": {
    "name": "客厅设计方案",
    "width": 8000,
    "height": 6000,
    "revitData": {
      "walls": [...],
      "doors": [...],
      "windows": [...]
    },
    "projectConfig": {
      "style": "modern",
      "spaceType": "livingRoom",
      "familyMembers": { "adults": 2, "children": 1 }
    },
    "designIntent": "现代简约风格三口之家客厅"
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "canvasId": "canvas_001",
    "name": "客厅设计方案",
    "version": 1,
    "bounds": { "width": 8000, "height": 6000 },
    "zonesCount": 0,
    "elementsCount": 0
  },
  "version": 1,
  "pendingCommits": []
}
```

---

### 2.2 canvas_describe

**获取画布描述（AI 友好）**

返回画布的自然语言描述，优化 AI Token 使用。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `includeRelations` | boolean | 否 | 是否包含空间关系描述，默认 true |
| `focusZoneId` | string | 否 | 聚焦特定区域，只描述该区域 |

#### 请求示例

```json
{
  "tool": "canvas_describe",
  "params": {
    "canvasId": "canvas_001"
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "text": "客厅区域（8m × 6m）共有 3 件家具：\n\n1. 三人沙发（f_001）：位于房间南侧中央（3000, 1500），面向北墙，属于客厅主区。\n2. 圆形茶几（f_002）：位于沙发正前方 650mm 处（3000, 2500），便于使用。\n3. 阅读单椅（f_003）：位于东北角阅读区（7000, 4500），斜向摆放利用自然光。\n\n空间关系：沙发面向窗户方向，茶几与沙发保持合理距离，阅读椅靠近窗户。",
    "summary": {
      "totalElements": 3,
      "byZone": {
        "zone_living": 2,
        "zone_reading": 1
      },
      "byCategory": {
        "seating": 2,
        "table": 1
      }
    },
    "staleAfterMs": 30000
  },
  "version": 15,
  "pendingCommits": []
}
```

| 返回字段 | 说明 |
|---------|------|
| `text` | 自然语言描述，AI 可直接理解 |
| `summary` | 统计摘要 |
| `staleAfterMs` | 描述过期时间（毫秒），超时后建议重新获取 |

---

### 2.3 canvas_get_state

**获取完整画布状态**

返回完整的 CanvasDocument JSON，适用于需要详细数据的场景。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `includeStructure` | boolean | 否 | 是否包含建筑结构，默认 false |
| `includeRelations` | boolean | 否 | 是否包含空间关系，默认 true |

#### 请求示例

```json
{
  "tool": "canvas_get_state",
  "params": {
    "canvasId": "canvas_001",
    "includeStructure": false
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "document": {
      "id": "canvas_001",
      "name": "客厅设计方案",
      "version": 15,
      "bounds": { "width": 8000, "height": 6000 },
      "grid": { "size": 500, "visible": true },
      "zones": [...],
      "elements": [...],
      "spatialRelations": [...]
    }
  },
  "version": 15,
  "pendingCommits": []
}
```

> **注意**：此工具返回大量数据，请优先使用 `canvas_describe` 获取概览。

---

### 2.4 canvas_screenshot

**获取画布截图**

生成画布的 PNG 截图，用于 AI 视觉验证。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `format` | string | 否 | 图片格式：`png`（默认）/ `jpeg` |
| `quality` | number | 否 | JPEG 质量 0-100，默认 85 |
| `scale` | number | 否 | 缩放比例，默认 1.0 |
| `focusArea` | object | 否 | 聚焦区域 `{ x, y, width, height }` |

#### 请求示例

```json
{
  "tool": "canvas_screenshot",
  "params": {
    "canvasId": "canvas_001",
    "format": "png",
    "scale": 0.5
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "imageBase64": "iVBORw0KGgoAAAANSUhEUgAA...",
    "mimeType": "image/png",
    "width": 4000,
    "height": 3000
  },
  "version": 15,
  "pendingCommits": []
}
```

---

### 2.5 canvas_export

**导出画布 JSON**

将画布导出为 JSON 文件，用于 Revit 同步或存档。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `filePath` | string | 否 | 导出文件路径（不指定则返回 JSON 内容） |
| `includeMetadata` | boolean | 否 | 是否包含元数据，默认 true |

#### 请求示例

```json
{
  "tool": "canvas_export",
  "params": {
    "canvasId": "canvas_001",
    "filePath": "C:/exports/canvas_001.json"
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "filePath": "C:/exports/canvas_001.json",
    "fileSize": 12580,
    "elementsCount": 3
  },
  "version": 15,
  "pendingCommits": []
}
```

---

## 3. 元素操作工具

### 3.1 element_add

**添加家具元素**

在画布上添加新的家具元素。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `expectedVersion` | number | 是 | **乐观锁**：期望的画布版本号 |
| `familyId` | string | 是 | 族库中的家具 ID |
| `position` | Point2D | 是 | 中心点位置 `{ x, y }` (mm)，Y-up 坐标系 |
| `rotation` | number | 否 | 旋转角度（度），逆时针为正，默认 0 |
| `zoneId` | string | 否 | 所属功能区域 ID |
| `intent` | string | **是** | **AI 必填**：添加此元素的意图说明 |

#### 请求示例

```json
{
  "tool": "element_add",
  "params": {
    "canvasId": "canvas_001",
    "expectedVersion": 15,
    "familyId": "sofa_3seat_modern",
    "position": { "x": 3000, "y": 1500 },
    "rotation": 0,
    "zoneId": "zone_living",
    "intent": "在客厅中央放置三人沙发，面向电视墙方向"
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "elementId": "f_004",
    "familyName": "三人沙发-现代款",
    "position": { "x": 3000, "y": 1500 },
    "gridPosition": { "row": 3, "col": 6 },
    "bounds": { "width": 2100, "depth": 900, "height": 850 },
    "zoneId": "zone_living",
    "zoneName": "客厅主区",
    "collisions": [],
    "newRelations": [
      { "type": "regional", "relation": "inZone", "zoneId": "zone_living" },
      { "type": "geometric", "relation": "facing", "object": "wall_001" }
    ]
  },
  "version": 16,
  "pendingCommits": []
}
```

| 返回字段 | 说明 |
|---------|------|
| `elementId` | 新元素的 ID |
| `gridPosition` | 自动计算的网格位置 |
| `collisions` | 与其他元素的碰撞（空数组表示无碰撞） |
| `newRelations` | 自动计算的空间关系 |

---

### 3.2 element_move

**移动家具元素**

移动指定元素到新位置。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `expectedVersion` | number | 是 | **乐观锁**：期望的画布版本号 |
| `elementId` | string | 是 | 要移动的元素 ID |
| `position` | Point2D | 是 | 新的中心点位置 `{ x, y }` (mm)，Y-up 坐标系 |
| `intent` | string | **是** | **AI 必填**：移动此元素的意图说明 |

#### 请求示例

```json
{
  "tool": "element_move",
  "params": {
    "canvasId": "canvas_001",
    "expectedVersion": 16,
    "elementId": "f_001",
    "position": { "x": 3500, "y": 1500 },
    "intent": "将沙发向右移动500mm，与茶几对齐"
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "elementId": "f_001",
    "previousPosition": { "x": 3000, "y": 1500 },
    "newPosition": { "x": 3500, "y": 1500 },
    "newGridPosition": { "row": 3, "col": 7 },
    "zoneChanged": false,
    "collisions": [],
    "updatedRelations": [
      { "type": "geometric", "relation": "alignedWith", "object": "f_002" }
    ]
  },
  "version": 17,
  "pendingCommits": []
}
```

---

### 3.3 element_rotate

**旋转家具元素**

旋转指定元素。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `expectedVersion` | number | 是 | **乐观锁**：期望的画布版本号 |
| `elementId` | string | 是 | 要旋转的元素 ID |
| `angle` | number | 是 | 旋转角度（度），逆时针为正，支持绝对值或相对值 |
| `relative` | boolean | 否 | 是否相对旋转，默认 false（绝对角度） |
| `intent` | string | **是** | **AI 必填**：旋转此元素的意图说明 |

#### 请求示例

```json
{
  "tool": "element_rotate",
  "params": {
    "canvasId": "canvas_001",
    "expectedVersion": 17,
    "elementId": "f_001",
    "angle": 90,
    "relative": false,
    "intent": "将沙发旋转90度，使其面向窗户方向"
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "elementId": "f_001",
    "previousRotation": 0,
    "newRotation": 90,
    "collisions": [],
    "updatedRelations": [
      { "type": "geometric", "relation": "facing", "object": "window_001" }
    ]
  },
  "version": 18,
  "pendingCommits": []
}
```

---

### 3.4 element_delete

**删除家具元素**

从画布删除指定元素。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `expectedVersion` | number | 是 | **乐观锁**：期望的画布版本号 |
| `elementId` | string | 是 | 要删除的元素 ID |
| `intent` | string | **是** | **AI 必填**：删除此元素的意图说明 |

#### 请求示例

```json
{
  "tool": "element_delete",
  "params": {
    "canvasId": "canvas_001",
    "expectedVersion": 18,
    "elementId": "f_003",
    "intent": "移除阅读椅，用户表示不需要阅读区"
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "deletedElement": {
      "id": "f_003",
      "familyName": "阅读单椅",
      "position": { "x": 7000, "y": 4500 }
    },
    "removedRelations": 3
  },
  "version": 19,
  "pendingCommits": []
}
```

---

### 3.5 element_list

**列出家具元素**

获取画布上的家具元素列表。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `zoneId` | string | 否 | 筛选特定区域的元素 |
| `category` | string | 否 | 筛选特定类别（seating, table 等） |

#### 请求示例

```json
{
  "tool": "element_list",
  "params": {
    "canvasId": "canvas_001",
    "zoneId": "zone_living"
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "elements": [
      {
        "id": "f_001",
        "familyId": "sofa_3seat_modern",
        "familyName": "三人沙发-现代款",
        "category": "seating",
        "position": { "x": 3500, "y": 1500 },
        "gridPosition": { "row": 3, "col": 7 },
        "rotation": 90,
        "zoneId": "zone_living"
      },
      {
        "id": "f_002",
        "familyId": "coffee_table_round",
        "familyName": "圆形茶几",
        "category": "table",
        "position": { "x": 3000, "y": 2500 },
        "gridPosition": { "row": 5, "col": 6 },
        "rotation": 0,
        "zoneId": "zone_living"
      }
    ],
    "totalCount": 2
  },
  "version": 19,
  "pendingCommits": []
}
```

---

## 4. 版本控制工具

### 4.1 canvas_get_changes

**获取待处理的用户变更**

查询用户通过 Web 画布提交的变更，尚未被 AI 确认。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `includeDetails` | boolean | 否 | 是否包含变更详情，默认 true |

#### 请求示例

```json
{
  "tool": "canvas_get_changes",
  "params": {
    "canvasId": "canvas_001",
    "includeDetails": true
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "pendingCommits": [
      {
        "changeSetId": "cs_001",
        "timestamp": "2025-12-02T15:30:00Z",
        "summary": "移动了沙发到窗边",
        "userId": "user_abc",
        "baseVersion": 17,
        "targetVersion": 19,
        "changes": [
          {
            "action": "move",
            "elementId": "f_001",
            "elementName": "三人沙发-现代款",
            "from": { "x": 3000, "y": 1500 },
            "to": { "x": 5000, "y": 4000 }
          }
        ]
      }
    ],
    "hasUnacknowledged": true,
    "totalPending": 1
  },
  "version": 19,
  "pendingCommits": []
}
```

---

### 4.2 canvas_ack_commits

**确认已处理用户变更**

AI 处理完用户变更后，调用此工具确认。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `changeSetIds` | string[] | 是 | 要确认的变更集 ID 列表 |
| `aiResponse` | string | 否 | AI 对变更的响应说明 |

#### 请求示例

```json
{
  "tool": "canvas_ack_commits",
  "params": {
    "canvasId": "canvas_001",
    "changeSetIds": ["cs_001"],
    "aiResponse": "已注意到沙发位置变更，将相应调整茶几位置"
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "acknowledgedCount": 1,
    "remainingPending": 0
  },
  "version": 19,
  "pendingCommits": []
}
```

---

## 5. 查询分析工具

### 5.1 element_at

**查询指定位置的元素**

查询给定坐标点处的元素。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `position` | Point2D | 是 | 查询位置 `{ x, y }` (mm)，Y-up 坐标系 |
| `radius` | number | 否 | 搜索半径 (mm)，默认 0（精确点） |

#### 请求示例

```json
{
  "tool": "element_at",
  "params": {
    "canvasId": "canvas_001",
    "position": { "x": 3000, "y": 2500 },
    "radius": 100
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "elementsFound": [
      {
        "id": "f_002",
        "familyName": "圆形茶几",
        "category": "table",
        "distance": 0,
        "containsPoint": true
      }
    ],
    "zoneAtPosition": {
      "zoneId": "zone_living",
      "zoneName": "客厅主区"
    },
    "nearbyStructure": [
      { "type": "wall", "id": "wall_001", "distance": 2500 }
    ]
  },
  "version": 19,
  "pendingCommits": []
}
```

---

### 5.2 space_analyze

**空间分析**

分析画布的空间使用情况，识别问题和机会。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `analysisTypes` | string[] | 否 | 分析类型，默认全部 |

分析类型：
- `circulation`：动线分析
- `density`：密度分析
- `lighting`：采光分析
- `collision`：碰撞检测
- `suggestion`：布置建议

#### 请求示例

```json
{
  "tool": "space_analyze",
  "params": {
    "canvasId": "canvas_001",
    "analysisTypes": ["circulation", "collision", "suggestion"]
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "circulation": {
      "status": "good",
      "mainPaths": [
        { "from": "door_001", "to": "zone_living", "width": 1200, "blocked": false }
      ],
      "issues": []
    },
    "collision": {
      "status": "ok",
      "collisions": [],
      "warnings": [
        {
          "elementId": "f_001",
          "issue": "距离窗户过近",
          "distance": 200,
          "recommendedDistance": 500
        }
      ]
    },
    "suggestion": {
      "emptyAreas": [
        {
          "position": { "x": 6500, "y": 1500 },
          "size": { "width": 1500, "height": 1500 },
          "suggestedUse": "可添加落地灯或装饰植物"
        }
      ],
      "improvements": [
        "建议将沙发后移 300mm，与墙保持适当距离",
        "阅读区可添加小书架增强功能性"
      ]
    }
  },
  "version": 19,
  "pendingCommits": []
}
```

---

### 5.3 relation_get

**获取元素空间关系**

获取指定元素与其他元素/结构的空间关系。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |
| `elementId` | string | 是 | 目标元素 ID |
| `relationTypes` | string[] | 否 | 关系类型筛选，默认全部 |

关系类型：`geometric`, `regional`, `semantic`, `distance`

#### 请求示例

```json
{
  "tool": "relation_get",
  "params": {
    "canvasId": "canvas_001",
    "elementId": "f_001"
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "elementId": "f_001",
    "elementName": "三人沙发-现代款",
    "relations": [
      {
        "type": "geometric",
        "relation": "facing",
        "object": "window_001",
        "objectName": "北侧窗户"
      },
      {
        "type": "geometric",
        "relation": "adjacentTo",
        "object": "f_002",
        "objectName": "圆形茶几"
      },
      {
        "type": "regional",
        "relation": "inZone",
        "zoneId": "zone_living",
        "zoneName": "客厅主区"
      },
      {
        "type": "semantic",
        "relation": "backToWall",
        "object": "wall_003",
        "objectName": "南墙"
      },
      {
        "type": "distance",
        "relation": "within",
        "object": "f_002",
        "objectName": "圆形茶几",
        "threshold": 1000,
        "actualDistance": 650
      }
    ]
  },
  "version": 19,
  "pendingCommits": []
}
```

---

## 6. 区域管理工具

### 6.1 zone_list

**列出功能区域**

获取画布上定义的功能区域列表。

#### 参数

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `canvasId` | string | 是 | 画布 ID |

#### 请求示例

```json
{
  "tool": "zone_list",
  "params": {
    "canvasId": "canvas_001"
  }
}
```

#### 返回值

```json
{
  "success": true,
  "data": {
    "zones": [
      {
        "id": "zone_living",
        "name": "客厅主区",
        "function": "living",
        "area": 36000000,
        "elementsCount": 2,
        "suggestedFurniture": ["sofa", "coffeeTable", "tvStand"]
      },
      {
        "id": "zone_reading",
        "name": "阅读角",
        "function": "study",
        "area": 6000000,
        "elementsCount": 0,
        "suggestedFurniture": ["armchair", "floorLamp", "bookshelf"]
      }
    ],
    "totalCount": 2
  },
  "version": 19,
  "pendingCommits": []
}
```

---

## 7. 错误码参考

### 7.1 通用错误

| 错误码 | 说明 | 处理建议 |
|--------|------|---------|
| `CANVAS_NOT_FOUND` | 画布不存在 | 检查 canvasId 是否正确 |
| `ELEMENT_NOT_FOUND` | 元素不存在 | 检查 elementId 是否正确 |
| `INVALID_PARAMS` | 参数无效 | 检查参数类型和格式 |
| `INTERNAL_ERROR` | 服务器内部错误 | 稍后重试 |

### 7.2 版本控制错误

| 错误码 | 说明 | 处理建议 |
|--------|------|---------|
| `VERSION_CONFLICT` | 版本冲突 | 调用 `canvas_describe()` 获取最新状态后重试 |
| `VERSION_MISSING` | 未提供版本号 | 修改操作必须携带 `expectedVersion` |

### 7.3 业务规则错误

| 错误码 | 说明 | 处理建议 |
|--------|------|---------|
| `COLLISION_DETECTED` | 检测到碰撞 | 调整元素位置或确认碰撞可接受 |
| `OUT_OF_BOUNDS` | 元素超出画布边界 | 调整元素位置 |
| `FAMILY_NOT_FOUND` | 族不存在 | 检查 familyId 或使用 Library-MCP 搜索 |
| `INTENT_REQUIRED` | 缺少意图说明 | AI 修改操作必须提供 `intent` 参数 |

### 7.4 错误响应示例

```json
{
  "success": false,
  "error": "VERSION_CONFLICT",
  "message": "版本冲突：期望版本 15，当前版本 17",
  "currentVersion": 17,
  "hint": "画布已被修改，请调用 canvas_describe() 获取最新状态后重试",
  "pendingCommits": [
    {
      "changeSetId": "cs_002",
      "summary": "用户添加了落地灯",
      "timestamp": "2025-12-02T15:45:00Z"
    }
  ]
}
```

---

## 8. 最佳实践

### 8.1 工具调用流程

```
1. 首次了解画布
   └─ canvas_describe() → 获取自然语言描述

2. 规划家具布置
   └─ zone_list() → 了解功能区域
   └─ space_analyze() → 分析可用空间

3. 执行布置操作（每次操作后版本号递增）
   └─ element_add() → 添加家具
   └─ element_move() → 调整位置
   └─ element_rotate() → 调整朝向

4. 验证布置效果
   └─ canvas_screenshot() → 获取截图视觉验证
   └─ relation_get() → 检查空间关系

5. 感知用户修改
   └─ canvas_get_changes() → 获取用户变更
   └─ canvas_ack_commits() → 确认已处理
```

### 8.2 乐观锁处理

```
AI 调用 element_add(expectedVersion: 15)
    │
    ├─ 成功 → 继续下一步操作（version 变为 16）
    │
    └─ 失败（VERSION_CONFLICT, currentVersion: 17）
         │
         └─ canvas_describe() → 获取最新状态
              │
              └─ 重新规划并重试（expectedVersion: 17）
```

### 8.3 意图说明规范

**好的意图说明：**
- "在客厅中央放置三人沙发，面向电视墙方向"
- "将沙发向右移动500mm，与茶几对齐"
- "移除阅读椅，用户表示不需要阅读区"

**不好的意图说明：**
- "添加沙发"（缺少位置和原因）
- "移动"（缺少具体说明）
- ""（空字符串）

### 8.4 Token 优化

| 场景 | 推荐工具 | 说明 |
|------|---------|------|
| 了解画布概况 | `canvas_describe` | 自然语言描述，Token 效率高 |
| 需要精确数据 | `canvas_get_state` | 完整 JSON，数据量大 |
| 检查特定元素 | `relation_get` | 聚焦单个元素的关系 |
| 视觉验证 | `canvas_screenshot` | 多模态能力，直观验证 |

---

## 9. 附录

### 9.1 类型定义

```typescript
// 2D 坐标点（Y-up 坐标系）
interface Point2D {
  x: number;  // mm, 向右为正
  y: number;  // mm, 向上为正
}

// 网格位置
interface GridPosition {
  row: number;
  col: number;
}

// 3D 尺寸
interface Bounds3D {
  width: number;   // mm (X 方向)
  depth: number;   // mm (Y 方向)
  height: number;  // mm (Z 方向)
}

// 空间关系
interface SpatialRelation {
  type: 'geometric' | 'regional' | 'semantic' | 'distance';
  subject: string;      // 主体元素 ID
  relation: string;     // 关系类型
  object?: string;      // 客体元素 ID（如有）
  zoneId?: string;      // 区域 ID（regional 类型）
  threshold?: number;   // 阈值（distance 类型）
  actualDistance?: number;
}

// 变更集
interface ChangeSet {
  changeSetId: string;
  timestamp: string;
  summary: string;
  userId: string;
  baseVersion: number;
  targetVersion: number;
  changes: Change[];
}

// 单个变更
interface Change {
  action: 'add' | 'move' | 'rotate' | 'delete';
  elementId: string;
  from?: any;
  to?: any;
}
```

### 9.2 关系类型速查

| 类型 | 可用值 |
|------|--------|
| **geometric** | leftOf, rightOf, above, below, facing, backTo, alignedWith, parallel, perpendicular, adjacentTo |
| **regional** | inZone, atBoundary, spanningZones |
| **semantic** | servingZone, blockingPath, nearWindow, nearDoor, awayFromDoor, facingEntrance, backToWall, centerOfRoom, cornerPlacement |
| **distance** | within, beyond, exactlyAt, tooClose, tooFar |

### 9.3 家具类别

| 类别 | 说明 | 示例 |
|------|------|------|
| `seating` | 座椅类 | 沙发、椅子、凳子 |
| `table` | 桌类 | 茶几、餐桌、书桌 |
| `storage` | 储物类 | 柜子、架子、衣柜 |
| `bed` | 床类 | 单人床、双人床 |
| `lighting` | 灯具类 | 落地灯、台灯 |
| `decor` | 装饰类 | 地毯、挂画、绿植 |
| `appliance` | 电器类 | 电视、冰箱 |

---

## 版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| v1.1 | 2026-01-13 | **坐标系统修正**：从 Y-down 改为 Y-up（CAD 标准）；原点从左上角改为左下角；旋转从顺时针改为逆时针；新增 Web 端渲染转换说明 |
| v1.0 | 2025-12-02 | 初始版本，定义完整 Canvas-MCP 工具集 |
