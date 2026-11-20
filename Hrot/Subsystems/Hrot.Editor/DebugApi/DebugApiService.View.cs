using System;
using System.Text.Json.Nodes;
using Fdp.Core;
using Hrot.Map.Common.Components;
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
        /// <remarks>⭐ CE-4571 (X-7) — a UI-side route (<see cref="DebugApiRouteSides"/>): it reads the SHOWN world (A on the threaded editor, the
        /// one world otherwise), the world the map draws and publishes its view under. ⛔ It read the sim's world: threaded, the telemetry was never
        /// found (<i>"no map canvas"</i>) and the in-view walk read S from the UI thread.</remarks>
        public JsonNode GetView(int limit)
        {
            var world = ShownWorld;
            var view = ViewTelemetry.Of(world);
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
                foreach (var u in ViewReader.InView(world, view, limit))
                    units.Add(new JsonObject
                    {
                        ["networkId"] = u.NetworkId, ["name"] = u.Name, ["distance"] = MathF.Round(u.Distance, 1),
                        ["screen"] = new JsonArray(MathF.Round(u.Screen.X), MathF.Round(u.Screen.Y)),
                    });
                result["inView"] = units;
                // ⭐ CE-1451 (R-1046) — the 3-D map's build: a 3-D request waiting for it (the 2-D map shows "Building 3-D map…"), and the build's own state
                result["pending3D"] = view.Pending3D;
                if (view.Pending3DFailure is { } why) result["pending3DFailure"] = why;
                if (view.MapBuilt is { } built)
                    result["map"] = new JsonObject { ["built"] = built, ["step"] = view.MapBuildStep is { Length: > 0 } step ? step : null };
            }
            var (primary, all) = SelectionOf(ShownWorld, ShownMap);
            result["selection"] = all;
            result["primary"] = primary;
            if (ViewControl.Of(world)?.Following is { } follow)   // ⭐ CE-1293 (UC-10) — the unit the camera follows
            {
                result["following"] = NetworkIdOrNull(follow.Unit);
                result["followDistance"] = MathF.Round(follow.Distance, 2);
            }
            return result;
        }

        /// <summary>
        /// ⭐ <c>CE-1293</c> (docs/DESIGN_Unit_Camera.md UC-10) — <c>POST /view/follow {entity, distance?}</c>: the map's 3-D camera follows the
        /// unit on the distance continuum (far: orbit, near: over the shoulder, 0: his eyes); <c>entity: null</c> hands the camera back.
        /// Refused where no map draws this world, and while the station possesses a unit (its view is the eyes).
        /// </summary>
        public (JsonNode? Result, string? Error) FollowUnit(int? networkId, float? distance)
        {
            if (ViewControl.Of(ShownWorld) is not { } view)   // ⭐ CE-4571 — the map is registered under the world it draws
                return (null, "No map canvas draws this world (a headless host): there is no camera to follow with.");
            if (networkId is not { } id)
            {
                view.StopFollowing();
                return (new JsonObject { ["following"] = null }, null);
            }
            if (!ShownMap.TryGetEntity(id, out var entity) || !ShownWorld.IsAlive(entity))
                return (null, $"Entity {id} not found.");
            if (view.FollowUnit(entity, distance) is { } refusal) return (null, refusal);
            return (new JsonObject { ["following"] = id, ["distance"] = MathF.Round(view.Following!.Distance, 2) }, null);
        }

        /// <summary>⭐ <c>CE-1984</c> (docs/designs/sensor-view/DESIGN_Sensor_View.md D-10) — <c>GET /view/sensor</c>: the map's sensor view and, under
        /// night vision, the light it sees.</summary>
        public JsonNode GetSensorView()
        {
            if (ViewControl.Of(ShownWorld)?.Sensor is not { } sv)
                return new JsonObject { ["available"] = false, ["reason"] = "No map canvas draws this world (a headless host)." };
            return SensorViewNode(sv);
        }

        /// <summary>⭐ <c>CE-1984</c> — <c>POST /view/sensor {mode, palette?, halo?, sensorResolution?, noise?, vignette?, autoGain?}</c>; mode is
        /// <c>off</c> · <c>nightVision</c> · <c>thermal</c> (refused until CE-1985).</summary>
        public (JsonNode? Result, string? Error) SetSensorView(JsonNode? body)
        {
            if (ViewControl.Of(ShownWorld)?.Sensor is not { } sv)
                return (null, "No map canvas draws this world (a headless host): there is no view to switch.");
            if (body?["mode"]?.GetValue<string>() is { } m)
            {
                var mode = m switch
                {
                    "off" or "none" => Fdp.Toolkit.Vis2D.Abstractions.SceneSensor.None,
                    "nightVision" or "nv" => Fdp.Toolkit.Vis2D.Abstractions.SceneSensor.NightVision,
                    "thermal" => Fdp.Toolkit.Vis2D.Abstractions.SceneSensor.Thermal,
                    _ => (Fdp.Toolkit.Vis2D.Abstractions.SceneSensor?)null,
                };
                if (mode is not { } md) return (null, $"Unknown mode '{m}' (off, nightVision, thermal).");
                if (sv.SetMode(md) is { } refusal) return (null, refusal);
            }
            if (body?["palette"] is JsonValue pv && pv.TryGetValue<int>(out var p)) sv.Palette = Math.Clamp(p, 0, sv.Mode == Fdp.Toolkit.Vis2D.Abstractions.SceneSensor.Thermal ? 2 : 1);
            if (body?["halo"] is JsonValue hv && hv.TryGetValue<bool>(out var h)) sv.Halo = h;
            if (body?["sensorResolution"] is JsonValue rv && rv.TryGetValue<bool>(out var r)) sv.SensorResolution = r;
            if (body?["noise"] is JsonValue nv && nv.TryGetValue<bool>(out var n)) sv.Noise = n;
            if (body?["vignette"] is JsonValue vv && vv.TryGetValue<bool>(out var v)) sv.Vignette = v;
            if (body?["autoGain"] is JsonValue gv && gv.TryGetValue<bool>(out var g)) sv.AutoGain = g;
            return (SensorViewNode(sv), null);
        }

        private static JsonObject SensorViewNode(SensorView sv) => new()
        {
            ["available"] = sv.Post.Available,
            ["mode"] = sv.Mode switch { Fdp.Toolkit.Vis2D.Abstractions.SceneSensor.NightVision => "nightVision", Fdp.Toolkit.Vis2D.Abstractions.SceneSensor.Thermal => "thermal", _ => "off" },
            ["palette"] = sv.Palette, ["halo"] = sv.Halo, ["sensorResolution"] = sv.SensorResolution, ["noise"] = sv.Noise,
            ["vignette"] = sv.Vignette, ["autoGain"] = sv.AutoGain, ["thermalOffered"] = SensorView.ThermalOffered,
            ["lux"] = float.IsFinite(sv.Lux) ? sv.Lux : null,
            ["moonElevationDeg"] = MathF.Round(sv.MoonElevationDeg, 1), ["moonIllumination"] = MathF.Round(sv.MoonIllumination, 3),
            ["framesProcessed"] = sv.Post.Frames,
        };

        public JsonNode GetSelection()
        {
            var (primary, all) = SelectionOf(ShownWorld, ShownMap);
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

        private int? NetworkIdOrNull(Entity e)
            => e != Entity.Null && ShownMap.TryGetNetworkId(e, out var id) ? id : null;   // ⭐ CE-4571 — an entity of the shown world
    }
}
