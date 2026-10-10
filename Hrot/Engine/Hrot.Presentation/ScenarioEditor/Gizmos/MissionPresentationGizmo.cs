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

        private readonly IGeographicTransform _geoTransform;

        public MissionPresentationGizmo(IGeographicTransform geoTransform)
        {
            _geoTransform = geoTransform ?? throw new ArgumentNullException(nameof(geoTransform));
        }

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasManagedComponent<ActiveMissionPlan>(entity)) return;
            var activePlan = view.GetManagedComponentRO<ActiveMissionPlan>(entity);
            if (activePlan?.Plan?.Tasks == null) return;

            ref readonly var simTr = ref view.GetComponentRO<SimTransform>(entity);
            var lastPos = new Vector3(simTr.Position.X, simTr.Position.Y, 0f);

            foreach (var task in activePlan.Plan.Tasks)
            {
                if (string.IsNullOrEmpty(task.BehaviorParams)) continue;

                float targetLat = float.NaN;
                float targetLon = float.NaN;

                try
                {
                    using var doc = JsonDocument.Parse(task.BehaviorParams);
                    if (doc.RootElement.TryGetProperty("targetLat", out var latEl))
                        targetLat = latEl.GetSingle();
                    if (doc.RootElement.TryGetProperty("targetLon", out var lonEl))
                        targetLon = lonEl.GetSingle();
                }
                catch { }

                if (!float.IsNaN(targetLat) && !float.IsNaN(targetLon))
                {
                    var cartesian = _geoTransform.ToCartesian(targetLat, targetLon, 0.0);
                    var targetPos = new Vector3((float)cartesian.X, (float)cartesian.Y, 0f);

                    draw.DrawLineGradient(lastPos, targetPos, StartColor, EndColor, thickness: 2f, SizeMode.WorldMeters);

                    lastPos = targetPos;
                }
            }
        }
    }
}
