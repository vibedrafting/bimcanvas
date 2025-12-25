# BIMCanvas Data Schema v3.0

> **Status**: Draft
> **Date**: 2025-12-25
> **Context**: [DataStructureRefactoring_Review](../reviews/DataStructureRefactoring_Review.md)

## 1. Overview

v3.0 introduces a **Multi-Repo + Git Branching** architecture.
- **Project**: A collection of strategies.
- **Strategy**: A distinct design direction (independent Git repository).
- **Variant**: A linear evolution of a strategy (Git branch).
- **Baseline**: Read-only Revit export data.

## 2. File Structure

```text
MyDesignProject/
├── project.json                  # [Entry] Project Manifest
├── baseline/                     # [L0] Read-only Revit Data
│   ├── architecture.json
│   ├── location_lines.json
│   └── ...
├── schemes/                      # [L1] Strategies Collection
│   ├── s1_Flow/                  # Strategy Repository
│   │   ├── strategy.json         # Strategy Metadata
│   │   ├── zones.json            # Zoning Data
│   │   ├── finishes.json         # Finish Overrides
│   │   └── modules.json          # Layout Data
│   └── s2_Space/
└── Assets/                       # Global Assets
```

## 3. JSON Schemas

### 3.1 Project Manifest (`project.json`)

Located at project root. Defines the active context.

```json
{
  "id": "string",               // Project ID
  "name": "string",             // Human readable name
  "version": "3.0",             // Schema version
  "activeSchemeId": "string",   // ID of the currently active strategy
  "schemes": [                  // Registered strategies
    {
      "id": "string",           // Strategy ID (must match folder name)
      "path": "string",         // Relative path, e.g., "./schemes/s1_Flow"
      "name": "string"          // Display name
    }
  ]
}
```

### 3.2 Strategy Metadata (`strategy.json`)

Located at strategy root (e.g., `schemes/s1_Flow/strategy.json`).

```json
{
  "id": "string",               // Strategy ID
  "name": "string",             // Strategy Name
  "type": "strategy",           // Fixed value
  "description": "string",      // Design intent description
  
  // Derivation Tracking
  "origin": {
    "sourceRepo": "string",     // Path to parent repo, e.g., "../s1_Flow"
    "sourceBranch": "string",   // Source branch name, e.g., "v1_backup"
    "sourceCommit": "string",   // Source commit hash
    "derivedAt": "ISO8601"      // Timestamp
  }, // Nullable if created from scratch

  // Baseline Validation
  "baselineRef": "../../baseline", // Path to baseline folder
  "lastValidatedBaselineHash": "string", // Hash of baseline folder content
  "status": "valid|dirty|invalid"
}
```

### 3.3 Zoning Data (`zones.json`)

Defines functional zones within rooms.

```json
{
  "zones": [
    {
      "id": "string",           // Zone ID
      "name": "string",         // Zone Name
      "roomId": "string",       // Reference to Revit Room ID
      "tags": ["string"],       // e.g., ["sleep", "storage"]
      "boundary": [             // Polygon2D
        [x, y], [x, y], ...
      ]
    }
  ]
}
```

### 3.4 Layout Data (`modules.json`)

Defines furniture placement.

```json
{
  "modules": [
    {
      "id": "string",           // Module ID
      "zoneId": "string",       // Reference to Zone ID
      "moduleTypeId": "string", // SKU or Family Type
      "bounds": [               // OBB (Oriented Bounding Box)
        [x, y], [x, y], [x, y], [x, y]
      ],
      "facing": "string"        // "north", "south", etc.
    }
  ]
}
```

### 3.5 Finish Overrides (`finishes.json`)

Defines wall finish configurations, overriding the baseline location lines.

```json
{
  "overrides": [
    {
      "locationLineId": "string", // Reference to Baseline Line ID
      "segments": [
        {
          "range": [0, 2500],     // [StartMm, EndMm] along the line
          "finishType": "string", // Material/Finish ID
          "thickness": 15         // Thickness in mm
        }
      ]
    }
  ]
}
```
