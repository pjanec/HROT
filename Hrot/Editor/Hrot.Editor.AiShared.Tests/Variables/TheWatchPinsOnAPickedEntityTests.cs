using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b><c>AQ55</c> — "watch this variable on entity…", the picked-entity pin.</b>
/// 📄 <c>Architect_Question_55_Watch_Concrete_Entity_Picker.md</c> *(<c>Q55-A</c> REUSE ·
/// <c>Q55-C</c> the action · <c>Q55-E</c> no filter)* · <c>DESIGN_Variable_Watch_Pinning.md</c> §3.
///
/// <para>⭐ The picker itself is faked, exactly as the handoff's rail asks: <i>"fake the pick service to
/// return a fixed id"</i>. ⛔ The map-pick mode is the host's, and no headless rail can click a map.</para>
/// </summary>
public sealed class TheWatchPinsOnAPickedEntityTests
{
    private static readonly Guid AssetA = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
    private const long PickedNetworkId = 4242;

    private static Entity Ent(int index) => new(index, 1);

    private static VariableRow Row(Entity entity, string name = "Health")
        => new(Origin:    new VariableRowOrigin(AssetA, entity, "s", name, "Alpha"),
               ShortName: name,
               TypeText:  "int",
               ClrType:   typeof(int),
               ReadValue: () => BitConverter.GetBytes(1));

    /// <summary>⭐ The manager needs a time controller; nothing here pauses.</summary>
    private sealed class NoTimeControl : Hrot.Blueprints.Core.Debug.IEngineDebugTimeController
    {
        public bool IsPausedByDebugger => false;
        public void RequestPause() { }
        public void RequestResume() { }
        public void RequestStepOneTick() { }
    }

    /// <summary>⭐⭐ The REAL manager, so the Watch is built the way production builds it — 📌 <c>R-67</c>:
    /// a stub is one more thing that can be right while production is wrong.</summary>
    private static Hrot.Diagnostics.Breakpoints.DataBreakpointManager RealManager()
    {
        var live    = new EntityRepository();
        var preTick = new EntityRepository();
        return new Hrot.Diagnostics.Breakpoints.DataBreakpointManager(
            live, preTick, new Hrot.Diagnostics.Breakpoints.DebugSnapshotProvider(preTick),
            new NoTimeControl());
    }

    private static PerspectiveWorkspaceServices Services(WatchEntityPicker? picker)
        => new(new AssetCatalog(),
               new Windows.TheDefaultLayoutIsNotStaleTests.NoRefactor(),
               new DebugSessionRegistry(),
               new StructEdit.Reflection.ComponentEditServiceBuilder().Build(),
               isSimUp: () => false, isFrozen: () => false)
        {
            BreakpointManager = RealManager(),
            EntityPicker      = picker,
        };

    private static PerspectiveWorkspaceRegistrar Registrar(WatchEntityPicker? picker)
        => Services(picker).CreateRegistrar("BTree", new EditorSelectionStore(),
                                            Array.Empty<IAssetValidator>());

    /// <summary>⭐ The Watch as the composition root builds it. ⛔ Not a hand-made window.</summary>
    private static AiWatchWindow Window(WatchEntityPicker? picker = null)
        => Registrar(picker).Watch!;

    private static WatchEntityPicker Picks(Entity entity, long networkId = PickedNetworkId)
        => _ => Task.FromResult<EntityBinding?>(EntityBinding.Concrete(networkId, entity));

    // ══ the gesture itself ══════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL:</b> the pick resolves a <c>NetworkId</c> ⇒ a <b>CONCRETE</b> pin exists carrying
    /// exactly that id, and the row is re-bound to the picked entity rather than to the selection.
    /// </summary>
    [Fact]
    public async Task APickedEntityBecomesAConcretePinCarryingItsNetworkId()
    {
        var watch = Window(Picks(Ent(7)));

        // ⚠ The row starts on a DIFFERENT entity — otherwise the rail could pass while the pick was
        //   ignored and the selection used.
        Assert.True(await watch.PinOnPickedEntityAsync(Row(Ent(1))));

        var (row, binding) = Assert.Single(watch.Pinned.PinnedWithBindings());
        Assert.Equal(EntityBindingKind.Concrete, binding.Kind);
        Assert.Equal(PickedNetworkId, binding.NetworkId);
        Assert.Equal(Ent(7), binding.Captured);
        Assert.Equal(Ent(7), row.Origin.Entity);
        Assert.True(binding.IsPersistable);
    }

    /// <summary>
    /// ⛔⛔ <b>A cancelled pick pins NOTHING</b> — ⚠ it does not silently fall back to the selection.
    /// 📌 A gesture that does something other than what it offered is worse than one that does nothing.
    /// </summary>
    [Fact]
    public async Task ACancelledPickPinsNothing()
    {
        var watch = Window(_ => Task.FromResult<EntityBinding?>(null));

        Assert.False(await watch.PinOnPickedEntityAsync(Row(Ent(1))));
        Assert.Empty(watch.Pinned.GetRows());
    }

    /// <summary>⛔ A cancellation from the host surfaces as "nothing pinned", not as a fault the UI
    /// callback would have to catch.</summary>
    [Fact]
    public async Task AnAbandonedPickIsNotAFault()
    {
        var watch = Window(_ => Task.FromException<EntityBinding?>(new OperationCanceledException()));

        Assert.False(await watch.PinOnPickedEntityAsync(Row(Ent(1))));
        Assert.Empty(watch.Pinned.GetRows());
    }

    /// <summary>⚠ A host with no map answers <c>false</c> — ⛔ never throws, and never pins.</summary>
    [Fact]
    public async Task AWindowWithNoPickerPinsNothing()
    {
        var watch = Window();

        Assert.False(watch.HasEntityPicker);
        Assert.False(await watch.PinOnPickedEntityAsync(Row(Ent(1))));
        Assert.Empty(watch.Pinned.GetRows());
    }

    /// <summary>
    /// ⛔ <b>A chameleon is REFUSED, not accepted.</b> This gesture promised a SPECIFIC entity; a
    /// "follows the selection" binding under that label would show a different entity's values.
    /// </summary>
    [Fact]
    public async Task AChameleonFromThePickerIsRefused()
    {
        var watch = Window(_ => Task.FromResult<EntityBinding?>(EntityBinding.Chameleon));

        Assert.False(await watch.PinOnPickedEntityAsync(Row(Ent(1))));
        Assert.Empty(watch.Pinned.GetRows());
    }

    // ══ the gesture RULE ════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ <b>The rule, asserted where it lives</b> *(📌 <c>R-21</c>/<c>R-62</c> — the menu needs ImGui)*:
    /// allowed while planning and paused, refused with a REASON while running or replaying, and refused
    /// with a different reason when the host has no map.
    /// </summary>
    [Fact]
    public void ThePinOnEntityRuleRefusesWithAReasonRatherThanDeadEnding()
    {
        var row = Row(Ent(1));

        Assert.True(VariableWatchGesture.DecidePinOnEntity(row, VariableRunState.Planning, true).Enabled);
        Assert.True(VariableWatchGesture.DecidePinOnEntity(row, VariableRunState.Paused,   true).Enabled);

        foreach (var refused in new[]
                 {
                     VariableWatchGesture.DecidePinOnEntity(row, VariableRunState.Running, true),
                     VariableWatchGesture.DecidePinOnEntity(row, VariableRunState.Replay,  true),
                     VariableWatchGesture.DecidePinOnEntity(row, VariableRunState.Planning, false),
                 })
        {
            Assert.False(refused.Enabled);
            Assert.False(string.IsNullOrWhiteSpace(refused.DisabledReason));
        }

        // ⛔ NOT a toggle: an already-pinned row may still be pinned on another entity, which is most
        //    of why this gesture exists.
        Assert.Equal(VariableWatchGesture.PinOnEntityLabel,
                     VariableWatchGesture.DecidePinOnEntity(row, VariableRunState.Planning, true).Label);
    }

    // ══ the FORWARDING — asserted on the CONSTRUCTED window (R-67) ══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE FORWARDING RAIL:</b> a composition root that HAS a picker gets a Watch that HAS one.
    /// 📌 <c>R-67</c> — asserted on the constructed WINDOW, ⛔ never on the registrar's source line.
    /// </summary>
    [Fact]
    public void TheRegistrarHandsThePickerToTheWatchItBuilds()
    {
        Assert.True(Registrar(Picks(Ent(7))).Watch!.HasEntityPicker);

        // ⚠ And a host with no map genuinely has none — ⛔ the entry is then ABSENT, not dead.
        Assert.False(Registrar(null).Watch!.HasEntityPicker);
    }

    /// <summary>
    /// ⭐⭐ <b>And the TABLE is wired to it</b> — the row menu asks <c>CanPinOnEntity</c>, so a table that
    /// was never handed the predicate would never draw the entry however well the window worked.
    /// </summary>
    [Fact]
    public void TheTableIsAskedWhetherAPickIsAvailable()
    {
        var reg   = Registrar(Picks(Ent(7)));
        var table = ((IVariableTableHost)reg.Variables).VariableTable;

        Assert.NotNull(table);
        Assert.True(table!.CanPinOnEntity?.Invoke());
    }
}
