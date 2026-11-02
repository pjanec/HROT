using Fdp.Toolkit.Replication;
using System;
using System.Numerics;
using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.IG.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    // GZ058: mirrors MissionRenderLayer rendering logic via StatelessGizmoSystem.
    // Draws orange gradient lines from a unit to its mission task targets.
    // ⭐ CE-3123 (R-228): a [GizmoProjector] now — the registrar hands the constructor the host's IGeographicTransform
    //   (MapInteractionContext.Services), so it is no longer registered by hand on the two hosts that remembered. Family Path:
    //   which units it draws for (selected / pinned / all) is the family policy's call, not a selection check of its own.
    //   📄 docs/DESIGN_Uniform_Gizmo_Membership.md §10.
    [GizmoProjector(typeof(SimTransform), Family = Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags.Path)]
    public sealed class MissionPresentationGizmo : IStatelessGizmo
    {
        private static readonly Rgba32 StartColor = new Rgba32(255, 165, 0, 200);  // orange
        private static readonly Rgba32 EndColor   = new Rgba32(0,   0, 139, 200);  // dark blue

        /// <remarks>⭐ S0 item 5 (SR-135): a task's place is local metres (<c>"target"</c>, a <c>PointLink</c>) — the transform is no longer needed to draw it; the parameter stays so hosts keep composing the gizmo unchanged.</remarks>
        public MissionPresentationGizmo(IGeographicTransform geoTransform)
        {
            if (geoTransform == null) throw new ArgumentNullException(nameof(geoTransform));
        }

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!draw.IsLayerActive(DebugTraceLayers.Paths)) return;   // ⭐ CE-1203 — its own checkbox (§5e FL-3): a hidden layer costs no work
            if (!view.HasManagedComponent<ActiveMissionPlan>(entity)) return;
            var activePlan = view.GetManagedComponentRO<ActiveMissionPlan>(entity);
            if (activePlan?.Plan?.Tasks == null) return;

            ref readonly var simTr = ref view.GetComponentRO<SimTransform>(entity);
            var lastPos = new Vector3(simTr.Position.X, simTr.Position.Y, 0f);

            foreach (var task in activePlan.Plan.Tasks)
            {
                if (string.IsNullOrEmpty(task.BehaviorParams)) continue;

                Vector3? target = null;

                try
                {
                    using var doc = JsonDocument.Parse(task.BehaviorParams);
                    if (doc.RootElement.TryGetProperty("target", out var targetEl))
                        target = JsonSerializer.Deserialize<Fdp.Toolkit.Replication.PointLink>(targetEl.GetRawText(), Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed).Local;   // the engine's options: a [x, y, z] is a Vector3 there
                }
                catch { }

                if (target is { } at)
                {
                    var targetPos = new Vector3(at.X, at.Y, 0f);

                    draw.DrawLineGradient(lastPos, targetPos, StartColor, EndColor, thickness: 2f, SizeMode.WorldMeters, layer: DebugTraceLayers.Paths);

                    lastPos = targetPos;
                }
            }
        }
    }
}
