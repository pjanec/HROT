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

    public JsonArray Shots() => CombatRows("/combat/shots?last=100000", "shots");

    public JsonArray Blasts() => CombatRows("/combat/detonations?last=100000", "detonations");

    /// <summary>
    /// ⭐ CE-9016 (found by the convoy lane, cv-ambush / uc-convoy-hit-ifv) — the combat log lives on the node that RESOLVES combat (the Muscle,
    /// SimHost), not on the Scenario (brain) node the run reads: measured on --mode all, the IED's detonation was logged on SimHost (count 1) and
    /// Scenario held none, so every shot / blast row of the cluster tier read nothing ("the charge goes off" FAIL while it went off). ⇒ read each
    /// world node's log and take the one that holds rows (the larger; a tie keeps Scenario). ⛔ Not a merge: a row's <c>seq</c> is per node.
    /// </summary>
    private JsonArray CombatRows(string path, string field)
    {
        var best = Get(path)?[field] as JsonArray ?? new JsonArray();
        foreach (var owner in OwnerPerspectives)
        {
            Post("/perspective", new JsonObject { ["name"] = owner });
            var rows = Get(path)?[field] as JsonArray;
            Post("/perspective", new JsonObject { ["name"] = "Scenario" });
            if (rows != null && rows.Count > best.Count) best = rows;
        }
        return best;
    }

    public string? SetComponent(int networkId, string component, JsonObject patch)
    {
        string? error = PostComponent(networkId, component, patch);
        // ⭐ CE-9021 (found by the convoy lane, cv-blocked-road) — the run reads the Scenario (brain) node, but a component another node OWNS (a body's
        //   SimTransform is the Muscle's) is refused there: "owned by another node … would be overwritten by the owner". ⛔ Before, the `do` was only
        //   noted as a problem and the run went on with the world unchanged (the broken truck never cleared, the convoy rightly waited for ever).
        //   ⇒ apply it through the owner's perspective, then return to Scenario.
        if (error is null || !error.Contains("owned by another node", StringComparison.Ordinal)) return error;
        foreach (var owner in OwnerPerspectives)
        {
            Post("/perspective", new JsonObject { ["name"] = owner });
            string? again = PostComponent(networkId, component, patch);
            Post("/perspective", new JsonObject { ["name"] = "Scenario" });
            if (again is null) return null;
        }
        return error;
    }

    /// <summary>⭐ CE-3494 F3 — the <c>POST /orders</c> route, one item.</summary>
    public string? Order(int networkId, JsonObject order)
    {
        var item = (JsonObject)order.DeepClone();
        item["networkId"] = networkId;
        var (body, error) = Post("/orders", new JsonObject { ["orders"] = new JsonArray(item) });
        if (error != null) return error;
        var r = (body?["results"] as JsonArray)?[0];
        return r?["accepted"]?.GetValue<bool>() == true ? null : (r?["detail"] ?? r?["refusal"])?.ToString() ?? "refused";
    }

    /// <summary>⭐ CE-3494 F3 — the <c>POST /missions/{id}/resume</c> route.</summary>
    public string? ResumePlan(int networkId) => Post($"/missions/{networkId}/resume", new JsonObject()).Error;

    /// <summary>⭐ CE-11061 — the <c>POST /entities/{id}/revive</c> route.</summary>
    public string? Revive(int networkId) => Post($"/entities/{networkId}/revive", new JsonObject()).Error;

    /// <summary>⭐ CE-9021 — the other perspectives that hold a world and may own a component (the Muscle: bodies' poses, health …).</summary>
    private static readonly string[] OwnerPerspectives = { "SimHost" };

    private string? PostComponent(int networkId, string component, JsonObject patch)
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
