<#
.SYNOPSIS
    Initializes a new BIMCanvas Project Structure (v3.0).
.DESCRIPTION
    Creates the folder structure and default project.json.
.PARAMETER ProjectPath
    The root path for the new project.
.PARAMETER ProjectId
    The unique ID for the project.
.PARAMETER ProjectName
    The display name for the project.
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$ProjectPath,
    
    [Parameter(Mandatory=$true)]
    [string]$ProjectId,
    
    [string]$ProjectName = "New Project"
)

$ErrorActionPreference = "Stop"

# Create directories
$dirs = @("baseline", "schemes", "Assets")
foreach ($d in $dirs) {
    $path = Join-Path $ProjectPath $d
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Path $path | Out-Null
        Write-Host "Created directory: $path"
    }
}

# Create project.json
$projectJsonPath = Join-Path $ProjectPath "project.json"
if (-not (Test-Path $projectJsonPath)) {
    $projectData = @{
        id = $ProjectId
        name = $ProjectName
        version = "3.0"
        activeSchemeId = $null
        schemes = @()
    }
    $projectData | ConvertTo-Json -Depth 4 | Set-Content -Path $projectJsonPath -Encoding UTF8
    Write-Host "Created manifest: $projectJsonPath"
} else {
    Write-Warning "project.json already exists at $projectJsonPath"
}

Write-Host "Project initialization complete."
