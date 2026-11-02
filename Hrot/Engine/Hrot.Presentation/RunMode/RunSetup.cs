using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Scenario;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐⭐ CE-4141 / AQ108 D24 (the ui half) — a RUN SETUP: what a start asks of the scenario beyond "as it is". The variant (<b>random</b> — the default, drawn by the load path, never here — or a number; <c>0</c> = as authored) and the
/// scenario's <c>header.params</c>, each defaulting to its declared default. Pure (no ImGui): the picker's <i>Run setup</i> expander edits it, <see cref="ArgsJson"/> is what <i>Open and go live</i> passes as the load's args.
/// ⭐ <b>Closed expander ⇒ nothing</b>: the defaults give <see langword="null"/>, so a start needs no interaction (D24 ⛔ <i>"a start that needs a choice is a defect"</i>). ⛔ It never draws a random number: a second random source would be two owners
/// (backend draws the default on the load path, <c>NOTE_Ui_To_Backend_D24_Variant_Default</c>).
/// </summary>
public sealed class RunSetup
{
    private readonly IReadOnlyList<ScenarioParamDecl> _decls;
    private readonly JsonObject _overrides = new();

    /// <param name="decls">The scenario header's declared params (null ⇒ none).</param>
    /// <param name="usesVariants">The scenario has variant content (<see cref="ScenarioVariants.Uses"/>): the variant row is shown only then.</param>
    public RunSetup(IReadOnlyList<ScenarioParamDecl>? decls, bool usesVariants)
    {
        _decls = decls ?? Array.Empty<ScenarioParamDecl>();
        UsesVariants = usesVariants;
    }

    public bool UsesVariants { get; }

    /// <summary><see langword="true"/> (the default) = leave the variant to the load ("random, drawn at start"); <see langword="false"/> = <see cref="Variant"/> is the choice.</summary>
    public bool RandomVariant { get; set; } = true;

    /// <summary>The chosen variant when <see cref="RandomVariant"/> is off (<c>0</c> = as authored).</summary>
    public int Variant { get; set; }

    public IReadOnlyList<ScenarioParamDecl> Params => _decls;

    /// <summary>The params whose value differs from their default (name → value).</summary>
    public JsonObject Overrides => _overrides;

    /// <summary>Everything at its default: nothing to pass.</summary>
    public bool IsDefault => _overrides.Count == 0 && !(UsesVariants && !RandomVariant);

    private ScenarioParamDecl? Decl(string name)
    {
        foreach (var d in _decls) if (d.Name == name) return d;
        return null;
    }

    /// <summary>Sets <paramref name="name"/> to <paramref name="valueJson"/>; a value equal to the declared default (or null) is no override — <i>reset</i> and <i>typed the default back</i> are the same thing.</summary>
    public void Set(string name, string? valueJson)
    {
        if (Decl(name) is not { } decl) return;
        JsonNode? value = null;
        if (valueJson is not null) { try { value = JsonNode.Parse(valueJson); } catch (System.Text.Json.JsonException) { return; } }
        if (valueJson is null || JsonNode.DeepEquals(value, decl.Default)) _overrides.Remove(name);
        else _overrides[name] = value;
    }

    public void Reset(string name) => _overrides.Remove(name);

    /// <summary>The load's args: the params that differ from their defaults, plus <see cref="ScenarioVariants.ArgName"/> only when a NUMBER was chosen. <see langword="null"/> when there is nothing to pass.</summary>
    public string? ArgsJson()
    {
        var o = new JsonObject();
        if (UsesVariants && !RandomVariant) o[ScenarioVariants.ArgName] = Variant;
        foreach (var (k, v) in _overrides) o[k] = v?.DeepClone();
        return o.Count == 0 ? null : o.ToJsonString();
    }

    /// <summary>A setup seeded from earlier args (the last run's): a <c>$variant</c> number is the variant choice, a param that differs from its default is an override; unknown keys are ignored.</summary>
    public static RunSetup FromArgs(IReadOnlyList<ScenarioParamDecl>? decls, bool usesVariants, string? argsJson)
    {
        var s = new RunSetup(decls, usesVariants);
        if (string.IsNullOrWhiteSpace(argsJson)) return s;
        try
        {
            if (JsonNode.Parse(argsJson) is not JsonObject o) return s;
            foreach (var (k, v) in o)
            {
                if (k == ScenarioVariants.ArgName) { if (usesVariants && v is JsonValue jv && jv.TryGetValue<int>(out int n)) { s.RandomVariant = false; s.Variant = n; } }
                else s.Set(k, v?.ToJsonString());
            }
        }
        catch (System.Text.Json.JsonException) { }
        return s;
    }
}

/// <summary>
/// ⭐ AQ108 D24 — the run setup as the mode indicator's menu shows it: the running scenario's ACTUAL variant (<i>as authored</i> when there is no <c>$variant</c>) and the params that differ from their declared defaults, read from what the load
/// stamped resident (<see cref="ScenarioDescription.ArgsOf"/>; the resident header holds the declared defaults). Pure over the world.
/// </summary>
public static class RunSetupText
{
    /// <summary>The menu lines for the scenario resident in <paramref name="world"/>; none when no scenario is loaded.</summary>
    public static IReadOnlyList<string> Of(EntityRepository? world)
    {
        if (world is null || ScenarioDescription.IdentityOf(world).Name is null) return Array.Empty<string>();
        return Of(ScenarioDescription.ArgsOf(world), ScenarioDescription.HeaderOf(world)?.Params);
    }

    /// <param name="argsJson">The args the load applied (variant + every param's effective value), or null.</param>
    /// <param name="declared">The scenario's declared params (their defaults say which values "differ"); null ⇒ every named param is listed.</param>
    public static IReadOnlyList<string> Of(string? argsJson, IReadOnlyList<ScenarioParamDecl>? declared)
    {
        var lines = new List<string>();
        int variant = ScenarioVariants.VariantOf(argsJson);
        lines.Add(variant == 0 ? "Variant: as authored" : "Variant: " + variant);
        if (string.IsNullOrWhiteSpace(argsJson)) return lines;
        try
        {
            if (JsonNode.Parse(argsJson) is not JsonObject o) return lines;
            foreach (var (k, v) in o)
            {
                if (k == ScenarioVariants.ArgName) continue;
                var decl = declared?.FirstOrDefault(d => d.Name == k);
                if (decl is not null && JsonNode.DeepEquals(v, decl.Default)) continue;   // at its default: not worth a line
                lines.Add($"{k} = {v?.ToJsonString() ?? "null"}");
            }
        }
        catch (System.Text.Json.JsonException) { }
        return lines;
    }

    private static T? FirstOrDefault<T>(this IReadOnlyList<T> list, Func<T, bool> pred) where T : class
    {
        foreach (var x in list) if (pred(x)) return x;
        return null;
    }
}
