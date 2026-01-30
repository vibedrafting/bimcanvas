# 后台截图系统（Background Screenshot）

> 本文档描述 BIMCanvas 后台截图的系统构成、配置项与调用方式。

## 1. 目标与原则

- **目标**：在不影响前端 UI 的前提下，通过后台接口生成与“前端截图功能”一致的图片。
- **一致性**：复用 Web 端 Three.js 渲染与 ScreenshotService 的合成逻辑（WebGL + LabelRenderer）。
- **静默运行**：后台通过 Playwright 打开渲染页，不占用用户前台视窗。

## 2. 架构概览

```
Server (BIMCanvas.Server)
  └─ BackgroundScreenshotController
      └─ BackgroundScreenshotService
          └─ Playwright (Chromium Headless)
              └─ Web /screenshot-render (ScreenshotRenderView)
                  └─ ScreenshotService.captureCanvas()
```

关键点：
- **渲染页**：`/screenshot-render` 为无 UI 的渲染页，仅用于截图输出。
- **图层与标签**：图层由 LayerManager 控制，标签通过 LabelRenderer 绘制（保证与前端一致）。
- **数据来源**：`projectPath` 指向解压后的项目目录（含 `project.json`、`baseline/*`、`computed/*`）。

## 3. 配置与环境

### 3.1 Web 服务地址

后台截图需要可访问的 Web 前端（Vite 或已部署的 Web）。

可通过配置项指定：
- `Web:BaseUrl`（appsettings）
- 环境变量：`BIMCANVAS_WEB_URL`
- 默认：`http://localhost:5173`

### 3.2 Playwright 安装

首次运行需要安装浏览器：

```powershell
pwsh BIMCanvas.Server\bin\Debug\net8.0\playwright.ps1 install
```

## 4. API 调用

### 4.1 接口

单张截图：
`POST /api/screenshot/render`

批量截图：
`POST /api/screenshot/render-batch`

返回：
```json
{ "imageData": "data:image/png;base64,..." }
```

批量返回：
```json
{
  "items": [
    { "name": "full_user_autofit", "imageData": "data:image/png;base64,...", "elapsedMs": 1200 },
    { "name": "room_r_3_fixed_16_9", "error": "xxx", "elapsedMs": 3000 }
  ]
}
```

### 4.2 单张请求参数

```json
{
  "projectPath": "C:\\path\\to\\project",
  "strategyId": "default",
  "viewMode": "human",
  "layers": [2,10,11],
  "layerPreset": "User",
  "layerEnable": ["Labels","Zones"],
  "layerDisable": ["Furniture"],
  "viewport": { "mode": "zone", "zoneId": "rz_1" },
  "scale": 2,
  "autoFitViewport": true,
  "theme": "dark"
}
```

说明：
- `projectPath`（必填）：解压后的项目目录。
- `strategyId`：方案 ID，默认 `default`。
- `viewMode`/`layers`：**旧参数**（兼容），建议使用新图层配置。
- `layerPreset`：图层预设（`User`/`Agent`，大小写不敏感）。
- `layerEnable`/`layerDisable`：额外开启/关闭的图层名称（字符串）。
  - **关闭优先**：同名同时出现时，`layerDisable` 生效。
  - 名称忽略大小写、空格、下划线、连字符。
  - 支持：`Grid`, `Architecture`, `Furniture`, `Labels`, `Bounds`, `Outline`,
    `SVG`, `SVG Preview`, `Zones`, `Semantic`, `AI Vision`, `Model`。
- `viewport`：
  - `mode = full | room | zone | bounds`
  - `roomId`：房间 ID（来自 `baseline/rooms.json`，如 `r_1`）
  - `zoneId`：设计区 ID（来自 `schemes/zones.json`，如 `rz_1`）
  - `bounds`：手动范围（见 §4.4）
- `scale`：1-4，放大像素密度。
- `autoFitViewport`：是否自动按范围计算输出比例（默认 `true`）。
- `theme`：`dark`/`light`。

### 4.3 批量请求参数

```json
{
  "projectPath": "C:\\path\\to\\project",
  "strategyId": "default",
  "scale": 2,
  "autoFitViewport": true,
  "theme": "dark",
  "items": [
    {
      "name": "full_user_autofit",
      "layerPreset": "User",
      "viewport": { "mode": "full" }
    },
    {
      "name": "zone_rz_1_labels_zones_autofit",
      "layerPreset": "User",
      "layerEnable": ["Labels", "Zones"],
      "viewport": { "mode": "zone", "zoneId": "rz_1" },
      "autoFitViewport": true
    }
  ]
}
```

说明：
- `scale`/`theme` 在 batch 内必须一致。
- `autoFitViewport` 是 batch 的默认值，单个 item 可覆盖。
- item 可使用与单张相同的图层/视口参数（`layerPreset`、`layerEnable`、`layerDisable`、`viewport` 等）。
- `elapsedMs` 为服务端单项渲染耗时；**总墙钟时间**以客户端计时为准。

### 4.4 自动比例（autoFitViewport）

当 `autoFitViewport=true` 时：
- 根据目标范围 + 边距计算宽高比；
- 输出视口面积约等于 1920×1080；
- 最小边 720、最大边 4096；
- `scale` 会进一步放大像素密度。

当 `autoFitViewport=false` 时：
- 固定视口 1920×1080。

### 4.5 bounds 示例

```json
{
  "viewport": {
    "mode": "bounds",
    "bounds": { "minX": 1000, "minY": 1000, "maxX": 8000, "maxY": 6000 }
  }
}
```

## 5. 调用示例（PowerShell）

### 5.1 rz_1 设计区 + Labels/Zones + 自动比例

```powershell
$body = @{
  projectPath = "C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1"
  layerPreset = "User"
  layerEnable = @("Labels", "Zones")
  viewport = @{ mode = "zone"; zoneId = "rz_1" }
  autoFitViewport = $true
  scale = 2
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Method Post `
  -Uri "http://localhost:5000/api/screenshot/render" `
  -ContentType "application/json" `
  -Body $body
```

### 5.2 固定 16:9（关闭自动比例）

```powershell
$body = @{
  projectPath = "C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1"
  layerPreset = "User"
  viewport = @{ mode = "zone"; zoneId = "rz_1" }
  autoFitViewport = $false
  scale = 2
} | ConvertTo-Json -Depth 10
```

### 5.3 只开启 Grid + Architecture

```powershell
$body = @{
  projectPath = "C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1"
  layerPreset = "User"
  layerEnable = @("Grid", "Architecture")
  viewport = @{ mode = "full" }
  autoFitViewport = $true
  scale = 2
} | ConvertTo-Json -Depth 10
```

## 6. 性能与测试

### 6.1 性能注意事项

- 批量截图通过 Playwright 多页面并行渲染，硬件资源不足时可能出现 **单张耗时升高**。
- `elapsedMs` 是服务端单项渲染时间，不代表 batch 总耗时。
- **总墙钟时间**应以客户端计时为准（例如测试脚本输出的 `batch_wall_time`）。
- 批量并行度由服务端常量 `MaxBatchParallelism` 控制（见 `BackgroundScreenshotService.cs`），可根据机器性能调整。

### 6.2 测试脚本

脚本路径：`BIMCanvas.Server/tests/test_background_screenshot.py`

当前输出包含：
- `batch_wall_time`：批量请求墙钟时间
- `batch_max_item`：批量中最慢单项
- `total_wall_time`：单张模式总墙钟时间

示例：
```
Summary:
- full_user_autofit: OK (8219ms)
- ...
- batch_wall_time: 9255ms
- batch_max_item: 8819ms
```

## 7. 常见问题

- **Room not found**：`roomId` 用错。`r_1` 来自 `baseline/rooms.json`。
- **Zone not found**：`zoneId` 用错。`rz_1` 来自 `schemes/zones.json`。
- **图层不对**：确认 `layerPreset` + `layerEnable/Disable` 是否冲突，关闭优先。
- **比例不符合预期**：尝试调整 `autoFitViewport` 或 `scale`。
