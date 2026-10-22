using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fdp.Core;

namespace Fdp.Toolkit.Perception.Survey
{
    /// <summary>The three questions a <c>WorldSurvey</c> sensor asks of the muscle (DA-25). 📄 docs/DESIGN_World_Survey_Sensor.md.</summary>
    public enum WorldSurveyKind : byte
    {
        /// <summary>No question (an empty request).</summary>
        SurveyNone = 0,
        /// <summary>≤ 12 origins × an arc × ≤ 24 rays → each ray's free distance and what stopped it.</summary>
        VisibilityFan = 1,
        /// <summary>Buildings → storeys, faces, openings and the rooms derived from the current walls.</summary>
        BuildingSurvey = 2,
        /// <summary>Points → building, storey, room, indoors, ground and surface heights.</summary>
        PointSurvey = 3,
    }

    /// <summary>⭐ WS-6 — what stopped a ray, as a meaning (R-252: never the trace's descriptive kind).</summary>
    public enum SurveyStop : byte
    {
        /// <summary>Nothing within the range.</summary>
        StopNone = 0,
        /// <summary>Part of an enclosure: a wall, a floor, a closed door.</summary>
        Structure = 1,
        /// <summary>Smoke or gas — it will clear.</summary>
        Obscurant = 2,
        /// <summary>Anything else that blocks: the ground, an open structure.</summary>
        Terrain = 3,
    }

    /// <summary>One origin of a visibility fan: the eye stands <see cref="WorldSurveyRequest.EyeHeight"/> above (X, Y, Z) and looks across
    /// <see cref="Bearing"/> ± <see cref="HalfArc"/> (radians; a bearing is the angle of the direction in the XY plane, 0 = +X, counter-clockwise).</summary>
    public sealed class SurveyOrigin
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Bearing { get; set; }
        public float HalfArc { get; set; } = MathF.PI / 4f;
    }

    /// <summary>A point to place (a PointSurvey).</summary>
    public sealed class SurveyPointQuery
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    /// <summary>
    /// ⭐ <c>CE-3249</c> (WS-2) — what a <c>WorldSurvey</c> sensor asks. Lives on the brain's child sensor and on the solver's carrier; crosses
    /// the wire as <c>SensorConfigPayload {Kind = <see cref="WorldSurveyRequestJson.Kind"/>, Json}</c>. Replace it (never edit it in place) and
    /// call <c>WorldSurveyChildSensor.Configure</c> — that is what starts a new epoch.
    /// </summary>
    // ⭐ CE-3299 D1 — RECORDED (was Transient/NoReplay): sim state a restored run diverges without (restore design §6b, M4).
    [DataPolicy(DataPolicy.NoScenario)]
    [ComponentId(GlobalComponentIds.WorldSurveyRequest)]
    public sealed class WorldSurveyRequest
    {
        /// <summary>The caps (WS-9).</summary>
        public const int MaxOrigins = 12, MaxRays = 24, MaxBuildings = 4, MaxPoints = 16;

        public WorldSurveyKind Kind { get; set; }
        /// <summary>VisibilityFan: the origins (≤ <see cref="MaxOrigins"/>).</summary>
        public List<SurveyOrigin> Origins { get; set; } = new();
        /// <summary>VisibilityFan: rays per origin, spread evenly across its arc (≤ <see cref="MaxRays"/>).</summary>
        public int Rays { get; set; } = 12;
        /// <summary>VisibilityFan: how far a ray looks (m).</summary>
        public float Range { get; set; } = 150f;
        /// <summary>VisibilityFan: the eye above each origin (m).</summary>
        public float EyeHeight { get; set; } = 1.6f;
        /// <summary>BuildingSurvey: the buildings by index (the terrain's building list — the same on every node).</summary>
        public List<int> Buildings { get; set; } = new();
        /// <summary>BuildingSurvey: also every building whose footprint meets this circle (radius 0 = none).</summary>
        public float AreaX { get; set; }
        public float AreaY { get; set; }
        public float AreaRadius { get; set; }
        /// <summary>PointSurvey: the points (≤ <see cref="MaxPoints"/>).</summary>
        public List<SurveyPointQuery> Points { get; set; } = new();
        /// <summary>WS-7 — the solver answers at most this often (s).</summary>
        public float RefreshSeconds { get; set; } = 1f;

        /// <summary>A fan from <paramref name="origins"/>.</summary>
        public static WorldSurveyRequest Fan(IEnumerable<SurveyOrigin> origins, int rays = 12, float range = 150f, float eyeHeight = 1.6f)
            => new() { Kind = WorldSurveyKind.VisibilityFan, Origins = new List<SurveyOrigin>(origins), Rays = rays, Range = range,
                       EyeHeight = eyeHeight, RefreshSeconds = 0.5f };

        /// <summary>A survey of these buildings.</summary>
        public static WorldSurveyRequest OfBuildings(params int[] buildings)
            => new() { Kind = WorldSurveyKind.BuildingSurvey, Buildings = new List<int>(buildings) };

        /// <summary>A survey of every building whose footprint meets the circle (at most <see cref="MaxBuildings"/>).</summary>
        public static WorldSurveyRequest OfArea(Vector2 centre, float radius)
            => new() { Kind = WorldSurveyKind.BuildingSurvey, AreaX = centre.X, AreaY = centre.Y, AreaRadius = radius };

        /// <summary>Place these points.</summary>
        public static WorldSurveyRequest OfPoints(IEnumerable<Vector3> points)
        {
            var r = new WorldSurveyRequest { Kind = WorldSurveyKind.PointSurvey };
            foreach (var p in points) r.Points.Add(new SurveyPointQuery { X = p.X, Y = p.Y, Z = p.Z });
            return r;
        }
    }

    // ── the answer ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One ray of a fan.</summary>
    public sealed class SurveyRay
    {
        public int Origin { get; set; }
        public float Bearing { get; set; }
        /// <summary>How far one sees along it (m) — the range when nothing stopped it.</summary>
        public float Free { get; set; }
        public SurveyStop Stop { get; set; }
        /// <summary>The top of what stopped it (m; 0 when nothing did).</summary>
        public float StopTopZ { get; set; }
    }

    /// <summary>A surveyed building.</summary>
    public sealed class SurveyBuilding
    {
        public int Building { get; set; }
        public string Label { get; set; } = "";
        public bool Solid { get; set; }
        /// <summary>The floor of each level, lowest first (the roof last when it has a floor).</summary>
        public List<float> Levels { get; set; } = new();
        /// <summary>Which level is the roof (−1 = none).</summary>
        public int RoofLevel { get; set; } = -1;
        public float CentreX { get; set; }
        public float CentreY { get; set; }
    }

    /// <summary>A room: <see cref="Index"/> is its index within its building (what a PointSurvey's room names).</summary>
    public sealed class SurveyRoom
    {
        public int Building { get; set; }
        public int Index { get; set; }
        public int Level { get; set; }
        public float FloorZ { get; set; }
        public float CentreX { get; set; }
        public float CentreY { get; set; }
        public float Area { get; set; }
        public bool Indoors { get; set; }
        /// <summary>The four corners (x0, y0, … x3, y3), 0.4 m inside.</summary>
        public float[] Corners { get; set; } = Array.Empty<float>();
    }

    /// <summary>What joins two rooms; a room of −1 is OUTSIDE. Rooms are building-local indices.</summary>
    public sealed class SurveyPortal
    {
        public int Building { get; set; }
        /// <summary>Door · Window · Gap · Stair (<c>BuildingPortalKind</c>).</summary>
        public byte Kind { get; set; }
        public int RoomA { get; set; }
        public int RoomB { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Width { get; set; }
        public float SillZ { get; set; }
        public float HeadZ { get; set; }
        public bool Walkable { get; set; }
        /// <summary>A door's state NOW (open · closed · locked · destroyed; <c>TerrainDoorState</c>); 0 for anything else.</summary>
        public byte DoorState { get; set; }
        public bool IsDoor { get; set; }
        /// <summary>The face it lies on (an index into this answer's faces of that building), −1 inside.</summary>
        public int Face { get; set; } = -1;
        /// <summary>⭐ <c>CE-2257</c> BC-1 (CB-56; behaviors, cross-lane) — a door's terrain-object key (<c>TerrainObjectRef</c>), the handle a
        /// brain commands the door by; "" for anything that is not a door.</summary>
        public string DoorKey { get; set; } = "";
    }

    /// <summary>An outside face of a building.</summary>
    public sealed class SurveyFace
    {
        public int Building { get; set; }
        public int Level { get; set; }
        public float AX { get; set; }
        public float AY { get; set; }
        public float BX { get; set; }
        public float BY { get; set; }
        public float OutX { get; set; }
        public float OutY { get; set; }
        public float BaseZ { get; set; }
        public float TopZ { get; set; }
    }

    /// <summary>A placed point.</summary>
    public sealed class SurveyPoint
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        /// <summary>The building it is in (−1 = none).</summary>
        public int Building { get; set; } = -1;
        public int Level { get; set; } = -1;
        /// <summary>The building-local room (−1 = none: outside, in a wall, over a stairwell).</summary>
        public int Room { get; set; } = -1;
        public bool Indoors { get; set; }
        public float GroundZ { get; set; }
        public float SurfaceZ { get; set; }
        /// <summary>The building's roof (0 when outside every building).</summary>
        public float RoofZ { get; set; }
    }

    /// <summary>⭐ <c>CE-3249</c> — one answer of a survey: what the muscle found, as plain data (it crosses the wire as JSON, WS-11).</summary>
    public sealed class WorldSurveyPayload
    {
        public WorldSurveyKind Kind { get; set; }
        /// <summary>A cap of WS-9 cut the answer short.</summary>
        public bool Truncated { get; set; }
        public List<SurveyRay> Rays { get; set; } = new();
        public List<SurveyBuilding> Buildings { get; set; } = new();
        public List<SurveyRoom> Rooms { get; set; } = new();
        public List<SurveyPortal> Portals { get; set; } = new();
        public List<SurveyFace> Faces { get; set; } = new();
        public List<SurveyPoint> Points { get; set; } = new();
    }

    /// <summary>⭐ <c>CE-3249</c> — the brain's view of a survey sensor's answer (the "cognitive buffer" header, unmanaged): when it was
    /// last answered and whether the content moved. The answer itself is <see cref="WorldSurveyAnswer"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.WorldSurveyStatus)]
    public struct WorldSurveyStatus
    {
        /// <summary>The solver's tick of the answer held (0 = none yet for this epoch).</summary>
        public uint LastUpdateTick;
        /// <summary>The brain's sim time when it was applied (s).</summary>
        public float LastUpdateTimeSeconds;
        /// <summary>Bumped each time the CONTENT changed (WS-10).</summary>
        public uint Version;
        /// <summary>The content hash of the answer held.</summary>
        public ulong ContentHash;
        public readonly bool IsReady => LastUpdateTick > 0;
    }

    /// <summary>⭐ <c>CE-3249</c> — the answer a survey sensor holds on the brain (managed: rooms and portals do not fit a fixed buffer).</summary>
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    [ComponentId(GlobalComponentIds.WorldSurveyAnswer)]
    public sealed class WorldSurveyAnswer
    {
        public WorldSurveyPayload? Payload { get; set; }
    }

    /// <summary>⭐ <c>CE-3249</c> (WS-2, WS-11) — the request and the answer as JSON (the request in <c>SensorConfigPayload.Json</c>, the answer on
    /// its topic), and the content hash that decides "changed".</summary>
    public static class WorldSurveyRequestJson
    {
        /// <summary>The <c>SensorConfigPayload.Kind</c> / <c>ConfigKind</c> of a survey request.</summary>
        public const string Kind = "WorldSurvey";

        // ⭐ CE-3187 (R-2005) — the one shared options instance (OneSharedJsonOptionsTests); both ends of the survey wire use this codec
        private static readonly JsonSerializerOptions s_options = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;

        public static string Encode(WorldSurveyRequest request) => JsonSerializer.Serialize(request, s_options);

        public static bool TryDecode(string? json, out WorldSurveyRequest? request)
        {
            request = null;
            if (string.IsNullOrEmpty(json)) return false;
            try { request = JsonSerializer.Deserialize<WorldSurveyRequest>(json, s_options); return request != null; }
            catch (JsonException) { return false; }
        }

        public static string EncodeAnswer(WorldSurveyPayload payload) => JsonSerializer.Serialize(payload, s_options);

        public static WorldSurveyPayload? DecodeAnswer(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonSerializer.Deserialize<WorldSurveyPayload>(json, s_options); }
            catch (JsonException) { return null; }
        }

        /// <summary>FNV-1a 64 of the answer's JSON — the same content gives the same hash on every node.</summary>
        public static ulong Hash(string json)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in json) { h ^= c; h *= 1099511628211UL; }
            return h == 0 ? 1 : h;
        }
    }
}
