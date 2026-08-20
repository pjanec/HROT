using Fdp.Core;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Editor.Variables;
using Hrot.Editor;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Variables;
using BlueprintDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;
using BlueprintTypeRef      = Hrot.Blueprints.Core.Assets.BlueprintTypeRef;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b>Batch 95 (<c>95b</c>) — the selected entity reaches every perspective, and a VALUE ARRIVES.</b>
///
/// <para>🔴🔴 <b>The defect, measured.</b> The editor holds <b>four</b>
/// <see cref="EditorSelectionStore"/>s — one per perspective plus the bridge's own — and
/// <c>CallbackSelectionBridge.Connect</c> is called <b>exactly once</b>, on the fourth
/// (<c>EditorSubsystem:1351</c>). ⇒ <c>SelectedEntity</c> was <c>null</c> on all three perspective
/// stores, always ⇒ every live-value provider returned <c>null</c> on its <b>second line</b>
/// (<c>var entity = _store.SelectedEntity; if (entity is null) return null;</c>) ⇒ ⛔ <b>every
/// Details/Watch row on every host rendered <c>(pending)</c> for ever.</b></para>
///
/// <para>⚠ <b>And it looked wired</b>, which is why four batches of fixes passed over it:
/// <c>ActiveAsset</c> IS set on all three stores, and the composition root's own comment claimed
/// <i>"Both selection stores share the same entity selection (global)."</i> 🔴 That sentence was
/// false.</para>
///
/// <para>⭐⭐⭐ <b>The design basis, cited</b> — 📄 <c>AI_Editor_Shared_Infrastructure.md:450</c>:
/// <i>"SelectedEntity stays global because entities exist independently of which asset is being
/// edited — the same entity is selectable while looking at any of its associated assets."</i> and
/// <c>:45</c>: the store is <i>"the single selection bus all three editors subscribe to"</i>. ⇒ the
/// entity was never meant to be per-perspective; the split arrived later for <c>ActiveAsset</c> and
/// took the entity with it. ⛔ So the fix is ONE shared fact — <b>not</b> three more <c>Connect</c>
/// calls, which is the shape <c>PerspectiveWorkspaceServices</c> exists to abolish.</para>
///
/// <para>⭐⭐ <b>Two rails, and they cover different layers on purpose</b> *(handoff §6: say which
/// layer is faked)*:
/// <list type="number">
///   <item>⭐ <b>The composition-root rail</b> — the REAL <see cref="EditorSubsystem"/>: select an
///   entity the way production selects it *(on the store the bridge writes)* and assert all three
///   perspectives see it. ⛔ <b>Faked here: nothing.</b> ⚠ <b>Not reachable here:</b> an actual
///   VALUE — every live provider needs a debug session or a repo adapter, i.e. a running sim.</item>
///   <item>⭐⭐⭐ <b>The value rail</b> — a real <c>BlueprintLiveValueProvider</c>, a real
///   <see cref="SectionVariableRowSource"/>, a real <see cref="VariableTableModel"/> and a real
///   <see cref="VariableValueFormatter"/>, joined by the same <see cref="SharedEntitySelection"/>.
///   ⛔ <b>Faked here: the RUN</b> — the state reader is a stub, because the alternative is stubbing
///   a 36-member debug session. ⭐ That is the one layer the value rail cannot see, and rail 1 is
///   what covers it.</item>
/// </list></para>
/// </summary>
public sealed class TheSelectedEntityReachesEveryPerspectiveTests
{
    private static readonly Entity Selected = new(7, 1);

    // ══ 1 — the composition-root rail ════════════════════════════════════════

    private static EditorSubsystem RealEditor()
    {
        var editor = new EditorSubsystem();
        editor.RegisterWindows(new WindowManager(new IconAtlas(IntPtr.Zero, 16f, 16f)));
        return editor;
    }

    /// <summary>
    /// ⭐⭐⭐ <b>RED before this batch, on all three.</b> Selecting an entity is
    /// <c>store.SelectedEntity = entity</c> on the bridge's store — that is the bridge's ENTIRE
    /// action — and every perspective must then be looking at the same entity.
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    [InlineData("blueprint")]
    public void SelectingAnEntityReachesEveryPerspectiveStore(string perspective)
    {
        var editor = RealEditor();

        editor.AiEditorSelectionStore.SelectedEntity = Selected;

        var reg = editor.RegistrarFor(perspective);
        Assert.NotNull(reg);
        Assert.Equal(Selected, reg!.SelectionStore.SelectedEntity);
    }

    /// <summary>
    /// ⭐⭐ <b>And each perspective REPAINTS.</b> ⛔ Sharing the cell without re-raising
    /// <c>OnSelectionChanged</c> would leave every panel showing its last frame until something else
    /// happened to change — 📌 the same class of half-fix this batch exists to stop shipping.
    /// </summary>
    [Theory]
    [InlineData("btree")]
    [InlineData("hsm")]
    [InlineData("blueprint")]
    public void SelectingAnEntityNotifiesEveryPerspectiveStore(string perspective)
    {
        var editor = RealEditor();
        var reg    = editor.RegistrarFor(perspective)!;

        int notifications = 0;
        reg.SelectionStore.OnSelectionChanged += () => notifications++;

        editor.AiEditorSelectionStore.SelectedEntity = Selected;

        Assert.True(notifications > 0,
            $"The '{perspective}' perspective never heard about the entity selection, so its panels " +
            "would keep drawing their last frame.");
    }

    /// <summary>
    /// ⭐ <b>Deselection travels too</b> — ⛔ otherwise a panel would keep reading a dead entity's
    /// values after the designer cleared the selection, which is worse than <c>(pending)</c>.
    /// </summary>
    [Fact]
    public void DeselectingTravelsAsWell()
    {
        var editor = RealEditor();
        editor.AiEditorSelectionStore.SelectedEntity = Selected;
        editor.AiEditorSelectionStore.SelectedEntity = null;

        foreach (var p in new[] { "btree", "hsm", "blueprint" })
            Assert.Null(editor.RegistrarFor(p)!.SelectionStore.SelectedEntity);
    }

    // ══ 2 — the value rail ═══════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE rail this batch is for: a Details cell renders the RUN'S VALUE, not
    /// <c>(pending)</c>.</b>
    ///
    /// <para>📌 <c>M-22</c>'s correction: <i>"'is it connected?' is not 'does anything flow?'"</i> —
    /// so this drives a value through the whole chain: <b>bridge store → shared cell → perspective
    /// store → <c>BlueprintLiveValueProvider</c>'s entity gate → the snapshot → the row's object arm →
    /// the sampler → <c>VariableValueFormatter</c></b>.</para>
    ///
    /// <para>⚠ <b>Batch 94's <c>TheWatchGoesLive</c> stops one step short and says so</b> — <i>"it does
    /// not, and cannot, prove the production HOST supplies a provider."</i> ⭐ This closes that step
    /// from the store end: the entity comes from the bridge's store, exactly as production's does.</para>
    /// </summary>
    [Fact]
    public void ADetailsCellRendersTheRunsValueOnceAnEntityIsSelected()
    {
        // ⭐ ONE cell, exactly as the composition root builds it.
        var shared      = new SharedEntitySelection();
        var bridgeStore = new EditorSelectionStore(shared);   // the one Connect() writes
        var perspective = new EditorSelectionStore(shared);   // the one the provider reads

        var asset    = BlueprintAssetWithHealth();
        // ⭐ The adapter production uses — BlueprintAsset is deliberately not an IEditableAsset.
        var editable = new BlueprintEditableAssetAdapter(asset);

        // ⭐ The REAL provider. ⛔ Only the state READER is a stub — see the class remarks.
        var provider = new BlueprintLiveValueProvider(
            readerFactory: () => (self, assetId) => new BlueprintStateSnapshot(
                Self:        self,
                AssetId:     assetId,
                AssetName:   asset.Name,
                Dispatch:    Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
                FieldValues: new Dictionary<string, object> { ["Health"] = 42 },
                Cursor:      null),
            store: perspective);

        var source = new SectionVariableRowSource(
            assetId:     asset.AssetId,
            assetName:   asset.Name,
            entity:      default,
            section:     "s",
            schema:      new BlueprintVariableSchemaSource(asset, VariableKind.Variable, () => { }),
            liveObjects: () => provider.GetLiveObjects(editable));

        var model = new VariableTableModel(source, VariableTableColumns.Details)
        { RunState = VariableRunState.Paused };
        var formatter = new VariableValueFormatter(RawValueDecoder.Instance);

        // ── nothing selected: the honest answer ──────────────────────────────
        var before = model.Build();
        Assert.Contains("pending", formatter.Cell(before.AllRows.Single(), before.ValueMode),
            StringComparison.OrdinalIgnoreCase);

        // ── the designer picks an entity in the world ────────────────────────
        bridgeStore.SelectedEntity = Selected;

        // ⚠⚠ AND THE SIM STEPS. ⛔ Not decoration — 📌 Batch 94's VariableRowSampler samples ONCE PER
        //    BRAIN FRAME and draws from cache in between (R-103, the user's own specification), so a
        //    selection made between two pulses is not visible until the next one.
        // 🔴 That is a REAL residual gap while the debugger holds time: under a breakpoint the pulse
        //    does not advance, so selecting a different entity shows the previous sample until the
        //    run continues. ⭐ Asserted as a finding by TheSelectionIsNotVisibleUntilTheNextPulse
        //    below rather than silently papered over here. ⛔ It is NOT what 95b is: without the
        //    shared cell, no number of pulses would ever produce a value.
        BehaviorFrame.Advance();

        var after = model.Build();
        var cell  = formatter.Cell(after.AllRows.Single(), after.ValueMode);

        Assert.DoesNotContain("pending", cell, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("42", cell);
    }

    /// <summary>
    /// ⚠⚠ <b>THE RESIDUAL GAP, asserted on purpose</b> — a selection made while the debugger holds
    /// time is not visible until the next brain frame.
    ///
    /// <para>📌 <c>R-103</c> (the user's specification) is <i>"the accessor is called once per brain
    /// frame and the value is cached"</i>, and 📌 it also says <i>"pin-while-paused samples
    /// immediately"</i> — ⭐ <b>the same courtesy is not extended to a SELECTION change</b>, because
    /// the sampler has no notion of one. ⇒ under a breakpoint pause, selecting a different entity
    /// shows the previous sample until the run continues.</para>
    ///
    /// <para>⛔ <b>Deliberately NOT fixed in this batch</b>, and this rail is why the claim is honest:
    /// 95b's scope is that a value can arrive at all. ⚠ If someone teaches the sampler about
    /// selection later, this test goes RED and that is the correct signal — flip it, do not delete
    /// it.</para>
    /// </summary>
    [Fact]
    public void TheSelectionIsNotVisibleUntilTheNextPulse()
    {
        var shared      = new SharedEntitySelection();
        var bridgeStore = new EditorSelectionStore(shared);
        var perspective = new EditorSelectionStore(shared);

        var asset    = BlueprintAssetWithHealth();
        var editable = new BlueprintEditableAssetAdapter(asset);

        var provider = new BlueprintLiveValueProvider(
            readerFactory: () => (self, assetId) => new BlueprintStateSnapshot(
                Self:        self,
                AssetId:     assetId,
                AssetName:   asset.Name,
                Dispatch:    Fdp.Toolkit.Blueprints.BlueprintDispatchKind.Instance,
                FieldValues: new Dictionary<string, object> { ["Health"] = 42 },
                Cursor:      null),
            store: perspective);

        var model = new VariableTableModel(
            new SectionVariableRowSource(
                assetId:     asset.AssetId,
                assetName:   asset.Name,
                entity:      default,
                section:     "s",
                schema:      new BlueprintVariableSchemaSource(asset, VariableKind.Variable, () => { }),
                liveObjects: () => provider.GetLiveObjects(editable)),
            VariableTableColumns.Details)
        { RunState = VariableRunState.Paused };
        var formatter = new VariableValueFormatter(RawValueDecoder.Instance);

        model.Build();                              // takes this pulse's sample: nothing selected
        bridgeStore.SelectedEntity = Selected;      // ...and the pulse does not move

        var same = model.Build();
        Assert.Contains("pending", formatter.Cell(same.AllRows.Single(), same.ValueMode),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⭐⭐ <b>A store built with no shared cell keeps its own</b> — ⛔ the optionality is what leaves
    /// every standalone and test construction unchanged, and it must not accidentally make two
    /// unrelated stores share a global.
    /// </summary>
    [Fact]
    public void TwoUnrelatedStoresDoNotShareAnEntity()
    {
        var a = new EditorSelectionStore();
        var b = new EditorSelectionStore();

        a.SelectedEntity = Selected;

        Assert.Null(b.SelectedEntity);
    }

    // ── fixture ──────────────────────────────────────────────────────────────

    private static BlueprintAsset BlueprintAssetWithHealth()
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "FeedHost",
            Dispatch = BlueprintDispatchKind.Instance,
            Header   = new Header(),
        };
        asset.Variables.Add(new VariableDecl
        {
            Id   = Guid.NewGuid(),
            Name = "Health",
            Type = new BlueprintTypeRef { TypeId = "System.Int32" },
        });
        return asset;
    }
}
