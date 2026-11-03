using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Replication.Services;

namespace Hrot.Common.Macros;

/// <summary>One thing a macro WOULD create: what it is, its name, where it stands relative to the macro's anchor, its side.</summary>
public sealed record MacroGhost(long TkbType, string Name, Vector3 Offset, ForceId Side);

/// <summary>The preview of one macro: its ghosts, or the expander's own words for why nothing may be created.</summary>
public sealed record MacroPreviewResult(string Macro, IReadOnlyList<MacroGhost> Ghosts, string? Failure, int Hidden = 0)
{
    public bool Ok => Failure is null;
}

/// <summary>
/// ⭐⭐ CE-4244 (MA-10; docs/DESIGN_Macro_Authoring_Editor.md §10, MP-1 · MP-4) — a macro PREVIEW, PURE: the ONE <see cref="MacroExpander"/> expands a throw-away <see cref="MacroInstance"/> and each child's creation request becomes a
/// <see cref="MacroGhost"/>. Nothing is created and nothing is published (the expander is pure): <i>Cancel</i> has nothing to undo. Offsets are relative to the anchor so one expansion serves a moving cursor.
/// Rules, scores and nested-instance markers (what <see cref="Hrot.Common.Diagnostics.Gizmos.Seeable"/> hides) are not ghosts — they are counted in <see cref="MacroPreviewResult.Hidden"/>.
/// </summary>
public static class MacroPreview
{
    /// <param name="outside">Where an arg's <c>@Key</c> resolves (the live world), or null.</param>
    public static MacroPreviewResult Of(MacroExpander? expander, string macroPath, string? argsJson, in SimTransform anchor, IRefScope? outside = null)
    {
        if (expander == null) return new(macroPath, Array.Empty<MacroGhost>(), "this host cannot expand macros");
        if (string.IsNullOrWhiteSpace(macroPath)) return new(macroPath, Array.Empty<MacroGhost>(), "no macro named");
        var instance = new MacroInstance { Macro = macroPath, ArgsJson = string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson };
        var result = expander.Expand(instance, int.MaxValue, Guid.Empty, NodeId.None, anchor, outside, new Hrot.Core.Network.SequentialIdAllocator());
        if (!result.Ok) return new(macroPath, Array.Empty<MacroGhost>(), result.Failure);

        var ghosts = new List<MacroGhost>(); int hidden = 0;
        foreach (var c in result.Children)
        {
            long type = c.TkbType;
            if (type == Hrot.Map.Common.TkbEntityTypes.ScenarioMacro || type == Hrot.Map.Common.TkbEntityTypes.Objective || type == Hrot.Map.Common.TkbEntityTypes.Trigger) { hidden++; continue; }
            var comps = c.InitialComponents;
            var tf = comps?.OfType<SimTransform>().Select(t => (SimTransform?)t).FirstOrDefault();
            if (tf == null) { hidden++; continue; }   // positionless: nothing to draw
            var info = comps!.OfType<EntityInfo>().Select(i => (EntityInfo?)i).FirstOrDefault();
            ghosts.Add(new MacroGhost(type, info?.Name.ToString() is { Length: > 0 } n ? n : $"#{type}", tf.Value.Position - anchor.Position, info?.ForceId ?? ForceId.Neutral));
        }
        return new(macroPath, ghosts, null, hidden);
    }

    /// <summary>The preview of one <c>InsertMacro</c> step, with what an <i>Insert now</i> would ask for (the macro, where, heading, args).</summary>
    public sealed record StepPreview(MacroPreviewResult Result, Vector3 Anchor, float HeadingDeg, string Macro, string ArgsJson);

    /// <summary>
    /// ⭐ MP-2 (b) — the Do tab's <i>Preview</i>: an <c>InsertMacro</c> step's own args (<c>macro</c> · <c>at</c> = <c>[x,y,z]</c> / <c>"@Key"</c> / a GUID / <c>{NetworkId, Ref}</c> · <c>heading</c> · <c>params</c>) resolved the way the step runner reads them,
    /// then <see cref="Of"/>. An anchor that names nothing is the failure text, never a guess.
    /// </summary>
    public static StepPreview OfStep(MacroExpander? expander, EntityRepository world, JsonObject? args)
    {
        string macro = Str(args, "macro") ?? "";
        float heading = Num(args, "heading");
        string argsJson = Node(args, "params")?.ToJsonString() ?? "{}";
        var at = Node(args, "at");
        Vector3? pos = null; string? why = null;
        switch (at)
        {
            case JsonArray a when a.Count >= 2: pos = new Vector3(F(a[0]), F(a[1]), a.Count > 2 ? F(a[2]) : 0f); break;
            case JsonValue v when v.TryGetValue<string>(out var text): pos = ResolveEntity(world, text, out why); break;
            case JsonObject o: pos = ResolveEntity(world, (o["Ref"] as JsonValue)?.TryGetValue<string>(out var r) == true ? r : (o["NetworkId"]?.ToString() ?? ""), out why); break;
            default: why = "the step has no place (at) to preview"; break;
        }
        if (pos is not { } p) return new(new MacroPreviewResult(macro, Array.Empty<MacroGhost>(), why ?? "the place does not resolve here"), Vector3.Zero, heading, macro, argsJson);
        var anchor = new SimTransform { Position = p, Rotation = Fdp.Modules.Geographic.Systems.SimTransformBridgeSystem.WithHeadingDeg(Quaternion.Identity, heading) };
        return new(Of(expander, macro, argsJson, anchor, LiveRefScope.Of(world)), p, heading, macro, argsJson);
    }

    private static Vector3? ResolveEntity(EntityRepository world, string text, out string? why)
    {
        why = null;
        if (int.TryParse(text, out int net) && net > 0)
        {
            var e = Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(world, net);
            if (!e.IsNull && world.IsAlive(e) && world.HasComponent<SimTransform>(e)) return world.GetComponentRO<SimTransform>(e).Position;
            why = $"entity #{net} has no place here"; return null;
        }
        if (!RefSpec.TryParse(text, out var spec) || spec.IsNone) { why = $"the place '{text}' is not a point, a key or an entity"; return null; }
        if (spec.Form is RefForm.Key or RefForm.Exact && LiveRefScope.Of(world).TryResolve(spec, out var r) && r.Resolve(world) is { IsNull: false } found && world.IsAlive(found) && world.HasComponent<SimTransform>(found))
            return world.GetComponentRO<SimTransform>(found).Position;
        why = $"nothing here is named {text}"; return null;
    }

    private static JsonNode? Node(JsonObject? o, string key) => o?.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
    private static string? Str(JsonObject? o, string key) => Node(o, key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    private static float Num(JsonObject? o, string key) => F(Node(o, key));
    private static float F(JsonNode? n) => n is JsonValue && float.TryParse(n.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;   // an int or a double: the JSON number as written
}
