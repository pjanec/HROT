param()

$FDP_CORE_GUID       = "{EFD178A2-CDEC-42BD-8269-F5F9CB975D08}"
$FDP_CORE_TESTS_GUID = "{0E9665BE-9B00-4B60-AD7A-E2A3BB8B0E89}"
$KERNEL_FOLDER_GUID  = "{13E3BE55-7803-45D3-8970-96D7D78481F6}"

$GuidFdpKernel      = "{802E0915-7950-4EB6-9B2A-7B4D0B4C5A47}"
$GuidModuleHostCore = "{E150C89A-BD13-6AB2-AD01-7DAACF959A39}"
$GuidFdpTests       = "{D14C9B90-D82E-02FD-79CE-2E970FD4F715}"
$GuidMhCoreTests    = "{A6C88231-BF9A-E039-04C3-2CDA1394DE36}"
$GuidFdpIfacesFDP   = "{E7FF3CB4-4A28-4D29-2D60-9C47E61B7251}"
$GuidFdpIfacesIOS   = "{CBB74ACA-FD26-06AB-06EB-7DFBBE3CF279}"

function Build-ConfigBlock([string]$Guid, [string]$Indent) {
    $cfgs = @("Debug|Any CPU","Debug|x64","Debug|x86","Release|Any CPU","Release|x64","Release|x86")
    $lines = foreach ($cfg in $cfgs) {
        $plat = if ($cfg -like "Release*") { "Release|Any CPU" } else { "Debug|Any CPU" }
        "$Indent$Guid.$cfg.ActiveCfg = $plat"
        "$Indent$Guid.$cfg.Build.0 = $plat"
    }
    return $lines -join "`r`n"
}

function Update-Sln([string]$SlnPath, [string]$CoreProjDecl, [string]$TestsProjDecl, [string]$GuidIfaces, [string]$Indent) {
    $c = [System.IO.File]::ReadAllText($SlnPath)

    # Replace Fdp.Kernel project declaration with Fdp.Core
    $pat1 = 'Project\("\{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC\}"\) = "Fdp\.Kernel"[^\r\n]*\r?\nEndProject'
    $c = [regex]::Replace($c, $pat1, "$CoreProjDecl`r`nEndProject")

    # Replace Fdp.Tests project declaration with Fdp.Core.Tests
    $pat2 = 'Project\("\{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC\}"\) = "Fdp\.Tests"[^\r\n]*\r?\nEndProject'
    $c = [regex]::Replace($c, $pat2, "$TestsProjDecl`r`nEndProject")

    # Remove ModuleHost.Core project block
    $pat3 = 'Project\("\{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC\}"\) = "ModuleHost\.Core"[^\r\n]*\r?\nEndProject\r?\n'
    $c = [regex]::Replace($c, $pat3, "")

    # Remove ModuleHost.Core.Tests project block
    $pat4 = 'Project\("\{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC\}"\) = "ModuleHost\.Core\.Tests"[^\r\n]*\r?\nEndProject\r?\n'
    $c = [regex]::Replace($c, $pat4, "")

    # Remove FDP.Interfaces project block
    $pat5 = 'Project\("\{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC\}"\) = "FDP\.Interfaces"[^\r\n]*\r?\nEndProject\r?\n'
    $c = [regex]::Replace($c, $pat5, "")

    # Remove config lines for old GUIDs
    foreach ($guid in @($GuidFdpKernel, $GuidModuleHostCore, $GuidFdpTests, $GuidMhCoreTests, $GuidIfaces)) {
        $escaped = [regex]::Escape($guid)
        $c = [regex]::Replace($c, "[ \t]*$escaped\.[^\r\n]*(\r?\n)?", "")
    }

    # Insert new config blocks before the EndGlobalSection of ProjectConfigurationPlatforms
    # That section is followed by SolutionProperties
    $newCoreBlk  = Build-ConfigBlock -Guid $FDP_CORE_GUID       -Indent $Indent
    $newTestsBlk = Build-ConfigBlock -Guid $FDP_CORE_TESTS_GUID -Indent $Indent
    $insertAfter = "`t`tEndGlobalSection`r`n`t`tGlobalSection(SolutionProperties)"
    $replacement = "$newCoreBlk`r`n$newTestsBlk`r`n`t`tEndGlobalSection`r`n`t`tGlobalSection(SolutionProperties)"
    $c = $c.Replace($insertAfter, $replacement)

    # Remove old NestedProjects entries for removed GUIDs
    foreach ($guid in @($GuidFdpKernel, $GuidModuleHostCore, $GuidFdpTests, $GuidMhCoreTests, $GuidIfaces)) {
        $escaped = [regex]::Escape($guid)
        $c = [regex]::Replace($c, "[ \t]*$escaped = \{[A-Fa-f0-9\-]+\}(\r?\n)?", "")
    }

    # Add new NestedProjects entries
    $nestedCore  = "`t`t$FDP_CORE_GUID = $KERNEL_FOLDER_GUID"
    $nestedTests = "`t`t$FDP_CORE_TESTS_GUID = $KERNEL_FOLDER_GUID"
    $nestMarker  = "`t`tEndGlobalSection`r`n`t`tGlobalSection(ExtensibilityGlobals)"
    $nestReplace = "$nestedCore`r`n$nestedTests`r`n`t`tEndGlobalSection`r`n`t`tGlobalSection(ExtensibilityGlobals)"
    $c = $c.Replace($nestMarker, $nestReplace)

    [System.IO.File]::WriteAllText($SlnPath, $c)
    Write-Host "Updated: $SlnPath"
}

# FDP.sln
$coreDeclFdp  = "Project(`"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`") = `"Fdp.Core`", `"Kernel\Fdp.Core\Fdp.Core.csproj`", `"$FDP_CORE_GUID`""
$testsDeclFdp = "Project(`"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`") = `"Fdp.Core.Tests`", `"Kernel\Fdp.Core.Tests\Fdp.Core.Tests.csproj`", `"$FDP_CORE_TESTS_GUID`""
Update-Sln -SlnPath "D:\Work\IOS-IG-SimHost-FDP-2\FDP\FDP.sln" `
           -CoreProjDecl $coreDeclFdp -TestsProjDecl $testsDeclFdp `
           -GuidIfaces $GuidFdpIfacesFDP -Indent "`t`t`t`t"

# IOS-IG-SimHost.sln
$coreDeclIOS  = "Project(`"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`") = `"Fdp.Core`", `"FDP\Kernel\Fdp.Core\Fdp.Core.csproj`", `"$FDP_CORE_GUID`""
$testsDeclIOS = "Project(`"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`") = `"Fdp.Core.Tests`", `"FDP\Kernel\Fdp.Core.Tests\Fdp.Core.Tests.csproj`", `"$FDP_CORE_TESTS_GUID`""
Update-Sln -SlnPath "D:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln" `
           -CoreProjDecl $coreDeclIOS -TestsProjDecl $testsDeclIOS `
           -GuidIfaces $GuidFdpIfacesIOS -Indent "`t`t`t`t"

Write-Host "Done."
