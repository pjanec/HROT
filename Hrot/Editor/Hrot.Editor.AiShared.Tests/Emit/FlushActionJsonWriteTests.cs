using System;
using System.IO;
using System.Text;
using Fbt;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Emit;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Emit;

/// <summary>
/// PU-D11 (PU-402): Headless test proving that the debounced <see cref="RegenerationScheduler"/>
/// flushAction, when wired with <c>saveBTreeDelegate</c> / <c>saveHsmDelegate</c> (the same
/// JSON-write delegates used by Save-All), produces round-trippable JSON at
/// <see cref="IEditableAsset.SourceFilePath"/> — NOT C#.
///
/// Mirrors the production wiring in <c>EditorSubsystem.Initialize</c> (PU-D11 fix).
///
/// Scope note (per BATCH-09 instructions): this test proves the flush PERSISTS correctly
/// (writes valid JSON). The end-to-end edit→MSBuild-regen→hot-reload latency is the subject
/// of Phase 9 (≤100 ms quick reload) + the user's manual editor smoke (deferred).
/// </summary>
public sealed class FlushActionJsonWriteTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BehaviorTreeAsset MakeBTreeAsset(string name, string path)
    {
        var blob = new BehaviorTreeBlob
        {
            TreeName        = name,
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };
        return new BehaviorTreeAsset(
            assetId:            Guid.NewGuid(),
            name:               name,
            sourceFilePath:     path,
            isEditorOwned:      true,
            blackboardTypeName: "",
            contextTypeName:    "",
            blob:               blob,
            targetNamespace:    "Test");
    }

    private static HsmAsset MakeHsmAsset(string name, string path)
    {
        var dto = new HsmAssetDto
        {
            AssetId         = Guid.NewGuid(),
            Name            = name,
            TargetNamespace = "Test",
        };
        return HsmAssetMapper.ToModel(dto, path, isEditorOwned: true);
    }

    // ── PU-D11 SC1: dirty BTree with path → flush writes JSON (not C#) ────────

    [Fact]
    public void FlushAction_DirtyBTree_WritesRoundTrippableJson_NotCSharp()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"flushbTree_{Guid.NewGuid():N}.btree.json");
        try
        {
            var btree = MakeBTreeAsset("FlushTree", tmp);

            // Wire saveBTreeDelegate (mirrors EditorSubsystem production wiring for PU-D11).
            SaveAllAiDocumentsCommand.SaveDelegate saveBTreeDelegate = (asset, path) =>
            {
                var a    = (BehaviorTreeAsset)asset;
                var dto  = BehaviorTreeAssetMapper.ToDto(a);
                var json = BTreeJsonServices.Serialize(dto);
                AtomicFileWriter.Write(path, json);
            };

            // Build the flushAction as EditorSubsystem does post-PU-D11:
            // BTree → run collision guard (benign) → saveBTreeDelegate.
            var flushed = 0;
            var scheduler = new RegenerationScheduler(
                flushAction: asset =>
                {
                    if (asset.Kind == AssetKind.Blueprint) return; // blueprint unchanged
                    try
                    {
                        var path = asset.SourceFilePath;
                        if (string.IsNullOrEmpty(path)) return;
                        if (asset.Kind == AssetKind.BTree)
                        {
                            var collision = AssetBaseNameCollisionGuard.CheckCollisionOnDisk(
                                path, System.IO.Directory.EnumerateFiles);
                            if (collision != null) return;
                            saveBTreeDelegate(asset, path);
                            flushed++;
                        }
                    }
                    catch { /* never throw from flush */ }
                },
                debounceTicks: 0); // instant for test

            scheduler.Schedule(btree);
            scheduler.FlushNow();

            // File must have been written.
            Assert.True(File.Exists(tmp), "flush must write the file");
            Assert.Equal(1, flushed);

            // Content must be valid JSON (not C#).
            var written = File.ReadAllText(tmp, Encoding.UTF8);
            written.Should_BeValidBTreeJson("FlushTree", btree.AssetId);

            // Round-trip: Deserialize → Serialize → byte-stable.
            var dto2   = BTreeJsonServices.Deserialize(written);
            Assert.NotNull(dto2);
            Assert.Equal("FlushTree", dto2!.Name);
            Assert.Equal(btree.AssetId, dto2.AssetId);
            var json2 = BTreeJsonServices.Serialize(dto2);
            Assert.Equal(written, json2);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    // ── PU-D11 SC2: dirty HSM with path → flush writes round-trippable JSON ───

    [Fact]
    public void FlushAction_DirtyHsm_WritesRoundTrippableJson_NotCSharp()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"flushHsm_{Guid.NewGuid():N}.hsm.json");
        try
        {
            var hsm = MakeHsmAsset("FlushHsm", tmp);

            SaveAllAiDocumentsCommand.SaveDelegate saveHsmDelegate = (asset, path) =>
            {
                var a    = (HsmAsset)asset;
                var dto  = HsmAssetMapper.ToDto(a);
                var json = HsmJsonServices.Serialize(dto);
                AtomicFileWriter.Write(path, json);
            };

            var flushed = 0;
            var scheduler = new RegenerationScheduler(
                flushAction: asset =>
                {
                    if (asset.Kind == AssetKind.Blueprint) return;
                    try
                    {
                        var path = asset.SourceFilePath;
                        if (string.IsNullOrEmpty(path)) return;
                        if (asset.Kind == AssetKind.Hsm)
                        {
                            var collision = AssetBaseNameCollisionGuard.CheckCollisionOnDisk(
                                path, System.IO.Directory.EnumerateFiles);
                            if (collision != null) return;
                            saveHsmDelegate(asset, path);
                            flushed++;
                        }
                    }
                    catch { /* never throw from flush */ }
                },
                debounceTicks: 0);

            scheduler.Schedule(hsm);
            scheduler.FlushNow();

            Assert.True(File.Exists(tmp), "flush must write the file");
            Assert.Equal(1, flushed);

            var written = File.ReadAllText(tmp, Encoding.UTF8);
            written.Should_BeValidHsmJson("FlushHsm", hsm.AssetId);

            var dto2   = HsmJsonServices.Deserialize(written);
            Assert.NotNull(dto2);
            Assert.Equal("FlushHsm", dto2!.Name);
            Assert.Equal(hsm.AssetId, dto2.AssetId);
            var json2 = HsmJsonServices.Serialize(dto2);
            Assert.Equal(written, json2);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    // ── PU-D11 SC3: no-path asset → flush skips silently (no throw) ──────────

    [Fact]
    public void FlushAction_NoPath_SkipsSilently_DoesNotThrow()
    {
        var btree = MakeBTreeAsset("NoPathTree", path: "");

        SaveAllAiDocumentsCommand.SaveDelegate saveBTreeDelegate = (asset, path) =>
        {
            throw new InvalidOperationException("should never be called for no-path asset");
        };

        var scheduler = new RegenerationScheduler(
            flushAction: asset =>
            {
                if (asset.Kind == AssetKind.Blueprint) return;
                try
                {
                    var path = asset.SourceFilePath;
                    if (string.IsNullOrEmpty(path)) return; // skip silently
                    if (asset.Kind == AssetKind.BTree)
                        saveBTreeDelegate(asset, path);
                }
                catch { /* never throw */ }
            },
            debounceTicks: 0);

        scheduler.Schedule(btree);

        // Must not throw.
        var ex = Record.Exception(() => scheduler.FlushNow());
        Assert.Null(ex);
    }

    // ── PU-D11 SC4: Blueprint asset → flush routes through blueprint path (unchanged) ──

    [Fact]
    public void FlushAction_Blueprint_IsRouted_ToBlueprint_NotJson()
    {
        // Simulate a Blueprint asset (we just check it's routed, not via JSON delegates).
        var blueprintRouteCalled = false;

        var bpAsset = new _MinimalBlueprintAsset();

        SaveAllAiDocumentsCommand.SaveDelegate saveBTreeDelegate = (_, __) =>
            throw new InvalidOperationException("BTree delegate must NOT be called for Blueprint");

        var scheduler = new RegenerationScheduler(
            flushAction: asset =>
            {
                if (asset.Kind == AssetKind.Blueprint)
                {
                    blueprintRouteCalled = true;
                    return; // blueprint handled separately (e.g. QuickReloadService)
                }
                // BTree/HSM path
                saveBTreeDelegate(asset, asset.SourceFilePath);
            },
            debounceTicks: 0);

        scheduler.Schedule(bpAsset);
        scheduler.FlushNow();

        Assert.True(blueprintRouteCalled, "Blueprint must be routed to blueprint path");
    }

    // ── Helper assertions ────────────────────────────────────────────────────────

    private sealed class _MinimalBlueprintAsset : IEditableAsset
    {
        public Guid AssetId { get; } = Guid.NewGuid();
        public string Name => "TestBp";
        public AssetKind Kind => AssetKind.Blueprint;
        public string SourceFilePath => "/fake/test.bp.json";
        public bool IsDirty => true;
        public bool IsEditorOwned => true;
        public event Action? Changed { add { } remove { } }
    }
}

/// <summary>
/// Assertion helpers for JSON content validation in flush tests.
/// </summary>
file static class FlushJsonAssertions
{
    public static void Should_BeValidBTreeJson(this string json, string expectedName, Guid expectedAssetId)
    {
        // Must not start with C# code
        Assert.False(json.TrimStart().StartsWith("//") || json.TrimStart().StartsWith("using"),
            $"Written content looks like C# (starts with comment or using), expected JSON. " +
            $"Content start: {json.Substring(0, Math.Min(60, json.Length))}");

        // Must be valid JSON containing expected name and assetId
        Assert.Contains($"\"Name\":\"{expectedName}\"", json);
        Assert.True(json.Contains($"\"AssetId\":\"{expectedAssetId:D}\"", StringComparison.OrdinalIgnoreCase),
            $"BTree JSON must contain AssetId {expectedAssetId:D}");
        Assert.Contains("\"$meta\"", json);
        Assert.Contains("\"docType\":\"Hrot.BTree\"", json);
    }

    public static void Should_BeValidHsmJson(this string json, string expectedName, Guid expectedAssetId)
    {
        Assert.False(json.TrimStart().StartsWith("//") || json.TrimStart().StartsWith("using"),
            $"Written content looks like C# (starts with comment or using), expected JSON. " +
            $"Content start: {json.Substring(0, Math.Min(60, json.Length))}");

        Assert.Contains($"\"Name\":\"{expectedName}\"", json);
        Assert.True(json.Contains($"\"AssetId\":\"{expectedAssetId:D}\"", StringComparison.OrdinalIgnoreCase),
            $"HSM JSON must contain AssetId {expectedAssetId:D}");
        Assert.Contains("\"$meta\"", json);
        Assert.Contains("\"docType\":\"Hrot.Hsm\"", json);
    }
}
