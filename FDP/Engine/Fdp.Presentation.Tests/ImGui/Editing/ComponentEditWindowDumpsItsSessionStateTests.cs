using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Editing;
using Fdp.Presentation.Tests.ImGui.Panels;
using StructEdit.Core;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Editing;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>ComponentEditWindow</c> converted to the <c>PanelSnapshot</c> contract.</b>
/// 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 4 (the caller-registers
/// rule — this window is the caller wrapping the generic <c>StructEdit.Core</c> edit-node tree).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class ComponentEditWindowDumpsItsSessionStateTests : IDisposable
{
    public ComponentEditWindowDumpsItsSessionStateTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    private sealed class FakeEditSession : IEditSession
    {
        public EditDocument Document { get; set; } = null!;
        public EditRebuildState RebuildState { get; set; } = EditRebuildState.Stable;
        public bool IsDirty { get; set; }
        public void MarkStructuralChange() { }
        public void RebuildDocument() { }
        public ValidationResult Validate() => ValidationResult.Ok();
        public object Commit() => new object();
        public void Cancel() { }
        public void Dispose() { }
    }

    private sealed class FakeInspectableSession : IInspectableSession
    {
        public bool IsReadOnly => false;
        public int EntityCount => 0;
        public IEnumerable<Entity> GetEntities() => Array.Empty<Entity>();
        public bool IsAlive(Entity e) => true;
        public bool HasComponent(Entity e, Type t) => false;
        public object? GetComponent(Entity e, Type t) => null;
        public void SetComponent(Entity e, Type t, object data) { }
        public IEnumerable<Type> GetAllComponentTypes() => Array.Empty<Type>();
        public bool HasAuthority(Entity e, Type t) => false;
    }

    private struct SampleComponent { public int Value; }

    private static ComponentEditWindow MakeWindow(IEditSession session, Entity entity) =>
        new ComponentEditWindow(
            id: "component_edit_test", title: "Edit", owningPerspective: "test-perspective",
            session: session, targetEntity: entity, componentType: typeof(SampleComponent),
            sessionGetter: () => new FakeInspectableSession());

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain("component_edit_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(new FakeEditSession(), new Entity(1, 1));

        Assert.Contains("component_edit_test", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("component_edit_test", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("component_edit_test"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheDirtyAndComponentState()
    {
        PanelSnapshot.CaptureEnabled = true;
        var session = new FakeEditSession { IsDirty = true };
        var window = MakeWindow(session, new Entity(3, 2));

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("component_edit_test");
        Assert.NotNull(vm);
        Assert.Equal("component_edit_test", vm!.PanelId);
        Assert.Equal(ComponentEditWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.True(dump["isDirty"]!.GetValue<bool>());
        Assert.Equal(nameof(SampleComponent), dump["componentTypeName"]!.GetValue<string>());
        Assert.Equal(3, dump["targetEntityIndex"]!.GetValue<int>());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var window = MakeWindow(new FakeEditSession(), new Entity(1, 1));   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("component_edit_test", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
        Assert.False(vm.IsDirty);
    }
}
