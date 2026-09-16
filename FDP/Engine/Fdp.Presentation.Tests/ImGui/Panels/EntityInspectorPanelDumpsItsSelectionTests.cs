using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Diagnostics.Contracts.Panels;
using Fdp.Presentation.Abstractions;
using Fdp.Presentation.Panels;
using Fdp.Presentation.Windows.ReplayBrowser;
using Xunit;

namespace Fdp.Presentation.Tests.ImGui.Panels;

/// <summary>
/// ⭐⭐⭐ <b>U-obs-5 — <c>EntityInspectorPanel</c>/<c>FdpEntityInspectorWindow</c> converted to the
/// <c>PanelSnapshot</c> contract.</b> 📄 <c>docs/DESIGN_UI_Observability_Snapshot.md</c> §Example ·
/// <c>docs/blueprints/batches/QUEUE_Panel_Observability_Sweep.md</c> group 4 ("the HOST registers" gotcha
/// — <c>EntityInspectorPanel</c> is the plain panel, <c>FdpEntityInspectorWindow</c> the host).
/// </summary>
[Collection(PanelSnapshotTestCollection.Name)]
public sealed class EntityInspectorPanelDumpsItsSelectionTests : IDisposable
{
    public EntityInspectorPanelDumpsItsSelectionTests()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    public void Dispose()
    {
        PanelSnapshot.Clear();
        PanelSnapshot.CaptureEnabled = false;
    }

    [ComponentId(9001)]
    private struct FakeTag { public int Value; }

    private sealed class FakeSession : IInspectableSession
    {
        private readonly List<Entity> _entities;
        private readonly HashSet<Entity> _tagged;

        public FakeSession(List<Entity> entities, IEnumerable<Entity>? tagged = null)
        {
            _entities = entities;
            _tagged = tagged is null ? new HashSet<Entity>() : new HashSet<Entity>(tagged);
        }

        public bool IsReadOnly => false;
        public int EntityCount => _entities.Count;
        public IEnumerable<Entity> GetEntities() => _entities;
        public bool IsAlive(Entity e) => true;
        public bool HasComponent(Entity e, Type t) => t == typeof(FakeTag) && _tagged.Contains(e);
        public object? GetComponent(Entity e, Type t) => t == typeof(FakeTag) && _tagged.Contains(e) ? new FakeTag { Value = 1 } : null;
        public void SetComponent(Entity e, Type t, object v) { }
        public IEnumerable<Type> GetAllComponentTypes() => new[] { typeof(FakeTag) };
        public bool HasAuthority(Entity e, Type t) => false;
    }

    private static Entity MakeEntity(int seed)
    {
        using var repo = new EntityRepository();
        for (int i = 0; i < seed; i++) repo.CreateEntity();
        return repo.CreateEntity();
    }

    // ── Rail 1 — instrumented at construction, on the PRODUCTION object ─────────────────────────

    /// <summary>⭐⭐⭐ The window is instrumented the moment it is CONSTRUCTED — before it has ever drawn.
    /// ⛔ Would go red if <c>DeclareInstrumented</c> drifted into the draw.</summary>
    [Fact]
    public void ConstructingTheWindow_DeclaresItInstrumented_BeforeItHasEverDrawn()
    {
        Assert.DoesNotContain("fdp_entity_inspector_test", PanelSnapshot.RegisteredPanels);   // ⛔ anti-vacuity

        var window = MakeWindow(new FakeSession(new List<Entity>()), new InspectorState());

        Assert.Contains("fdp_entity_inspector_test", PanelSnapshot.RegisteredPanels);
        Assert.DoesNotContain("fdp_entity_inspector_test", PanelSnapshot.CapturedPanels);
        Assert.Null(PanelSnapshot.TryGet("fdp_entity_inspector_test"));
        Assert.NotNull(window);
    }

    // ── Rail 2 — the dump carries a real field ───────────────────────────────────────────────────

    [Fact]
    public void AfterABuild_TheDumpCarriesTheSelectedEntitysComponents()
    {
        PanelSnapshot.CaptureEnabled = true;
        var e0 = MakeEntity(0);
        var e1 = MakeEntity(1);
        var session = new FakeSession(new List<Entity> { e0, e1 }, tagged: new[] { e1 });
        var state = new InspectorState { SelectedEntity = e1 };
        var window = MakeWindow(session, state);

        window.SimulateDrawClientArea();

        var vm = PanelSnapshot.TryGet("fdp_entity_inspector_test");
        Assert.NotNull(vm);
        Assert.Equal("fdp_entity_inspector_test", vm!.PanelId);
        Assert.Equal(FdpEntityInspectorWindow.Kind, vm.PanelKind);

        var dump = vm.Dump();
        Assert.Equal(2, dump["totalEntityCount"]!.GetValue<int>());
        Assert.Equal(2, dump["entities"]!.AsArray().Count);
        Assert.Equal(1, dump["selectedCount"]!.GetValue<int>());
        var comps = dump["selectedComponentTypeNames"]!.AsArray();
        Assert.Single(comps);
        Assert.Equal(nameof(FakeTag), comps[0]!.GetValue<string>());
    }

    // ── Rail 3 — the flag gates the DUMP, not the BUILD ──────────────────────────────────────────

    [Fact]
    public void WithCaptureOff_TheProductionPathPublishesNothing()
    {
        var session = new FakeSession(new List<Entity> { MakeEntity(0) });
        var window = MakeWindow(session, new InspectorState());   // CaptureEnabled stays false

        var vm = window.SimulateDrawClientArea();

        Assert.Empty(PanelSnapshot.CapturedPanels);
        Assert.Contains("fdp_entity_inspector_test", PanelSnapshot.RegisteredPanels);
        Assert.NotNull(vm);   // ⭐ the BUILD is unaffected by the flag
        Assert.Equal(1, vm!.TotalEntityCount);
    }

    private static FdpEntityInspectorWindow MakeWindow(IInspectableSession session, InspectorState state) =>
        new FdpEntityInspectorWindow(
            "fdp_entity_inspector_test", "Entity Inspector", "test-perspective",
            new EntityInspectorPanel(), () => session, () => state, new Vector4(1, 1, 1, 1));
}
