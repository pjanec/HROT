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
// PU-502 Task 2: SaveAllAiDocumentsCommand — collision guard wiring
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Headless tests verifying that <see cref="SaveAllAiDocumentsCommand.Execute"/>
/// correctly blocks BTree/HSM JSON writes when a D5 base-name collision is detected
/// (design §3 D5, PU-502 Task 2).
///
/// The injected <c>listFilesInDir</c> delegate stands in for
/// <c>Directory.EnumerateFiles</c>, so no real filesystem writes occur.
/// </summary>
public sealed class SaveAllWithCollisionGuardTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static AiDocumentManager MakeManager()
        => new AiDocumentManager(perspectiveSwitchCallback: _ => { });

    // ── Task 2 — collision blocks BTree write ─────────────────────────────────

    /// <summary>
    /// Real filesystem test: a temp dir contains <c>Foo.cs</c>.
    /// Saving <c>Foo.btree.json</c> must be blocked; doc stays dirty; [BLOCKED] reported.
    /// </summary>
    [Fact]
    public void ExecuteReal_BTree_CollidingCs_Blocked_DocStaysDirty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pu502_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Plant the colliding sibling .cs file.
            var csPath   = Path.Combine(tempDir, "Foo.cs");
            File.WriteAllText(csPath, "// generated");

            var jsonPath = Path.Combine(tempDir, "Foo.btree.json");
            var emptyBlob = new BehaviorTreeBlob
            {
                TreeName        = "Foo",
                Nodes           = Array.Empty<NodeDefinition>(),
                MethodNames     = Array.Empty<string>(),
                FloatParams     = Array.Empty<float>(),
                IntParams       = Array.Empty<int>(),
                SubtreeAssetIds = Array.Empty<string>(),
            };
            var asset = new BehaviorTreeAsset(
                assetId:           Guid.NewGuid(),
                name:              "Foo",
                sourceFilePath:    jsonPath,
                isEditorOwned:     true,
                blackboardTypeName: "",
                contextTypeName:    "",
                blob:              emptyBlob,
                targetNamespace:   "Test");

            var mgr = MakeManager();
            var doc = mgr.Open(asset);
            doc.MarkDirty();

            bool delegateCalled = false;
            var reports = new List<string>();

            SaveAllAiDocumentsCommand.Execute(
                manager:               mgr,
                saveBlueprintDelegate: null,
                saveBTreeDelegate: (a, path) =>
                {
                    delegateCalled = true;
                    AtomicFileWriter.Write(path, "{}");
                },
                saveHsmDelegate: null,
                report: msg => reports.Add(msg));

            // The JSON must NOT have been written.
            Assert.False(File.Exists(jsonPath), "JSON must not have been written due to D5 collision");

            // The save delegate must NOT have been called.
            Assert.False(delegateCalled, "Save delegate must not be called on a blocked write");

            // A [BLOCKED] line must have been reported.
            Assert.Contains(reports, r => r.Contains("[BLOCKED]"));

            // The document must still be dirty (not silently cleaned).
            Assert.True(doc.IsDirty, "Doc must remain dirty after a blocked write");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Real filesystem test (HSM direction): dir contains <c>Bar.cs</c>.
    /// Saving <c>Bar.hsm.json</c> must be blocked.
    /// </summary>
    [Fact]
    public void ExecuteReal_Hsm_CollidingCs_Blocked_DocStaysDirty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pu502_hsm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Bar.cs"), "// generated");

            var jsonPath = Path.Combine(tempDir, "Bar.hsm.json");
            var dto = new HsmAssetDto
            {
                AssetId         = Guid.NewGuid(),
                Name            = "Bar",
                TargetNamespace = "Test",
            };
            var asset = HsmAssetMapper.ToModel(dto, jsonPath, isEditorOwned: true);

            var mgr = MakeManager();
            var doc = mgr.Open(asset);
            doc.MarkDirty();

            bool delegateCalled = false;
            var reports = new List<string>();

            SaveAllAiDocumentsCommand.Execute(
                manager:               mgr,
                saveBlueprintDelegate: null,
                saveBTreeDelegate:     null,
                saveHsmDelegate: (a, path) =>
                {
                    delegateCalled = true;
                    AtomicFileWriter.Write(path, "{}");
                },
                report: msg => reports.Add(msg));

            Assert.False(File.Exists(jsonPath),    "HSM JSON must not have been written");
            Assert.False(delegateCalled,            "HSM save delegate must not be called");
            Assert.Contains(reports, r => r.Contains("[BLOCKED]"));
            Assert.True(doc.IsDirty,               "HSM doc must remain dirty");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Regression (BATCH-06): no-collision sibling still writes normally.
    /// </summary>
    [Fact]
    public void ExecuteReal_BTree_NoCsCollision_WritesNormally()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pu502_noblock_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Plant a non-colliding sibling (different base name).
            File.WriteAllText(Path.Combine(tempDir, "Other.cs"), "// other");

            var jsonPath = Path.Combine(tempDir, "MyTree.btree.json");
            var emptyBlob = new BehaviorTreeBlob
            {
                TreeName        = "MyTree",
                Nodes           = Array.Empty<NodeDefinition>(),
                MethodNames     = Array.Empty<string>(),
                FloatParams     = Array.Empty<float>(),
                IntParams       = Array.Empty<int>(),
                SubtreeAssetIds = Array.Empty<string>(),
            };
            var asset = new BehaviorTreeAsset(
                assetId:           Guid.NewGuid(),
                name:              "MyTree",
                sourceFilePath:    jsonPath,
                isEditorOwned:     true,
                blackboardTypeName: "",
                contextTypeName:    "",
                blob:              emptyBlob,
                targetNamespace:   "Test");

            var mgr = MakeManager();
            var doc = mgr.Open(asset);
            doc.MarkDirty();

            var reports = new List<string>();
            SaveAllAiDocumentsCommand.Execute(
                manager:               mgr,
                saveBlueprintDelegate: null,
                saveBTreeDelegate: (a, path) =>
                {
                    var dto = BehaviorTreeAssetMapper.ToDto((BehaviorTreeAsset)a);
                    AtomicFileWriter.Write(path, BTreeJsonServices.Serialize(dto));
                },
                saveHsmDelegate: null,
                report: msg => reports.Add(msg));

            // File should have been written normally.
            Assert.True(File.Exists(jsonPath),            "JSON must have been written");
            Assert.False(doc.IsDirty,                     "Doc should be clean after normal save");
            Assert.DoesNotContain(reports, r => r.Contains("[BLOCKED]"));

            // Verify round-trip.
            var dto2 = BTreeJsonServices.Deserialize(File.ReadAllText(jsonPath));
            Assert.NotNull(dto2);
            Assert.Equal("MyTree", dto2!.Name);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
