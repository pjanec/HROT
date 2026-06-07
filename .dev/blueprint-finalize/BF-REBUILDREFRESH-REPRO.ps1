# BF-BATCH-DIAGFAIL-REBUILD: scripted repro for REBUILDREFRESH staleness fix
# Purpose: Prove that editing a .bp.json and running dotnet build regenerates the .g.cs
# Usage:   pwsh -File .dev/blueprint-finalize/BF-REBUILDREFRESH-REPRO.ps1
# Requirements: dotnet SDK 8.0 on PATH

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path "$PSScriptRoot\..\.."
$bpJsonPath = "$repoRoot\Hrot\Subsystems\Hrot.AI.Behaviors\Blueprints\Count4.bp.json"
$genDir = "$repoRoot\Hrot\Subsystems\Hrot.AI.Behaviors\obj\GeneratedFiles\Hrot.Blueprints.Generators\Hrot.Blueprints.Generators.BlueprintIncrementalGenerator"
$projPath = "$repoRoot\Hrot\Subsystems\Hrot.AI.Behaviors\Hrot.AI.Behaviors.csproj"

Write-Host "=== REBUILDREFRESH Repro Script ===" -ForegroundColor Cyan

# Step 1: Snapshot
Write-Host "[1/5] Snapshotting current generated files..." -ForegroundColor Yellow
$beforeFiles = Get-ChildItem "$genDir\Count4_*.g.cs" -ErrorAction SilentlyContinue
if (-not $beforeFiles) {
    Write-Host "ERROR: No generated Count4_*.g.cs found in $genDir" -ForegroundColor Red
    exit 1
}
$beforeHash = @{}
foreach ($f in $beforeFiles) {
    $beforeHash[$f.Name] = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
}
Write-Host "  Snapshot: $($beforeHash.Count) file(s)" -ForegroundColor Gray

# Step 2: Ensure clean incremental state
Write-Host "[2/5] Building from clean state (--no-incremental)..." -ForegroundColor Yellow
dotnet build $projPath --no-incremental 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Clean build failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit 1
}

# Step 3: Modify .bp.json (change a Literal value to trigger code change)
Write-Host "[3/5] Modifying Count4.bp.json (changing Literal value)..." -ForegroundColor Yellow
$originalContent = Get-Content $bpJsonPath -Raw
$modifiedContent = $originalContent -replace '"ValueJson": "5"', '"ValueJson": "99"'
if ($modifiedContent -eq $originalContent) {
    # Fallback: modify another known literal
    $modifiedContent = $originalContent -replace '"ValueJson": "1"', '"ValueJson": "99"'
}
if ($modifiedContent -eq $originalContent) {
    Write-Host "ERROR: Could not find a mutable Literal value in Count4.bp.json" -ForegroundColor Red
    exit 1
}
Set-Content $bpJsonPath $modifiedContent -NoNewline
Write-Host "  Modified: changed Literal value" -ForegroundColor Gray

# Step 4: Run incremental build (same command the editor uses)
Write-Host "[4/5] Running incremental dotnet build Hrot.AI.Behaviors.csproj..." -ForegroundColor Yellow
$buildOutput = dotnet build $projPath 2>&1
$buildExit = $LASTEXITCODE
Write-Host $buildOutput
if ($buildExit -ne 0) {
    Write-Host "ERROR: Incremental build failed with exit code $buildExit" -ForegroundColor Red
    # Restore backup
    Set-Content $bpJsonPath $originalContent -NoNewline
    exit 1
}

# Step 5: Assert .g.cs changed
Write-Host "[5/5] Checking if .g.cs was regenerated..." -ForegroundColor Yellow
$afterFiles = Get-ChildItem "$genDir\Count4_*.g.cs" -ErrorAction SilentlyContinue
if (-not $afterFiles) {
    Write-Host "FAIL: No Count4_*.g.cs found after build!" -ForegroundColor Red
    Set-Content $bpJsonPath $originalContent -NoNewline
    exit 1
}

$changed = $false
foreach ($f in $afterFiles) {
    $afterHash = (Get-FileHash $f.FullName -Algorithm SHA256).Hash
    $before = $beforeHash[$f.Name]
    if ($before -and $afterHash -ne $before) {
        Write-Host "  CHANGED: $($f.Name) (hash differs -> regenerated)" -ForegroundColor Green
        $changed = $true
    } elseif ($before) {
        Write-Host "  UNCHANGED: $($f.Name)" -ForegroundColor Gray
    } else {
        Write-Host "  NEW: $($f.Name)" -ForegroundColor Green
        $changed = $true
    }
}

# Restore original
Set-Content $bpJsonPath $originalContent -NoNewline

if ($changed) {
    Write-Host "`nPASS: Incremental build regenerated .g.cs after .bp.json change." -ForegroundColor Green
    exit 0
} else {
    Write-Host "`nFAIL: Incremental build did NOT regenerate .g.cs. Staleness bug reproduced." -ForegroundColor Red
    exit 1
}
