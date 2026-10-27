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

/// <summary>⭐ <c>TE-8</c> (docs/DESIGN_Director_Timeline_Editor.md §0, §5) — how the camera gets TO a key: a hard cut, a smooth eased fly, or a dip to black and back.</summary>
public enum KeyTransitionKind : byte { Cut, Fly, Dip }

/// <summary>
/// ⭐ <c>TE-8</c> — a key's transition IN. <see cref="Cut"/> has no duration; a fly has <see cref="Seconds"/> and an <see cref="Ease"/>; a dip goes
/// to black for <see cref="OutSeconds"/> BEFORE the key's time and comes back for <see cref="InSeconds"/> AFTER it.
/// </summary>
public sealed record KeyTransition(KeyTransitionKind Kind, double Seconds = 0d, Ease Ease = Ease.InOut, double OutSeconds = 0d, double InSeconds = 0d)
{
    public static readonly KeyTransition Cut = new(KeyTransitionKind.Cut);

    public static KeyTransition Fly(double seconds, Ease ease = Ease.InOut) => new(KeyTransitionKind.Fly, Seconds: seconds, Ease: ease);

    public static KeyTransition Dip(double outSeconds, double inSeconds) => new(KeyTransitionKind.Dip, OutSeconds: outSeconds, InSeconds: inSeconds);
}

/// <summary>
/// ⭐ <c>TE-2</c> (docs/DESIGN_Director_Timeline_Editor.md §0, §5) — THE camera key, the unit of a take's camera: from <see cref="At"/> the
/// <see cref="Camera"/> is on screen, reached by <see cref="In"/>. <see cref="Fov"/> is the lens (it is also <c>Camera.Fov</c>; the two are kept
/// equal by the reader and <see cref="CameraKeys"/>), <see cref="Still"/> names the PNG a regression run writes at this key (TE-10).
/// </summary>
public sealed record CameraKey
{
    public AtExpr At { get; init; }
    public CameraSpec Camera { get; init; } = new();
    public KeyTransition In { get; init; } = KeyTransition.Cut;
    public float? Fov { get; init; }
    public string? Still { get; init; }
    /// <summary>
    /// ⭐ <c>CE-4059</c> / CR-7 (docs/DESIGN_Director_Timeline_Editor.md §11q) — the <see cref="TimeSegment.Id"/> of the ONE segment this key belongs to (null = every
    /// pass, as before). A scoped key is placed at its real SOURCE time and is looked up only while its segment plays (<see cref="CameraKeys.OnScreenIn"/>),
    /// so a replay's reverse angle can sit inside the stretch the main pass also plays without cutting it.
    /// </summary>
    public string? Segment { get; init; }
}

/// <summary>⭐ <c>TE-2</c> — what the film shows where a <see cref="TimeSegment"/> ends and the next begins: a hard cut or a dip to black.</summary>
public enum SegmentGap : byte { Cut, Dip }

/// <summary>
/// ⭐ <c>TE-2</c> (§5) — a stretch of SIM time <see cref="From"/>…<see cref="To"/> the film keeps, at <see cref="Speed"/> sim seconds per film
/// second; what lies between two segments is skipped (<see cref="Gap"/> says how the film joins them).
/// </summary>
public sealed record TimeSegment
{
    /// <summary>⭐ <c>CE-4059</c> / CR-7 (§11q) — the name a scoped key uses (<see cref="CameraKey.Segment"/>); null = no key can name it. Stable under a reorder
    /// or an insert (a list position is not); unique within the take (the reader names a duplicate).</summary>
    public string? Id { get; init; }
    public AtExpr From { get; init; }
    public AtExpr To { get; init; }
    public double Speed { get; init; } = 1d;
    public SegmentGap Gap { get; init; } = SegmentGap.Cut;
}

/// <summary>⭐ <c>CE-1303</c> P2b — CR-11 (§11o) — where a subtitle sits on the picture.</summary>
public enum SubtitlePosition : byte { Bottom, Top, Middle }

/// <summary>
/// ⭐ <c>CE-1303</c> P2b — CR-11 (docs/DESIGN_Director_Timeline_Editor.md §5, §11o S7) — one subtitle: <see cref="Text"/> on screen from <see cref="At"/>
/// until <see cref="Until"/>, both in SOURCE sim seconds like a key (a time, or an anchor + offset — so it stays on its event when the run's timing
/// changes), at <see cref="Position"/>, fading in and out over <see cref="Fade"/> sim seconds (0 = a hard cut).
/// </summary>
public sealed record Subtitle
{
    public AtExpr At { get; init; }
    public AtExpr Until { get; init; }
    public string Text { get; init; } = "";
    public SubtitlePosition Position { get; init; } = SubtitlePosition.Bottom;
    public double Fade { get; init; }
}

/// <summary>⭐ CR-11 — the subtitle on screen now: its text, where, and how opaque (1 = fully, while fading less).</summary>
public sealed record SubtitleFrame(string Text, SubtitlePosition Position, float Alpha);

/// <summary>
/// ⭐ <c>CE-1370</c> — WHERE a subtitle's dark band sits and HOW opaque it is: the ONE computation behind the cinema overlay on screen
/// (<c>WindowCinema</c>) and the caption burned into a captured frame (<c>FilmText.Caption</c>), so a rendered film shows what the screen showed.
/// The band is as wide as the picture; the text is <see cref="TextSize"/> tall, with half a text-size of padding above and below.
/// </summary>
public readonly record struct SubtitleBand(float TextSize, float Top, float Height, byte BandAlpha, byte TextAlpha)
{
    /// <summary>The band's own opacity at a fully opaque subtitle (of 255).</summary>
    public const int BandOpacity = 150;

    /// <param name="sub">The subtitle (its position and fade-scaled alpha).</param>
    /// <param name="frameHeight">The picture's height in pixels.</param>
    /// <param name="textHeight">The wrapped text's height in pixels, as the caller's font measures it at <see cref="TextSizeFor"/>.</param>
    public static SubtitleBand Of(SubtitleFrame sub, float frameHeight, float textHeight)
    {
        float s = TextSizeFor(frameHeight);
        float height = textHeight + s;
        float top = sub.Position switch
        {
            SubtitlePosition.Top => frameHeight / 18f,
            SubtitlePosition.Middle => (frameHeight - height) / 2f,
            _ => frameHeight - height - frameHeight / 18f,
        };
        byte a = (byte)Math.Round(Math.Clamp(sub.Alpha, 0f, 1f) * 255f);
        return new SubtitleBand(s, top, height, (byte)Math.Round(a * (double)BandOpacity / 255d), a);
    }

    /// <summary>The text size for a picture of this height (the wrapped text must be measured at it).</summary>
    public static float TextSizeFor(float frameHeight) => MathF.Max(14f, frameHeight / 30f);
}

/// <summary>
/// ⭐ <c>CE-1303</c> P2b — CR-11 (§11o S8) — a take's subtitles, however it was written: its own <see cref="Take.Subtitles"/> when it has any, else its
/// CAPTIONS READ AS subtitles — each laid-out shot's <c>caption ?? take.caption</c> over the shot's stretch, equal neighbours merged — which is exactly
/// what the runner showed before subtitles existed (the TE-2 pattern of <see cref="CameraKeys.Of"/>). ⛔ A shot's <c>why</c> is NOT read: it is the
/// director's note (the Subtitles track offers it explicitly).
/// </summary>
public static class TakeSubtitles
{
    public static IReadOnlyList<Subtitle> Of(Take take, TakeTimeline laidOut)
    {
        if (take.Subtitles.Count > 0) return take.Subtitles;
        var list = new List<Subtitle>();
        foreach (var shot in laidOut.Shots)
        {
            var text = shot.Shot.Caption ?? take.Caption;
            if (string.IsNullOrWhiteSpace(text) || !(shot.End > shot.Start)) continue;
            if (list.Count > 0 && list[^1].Text == text && Math.Abs(list[^1].Until.Offset - shot.Start) < 1e-9)
                list[^1] = list[^1] with { Until = new AtExpr(shot.End) };
            else list.Add(new Subtitle { At = new AtExpr(shot.Start), Until = new AtExpr(shot.End), Text = text! });
        }
        return list;
    }

    /// <summary>The subtitle on screen at SOURCE second <paramref name="sourceSim"/>: the LAST listed whose [at, until) holds it (null: none), its
    /// alpha rising over <see cref="Subtitle.Fade"/> after its start and falling over it before its end.</summary>
    public static SubtitleFrame? At(IReadOnlyList<Subtitle> subtitles, Func<string, double?> anchorTime, double sourceSim)
    {
        for (int i = subtitles.Count - 1; i >= 0; i--)
        {
            var s = subtitles[i];
            if (s.At.Resolve(anchorTime) is not { } from || s.Until.Resolve(anchorTime) is not { } until) continue;
            if (sourceSim < from - 1e-9 || sourceSim >= until - 1e-9 || string.IsNullOrEmpty(s.Text)) continue;
            return new SubtitleFrame(s.Text, s.Position, FilmFade.Alpha(from, until, s.Fade, sourceSim));   // ⭐ CE-1305 — the one fade rule (graphics too)
        }
        return null;
    }
}

/// <summary>
/// ⭐ <c>TE-2</c> — a take's keys, however it was written: its <see cref="Take.Keys"/> when it has any (format v2), else its <see cref="Take.Shots"/>
/// READ AS keys — each shot a CUT key at its <c>at</c>; a shot with a <c>to</c> also a FLY key at its end carrying the <c>to</c> values, reached
/// in the shot's seconds with its ease. A replay shot has no key (as <see cref="TakeTimeline.Plan"/> skips it).
/// </summary>
public static class CameraKeys
{
    public static IReadOnlyList<CameraKey> Of(Take take)
    {
        if (take.Keys.Count > 0) return take.Keys;
        var keys = new List<CameraKey>();
        foreach (var s in take.Shots)
            if (s.Replay == null) AddShot(keys, s, s.At);
        return keys;
    }

    /// <summary>
    /// ⭐ <c>T4</c> — the keys a RUN plays: a v2 take's own, else its shots read as keys <b>where <see cref="TakeTimeline.Plan"/> laid them</b>.
    /// ⚠ The layout can start a shot LATER than its <c>at</c> (a shot listed after another that is still running waits for it — measured in
    /// <c>pc-defend-house/main</c>, whose third shot's anchor precedes its second's end); the old runner played that, so the keys must too, or a
    /// seeded take would film differently. The same per-shot rule as <see cref="Of"/> (<see cref="AddShot"/>), only the start time differs.
    /// </summary>
    public static IReadOnlyList<CameraKey> Played(Take take, TakeTimeline laidOut)
    {
        if (take.Keys.Count > 0) return take.Keys;
        var keys = new List<CameraKey>();
        foreach (var shot in laidOut.Shots) AddShot(keys, shot.Shot, new AtExpr(shot.Start));
        return keys;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4059</c> / CR-7 (docs/DESIGN_Director_Timeline_Editor.md §11q K1) — the keys the camera is looked up among while segment <paramref name="segmentId"/>
    /// plays (null: no segment plays, or the one playing has no id): the UNSCOPED keys, and — when the segment owns keys — its OWN keys, which take over from the
    /// first of them for the rest of that segment (an unscoped key placed after it is not seen there). A key scoped to ANOTHER segment is never seen.
    /// <para>⛔ Not "unscoped ∪ own, by time": the drafter's replay key sits at t−1 and the main pass's moment key at t−0.7 — the union would cut back to the
    /// main angle 0.3 s into the replay.</para>
    /// </summary>
    public static IReadOnlyList<CameraKey> OnScreenIn(IReadOnlyList<CameraKey> keys, string? segmentId, Func<string, double?> anchorTime)
    {
        bool scoped = false;
        foreach (var k in keys) if (k.Segment != null) { scoped = true; break; }
        if (!scoped) return keys;   // ⭐ every take before CR-7 keys: the same list, untouched
        double? first = null;
        if (segmentId != null)
            foreach (var k in keys)
                if (k.Segment == segmentId && k.At.Resolve(anchorTime) is { } at && (first is null || at < first)) first = at;
        var list = new List<CameraKey>(keys.Count);
        foreach (var k in keys)
        {
            if (k.Segment != null) { if (k.Segment == segmentId) list.Add(k); continue; }
            if (first is { } f && !(k.At.Resolve(anchorTime) is { } at && at < f - 1e-9)) continue;   // muted from the segment's own first key on
            list.Add(k);
        }
        return list;
    }

    private static void AddShot(List<CameraKey> keys, ShotSpec s, AtExpr start)
    {
        keys.Add(new CameraKey { At = start, Camera = s.Camera, Fov = s.Camera.Fov, In = KeyTransition.Cut });
        if (s.To is not { Count: > 0 } to) return;
        var c = s.Camera;
        float V(string key, float from) => to.TryGetValue(key, out var reached) ? reached : from;
        var end = c with
        {
            Distance = V("distance", c.Distance), Yaw = V("yaw", c.Yaw), Pitch = V("pitch", c.Pitch), Lift = V("lift", c.Lift),
            Back = V("back", c.Back), Height = V("height", c.Height), Ahead = V("ahead", c.Ahead),
            Fov = to.TryGetValue("fov", out var fov) ? fov : c.Fov,
        };
        keys.Add(new CameraKey
        {
            At = new AtExpr(start.Offset + s.Seconds, start.Anchor), Camera = end, Fov = end.Fov, In = KeyTransition.Fly(s.Seconds, s.Ease),
        });
    }
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
    /// <summary>⭐ <c>TE-2</c> — format v2's camera keys (empty in a v1 take; read the keys of either through <see cref="CameraKeys.Of"/>).</summary>
    public IReadOnlyList<CameraKey> Keys { get; init; } = Array.Empty<CameraKey>();
    /// <summary>⭐ <c>TE-2</c> — format v2's time segments (empty = the whole take at speed 1).</summary>
    public IReadOnlyList<TimeSegment> Segments { get; init; } = Array.Empty<TimeSegment>();
    /// <summary>⭐ <c>CE-1303</c> P2b — CR-11: the take's subtitles (empty = its captions are read as subtitles — <see cref="TakeSubtitles.Of"/>).</summary>
    public IReadOnlyList<Subtitle> Subtitles { get; init; } = Array.Empty<Subtitle>();
    /// <summary>⭐ <c>CE-1304</c> P6 (§11t V1) — the View track: 2-D / 3-D and the layer set from a time (empty = <see cref="Layers"/> and the shots' layers are
    /// read as views — <see cref="TakeViews.Of"/>).</summary>
    public IReadOnlyList<ViewChange> Views { get; init; } = Array.Empty<ViewChange>();
    /// <summary>⭐ <c>CE-1305</c> P6 — TE-9 / R-4070 (§11t G1): the film-only graphics (ring, arrow, route, area, label).</summary>
    public IReadOnlyList<FilmGraphic> Graphics { get; init; } = Array.Empty<FilmGraphic>();
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

/// <summary>A clip waiting to join a reel, and why it is not on it yet.</summary>
public sealed record ReelWaiting(string Clip, string? Why);

/// <summary>⭐ <c>CE-4038</c> (§4a, D-15) — a playlist of takes, and on film ONE movie: <c>films/&lt;name&gt;.reel.json</c>.</summary>
public sealed record Reel
{
    public string Name { get; init; } = "";
    public string? Title { get; init; }
    public string Transition { get; init; } = "fade";
    public IReadOnlyList<ReelItem> Items { get; init; } = Array.Empty<ReelItem>();
    public bool Loop { get; init; }
    public double GapSeconds { get; init; } = 1.5d;
    /// <summary>Clips parked off the reel until they are ready, each with the reason (<c>"waiting": [{clip, why}]</c>,
    /// PLAN_Feature_Films §5) — a note for whoever edits the reel; never played.</summary>
    public IReadOnlyList<ReelWaiting> Waiting { get; init; } = Array.Empty<ReelWaiting>();

    /// <summary>A reel of one take (<c>POST /director/play {scenario, take}</c>).</summary>
    public static Reel Of(string scenario, string take) => new()
    {
        Name = scenario + "/" + take,
        Items = new[] { new ReelItem { Scenario = scenario, Take = take } },
    };
}
