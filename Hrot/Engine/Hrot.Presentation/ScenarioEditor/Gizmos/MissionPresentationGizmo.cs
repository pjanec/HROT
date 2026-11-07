using Fdp.Toolkit.Replication;
using System;
using System.Numerics;
using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.Map.Common.Components;
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.ScenarioEditor.Gizmos
{
    // GZ058: mirrors MissionRenderLayer rendering logic via StatelessGizmoSystem.
    // ⭐ CE-3123 (R-228): a [GizmoProjector] now — the registrar hands the constructor the host's IGeographicTransform
    //   (MapInteractionContext.Services), so it is no longer registered by hand on the two hosts that remembered. Family Path:
    //   which units it draws for (selected / pinned / all) is the family policy's call, not a selection check of its own.
    //   📄 docs/DESIGN_Uniform_Gizmo_Membership.md §10.
    /// <summary>
    /// ⭐⭐ W-1 / P-8 8d (📄 docs/UX/DESIGN_Mission_Plan_Editor.md §5.5 M3, §5.8 E2, §5.12; mockup <c>mission-plan-editor.svg</c>) — a unit's MISSION PLAN on
    /// the map, the ONE drawing of it: the route from the unit through each task's place (solid to task 1, dashed after, in the unit's side colour)
    /// and a numbered marker per task — done green, running amber, planned grey (<see cref="MissionPlanQueue"/>). A task's place is its order DTO's
    /// first place field (<see cref="Hrot.Presentation.Orders.MissionPlanPlaces"/>), else a legacy behaviour's <c>"target"</c> point, else the
    /// previous task's place (E2). ⛔ SUPERSEDED: the orange → blue gradient lines to the <c>"target"</c> points only (an order's <c>Location</c> drew nothing).
    /// </summary>
    [GizmoProjector(typeof(SimTransform), Family = Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path)]
    public sealed class MissionPresentationGizmo : IStatelessGizmo
    {
        private static readonly Rgba32 DoneColour = new(91, 208, 106), RunningColour = new(242, 192, 51), PlannedColour = new(60, 60, 70), Rim = new(255, 255, 255);
        private static readonly FixedString32[] Numbers = BuildNumbers(MissionPlanQueue.MaxTasks);

        /// <remarks>⭐ S0 item 5 (SR-135): a task's place is local metres — the transform is no longer needed to draw it; the parameter stays so hosts keep composing the gizmo unchanged.</remarks>
        public MissionPresentationGizmo(IGeographicTransform geoTransform)
        {
            if (geoTransform == null) throw new ArgumentNullException(nameof(geoTransform));
        }

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!draw.IsLayerActive(DebugTraceLayers.Paths)) return;   // ⭐ CE-1203 — its own checkbox (§5e FL-3): a hidden layer costs no work
            if (!view.HasManagedComponent<ActiveMissionPlan>(entity)) return;
            var activePlan = view.GetManagedComponentRO<ActiveMissionPlan>(entity);
            if (activePlan?.Plan?.Tasks is not { Count: > 0 } tasks) return;
            var places = Hrot.Presentation.Orders.MissionPlanPlaces.Of(activePlan);
            var refs = view is EntityRepository world ? Hrot.UI.Common.Rules.AuthoredRules.ScopeOf(world) : null;   // ⚠ ScopeOf is cached per world
            var repo = view as EntityRepository;
            var colour = repo != null && repo.IsComponentTypeRegistered<EntityInfo>() && repo.HasComponent<EntityInfo>(entity)
                ? Hrot.Common.Diagnostics.Gizmos.SidePalette.Of(repo.GetComponentRO<EntityInfo>(entity).ForceId)
                : Hrot.Common.Diagnostics.Gizmos.SidePalette.Neutral;
            int current = repo != null && repo.IsComponentTypeRegistered<MissionPlanQueue>() && repo.HasComponent<MissionPlanQueue>(entity) ? repo.GetComponentRO<MissionPlanQueue>(entity).CurrentTask : -1;

            var previous = view.GetComponentRO<SimTransform>(entity).Position;
            int n = Math.Min(tasks.Count, Numbers.Length);
            Span<Vector3> at = stackalloc Vector3[MissionPlanQueue.MaxTasks];
            for (int i = 0; i < n; i++)
            {
                var place = Fdp.Presentation.Editing.MapPreviews.PlaceOf(places[i], refs) ?? LegacyTarget(tasks[i].BehaviorParams) ?? previous;   // E2
                if (place != previous) draw.DrawLine(previous, place, colour, 2f, layer: DebugTraceLayers.Paths, style: i == 0 ? LineStyle.Solid : LineStyle.Dashed);
                at[i] = previous = place;
            }
            for (int i = 0; i < n; i++)   // the markers over the route, numbered from 1 (map text anchors at its top-left: the number sits below)
            {
                var fill = current < 0 ? PlannedColour : i < current ? DoneColour : i == current ? RunningColour : PlannedColour;
                draw.DrawSphere(at[i], 10f, Rim, thickness: 1.2f, sizeMode: SizeMode.ScreenPixels, layer: DebugTraceLayers.Paths, fillColor: fill);
                draw.DrawText(at[i].X, at[i].Y, Numbers[i], new Rgba32(240, 240, 245), layer: DebugTraceLayers.Paths, fontSizePx: 14f, lineOffsetPx: 12f);
            }
        }

        /// <summary>A behaviour task's legacy <c>"target"</c> point (local metres), else null.</summary>
        private static Vector3? LegacyTarget(string? json)
        {
            if (string.IsNullOrEmpty(json) || !json.Contains("\"target\"", StringComparison.Ordinal)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("target", out var targetEl))
                {
                    var p = JsonSerializer.Deserialize<Fdp.Toolkit.Replication.PointLink>(targetEl.GetRawText(), Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed).Local;   // the engine's options: a [x, y, z] is a Vector3 there
                    return new Vector3(p.X, p.Y, 0f);
                }
            }
            catch (JsonException) { }
            return null;
        }

        private static FixedString32[] BuildNumbers(int n)
        {
            var a = new FixedString32[n];
            for (int i = 0; i < n; i++) a[i] = new FixedString32((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return a;
        }
    }
}
