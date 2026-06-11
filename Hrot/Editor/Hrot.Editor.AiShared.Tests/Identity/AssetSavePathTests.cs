using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.BTree.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Hsm.Editor;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Identity;

// ─────────────────────────────────────────────────────────────────────────────
// MTB-P6-T7 — AssetSavePath + subfolder-aware save round-trip tests
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Unit tests for <see cref="AssetSavePath"/> (compose, root-bounding,
/// extension mapping) and the subfolder round-trip integration test.
/// </summary>
public sealed class AssetSavePathTests
{
    // ── Compose — basic path construction ──────────────────────────────────

    [Fact]
    public void Compose_Blueprint_AtRoot_ReturnsExpectedPath()
    {
        var path = AssetSavePath.Compose(AssetKind.Blueprint, "", "MyBlueprint");

        Assert.EndsWith($"Assets{Path.DirectorySeparatorChar}Blueprints{Path.DirectorySeparatorChar}MyBlueprint.bp.json",
            path);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void Compose_BTree_NestedRelPath_ReturnsExpectedPath()
    {
        var path = AssetSavePath.Compose(AssetKind.BTree, "combat/Guard", "PatrolBehavior");

        var expectedSuffix = $"Assets{Path.DirectorySeparatorChar}BTrees{Path.DirectorySeparatorChar}combat{Path.DirectorySeparatorChar}Guard{Path.DirectorySeparatorChar}PatrolBehavior.btree.json";
        Assert.EndsWith(expectedSuffix, path);
        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void Compose_Hsm_BackslashRelPath_NormalizedToForwardSlash()
    {
        // Backslashes in relPath are normalized to forward slashes.
        var path = AssetSavePath.Compose(AssetKind.Hsm, @"states\combat", "Machine");

        var expectedSuffix = $"Assets{Path.DirectorySeparatorChar}HSMs{Path.DirectorySeparatorChar}states{Path.DirectorySeparatorChar}combat{Path.DirectorySeparatorChar}Machine.hsm.json";
        Assert.EndsWith(expectedSuffix, path);
    }

    [Fact]
    public void Compose_NullRelPath_TreatedAsRoot()
    {
        var path = AssetSavePath.Compose(AssetKind.BTree, null!, "RootAsset");

        Assert.EndsWith($"Assets{Path.DirectorySeparatorChar}BTrees{Path.DirectorySeparatorChar}RootAsset.btree.json",
            path);
    }

    [Fact]
    public void Compose_AssetRootOverride_UsesOverridePath()
    {
        var overrideRoot = Path.Combine(Path.GetTempPath(), "test-root");
        var path = AssetSavePath.Compose(AssetKind.Blueprint, "sub", "Asset", overrideRoot);

        Assert.StartsWith(overrideRoot, path);
        Assert.EndsWith(Path.Combine("sub", "Asset.bp.json"), path);
    }

    // ── GetExtension ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AssetKind.Blueprint, ".bp.json")]
    [InlineData(AssetKind.BTree, ".btree.json")]
    [InlineData(AssetKind.Hsm, ".hsm.json")]
    public void GetExtension_FileKinds_ReturnsCorrectExtension(AssetKind kind, string expected)
    {
        Assert.Equal(expected, AssetSavePath.GetExtension(kind));
    }

    [Theory]
    [InlineData(AssetKind.Scenario)]
    [InlineData(AssetKind.Blackboard)]
    [InlineData(AssetKind.Utility)]
    public void GetExtension_NonFileKinds_ThrowsArgumentOutOfRangeException(AssetKind kind)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetSavePath.GetExtension(kind));
        Assert.Equal("kind", ex.ParamName);
    }

    // ── Root-bounding / validation ─────────────────────────────────────────

    [Fact]
    public void Compose_DotDot_EscapesRoot_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => AssetSavePath.Compose(AssetKind.BTree, "../escape", "Name"));
        Assert.Contains("must not escape", ex.Message);
    }

    [Fact]
    public void Compose_AbsoluteRelPath_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => AssetSavePath.Compose(AssetKind.BTree, "/absolute/path", "Name"));
        Assert.Contains("must not escape", ex.Message);
    }

    [Fact]
    public void Compose_DriveLetterInRelPath_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => AssetSavePath.Compose(AssetKind.BTree, "C:escape", "Name"));
        Assert.Contains("must not escape", ex.Message);
    }

    [Fact]
    public void Compose_EmptyBaseName_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => AssetSavePath.Compose(AssetKind.Blueprint, "sub", ""));
        Assert.Equal("baseName", ex.ParamName);
    }

    [Fact]
    public void Compose_WhitespaceBaseName_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => AssetSavePath.Compose(AssetKind.Blueprint, "sub", "  "));
        Assert.Equal("baseName", ex.ParamName);
    }

    [Fact]
    public void Compose_ScenarioKind_ThrowsArgumentOutOfRangeException()
    {
        // Scenario has no Assets root → AssetsFor throws.
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => AssetSavePath.Compose(AssetKind.Scenario, "", "Name"));
        Assert.Equal("kind", ex.ParamName);
    }

    // ── Round-trip: compose, write, recursive scan ──────────────────────────
    // (MTB-P6-T7 success condition)

    /// <summary>
    /// Composes a save path for <c>"combat/Guard"</c>, writes a valid BTree JSON
    /// file at that path, then recursively scans the temp root — the scan must
    /// find the asset at the SAME relative path <c>"combat/Guard"</c>.
    /// </summary>
    [Fact]
    public void Save_PreservesSubfolder_RoundTrip()
    {
        // 1. Create a temporary Assets root.
        var tempRoot = Path.Combine(Path.GetTempPath(), $"B19_T7_BTree_{Guid.NewGuid():N}");
        // Simulate: tempRoot/Assets/BTrees/… (the override path IS the "BTrees" root).
        Directory.CreateDirectory(tempRoot);
        try
        {
            // 2. Compose the save path using the temp root as the kind root.
            const string relPath = "combat/Guard";
            const string baseName = "PatrolBehavior";
            var savePath = AssetSavePath.Compose(AssetKind.BTree, relPath, baseName, tempRoot);

            // Expected suffix (path within the root): combat/Guard/PatrolBehavior.btree.json
            var expectedRelFileName = Path.Combine("combat", "Guard", "PatrolBehavior.btree.json");
            Assert.EndsWith(expectedRelFileName, savePath);

            // 3. Write a minimal valid BTree JSON file.
            var dir = Path.GetDirectoryName(savePath)!;
            Directory.CreateDirectory(dir);

            var dto = new BehaviorTreeAssetDto
            {
                AssetId         = Guid.NewGuid(),
                Name            = baseName,
                TargetNamespace = "",
                BlackboardTypeName = "",
                ContextTypeName = "",
                Canvas          = new CanvasDto { Zoom = 1.0f },
                Nodes           = new List<BTreeNodeDto>(),
                Pills           = new List<BTreePillDto>(),
                SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
                Suppressions    = new SuppressionsDto(),
                Blackboard      = new BlackboardBlockDto(),
            };
            var json = BTreeJsonServices.Serialize(dto);
            File.WriteAllText(savePath, json);

            // 4. Recursive scan via the kind's DiscoverHeaders (simulates contributor scan).
            var found = BTreeJsonServices.DiscoverHeaders(tempRoot, SearchOption.AllDirectories)
                .Select(h => h.FilePath)
                .ToList();

            Assert.NotEmpty(found);

            // 5. The scan must find the file at the SAME relative path.
            // Compute the file's relative path from the temp root (forward-slash normalized).
            var foundFile = found.FirstOrDefault(f =>
                string.Equals(
                    Path.GetRelativePath(tempRoot, f).Replace('\\', '/'),
                    expectedRelFileName.Replace('\\', '/'),
                    StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(foundFile);

            var actualRelPath = Path.GetRelativePath(tempRoot, foundFile!)
                .Replace('\\', '/');
            Assert.Equal(expectedRelFileName.Replace('\\', '/'), actualRelPath);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Same round-trip test using HSM kind to ensure both file kinds work.
    /// </summary>
    [Fact]
    public void Save_PreservesSubfolder_RoundTrip_Hsm()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"B19_T7_Hsm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            const string relPath = "states/combat";
            const string baseName = "EngageMachine";
            var savePath = AssetSavePath.Compose(AssetKind.Hsm, relPath, baseName, tempRoot);

            var expectedRelFileName = Path.Combine("states", "combat", "EngageMachine.hsm.json");
            Assert.EndsWith(expectedRelFileName, savePath);

            var dir = Path.GetDirectoryName(savePath)!;
            Directory.CreateDirectory(dir);

            var dto = new HsmAssetDto
            {
                AssetId            = Guid.NewGuid(),
                Name               = baseName,
                TargetNamespace    = "",
                BlackboardTypeName = "",
                Canvas             = new HsmCanvasDto { Zoom = 1.0f },
                States             = new List<StateNodeDto>(),
                Regions            = new List<RegionNodeDto>(),
                Transitions        = new List<TransitionNodeDto>(),
                GlobalTransitions  = new List<GlobalTransitionNodeDto>(),
                Events             = new List<EventDefinitionDto>(),
                Suppressions       = new HsmSuppressionsDto(),
                Blackboard         = new HsmBlackboardBlockDto(),
            };
            var json = HsmJsonServices.Serialize(dto);
            File.WriteAllText(savePath, json);

            var found = HsmJsonServices.DiscoverHeaders(tempRoot, SearchOption.AllDirectories)
                .Select(h => h.FilePath)
                .ToList();

            Assert.NotEmpty(found);

            var foundFile = found.FirstOrDefault(f =>
                string.Equals(
                    Path.GetRelativePath(tempRoot, f).Replace('\\', '/'),
                    expectedRelFileName.Replace('\\', '/'),
                    StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(foundFile);

            var actualRelPath = Path.GetRelativePath(tempRoot, foundFile!)
                .Replace('\\', '/');
            Assert.Equal(expectedRelFileName.Replace('\\', '/'), actualRelPath);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    // ── Verify updated services use AssetSavePath ───────────────────────────

    /// <summary>
    /// Ensures BTreeNewAssetService.CreateNew writes to the correct nested path
    /// when given a relPath (verifies the wiring from Task 7).
    /// </summary>
    [Fact]
    public void BTreeService_CreateNew_WritesToSubfolder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"B19_T7_BTreeSvc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var svc = new BTreeNewAssetService(tempRoot);
            var result = svc.CreateNew(recipe: null, name: "NestedAsset", relPath: "group/sub");

            Assert.NotNull(result);

            // The SourceFilePath should point to the subfolder.
            var expectedSuffix = Path.Combine("group", "sub", "NestedAsset.btree.json");
            Assert.EndsWith(expectedSuffix, result.SourceFilePath);
            Assert.True(File.Exists(result.SourceFilePath));

            // Recursive scan verifies the file is found.
            var found = BTreeJsonServices.DiscoverHeaders(tempRoot, SearchOption.AllDirectories)
                .Select(h => h.FilePath)
                .ToList();

            Assert.Contains(found, f =>
                f.Replace('\\', '/').EndsWith("group/sub/NestedAsset.btree.json", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Ensures HsmNewAssetService.CreateNew writes to the correct nested path.
    /// </summary>
    [Fact]
    public void HsmService_CreateNew_WritesToSubfolder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"B19_T7_HsmSvc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var svc = new HsmNewAssetService(tempRoot);
            var result = svc.CreateNew(recipe: null, name: "NestedHsm", relPath: "machines/sub");

            Assert.NotNull(result);

            var expectedSuffix = Path.Combine("machines", "sub", "NestedHsm.hsm.json");
            Assert.EndsWith(expectedSuffix, result.SourceFilePath);
            Assert.True(File.Exists(result.SourceFilePath));

            var found = HsmJsonServices.DiscoverHeaders(tempRoot, SearchOption.AllDirectories)
                .Select(h => h.FilePath)
                .ToList();

            Assert.Contains(found, f =>
                f.Replace('\\', '/').EndsWith("machines/sub/NestedHsm.hsm.json", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    // ── Existing save of subfolder asset preserves its path ─────────────────

    /// <summary>
    /// An asset that already has a SourceFilePath with a subfolder can be
    /// re-saved (overwritten) at that same path — existing Save behavior.
    /// </summary>
    [Fact]
    public void ExistingAssetWithSubfolder_Save_KeepsWritingToSamePath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"B19_T7_Existing_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var svc = new BTreeNewAssetService(tempRoot);
            var asset = svc.CreateNew(recipe: null, name: "KeepPath", relPath: "nested/deep");
            var originalPath = asset.SourceFilePath;
            Assert.True(File.Exists(originalPath));

            // Simulate Save: write again to the same path (existing behavior).
            var dto = new BehaviorTreeAssetDto
            {
                AssetId         = asset.AssetId,
                Name            = "KeepPath",
                TargetNamespace = "",
                BlackboardTypeName = "",
                ContextTypeName = "",
                Canvas          = new CanvasDto { Zoom = 2.0f }, // changed
                Nodes           = new List<BTreeNodeDto>(),
                Pills           = new List<BTreePillDto>(),
                SubtreeSyncBindings = new Dictionary<string, List<SubtreeSyncBindingDto>>(),
                Suppressions    = new SuppressionsDto(),
                Blackboard      = new BlackboardBlockDto(),
            };
            var json = BTreeJsonServices.Serialize(dto);
            File.WriteAllText(originalPath, json);

            // Verify the file still exists and was overwritten.
            Assert.True(File.Exists(originalPath));
            var reloaded = BTreeJsonServices.Deserialize(File.ReadAllText(originalPath));
            Assert.NotNull(reloaded);
            Assert.Equal(2.0f, reloaded!.Canvas.Zoom);

            // Scan still finds it at the same relpath.
            var found = BTreeJsonServices.DiscoverHeaders(tempRoot, SearchOption.AllDirectories)
                .ToList();
            Assert.Contains(found, h => h.FilePath.Replace('\\', '/').EndsWith("nested/deep/KeepPath.btree.json",
                StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}
