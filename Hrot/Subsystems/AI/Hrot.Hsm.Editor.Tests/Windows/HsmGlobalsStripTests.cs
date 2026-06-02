using System;
using System.Collections.Generic;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Editor.AiShared.Selection;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using Hrot.Hsm.Editor.Windows;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Windows;

/// <summary>
/// AIE-027 tests for <see cref="HsmGlobalsStripLogic"/> (logic layer of HsmGlobalsStrip).
/// All headless — no ImGui context required.
/// </summary>
public sealed class HsmGlobalsStripTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static (HsmDefinitionBlob blob, MachineMetadata meta) Compile(HsmBuilder b)
    {
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat = HsmFlattener.Flatten(graph);
        return (HsmEmitter.Emit(flat), HsmEmitter.BuildMachineMetadata(graph));
    }

    private static HsmAsset MakeAssetWithGlobal()
    {
        var b = new HsmBuilder("Test");
        b.Event("Emergency", 1);
        b.State("Disabled").Final();
        b.State("Active").Final();
        b.State("Idle").Initial().On("Emergency").GoTo("Disabled");
        b.GlobalTransition("Emergency", "Disabled");
        var (blob, meta) = Compile(b);
        return HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "Test", "", false, "");
    }

    private static HsmAsset MakeAssetNoGlobal()
    {
        var b = new HsmBuilder("NoGlobals");
        b.Event("Fire", 1);
        b.State("Idle").Initial();
        var (blob, meta) = Compile(b);
        return HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "NoGlobals", "", false, "");
    }

    private static (HsmGlobalsStripLogic logic, SpyDispatcher spy) MakeLogic(HsmAsset asset)
    {
        var store = new EditorSelectionStore();
        store.ActiveAsset = asset;
        var spy   = new SpyDispatcher();
        var logic = new HsmGlobalsStripLogic(asset, store, spy);
        return (logic, spy);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void HsmGlobalsStrip_RendersChipPerGlobalTransition()
    {
        var asset    = MakeAssetWithGlobal();
        var (logic, _) = MakeLogic(asset);

        var labels = logic.GetChipLabels();

        labels.Should().HaveCount(asset.AllGlobalTransitions.Count,
            "one chip label per global transition");
        // Label should contain the event name and target state name.
        labels[0].Should().Contain("Emergency");
        labels[0].Should().Contain("Disabled");
    }

    [Fact]
    public void HsmGlobalsStrip_NoGlobalTransitions_ReturnsEmptyChipList()
    {
        var asset    = MakeAssetNoGlobal();
        var (logic, _) = MakeLogic(asset);

        logic.GetChipLabels().Should().BeEmpty();
    }

    [Fact]
    public void HsmGlobalsStrip_ClickChip_SetsGlobalTransitionSubSelection()
    {
        var asset       = MakeAssetWithGlobal();
        var store       = new EditorSelectionStore();
        store.ActiveAsset = asset;
        var spy         = new SpyDispatcher();
        var logic       = new HsmGlobalsStripLogic(asset, store, spy);

        // Click chip 0.
        logic.OnChipClicked(0);

        // ActiveSubSelection must be HsmGlobalTransitionSelection with correct VisualId.
        store.ActiveSubSelection.Should().BeOfType<HsmGlobalTransitionSelection>();
        var sel = (HsmGlobalTransitionSelection)store.ActiveSubSelection!;
        sel.VisualId.Should().Be(asset.AllGlobalTransitions[0].VisualId);
    }

    [Fact]
    public void HsmGlobalsStrip_ClickChip_OutOfRange_DoesNotThrow()
    {
        var asset       = MakeAssetWithGlobal();
        var (logic, _) = MakeLogic(asset);

        // Out-of-range click must be a silent no-op.
        Action act = () => logic.OnChipClicked(999);
        act.Should().NotThrow();
    }

    [Fact]
    public void HsmGlobalsStrip_Remove_DispatchesCommand()
    {
        var asset       = MakeAssetWithGlobal();
        var (logic, spy) = MakeLogic(asset);
        var expectedId  = asset.AllGlobalTransitions[0].VisualId;

        logic.OnChipRemoved(0);

        spy.Removed.Should().ContainSingle()
           .Which.Should().Be(expectedId,
               "dispatcher must receive the correct VisualId");
    }

    [Fact]
    public void HsmGlobalsStrip_Remove_OutOfRange_DoesNotDispatch()
    {
        var asset       = MakeAssetWithGlobal();
        var (logic, spy) = MakeLogic(asset);

        logic.OnChipRemoved(999);

        spy.Removed.Should().BeEmpty("out-of-range index must not dispatch");
    }

    // ── DefaultHsmGlobalsCommandDispatcher integration ────────────────────────

    [Fact]
    public void DefaultDispatcher_RemoveGlobalTransition_ReducesCount()
    {
        var asset      = MakeAssetWithGlobal();
        int before     = asset.AllGlobalTransitions.Count;
        var dispatcher = new DefaultHsmGlobalsCommandDispatcher(asset);
        var visualId   = asset.AllGlobalTransitions[0].VisualId;

        dispatcher.RemoveGlobalTransition(visualId);

        asset.AllGlobalTransitions.Count.Should().Be(before - 1);
        asset.IsDirty.Should().BeTrue("dispatcher must mark asset dirty");
    }

    [Fact]
    public void DefaultDispatcher_RemoveGlobalTransition_UnknownId_NoThrow()
    {
        var asset      = MakeAssetWithGlobal();
        var dispatcher = new DefaultHsmGlobalsCommandDispatcher(asset);

        Action act = () => dispatcher.RemoveGlobalTransition(Guid.NewGuid());
        act.Should().NotThrow();
    }

    // ── Spy ───────────────────────────────────────────────────────────────────

    private sealed class SpyDispatcher : IHsmGlobalsCommandDispatcher
    {
        public List<Guid> Removed { get; } = new();
        public void RemoveGlobalTransition(Guid visualId) => Removed.Add(visualId);
    }
}
