using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>One tunable value of a unit's brain, as the user sees it.</summary>
/// <param name="Key">The provider's own key (stable).</param>
/// <param name="Value">What the unit uses now (authored, else its type's, else the default).</param>
/// <param name="TypeValue">What it would use with no authored value (its type's, else the default) — the reset target.</param>
/// <param name="IsAuthored">An authored value is set (the reset is offered).</param>
/// <param name="Presets">Named points of the range (a proficiency tier), drawn as the slider's tick labels; empty for a plain 0..1.</param>
public sealed record BrainSetting(string Key, string Label, float Value, float TypeValue, float Min, float Max, bool IsAuthored,
    string? Tooltip = null, IReadOnlyList<(string Name, float Value)>? Presets = null);

/// <summary>
/// ⭐⭐ <c>CE-1311</c> (Brain View §12b BV-41, R-1037) — one COMPONENT that steers a behaviour, offered to the user as settings in a
/// user-facing <see cref="Group"/> (🔒 user: <i>"the user who wants to see and tune the behavior doesn't care what component the settings
/// lives in"</i>). Two providers may share a group and merge into it. ⭐ A write sets the component's AUTHORED bit — a slider that only
/// moved the raw value would leave the unit unchanged (the reader honours the bit); a reset clears it. Written LOCALLY, on the node that
/// runs the brain (R-1036).
/// </summary>
public interface IBrainSettingProvider
{
    string Group { get; }
    bool AppliesTo(ISimulationView view, Entity unit);
    IReadOnlyList<BrainSetting> Settings(ISimulationView view, Entity unit);
    void Write(EntityRepository world, Entity unit, string key, float value);
    void Reset(EntityRepository world, Entity unit, string key);
}

/// <summary>⭐ <c>CE-1311</c> — the providers, merged by group for one unit. <see cref="Default"/> carries the engine's two.</summary>
public sealed class BrainSettingRegistry
{
    private readonly List<IBrainSettingProvider> _providers = new();

    public static BrainSettingRegistry Default { get; } = new BrainSettingRegistry()
        .Add(new CombatTemperamentSettings())
        .Add(new CombatProficiencySettings());

    public BrainSettingRegistry Add(IBrainSettingProvider provider)
    {
        _providers.Add(provider ?? throw new ArgumentNullException(nameof(provider)));
        return this;
    }

    /// <summary>The groups for <paramref name="unit"/>, in registration order, each with its providers' settings.</summary>
    public IReadOnlyList<(string Group, IReadOnlyList<(IBrainSettingProvider Provider, BrainSetting Setting)> Settings)> For(
        ISimulationView view, Entity unit)
    {
        var groups = new List<(string, List<(IBrainSettingProvider, BrainSetting)>)>();
        foreach (var p in _providers)
        {
            if (!p.AppliesTo(view, unit)) continue;
            int at = groups.FindIndex(g => g.Item1 == p.Group);
            if (at < 0) { groups.Add((p.Group, new())); at = groups.Count - 1; }
            foreach (var s in p.Settings(view, unit)) groups[at].Item2.Add((p, s));
        }
        return groups.Select(g => (g.Item1, (IReadOnlyList<(IBrainSettingProvider, BrainSetting)>)g.Item2)).ToList();
    }

    internal static bool Has<T>(ISimulationView view, Entity e) where T : unmanaged
        => (view is not EntityRepository r || r.IsComponentTypeRegistered<T>()) && view.HasComponent<T>(e);

    internal static bool Registered<T>(ISimulationView view) where T : unmanaged
        => view is not EntityRepository r || r.IsComponentTypeRegistered<T>();
}

/// <summary>⭐ <c>CE-1311</c> — <see cref="CombatTemperament"/> as the <b>Temperament</b> group: bravery, obedience, nerve, resolve.</summary>
public sealed class CombatTemperamentSettings : IBrainSettingProvider
{
    public string Group => "Temperament";

    public bool AppliesTo(ISimulationView view, Entity unit)
        => BrainSettingRegistry.Registered<CombatTemperament>(view) && BrainSettingRegistry.Has<BehaviorState>(view, unit);

    public IReadOnlyList<BrainSetting> Settings(ISimulationView view, Entity unit)
    {
        CombatTemperament t = BrainSettingRegistry.Has<CombatTemperament>(view, unit) ? view.GetComponentRO<CombatTemperament>(unit) : default;
        BrainSetting S(string key, string label, byte bit, float value, float type, float fallback, string tip)
            => new(key, label, value, (t.TypeSet & bit) != 0 ? type : fallback, 0f, 1f, (t.Set & bit) != 0, tip);
        return new[]
        {
            S("bravery", "Bravery", CombatTemperament.RiskAcceptanceSet, TemperamentOf.RiskAcceptance(view, unit), t.TypeRiskAcceptance,
              TemperamentOf.DefaultRiskAcceptance, "How much exposure he accepts on his own initiative (0 none, 1 reckless)."),
            S("obedience", "Obedience", CombatTemperament.ObedienceSet, TemperamentOf.Obedience(view, unit), t.TypeObedience,
              TemperamentOf.DefaultObedience, "How far he follows an order that exposes him (0 refuses, 1 always)."),
            S("nerve", "Nerve", CombatTemperament.NerveSet, TemperamentOf.Nerve(view, unit), t.TypeNerve,
              TemperamentOf.DefaultNerve, "How much danger before he goes to ground or breaks (0 none, 1 iron)."),
            S("resolve", "Resolve", CombatTemperament.ResolveSet, TemperamentOf.Resolve(view, unit), t.TypeResolve,
              TemperamentOf.DefaultResolve, "How unwilling he is to give up (0 surrenders at once, 1 fanatic)."),
        };
    }

    public void Write(EntityRepository world, Entity unit, string key, float value)
    {
        if (!world.IsComponentTypeRegistered<CombatTemperament>() || !world.IsAlive(unit)) return;
        if (!world.HasComponent<CombatTemperament>(unit)) world.AddComponent(unit, default(CombatTemperament));
        ref var t = ref world.GetComponentRW<CombatTemperament>(unit);
        value = Math.Clamp(value, 0f, 1f);
        switch (key)
        {
            case "bravery":   t.RiskAcceptance = value; t.Set |= CombatTemperament.RiskAcceptanceSet; break;
            case "obedience": t.Obedience = value;      t.Set |= CombatTemperament.ObedienceSet; break;
            case "nerve":     t.Nerve = value;          t.Set |= CombatTemperament.NerveSet; break;
            case "resolve":   t.Resolve = value;        t.Set |= CombatTemperament.ResolveSet; break;
        }
    }

    public void Reset(EntityRepository world, Entity unit, string key)
    {
        if (!world.IsComponentTypeRegistered<CombatTemperament>() || !world.IsAlive(unit) || !world.HasComponent<CombatTemperament>(unit)) return;
        ref var t = ref world.GetComponentRW<CombatTemperament>(unit);
        byte bit = key switch
        {
            "bravery" => CombatTemperament.RiskAcceptanceSet, "obedience" => CombatTemperament.ObedienceSet,
            "nerve" => CombatTemperament.NerveSet, "resolve" => CombatTemperament.ResolveSet, _ => 0,
        };
        t.Set &= (byte)~bit;
    }
}

/// <summary>⭐ <c>CE-1311</c> — <see cref="CombatProficiency"/> as the <b>Combat skill</b> group: one value, the tiers as presets.</summary>
public sealed class CombatProficiencySettings : IBrainSettingProvider
{
    public string Group => "Combat skill";

    private static readonly IReadOnlyList<(string, float)> Tiers = new[]
    {
        ("Untrained", ProficiencyOf.Untrained), ("Militia", ProficiencyOf.Militia), ("Regular", ProficiencyOf.Regular),
        ("Veteran", ProficiencyOf.Veteran), ("Elite", ProficiencyOf.Elite),
    };

    public bool AppliesTo(ISimulationView view, Entity unit)
        => BrainSettingRegistry.Registered<CombatProficiency>(view) && BrainSettingRegistry.Has<BehaviorState>(view, unit);

    public IReadOnlyList<BrainSetting> Settings(ISimulationView view, Entity unit)
    {
        CombatProficiency p = BrainSettingRegistry.Has<CombatProficiency>(view, unit) ? view.GetComponentRO<CombatProficiency>(unit) : default;
        // the value without the authored one: the unit's, else the type's, else the reader's own default
        var unauthored = p; unauthored.Set &= unchecked((byte)~CombatProficiency.AuthoredSet);
        float type = (unauthored.Set & CombatProficiency.UnitSet) != 0 ? p.Unit
                   : (unauthored.Set & CombatProficiency.TypeDefaultSet) != 0 ? p.TypeDefault
                   : ProficiencyOf.Regular;
        return new[]
        {
            new BrainSetting("proficiency", "Proficiency", ProficiencyOf.Value(view, unit), type, 0f, 1f,
                (p.Set & CombatProficiency.AuthoredSet) != 0, "How well he fights: spread, exposure, reaction time, bursts.", Tiers),
        };
    }

    public void Write(EntityRepository world, Entity unit, string key, float value)
    {
        if (key != "proficiency" || !world.IsComponentTypeRegistered<CombatProficiency>() || !world.IsAlive(unit)) return;
        if (!world.HasComponent<CombatProficiency>(unit)) world.AddComponent(unit, default(CombatProficiency));
        ref var p = ref world.GetComponentRW<CombatProficiency>(unit);
        p.Authored = Math.Clamp(value, 0f, 1f);
        p.Set |= CombatProficiency.AuthoredSet;
    }

    public void Reset(EntityRepository world, Entity unit, string key)
    {
        if (key != "proficiency" || !world.IsComponentTypeRegistered<CombatProficiency>() || !world.IsAlive(unit)
            || !world.HasComponent<CombatProficiency>(unit)) return;
        world.GetComponentRW<CombatProficiency>(unit).Set &= unchecked((byte)~CombatProficiency.AuthoredSet);
    }
}
