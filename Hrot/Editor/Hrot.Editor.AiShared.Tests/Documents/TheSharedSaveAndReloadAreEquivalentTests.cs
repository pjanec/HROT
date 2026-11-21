using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.BTree.Editor.Catalog;
using Hrot.BTree.Editor.Model;
using Hrot.BTree.Editor.Persistence;
using Hrot.Editor.AiShared.Documents;
using Hrot.Hsm.Editor.Catalog;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Persistence;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Documents;

/// <summary>
/// ⭐⭐⭐ THE EQUIVALENCE RAIL for phase 2 slice ① — <c>AiAssetSavers</c> + <c>AiAssetReload</c>.
///
/// <para>🔒 <b>Why it exists on the day the shared code was introduced.</b> <c>CE-072</c>'s lesson,
/// verbatim in the resume doc: <i>when a wrapper becomes the only production path to tested code, the
/// existing tests stop covering production</i>. ⇒ ⛔ moving both hosts onto one implementation without
/// asserting the new path emits exactly what the old ones did is how a silent regression ships GREEN.</para>
///
/// <para>⭐⭐ <b>What "equivalence" means here, concretely.</b> Every expectation below is either
/// <b>the pre-change expression re-implemented locally</b> (so the assertion is a byte comparison
/// against the old pipeline, not against the new one restated) or <b>a literal string copied out of the
/// pre-change hosts</b>. ⚠ A test that called the shared helper twice and compared the results would
/// be vacuous — that is the trap this file is written to avoid.</para>
///
/// <para>⭐ Real assets, not hand-made stubs: <c>SampleScout</c> (BTree) and <c>SampleGuard</c> (HSM)
/// come out of <c>Hrot.AI.Behaviors</c> through the same contributors the hosts use.</para>
///
/// <para>📄 Design: <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.6 (item ③).</para>
/// </summary>
public sealed class TheSharedSaveAndReloadAreEquivalentTests
{
    private static readonly System.Reflection.Assembly BehaviorsAssembly =
        typeof(Hrot.AI.Behaviors.Trees.SampleScout).Assembly;

    private static BehaviorTreeAssetDto BTreeDto(string name = "SampleScout")
    {
        var contributor = new BTreeAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var asset = contributor.Enumerate().FirstOrDefault(a => a.Name == name);
        Assert.NotNull(asset);   // anti-vacuity: no fixture ⇒ every assertion below is empty
        return BehaviorTreeAssetMapper.ToDto((BehaviorTreeAsset)asset!);
    }

    private static HsmAssetDto HsmDto(string name = "SampleGuard")
    {
        var contributor = new HsmAssetContributor();
        contributor.LoadFrom(BehaviorsAssembly);
        var asset = contributor.Enumerate().FirstOrDefault(a => a.Name == name);
        Assert.NotNull(asset);
        return HsmAssetMapper.ToDto((HsmAsset)asset!);
    }

    // ── ① the BYTES on disk ────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ The saved file is byte-identical to what the pre-change hosts wrote.
    /// ⚠ The expectation is the OLD expression, spelled out here: serialize → flatten → write.
    /// ⛔ If the flatten pass is ever dropped from the shared saver, this reddens.
    /// </summary>
    [Fact]
    public void The_saved_BTree_bytes_are_identical_to_the_pre_change_writer()
    {
        var dto = BTreeDto();

        // the PRE-CHANGE pipeline, re-implemented verbatim from EditorSubsystem/CgfSubsystem
        var expected = Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(
            BTreeJsonServices.Serialize(dto));

        var path = Path.Combine(Path.GetTempPath(), $"btree-equiv-{Guid.NewGuid():N}.json");
        try
        {
            AiAssetSavers.SaveBTree(dto, path);
            Assert.True(File.Exists(path), "the shared saver wrote nothing at all");
            var actual = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(actual), "the shared saver wrote an empty file");
            Assert.Equal(expected, actual);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>The HSM half of the same claim.</summary>
    [Fact]
    public void The_saved_Hsm_bytes_are_identical_to_the_pre_change_writer()
    {
        var dto = HsmDto();

        var expected = Fdp.Toolkit.Serialization.JsonAestheticFormatter.FlattenNumericArrays(
            HsmJsonServices.Serialize(dto));

        var path = Path.Combine(Path.GetTempPath(), $"hsm-equiv-{Guid.NewGuid():N}.json");
        try
        {
            AiAssetSavers.SaveHsm(dto, path);
            Assert.True(File.Exists(path), "the shared saver wrote nothing at all");
            var actual = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(actual), "the shared saver wrote an empty file");
            Assert.Equal(expected, actual);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ── ② the SOURCES handed to the compiler ───────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ The shared reload emits exactly the two sources, with exactly the two file names, that
    /// both hosts emitted before — and their CONTENT is byte-identical to the old emit calls.
    /// ⚠ The file names are load-bearing: the bridge source is what self-registers the tree.
    /// </summary>
    [Fact]
    public void The_BTree_reload_hands_the_compiler_what_the_pre_change_hosts_handed_it()
    {
        var dto = BTreeDto();

        // the PRE-CHANGE emit, re-implemented verbatim
        var expectedTopology = BTreeEmitCore.EmitTopologyCore(dto);
        var expectedBridge   = BTreeBridgeEmitCore.EmitBridge(dto);

        IReadOnlyList<(string Source, string FileName)>? captured = null;
        var status = AiAssetReload.ReloadBTree(dto, (sources, _) =>
        {
            captured = sources;
            return new AiAssetReload.CompileOutcome(true, null, 42);
        });

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Count);
        Assert.Equal(dto.Name + ".g.cs",           captured[0].FileName);
        Assert.Equal(dto.Name + ".Registrar.g.cs", captured[1].FileName);
        Assert.Equal(expectedTopology, captured[0].Source);
        Assert.Equal(expectedBridge,   captured[1].Source);
        Assert.False(string.IsNullOrWhiteSpace(captured[0].Source), "topology source is empty");
        Assert.False(string.IsNullOrWhiteSpace(captured[1].Source), "bridge source is empty");

        // and the wording the hosts produced for a success
        Assert.Equal($"Compiled BTree '{dto.Name}' in 42ms", status);
    }

    /// <summary>The HSM half of the same claim.</summary>
    [Fact]
    public void The_Hsm_reload_hands_the_compiler_what_the_pre_change_hosts_handed_it()
    {
        var dto = HsmDto();

        var expectedTopology = HsmEmitCore.EmitTopologyCore(dto);
        var expectedBridge   = HsmBridgeEmitCore.EmitBridge(dto);

        IReadOnlyList<(string Source, string FileName)>? captured = null;
        var status = AiAssetReload.ReloadHsm(dto, (sources, _) =>
        {
            captured = sources;
            return new AiAssetReload.CompileOutcome(true, null, 7);
        });

        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Count);
        Assert.Equal(dto.Name + ".g.cs",           captured[0].FileName);
        Assert.Equal(dto.Name + ".Registrar.g.cs", captured[1].FileName);
        Assert.Equal(expectedTopology, captured[0].Source);
        Assert.Equal(expectedBridge,   captured[1].Source);

        Assert.Equal($"Compiled HSM '{dto.Name}' in 7ms", status);
    }

    /// <summary>
    /// ⚠ The patch assembly name must stay UNIQUE per reload — two patches sharing a name cannot both
    /// be loaded. ⭐ The prefix shape is asserted too, because it is what the pre-change hosts emitted
    /// (<c>BTreePatch_{assetId:N}_{guid:N}</c>) and a log reader greps for it.
    /// </summary>
    [Fact]
    public void The_patch_assembly_name_keeps_its_shape_and_is_unique_per_reload()
    {
        var dto = BTreeDto();
        var names = new List<string>();

        for (var i = 0; i < 3; i++)
            AiAssetReload.ReloadBTree(dto, (_, asmName) =>
            {
                names.Add(asmName);
                return new AiAssetReload.CompileOutcome(true, null, 0);
            });

        Assert.All(names, n => Assert.StartsWith($"BTreePatch_{dto.AssetId:N}_", n));
        Assert.Equal(3, names.Distinct().Count());
    }

    // ── ③ the WORDINGS ────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ Every status string, against the literal the pre-change hosts produced.
    /// 📐 Copied out of <c>EditorSubsystem</c> / <c>CgfSubsystem</c> before the change; ⛔ if the shared
    /// formatter ever rewords one, a user-visible string changed and this says so.
    /// </summary>
    [Fact]
    public void Every_status_wording_matches_the_pre_change_hosts()
    {
        Assert.Equal("No active AI document to reload.", AiAssetReload.NoActiveDocument);

        Assert.Equal("'Foo' (BTree) has no compilable canvas context.",
            AiAssetReload.NoCompilableContext("Foo", AssetKind.BTree));

        Assert.Equal("Compiled blueprint 'Bar' in 12ms",
            AiAssetReload.FormatBlueprint("Bar", new AiAssetReload.CompileOutcome(true, null, 12)));

        Assert.Equal("Blueprint compile failed: boom",
            AiAssetReload.FormatBlueprint("Bar", new AiAssetReload.CompileOutcome(false, "boom", 0)));

        var btree = BTreeDto();
        Assert.Equal("BTree compile failed: nope",
            AiAssetReload.ReloadBTree(btree,
                (_, __) => new AiAssetReload.CompileOutcome(false, "nope", 0)));

        var hsm = HsmDto();
        Assert.Equal("HSM compile failed: nope",
            AiAssetReload.ReloadHsm(hsm,
                (_, __) => new AiAssetReload.CompileOutcome(false, "nope", 0)));
    }

    // ── ④ the POLICY — what the editor did NOT have before this slice ─────────────

    private sealed class FakeAsset : IEditableAsset
    {
        public FakeAsset(AssetKind kind, string name)
        {
            Kind = kind; Name = name; AssetId = Guid.NewGuid(); SourceFilePath = "";
        }
        public Guid      AssetId        { get; }
        public string    Name           { get; }
        public AssetKind Kind           { get; }
        public string    SourceFilePath { get; }
        public bool      IsDirty        => false;
        public bool      IsEditorOwned  => true;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    private static AiDocumentManager ManagerWith(AssetKind kind, string name)
    {
        var mgr = new AiDocumentManager(perspectiveSwitchCallback: _ => { });
        mgr.Open(new FakeAsset(kind, name));
        return mgr;
    }

    [Fact]
    public void With_no_active_document_the_policy_reports_and_still_logs()
    {
        var logged = new List<(string Name, string Status)>();
        var status = AiAssetReload.Reload(null, new AiReloadArms(), (n, s) => logged.Add((n, s)));

        Assert.Equal(AiAssetReload.NoActiveDocument, status);
        // ⭐⭐ ruling 53: the origin-side log is the whole safety net, so it fires even here —
        //    an operator who triggered a reload over MCP must see that it was attempted.
        Assert.Single(logged);
        Assert.Equal("(none)", logged[0].Name);
        Assert.Equal(AiAssetReload.NoActiveDocument, logged[0].Status);
    }

    /// <summary>
    /// ⭐⭐ An arm that cannot resolve its model gets the ONE shared wording — which is how CGF's old
    /// runtime-type dispatch and the editor's kind dispatch stay byte-identical (design §5c.6 E4).
    /// </summary>
    [Fact]
    public void An_arm_that_finds_no_model_gets_the_one_shared_wording()
    {
        var mgr = ManagerWith(AssetKind.BTree, "Tree1");
        var status = AiAssetReload.Reload(mgr, new AiReloadArms(BTree: () => null));

        Assert.Equal(AiAssetReload.NoCompilableContext("Tree1", AssetKind.BTree), status);
    }

    /// <summary>
    /// ⭐⭐ A kind this host cannot compile at all (a null ARM) lands on the same wording — ⛔ NOT on a
    /// silent no-op, which is what the editor's toolbar switch did before this slice.
    /// </summary>
    [Fact]
    public void A_kind_the_host_cannot_compile_is_reported_not_silently_ignored()
    {
        var mgr = ManagerWith(AssetKind.Scenario, "Scen1");
        var status = AiAssetReload.Reload(mgr, new AiReloadArms(BTree: () => "should not run"));

        Assert.Equal(AiAssetReload.NoCompilableContext("Scen1", AssetKind.Scenario), status);
    }

    /// <summary>⛔ A compile is user input; it must not take the node down.</summary>
    [Fact]
    public void A_throwing_arm_is_reported_not_propagated()
    {
        var mgr = ManagerWith(AssetKind.Hsm, "Machine1");
        var logged = new List<(string Name, string Status)>();

        var status = AiAssetReload.Reload(
            mgr,
            new AiReloadArms(Hsm: () => throw new InvalidOperationException("kaboom")),
            (n, s) => logged.Add((n, s)));

        Assert.Equal("Reload threw: kaboom", status);
        Assert.Single(logged);                        // ⭐ ruling 53 holds on the throw path too
        Assert.Equal("Machine1", logged[0].Name);
    }

    /// <summary>⭐ Dispatch correctness: only the matching kind's arm runs.</summary>
    [Fact]
    public void Only_the_matching_kind_arm_is_invoked()
    {
        var mgr  = ManagerWith(AssetKind.Hsm, "Machine1");
        var ran  = new List<string>();

        var status = AiAssetReload.Reload(mgr, new AiReloadArms(
            Blueprint: () => { ran.Add("bp");    return "bp";    },
            BTree:     () => { ran.Add("btree"); return "btree"; },
            Hsm:       () => { ran.Add("hsm");   return "hsm";   }));

        Assert.Equal(new[] { "hsm" }, ran);
        Assert.Equal("hsm", status);
    }

    /// <summary>
    /// ⭐⭐ The log fires on the SUCCESS path too — ⛔ not only on failures. 📄 ruling 53 /
    /// <c>DESIGN_Cgf_Editor_Sharing_Slice3_Editing_HotReload.md</c> §10.4: the log records the ACT.
    /// </summary>
    [Fact]
    public void The_log_records_every_reload_including_the_successful_ones()
    {
        var mgr = ManagerWith(AssetKind.BTree, "Tree1");
        var logged = new List<(string Name, string Status)>();

        AiAssetReload.Reload(mgr, new AiReloadArms(BTree: () => "Compiled BTree 'Tree1' in 3ms"),
            (n, s) => logged.Add((n, s)));

        Assert.Single(logged);
        Assert.Equal("Tree1", logged[0].Name);
        Assert.Equal("Compiled BTree 'Tree1' in 3ms", logged[0].Status);
    }
}
