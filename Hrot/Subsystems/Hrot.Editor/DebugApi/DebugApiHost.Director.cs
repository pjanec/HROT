using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Hrot.Editor.Director;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐ <c>CE-4038</c> B5 / B10 / B11 (docs/DESIGN_Film_Director.md §4, §4b, §4c) — the film director over HTTP (and MCP, through the route
    /// docs): list and author takes, play a reel live or to film, scout it; the map's layers as the user's click sets them; the Sound switch.
    /// Every handler runs on the main thread, as every world-touching route does, and calls the SAME runner methods the Director panel calls.
    /// </summary>
    public sealed partial class DebugApiHost
    {
        private DirectorRunner? _director;
        private TakeStore? _takes;
        private Func<Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo?>? _layerControl;

        /// <summary>⭐ CE-4038 — hands the host the editor's director, its take store and the map's layer control (held by the editor).</summary>
        public void AttachDirector(DirectorRunner director, TakeStore takes, Func<Hrot.Common.Diagnostics.Gizmos.LayerControlGizmo?> layerControl)
        {
            _director = director ?? throw new ArgumentNullException(nameof(director));
            _takes = takes ?? throw new ArgumentNullException(nameof(takes));
            _layerControl = layerControl ?? throw new ArgumentNullException(nameof(layerControl));
        }

        private const string NoDirector = "This host has no film director (it is the editor's: run --mode editor).";

        private void BuildDirectorRoutes()
        {
            _routes.Add(new("GET", "/director/takes", ctx => Main(() => TakesList(ctx.Query("scenario")))));
            _routes.Add(new("GET", "/director/reels", _ => Main(ReelsList)));
            _routes.Add(new("GET", "/director/take/{scenario}/{take}", ctx => Main(() => TakeRead(ctx.RouteValue("scenario")!, ctx.RouteValue("take")!))));
            _routes.Add(new("PUT", "/director/take/{scenario}/{take}", ctx => Main(() => TakeWrite(ctx.RouteValue("scenario")!, ctx.RouteValue("take")!, ctx.Body))));
            _routes.Add(new("POST", "/director/play", ctx => Main(() => DirectorPlay(ctx.Body, scout: false))));
            _routes.Add(new("POST", "/director/scout", ctx => Main(() => DirectorPlay(ctx.Body, scout: true))));
            _routes.Add(new("POST", "/director/stop", _ => Main(() => Transport(d => d.Stop()))));
            _routes.Add(new("POST", "/director/next", _ => Main(() => Transport(d => d.Next()))));
            _routes.Add(new("POST", "/director/pause", _ => Main(() => Transport(d => d.Pause()))));
            _routes.Add(new("POST", "/director/resume", _ => Main(() => Transport(d => d.Resume()))));
            _routes.Add(new("GET", "/director/status", _ => Main(() => _director is { } d ? Ok(d.Status()) : Fail(503, NoDirector))));
            _routes.Add(new("GET", "/map/layers", ctx => Main(() => LayersRead(ctx.Query("view")))));
            _routes.Add(new("POST", "/map/layers", ctx => Main(() => LayersWrite(ctx.Body))));
            _routes.Add(new("GET", "/audio", _ => Main(AudioRead)));
            _routes.Add(new("POST", "/audio", ctx => Main(() => AudioWrite(ctx.Body))));
        }

        private Task<RouteResult> Main(Func<RouteResult> work) => _jobQueue.RunOnMainThread(work);

        // ── takes and reels ───────────────────────────────────────────────────────────────────

        private RouteResult TakesList(string? scenario)
        {
            if (_takes is not { } store) return Fail(503, NoDirector);
            var arr = new JsonArray();
            var all = string.IsNullOrWhiteSpace(scenario) ? store.AllTakes() : new[] { (scenario!, store.TakesOf(scenario!)) };
            foreach (var (s, takes) in all)
                arr.Add(new JsonObject { ["scenario"] = s, ["takes"] = new JsonArray(takes.Select(t => (JsonNode?)t).ToArray()) });
            return Ok(new JsonObject { ["root"] = store.ScenariosRoot, ["count"] = all.Sum(x => x.Item2.Count), ["scenarios"] = arr });
        }

        private RouteResult ReelsList()
        {
            if (_takes is not { } store) return Fail(503, NoDirector);
            var arr = new JsonArray();
            foreach (var name in store.ReelNames())
            {
                var reel = store.LoadReel(name);
                arr.Add(new JsonObject
                {
                    ["name"] = name, ["title"] = reel.Value?.Title, ["items"] = reel.Value?.Items.Count,
                    ["problems"] = new JsonArray(reel.Problems.Select(p => (JsonNode?)p).ToArray()),
                });
            }
            return Ok(new JsonObject { ["root"] = store.FilmsRoot, ["reels"] = arr });
        }

        private RouteResult TakeRead(string scenario, string take)
        {
            if (_takes is not { } store) return Fail(503, NoDirector);
            var loaded = store.Load(scenario, take);
            if (loaded.Value is null) return Fail(404, string.Join("; ", loaded.Problems));
            return Ok(new JsonObject
            {
                ["scenario"] = scenario, ["take"] = take, ["path"] = store.TakePath(scenario, take),
                ["json"] = TakeJson.WriteTake(loaded.Value),
                ["problems"] = new JsonArray(loaded.Problems.Select(p => (JsonNode?)p).ToArray()),
            });
        }

        private RouteResult TakeWrite(string scenario, string take, JsonNode? body)
        {
            if (_takes is not { } store) return Fail(503, NoDirector);
            if (body is not JsonObject json) return Fail(400, "the body is the take (a JSON object: title, caption, anchors, shots, …)");
            var problems = new List<string>();
            var parsed = TakeJson.ParseTake(json, scenario, take, problems);
            if (problems.Count > 0) return Fail(400, "the take is not valid: " + string.Join("; ", problems));
            var path = store.Save(parsed);
            return Ok(new JsonObject { ["saved"] = path, ["shots"] = parsed.Shots.Count, ["anchors"] = parsed.Anchors.Count });
        }

        // ── play / scout / transport ──────────────────────────────────────────────────────────

        private RouteResult DirectorPlay(JsonNode? body, bool scout)
        {
            if (_director is not { } director || _takes is not { } store) return Fail(503, NoDirector);
            Reel reel;
            Dictionary<string, Take>? inline = null;
            var scenario = body?["scenario"]?.ToString();
            var takeName = body?["take"] is JsonValue tv ? tv.ToString() : "main";
            if (body?["reel"]?.ToString() is { } reelName)
            {
                var loaded = store.LoadReel(reelName);
                if (loaded.Value is null) return Fail(404, string.Join("; ", loaded.Problems));
                if (loaded.Problems.Count > 0) return Fail(400, string.Join("; ", loaded.Problems));
                reel = loaded.Value;
            }
            else if (!string.IsNullOrWhiteSpace(scenario))
            {
                if (body?["take"] is JsonObject inlineTake)
                {
                    // ⭐ D-9 — a one-off take sent with the request (film.py's plan): played as is, never written
                    var problems = new List<string>();
                    var parsed = TakeJson.ParseTake(inlineTake, scenario!, "_inline", problems);
                    if (problems.Count > 0) return Fail(400, "the take is not valid: " + string.Join("; ", problems));
                    takeName = "_inline";
                    inline = new Dictionary<string, Take>(StringComparer.Ordinal) { [scenario + "/_inline"] = parsed };
                }
                reel = Reel.Of(scenario!, takeName);
            }
            else return Fail(400, "name what to play: {reel} or {scenario, take?} (take: a name, default main, or the take itself as an object)");

            CaptureOptions? capture = null;
            if (body?["capture"] is JsonObject c)
            {
                var dir = c["dir"]?.ToString();
                if (string.IsNullOrWhiteSpace(dir)) return Fail(400, "capture needs dir (where the frames and the movie go)");
                int w = 1280, h = 720;
                if (c["size"]?.ToString() is { } size)
                {
                    var parts = size.ToLowerInvariant().Split('x');
                    if (parts.Length != 2 || !int.TryParse(parts[0], out w) || !int.TryParse(parts[1], out h)) return Fail(400, $"size '{size}': WIDTHxHEIGHT");
                }
                capture = new CaptureOptions(System.IO.Path.GetFullPath(dir!),
                    Fps: (int)(TakeJson.Double(c["fps"]) ?? 30d), Width: w, Height: h,
                    Supersample: (int)(TakeJson.Double(c["supersample"]) ?? 1d),
                    BurnCaptions: c["captions"] is not JsonValue cv || !cv.TryGetValue<bool>(out var burn) || burn,
                    Movie: c["movie"]?.ToString(),
                    Encode: c["encode"] is not JsonValue ev || !ev.TryGetValue<bool>(out var enc) || enc);
                if (capture.Fps is < 1 or > 60) return Fail(400, "fps 1…60");
            }
            double speed = TakeJson.Double(body?["speed"]) ?? 1d;
            bool loop = body?["loop"] is JsonValue lv && lv.TryGetValue<bool>(out var l) && l;
            bool bake = body?["bake"] is not JsonValue bv || !bv.TryGetValue<bool>(out var b) || b;
            var refused = director.Play(new PlayRequest(reel, speed, loop, capture, scout, inline, bake));
            if (refused.Count > 0) return Fail(400, "not started: " + string.Join("; ", refused));
            return Ok(director.Status());
        }

        private RouteResult Transport(Action<DirectorRunner> act)
        {
            if (_director is not { } d) return Fail(503, NoDirector);
            act(d);
            return Ok(d.Status());
        }

        // ── layers (R-4060) ───────────────────────────────────────────────────────────────────

        private RouteResult LayersRead(string? view)
        {
            if (_layerControl?.Invoke() is not { } lc) return Fail(503, "This host has no map layer control.");
            bool? only3D = view switch { "3d" or "3D" => true, "2d" or "2D" => false, _ => null };
            var o = new JsonObject { ["shown"] = lc.Shows3D ? "3d" : "2d", ["override"] = lc.Override != null };
            foreach (var is3D in new[] { false, true })
            {
                if (only3D is { } v && v != is3D) continue;
                var rows = new JsonArray();
                var set = lc.SetOf(is3D);
                foreach (var (name, display, doc) in Hrot.Common.Diagnostics.Gizmos.LayerControlSets.RowNames)
                {
                    Hrot.Common.Diagnostics.Gizmos.LayerControlSets.TryGet(set, name, out var on);
                    rows.Add(new JsonObject { ["name"] = name, ["display"] = display, ["on"] = on, ["doc"] = doc });
                }
                o[is3D ? "3d" : "2d"] = rows;
            }
            return Ok(o);
        }

        private RouteResult LayersWrite(JsonNode? body)
        {
            if (_layerControl?.Invoke() is not { } lc) return Fail(503, "This host has no map layer control.");
            var view = body?["view"]?.ToString();
            bool is3D = view switch { "3d" or "3D" => true, "2d" or "2D" => false, _ => lc.Shows3D };
            static List<string> Names(JsonNode? n) => n is JsonArray a ? a.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList() : new();
            var unknown = lc.SetRows(is3D, Names(body?["on"]), Names(body?["off"]));
            if (unknown.Count > 0) return Fail(400, $"no such layer: {string.Join(", ", unknown)} — GET /map/layers lists them", DebugApiHints.Panel);
            return LayersRead(is3D ? "3d" : "2d");
        }

        // ── sound (R-4059) ────────────────────────────────────────────────────────────────────

        private static RouteResult AudioRead()
        {
            var s = Hrot.UI.Common.Audio.AudioPreferences.For(Hrot.UI.Common.Audio.AudioSettings.DefaultPath());
            return Ok(new JsonObject
            {
                ["sound"] = Hrot.UI.Common.Audio.AudioPreferences.CameraSound,
                ["takeSound"] = Hrot.UI.Common.Audio.AudioDirector.TakeSound,
                ["master"] = s.Master, ["muted"] = s.Muted,
                ["note"] = "sound = Settings ▸ Sound (the scene heard from the camera, 2-D and 3-D); FPS always sounds",
            });
        }

        private static RouteResult AudioWrite(JsonNode? body)
        {
            var path = Hrot.UI.Common.Audio.AudioSettings.DefaultPath();
            var s = Hrot.UI.Common.Audio.AudioPreferences.For(path);
            if (body?["sound"] is JsonValue sv && sv.TryGetValue<bool>(out var sound)) Hrot.UI.Common.Audio.AudioPreferences.CameraSound = sound;
            bool changed = false;
            if (TakeJson.Double(body?["master"]) is { } master) { s.Master = (float)Math.Clamp(master, 0d, 1d); changed = true; }
            if (body?["muted"] is JsonValue mv && mv.TryGetValue<bool>(out var muted)) { s.Muted = muted; changed = true; }
            if (changed) s.Save(path);
            return AudioRead();
        }
    }
}
