using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Hrot.UI.Common.Map3D.Director;

/// <summary>
/// ⭐ <c>CE-4038</c> (docs/DESIGN_Film_Director.md §4, §4a) — reads and writes takes and reels. The shot keys are <c>clips.json</c>'s (film.py's
/// plan keys, snake or camel case) plus <c>to</c>, <c>ease</c>, <c>speed</c>, <c>replay</c>, <c>caption</c>, <c>layers</c>, <c>sound</c>.
/// ⭐ Lenient where a key is spelt two ways, ⛔ strict about a key it does not know: every one is listed in <c>problems</c>, never dropped.
/// </summary>
public static class TakeJson
{
    private static readonly HashSet<string> ShotKeys = new(StringComparer.Ordinal)
    {
        "at", "seconds", "follow", "over", "toward", "look_at", "lookAt", "eye", "target", "distance", "yaw", "pitch", "orbit", "lift",
        "back", "height", "ahead", "fov", "to", "ease", "speed", "replay", "caption", "layers", "sound", "why",
    };

    private static readonly HashSet<string> ToKeys = new(StringComparer.Ordinal) { "distance", "yaw", "pitch", "lift", "back", "height", "ahead", "fov" };

    private static readonly HashSet<string> TakeKeys = new(StringComparer.Ordinal)
        { "title", "caption", "tickHz", "endAt", "anchors", "baked", "shots", "keys", "segments", "subtitles", "layers", "sound" };

    /// <summary>⭐ <c>TE-2</c> — a camera key's keys: a shot's camera keys at its top level, plus <c>in</c> and <c>still</c>.</summary>
    private static readonly HashSet<string> KeyKeys = new(StringComparer.Ordinal)
    {
        "at", "follow", "over", "toward", "look_at", "lookAt", "eye", "target", "distance", "yaw", "pitch", "orbit", "lift",
        "back", "height", "ahead", "fov", "in", "still", "segment",
    };

    private static readonly HashSet<string> SegmentKeys = new(StringComparer.Ordinal) { "id", "from", "to", "speed", "gap" };

    /// <summary>⭐ <c>CE-1303</c> P2b — CR-11 (§5, §11o) — a subtitle's keys.</summary>
    private static readonly HashSet<string> SubtitleKeys = new(StringComparer.Ordinal) { "at", "until", "text", "position", "fade" };

    /// <summary>Kept, not read: what <c>clips.json</c> carries per clip, and a file's own note.</summary>
    private static readonly HashSet<string> ExtraKeys = new(StringComparer.Ordinal)
        { "id", "clip", "chapter", "state", "needs", "take", "scenario", "$doc", "note" };

    // ── takes ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A take from its JSON; every problem (an unknown key, a bad value, a camera naming none or two kinds) goes in <paramref name="problems"/>.</summary>
    public static Take ParseTake(JsonObject json, string scenario, string name, List<string> problems)
    {
        var anchors = new Dictionary<string, AnchorSpec>(StringComparer.Ordinal);
        if (json["anchors"] is JsonObject a)
            foreach (var (key, node) in a)
                if (node is JsonObject o) anchors[key] = ParseAnchor(o, key, problems);
                else problems.Add($"anchor '{key}' is not an object");

        var shots = new List<ShotSpec>();
        if (json["shots"] is JsonArray arr)
            for (int i = 0; i < arr.Count; i++)
                if (arr[i] is JsonObject s) shots.Add(ParseShot(s, i + 1, problems));
                else problems.Add($"shot {i + 1} is not an object");

        var keys = new List<CameraKey>();
        if (json["keys"] is JsonArray karr)
            for (int i = 0; i < karr.Count; i++)
                if (karr[i] is JsonObject k) keys.Add(ParseKey(k, i + 1, problems));
                else problems.Add($"key {i + 1} is not an object");

        var segments = new List<TimeSegment>();
        if (json["segments"] is JsonArray sarr)
            for (int i = 0; i < sarr.Count; i++)
                if (sarr[i] is JsonObject g) { if (ParseSegment(g, i + 1, problems) is { } seg) segments.Add(seg); }
                else problems.Add($"segment {i + 1} is not an object");

        // ⭐ CE-4059 / CR-7 (§11q) — a segment id is unique; a key names a segment the take has
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in segments)
            if (g.Id is { } id && !ids.Add(id)) problems.Add($"segment id '{id}' is used twice");
        for (int i = 0; i < keys.Count; i++)
            if (keys[i].Segment is { } seg && !ids.Contains(seg)) problems.Add($"key {i + 1}: segment '{seg}' is not a segment of the take");

        var subtitles = new List<Subtitle>();
        if (json["subtitles"] is JsonArray tarr)
            for (int i = 0; i < tarr.Count; i++)
                if (tarr[i] is JsonObject u) { if (ParseSubtitle(u, i + 1, problems) is { } sub) subtitles.Add(sub); }
                else problems.Add($"subtitle {i + 1} is not an object");

        JsonObject? extra = null;
        foreach (var (key, node) in json)
        {
            if (TakeKeys.Contains(key)) continue;
            if (ExtraKeys.Contains(key)) (extra ??= new JsonObject())[key] = node?.DeepClone();
            else problems.Add($"unknown take key '{key}'");
        }

        int tickHz = Int(json["tickHz"]) ?? 60;
        if (tickHz is not (60 or 30 or 20 or 15)) problems.Add($"tickHz {tickHz}: 60, 30, 20 or 15");

        return new Take
        {
            Scenario = scenario, Name = name,
            Title = Str(json["title"]), Caption = Str(json["caption"]),
            TickHz = tickHz,
            EndAt = TryAt(json["endAt"], "endAt", problems),
            Anchors = anchors,
            Baked = json["baked"] is JsonObject b ? ParseBaked(b) : null,
            Shots = shots, Keys = keys, Segments = segments, Subtitles = subtitles,
            Layers = json["layers"] is JsonObject l ? ParseLayers(l, "the take", problems) : null,
            Sound = Bool(json["sound"]),
            Extra = extra,
        };
    }

    /// <summary>
    /// The camera a shot or a key writes at its top level (film.py's plan keys). <paramref name="where"/> is "shot 3" / "key 2" — it prefixes
    /// every problem. A camera must name exactly one kind.
    /// </summary>
    private static CameraSpec ParseCamera(JsonObject s, string where, List<string> problems)
    {
        var camera = new CameraSpec
        {
            Follow = Name(s["follow"]), Over = Name(s["over"]), Toward = Name(s["toward"]),
            LookAt = Vec(s["lookAt"] ?? s["look_at"], $"{where} lookAt", problems),
            Eye = Vec(s["eye"], $"{where} eye", problems), Target = Vec(s["target"], $"{where} target", problems),
            Distance = Float(s["distance"]) ?? 30f, Yaw = Float(s["yaw"]) ?? 0f, Pitch = Float(s["pitch"]) ?? -20f,
            Orbit = Float(s["orbit"]) ?? 0f, Lift = Float(s["lift"]) ?? 1.5f, Back = Float(s["back"]) ?? 20f,
            Height = Float(s["height"]) ?? 6f, Ahead = Float(s["ahead"]) ?? 0.35f, Fov = Float(s["fov"]),
        };
        if (camera.Kinds != 1) problems.Add($"{where}: needs exactly one of follow · over (+ toward) · lookAt · eye + target ({camera.Kinds} given)");
        if (camera.Over != null && camera.Toward == null) problems.Add($"{where}: over needs toward");
        if ((camera.Eye == null) != (camera.Target == null)) problems.Add($"{where}: eye needs target");
        return camera;
    }

    /// <summary>
    /// ⭐ <c>TE-2</c> — one camera key: <c>at</c>, the camera at its top level, <c>in</c> (<c>"cut"</c> · <c>{"fly": seconds, "ease": …}</c> ·
    /// <c>{"dip": [out, in]}</c>), <c>fov</c>, <c>still</c>. ⛔ An unknown key is a problem naming the key's number.
    /// </summary>
    public static CameraKey ParseKey(JsonObject k, int number, List<string> problems)
    {
        var where = $"key {number}";
        foreach (var (key, _) in k)
            if (!KeyKeys.Contains(key)) problems.Add($"{where}: unknown key '{key}'");
        if (k["at"] is null) problems.Add($"{where}: needs at");
        var camera = ParseCamera(k, where, problems);
        return new CameraKey
        {
            At = TryAt(k["at"], $"{where} at", problems) ?? new AtExpr(0),
            Camera = camera, Fov = camera.Fov,
            In = ParseTransition(k["in"], where, problems),
            Still = Str(k["still"]),
            Segment = Str(k["segment"]),   // ⭐ CE-4059 / CR-7 (§11q) — the segment it belongs to (absent: every pass)
        };
    }

    private static KeyTransition ParseTransition(JsonNode? n, string where, List<string> problems)
    {
        if (n is null) return KeyTransition.Cut;
        if (n is JsonValue v)
        {
            if (string.Equals(v.ToString(), "cut", StringComparison.Ordinal)) return KeyTransition.Cut;
            problems.Add($"{where}: in '{v}' (\"cut\", {{\"fly\": seconds}} or {{\"dip\": [out, in]}})");
            return KeyTransition.Cut;
        }
        if (n is not JsonObject o) { problems.Add($"{where}: in is \"cut\", {{\"fly\": seconds, \"ease\": …}} or {{\"dip\": [out, in]}}"); return KeyTransition.Cut; }
        foreach (var (key, _) in o)
            if (key is not ("fly" or "ease" or "dip")) problems.Add($"{where}: in has unknown key '{key}'");
        if (o["fly"] is not null && o["dip"] is not null) problems.Add($"{where}: in names both fly and dip");
        if (o["fly"] is { } fly)
        {
            var seconds = Double(fly);
            if (seconds is not > 0) { problems.Add($"{where}: in.fly must be positive seconds"); seconds = 1d; }
            var ease = Ease.InOut;
            if (Str(o["ease"]) is { } e && !Enum.TryParse(e, ignoreCase: true, out ease)) problems.Add($"{where}: ease '{e}' (linear, in, out, inOut)");
            return KeyTransition.Fly(seconds.Value, ease);
        }
        if (o["dip"] is { } dip)
        {
            if (dip is JsonArray { Count: 2 } d && Double(d[0]) is { } outS && Double(d[1]) is { } inS && outS >= 0 && inS >= 0)
                return KeyTransition.Dip(outS, inS);
            problems.Add($"{where}: in.dip is [out seconds, in seconds], both 0 or more");
            return KeyTransition.Dip(0.5d, 0.5d);
        }
        problems.Add($"{where}: in names neither fly nor dip");
        return KeyTransition.Cut;
    }

    /// <summary>⭐ <c>TE-2</c> — one time segment: <c>from</c>, <c>to</c>, optional <c>speed</c>, <c>gap</c> (<c>"cut"</c> | <c>"dip"</c>). Null (and a problem) when it has no <c>from</c> / <c>to</c>.</summary>
    public static TimeSegment? ParseSegment(JsonObject g, int number, List<string> problems)
    {
        var where = $"segment {number}";
        foreach (var (key, _) in g)
            if (!SegmentKeys.Contains(key)) problems.Add($"{where}: unknown key '{key}'");
        var from = TryAt(g["from"], $"{where} from", problems);
        var to = TryAt(g["to"], $"{where} to", problems);
        if (g["from"] is null) problems.Add($"{where}: needs from");
        if (g["to"] is null) problems.Add($"{where}: needs to");
        double speed = Float(g["speed"]) ?? 1f;
        if (!(speed > 0)) problems.Add($"{where}: speed must be positive");
        var gap = SegmentGap.Cut;
        if (Str(g["gap"]) is { } gp && !Enum.TryParse(gp, ignoreCase: true, out gap)) problems.Add($"{where}: gap '{gp}' (cut, dip)");
        if (from is not { } f || to is not { } t) return null;
        return new TimeSegment { Id = Str(g["id"]), From = f, To = t, Speed = speed, Gap = gap };
    }

    /// <summary>⭐ <c>CE-1303</c> P2b — CR-11: one subtitle — <c>at</c>, <c>until</c>, <c>text</c>, optional <c>position</c> (<c>"bottom"</c> | <c>"top"</c> |
    /// <c>"middle"</c>) and <c>fade</c> (sim seconds). Null (and a problem) without <c>at</c> / <c>until</c>.</summary>
    public static Subtitle? ParseSubtitle(JsonObject u, int number, List<string> problems)
    {
        var where = $"subtitle {number}";
        foreach (var (key, _) in u)
            if (!SubtitleKeys.Contains(key)) problems.Add($"{where}: unknown key '{key}'");
        var at = TryAt(u["at"], $"{where} at", problems);
        var until = TryAt(u["until"], $"{where} until", problems);
        if (u["at"] is null) problems.Add($"{where}: needs at");
        if (u["until"] is null) problems.Add($"{where}: needs until");
        var position = SubtitlePosition.Bottom;
        if (Str(u["position"]) is { } pos && !Enum.TryParse(pos, ignoreCase: true, out position)) problems.Add($"{where}: position '{pos}' (bottom, top, middle)");
        double fade = Double(u["fade"]) ?? 0d;
        if (fade < 0) problems.Add($"{where}: fade must not be negative");
        if (at is not { } a || until is not { } t) return null;
        return new Subtitle { At = a, Until = t, Text = Str(u["text"]) ?? "", Position = position, Fade = Math.Max(fade, 0d) };
    }

    public static ShotSpec ParseShot(JsonObject s, int number, List<string> problems)
    {
        foreach (var (key, _) in s)
            if (!ShotKeys.Contains(key)) problems.Add($"shot {number}: unknown key '{key}'");
        var camera = ParseCamera(s, $"shot {number}", problems);

        Dictionary<string, float>? to = null;
        if (s["to"] is JsonObject t)
            foreach (var (key, node) in t)
                if (!ToKeys.Contains(key)) problems.Add($"shot {number}: 'to' cannot move '{key}' (only {string.Join(", ", ToKeys)})");
                else if (Float(node) is { } f) (to ??= new Dictionary<string, float>(StringComparer.Ordinal))[key] = f;

        var ease = Ease.InOut;
        if (Str(s["ease"]) is { } e && !Enum.TryParse(e, ignoreCase: true, out ease)) problems.Add($"shot {number}: ease '{e}' (linear, in, out, inOut)");

        double seconds = Float(s["seconds"]) ?? 3f;
        if (!(seconds > 0)) problems.Add($"shot {number}: seconds must be positive");
        double speed = Float(s["speed"]) ?? 1f;
        if (!(speed > 0)) problems.Add($"shot {number}: speed must be positive");

        return new ShotSpec
        {
            At = TryAt(s["at"], $"shot {number} at", problems) ?? new AtExpr(0),
            Seconds = seconds, Camera = camera, To = to, Ease = ease, Speed = speed,
            Replay = s["replay"] is JsonNode r && r.ToString() != "false" ? r.DeepClone() : null,
            Caption = Str(s["caption"]),
            Layers = s["layers"] is JsonObject l ? ParseLayers(l, $"shot {number}", problems) : null,
            Sound = Bool(s["sound"]),
            Why = Str(s["why"]),
        };
    }

    public static AnchorSpec ParseAnchor(JsonObject o, string key, List<string> problems)
    {
        var on = Str(o["on"]) ?? "";
        if (!AnchorSpec.Kinds.Contains(on)) problems.Add($"anchor '{key}': cue '{on}' is not one of {string.Join(", ", AnchorSpec.Kinds)}");
        foreach (var (k, _) in o)
            if (k is not ("on" or "who" or "by" or "target" or "is" or "n" or "point" or "within" or "below"))
                problems.Add($"anchor '{key}': unknown key '{k}'");
        var p = Vec(o["point"], $"anchor '{key}' point", problems);
        return new AnchorSpec
        {
            On = on, Who = Name(o["who"]), By = Name(o["by"]), Target = Name(o["target"]), Is = o["is"]?.DeepClone(),
            N = Int(o["n"]) ?? 1, Point = p is { } v ? new Vector2(v.X, v.Y) : null,
            Within = Float(o["within"]) ?? 3f, Below = Float(o["below"]) ?? 0f,
        };
    }

    public static BakedAnchors ParseBaked(JsonObject b)
    {
        var times = new Dictionary<string, double>(StringComparer.Ordinal);
        if (b["times"] is JsonObject t)
            foreach (var (k, v) in t)
                if (Float(v) is { } f) times[k] = Double(v) ?? f;
        return new BakedAnchors
        {
            Times = times, TickHz = Int(b["tickHz"]) ?? 60,
            ScenarioHash = Str(b["scenarioHash"]) ?? "", BuildSha = Str(b["buildSha"]) ?? "",
            Missing = b["missing"] is JsonArray m ? m.Select(x => x?.ToString() ?? "").ToList() : Array.Empty<string>(),
        };
    }

    public static LayerChange ParseLayers(JsonObject l, string where, List<string> problems)
    {
        foreach (var (k, _) in l)
            if (k is not ("on" or "off")) problems.Add($"{where}: layers key '{k}' (on, off)");
        static List<string> Names(JsonNode? n) => n is JsonArray a ? a.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList() : new();
        return new LayerChange(Names(l["on"]), Names(l["off"]));
    }

    /// <summary>The take as written to its file (camel case; a camera key only when it differs from the default).</summary>
    public static JsonObject WriteTake(Take take)
    {
        var o = new JsonObject();
        if (take.Title != null) o["title"] = take.Title;
        if (take.Caption != null) o["caption"] = take.Caption;
        if (take.TickHz != 60) o["tickHz"] = take.TickHz;
        if (take.EndAt is { } end) o["endAt"] = end.ToJson();
        if (take.Layers != null) o["layers"] = WriteLayers(take.Layers);
        if (take.Sound is { } sound) o["sound"] = sound;
        var anchors = new JsonObject();
        foreach (var (k, a) in take.Anchors) anchors[k] = WriteAnchor(a);
        o["anchors"] = anchors;
        if (take.Baked != null) o["baked"] = WriteBaked(take.Baked);
        var shots = new JsonArray();
        foreach (var s in take.Shots) shots.Add(WriteShot(s));
        o["shots"] = shots;
        if (take.Keys.Count > 0) o["keys"] = new JsonArray(take.Keys.Select(k => (JsonNode?)WriteKey(k)).ToArray());
        if (take.Segments.Count > 0) o["segments"] = new JsonArray(take.Segments.Select(g => (JsonNode?)WriteSegment(g)).ToArray());
        if (take.Subtitles.Count > 0) o["subtitles"] = new JsonArray(take.Subtitles.Select(u => (JsonNode?)WriteSubtitle(u)).ToArray());
        if (take.Extra != null)
            foreach (var (k, v) in take.Extra) o[k] = v?.DeepClone();
        return o;
    }

    public static JsonObject WriteShot(ShotSpec s)
    {
        var o = new JsonObject { ["at"] = s.At.ToJson(), ["seconds"] = s.Seconds };
        WriteCamera(o, s.Camera);
        if (s.To is { Count: > 0 } to)
        {
            var t = new JsonObject();
            foreach (var (k, v) in to) t[k] = v;
            o["to"] = t;
        }
        if (s.Ease != Ease.InOut) o["ease"] = s.Ease == Ease.Linear ? "linear" : s.Ease == Ease.In ? "in" : "out";
        if (s.Speed != 1d) o["speed"] = s.Speed;
        if (s.Replay != null) o["replay"] = s.Replay.DeepClone();
        if (s.Caption != null) o["caption"] = s.Caption;
        if (s.Layers != null) o["layers"] = WriteLayers(s.Layers);
        if (s.Sound is { } sound) o["sound"] = sound;
        if (s.Why != null) o["why"] = s.Why;
        return o;
    }

    /// <summary>A camera's keys into <paramref name="o"/>, each only when it differs from the default (as a shot and a key write it).</summary>
    private static void WriteCamera(JsonObject o, CameraSpec c)
    {
        var d = new CameraSpec();
        if (c.Follow != null) o["follow"] = c.Follow;
        if (c.Over != null) o["over"] = c.Over;
        if (c.Toward != null) o["toward"] = c.Toward;
        if (c.LookAt is { } la) o["lookAt"] = V(la);
        if (c.Eye is { } eye) o["eye"] = V(eye);
        if (c.Target is { } tg) o["target"] = V(tg);
        void F(string key, float value, float fallback) { if (value != fallback) o[key] = value; }
        F("distance", c.Distance, d.Distance); F("yaw", c.Yaw, d.Yaw); F("pitch", c.Pitch, d.Pitch); F("orbit", c.Orbit, d.Orbit);
        F("lift", c.Lift, d.Lift); F("back", c.Back, d.Back); F("height", c.Height, d.Height); F("ahead", c.Ahead, d.Ahead);
        if (c.Fov is { } fov) o["fov"] = fov;
    }

    /// <summary>⭐ <c>TE-2</c> — a key as written: <c>at</c>, its camera, <c>in</c> (omitted for a cut), <c>still</c>. <see cref="CameraKey.Fov"/> is the lens that is written.</summary>
    public static JsonObject WriteKey(CameraKey k)
    {
        var o = new JsonObject { ["at"] = k.At.ToJson() };
        WriteCamera(o, k.Camera with { Fov = k.Fov });
        switch (k.In.Kind)
        {
            case KeyTransitionKind.Fly:
                o["in"] = new JsonObject { ["fly"] = k.In.Seconds, ["ease"] = k.In.Ease == Ease.InOut ? "inOut" : k.In.Ease == Ease.Linear ? "linear" : k.In.Ease == Ease.In ? "in" : "out" };
                break;
            case KeyTransitionKind.Dip:
                o["in"] = new JsonObject { ["dip"] = new JsonArray(k.In.OutSeconds, k.In.InSeconds) };
                break;
        }
        if (k.Still != null) o["still"] = k.Still;
        if (k.Segment != null) o["segment"] = k.Segment;
        return o;
    }

    /// <summary>⭐ <c>TE-2</c> — a segment as written (<c>speed</c> / <c>gap</c> only when they differ from 1 / cut).</summary>
    public static JsonObject WriteSegment(TimeSegment g)
    {
        var o = new JsonObject();
        if (g.Id != null) o["id"] = g.Id;
        o["from"] = g.From.ToJson();
        o["to"] = g.To.ToJson();
        if (g.Speed != 1d) o["speed"] = g.Speed;
        if (g.Gap != SegmentGap.Cut) o["gap"] = "dip";
        return o;
    }

    /// <summary>⭐ CR-11 — a subtitle as written (<c>position</c> / <c>fade</c> only when they differ from bottom / 0).</summary>
    public static JsonObject WriteSubtitle(Subtitle u)
    {
        var o = new JsonObject { ["at"] = u.At.ToJson(), ["until"] = u.Until.ToJson(), ["text"] = u.Text };
        if (u.Position != SubtitlePosition.Bottom) o["position"] = u.Position == SubtitlePosition.Top ? "top" : "middle";
        if (u.Fade > 0d) o["fade"] = u.Fade;
        return o;
    }

    public static JsonObject WriteAnchor(AnchorSpec a)
    {
        var o = new JsonObject { ["on"] = a.On };
        if (a.Who != null) o["who"] = a.Who;
        if (a.By != null) o["by"] = a.By;
        if (a.Target != null) o["target"] = a.Target;
        if (a.Is != null) o["is"] = a.Is.DeepClone();
        if (a.N != 1 || a.On is "shot" or "blast") o["n"] = a.N;
        if (a.Point is { } p) o["point"] = new JsonArray(p.X, p.Y);
        if (a.On == "near") o["within"] = a.Within;
        if (a.On == "health") o["below"] = a.Below;
        return o;
    }

    public static JsonObject WriteBaked(BakedAnchors b)
    {
        var times = new JsonObject();
        foreach (var (k, v) in b.Times.OrderBy(kv => kv.Value)) times[k] = Math.Round(v, 4);
        var o = new JsonObject { ["times"] = times, ["tickHz"] = b.TickHz, ["scenarioHash"] = b.ScenarioHash, ["buildSha"] = b.BuildSha };
        if (b.Missing.Count > 0) o["missing"] = new JsonArray(b.Missing.Select(m => (JsonNode?)m).ToArray());
        return o;
    }

    public static JsonObject WriteLayers(LayerChange l) => new()
    {
        ["on"] = new JsonArray(l.On.Select(x => (JsonNode?)x).ToArray()),
        ["off"] = new JsonArray(l.Off.Select(x => (JsonNode?)x).ToArray()),
    };

    // ── reels ─────────────────────────────────────────────────────────────────────────────────

    public static Reel ParseReel(JsonObject json, string name, List<string> problems)
    {
        foreach (var (k, _) in json)
            if (k is not ("title" or "transition" or "items" or "loop" or "gapSeconds" or "note" or "waiting" or "$doc"))
                problems.Add($"unknown reel key '{k}'");
        var waiting = new List<ReelWaiting>();
        if (json["waiting"] is JsonArray parked)
            for (int i = 0; i < parked.Count; i++)
            {
                if (parked[i] is not JsonObject w) { problems.Add($"waiting {i + 1} is not an object"); continue; }
                foreach (var (k, _) in w)
                    if (k is not ("clip" or "why")) problems.Add($"waiting {i + 1}: unknown key '{k}'");
                var clip = Str(w["clip"]);
                if (string.IsNullOrWhiteSpace(clip)) { problems.Add($"waiting {i + 1}: no clip"); continue; }
                waiting.Add(new ReelWaiting(clip!, Str(w["why"])));
            }
        else if (json["waiting"] is not null) problems.Add("'waiting' is a list of {clip, why}");
        var items = new List<ReelItem>();
        if (json["items"] is JsonArray arr)
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not JsonObject it) { problems.Add($"item {i + 1} is not an object"); continue; }
                foreach (var (k, _) in it)
                    if (k is not ("scenario" or "take" or "title" or "chapterCard" or "clip" or "from" or "to" or "note"))
                        problems.Add($"item {i + 1}: unknown key '{k}'");
                var scenario = Str(it["scenario"]);
                if (string.IsNullOrWhiteSpace(scenario)) { problems.Add($"item {i + 1}: no scenario"); continue; }
                items.Add(new ReelItem
                {
                    Scenario = scenario!, Take = Str(it["take"]) ?? "main", Title = Str(it["title"]),
                    ChapterCard = Str(it["chapterCard"]), Clip = Str(it["clip"]),
                    From = TryAt(it["from"], $"item {i + 1} from", problems), To = TryAt(it["to"], $"item {i + 1} to", problems),
                });
            }
        else problems.Add("a reel needs items");
        return new Reel
        {
            Name = name, Title = Str(json["title"]), Transition = Str(json["transition"]) ?? "fade", Items = items,
            Loop = Bool(json["loop"]) ?? false, GapSeconds = Float(json["gapSeconds"]) ?? 1.5f, Waiting = waiting,
        };
    }

    public static JsonObject WriteReel(Reel reel)
    {
        var items = new JsonArray();
        foreach (var it in reel.Items)
        {
            var o = new JsonObject { ["scenario"] = it.Scenario, ["take"] = it.Take };
            if (it.Title != null) o["title"] = it.Title;
            if (it.Clip != null) o["clip"] = it.Clip;
            if (it.ChapterCard != null) o["chapterCard"] = it.ChapterCard;
            if (it.From is { } f) o["from"] = f.ToJson();
            if (it.To is { } t) o["to"] = t.ToJson();
            items.Add(o);
        }
        var r = new JsonObject();
        if (reel.Title != null) r["title"] = reel.Title;
        r["transition"] = reel.Transition;
        if (reel.Loop) r["loop"] = true;
        r["items"] = items;
        if (reel.Waiting.Count > 0)
        {
            var parked = new JsonArray();
            foreach (var w in reel.Waiting)
            {
                var o = new JsonObject { ["clip"] = w.Clip };
                if (w.Why != null) o["why"] = w.Why;
                parked.Add(o);
            }
            r["waiting"] = parked;
        }
        return r;
    }

    // ── values ────────────────────────────────────────────────────────────────────────────────

    private static AtExpr? TryAt(JsonNode? n, string where, List<string> problems)
    {
        if (n is null) return null;
        try { return AtExpr.Parse(n); }
        catch (FormatException ex) { problems.Add($"{where}: {ex.Message}"); return null; }
    }

    internal static string? Str(JsonNode? n) => n is JsonValue v ? v.ToString() : null;

    /// <summary>A unit: a name, or a network id written as a number.</summary>
    private static string? Name(JsonNode? n) => n is JsonValue v ? v.ToString() : null;

    public static double? Double(JsonNode? n) =>
        n is JsonValue v && (v.TryGetValue<double>(out var d) || double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) ? d : null;

    internal static float? Float(JsonNode? n) => Double(n) is { } d ? (float)d : null;

    private static int? Int(JsonNode? n) => Double(n) is { } d ? (int)Math.Round(d) : null;

    private static bool? Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    /// <summary><c>[x, y, z]</c>, <c>[x, y]</c> (z = 0) or <c>"x,y,z"</c>.</summary>
    internal static Vector3? Vec(JsonNode? n, string where, List<string> problems)
    {
        if (n is null) return null;
        var parts = n is JsonArray a ? a.Select(x => Double(x)).ToList()
                  : n.ToString().Split(',').Select(t => double.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : (double?)null).ToList();
        if (parts.Count is not (2 or 3) || parts.Any(p => p is null)) { problems.Add($"{where}: '{n.ToJsonString()}' is not [x, y, z]"); return null; }
        return new Vector3((float)parts[0]!, (float)parts[1]!, parts.Count == 3 ? (float)parts[2]! : 0f);
    }

    private static JsonArray V(Vector3 v) => new(v.X, v.Y, v.Z);
}
