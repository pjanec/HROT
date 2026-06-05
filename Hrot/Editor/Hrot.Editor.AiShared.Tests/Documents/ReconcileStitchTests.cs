using System;
using System.IO;
using System.Collections.Generic;
using FluentAssertions;
using Fbt;
using Fhsm.Kernel.Data;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Documents;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Hrot.Editor.AiShared.Catalog;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Documents;

/// <summary>
/// PU-301 acceptance + PU-302 stitch tests (headless).
///
/// Coverage:
/// - PU-301 acceptance: JSON-loaded asset opens with no assembly contribution
///   (ReconcileFromCatalog(empty) leaves document open, topology intact, no throw).
/// - PU-302 stitch: StitchKernelIndices assigns correct KernelBlobIndex by VisualId,
///   updates Blob, unmatched node gets sentinel + diagnostic.
/// - Kind-guard: Blueprint doc → full-replace path (ReconcileAsset), not stitch.
/// - No-dirty: load + stitch leave IsDirty==false.
/// </summary>
public sealed class ReconcileStitchTests : IDisposable
{
    private readonly string _tempDir;

    public ReconcileStitchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ReconcileStitch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ── BTree helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a BehaviorTreeBlob with two nodes (Root → Sequence) whose
    /// DebugMetadata carries the supplied visual IDs.
    /// </summary>
    private static BehaviorTreeBlob MakeBlobWithVisualIds(Guid rootVisualId, Guid seqVisualId)
    {
        return new BehaviorTreeBlob
        {
            TreeName        = "Test",
            Nodes           = new[]
            {
                new NodeDefinition { Type = NodeType.Root,     ChildCount = 1, SubtreeOffset = 2 },
                new NodeDefinition { Type = NodeType.Sequence, ChildCount = 0, SubtreeOffset = 1 },
            },
            MethodNames     = Array.Empty<string>(),
            FloatParams     = Array.Empty<float>(),
            IntParams       = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
            DebugMetadata   = new[]
            {
                new NodeDebugMetadata { Label = "Root",     VisualId = rootVisualId.ToString() },
                new NodeDebugMetadata { Label = "Sequence", VisualId = seqVisualId.ToString() },
            },
        };
    }

    /// <summary>
    /// Creates a BehaviorTreeAssetDto with two nodes whose VisualIds are known.
    /// Returns the dto plus the two VisualIds for assertion.
    /// </summary>
    private static (BehaviorTreeAssetDto Dto, Guid RootVisualId, Guid SeqVisualId) MakeBTreeDto(
        string name = "TestTree",
        Guid? assetId = null)
    {
        var id         = assetId ?? Guid.NewGuid();
        var rootVid    = Guid.NewGuid();
        var seqVid     = Guid.NewGuid();

        var dto = new BehaviorTreeAssetDto
        {
            AssetId         = id,
            Name            = name,
            TargetNamespace = "Test.Trees",
        };

        var rootNode = new BTreeRootNodeDto
        {
            VisualId       = rootVid,
            EditorMetadata = new NodeEditorMetadataDto { X = 100f, Y = 200f },
        };
        rootNode.ChildVisualIds.Add(seqVid);
        dto.Nodes.Add(rootNode);
        dto.Nodes.Add(new BTreeSequenceNodeDto
        {
            VisualId       = seqVid,
            EditorMetadata = new NodeEditorMetadataDto { X = 150f, Y = 300f },
        });
        return (dto, rootVid, seqVid);
    }

    private string WriteBTreeJson(BehaviorTreeAssetDto dto, string? fileName = null)
    {
        var json = BTreeJsonServices.Serialize(dto);
        var path = Path.Combine(_tempDir, fileName ?? (dto.Name + ".btree.json"));
        File.WriteAllText(path, json);
        return path;
    }

    // ── HSM helpers ───────────────────────────────────────────────────────────

    private static (HsmAssetDto Dto, Guid StateStableId) MakeHsmDto(
        string name = "TestMachine",
        Guid? assetId = null)
    {
        var id      = assetId ?? Guid.NewGuid();
        var stableId = Guid.NewGuid();

        var dto = new HsmAssetDto
        {
            AssetId         = id,
            Name            = name,
            TargetNamespace = "Test.Machines",
        };
        dto.States.Add(new StateNodeDto
        {
            StableId  = stableId,
            Name      = "Idle",
            IsInitial = true,
            X = 100f,
            Y = 150f,
        });
        return (dto, stableId);
    }

    private string WriteHsmJson(HsmAssetDto dto, string? fileName = null)
    {
        var json = HsmJsonServices.Serialize(dto);
        var path = Path.Combine(_tempDir, fileName ?? (dto.Name + ".hsm.json"));
        File.WriteAllText(path, json);
        return path;
    }

    private static AiDocumentManager MakeDocManager()
        => new AiDocumentManager(kind => { });

    // ────────────────────────────────────────────────────────────────────────────
    // PU-301 acceptance: reopen-when-C#-won't-compile (BTree)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// CRUX: valid .btree.json on disk + NO assembly contribution for the same AssetId
    /// (simulates "assembly failed to compile") → JSON contributor LoadFull's it,
    /// AiDocumentManager.Open succeeds, ReconcileFromCatalog(empty) leaves the document
    /// open with topology intact (no throw).  Runtime indices are unset (stitch inert).
    /// </summary>
    [Fact]
    public void PU301_Acceptance_BTree_ReopenWhenCSharpBroken_DocumentStaysOpenTopologyIntact()
    {
        var (dto, rootVid, seqVid) = MakeBTreeDto("NoAssemblyTree");
        WriteBTreeJson(dto);

        // JSON contributor: only file-based, no assembly contribution
        var jsonContrib = new BTreeJsonAssetContributor();
        jsonContrib.Refresh(rootDirectory: _tempDir);

        var catalog = new AssetCatalog();
        catalog.AddContributor(jsonContrib);

        // Open the document
        var docManager = MakeDocManager();
        var jsonAsset  = (BehaviorTreeAsset)catalog.FindByAssetId(dto.AssetId)!;
        jsonAsset.Should().NotBeNull("JSON contributor should have loaded the asset");

        var doc = docManager.Open(jsonAsset);
        doc.Should().NotBeNull("Open must succeed with JSON-loaded asset");
        doc.Asset.Should().BeSameAs(jsonAsset);

        // Simulate reload with EMPTY catalog (assembly failed → no assembly entry)
        var exception = Record.Exception(() =>
            docManager.ReconcileFromCatalog(Array.Empty<IEditableAsset>()));
        exception.Should().BeNull("ReconcileFromCatalog(empty) must not throw");

        // Document should still be open with topology intact
        docManager.OpenDocuments.Should().HaveCount(1, "document must not be closed");
        var stayedDoc = docManager.OpenDocuments[0];
        var stayedAsset = (BehaviorTreeAsset)stayedDoc.Asset;
        stayedAsset.Nodes.Should().HaveCount(2,
            "topology must be intact even when assembly not available");

        // Runtime indices are unset (stitch inert — blob was empty)
        stayedAsset.FindBlobIndex(rootVid).Should().Be(-1,
            "without assembly blob, KernelBlobIndex must remain sentinel");
        stayedAsset.FindBlobIndex(seqVid).Should().Be(-1,
            "without assembly blob, KernelBlobIndex must remain sentinel");

        // IsDirty must be false — reopen-and-reconcile must NOT dirty the document
        stayedAsset.IsDirty.Should().BeFalse(
            "reopen from JSON must never set IsDirty (PU-602 constraint)");
    }

    /// <summary>
    /// Symmetric acceptance test for HSM.
    /// </summary>
    [Fact]
    public void PU301_Acceptance_Hsm_ReopenWhenCSharpBroken_DocumentStaysOpenTopologyIntact()
    {
        var (dto, stableId) = MakeHsmDto("NoAssemblyMachine");
        WriteHsmJson(dto);

        var jsonContrib = new HsmJsonAssetContributor();
        jsonContrib.Refresh(rootDirectory: _tempDir);

        var catalog = new AssetCatalog();
        catalog.AddContributor(jsonContrib);

        var docManager = MakeDocManager();
        var jsonAsset  = (HsmAsset)catalog.FindByAssetId(dto.AssetId)!;
        jsonAsset.Should().NotBeNull();

        var doc = docManager.Open(jsonAsset);
        doc.Should().NotBeNull();

        var ex = Record.Exception(() =>
            docManager.ReconcileFromCatalog(Array.Empty<IEditableAsset>()));
        ex.Should().BeNull("ReconcileFromCatalog(empty) must not throw for HSM");

        docManager.OpenDocuments.Should().HaveCount(1);
        var stayedAsset = (HsmAsset)docManager.OpenDocuments[0].Asset;
        stayedAsset.AllStates.Should().HaveCount(1, "HSM topology must be intact");
        stayedAsset.IsDirty.Should().BeFalse("reopen must not dirty the document");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // PU-302: Stitch assigns correct KernelBlobIndex by VisualId
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// StitchKernelIndices maps blob[0] → rootVid, blob[1] → seqVid.
    /// After stitch the correct indices are present and Blob is updated.
    /// </summary>
    [Fact]
    public void PU302_Stitch_BTree_CorrectKernelBlobIndex_ByVisualId()
    {
        var (dto, rootVid, seqVid) = MakeBTreeDto("StitchTree");
        WriteBTreeJson(dto);

        // Load via JSON contributor
        var jsonContrib = new BTreeJsonAssetContributor();
        jsonContrib.Refresh(rootDirectory: _tempDir);
        var jsonAsset = (BehaviorTreeAsset)jsonContrib.Enumerate()[0];

        // Build "fresh" assembly-projected asset with matching VisualIds in DebugMetadata
        var freshBlob = MakeBlobWithVisualIds(rootVid, seqVid);
        var freshAsset = BehaviorTreeAssetProjector.Project(
            freshBlob,
            freshBlob.DebugMetadata,
            layout: null,
            dto.AssetId,
            dto.Name,
            sourceFilePath: string.Empty,
            isEditorOwned: false,
            dto.BlackboardTypeName,
            dto.ContextTypeName,
            dto.TargetNamespace);

        // Stitch
        jsonAsset.StitchKernelIndices(freshAsset);

        // Root is blob index 0, Sequence is blob index 1
        jsonAsset.FindBlobIndex(rootVid).Should().Be(0,
            "root node must map to blob index 0 (VisualId match)");
        jsonAsset.FindBlobIndex(seqVid).Should().Be(1,
            "sequence node must map to blob index 1 (VisualId match)");

        // Blob reference must be updated to the recompiled blob
        jsonAsset.Blob.Should().BeSameAs(freshBlob,
            "Blob reference must be updated to the fresh blob after stitch");

        // IsDirty must NOT be set by stitch
        jsonAsset.IsDirty.Should().BeFalse("stitch must not call MarkDirty (PU-602)");
    }

    /// <summary>
    /// Unmatched node (no blob entry for its VisualId) gets sentinel index (-1)
    /// and a diagnostic is set on the asset.
    /// </summary>
    [Fact]
    public void PU302_Stitch_BTree_UnmatchedNode_Getssentinel_AndDiagnostic()
    {
        var (dto, rootVid, seqVid) = MakeBTreeDto("UnmatchedTree");
        WriteBTreeJson(dto);

        var jsonContrib = new BTreeJsonAssetContributor();
        jsonContrib.Refresh(rootDirectory: _tempDir);
        var jsonAsset = (BehaviorTreeAsset)jsonContrib.Enumerate()[0];

        // Build a blob that matches only rootVid (seqVid has no matching entry)
        var unmatchedBlob = new BehaviorTreeBlob
        {
            TreeName      = "Test",
            Nodes         = new[] { new NodeDefinition { Type = NodeType.Root, ChildCount = 0, SubtreeOffset = 1 } },
            MethodNames   = Array.Empty<string>(),
            FloatParams   = Array.Empty<float>(),
            IntParams     = Array.Empty<int>(),
            SubtreeAssetIds = Array.Empty<string>(),
            DebugMetadata = new[] { new NodeDebugMetadata { VisualId = rootVid.ToString() } },
        };

        var freshAsset = BehaviorTreeAssetProjector.Project(
            unmatchedBlob, unmatchedBlob.DebugMetadata, null,
            dto.AssetId, dto.Name, string.Empty, false, string.Empty, string.Empty, string.Empty);

        jsonAsset.StitchKernelIndices(freshAsset);

        // Root matched
        jsonAsset.FindBlobIndex(rootVid).Should().Be(0, "root VisualId matched → index 0");
        // Sequence unmatched → sentinel
        jsonAsset.FindBlobIndex(seqVid).Should().Be(-1,
            "seqVid has no blob entry → sentinel -1");
        // Diagnostic set because of unmatched node
        jsonAsset.LoadState.Should().NotBe(Hrot.Editor.AiShared.Blackboard.BlackboardLoadState.Clean,
            "unmatched node must produce a non-Clean diagnostic");
        jsonAsset.IsDirty.Should().BeFalse("stitch must not MarkDirty even on unmatched nodes");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // PU-302: HSM stitch maps StableId → FlatIndex
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PU302_Stitch_Hsm_CorrectFlatIndex_ByStableId()
    {
        var (dto, stableId) = MakeHsmDto("StitchMachine");
        WriteHsmJson(dto);

        var jsonContrib = new HsmJsonAssetContributor();
        jsonContrib.Refresh(rootDirectory: _tempDir);
        var jsonAsset = (HsmAsset)jsonContrib.Enumerate()[0];

        // Build a minimal blob + metadata that map stableId → FlatIndex 1
        var freshBlob     = new HsmDefinitionBlob();
        var freshMetadata = new MachineMetadata
        {
            StateStableIds      = new Dictionary<ushort, Guid> { { 1, stableId } },
            TransitionVisualIds = new Dictionary<ushort, Guid>(),
        };

        var freshAsset = HsmAssetProjector.Project(
            freshBlob, freshMetadata, null,
            dto.AssetId, dto.Name,
            string.Empty, false, string.Empty);

        jsonAsset.StitchKernelIndices(freshAsset);

        // The Idle state should have FlatIndex=1
        var idleState = jsonAsset.AllStates[0];
        idleState.FlatIndex.Should().Be(1,
            "state StableId must be matched to FlatIndex 1 from fresh MachineMetadata");

        jsonAsset.IsDirty.Should().BeFalse("stitch must not MarkDirty (PU-602)");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // PU-302 kind-guard: Blueprint doc → full-replace path, NOT stitch
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A Blueprint document (AssetKind.Blueprint, IsEditorOwned=true) routed through
    /// ReconcileFromCatalog must use ReconcileAsset (full replace), NOT StitchRuntimeIndices.
    /// Verified by: after reconcile, the document's asset IS the fresh asset (same reference).
    /// </summary>
    [Fact]
    public void PU302_KindGuard_Blueprint_UsesFullReplace_NotStitch()
    {
        // Create a fake Blueprint asset that implements IEditableAsset
        var bpId = Guid.NewGuid();
        var original = new FakeBlueprintAsset(bpId, "BpOrig");
        var fresh    = new FakeBlueprintAsset(bpId, "BpFresh");

        var docManager = MakeDocManager();
        var doc        = docManager.Open(original);

        // Reconcile: fresh catalog contains the blueprint asset
        docManager.ReconcileFromCatalog(new[] { (IEditableAsset)fresh });

        // Blueprint path → ReconcileAsset → full replace: Asset should be fresh
        doc.Asset.Should().BeSameAs(fresh,
            "Blueprint reconcile must use ReconcileAsset (full replace), not stitch");
        doc.Asset.Name.Should().Be("BpFresh",
            "the replaced asset carries the fresh name");
    }

    /// <summary>
    /// A hand-authored (IsEditorOwned=false) BTree asset routed through
    /// ReconcileFromCatalog must use the full-replace path.
    /// </summary>
    [Fact]
    public void PU302_KindGuard_HandAuthored_BTree_UsesFullReplace_NotStitch()
    {
        // Create a hand-authored asset (IsEditorOwned=false)
        var assetId    = Guid.NewGuid();
        var emptyBlob  = new BehaviorTreeBlob
        {
            TreeName = "Test", Nodes = Array.Empty<NodeDefinition>(),
            MethodNames = Array.Empty<string>(), FloatParams = Array.Empty<float>(),
            IntParams = Array.Empty<int>(), SubtreeAssetIds = Array.Empty<string>(),
        };
        var original = new BehaviorTreeAsset(assetId, "HandAuthOrig", string.Empty, false,
            string.Empty, string.Empty, emptyBlob);
        var fresh    = new BehaviorTreeAsset(assetId, "HandAuthFresh", string.Empty, false,
            string.Empty, string.Empty, emptyBlob);

        var docManager = MakeDocManager();
        var doc        = docManager.Open(original);

        docManager.ReconcileFromCatalog(new[] { (IEditableAsset)fresh });

        // Hand-authored → full replace
        doc.Asset.Should().BeSameAs(fresh,
            "hand-authored BTree must use ReconcileAsset (full replace) — not stitch");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // No-dirty: load + full reconcile leave IsDirty==false
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoDirty_LoadAndStitch_LeaveDirtyFalse()
    {
        var (dto, rootVid, seqVid) = MakeBTreeDto("NoDirtyTree");
        WriteBTreeJson(dto);

        var jsonContrib = new BTreeJsonAssetContributor();
        jsonContrib.Refresh(rootDirectory: _tempDir);
        var jsonAsset = (BehaviorTreeAsset)jsonContrib.Enumerate()[0];

        jsonAsset.IsDirty.Should().BeFalse("load must not dirty the asset");

        // Stitch with a matching fresh asset
        var freshBlob  = MakeBlobWithVisualIds(rootVid, seqVid);
        var freshAsset = BehaviorTreeAssetProjector.Project(
            freshBlob, freshBlob.DebugMetadata, null,
            dto.AssetId, dto.Name, string.Empty, false, string.Empty, string.Empty, string.Empty);

        jsonAsset.StitchKernelIndices(freshAsset);
        jsonAsset.IsDirty.Should().BeFalse("stitch must not dirty the asset (PU-602)");
    }

    // ── Fake Blueprint asset ──────────────────────────────────────────────────

    private sealed class FakeBlueprintAsset : IEditableAsset
    {
        public FakeBlueprintAsset(Guid id, string name)
        {
            AssetId = id;
            Name    = name;
        }

        public Guid AssetId { get; }
        public string Name  { get; }
        public AssetKind Kind        => AssetKind.Blueprint;
        public string SourceFilePath => "/fake.bp.json";
        public bool IsDirty          => false;
        public bool IsEditorOwned    => true;

#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }
}
