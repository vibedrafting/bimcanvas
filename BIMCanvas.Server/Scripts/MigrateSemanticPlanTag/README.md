# MigrateSemanticPlanTag

BIMCanvas Phase 0（version → tag 全栈迁移）的 .bcp 项目数据清洗工具。

## 适用范围

Phase 0 改造让 Server 严格只认新字段名（`Entries` / `Tag` / `ReferenceAnalysisTag`），旧字段名（`Versions` / `Version` / `ReferenceAnalysisVersion`）写出的历史数据无法读取。本脚本批量重命名字段，不改值。

## 处理对象

| 文件 | 改动 |
|---|---|
| `schemes/*/semantic_plan.json` | 顶层 `Versions` → `Entries`；条目内 `Version` → `Tag`、`ReferenceAnalysisVersion` → `ReferenceAnalysisTag`；顶层 `referenceAnalysis`（旧版内嵌兼容字段）保持不动 |
| `schemes/*/reference_analysis.json` | 顶层数组，每条目 `Version` → `Tag` |

值（`v0.1` / `v0.2` / `v0.3` / `v1` / `v2` / ...）保持原样。

## 使用方法

```bash
# 1. 预演（不写入）
dotnet run --project BIMCanvas.Server\Scripts\MigrateSemanticPlanTag -- "C:\path\to\project" --dry-run

# 2. 实际迁移
dotnet run --project BIMCanvas.Server\Scripts\MigrateSemanticPlanTag -- "C:\path\to\project"
```

## 必读注意事项

1. **必须先 git 存档项目目录**（脚本不主动 commit）。
2. **必须先部署 Server Phase 0 适配版本再跑**——旧 Server 启动会因读不到新字段挂掉。
3. 多个 `.bcp` 项目分别执行。
4. `--dry-run` 仅预演，不写入。
5. 写入采用 `.tmp` + rename 原子模式，中断不损坏现有文件。
6. **本脚本只处理 Phase 0（字段重命名）**。modules.json wrapper 升级用 `MigrateModulesWrapper` 脚本，两者独立。

## 退出码

- `0` —— 全部成功
- `1` —— 有文件错误
- `2` —— 参数错误或项目目录不存在
