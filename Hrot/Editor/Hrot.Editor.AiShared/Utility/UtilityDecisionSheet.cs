using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Utility;

namespace Hrot.Editor.AiShared.Utility;

/// <summary>One consideration as the view shows it: the setting, and (with a unit) what it read on the last traced pass.</summary>
public sealed record SheetConsideration(
    string Input, InputContext Context, float Weight, ResponseCurve Curve, bool Modifier)
{
    public float? Raw { get; init; }
    public float? Curved { get; init; }
    public float? Weighted { get; init; }
}

/// <summary>One option: its name, its aggregation and its considerations; with a unit, its score.</summary>
public sealed record SheetOption(int OptionId, string Name, ScoringMode Mode, IReadOnlyList<SheetConsideration> Considerations)
{
    public float? Score { get; init; }
    public bool Winner { get; init; }
    /// <summary>The consideration that scored 0 (an option at 0 names its cause).</summary>
    public string? ZeroedBy { get; init; }
}

/// <summary>A candidate a ranking scored (W4).</summary>
public sealed record SheetCandidate(Entity Entity, float Score);

/// <summary>
/// ⭐ <c>CE-1325</c> (docs/DESIGN_Utility_Decision_View.md §3) — a utility decision AS THE VIEW SHOWS IT: the running definition's settings
/// (<see cref="UtilityDecisionCatalog"/> — the same for a C#-built and an authored behaviour) and, for one unit, what it decided
/// (<see cref="UtilityDecisionLog"/>) and read (<see cref="UtilityTraceWorkingMemory1024"/>). Pure: the Details view, the Brain tab and the
/// API read the same sheet.
/// </summary>
public sealed class UtilityDecisionSheet
{
    public int DecisionId { get; init; }
    public string Name { get; init; } = "";
    public string AssetId { get; init; } = "";
    public DecisionKind Kind { get; init; }
    public float Hysteresis { get; init; }
    public IReadOnlyList<SheetOption> Options { get; init; } = Array.Empty<SheetOption>();

    /// <summary>A ranking scores ONE option per candidate (W4); an option decision picks one of its options.</summary>
    public bool IsRanking => Kind != DecisionKind.PostureSelect;

    // ── live (null without a unit, or when the unit is not traced) ───────────────────────────────────────────────────────────
    public Entity? Unit { get; init; }
    public bool Traced { get; init; }
    public string? WinnerName { get; init; }
    public string? RunnerUpName { get; init; }
    public float? Margin { get; init; }
    public int Switches { get; init; }
    public int Evaluations { get; init; }
    public IReadOnlyList<SheetCandidate> Ranked { get; init; } = Array.Empty<SheetCandidate>();

    /// <summary>The decision's settings; null when no decision of that id is registered.</summary>
    public static UtilityDecisionSheet? Build(int decisionId)
    {
        UtilityDecisionCatalog.EnsureRegistered();
        if (!UtilityDecisionCatalog.Shared.TryGet(decisionId, out var def, out float hysteresis) || def == null) return null;
        var options = def.Options.Select(o => new SheetOption(o.OptionId, def.OptionName(o.OptionId), o.Mode,
            o.Considerations.Select(c => new SheetConsideration(UtilityInputNames.Of(c.InputId), c.Context, c.Weight, c.Curve,
                (c.Flags & ConsiderationFlags.Modifier) != 0)).ToArray())).ToArray();
        return new UtilityDecisionSheet
        {
            DecisionId = decisionId, Name = def.DebugName.Length > 0 ? def.DebugName : $"decision #{decisionId:X8}", AssetId = def.AssetId,
            Kind = def.Kind, Hysteresis = hysteresis, Options = options,
        };
    }

    /// <summary>This sheet with <paramref name="unit"/>'s live values: its log slot for this decision (winner, margin, switches, ranked) and
    /// the newest traced pass that is THIS decision's (the ring has no decision id: a pass is matched by its option and input ids). Not
    /// traced ⇒ <see cref="Traced"/> false and the settings only.</summary>
    public UtilityDecisionSheet WithLive(EntityRepository world, Entity unit)
    {
        if (!world.IsAlive(unit) || !world.IsComponentTypeRegistered<UtilityDecisionLog>() || !world.HasComponent<UtilityDecisionLog>(unit))
            return With(unit, traced: false);
        ref readonly var log = ref world.GetComponentRO<UtilityDecisionLog>(unit);
        UtilityLogSlot? slot = null;
        foreach (var s in log.SlotsRO()) if (s.DecisionId == DecisionId) { slot = s; break; }
        if (slot is not { } sl) return With(unit, traced: true);

        var options = Options.ToList();
        var ranked = new List<SheetCandidate>();
        var scores = new Dictionary<int, float>();
        var entries = sl.RankedRO();
        for (int i = 0; i < sl.Count; i++)
        {
            if (IsRanking) ranked.Add(new SheetCandidate(new Entity((ulong)entries[i].CandidateHandle), entries[i].Score));
            else scores[entries[i].WinningPostureId] = entries[i].Score;
        }
        if (!IsRanking && TracedPass(world, unit) is { } pass)
            for (int i = 0; i < options.Count; i++)
                if (pass.TryGetValue(i, out var rows))
                {
                    var cs = options[i].Considerations.ToArray();
                    for (int k = 0; k < cs.Length && k < rows.Count; k++)
                        cs[k] = cs[k] with { Raw = rows[k].RawValue, Curved = rows[k].CurveOutput, Weighted = Weigh(rows[k]) };
                    options[i] = options[i] with { Considerations = cs };
                }
        for (int i = 0; i < options.Count; i++)
        {
            var o = options[i];
            float? score = scores.TryGetValue(o.OptionId, out var sc) ? sc : null;
            string? zeroed = score is 0f ? o.Considerations.FirstOrDefault(c => c.Curved is 0f) is { } z ? $"{z.Input} · {z.Curve.Kind}" : null : null;
            options[i] = o with { Score = score, Winner = !IsRanking && sl.Winner == o.OptionId, ZeroedBy = zeroed };
        }
        if (!IsRanking) options = options.OrderByDescending(o => o.Score ?? -1f).ToList();
        var runnerUp = IsRanking ? null : options.Where(o => !o.Winner && o.Score.HasValue).Select(o => o.Name).FirstOrDefault();
        return new UtilityDecisionSheet
        {
            DecisionId = DecisionId, Name = Name, AssetId = AssetId, Kind = Kind, Hysteresis = Hysteresis, Options = options,
            Unit = unit, Traced = true, Margin = sl.Margin, Switches = sl.SwitchCount, Evaluations = sl.EvalCount, Ranked = ranked,
            WinnerName = IsRanking ? null : sl.Winner == 0 ? null : Options.FirstOrDefault(o => o.OptionId == sl.Winner)?.Name,
            RunnerUpName = runnerUp,
        };
    }

    /// <summary>⭐ The sheet as JSON — <c>GET /utility/decisions/{assetId}</c> (W1) and, with a unit, its values (W2).</summary>
    public JsonObject ToJson(Func<Entity, JsonNode?>? unitNode = null)
    {
        static JsonNode? N(float? v) => v is { } f ? Math.Round(f, 4) : null;
        var options = new JsonArray();
        foreach (var o in Options)
        {
            var cons = new JsonArray();
            foreach (var c in o.Considerations)
                cons.Add(new JsonObject
                {
                    ["input"] = c.Input, ["context"] = c.Context.ToString(), ["weight"] = Math.Round(c.Weight, 4),
                    ["curve"] = c.Curve.Kind.ToString(), ["slope"] = Math.Round(c.Curve.Slope, 4), ["exponent"] = Math.Round(c.Curve.Exponent, 4),
                    ["xShift"] = Math.Round(c.Curve.XShift, 4), ["modifier"] = c.Modifier,
                    ["raw"] = N(c.Raw), ["curved"] = N(c.Curved), ["weighted"] = N(c.Weighted),
                });
            options.Add(new JsonObject
            {
                ["optionId"] = o.OptionId, ["name"] = o.Name, ["aggregation"] = o.Mode.ToString(), ["considerations"] = cons,
                ["score"] = N(o.Score), ["winner"] = o.Winner ? true : null, ["zeroedBy"] = o.ZeroedBy,
            });
        }
        var json = new JsonObject
        {
            ["decisionId"] = DecisionId, ["name"] = Name, ["assetId"] = AssetId, ["kind"] = Kind.ToString(),
            ["ranking"] = IsRanking, ["hysteresis"] = Math.Round(Hysteresis, 4), ["options"] = options,
        };
        if (Unit is { } u)
        {
            var ranked = new JsonArray();
            foreach (var r in Ranked)
            {
                var row = new JsonObject { ["score"] = Math.Round(r.Score, 4) };
                if (unitNode?.Invoke(r.Entity) is { } who) row["candidate"] = who;
                ranked.Add(row);
            }
            json["live"] = new JsonObject
            {
                ["unit"] = unitNode?.Invoke(u), ["traced"] = Traced, ["evaluations"] = Evaluations, ["switches"] = Switches,
                ["winner"] = WinnerName, ["runnerUp"] = RunnerUpName, ["margin"] = N(Margin), ["ranked"] = ranked,
            };
        }
        return json;
    }

    private static float Weigh(in UtilityTraceRecord r) => r.Weight;

    private UtilityDecisionSheet With(Entity unit, bool traced) => new()
    {
        DecisionId = DecisionId, Name = Name, AssetId = AssetId, Kind = Kind, Hysteresis = Hysteresis, Options = Options, Unit = unit, Traced = traced,
    };

    /// <summary>The newest complete pass in the unit's trace whose records match this decision (option index → its rows, in order).</summary>
    private Dictionary<int, List<UtilityTraceRecord>>? TracedPass(EntityRepository world, Entity unit)
    {
        if (!world.IsComponentTypeRegistered<UtilityTraceWorkingMemory1024>() || !world.HasComponent<UtilityTraceWorkingMemory1024>(unit)) return null;
        var copy = world.GetComponentRO<UtilityTraceWorkingMemory1024>(unit);
        int count = copy.RecordCount;
        for (int w = count - 1; w >= 1; w--)
        {
            copy.ReadRecord(w, out var win);
            if (win.OpCode != UtilityTraceOpCode.Winner) continue;
            var byOption = new Dictionary<int, List<UtilityTraceRecord>>();
            bool matches = true;
            for (int i = w - 1; i >= 0; i--)
            {
                copy.ReadRecord(i, out var r);
                if (r.OpCode == UtilityTraceOpCode.Winner) break;
                if (r.OptionIndex >= Options.Count || Options[r.OptionIndex].Considerations.All(c => c.Input != UtilityInputNames.Of(r.InputId)))
                { matches = false; break; }
                if (!byOption.TryGetValue(r.OptionIndex, out var rows)) byOption[r.OptionIndex] = rows = new List<UtilityTraceRecord>();
                rows.Insert(0, r);
            }
            if (matches && byOption.Count > 0) return byOption;
        }
        return null;
    }
}
