using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Hrot.Editor.AiShared.Scenarios;

namespace Hrot.Editor.Scenario;

/// <summary>
/// ⭐⭐ CE-4210 MA-8 (SR-39a; docs/DESIGN_Macro_Authoring_Editor.md) — the PURE half of the instance's args form: rows from the macro's HEADER params (read with <see cref="ScenarioHeaderJson.Read"/>, the same one the save and
/// load use), a default versus an override, and an edit that builds a NEW <see cref="MacroInstance"/> (immutable) carrying the merged <c>ArgsJson</c> — overlaid key by key on the defaults (SR-28). A value equal to the
/// default is no override (it is removed), so <i>reset to default</i> and <i>typed the default back</i> are the same thing.
/// </summary>
public static class MacroArgs
{
    /// <summary>The macro's header read from <c>{macrosRoot}/{path}/scenario.json</c>, or why not.</summary>
    public static (ScenarioHeader? Header, string? Problem) ReadHeader(string? macrosRoot, string? macroPath)
    {
        if (string.IsNullOrWhiteSpace(macroPath)) return (null, "No macro is chosen.");
        if (string.IsNullOrWhiteSpace(macrosRoot)) return (null, "This host has no macros root.");
        string file = Path.Combine(macrosRoot, macroPath.Replace('/', Path.DirectorySeparatorChar), ContentLayout.MacroFileName);
        if (!File.Exists(file)) return (null, $"The macro '{macroPath}' is not on the server ({ContentLayout.MacroName(macroPath)}).");
        try
        {
            var header = ScenarioHeaderJson.Read(JsonNode.Parse(File.ReadAllText(file)) as JsonObject);
            return (header ?? new ScenarioHeader(""), null);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            return (null, $"The macro '{macroPath}' could not be read: {e.Message}");
        }
    }

    public static JsonObject ArgsOf(string? argsJson)
    {
        try { return JsonNode.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson) as JsonObject ?? new JsonObject(); }
        catch (System.Text.Json.JsonException) { return new JsonObject(); }
    }

    /// <summary>One row per declared param of <paramref name="header"/>, in declaration order.</summary>
    public static IReadOnlyList<ParamArgRow> Rows(ScenarioHeader? header, string? argsJson)
        => ParamArgRow.From(header?.Params, ArgsOf(argsJson));   // ⭐ the ONE row builder, shared with the picker's run setup

    /// <summary>A NEW instance whose args carry <paramref name="valueJson"/> for <paramref name="name"/> (merged into the existing args); a null value, or one equal to <paramref name="defaultJson"/>, removes the override.</summary>
    public static MacroInstance With(MacroInstance instance, string name, string? valueJson, string? defaultJson)
    {
        var args = ArgsOf(instance.ArgsJson);
        JsonNode? value = null;
        if (valueJson is not null) { try { value = JsonNode.Parse(valueJson); } catch (System.Text.Json.JsonException) { return instance with { }; } }
        bool isDefault = valueJson is null || (defaultJson is not null && JsonNode.DeepEquals(value, JsonNode.Parse(defaultJson)));
        if (isDefault) args.Remove(name); else args[name] = value;
        return instance with { ArgsJson = args.ToJsonString() };
    }

    public static MacroInstance Reset(MacroInstance instance, string name) => With(instance, name, null, null);

    /// <summary>A NEW instance of another macro: its args start empty (the old macro's overrides name params the new one may not have).</summary>
    public static MacroInstance Retarget(MacroInstance instance, string macroPath) => instance with { Macro = macroPath, ArgsJson = "{}" };

    public enum WriteResult { Written, NoInstance, NotOwned }

    /// <summary>
    /// Writes <paramref name="next"/> to the entity — ⭐ ONLY where this node owns the component (<see cref="IInspectableSession.HasAuthority"/>): a managed component written to an entity another node owns is
    /// overwritten by the owner's next replication, so the view says <i>edit on the owning node</i> instead of pretending.
    /// </summary>
    /// <param name="world">⭐ CE-3384 — with <paramref name="label"/>, the write is ONE undo step of the editor's history: it is bracketed by <see cref="EditSteps"/> exactly as every <c>Edits.Submit</c> is (the world as it was is the step's "before"). Null ⇒ no bracket (a host with no history).</param>
    public static WriteResult Write(IInspectableSession session, Entity entity, MacroInstance next, EntityRepository? world = null, string? label = null)
    {
        if (!session.IsAlive(entity) || !session.HasComponent(entity, typeof(MacroInstance))) return WriteResult.NoInstance;
        if (!session.HasAuthority(entity, typeof(MacroInstance))) return WriteResult.NotOwned;
        bool step = world is not null && label is not null;
        if (step) EditSteps.Begin(world, label!);
        try { session.SetComponent(entity, typeof(MacroInstance), next); }
        finally { if (step) EditSteps.End(world); }
        return WriteResult.Written;
    }
}
