using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Settings;
using Fdp.Toolkit.Replication.Components;
using Hrot.Common.Constants;
using Hrot.Common.Diagnostics.Gizmos;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.ScenarioEditor.Map;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>
/// ⭐ <c>CE-3123</c> (R-228) — nothing gizmo-related is wired per host: the map pack gives every host the debug view state, the
/// system that applies its patches, the pin and AI-trace actions, and the projectors that need a service.
/// 📄 docs/DESIGN_Uniform_Gizmo_Membership.md §10.
/// </summary>
public sealed class HostOnlyWiringTests : IDisposable
{
    private readonly EntityRepository _w = new();

    public HostOnlyWiringTests()
    {
        _w.RegisterComponent<SimTransform>();
        _w.RegisterComponent<NetworkIdentity>();
    }

    public void Dispose() => _w.Dispose();

    private Entity Unit(bool brain = false)
    {
        var e = _w.CreateEntity();
        _w.AddComponent(e, new SimTransform { Position = new Vector3(5, 5, 0) });
        _w.AddComponent(e, new NetworkIdentity(77));
        if (brain) _w.AddComponent(e, new BehaviorState());
        return e;
    }

    private void Fire(MapInteraction mi, int actionId, Entity target)
    {
        Assert.True(mi.Actions.TryGetHandler(actionId, out var handler), $"action {actionId} registered");
        handler!(_w, target);
        _w.Bus.SwapBuffers();
        mi.DebugStatePatch.Execute(_w, 0.016f);
    }

    [Fact]
    public void CE3123_ThePack_GivesAWorldWithoutIt_TheDebugViewState_AndRunsThePatchInItsInteractionSystems()
    {
        Assert.False(_w.IsComponentTypeRegistered<DebugState>());
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _w });

        Assert.True(_w.IsComponentTypeRegistered<DebugState>());
        var systems = mi.InteractionSystems;
        Assert.Same(mi.ActionDispatch, systems[0]);
        Assert.Same(mi.DebugStatePatch, systems[1]);   // after the dispatch, before the gizmo group draws
        Assert.Same(mi.GizmoGroup, systems[2]);
        Assert.Contains(typeof(DebugStatePatchSystem), mi.RequiredSystems);
        Assert.Empty(mi.Unserviceable(systems));

        var unit = Unit();
        Fire(mi, GizmoPins.ActionIdOf(AiOverlayFlags.Path), unit);
        Assert.Equal(AiOverlayFlags.Path, _w.GetComponentRO<DebugState>(unit).Ai);
    }

    [Fact]
    public void CE3123_TheAiTraceToggles_AreOfferedForABrain_AndFlipTheFlag()
    {
        _w.RegisterComponent<BehaviorState>();
        var mi = MapInteractionPack.Build(new MapInteractionContext { World = _w });
        var brain = Unit(brain: true);
        var plain = Unit();

        Assert.Contains("AI trace", ContextMenuProjectorGizmo.MenuJsonFor(_w, brain));
        Assert.Contains($"\"id\":{GlobalActionIds.ToggleAiTrace},", ContextMenuProjectorGizmo.MenuJsonFor(_w, brain));
        Assert.DoesNotContain("AI trace", ContextMenuProjectorGizmo.MenuJsonFor(_w, plain));

        Fire(mi, GlobalActionIds.ToggleAiTrace, brain);
        Assert.Equal(BehaviorDebugFlags.EnableTraceBuffer, _w.GetComponentRO<DebugState>(brain).Behavior);
        Fire(mi, GlobalActionIds.ToggleAiTraceLog, brain);
        Assert.Equal(BehaviorDebugFlags.EnableTraceBuffer | BehaviorDebugFlags.EmitToLog, _w.GetComponentRO<DebugState>(brain).Behavior);
        Fire(mi, GlobalActionIds.ToggleAiTrace, brain);
        Assert.Equal(BehaviorDebugFlags.EmitToLog, _w.GetComponentRO<DebugState>(brain).Behavior);

        Fire(mi, GlobalActionIds.ToggleAiTrace, plain);   // no brain: nothing to trace, nothing written
        Assert.False(_w.HasComponent<DebugState>(plain));
    }

    [Fact]
    public void CE3123_TwoPatchSystems_OnOneWorld_ApplyTheSameState()
    {
        // CGF and the Editor run BehaviorDiagnosticsModule's patch system AND the pack's; a patch sets absolute values.
        Hrot.Map.Common.PresentationComponentRegistry.RegisterDebugViewState(_w);
        var unit = Unit();
        GizmoPins.Set(_w, unit, AiOverlayFlags.Eqs, true);
        _w.Bus.SwapBuffers();
        new DebugStatePatchSystem().Execute(_w, 0.016f);
        new DebugStatePatchSystem().Execute(_w, 0.016f);
        Assert.Equal(AiOverlayFlags.Eqs, _w.GetComponentRO<DebugState>(unit).Ai);
    }

    [Fact]
    public void CE3123_AProjectorNeedingAServiceTheHostLacks_IsReportedByName_AndDrawsWhereTheServiceIs()
    {
        var reports = new System.Collections.Generic.List<string>();
        MapInteractionPack.Build(new MapInteractionContext { World = _w, ReportMapDiagnostic = reports.Add });
        Assert.Contains(reports, r => r.Contains("MissionPresentationGizmo") && r.Contains("IGeographicTransform"));
        Assert.DoesNotContain(reports, r => r.Contains("EntityEditorLabelGizmo"));   // it has a service-free constructor

        reports.Clear();
        using var other = new EntityRepository();
        MapInteractionPack.Build(new MapInteractionContext
        {
            World = other,
            ReportMapDiagnostic = reports.Add,
            Services = MapServices.Of(new Fdp.Modules.Geographic.Transforms.WGS84Transform()),
        });
        Assert.DoesNotContain(reports, r => r.Contains("MissionPresentationGizmo"));
    }

    [Fact]
    public void CE3123_TheRegistrar_PicksTheRichestConstructorItCanSatisfy()
    {
        var settings = new GizmoSettingsRegistry();
        var registry = new Fdp.Toolkit.Behavior.BehaviorRegistry();

        var bare = (EntityEditorLabelGizmo)GizmoReflectionRegistrar.Instantiate(
            typeof(EntityEditorLabelGizmo), settings, services: null, out _)!;
        Assert.NotNull(bare);
        var rich = GizmoReflectionRegistrar.Instantiate(
            typeof(EntityEditorLabelGizmo), settings, MapServices.Of(registry), out _);
        Assert.NotNull(rich);

        Assert.Null(GizmoReflectionRegistrar.Instantiate(typeof(MissionPresentationGizmo), settings, null, out var missing));
        Assert.Equal("IGeographicTransform", missing);
        Assert.Throws<MissingMethodException>(() =>
            GizmoReflectionRegistrar.Instantiate(typeof(NoPublicCtor), settings, null, out _));
    }

    [Fact]
    public void CE3123_TheLabel_WithoutABehaviourRegistry_DrawsTheIdAndHitPoints_NotTheBehaviour()
    {
        _w.RegisterComponent<BehaviorState>();
        _w.RegisterComponent<Fdp.Toolkit.Combat.Components.Health>();
        var unit = Unit(brain: true);
        _w.SetComponent(unit, new BehaviorState { ActiveBehaviorHash = 12345 });
        _w.AddComponent(unit, new Fdp.Toolkit.Combat.Components.Health { Current = 50, Max = 100 });
        var draw = new DebugPrimitiveBuffer();

        new EntityEditorLabelGizmo().Draw(_w, unit, draw);

        Assert.Equal(2, draw.GetFrame().ToArray().Count(p =>
            p.Shape == DebugPrimitiveShape.Text));
    }

    private sealed class NoPublicCtor { private NoPublicCtor() { } }
}
