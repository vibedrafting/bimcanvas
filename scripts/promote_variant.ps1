<#
.SYNOPSIS
    Promotes a Variant (Git Branch) to a new independent Strategy.
.DESCRIPTION
    Copies the source strategy folder, checks out the target branch, updates origin metadata, and registers in project.json.
.PARAMETER ProjectPath
    The root path of the project.
.PARAMETER SourceStrategyId
    The ID of the source strategy.
.PARAMETER TargetBranch
    The name of the branch (variant) to promote.
.PARAMETER NewStrategyId
    The ID for the new strategy.
.PARAMETER NewStrategyName
    The display name for the new strategy.
#>
param(
    [Parameter(Mandatory=$true)]
    [string]$ProjectPath,
    
    [Parameter(Mandatory=$true)]
    [string]$SourceStrategyId,
    
    [Parameter(Mandatory=$true)]
    [string]$TargetBranch,
    
    [Parameter(Mandatory=$true)]
    [string]$NewStrategyId,
    
    [string]$NewStrategyName
)

$ErrorActionPreference = "Stop"

$sourcePath = Join-Path $ProjectPath "schemes/$SourceStrategyId"
$destPath = Join-Path $ProjectPath "schemes/$NewStrategyId"

# Validation
if (-not (Test-Path $sourcePath)) { Write-Error "Source strategy not found." }
if (Test-Path $destPath) { Write-Error "Destination strategy already exists." }

# 1. Copy Folder
Write-Host "Copying repository..."
Copy-Item -Path $sourcePath -Destination $destPath -Recurse

# 2. Checkout Branch & Clean up
Push-Location $destPath
Write-Host "Checking out target branch..."
git checkout $TargetBranch
if ($LASTEXITCODE -ne 0) { 
    Pop-Location
    Remove-Item $destPath -Recurse -Force
    Write-Error "Failed to checkout branch '$TargetBranch'." 
}

# Get Commit Hash for Origin
$commitHash = git rev-parse HEAD
$timestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ"

# Optional: Rename branch to main
# git branch -m $TargetBranch main

Pop-Location

# 3. Update strategy.json
$strategyJsonPath = Join-Path $destPath "strategy.json"
if (Test-Path $strategyJsonPath) {
    $strategyData = Get-Content $strategyJsonPath | ConvertFrom-Json
    $strategyData.id = $NewStrategyId
    $strategyData.name = if ($NewStrategyName) { $NewStrategyName } else { "$($strategyData.name) (Derived)" }
    $strategyData.origin = @{
        sourceRepo = "../$SourceStrategyId"
        sourceBranch = $TargetBranch
        sourceCommit = $commitHash
        derivedAt = $timestamp
    }
    $strategyData | ConvertTo-Json -Depth 4 | Set-Content -Path $strategyJsonPath -Encoding UTF8
}

# 4. Register in project.json
$projectJsonPath = Join-Path $ProjectPath "project.json"
$projectData = Get-Content $projectJsonPath | ConvertFrom-Json
$newScheme = @{
    id = $NewStrategyId
    path = "./schemes/$NewStrategyId"
    name = $strategyData.name
}
$projectData.schemes += $newScheme
$projectData | ConvertTo-Json -Depth 4 | Set-Content -Path $projectJsonPath -Encoding UTF8

Write-Host "Variant '$TargetBranch' promoted to Strategy '$NewStrategyId' successfully."
