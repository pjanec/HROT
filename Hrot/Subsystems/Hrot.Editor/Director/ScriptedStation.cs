using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Behavior.Possession;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Editor.Director
{
    /// <summary>
    /// ⭐⭐⭐ Simulated devices S1e-2 (docs/designs/devices/DESIGN_Simulated_Devices.md §5a, §3.4 D-38 / D-43, §3.6 D-38) — THE SCRIPTED TRAINEE's station:
    /// what a scenario's <c>do: possess / view / control</c> rows drive, so a feature-test scenario can operate a device with no human (⛔ before, possession
    /// was proven over HTTP only — <c>scenarios/showcases/player-station</c>). It is a STATION in every sense the device code sees: ① it possesses through the
    /// one possess path (the host's <c>POST /entities/{id}/possess</c> service — the unit's row names this node's station, the device walk then writes each
    /// device's <c>Operated</c> row, R7-1); ② its view is bound to one of the devices those rows name (<see cref="OperatedDevices"/>, D-38) or the eye;
    /// ③ ⭐ SD-S5v (D-146) — it is the SCRIPT SOURCE of every device it operates: through <see cref="DeviceInputEncoder"/> — the SAME encoder the keyboard /
    /// mouse station and its virtual panel write through — it sends each device only what changed (sparse <see cref="DeviceInputEvent"/>s): the LOOK
    /// relative to each mount (aimed at a unit or a point — the device's <c>DevicePose</c> follows, R5-13), <c>Viewed</c> on the bound one, and its elements
    /// pressed by their TKB NAME (<see cref="DeviceDefDto.Elements"/>, D-142 — a click on the HUD makes the same bytes) and its axes.
    /// Main thread, on the host's brain world (the debug service's).
    /// </summary>
    public sealed class ScriptedStation : IVerifyStation
    {
        /// <summary>The height a unit is aimed at above its reference point (m) — a man's centre.</summary>
        public const float AimHeight = 1.0f;

        private readonly Func<EntityRepository?> _world;
        private readonly Func<int, string?> _possess;
        // ⭐ SD-S5v — the script source: only changes are sent, so the device's input never depends on how many frames a sim instant had (S1e-3, dv-replay-hud)
        private readonly DeviceInputEncoder _encoder = new(DeviceInputSource.Script, MaxDevices);
        private readonly Entity[] _sources = new Entity[MaxDevices];
        private readonly float[] _axes = new float[DeviceAxes.Count];
        private int _axesSet;   // bit k: the script set axis k
        private double _axesUntil = double.PositiveInfinity;
        private int _body;            // the possessed unit's network id (0 = none)
        private long _bound;          // the view's device (network id, 0 = the eye)
        private int _aimAt;           // aim at this unit (network id) …
        private Vector3? _aimPoint;   // … or at this point

        public const int MaxDevices = 8;

        /// <param name="world">the brain world (where the device tick runs).</param>
        /// <param name="possess">the host's possess service (null when accepted, else why not).</param>
        public ScriptedStation(Func<EntityRepository?> world, Func<int, string?> possess)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _possess = possess ?? throw new ArgumentNullException(nameof(possess));
        }

        /// <summary>Input events sent (rails).</summary>
        public long FramesSent => _encoder.Sent;

        /// <summary>⭐ SD-S5v — the script source's encoder (a rail reads it; the <c>do: click</c> row's adapter writes through it).</summary>
        public DeviceInputEncoder Encoder => _encoder;

        /// <summary>⭐ SD-S5v — the bound device's entity (null when the view shows the eye).</summary>
        public Entity BoundDevice => _world() is { } w ? BoundEntity(w) : Entity.Null;

        /// <summary>The bound device's network id (0 = the plain eye).</summary>
        public long BoundSource => _bound;

        public string? Possess(int networkId)
        {
            var why = _possess(networkId);
            if (why != null) return why;
            _body = networkId;
            _bound = 0;
            return null;
        }

        public string? View(long deviceNetworkId)
        {
            if (_body == 0) return "no unit is possessed at this station (do: possess first)";
            if (deviceNetworkId == 0) { _bound = 0; return null; }
            if (_world() is not { } w) return "no world";
            int n = Sources(w, out _);
            for (int i = 0; i < n; i++)
                if (OperatedDevices.NetIdOf(w, _sources[i]) == deviceNetworkId) { _bound = deviceNetworkId; return null; }
            return $"device {deviceNetworkId} is not operated by this station's man (he carries / mans / sits at none such)";
        }

        public string? Control(StationAction action, double rel)
        {
            if (_body == 0) return "no unit is possessed at this station (do: possess first)";
            if (_world() is not { } w) return "no world";
            if (action.AimAt is { } who)
            {
                int id = IdOf(w, who);
                if (id == 0) return $"no unit '{who}' to aim at";
                _aimAt = id; _aimPoint = null;
            }
            else if (action.AimPoint is { } p) { _aimPoint = p; _aimAt = 0; }
            if (action.Press.Count == 0 && action.Axes.Count == 0) return null;
            var bound = BoundEntity(w);
            if (bound.IsNull) return "the view shows no device (do: view first): a press reaches only the device the view shows";
            if (action.Press.Count > 0)
            {
                var type = TypeOf(w, bound);
                foreach (var name in action.Press)
                    if (_encoder.Press(bound, type, name, CurrentSwitch(w, bound, type, name)) is { } why) return why;   // ⭐ D-146 — the one name → element lookup
            }
            foreach (var (axis, value) in action.Axes) { _axes[axis] = value; _axesSet |= 1 << axis; }
            if (action.Axes.Count > 0) _axesUntil = action.For > 0d ? rel + action.For : double.PositiveInfinity;
            return null;
        }

        public void Frame(double rel)
        {
            if (_body == 0 || _world() is not { } w) return;
            int n = Sources(w, out var station);
            if (n == 0) return;
            if (rel + 1e-9 >= _axesUntil) { Array.Clear(_axes); _axesUntil = double.PositiveInfinity; }
            var bound = BoundEntity(w);
            if (!_bound.Equals(0L) && bound.IsNull) _bound = 0;   // dropped, taken, handed over: back to the eye (as the station does)
            var look = LookFrom(w, bound.IsNull ? _sources[0] : bound);
            for (int i = 0; i < n; i++)
            {
                _encoder.SetWorldLook(w, _sources[i], look);
                _encoder.SetViewed(_sources[i], _sources[i] == bound);
            }
            if (!bound.IsNull)
                for (int k = 0; k < DeviceAxes.Count; k++) if ((_axesSet & (1 << k)) != 0) _encoder.SetAxis(bound, k, _axes[k]);
            _encoder.Flush(w, station, _sources.AsSpan(0, n));
        }

        /// <summary>A switch element's NEXT position as the device holds it now (its merged input) — a press steps it; −1 for any other element.</summary>
        private static int CurrentSwitch(EntityRepository w, Entity device, DeviceDefDto? type, string name)
        {
            if (!DeviceInputEncoder.Encode(type, name, out var kind, out int index) || kind != DeviceElementKind.Switch) return -1;
            int now = w.IsComponentTypeRegistered<DeviceInput>() && w.HasComponent<DeviceInput>(device) ? w.GetComponentRO<DeviceInput>(device).Control.Switches[index] : 0;
            return (now + 1) % Math.Max(1, type!.ElementAt(kind, index)!.Positions);
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────

        private int Sources(EntityRepository w, out StationId station)
        {
            station = default;
            if (!w.HasSingletonManaged<NetworkEntityMap>() || !w.GetSingletonManaged<NetworkEntityMap>()!.TryGetEntity(_body, out var body)) return 0;
            if (!w.IsAlive(body) || !w.IsComponentTypeRegistered<PossessedBy>() || !w.HasComponent<PossessedBy>(body)) return 0;
            ref readonly var row = ref w.GetComponentRO<PossessedBy>(body);
            if (!StationBody.IsBodyMode(row.Mode)) return 0;
            station = row.StationNodeId;
            return OperatedDevices.Of(w, station, _sources);
        }

        private Entity BoundEntity(EntityRepository w)
        {
            if (_bound == 0) return Entity.Null;
            int n = Sources(w, out _);
            for (int i = 0; i < n; i++) if (OperatedDevices.NetIdOf(w, _sources[i]) == _bound) return _sources[i];
            return Entity.Null;
        }

        /// <summary>The look from the device's world position at the aim (a unit's centre, or a point); the possessed man's heading when nothing is aimed.</summary>
        private Quaternion LookFrom(EntityRepository w, Entity device)
        {
            DevicePoses.TryWorld(w, device, out var from, out _);
            Vector3? to = _aimPoint;
            if (to is null && _aimAt != 0 && w.HasSingletonManaged<NetworkEntityMap>() && w.GetSingletonManaged<NetworkEntityMap>()!.TryGetEntity(_aimAt, out var t)
                && w.IsAlive(t) && w.HasComponent<SimTransform>(t))
                to = w.GetComponentRO<SimTransform>(t).Position + new Vector3(0f, 0f, AimHeight);
            if (to is { } target && (target - from).LengthSquared() > 1e-6f)
            {
                var d = target - from;
                return DeviceInputEncoder.Look(MathF.Atan2(d.Y, d.X), MathF.Atan2(d.Z, MathF.Sqrt(d.X * d.X + d.Y * d.Y)));
            }
            if (w.HasSingletonManaged<NetworkEntityMap>() && w.GetSingletonManaged<NetworkEntityMap>()!.TryGetEntity(_body, out var body) && w.HasComponent<SimTransform>(body))
            {
                var f = Vector3.Transform(Vector3.UnitX, (Quaternion)w.GetComponentRO<SimTransform>(body).Rotation);
                return DeviceInputEncoder.Look(MathF.Atan2(f.Y, f.X), 0f);
            }
            return Quaternion.Identity;
        }

        private static int IdOf(EntityRepository w, string name)
        {
            foreach (var (id, n) in VerifyWorldNames.Named(w)) if (string.Equals(n, name, StringComparison.Ordinal)) return id;
            return 0;
        }

        /// <summary>The device's TKB type (its <c>DeviceInfo.DeviceType</c>) — what names its elements (D-142).</summary>
        public static DeviceDefDto? TypeOf(EntityRepository w, Entity device)
        {
            if (!w.IsComponentTypeRegistered<DeviceInfo>() || !w.HasComponent<DeviceInfo>(device) || !w.HasSingletonManaged<ITkbDatabase>()) return null;
            var db = w.GetSingletonManaged<ITkbDatabase>();
            return db != null && db.TryGetByType(w.GetComponentRO<DeviceInfo>(device).DeviceType, out var t) && t != null ? t.GetDescriptor<DeviceDefDto>() : null;
        }
    }

    /// <summary>
    /// ⭐⭐ S1e-2 (DESIGN_Simulated_Devices §5a, the <c>field</c> cue's <c>memory</c>) — a unit's <c>[UnitMemory]</c> struct read BY NAME as JSON, for the judge:
    /// the type is found once among the loaded assemblies (a value type carrying <see cref="UnitMemoryAttribute"/>), read with <see cref="UnitMemory.Get{T}"/>
    /// (a read creates nothing), and written field by field — an enum by name, an entity as the NAME of the unit it holds (null when none), a fixed string
    /// as its text, a nested struct as an object. ⚠ Reflection: a judge's read per sample, never the sim's hot path.
    /// </summary>
    public static class UnitMemoryJson
    {
        private static readonly Dictionary<string, Type?> s_types = new(StringComparer.Ordinal);
        private static readonly MethodInfo s_get = typeof(UnitMemory).GetMethod(nameof(UnitMemory.Get))!;

        /// <summary>The <c>[UnitMemory]</c> struct named <paramref name="name"/>, or null.</summary>
        public static Type? TypeOf(string name)
        {
            lock (s_types)
            {
                if (s_types.TryGetValue(name, out var known)) return known;
                Type? found = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = Array.FindAll(ex.Types, t => t != null)!; }
                    foreach (var t in types)
                        if (t.IsValueType && t.Name == name && t.GetCustomAttribute<UnitMemoryAttribute>() != null) { found = t; break; }
                    if (found != null) break;
                }
                return s_types[name] = found;
            }
        }

        /// <summary>The struct on <paramref name="unit"/> as JSON, or null when the type is unknown.</summary>
        public static JsonObject? Read(EntityRepository w, Entity unit, string typeName)
        {
            if (TypeOf(typeName) is not { } t || unit.IsNull || !w.IsAlive(unit)) return null;
            object value = s_get.MakeGenericMethod(t).Invoke(null, new object[] { w, unit })!;
            return (JsonObject)ToJson(w, value)!;
        }

        private static JsonNode? ToJson(EntityRepository w, object? v)
        {
            switch (v)
            {
                case null: return null;
                case Enum e: return JsonValue.Create(e.ToString());
                case Entity ent:
                    return !ent.IsNull && w.IsAlive(ent) && w.IsComponentTypeRegistered<EntityInfo>() && w.HasComponent<EntityInfo>(ent)
                        ? JsonValue.Create(w.GetComponentRO<EntityInfo>(ent).Name.ToString()) : null;
                case bool b: return JsonValue.Create(b);
                case byte or sbyte or short or ushort or int or uint or long or ulong or float or double:
                    return JsonValue.Create(Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture));
                case Fdp.Core.FixedString32 f32: return JsonValue.Create(f32.ToString());
                case Fdp.Core.FixedString64 f64: return JsonValue.Create(f64.ToString());
            }
            var type = v.GetType();
            if (!type.IsValueType || type.IsPrimitive) return JsonValue.Create(v.ToString());
            var o = new JsonObject();
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) o[f.Name] = ToJson(w, f.GetValue(v));
            return o;
        }
    }

    /// <summary>
    /// ⭐⭐ S1e-2 (DESIGN_Simulated_Devices §5a, the <c>hud</c> cue) — a frame's primitives as a view BOUND to <paramref name="source"/> draws them
    /// (<see cref="HudScopes.Keep"/>, the one run reader): the texts it shows, and the size of the source's run (what the bound view keeps beyond what an
    /// unbound one does). The judge's read — never a drawer.
    /// </summary>
    public static class HudRuns
    {
        public static int Read(ReadOnlySpan<DebugPrimitive> frame, long source, List<string> texts, ref DebugPrimitive[] bound, ref DebugPrimitive[] unbound)
        {
            var kept = HudScopes.Keep(frame, source, ref bound);
            int plain = HudScopes.Unscoped(frame, ref unbound).Length;
            for (int i = 0; i < kept.Length; i++)
                if (kept[i].Shape == DebugPrimitiveShape.Text) texts.Add(kept[i].TextContent.ToString());
            return kept.Length - plain;
        }
    }
}
