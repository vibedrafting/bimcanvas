# BIMCanvas 数据架构 v3.0

> **状态**: 草稿
> **日期**: 2025-12-25
> **上下文**: [DataStructureRefactoring_Review](../reviews/DataStructureRefactoring_Review.md)

## 1. 概述

v3.0 引入了 **多仓库 + Git 分支 (Multi-Repo + Git Branching)** 架构。
- **项目 (Project)**: 策略的集合。
- **策略 (Strategy)**: 一个独立的设计方向（独立的 Git 仓库）。
- **变体 (Variant)**: 策略的线性演化版本（Git 分支）。
- **基准 (Baseline)**: 只读的 Revit 导出数据。

## 2. 文件结构

```text
MyDesignProject/
├── project.json                  # [入口] 项目清单
├── baseline/                     # [L0] 只读 Revit 数据
│   ├── architecture.json
│   ├── location_lines.json
│   └── ...
├── schemes/                      # [L1] 策略集合
│   ├── s1_Flow/                  # 策略仓库
│   │   ├── strategy.json         # 策略元数据
│   │   ├── zones.json            # 分区数据
│   │   ├── finishes.json         # 完成面覆盖
│   │   └── modules.json          # 布置数据
│   └── s2_Space/
└── Assets/                       # 全局资产
```

## 3. JSON Schema 定义

### 3.1 项目清单 (`project.json`)

位于项目根目录。定义当前的活动上下文。

```json
{
  "id": "string",               // 项目 ID
  "name": "string",             // 人类可读名称
  "version": "3.0",             // Schema 版本
  "activeSchemeId": "string",   // 当前激活的策略 ID
  "schemes": [                  // 已注册的策略
    {
      "id": "string",           // 策略 ID (必须匹配文件夹名)
      "path": "string",         // 相对路径, 例如 "./schemes/s1_Flow"
      "name": "string"          // 显示名称
    }
  ]
}
```

### 3.2 策略元数据 (`strategy.json`)

位于策略根目录 (例如 `schemes/s1_Flow/strategy.json`)。

```json
{
  "id": "string",               // 策略 ID
  "name": "string",             // 策略名称
  "type": "strategy",           // 固定值
  "description": "string",      // 设计意图描述
  
  // 衍生追踪
  "origin": {
    "sourceRepo": "string",     // 父仓库路径, 例如 "../s1_Flow"
    "sourceBranch": "string",   // 源分支名称, 例如 "v1_backup"
    "sourceCommit": "string",   // 源提交哈希
    "derivedAt": "ISO8601"      // 时间戳
  }, // 如果是从零创建则为 null

  // 基准验证
  "baselineRef": "../../baseline", // 基准文件夹路径
  "lastValidatedBaselineHash": "string", // 基准文件夹内容的哈希
  "status": "valid|dirty|invalid"
}
```

### 3.3 分区数据 (`zones.json`)

定义房间内的功能分区。

```json
{
  "zones": [
    {
      "id": "string",           // 分区 ID
      "name": "string",         // 分区名称
      "roomId": "string",       // 引用 Revit 房间 ID
      "tags": ["string"],       // 例如 ["sleep", "storage"]
      "boundary": [             // Polygon2D
        [x, y], [x, y], ...
      ]
    }
  ]
}
```

### 3.4 布置数据 (`modules.json`)

定义家具布置。

```json
{
  "modules": [
    {
      "id": "string",           // 模块 ID
      "zoneId": "string",       // 引用分区 ID
      "moduleTypeId": "string", // SKU 或族类型
      "bounds": [               // OBB (定向包围盒)
        [x, y], [x, y], [x, y], [x, y]
      ],
      "facing": "string"        // "north", "south", 等
    }
  ]
}
```

### 3.5 完成面覆盖 (`finishes.json`)

定义墙面完成面配置，覆盖基准定位线。

```json
{
  "overrides": [
    {
      "locationLineId": "string", // 引用基准线 ID
      "segments": [
        {
          "range": [0, 2500],     // [StartMm, EndMm] 沿线段
          "finishType": "string", // 材质/完成面 ID
          "thickness": 15         // 厚度 (mm)
        }
      ]
    }
  ]
}
```
