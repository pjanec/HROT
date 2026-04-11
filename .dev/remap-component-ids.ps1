param()
# Remap ModuleHost test ComponentIds to non-conflicting byte-range values
# Available ranges: 13-19, 85-99, 101-109

$moduleHostDir = "D:\Work\IOS-IG-SimHost-FDP-2\FDP\Kernel\Fdp.Core.Tests\ModuleHost"

# Mapping: old ID -> new ID
# Old IDs 214-239 (currently stored as 4214-4239 in files from bad previous fix)
# Old IDs 247, 249 (DynCompBeta, DynCompGamma - not touched by bad fix)
$componentIdMap = @{
    4214 = 13;  4215 = 14;  4216 = 15;
    4217 = 16;  4218 = 17;  4219 = 18;
    4220 = 19;
    4221 = 85;  4222 = 86;  4223 = 87;
    4224 = 88;  4225 = 89;  4226 = 90;
    4227 = 91;  4228 = 92;  4229 = 93;
    4230 = 94;  4231 = 95;  4232 = 96;
    4233 = 97;  4234 = 98;  4235 = 99;
    4236 = 101; 4237 = 102; 4238 = 103;
    4239 = 104;
    247 = 105;
    249 = 106
}

# EventId mapping: 4201 and 4202 are already int-safe, keep them as-is
# Just verify they don't conflict with any others

$files = Get-ChildItem -Path $moduleHostDir -Recurse -Filter "*.cs"

foreach ($f in $files) {
    $content = [System.IO.File]::ReadAllText($f.FullName)
    $original = $content

    # Apply ComponentId remapping (sort by key descending to avoid partial matches)
    foreach ($oldId in ($componentIdMap.Keys | Sort-Object -Descending)) {
        $newId = $componentIdMap[$oldId]
        $content = $content -replace "\[ComponentId\($oldId\)\]", "[ComponentId($newId)]"
    }

    if ($content -ne $original) {
        [System.IO.File]::WriteAllText($f.FullName, $content)
        Write-Host "Remapped: $($f.Name)"
    }
}

Write-Host "Done."
