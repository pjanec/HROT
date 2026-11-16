using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Fdp.Core;
using Hrot.Editor.Director;
using Hrot.UI.Common.Map3D.Director;

namespace Hrot.ClusterRunner;

/// <summary>
/// ⭐ RT-5 (R-4279, docs/DESIGN_Scenario_Regression.md §11.3) — the THIRD <see cref="IVerifyWorld"/>: a running cluster's node read over its debug
/// HTTP (the editor reads its own process — <c>EditorVerifyWorld</c>; the replay harness reads a recording). No repository: the condition book
/// judges every cue on the dumped components (<c>ConditionBook.StateHolds</c>, R-3094). One HTTP read per unit per sample, cached for the sample.
/// </summary>
internal sealed class ClusterVerifyWorld : IVerifyWorld, IShotWorld, IDisposable
{
    private readonly HttpClient _http;
    private List<(int Id, string Name)> _named = new();
    private readonly Dictionary<int, string> _taskNames = new();

    public ClusterVerifyWorld(int port)
    {
        // ⚠ the HOSTNAME: 127.0.0.1 404s every route (RUNBOOK_Cluster_Debugging_Over_Http §2)
        _http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(60) };
    }

    public EntityRepository? World => null;
    public IShotWorld Units => this;

    /// <summary>The <c>data</c> of a debug-API answer, or null (nothing listening yet, or <c>ok:false</c>).</summary>
    public JsonNode? Get(string path)
    {
        try { return Data(_http.GetStringAsync(path).GetAwaiter().GetResult()); }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    public (JsonNode? Data, string? Error) Post(string path, JsonNode? body)
    {
        try
        {
            using var content = new StringContent(body?.ToJsonString() ?? "{}", Encoding.UTF8, "application/json");
            using var r = _http.PostAsync(path, content).GetAwaiter().GetResult();
            var text = r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var json = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
            if (json?["ok"]?.GetValue<bool>() == true) return (json["data"], null);
            return (null, json?["error"]?.ToString() ?? $"HTTP {(int)r.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { return (null, e.Message); }
    }

    private static JsonNode? Data(string text)
    {
        var json = JsonNode.Parse(text);
        return json?["ok"]?.GetValue<bool>() == true ? json["data"] : null;
    }

    /// <summary>Re-reads the cast (units inserted during the run — the checks' <c>later</c> — appear here).</summary>
    public void Refresh()
    {
        if (Get("/entities") is not JsonArray rows) return;
        var named = new List<(int, string)>();
        foreach (var r in rows.OfType<JsonObject>())
        {
            var name = r["name"]?.ToString();
            int id = (int)(ConditionBook.Num(r["networkId"]) ?? 0);
            if (id != 0 && !string.IsNullOrEmpty(name)) named.Add((id, name));
        }
        _named = named;
    }

    public IReadOnlyList<(int Id, string Name)> Named() { Refresh(); return _named; }

    /// <summary>Not carried over HTTP (an entity index is the node's own): a golden's <c>@index</c> reads <c>?</c> — this tier keeps no golden.</summary>
    public string? NameOfEntityIndex(int index) => null;

    public string? BehaviourName(int hash) => _taskNames.TryGetValue(hash, out var n) ? n : null;

    public JsonObject? Components(int networkId)
    {
        var comps = Get($"/entities/{networkId}")?["Components"] as JsonObject;
        // the dump carries the running behaviour's HASH and its start record's NAME: remember the pair (a task cue reads hash → name)
        if (comps?["BehaviorState"]?["ActiveBehaviorHash"] is { } h && comps["BehaviorStartRecord"]?["BehaviorName"]?.ToString() is { Length: > 0 } name
            && ConditionBook.Num(h) is { } hash && hash != 0)
            _taskNames[(int)hash] = name;
        return comps;
    }

    public JsonArray Shots() => Get("/combat/shots?last=100000")?["shots"] as JsonArray ?? new JsonArray();

    public JsonArray Blasts() => Get("/combat/detonations?last=100000")?["detonations"] as JsonArray ?? new JsonArray();

    public string? SetComponent(int networkId, string component, JsonObject patch)
        => Post($"/entities/{networkId}/component", new JsonObject { ["networkId"] = networkId, ["componentType"] = component, ["patch"] = patch.DeepClone() }).Error;

    // ── IShotWorld: the units by name ─────────────────────────────────────────────

    public int? Resolve(string who) { foreach (var (id, n) in _named) if (string.Equals(n, who, StringComparison.Ordinal)) return id; return null; }

    IReadOnlyList<int> IShotWorld.ResolveAll(string who) => _named.Where(x => string.Equals(x.Name, who, StringComparison.Ordinal)).Select(x => x.Id).ToList();

    public Vector3? PositionOf(int networkId)
        => Components(networkId)?["SimTransform"]?["Position"] is JsonArray p && p.Count >= 3
            ? new Vector3((float)(ConditionBook.Num(p[0]) ?? 0), (float)(ConditionBook.Num(p[1]) ?? 0), (float)(ConditionBook.Num(p[2]) ?? 0))
            : null;

    public IReadOnlyCollection<string> Names => _named.Select(x => x.Name).Distinct().ToList();

    public void Dispose() => _http.Dispose();
}
