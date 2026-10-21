using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>
/// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §2, §4) — WHEN a shot starts: a sim time (<c>"at": 12</c>), or an anchor of the take plus
/// an offset (<c>"fire"</c>, <c>"fire-0.3"</c>, <c>"door+5"</c>). An anchor's time is baked by the scout and verified live (§0, R-3038).
/// </summary>
public readonly record struct AtExpr(double Offset, string? Anchor = null)
{
    private static readonly Regex Shape = new(@"^(?<name>.+?)(?<off>[+-]\d+(\.\d+)?)?$", RegexOptions.CultureInvariant);

    /// <summary>A plain sim time.</summary>
    public bool IsFixed => Anchor is null;

    /// <summary>The time, given each anchor's; null when the anchor's time is not known.</summary>
    public double? Resolve(Func<string, double?> anchorTime) => Anchor is null ? Offset : anchorTime(Anchor) is { } t ? t + Offset : null;

    /// <summary>A number or <c>"name[+-offset]"</c>; throws <see cref="FormatException"/> on anything else.</summary>
    public static AtExpr Parse(JsonNode? node)
    {
        if (node is null) throw new FormatException("no time");
        if (node is JsonValue v && v.TryGetValue<double>(out var seconds)) return new AtExpr(seconds);
        var text = node.ToString().Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) return new AtExpr(seconds);
        var m = Shape.Match(text);
        if (text.Length == 0 || !m.Success) throw new FormatException($"'{text}' is neither a time nor an anchor");
        double off = m.Groups["off"].Success ? double.Parse(m.Groups["off"].Value, NumberStyles.Float, CultureInfo.InvariantCulture) : 0d;
        return new AtExpr(off, m.Groups["name"].Value);
    }

    public JsonNode ToJson() => Anchor is null
        ? JsonValue.Create(Offset)!
        : JsonValue.Create(Offset == 0 ? Anchor : Anchor + (Offset > 0 ? "+" : "") + Offset.ToString(CultureInfo.InvariantCulture))!;

    public override string ToString() => ToJson().ToString();
}

/// <summary>How a shot's <c>to</c> is reached.</summary>
public enum Ease : byte { Linear, In, Out, InOut }

/// <summary>
/// ⭐ <c>CE-4038</c> (§2) — the camera of a shot, in <c>film.py</c>'s plan keys: exactly one of <see cref="Follow"/> · <see cref="Over"/> +
/// <see cref="Toward"/> · <see cref="LookAt"/> · <see cref="Eye"/> + <see cref="Target"/>. Units are names (or network ids as text). Angles in
/// DEGREES (yaw from east counter-clockwise, the direction the camera LOOKS; pitch negative = down); the defaults are film.py's.
/// </summary>
public sealed record CameraSpec
{
    public string? Follow { get; init; }
    public string? Over { get; init; }
    public string? Toward { get; init; }
    public Vector3? LookAt { get; init; }
    public Vector3? Eye { get; init; }
    public Vector3? Target { get; init; }
    public float Distance { get; init; } = 30f;
    public float Yaw { get; init; }
    public float Pitch { get; init; } = -20f;
    /// <summary>Degrees per sim second the yaw turns.</summary>
    public float Orbit { get; init; }
    public float Lift { get; init; } = 1.5f;
    public float Back { get; init; } = 20f;
    public float Height { get; init; } = 6f;
    /// <summary>The share of the way to <see cref="Toward"/> the over-the-shoulder camera looks at.</summary>
    public float Ahead { get; init; } = 0.35f;
    public float? Fov { get; init; }

    /// <summary>How many camera kinds it names (a valid camera names exactly one).</summary>
    public int Kinds => (Follow != null ? 1 : 0) + (Over != null ? 1 : 0) + (LookAt != null ? 1 : 0) + (Eye != null || Target != null ? 1 : 0);

    /// <summary>The units it names (to validate against the world).</summary>
    public IEnumerable<string> Names()
    {
        if (Follow != null) yield return Follow;
        if (Over != null) yield return Over;
        if (Toward != null) yield return Toward;
    }
}

/// <summary>A layer change: turn these on, these off (§4b; names as the Layers panel names them).</summary>
public sealed record LayerChange(IReadOnlyList<string> On, IReadOnlyList<string> Off)
{
    public static readonly LayerChange None = new(Array.Empty<string>(), Array.Empty<string>());
}

/// <summary>
/// ⭐ <c>CE-4038</c> (§2) — one shot: from <see cref="At"/>, <see cref="Seconds"/> of SIM time held on <see cref="Camera"/>, easing to
/// <see cref="To"/> (the numeric camera keys reached at its end). <see cref="Speed"/> is sim seconds per film second (0.5 = slow motion).
/// </summary>
public sealed record ShotSpec
{
    public AtExpr At { get; init; }
    public double Seconds { get; init; } = 3d;
    public CameraSpec Camera { get; init; } = new();
    public IReadOnlyDictionary<string, float>? To { get; init; }
    public Ease Ease { get; init; } = Ease.InOut;
    public double Speed { get; init; } = 1d;
    /// <summary>A replay shot (§3c) — ⛔ skipped in slice 1 (R-4061), named in the report.</summary>
    public JsonNode? Replay { get; init; }
    public string? Caption { get; init; }
    public LayerChange? Layers { get; init; }
    public bool? Sound { get; init; }
    /// <summary>The director's note; read by no tool.</summary>
    public string? Why { get; init; }
}

/// <summary>
/// ⭐ <c>CE-4038</c> (§2, D-3) — a beat the scout looks for: <see cref="On"/> is the cue (<c>shot</c> · <c>blast</c> · <c>hit</c> · <c>near</c> ·
/// <c>task</c> · <c>side</c> · <c>health</c> · <c>embark</c> · <c>gesture</c>), the rest its parameters. <see cref="Who"/> is a name, or
/// <c>"*part"</c> for any whose name contains <c>part</c>.
/// </summary>
public sealed record AnchorSpec
{
    public string On { get; init; } = "";
    public string? Who { get; init; }
    public string? By { get; init; }
    public string? Target { get; init; }
    public JsonNode? Is { get; init; }
    public int N { get; init; } = 1;
    public Vector2? Point { get; init; }
    public float Within { get; init; } = 3f;
    public float Below { get; init; }

    /// <summary>The cues the scout can see (an anchor naming another is refused).</summary>
    public static readonly IReadOnlyList<string> Kinds = new[] { "shot", "blast", "hit", "near", "task", "side", "health", "embark", "gesture" };

    public IEnumerable<string> Names()
    {
        if (Who != null) yield return Who;
        if (By != null) yield return By;
        if (Target != null) yield return Target;
    }
}

/// <summary>
/// ⭐ <c>CE-4038</c> (§0, R-3038) — the anchor times one scout run found, and what that run was: its step rate, the scenario file's hash and
/// the build. ⭐ A take keeps ONE step rate; a change of rate, scenario or build makes the bake stale (re-scout).
/// </summary>
public sealed record BakedAnchors
{
    public IReadOnlyDictionary<string, double> Times { get; init; } = new Dictionary<string, double>(StringComparer.Ordinal);
    public int TickHz { get; init; } = 60;
    public string ScenarioHash { get; init; } = "";
    public string BuildSha { get; init; } = "";
    /// <summary>The anchors the scout looked for and did not find.</summary>
    public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();

    public bool IsStaleFor(string scenarioHash, string buildSha, int tickHz) =>
        tickHz != TickHz || !string.Equals(scenarioHash, ScenarioHash, StringComparison.Ordinal)
                         || (buildSha.Length > 0 && !string.Equals(buildSha, BuildSha, StringComparison.Ordinal));
}

/// <summary>
/// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §2, §4) — one scenario's camera script: <c>scenarios/&lt;scenario&gt;/takes/&lt;name&gt;.take.json</c>.
/// </summary>
public sealed record Take
{
    public string Scenario { get; init; } = "";
    public string Name { get; init; } = "main";
    public string? Title { get; init; }
    public string? Caption { get; init; }
    /// <summary>The fixed step rate (60, or 20 / 15 when 60 cannot keep up in real time — R-4063).</summary>
    public int TickHz { get; init; } = 60;
    public AtExpr? EndAt { get; init; }
    public IReadOnlyDictionary<string, AnchorSpec> Anchors { get; init; } = new Dictionary<string, AnchorSpec>(StringComparer.Ordinal);
    public BakedAnchors? Baked { get; init; }
    public IReadOnlyList<ShotSpec> Shots { get; init; } = Array.Empty<ShotSpec>();
    public LayerChange? Layers { get; init; }
    public bool? Sound { get; init; }
    /// <summary>Keys the director does not read but keeps (the clip id, its chapter, its state, what it needs).</summary>
    public JsonObject? Extra { get; init; }
}

/// <summary>One item of a reel: a scenario's take, optionally a part of it (<see cref="From"/> / <see cref="To"/>).</summary>
public sealed record ReelItem
{
    public string Scenario { get; init; } = "";
    public string Take { get; init; } = "main";
    public string? Title { get; init; }
    public string? ChapterCard { get; init; }
    public string? Clip { get; init; }
    public AtExpr? From { get; init; }
    public AtExpr? To { get; init; }
}

/// <summary>⭐ <c>CE-4038</c> (§4a, D-15) — a playlist of takes, and on film ONE movie: <c>films/&lt;name&gt;.reel.json</c>.</summary>
public sealed record Reel
{
    public string Name { get; init; } = "";
    public string? Title { get; init; }
    public string Transition { get; init; } = "fade";
    public IReadOnlyList<ReelItem> Items { get; init; } = Array.Empty<ReelItem>();
    public bool Loop { get; init; }
    public double GapSeconds { get; init; } = 1.5d;

    /// <summary>A reel of one take (<c>POST /director/play {scenario, take}</c>).</summary>
    public static Reel Of(string scenario, string take) => new()
    {
        Name = scenario + "/" + take,
        Items = new[] { new ReelItem { Scenario = scenario, Take = take } },
    };
}
