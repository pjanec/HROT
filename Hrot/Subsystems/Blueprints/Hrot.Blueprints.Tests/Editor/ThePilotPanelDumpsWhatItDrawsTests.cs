using System;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Components;
using Hrot.Blueprints.Editor.EntityBlueprints;
using Hrot.Blueprints.Editor.Runtime;
using Hrot.Common.Serializers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b><c>U-obs-1</c> / <c>U1c</c> — THE PILOT, end to end.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example *(this panel is the design's own worked
/// example)* · §Invariant · §"Perf &amp; correctness".
///
/// <para>⭐⭐ <b>Why these rails run HEADLESS, with no ImGui context at all — and why that is the point,
/// not a shortcut.</b> 📄 The umbrella *(<c>DESIGN_Headless_Testability.md</c>)* wants the UI checkable
/// <b>without a display</b>. ⇒ ⛔ if the capture sat after the render guard, a headless run would observe
/// NOTHING and this whole programme would only work where a GPU already does. ⭐ So <c>DrawUI</c> builds
/// and publishes <b>before</b> that guard, and these rails prove it by never opening a frame.</para>
///
/// <para>⚠ <b>ONE class</b>: <c>PanelSnapshot</c> is process-global static state and xunit parallelises
/// across CLASSES. Every case opens by resetting it.</para>
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ThePilotPanelDumpsWhatItDrawsTests : IDisposable
{
    private readonly EntityRepository _repo;
    private readonly BlueprintRegistry _registry;
    private readonly Guid _assetA = new("00000000-0000-0000-0000-0000000000a1");
    private readonly Guid _assetB = new("00000000-0000-0000-0000-0000000000b2");

    public ThePilotPanelDumpsWhatItDrawsTests()
    {
        _repo = new EntityRepository();
        BlueprintRuntimeWiring.RegisterTierComponents(_repo);
        _repo.RegisterManagedComponent<InitialBlueprintsIntent>();
        _registry = new BlueprintRegistry();

        Register("PatrolBlueprint", _assetA);
        Register("GuardBlueprint",  _assetB);

        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
        _repo.Dispose();
    }

    private void Register(string name, Guid assetId)
        => _registry.RegisterInstance(BlueprintIdHash.Compute(assetId), new BlueprintDefinition
        {
            Name = name, Kind = BlueprintDispatchKind.Instance,
            StructureHash = (ulong)BlueprintIdHash.Compute(assetId), StateSize = 64,
            AssetId = assetId, InitDefault = span => span.Clear(),
        });

    private Entity CreateEntity()
    {
        var e = _repo.CreateEntity();
        _repo.AddComponent(e, default(BlueprintBlackboard1024));
        return e;
    }

    private EntityBlueprintsPanel PanelOn(Entity entity, out EntityBlueprintsEditModel model)
    {
        model = new EntityBlueprintsEditModel(_repo, _registry, entity);
        return new EntityBlueprintsPanel(model, _repo, _registry);
    }

    // ── U1b, on the PRODUCTION object ──────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>The panel is instrumented the moment it is CONSTRUCTED — before it has ever drawn.</b>
    /// ⛔ This is the rail that would go red if <c>DeclareInstrumented</c> drifted into the draw: a panel
    /// whose window nobody opened would then look exactly like a panel nobody converted, and the reader
    /// could not tell <i>"showed nothing"</i> from <i>"not instrumented"</i>.
    /// 📌 Asserted on the CONSTRUCTED object, not on the source — <c>R-67</c>.
    /// </summary>
    [Fact]
    public void ThePanelIsInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain(PanelIds.EntityBlueprints, PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        _ = PanelOn(CreateEntity(), out _);

        Assert.Contains(PanelIds.EntityBlueprints, PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain(PanelIds.EntityBlueprints, PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet(PanelIds.EntityBlueprints));
    }

    // ── U1c — the dump carries what the designer sees ──────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b>Draw a frame, read the model over the snapshot, assert FIELDS.</b> 📄 §Example's payload:
    /// <c>panelId</c>, <c>simState</c>, <c>tier</c>, <c>rows[].name</c>, <c>rows[].state</c>.
    /// </summary>
    [Fact]
    public void AfterAFrame_TheDumpCarriesTheStateTheDesignerWouldSee()
    {
        PanelSnapshot.CaptureEnabled = true;
        var panel = PanelOn(CreateEntity(), out var model);
        panel.IsRunning = true;

        model.StageAdd(_assetA);

        panel.DrawUI();                                    // ⭐ no ImGui context — headless on purpose

        var dump = PanelSnapshot.DumpAll()[PanelIds.EntityBlueprints]!;

        Assert.Equal(PanelIds.EntityBlueprints, dump["panelId"]!.GetValue<string>());
        Assert.Equal("Entity Blueprints",       dump["title"]!.GetValue<string>());
        Assert.True(dump["hasEntity"]!.GetValue<bool>());
        Assert.Equal("Running",                 dump["simState"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(dump["tier"]!.GetValue<string>()));

        var rows = dump["rows"]!.AsArray();
        Assert.Single(rows);
        Assert.Equal("PatrolBlueprint", rows[0]!["name"]!.GetValue<string>());
        Assert.Equal("Add pending",     rows[0]!["status"]!.GetValue<string>());
        Assert.Equal("Cancel",          rows[0]!["actionLabel"]!.GetValue<string>());
    }

    /// <summary>
    /// ⭐⭐ <b>The footer's enablement is a VALUE now, and that is the payoff.</b> ⛔ Before the conversion
    /// <c>canApply</c> was a local computed inside the draw between two <c>BeginDisabled</c> calls — a rule
    /// no test could reach without pixels. ⭐ Here it is asserted directly, in both directions.
    /// </summary>
    [Fact]
    public void TheApplyAndRevertEnablement_IsReadableFromTheModel()
    {
        PanelSnapshot.CaptureEnabled = true;
        var panel = PanelOn(CreateEntity(), out var model);

        panel.DrawUI();
        var clean = PanelSnapshot.DumpAll()[PanelIds.EntityBlueprints]!;
        Assert.False(clean["canApply"]!.GetValue<bool>());
        Assert.False(clean["canRevert"]!.GetValue<bool>());

        model.StageAdd(_assetB);
        panel.DrawUI();
        var staged = PanelSnapshot.DumpAll()[PanelIds.EntityBlueprints]!;
        Assert.True(staged["canApply"]!.GetValue<bool>());
        Assert.True(staged["canRevert"]!.GetValue<bool>());
    }

    /// <summary>
    /// ⭐⭐ <b>The add-popup's offer set is observable, including WHY an entry cannot be picked.</b>
    /// ⛔ Previously the <c>(staged)</c>/<c>(attached)</c> disabling lived entirely inside a popup body that
    /// only opens on a click — 📌 unreachable to any headless check.
    /// </summary>
    [Fact]
    public void AStagedBlueprint_IsOfferedAsStagedRatherThanSelectable()
    {
        PanelSnapshot.CaptureEnabled = true;
        var panel = PanelOn(CreateEntity(), out var model);

        model.StageAdd(_assetA);
        panel.DrawUI();

        var options = PanelSnapshot.DumpAll()[PanelIds.EntityBlueprints]!["addOptions"]!.AsArray();
        Assert.Equal(2, options.Count);

        var patrol = Assert.Single(options, o => o!["label"]!.GetValue<string>().StartsWith("PatrolBlueprint", StringComparison.Ordinal));
        Assert.Equal("staged", patrol!["state"]!.GetValue<string>());
        Assert.Equal("PatrolBlueprint (staged)", patrol["label"]!.GetValue<string>());

        var guard = Assert.Single(options, o => o!["label"]!.GetValue<string>() == "GuardBlueprint");
        Assert.Equal("selectable", guard!["state"]!.GetValue<string>());
    }

    /// <summary>
    /// ⭐⭐ <b>The empty state is a MODEL, not an absence</b> — 📌 the same distinction <c>R-117</c> makes on
    /// screen, one layer down. ⛔ A panel that dumped nothing when it had nothing to show would be
    /// indistinguishable from one that is not instrumented.
    /// </summary>
    [Fact]
    public void WithNoEntity_ThePanelStillDumpsAModelThatSaysSo()
    {
        PanelSnapshot.CaptureEnabled = true;
        var panel = PanelOn(default, out _);

        panel.DrawUI();

        var dump = PanelSnapshot.DumpAll()[PanelIds.EntityBlueprints]!;
        Assert.False(dump["hasEntity"]!.GetValue<bool>());
        Assert.Contains("No entity selected", dump["emptyMessage"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Empty(dump["rows"]!.AsArray());
    }

    // ── The flag gates the DUMP, not the BUILD ─────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Production default: capture OFF ⇒ nothing is published, ⛔ but the panel is still known to be
    /// instrumented, and the model is still BUILT *(the draw needs it)*.
    /// </summary>
    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var panel = PanelOn(CreateEntity(), out _);        // CaptureEnabled stays false

        panel.DrawUI();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains(PanelIds.EntityBlueprints, PanelSnapshot.RegisteredPanels);
        Assert.NotNull(panel.BuildViewModel());            // ⭐ the BUILD is unaffected by the flag
    }
}
