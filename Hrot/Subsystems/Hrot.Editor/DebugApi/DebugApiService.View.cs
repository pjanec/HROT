using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Hrot.IG.Components;
using Hrot.UI.Common.Map3D;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ <c>CE-1262</c> / <c>CE-1263</c> (R-1029, AV-1…AV-4) — <c>GET /view</c>: what the operator is looking at (the mode, the camera with
    /// its field of view, the possessed unit and what its crosshair rests on, the selection, the debugged unit, the units in view nearest
    /// first) and <c>GET /selection</c>. 📄 docs/DESIGN_Agent_Shared_View.md §6a.
    /// </summary>
    public sealed partial class DebugApiService
    {
        public JsonNode GetView(int limit)
        {
            var view = ViewTelemetry.Of(_world);
            var result = new JsonObject();
            if (view is null)
            {
                result["camera"] = null;
                result["note"] = "No map canvas draws this world (a headless host): there is no camera. The selection is still below.";
            }
            else
            {
                static JsonArray V3(System.Numerics.Vector3 v) => new(MathF.Round(v.X, 2), MathF.Round(v.Y, 2), MathF.Round(v.Z, 2));
                result["mode"] = view.Mode;
                var camera = new JsonObject
                {
                    ["eye"] = V3(view.Eye), ["forward"] = V3(view.Forward), ["yawDeg"] = MathF.Round(view.YawDeg, 1),
                    ["pitchDeg"] = MathF.Round(view.PitchDeg, 1), ["distance"] = MathF.Round(view.Distance, 1),
                    ["fovyDeg"] = MathF.Round(view.FovyDeg, 1), ["viewport"] = new JsonArray(view.Viewport.X, view.Viewport.Y),
                };
                if (view.Rect2DMin is { } lo && view.Rect2DMax is { } hi)
                    camera["rect2D"] = new JsonArray(MathF.Round(lo.X, 1), MathF.Round(lo.Y, 1), MathF.Round(hi.X, 1), MathF.Round(hi.Y, 1));
                result["camera"] = camera;
                result["possessed"] = NetworkIdOrNull(view.Possessed);
                result["aimTarget"] = view.AimTarget != 0 ? view.AimTarget : null;
                if (view.ZoomFovDeg is { } z) result["zoomFovDeg"] = MathF.Round(z, 1);

                var units = new JsonArray();
                foreach (var u in ViewReader.InView(_world, view, limit))
                    units.Add(new JsonObject
                    {
                        ["networkId"] = u.NetworkId, ["name"] = u.Name, ["distance"] = MathF.Round(u.Distance, 1),
                        ["screen"] = new JsonArray(MathF.Round(u.Screen.X), MathF.Round(u.Screen.Y)),
                    });
                result["inView"] = units;
            }
            var (primary, all) = SelectionOf(_world, _entityMap);
            result["selection"] = all;
            result["primary"] = primary;
            result["debugged"] = Hrot.Common.Diagnostics.Gizmos.UnitDebugController.Current?.Debugged is { } d ? NetworkIdOrNull(d) : null;
            return result;
        }

        public JsonNode GetSelection()
        {
            var (primary, all) = SelectionOf(_world, _entityMap);
            return new JsonObject { ["primary"] = primary, ["selection"] = all };
        }

        private static (long? Primary, JsonArray All) SelectionOf(EntityRepository world, Fdp.Toolkit.Replication.Services.NetworkEntityMap map)
        {
            var all = new JsonArray();
            long? primary = null;
            if (!world.IsComponentTypeRegistered<SelectionState>()) return (null, all);
            foreach (var e in world.Query().With<SelectionState>().Build())
            {
                ref readonly var s = ref world.GetComponentRO<SelectionState>(e);
                if (!s.IsSelected || !map.TryGetNetworkId(e, out var id)) continue;
                all.Add(id);
                if (s.IsPrimarySelection) primary = id;
            }
            return (primary, all);
        }

        private long? NetworkIdOrNull(Entity e)
            => e != Entity.Null && _entityMap.TryGetNetworkId(e, out var id) ? id : null;
    }
}
