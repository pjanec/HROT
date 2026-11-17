using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Presentation.Forms;
using Fdp.Toolkit.Aggregate;
using NodeEditor.Core.Widgets;

namespace Hrot.Presentation.Forms;

/// <summary>
/// ⭐ <c>CE-8615</c> (docs/designs/aggregate-units/DESIGN_Stochastic_Unit_UI.md §4 UI-9, mockup ⑤'s Details rows) — while an aggregate unit CHANGES resolution, three
/// rows of its Unit tab's Resolution section: <b>Changing</b> (from → to — the to from the instructor's pin, else the direction of the stage — and who asked),
/// <b>Stage</b> (a gauge in thirds: Planning › Materialising › Handover; a merge fills it whole) and <b>Progress</b> (the men created so far against the men up,
/// the deadline). Hidden while the unit is settled. Texts cached by value (0 B on a steady frame).
/// </summary>
public static class SplitProgress
{
    private static readonly ConditionalWeakTable<EntityRepository, EntityQuery> Children = new();

    public static bool IsChanging(EntityRepository w, Entity e)
        => UnitFacts.IsAggregate(w, e) && w.GetComponentRO<AggregateUnit>(e).Splitting != SplitStage.NotSplitting;

    public static AggregateResolution TargetOf(in AggregateUnit u) => u.Pin switch
    {
        ResolutionPin.PinPhysical => AggregateResolution.Physical,
        ResolutionPin.PinStochastic => AggregateResolution.Stochastic,
        _ => u.Splitting == SplitStage.Merging ? AggregateResolution.Stochastic : AggregateResolution.Physical,
    };

    /// <summary>The men a split has created so far for <paramref name="unit"/> (its <see cref="AggregateChild"/> entities).</summary>
    public static int MenCreated(EntityRepository w, Entity unit)
    {
        if (!w.IsComponentTypeRegistered<AggregateChild>()) return 0;
        var q = Children.GetValue(w, static world => world.Query().With<AggregateChild>().Build());
        int n = 0;
        foreach (var e in q) if (w.GetComponentRO<AggregateChild>(e).Unit == unit) n++;
        return n;
    }

    private static string Word(AggregateResolution r) => r switch { AggregateResolution.Stochastic => "Stochastic", AggregateResolution.Fusion => "Fusion", _ => "Physical" };

    private static readonly Dictionary<int, FixedString64> ChangeTexts = new();

    public static FixedString64 ChangeText(EntityRepository w, Entity e)
    {
        ref readonly var u = ref w.GetComponentRO<AggregateUnit>(e);
        var to = TargetOf(u);
        int key = (int)u.Effective | ((int)to << 4) | ((u.Pin != ResolutionPin.NoPin ? 1 : 0) << 8);
        lock (ChangeTexts)
        {
            if (!ChangeTexts.TryGetValue(key, out var t))
                ChangeTexts[key] = t = new FixedString64($"{Word(u.Effective)}  \uf061  {Word(to)}  ·  {(u.Pin != ResolutionPin.NoPin ? "pinned by the instructor" : "the policy")}");
            return t;
        }
    }

    private static readonly FixedString64[] StageTexts = { new("-"), new("Planning  (1 of 3)"), new("Materialising  (2 of 3)"), new("Handover  (3 of 3)"), new("Merging") };

    public static FormGauge StageGauge(EntityRepository w, Entity e)
    {
        var st = w.GetComponentRO<AggregateUnit>(e).Splitting;
        int i = Math.Clamp((int)st, 0, 4);
        float f = st == SplitStage.Merging ? 1f : i / 3f;
        return new FormGauge { Fraction = f, Fill = IconPalette.Blue, TickA = 1f / 3f, TickB = 2f / 3f, Text = StageTexts[i] };
    }

    private static readonly Dictionary<ulong, (long Key, FixedString64 Text)> ProgressTexts = new();

    public static FixedString64 ProgressText(EntityRepository w, Entity e)
    {
        ref readonly var u = ref w.GetComponentRO<AggregateUnit>(e);
        int created = MenCreated(w, e);
        int up = w.IsComponentTypeRegistered<UnitSlotTable>() && w.HasComponent<UnitSlotTable>(e) ? w.GetComponentRO<UnitSlotTable>(e).Present() : 0;
        double now = w.HasSingletonUnmanaged<GlobalTime>() ? w.GetSingletonUnmanaged<GlobalTime>().TotalTime : 0.0;
        int left = Math.Max(0, (int)Math.Ceiling(u.SplittingSince.Seconds + ResolutionBands.Default.DeadlineSeconds - now));
        bool merging = u.Splitting == SplitStage.Merging;
        long key = ((long)created << 40) | ((long)up << 20) | ((long)left << 1) | (merging ? 1L : 0L);
        lock (ProgressTexts)
        {
            if (!ProgressTexts.TryGetValue(e.PackedValue, out var c) || c.Key != key)
                ProgressTexts[e.PackedValue] = c = (key, new FixedString64(merging
                    ? string.Create(CultureInfo.InvariantCulture, $"{created} men still to merge  ·  deadline {left} s")
                    : string.Create(CultureInfo.InvariantCulture, $"{created} of {up} men created  ·  deadline {left} s")));
            return c.Text;
        }
    }
}
