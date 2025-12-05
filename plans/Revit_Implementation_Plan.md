# BIMCanvas.Revit Implementation Plan

## Goal Description
Implement the `BIMCanvas.Revit` project, which serves as the Revit plugin interface for the BIMCanvas system. This project will be responsible for extracting building data, launching the AI CLI, and eventually synchronizing changes back to Revit.

## User Review Required
> [!IMPORTANT]
> **Revit API References**: The project assumes Revit 2025 (or compatible) API references. Please ensure the `RevitAPI.dll` and `RevitAPIUI.dll` paths in the `.csproj` match your local Revit installation, or use a NuGet package if preferred. The plan defaults to a standard path or NuGet `Revit_All_Main_Versions_API_x64`.

## Proposed Changes

### 1. Project Initialization
#### [NEW] [BIMCanvas.Revit.csproj](file:///e:/工作文档/开发类/MyCode/BIMCanvas/BIMCanvas.Revit/BIMCanvas.Revit.csproj)
- Target Framework: `.NET Framework 4.7.2`
- References:
    - `BIMCanvas.Core` (Project Reference)
    - `RevitAPI`, `RevitAPIUI` (NuGet or Local)
    - `PresentationCore`, `PresentationFramework`, `WindowsBase` (WPF)

#### [NEW] [BIMCanvas.Revit.addin](file:///e:/工作文档/开发类/MyCode/BIMCanvas/BIMCanvas.Revit/BIMCanvas.Revit.addin)
- Revit Add-in Manifest file.

### 2. Application Entry Point
#### [NEW] [App.cs](file:///e:/工作文档/开发类/MyCode/BIMCanvas/BIMCanvas.Revit/App.cs)
- Implements `IExternalApplication`.
- Creates Ribbon Panel "BIMCanvas".
- Adds buttons: "Quick Layout", "Start Dialog", "Config".

### 3. Commands
#### [NEW] [QuickLayoutCommand.cs](file:///e:/工作文档/开发类/MyCode/BIMCanvas/BIMCanvas.Revit/Commands/QuickLayoutCommand.cs)
- Implements `IExternalCommand`.
- Logic: Extract View -> Show Config (Optional) -> Launch AI.

#### [NEW] [StartDialogCommand.cs](file:///e:/工作文档/开发类/MyCode/BIMCanvas/BIMCanvas.Revit/Commands/StartDialogCommand.cs)
- Implements `IExternalCommand`.
- Logic: Launch AI CLI directly.

### 4. UI (WPF)
#### [NEW] [ConfigWindow.xaml](file:///e:/工作文档/开发类/MyCode/BIMCanvas/BIMCanvas.Revit/Views/ConfigWindow.xaml)
- WPF Window for project configuration.
#### [NEW] [ConfigWindow.xaml.cs](file:///e:/工作文档/开发类/MyCode/BIMCanvas/BIMCanvas.Revit/Views/ConfigWindow.xaml.cs)
- Code-behind for ConfigWindow.

### 5. Adapters (Data Conversion)
#### [NEW] [ElementAdapter.cs](file:///e:/工作文档/开发类/MyCode/BIMCanvas/BIMCanvas.Revit/Adapters/ElementAdapter.cs)
- Converts Revit `Wall`, `FamilyInstance` (Door/Window) to `BIMCanvas.Core` models (`Wall`, `Opening`).
- Uses `Geometry` extraction.

#### [NEW] [ViewAdapter.cs](file:///e:/工作文档/开发类/MyCode/BIMCanvas/BIMCanvas.Revit/Adapters/ViewAdapter.cs)
- Extracts View Crop Box and Scale.
- Sets up `Metadata` in `CanvasDocument`.

### 6. Services
#### [NEW] [AiLauncherService.cs](file:///e:/工作文档/开发类/MyCode/BIMCanvas/BIMCanvas.Revit/Services/AiLauncherService.cs)
- Helper to launch the AI CLI process with arguments.

## Verification Plan

### Automated Tests
- **Build Verification**: Run `dotnet build BIMCanvas.Revit` to ensure all references and dependencies are correct.
- **Note**: Unit testing Revit API code is difficult without a running Revit instance. We will rely on build verification and manual testing.

### Manual Verification
1.  **Load Add-in**: Copy `.addin` and DLLs to Revit Add-ins folder.
2.  **Ribbon Check**: Verify "BIMCanvas" tab and buttons appear in Revit.
3.  **Command Execution**: Click "Quick Layout" and verify it attempts to extract data (or shows a placeholder message if AI CLI is not connected yet).
