using System;
using System.Linq;
using Fhsm.Compiler;
using Fhsm.Kernel.Data;
using FluentAssertions;
using Hrot.Hsm.Editor.Inspector;
using Hrot.Hsm.Editor.Model;
using Xunit;

namespace Hrot.Hsm.Editor.Tests.Inspector;

/// <summary>
/// AIE-024 tests for HSM field picker drawers.
/// All logic-level: no ImGui context required.
/// </summary>
public sealed class HsmPickerDrawerTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static (HsmDefinitionBlob blob, MachineMetadata meta) Compile(HsmBuilder b)
    {
        var graph = b.Build();
        HsmNormalizer.Normalize(graph);
        var flat = HsmFlattener.Flatten(graph);
        return (HsmEmitter.Emit(flat), HsmEmitter.BuildMachineMetadata(graph));
    }

    private static HsmAsset MakeAsset()
    {
        var b = new HsmBuilder("Test");
        b.Event("Fire",  1);
        b.Event("Reset", 2);
        b.State("Active").Final();
        b.State("Idle").Initial()
            .OnEntry("Ns.Actions.StartIdle")
            .OnExit("Ns.Actions.StopIdle")
            .On("Fire").GoTo("Active");
        var (blob, meta) = Compile(b);
        return HsmAssetProjector.Project(blob, meta, null, Guid.NewGuid(), "Test", "", false, "");
    }

    // ── HsmEventPickerDrawer tests ────────────────────────────────────────────

    [Fact]
    public void FieldPicker_HsmEvent_ListsAssetEvents()
    {
        var asset  = MakeAsset();
        var drawer = new HsmEventPickerDrawer(asset);
        var items  = drawer.GetItems();

        items.Should().Contain("Fire");
        items.Should().Contain("Reset");
        items.Should().HaveCount(2, "exactly the two events defined in the builder");
    }

    [Fact]
    public void FieldPicker_HsmEvent_ItemsSorted()
    {
        var asset  = MakeAsset();
        var drawer = new HsmEventPickerDrawer(asset);
        drawer.GetItems().Should().BeInAscendingOrder();
    }

    // ── HsmStateSelectorDrawer tests ──────────────────────────────────────────

    [Fact]
    public void FieldPicker_HsmState_ListsAssetStates()
    {
        var asset  = MakeAsset();
        var drawer = new HsmStateSelectorDrawer(asset);
        var items  = drawer.GetItems();

        items.Should().Contain("Idle");
        items.Should().Contain("Active");
        // Compiler-internal states (names starting with __) must not appear.
        items.Should().NotContain(s => s.StartsWith("__"),
            "compiler-internal and synthetic root states must not appear in the picker");
    }

    [Fact]
    public void FieldPicker_HsmState_ItemsSorted()
    {
        var asset  = MakeAsset();
        var drawer = new HsmStateSelectorDrawer(asset);
        drawer.GetItems().Should().BeInAscendingOrder();
    }

    // ── HsmActionPickerDrawer tests ───────────────────────────────────────────

    [Fact]
    public void FieldPicker_HsmAction_ListsStateActions()
    {
        var asset  = MakeAsset();
        var drawer = new HsmActionPickerDrawer(asset);
        var items  = drawer.GetItems();

        // OnEntry and OnExit actions on "Idle" state must appear.
        items.Should().Contain("Ns.Actions.StartIdle");
        items.Should().Contain("Ns.Actions.StopIdle");
    }

    // ── HsmGuardPickerDrawer tests ────────────────────────────────────────────

    [Fact]
    public void FieldPicker_HsmGuard_EmptyAsset_ReturnsEmpty()
    {
        // Asset with no guard functions.
        var asset  = MakeAsset();
        var drawer = new HsmGuardPickerDrawer(asset);
        // No guards were set in the builder so list should be empty.
        drawer.GetItems().Should().BeEmpty("no guard functions were registered");
    }
}
