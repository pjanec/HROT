param()

function Get-RelativePath {
    param([string]$TargetPath, [string]$BasePath)
    $target = [System.IO.Path]::GetFullPath($TargetPath)
    $base = [System.IO.Path]::GetFullPath($BasePath)
    $targetUri = New-Object System.Uri("file:///$($target.Replace('\','/'))")
    $baseUri   = New-Object System.Uri("file:///$($base.Replace('\','/') + '/')")
    $rel = $baseUri.MakeRelativeUri($targetUri).ToString()
    return $rel -replace '/', '\'
}

$root = "D:\Work\IOS-IG-SimHost-FDP-2"
$fdpCorePath = "$root\FDP\Kernel\Fdp.Core\Fdp.Core.csproj"

$updated = @()

Get-ChildItem -Path $root -Recurse -Filter "*.csproj" | ForEach-Object {
    $f = $_
    $content = [System.IO.File]::ReadAllText($f.FullName)

    if ($content -notmatch '<ProjectReference Include="" />') {
        return
    }

    $projDir = $f.DirectoryName
    $relPath = Get-RelativePath -TargetPath $fdpCorePath -BasePath $projDir

    # Replace the empty Include with the correct path
    $newContent = $content -replace '<ProjectReference Include="" />', "<ProjectReference Include=`"$relPath`" />"

    [System.IO.File]::WriteAllText($f.FullName, $newContent)
    $updated += $f.FullName
    Write-Host "Fixed: $($f.FullName)"
}

Write-Host "Fixed $($updated.Count) files"
