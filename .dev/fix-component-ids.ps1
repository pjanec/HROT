param()
# Fix ComponentId and EventId collisions in ModuleHost test files
# by offsetting all IDs in the 210-239 range by +4000, and EventIds in 200-209 range by +4000

$moduleHostDir = "D:\Work\IOS-IG-SimHost-FDP-2\FDP\Kernel\Fdp.Core.Tests\ModuleHost"

$files = Get-ChildItem -Path $moduleHostDir -Recurse -Filter "*.cs"

foreach ($f in $files) {
    $content = [System.IO.File]::ReadAllText($f.FullName)
    $original = $content

    # Renumber ComponentIds 210-239 -> 4210-4239
    for ($i = 239; $i -ge 210; $i--) {
        $content = $content -replace "\[ComponentId\($i\)\]", "[ComponentId($($i + 4000))]"
    }

    # Renumber EventIds 200-209 -> 4200-4209  (covers 201-202 range)
    for ($i = 209; $i -ge 200; $i--) {
        $content = $content -replace "\[EventId\($i\)\]", "[EventId($($i + 4000))]"
    }

    # Note: We already manually fixed 201->4001 and 202->4002 in two files,
    # so also fix those back to consistent 4000+N scheme
    $content = $content -replace "\[EventId\(4001\)\]", "[EventId(4201)]"
    $content = $content -replace "\[EventId\(4002\)\]", "[EventId(4202)]"

    if ($content -ne $original) {
        [System.IO.File]::WriteAllText($f.FullName, $content)
        Write-Host "Fixed: $($f.Name)"
    }
}

Write-Host "Done."
