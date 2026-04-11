param()

$FDP_CORE_GUID    = "{EFD178A2-CDEC-42BD-8269-F5F9CB975D08}"
$FDP_CORE_TESTS_GUID = "{0E9665BE-9B00-4B60-AD7A-E2A3BB8B0E89}"
$KERNEL_FOLDER_GUID  = "{13E3BE55-7803-45D3-8970-96D7D78481F6}"

# GUIDs of old projects
$GuidFdpKernel        = "{802E0915-7950-4EB6-9B2A-7B4D0B4C5A47}"
$GuidModuleHostCore   = "{E150C89A-BD13-6AB2-AD01-7DAACF959A39}"
$GuidFdpTests         = "{D14C9B90-D82E-02FD-79CE-2E970FD4F715}"
$GuidMhCoreTests      = "{A6C88231-BF9A-E039-04C3-2CDA1394DE36}"
$GuidFdpIfacesFDP     = "{E7FF3CB4-4A28-4D29-2D60-9C47E61B7251}"  # FDP.sln
$GuidFdpIfacesIOS     = "{CBB74ACA-FD26-06AB-06EB-7DFBBE3CF279}"  # IOS.sln

function Get-ConfigBlock {
    param([string]$Guid, [string]$Indent)
    $lines = @(
        "$Indent$Guid.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
        "$Indent$Guid.Debug|Any CPU.Build.0 = Debug|Any CPU",
        "$Indent$Guid.Debug|x64.ActiveCfg = Debug|Any CPU",
        "$Indent$Guid.Debug|x64.Build.0 = Debug|Any CPU",
        "$Indent$Guid.Debug|x86.ActiveCfg = Debug|Any CPU",
        "$Indent$Guid.Debug|x86.Build.0 = Debug|Any CPU",
        "$Indent$Guid.Release|Any CPU.ActiveCfg = Release|Any CPU",
        "$Indent$Guid.Release|Any CPU.Build.0 = Release|Any CPU",
        "$Indent$Guid.Release|x64.ActiveCfg = Release|Any CPU",
        "$Indent$Guid.Release|x64.Build.0 = Release|Any CPU",
        "$Indent$Guid.Release|x86.ActiveCfg = Release|Any CPU",
        "$Indent$Guid.Release|x86.Build.0 = Release|Any CPU"
    )
    return $lines -join "`r`n"
}

function Update-SolutionFile {
    param(
        [string]$SlnPath,
        [string]$FdpCoreProjLine,      # "Project(...)...EndProject" line pair for Fdp.Core
        [string]$FdpCoreTestsProjLine, # "Project(...)...EndProject" line pair for Fdp.Core.Tests
        [string]$GuidInterfaces,       # the FDP.Interfaces GUID (different in each sln)
        [string]$ConfigIndent          # the indentation used for config entries
    )

    $content = [System.IO.File]::ReadAllText($SlnPath)

    # ====================================================
    # 1. Replace the Fdp.Kernel project block with Fdp.Core
    # ====================================================
    $content = $content -replace `
        [regex]::Escape('Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Fdp.Kernel"') + '[^\r\n]*\r?\nEndProject', `
        "$FdpCoreProjLine`r`nEndProject"

    # ====================================================
    # 2. Replace the Fdp.Tests project block with Fdp.Core.Tests
    # ====================================================
    $content = $content -replace `
        [regex]::Escape('Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Fdp.Tests"') + '[^\r\n]*\r?\nEndProject', `
        "$FdpCoreTestsProjLine`r`nEndProject"

    # ====================================================
    # 3. Remove ModuleHost.Core, ModuleHost.Core.Tests, FDP.Interfaces project blocks
    # ====================================================
    $content = $content -replace `
        [regex]::Escape('Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ModuleHost.Core"') + '[^\r\n]*\r?\nEndProject\r?\n', ''

    $content = $content -replace `
        [regex]::Escape('Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ModuleHost.Core.Tests"') + '[^\r\n]*\r?\nEndProject\r?\n', ''

    $content = $content -replace `
        [regex]::Escape('Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "FDP.Interfaces"') + '[^\r\n]*\r?\nEndProject\r?\n', ''

    # ====================================================
    # 4. Remove config blocks for old GUIDs
    # ====================================================
    foreach ($guid in @($GuidFdpKernel, $GuidModuleHostCore, $GuidFdpTests, $GuidMhCoreTests, $GuidInterfaces)) {
        $escapedGuid = [regex]::Escape($guid)
        # Remove all lines starting with the GUID
        $content = $content -replace "[ \t]*$escapedGuid\.[^\r\n]*\r?\n", ''
    }

    # ====================================================
    # 5. Add config blocks for new projects before EndGlobalSection
    #    (find the last "EndGlobalSection" in ProjectConfigurationPlatforms section)
    # ====================================================
    $newCfgCore      = Get-ConfigBlock -Guid $FDP_CORE_GUID      -Indent $ConfigIndent
    $newCfgCoreTests = Get-ConfigBlock -Guid $FDP_CORE_TESTS_GUID -Indent $ConfigIndent
    
    # Insert before the first EndGlobalSection after the config section
    # Strategy: find the pattern where config entries end
    $insertMarker = "$ConfigIndent$GuidFdpKernel"
    # Since we already removed old config entries, find a spot near another project's config
    # Actually let's insert before the "EndGlobalSection" that closes ProjectConfigurationPlatforms
    # We look for the pattern: last config line followed by EndGlobalSection
    # Simple approach: insert before the EndGlobalSection of ProjectConfigurationPlatforms
    # Which appears right after all config lines
    # Pattern: lines of config followed by \t\tEndGlobalSection (for ProjectConfigurationPlatforms)
    $content = $content -replace '(\t\tEndGlobalSection\r?\n\t\tGlobalSection\(SolutionProperties\))', `
        "$newCfgCore`r`n$newCfgCoreTests`r`n`t`tEndGlobalSection`r`n`t`tGlobalSection(SolutionProperties)"

    # ====================================================
    # 6. Update NestedProjects section
    # ====================================================
    $content = $content -replace [regex]::Escape("$GuidFdpKernel = $KERNEL_FOLDER_GUID") + '\r?\n', ''
    $content = $content -replace [regex]::Escape("$GuidFdpTests = $KERNEL_FOLDER_GUID")  + '\r?\n', ''
    $content = $content -replace [regex]::Escape("{E150C89A-BD13-6AB2-AD01-7DAACF959A39} = {6BE96024-A956-4626-9355-447F0ACD8D3E}") + '\r?\n', ''
    $content = $content -replace [regex]::Escape("{A6C88231-BF9A-E039-04C3-2CDA1394DE36} = {6BE96024-A956-4626-9355-447F0ACD8D3E}") + '\r?\n', ''

    # Remove FDP.Interfaces nested entry (different per sln)
    if ($GuidInterfaces -ne "") {
        $content = $content -replace [regex]::Escape("$GuidInterfaces = ") + '\{[A-Fa-f0-9-]+\}\r?\n', ''
    }

    # Add new nested entries before EndGlobalSection of NestedProjects
    $nestedCore      = "`t`t$FDP_CORE_GUID = $KERNEL_FOLDER_GUID"
    $nestedCoreTests = "`t`t$FDP_CORE_TESTS_GUID = $KERNEL_FOLDER_GUID"
    $content = $content -replace '(\t\tEndGlobalSection\r?\n\t\tGlobalSection\(ExtensibilityGlobals\))', `
        "$nestedCore`r`n$nestedCoreTests`r`n`t`tEndGlobalSection`r`n`t`tGlobalSection(ExtensibilityGlobals)"

    [System.IO.File]::WriteAllText($SlnPath, $content)
    Write-Host "Updated: $SlnPath"
}

# ======================================================================
# FDP.sln
# ======================================================================
$fdpCoreProjLineFdp = "Project(`"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`") = `"Fdp.Core`", `"Kernel\Fdp.Core\Fdp.Core.csproj`", `"$FDP_CORE_GUID`""
$fdpCoreTestsProjLineFdp = "Project(`"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`") = `"Fdp.Core.Tests`", `"Kernel\Fdp.Core.Tests\Fdp.Core.Tests.csproj`", `"$FDP_CORE_TESTS_GUID`""

Update-SolutionFile `
    -SlnPath "D:\Work\IOS-IG-SimHost-FDP-2\FDP\FDP.sln" `
    -FdpCoreProjLine $fdpCoreProjLineFdp `
    -FdpCoreTestsProjLine $fdpCoreTestsProjLineFdp `
    -GuidInterfaces $GuidFdpIfacesFDP `
    -ConfigIndent "`t`t`t`t"

# ======================================================================
# IOS-IG-SimHost.sln
# ======================================================================
$fdpCoreProjLineIOS = "Project(`"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`") = `"Fdp.Core`", `"FDP\Kernel\Fdp.Core\Fdp.Core.csproj`", `"$FDP_CORE_GUID`""
$fdpCoreTestsProjLineIOS = "Project(`"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`") = `"Fdp.Core.Tests`", `"FDP\Kernel\Fdp.Core.Tests\Fdp.Core.Tests.csproj`", `"$FDP_CORE_TESTS_GUID`""

Update-SolutionFile `
    -SlnPath "D:\Work\IOS-IG-SimHost-FDP-2\IOS-IG-SimHost.sln" `
    -FdpCoreProjLine $fdpCoreProjLineIOS `
    -FdpCoreTestsProjLine $fdpCoreTestsProjLineIOS `
    -GuidInterfaces $GuidFdpIfacesIOS `
    -ConfigIndent "`t`t`t`t"

Write-Host "Done updating solution files."
