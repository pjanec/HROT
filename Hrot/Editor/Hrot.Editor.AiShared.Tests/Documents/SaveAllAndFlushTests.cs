using System;
using System.Collections.Generic;
using System.IO;
using Fbt;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Documents;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Documents;

// ─────────────────────────────────────────────────────────────────────────────
// Helpers shared across the test classes in this file
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Creates a minimal <see cref="BehaviorTreeAsset"/> with a given name and
/// optional source-file path for use in headless Save-All tests.
/// </summary>
file static class BTreeAssetFactory
{
    public static BehaviorTreeAsset Create(string name, string sourcePath = "")
    {
        var emptyBlob = new BehaviorTreeBlob
        {
            TreeName        = name,
            Nodes           = Array.Empty<NodeDefinition>(),
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
        };
        return new BehaviorTreeAsset(
            assetId:           Guid.NewGuid(),
            name:              name,
            sourceFilePath:    sourcePath,
            isEditorOwned:     true,
            blackboardTypeName: "",
            contextTypeName:    "",
            blob:              emptyBlob,
            targetNamespace:   "Test");
    }
}

/// <summary>
/// Creates a minimal <see cref="HsmAsset"/> via the DTO/mapper round-trip
/// (avoids needing to call the internal constructor directly).
/// </summary>
file static class HsmAssetFactory
{
    public static HsmAsset Create(string name, string sourcePath = "")
    {
        var dto = new HsmAssetDto
        {
            AssetId         = Guid.NewGuid(),
            Name            = name,
            TargetNamespace = "Test",
        };
        // Use ToModel so we can set SourceFilePath
        return HsmAssetMapper.ToModel(dto, sourcePath, isEditorOwned: true);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PU-602: AtomicFileWriter
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Tests for <see cref="AtomicFileWriter"/> (netstandard2.0; PU-602).
/// </summary>
public sealed class AtomicFileWriterTests
{
    [Fact]
    public void Write_CreatesFile_WithCorrectContent()
    {
        var dir  = Path.GetTempPath();
        var path = Path.Combine(dir, $"atfw_test_{Guid.NewGuid():N}.json");
        try
        {
            AtomicFileWriter.Write(path, "{\"x\":1}");
            Assert.True(File.Exists(path));
            Assert.Equal("{\"x\":1}", File.ReadAllText(path));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"atfw_overwrite_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "old");
            AtomicFileWriter.Write(path, "new");
            Assert.Equal("new", File.ReadAllText(path));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Write_CreatesDirectoryIfAbsent()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"atfw_dir_{Guid.NewGuid():N}");
        var path   = Path.Combine(subDir, "test.json");
        try
        {
            AtomicFileWriter.Write(path, "{}");
            Assert.True(File.Exists(path));
        }
        finally { try { Directory.Delete(subDir, true); } catch { } }
    }

    [Fact]
    public void Write_NullPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => AtomicFileWriter.Write(null!, "content"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// PU-602: SaveAllAiDocumentsCommand
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Headless tests for <see cref="SaveAllAiDocumentsCommand"/>.
/// </summary>
public sealed class SaveAllAiDocumentsCommandTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static AiDocumentManager MakeManager()
        => new AiDocumentManager(perspectiveSwitchCallback: _ => { });

    // Minimal "save delegate" that records calls.
    private sealed class SaveSpy
    {
        public readonly List<(IEditableAsset asset, string path)> Calls = new();
        public void Invoke(IEditableAsset a, string p) => Calls.Add((a, p));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_NullManager_IsNoOp()
    {
        // Should not throw.
        var spy = new SaveSpy();
        SaveAllAiDocumentsCommand.Execute(null,
            (a, p) => spy.Invoke(a, p), null, null);
        Assert.Empty(spy.Calls);
    }

    [Fact]
    public void Execute_CleanDocs_NotWritten()
    {
        var mgr    = MakeManager();
        var btree  = BTreeAssetFactory.Create("Clean", "/tmp/clean.btree.json");
        var doc    = mgr.Open(btree);
        // doc starts clean (not dirty)
        Assert.False(doc.IsDirty);

        var spy = new SaveSpy();
        SaveAllAiDocumentsCommand.Execute(mgr,
            (a, p) => spy.Invoke(a, p),
            (a, p) => spy.Invoke(a, p),
            (a, p) => spy.Invoke(a, p));

        Assert.Empty(spy.Calls); // nothing written — doc was clean
    }

    [Fact]
    public void Execute_NoPath_SkippedWithWarnReport_DocStillDirty()
    {
        var mgr   = MakeManager();
        var btree = BTreeAssetFactory.Create("NoPath", sourcePath: ""); // empty path
        var doc   = mgr.Open(btree);
        doc.MarkDirty();

        var reports = new List<string>();
        var spy     = new SaveSpy();
        SaveAllAiDocumentsCommand.Execute(mgr,
            null,
            (a, p) => spy.Invoke(a, p),
            null,
            msg => reports.Add(msg));

        // Should not have been saved.
        Assert.Empty(spy.Calls);
        // Should have emitted a warning.
        Assert.Contains(reports, r => r.Contains("[WARN]") && r.Contains("NoPath"));
        // Document must still be dirty (not silently cleaned).
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void Execute_DirtyBTree_WriteJsonToFile_MarkClean()
    {
        var tmp  = Path.Combine(Path.GetTempPath(), $"saveall_{Guid.NewGuid():N}.btree.json");
        try
        {
            var mgr   = MakeManager();
            var btree = BTreeAssetFactory.Create("MyTree", tmp);
            var doc   = mgr.Open(btree);
            doc.MarkDirty();

            var reports = new List<string>();
            SaveAllAiDocumentsCommand.Execute(
                mgr,
                saveBlueprintDelegate: null,
                saveBTreeDelegate: (asset, path) =>
                {
                    var a   = (BehaviorTreeAsset)asset;
                    var dto = BehaviorTreeAssetMapper.ToDto(a);
                    var js  = BTreeJsonServices.Serialize(dto);
                    AtomicFileWriter.Write(path, js);
                },
                saveHsmDelegate: null,
                report: msg => reports.Add(msg));

            // File should exist.
            Assert.True(File.Exists(tmp));

            // JSON should round-trip back to the original asset.
            var json2  = File.ReadAllText(tmp);
            var dto2   = BTreeJsonServices.Deserialize(json2);
            Assert.NotNull(dto2);
            Assert.Equal("MyTree", dto2!.Name);
            Assert.Equal(btree.AssetId, dto2.AssetId);

            // Document should be clean after save.
            Assert.False(doc.IsDirty);

            // Report should contain success message.
            Assert.Contains(reports, r => r.Contains("MyTree"));
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    [Fact]
    public void Execute_DirtyHsm_WriteJsonToFile_MarkClean()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"saveall_{Guid.NewGuid():N}.hsm.json");
        try
        {
            var mgr    = MakeManager();
            var hsm    = HsmAssetFactory.Create("MyHsm", tmp);
            var doc    = mgr.Open(hsm);
            doc.MarkDirty();

            var reports = new List<string>();
            SaveAllAiDocumentsCommand.Execute(
                mgr,
                saveBlueprintDelegate: null,
                saveBTreeDelegate:     null,
                saveHsmDelegate: (asset, path) =>
                {
                    var a   = (HsmAsset)asset;
                    var dto = HsmAssetMapper.ToDto(a);
                    var js  = HsmJsonServices.Serialize(dto);
                    AtomicFileWriter.Write(path, js);
                },
                report: msg => reports.Add(msg));

            Assert.True(File.Exists(tmp));

            var json2 = File.ReadAllText(tmp);
            var dto2  = HsmJsonServices.Deserialize(json2);
            Assert.NotNull(dto2);
            Assert.Equal("MyHsm", dto2!.Name);
            Assert.Equal(hsm.AssetId, dto2.AssetId);

            Assert.False(doc.IsDirty);
            Assert.Contains(reports, r => r.Contains("MyHsm"));
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    [Fact]
    public void Execute_MixedDocs_PathDSaved_NoPathSkipped_CleanNotWritten()
    {
        var tmpBTree = Path.Combine(Path.GetTempPath(), $"mixed_{Guid.NewGuid():N}.btree.json");
        var tmpHsm   = Path.Combine(Path.GetTempPath(), $"mixed_{Guid.NewGuid():N}.hsm.json");
        try
        {
            var mgr          = MakeManager();

            var dirtyBTree   = BTreeAssetFactory.Create("DirtyTree", tmpBTree);
            var noPathBTree  = BTreeAssetFactory.Create("NoPathTree", "");    // no path
            var cleanBTree   = BTreeAssetFactory.Create("CleanTree",  tmpBTree + "2");
            var dirtyHsm     = HsmAssetFactory.Create("DirtyHsm", tmpHsm);

            var docDirtyTree  = mgr.Open(dirtyBTree); docDirtyTree.MarkDirty();
            var docNoPath     = mgr.Open(noPathBTree); docNoPath.MarkDirty();
            var docClean      = mgr.Open(cleanBTree);  // stays clean
            var docDirtyHsm   = mgr.Open(dirtyHsm);   docDirtyHsm.MarkDirty();

            var reports = new List<string>();
            SaveAllAiDocumentsCommand.Execute(
                mgr,
                saveBlueprintDelegate: null,
                saveBTreeDelegate: (asset, path) =>
                {
                    var dto = BehaviorTreeAssetMapper.ToDto((BehaviorTreeAsset)asset);
                    AtomicFileWriter.Write(path, BTreeJsonServices.Serialize(dto));
                },
                saveHsmDelegate: (asset, path) =>
                {
                    var dto = HsmAssetMapper.ToDto((HsmAsset)asset);
                    AtomicFileWriter.Write(path, HsmJsonServices.Serialize(dto));
                },
                report: msg => reports.Add(msg));

            // Path'd dirty docs: saved + cleaned.
            Assert.True(File.Exists(tmpBTree), "DirtyTree JSON should exist");
            Assert.True(File.Exists(tmpHsm),   "DirtyHsm JSON should exist");
            Assert.False(docDirtyTree.IsDirty,  "DirtyTree should be clean after save");
            Assert.False(docDirtyHsm.IsDirty,   "DirtyHsm should be clean after save");

            // No-path doc: warn + still dirty.
            Assert.True(docNoPath.IsDirty, "No-path doc should remain dirty");
            Assert.Contains(reports, r => r.Contains("[WARN]") && r.Contains("NoPathTree"));

            // Clean doc: not written (file does not exist).
            Assert.False(File.Exists(tmpBTree + "2"), "CleanTree should not have been saved");
        }
        finally
        {
            try { File.Delete(tmpBTree); } catch { }
            try { File.Delete(tmpHsm);   } catch { }
            try { File.Delete(tmpBTree + "2"); } catch { }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AiDocumentManager.BeforeDocumentClosed event contract
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Headless tests for the <see cref="AiDocumentManager.BeforeDocumentClosed"/> event contract
/// (fires once, before the doc is removed from the open list, with the doc still present).
/// <para>
/// NOTE: the old PU-603 "flush-on-close" — where <c>EditorSubsystem</c> subscribed this event to
/// silently write ANY dirty doc to disk on every close — was REMOVED (save is now decoupled from
/// close: only the unsaved-changes prompt's "Save" persists, via an injected delegate). These
/// tests therefore subscribe their OWN handler and validate the manager's event mechanism only;
/// they no longer mirror any production subscription.
/// </para>
/// </summary>
public sealed class BeforeDocumentClosedEventTests
{
    [Fact]
    public void BeforeDocumentClosed_FiredBeforeDocRemoved_WithDirtyDoc()
    {
        var mgr   = new AiDocumentManager(perspectiveSwitchCallback: _ => { });
        var btree = BTreeAssetFactory.Create("ClosingTree", "/path/to/tree.btree.json");
        var doc   = mgr.Open(btree);
        doc.MarkDirty();

        AiDocument? seenDoc   = null;
        bool        wasInList = false;

        mgr.BeforeDocumentClosed += d =>
        {
            seenDoc   = d;
            // At the time of the event, the doc must still be in the list.
            wasInList = mgr.OpenDocuments.Contains(d);
        };

        mgr.Close(doc);

        // BeforeDocumentClosed must have fired with the correct doc.
        Assert.Same(doc, seenDoc);
        // And the doc was still in the list when the event fired.
        Assert.True(wasInList);
        // After Close the doc is gone.
        Assert.Empty(mgr.OpenDocuments);
    }

    [Fact]
    public void BeforeDocumentClosed_SaveDirtyDoc_ViaSpyDelegate()
    {
        // Event-mechanism check: a subscriber CAN observe the closing doc and act on it.
        // (Production no longer subscribes a flush here — save is decoupled from close — so this
        // only exercises the event surface, using a locally-wired handler.)
        var tmp = Path.Combine(Path.GetTempPath(), $"onclose_{Guid.NewGuid():N}.btree.json");
        try
        {
            var mgr   = new AiDocumentManager(perspectiveSwitchCallback: _ => { });
            var btree = BTreeAssetFactory.Create("OnCloseTree", tmp);
            var doc   = mgr.Open(btree);
            doc.MarkDirty();

            // Wire the flush-on-close handler (mirrors what EditorSubsystem does).
            mgr.BeforeDocumentClosed += d =>
            {
                if (!d.IsDirty) return;
                var asset = d.Asset;
                var path  = asset.SourceFilePath;
                if (string.IsNullOrEmpty(path)) return;

                if (d.Kind == AssetKind.BTree)
                {
                    var a   = (BehaviorTreeAsset)asset;
                    var dto = BehaviorTreeAssetMapper.ToDto(a);
                    var js  = BTreeJsonServices.Serialize(dto);
                    AtomicFileWriter.Write(path, js);
                    d.MarkClean();
                }
            };

            mgr.Close(doc);

            // File was written.
            Assert.True(File.Exists(tmp), "JSON file must have been written on close");

            // Content round-trips.
            var json2 = File.ReadAllText(tmp);
            var dto2  = BTreeJsonServices.Deserialize(json2);
            Assert.NotNull(dto2);
            Assert.Equal("OnCloseTree", dto2!.Name);
            Assert.Equal(btree.AssetId, dto2.AssetId);

            // Doc is clean (marked by the handler).
            Assert.False(doc.IsDirty);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    [Fact]
    public void BeforeDocumentClosed_CleanDoc_NothingWritten()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"onclose_clean_{Guid.NewGuid():N}.btree.json");
        try
        {
            var mgr   = new AiDocumentManager(perspectiveSwitchCallback: _ => { });
            var btree = BTreeAssetFactory.Create("CleanOnClose", tmp);
            var doc   = mgr.Open(btree);
            // doc is NOT dirtied

            bool handlerFired = false;
            bool wroteFile    = false;
            mgr.BeforeDocumentClosed += d =>
            {
                handlerFired = true;
                if (!d.IsDirty) return;
                wroteFile = true;
            };

            mgr.Close(doc);

            Assert.True(handlerFired);      // handler fires regardless
            Assert.False(wroteFile);        // but skips write because not dirty
            Assert.False(File.Exists(tmp)); // file never written
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    [Fact]
    public void BeforeDocumentClosed_HsmDirtyDoc_SavesHsmJson()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"onclose_hsm_{Guid.NewGuid():N}.hsm.json");
        try
        {
            var mgr = new AiDocumentManager(perspectiveSwitchCallback: _ => { });
            var hsm = HsmAssetFactory.Create("OnCloseHsm", tmp);
            var doc = mgr.Open(hsm);
            doc.MarkDirty();

            mgr.BeforeDocumentClosed += d =>
            {
                if (!d.IsDirty) return;
                var path = d.Asset.SourceFilePath;
                if (string.IsNullOrEmpty(path)) return;
                if (d.Kind == AssetKind.Hsm)
                {
                    var a   = (HsmAsset)d.Asset;
                    var dto = HsmAssetMapper.ToDto(a);
                    var js  = HsmJsonServices.Serialize(dto);
                    AtomicFileWriter.Write(path, js);
                    d.MarkClean();
                }
            };

            mgr.Close(doc);

            Assert.True(File.Exists(tmp));
            var dto2 = HsmJsonServices.Deserialize(File.ReadAllText(tmp));
            Assert.NotNull(dto2);
            Assert.Equal("OnCloseHsm", dto2!.Name);
            Assert.False(doc.IsDirty);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    [Fact]
    public void BeforeDocumentClosed_SaveAllCallback_InvokedByExecute()
    {
        // Confirm that SaveAllAiDocumentsCommand.Execute dispatches correctly
        // when called from a simulated _saveAllCallback (PU-603 wiring test).
        var tmp = Path.Combine(Path.GetTempPath(), $"callback_{Guid.NewGuid():N}.btree.json");
        try
        {
            var mgr   = new AiDocumentManager(perspectiveSwitchCallback: _ => { });
            var btree = BTreeAssetFactory.Create("CallbackTree", tmp);
            var doc   = mgr.Open(btree);
            doc.MarkDirty();

            // Simulate the _saveAllCallback that EditorSubsystem wires.
            var saveAllCallback = new Action(() =>
            {
                SaveAllAiDocumentsCommand.Execute(
                    mgr,
                    saveBlueprintDelegate: null,
                    saveBTreeDelegate: (asset, path) =>
                    {
                        var dto = BehaviorTreeAssetMapper.ToDto((BehaviorTreeAsset)asset);
                        AtomicFileWriter.Write(path, BTreeJsonServices.Serialize(dto));
                    },
                    saveHsmDelegate: null);
            });

            // Invoke the callback (as if user pressed Ctrl+Shift+S or shutdown occurred).
            saveAllCallback();

            Assert.True(File.Exists(tmp));
            Assert.False(doc.IsDirty);
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
}
