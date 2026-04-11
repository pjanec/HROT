param()

function Get-RelativePath {
    param([string]$TargetPath, [string]$BasePath)
    # Normalize paths
    $target = [System.IO.Path]::GetFullPath($TargetPath)
    $base = [System.IO.Path]::GetFullPath($BasePath)
    # Use Uri to compute relative path
    $targetUri = New-Object System.Uri($target)
    $baseUri = New-Object System.Uri($base + '\')
    $rel = $baseUri.MakeRelativeUri($targetUri).ToString()
    return $rel -replace '/', '\'
}

$root = "D:\Work\IOS-IG-SimHost-FDP-2"
$fdpCorePath = "$root\FDP\Kernel\Fdp.Core\Fdp.Core.csproj"

$skipFiles = @(
    "$root\FDP\Kernel\Fdp.Kernel\Fdp.Kernel.csproj",
    "$root\FDP\Common\FDP.Interfaces\FDP.Interfaces.csproj",
    "$root\FDP\ModuleHost\ModuleHost.Core\ModuleHost.Core.csproj",
    "$root\FDP\Kernel\Fdp.Kernel.Tests\Fdp.Tests.csproj",
    "$root\FDP\ModuleHost\ModuleHost.Core.Tests\ModuleHost.Core.Tests.csproj",
    "$root\FDP\Kernel\Fdp.Core\Fdp.Core.csproj",
    "$root\FDP\Kernel\Fdp.Core.Tests\Fdp.Core.Tests.csproj"
)

$updated = @()

Get-ChildItem -Path $root -Recurse -Filter "*.csproj" | Where-Object { $_.FullName -notin $skipFiles } | ForEach-Object {
    $f = $_
    $content = [System.IO.File]::ReadAllText($f.FullName)

    if ($content -notmatch "(?:Fdp\.Kernel|FDP\.Interfaces|ModuleHost\.Core)\.csproj") {
        return
    }

    $projDir = $f.DirectoryName
    $relPath = Get-RelativePath -TargetPath $fdpCorePath -BasePath $projDir

    $lines = $content -split "`r?`n"
    $newLines = [System.Collections.Generic.List[string]]::new()
    $alreadyAdded = $false

    foreach ($line in $lines) {
        if ($line -match '<ProjectReference Include="[^"]*(?:Fdp\.Kernel|FDP\.Interfaces|ModuleHost\.Core)\.csproj"') {
            if (-not $alreadyAdded) {
                $indent = ($line -replace '<.*', '')
                $newLines.Add("${indent}<ProjectReference Include=`"$relPath`" />")
                $alreadyAdded = $true
            }
            # else: skip duplicate
        } else {
            $newLines.Add($line)
        }
    }

    $useCrlf = $content.Contains("`r`n")
    $sep = if ($useCrlf) { "`r`n" } else { "`n" }
    $newContent = $newLines -join $sep

    [System.IO.File]::WriteAllText($f.FullName, $newContent)
    $updated += $f.FullName
}

Write-Host "Updated $($updated.Count) files:"
$updated | ForEach-Object { Write-Host "  $_" }
