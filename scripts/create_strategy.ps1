<#
.SYNOPSIS
    Creates a new Strategy (Git Repo) in a BIMCanvas Project.
.DESCRIPTION
    Creates a new folder in 'schemes/', initializes Git, creates strategy.json, and updates project.json.
.PARAMETER ProjectPath
    The root path of the project.
.PARAMETER StrategyId
    The unique ID for the new strategy (used as folder name).
.PARAMETER StrategyName
    The display name for the strategy.
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$ProjectPath,
    
    [Parameter(Mandatory=$true)]
    [string]$StrategyId,
    
    [string]$StrategyName = "New Strategy"
)

$ErrorActionPreference = "Stop"

# Validate Project
$projectJsonPath = Join-Path $ProjectPath "project.json"
if (-not (Test-Path $projectJsonPath)) {
    Write-Error "Invalid project path: project.json not found."
}

# Create Strategy Folder
$strategyPath = Join-Path $ProjectPath "schemes/$StrategyId"
if (Test-Path $strategyPath) {
    Write-Error "Strategy '$StrategyId' already exists."
}
New-Item -ItemType Directory -Path $strategyPath | Out-Null

# Initialize Git
Push-Location $strategyPath
git init
New-Item -ItemType File -Path ".gitignore" -Value "Assets/`n.DS_Store" | Out-Null
Pop-Location

# Create strategy.json
$strategyData = @{
    id = $StrategyId
    name = $StrategyName
    type = "strategy"
    description = ""
    origin = $null
    baselineRef = "../../baseline"
    lastValidatedBaselineHash = "" # TODO: Calculate hash
    status = "valid"
}
$strategyJsonPath = Join-Path $strategyPath "strategy.json"
$strategyData | ConvertTo-Json -Depth 4 | Set-Content -Path $strategyJsonPath -Encoding UTF8

# Update project.json
$projectData = Get-Content $projectJsonPath | ConvertFrom-Json
$newScheme = @{
    id = $StrategyId
    path = "./schemes/$StrategyId"
    name = $StrategyName
}
$projectData.schemes += $newScheme
# Set as active if it's the first one
if (-not $projectData.activeSchemeId) {
    $projectData.activeSchemeId = $StrategyId
}
$projectData | ConvertTo-Json -Depth 4 | Set-Content -Path $projectJsonPath -Encoding UTF8

Write-Host "Strategy '$StrategyName' ($StrategyId) created successfully."
