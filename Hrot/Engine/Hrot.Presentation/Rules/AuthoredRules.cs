using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐ CE-4216 (rules view R2, RV-3a; docs/DESIGN_Rules_View_UI.md) — a rule entity BEFORE a run: no <see cref="ObjectiveStatus"/> exists (the running brain writes it), so the row is read from the AUTHORED params
/// (<c>BehaviorStartRecord</c>: the behaviour name = the kind, the params JSON). The params' <c>common</c> block (<see cref="ObjectiveCommon"/>) gives the title, subject, parent (the tree), group, required / scored / weight /
/// points / deadline / visibleSides; <i>Waits on</i> = every <c>ref</c> in the <c>when</c> (<c>OnEntity</c>, recursively); <i>Starts</i> = every <c>target</c> / <c>ref</c> in the <c>do</c> steps. The result is a SYNTHETIC
/// <see cref="ObjectiveStatus"/> carrying only those authored fields, so the model has ONE row type with two sources (the real status wins — <see cref="RulesReader"/>). Pure; tolerant: bad JSON ⇒ not a rule.
/// </summary>
public static class AuthoredRules
{
    /// <summary>Kinds that are measures (the rest of the non-trigger kinds are objectives). ⚠ A list by name: the kind registry is not reachable from the reader — a new measure kind is added here.</summary>
    public static readonly IReadOnlySet<string> MeasureKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Count", "HitRatio", "ReportLatency", "TimeTo", "Sample", "Points", "Best", "SelectionMean" };

    public const long TkbObjective = 8860, TkbTrigger = 8861;

    public sealed record Authored(ObjectiveStatus Status, int ParentNetworkId, int SubjectNetworkId);

    public static ObjectiveRole RoleOf(string kind)
        => kind.Equals("Trigger", StringComparison.OrdinalIgnoreCase) ? ObjectiveRole.Trigger : MeasureKinds.Contains(kind) ? ObjectiveRole.Measure : ObjectiveRole.Objective;

    /// <summary>The authored row of <paramref name="entity"/>, or null when its params are no rule's (no <c>common</c> block, bad JSON).</summary>
    public static Authored? Parse(EntityRepository world, Entity entity, int networkId, string kind, string json)
    {
        if (string.IsNullOrEmpty(kind) || string.IsNullOrWhiteSpace(json)) return null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Prop(root, "common", out var common) || common.ValueKind != JsonValueKind.Object) return null;

            int parent = LinkId(Prop(common, "parent", out var p) ? p : default);
            int subject = LinkId(Prop(common, "subject", out var s) ? s : default);
            string title = Prop(common, "title", out var ti) && ti.ValueKind == JsonValueKind.String ? ti.GetString() ?? "" : "";
            if (title.Length == 0 && world.IsComponentTypeRegistered<EntityInfo>() && world.HasComponent<EntityInfo>(entity)) title = world.GetComponentRO<EntityInfo>(entity).Name.ToString();

            var st = new ObjectiveStatus
            {
                Role = RoleOf(kind), State = ObjectiveState.Pending, Title = new FixedString64(title), Kind = new FixedString32(kind.Length > 31 ? kind[..31] : kind),
                Group = new FixedString32(Prop(common, "group", out var g) && g.ValueKind == JsonValueKind.String ? Cut(g.GetString(), 31) : ""),
                Required = (byte)(Bool(common, "required") == true ? 1 : 0), Scored = (byte)(Bool(common, "scored") != false ? 1 : 0),
                Weight = Num(common, "weight"), Points = Num(common, "points"), Pass = Num(common, "pass"), Deadline = Num(common, "deadlineSeconds"),
                VisibleSides = (byte)Num(common, "visibleSides"), Owner = SideOf(world, entity, subject),
            };
            var waits = new List<int>(); var starts = new List<int>();
            if (Prop(root, "when", out var when)) Collect(when, waits, "ref");
            if (Prop(root, "do", out var @do)) Collect(@do, starts, "target", "ref");
            Fill(ref st.DependsOnCount, ref st.DependsOn, waits);
            Fill(ref st.ActsOnCount, ref st.ActsOn, starts);
            return new Authored(st, parent, subject);
        }
    }

    private static void Fill(ref byte count, ref NetIds8 ids, List<int> list)
    {
        int n = Math.Min(8, list.Count);
        for (int i = 0; i < n; i++) ids[i] = list[i];
        count = (byte)n;
    }

    /// <summary>The side a rule is shown under: its subject's, else its own entity's, else Neutral.</summary>
    private static ForceId SideOf(EntityRepository world, Entity rule, int subjectNet)
    {
        if (world.IsComponentTypeRegistered<EntityInfo>())
        {
            var s = Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(world, subjectNet);
            if (!s.IsNull && world.IsAlive(s) && world.HasComponent<EntityInfo>(s)) return world.GetComponentRO<EntityInfo>(s).ForceId;
            if (world.HasComponent<EntityInfo>(rule)) return world.GetComponentRO<EntityInfo>(rule).ForceId;
        }
        return ForceId.Neutral;
    }

    /// <summary>Every link inside <paramref name="el"/> under a property named one of <paramref name="names"/> (recursively, objects and arrays), distinct, in document order.</summary>
    private static void Collect(JsonElement el, List<int> into, params string[] names)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    if (Array.Exists(names, n => n.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)) && LinkId(prop.Value) is var id and > 0 && !into.Contains(id)) into.Add(id);
                    else Collect(prop.Value, into, names);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray()) Collect(item, into, names);
                break;
        }
    }

    /// <summary>An authored <c>EntityLink</c>: a number · a numeric string · <c>{"NetworkId": n, "Ref": …}</c>. ⚠ A link authored only as <c>"@Key"</c> / a GUID is not resolved before a run and reads 0 (none).</summary>
    public static int LinkId(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.TryGetInt32(out int n) ? n : 0,
        JsonValueKind.String => int.TryParse(el.GetString(), out int n) ? n : 0,
        JsonValueKind.Object => Prop(el, "NetworkId", out var v) ? LinkId(v) : 0,
        _ => 0,
    };

    private static bool Prop(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in obj.EnumerateObject()) if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = p.Value; return true; }
        return false;
    }

    private static bool? Bool(JsonElement obj, string name) => Prop(obj, name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
    private static float Num(JsonElement obj, string name) => Prop(obj, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out float f) ? f : 0f;
    private static string Cut(string? s, int n) => s == null ? "" : s.Length > n ? s[..n] : s;
}
