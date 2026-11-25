using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;

namespace Hrot.CGF.Commander;

/// <summary>⭐ <c>CE-4684</c> C-2 — a condition with its ids resolved once (the unit's entity and network id, the place's point) and the time it was armed.</summary>
public readonly record struct ArmedCondition(Condition Condition, Entity Unit, int UnitNetId, Vector2 At, double ArmedAt);

/// <summary>⭐ <c>CE-4684</c> C-2 — one order the watcher holds: waiting for its start, or running with a completion / abort to watch.</summary>
public sealed class WatchedOrder
{
    public required UnitOrder Order { get; init; }
    public ArmedCondition? Start { get; init; }
    public ArmedCondition? CompleteWhen { get; set; }
    public ArmedCondition[] AbortWhen { get; set; } = Array.Empty<ArmedCondition>();
    public bool Started { get; set; }
}

/// <summary>
/// ⭐⭐ <c>CE-4684</c> (DESIGN_LLM_Commander §5c C-2, C-3) — what a seat's last decision left for HROT to do WITHOUT a model call: orders waiting for their
/// <c>start</c>, running orders' <c>complete_when</c> / <c>abort_when</c>, up to three contingencies, and the decision's own wake conditions and deadline.
/// Frame thread only (<see cref="CommanderWatchSystem"/> evaluates it, <see cref="CommanderActionSystem"/> arms it). ⭐ 0 B per steady frame: index loops
/// over at most ~20 armed conditions, reading the world; an item is written only when one holds.
/// </summary>
public sealed class ConditionWatcher
{
    /// <summary>UNIT_AT holds within this of the place (m).</summary>
    public const float AtMetres = 25f;
    /// <summary>ENEMY_SEEN_AT: an element reported within this of the place (m).</summary>
    public const float SeenMetres = 50f;

    internal readonly List<WatchedOrder> Orders = new();
    internal readonly List<(Contingency Contingency, ArmedCondition Trigger)> Contingencies = new();
    internal readonly List<ArmedCondition> WakeOn = new();

    /// <summary>⭐ C-3 — when the model asked to decide again (sim s); 0 = no decision yet (the fixed heartbeat runs).</summary>
    public double NextDecisionAt { get; internal set; }

    // the counts a run reports
    public int Started { get; private set; }
    public int Completed { get; private set; }
    public int Aborted { get; private set; }
    public int Fired { get; private set; }
    public int Woke { get; private set; }
    /// <summary>⭐ CE-4687 — conditions dropped because they could never hold (a unit out of the fight, a name that names nothing).</summary>
    public int Dropped { get; private set; }

    public int Armed => Orders.Count + Contingencies.Count + WakeOn.Count;

    /// <summary>A condition resolved against the seat's world; null when an id no longer names anything (it then never holds).</summary>
    internal static ArmedCondition? Arm(EntityRepository world, SeatRuntime seat, Condition? c, double now)
    {
        if (c == null) return null;
        Entity unit = Entity.Null;
        int net = 0;
        if (c.Unit != null)
        {
            unit = CommanderActionSystem.SquadNamed(world, seat.Commander, c.Unit, out _);
            if (unit.IsNull) return null;
            net = CommandPictureOps.NetIdOf(world, unit);
        }
        var at = Vector2.Zero;
        if (c.At != null)
        {
            if (seat.Places.Resolve(c.At, out _) is not { } p) return null;
            at = p;
        }
        return new ArmedCondition(c, unit, net, at, now);
    }

    /// <summary>
    /// ⭐ The decision takes over: each unit it names drops what the watcher held for it (a new order supersedes), the contingencies and wake conditions
    /// are REPLACED by the decision's, and the deadline is set. Orders that start at once are given by the caller; this keeps their completion / abort.
    /// </summary>
    internal void Take(EntityRepository world, SeatRuntime seat, Decision d, double now)
    {
        for (int i = 0; i < d.Orders.Count; i++)
        {
            var o = d.Orders[i];
            if (o.Task == DecisionContract.Continue) continue;
            for (int k = Orders.Count - 1; k >= 0; k--) if (Orders[k].Order.Unit == o.Unit) Orders.RemoveAt(k);
            bool waits = o.Start is { On: not ConditionOn.Immediate };
            // ⭐ CE-4687 — a start that can never hold (it names nothing, or a unit already out of the fight) is said now, never left to wait silently
            if (waits && CannotStart(world, seat, o.Start!, now) is { } why)
            {
                Dropped++;
                Say(seat, now, true, "cannot-start", $"ORDER {DecisionContract.Text(o)}: CANNOT START — {why}; {o.Unit} keeps what it has");
                continue;
            }
            var watched = new WatchedOrder
            {
                Order = o, Start = waits ? Arm(world, seat, o.Start, now) : null, Started = !waits,
                CompleteWhen = waits ? null : Arm(world, seat, o.CompleteWhen, now), AbortWhen = waits ? Array.Empty<ArmedCondition>() : ArmAll(world, seat, o.AbortWhen, now),
            };
            if (waits || watched.CompleteWhen != null || watched.AbortWhen.Length > 0) Orders.Add(watched);
        }
        Contingencies.Clear();
        for (int i = 0; i < d.Contingencies.Count; i++)
            if (Arm(world, seat, d.Contingencies[i].Trigger, now) is { } t) Contingencies.Add((d.Contingencies[i], t));
        WakeOn.Clear();
        for (int i = 0; i < d.WakeOn.Count; i++)
            if (Arm(world, seat, d.WakeOn[i], now) is { } w) WakeOn.Add(w);
        NextDecisionAt = now + d.NextDecisionS;
        // ⭐ CE-4687 — what the decision armed on a unit ALREADY out of the fight is dropped and said at once (the fall event came before)
        while (FirstOutOfTheFight(world) is { IsNull: false } gone) UnitLeft(world, seat, gone, now);
    }

    /// <summary>The first unit an armed condition waits on to arrive / finish / make contact that is out of the fight; null when none (on a decision, not per frame).</summary>
    private Entity FirstOutOfTheFight(EntityRepository world)
    {
        for (int i = 0; i < Orders.Count; i++)
        {
            var w = Orders[i];
            if (w.CompleteWhen is { } c && Gone(world, c)) return c.Unit;
            for (int k = 0; k < w.AbortWhen.Length; k++) if (Gone(world, w.AbortWhen[k])) return w.AbortWhen[k].Unit;
        }
        for (int i = 0; i < Contingencies.Count; i++) if (Gone(world, Contingencies[i].Trigger)) return Contingencies[i].Trigger.Unit;
        for (int i = 0; i < WakeOn.Count; i++) if (Gone(world, WakeOn[i])) return WakeOn[i].Unit;
        return Entity.Null;
    }

    private static bool Gone(EntityRepository world, in ArmedCondition c) => NeverHolds(c) && !InTheFight(world, c.Unit);

    /// <summary>Is <paramref name="unit"/> alive and still in the fight (its members at or above its minimum) — the feed's own test.</summary>
    internal static bool InTheFight(EntityRepository world, Entity unit) => world.IsAlive(unit) && CombatEffective.IsInTheFight(world, unit);

    /// <summary>
    /// ⭐ CE-4687 — would <paramref name="c"/> wait forever: it names nothing this seat knows, or it waits on a unit already out of the fight to ARRIVE,
    /// FINISH or MAKE CONTACT. Null = it can still hold. ⛔ STRENGTH_BELOW is not one: a unit that fell is exactly when it should fire.
    /// </summary>
    internal static string? CannotStart(EntityRepository world, SeatRuntime seat, Condition c, double now)
    {
        if (Arm(world, seat, c, now) is not { } a) return $"{c.Text} names nothing I know";
        return NeverHolds(a) && !InTheFight(world, a.Unit) ? $"{c.Unit} is out of the fight" : null;
    }

    /// <summary>A condition that a unit out of the fight can never make true (it will not arrive, finish, or report contact).</summary>
    private static bool NeverHolds(in ArmedCondition c) => !c.Unit.IsNull && c.Condition.On is ConditionOn.UnitAt or ConditionOn.UnitTaskComplete or ConditionOn.Contact;

    /// <summary>
    /// ⭐⭐ CE-4687 — <paramref name="unit"/> fell out of the fight (the feed's fall event, said once): every waiting start, completion, abort,
    /// contingency trigger and wake condition that waits on IT to arrive, finish or make contact can never hold — drop each, and say it in ONE order-state
    /// WAKE so the seat decides again. A waiting order is not given (its unit keeps what it has); a running order runs on without that completion / abort.
    /// 📌 llm-company-aggregated (Haiku): <c>1st Platoon Attack, start UNIT_AT 2nd Platoon support</c> waited silently after 2nd Platoon was destroyed.
    /// </summary>
    internal void UnitLeft(EntityRepository world, SeatRuntime seat, Entity unit, double now)
    {
        System.Text.StringBuilder? said = null;
        void Note(string text) { (said ??= new System.Text.StringBuilder()).Append(said.Length > 0 ? "; " : "").Append(text); Dropped++; }
        for (int i = Orders.Count - 1; i >= 0; i--)
        {
            var w = Orders[i];
            if (!w.Started)
            {
                if (w.Start is { } s && s.Unit == unit && NeverHolds(s)) { Note($"ORDER {DecisionContract.Text(w.Order)} CANNOT START ({w.Order.Unit} keeps what it has)"); Orders.RemoveAt(i); }
                continue;
            }
            if (w.CompleteWhen is { } c && c.Unit == unit && NeverHolds(c)) { Note($"{w.Order.Unit}'s order will not complete on {c.Condition.Text}"); w.CompleteWhen = null; }
            int kept = 0;
            for (int k = 0; k < w.AbortWhen.Length; k++) if (!(w.AbortWhen[k].Unit == unit && NeverHolds(w.AbortWhen[k]))) kept++;
            if (kept < w.AbortWhen.Length)
            {
                var a = new ArmedCondition[kept];
                for (int k = 0, j = 0; k < w.AbortWhen.Length; k++) if (!(w.AbortWhen[k].Unit == unit && NeverHolds(w.AbortWhen[k]))) a[j++] = w.AbortWhen[k];
                Note($"{w.Order.Unit}'s order will not abort on {unit}'s condition");
                w.AbortWhen = a;
            }
            if (w.CompleteWhen == null && w.AbortWhen.Length == 0) Orders.RemoveAt(i);
        }
        for (int i = Contingencies.Count - 1; i >= 0; i--)
            if (Contingencies[i].Trigger.Unit == unit && NeverHolds(Contingencies[i].Trigger)) { Note($"CONTINGENCY {Contingencies[i].Trigger.Condition.Text} can never fire"); Contingencies.RemoveAt(i); }
        for (int i = WakeOn.Count - 1; i >= 0; i--)
            if (WakeOn[i].Unit == unit && NeverHolds(WakeOn[i])) { Note($"WAKE {WakeOn[i].Condition.Text} can never hold"); WakeOn.RemoveAt(i); }
        if (said == null) return;
        string name = world.IsAlive(unit) ? CommanderFeedSystem.NameOf(world, unit) : "a unit";
        Say(seat, now, true, "cannot-hold", $"{name} is out of the fight — {said}");
    }

    private static ArmedCondition[] ArmAll(EntityRepository world, SeatRuntime seat, IReadOnlyList<Condition> cs, double now)
    {
        if (cs.Count == 0) return Array.Empty<ArmedCondition>();
        var list = new List<ArmedCondition>(cs.Count);
        for (int i = 0; i < cs.Count; i++) if (Arm(world, seat, cs[i], now) is { } a) list.Add(a);
        return list.ToArray();
    }

    /// <summary>⭐ The frame's pass: what holds now acts — a start gives its order, a completion / abort / contingency / wake condition is said (a wake).</summary>
    internal void Evaluate(EntityRepository world, SeatRuntime seat, double now)
    {
        if (Armed == 0) return;
        var vocab = seat.Vocabulary;
        for (int i = Orders.Count - 1; i >= 0; i--)
        {
            var w = Orders[i];
            if (!w.Started)
            {
                if (w.Start is not { } s || !Holds(world, seat, s, now)) continue;
                w.Started = true;
                Started++;
                Give(seat, w.Order, vocab);
                Say(seat, now, false, "started", $"ORDER {DecisionContract.Text(w.Order)}: STARTED — {s.Condition.Text}");
                w.CompleteWhen = Arm(world, seat, w.Order.CompleteWhen, now);
                w.AbortWhen = ArmAll(world, seat, w.Order.AbortWhen, now);
                if (w.CompleteWhen == null && w.AbortWhen.Length == 0) Orders.RemoveAt(i);
                continue;
            }
            bool aborted = false;
            for (int k = 0; k < w.AbortWhen.Length; k++)
            {
                if (!Holds(world, seat, w.AbortWhen[k], now)) continue;
                aborted = true;
                Aborted++;
                // an aborted squad order holds where the squad is; an aborted play ends
                seat.Channel.Inject(w.Order.Unit == vocab?.Self ? new CommanderAction(CommanderActionKind.StopPlay)
                                                                  : new CommanderAction(CommanderActionKind.Hold, w.Order.Unit));
                Say(seat, now, true, "aborted", $"ORDER {DecisionContract.Text(w.Order)}: ABORTED — {w.AbortWhen[k].Condition.Text}; {(w.Order.Unit == vocab?.Self ? "the play ends" : w.Order.Unit + " holds")}");
                break;
            }
            if (aborted) { Orders.RemoveAt(i); continue; }
            if (w.CompleteWhen is { } c && Holds(world, seat, c, now))
            {
                Completed++;
                Say(seat, now, true, "complete", $"ORDER {DecisionContract.Text(w.Order)}: COMPLETE — {c.Condition.Text}");
                Orders.RemoveAt(i);
            }
        }
        for (int i = Contingencies.Count - 1; i >= 0; i--)
        {
            var (contingency, trigger) = Contingencies[i];
            if (!Holds(world, seat, trigger, now)) continue;
            Fired++;
            for (int k = 0; k < contingency.Orders.Count; k++) Give(seat, contingency.Orders[k], vocab);
            Say(seat, now, true, "contingency", $"CONTINGENCY FIRED — {trigger.Condition.Text}: {OrdersText(contingency)}");
            Contingencies.RemoveAt(i);   // once
        }
        for (int i = 0; i < WakeOn.Count; i++)
        {
            if (!Holds(world, seat, WakeOn[i], now)) continue;
            Woke++;
            Say(seat, now, true, "wake", $"WAKE — your condition {WakeOn[i].Condition.Text} holds");
            WakeOn.Clear();   // one wake; the next decision sets new ones
            break;
        }
    }

    private static string OrdersText(Contingency c)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < c.Orders.Count; i++) sb.Append(i > 0 ? "; " : "").Append(DecisionContract.Text(c.Orders[i]));
        return sb.ToString();
    }

    private static void Give(SeatRuntime seat, UnitOrder o, SeatVocabulary? vocab)
    {
        if (vocab != null && DecisionContract.ToAction(o, vocab) is { } a) seat.Channel.Inject(a);
    }

    private static void Say(SeatRuntime seat, double now, bool wake, string state, string text)
        => seat.Channel.Publish(FeedKind.OrderState, now, wake, $"{CommanderFeedSystem.Clock(now)} {text}",
                                new JsonObject { ["kind"] = "order-state", ["state"] = state, ["text"] = text }.ToJsonString());

    /// <summary>⭐ Does <paramref name="c"/> hold now — from what HROT keeps: the squad's position, the commander's picture (its progress, its strength, the
    /// elements reported to him) and the clock. ⛔ Never the enemy's truth: an enemy condition reads only what was REPORTED.</summary>
    public static bool Holds(EntityRepository world, SeatRuntime seat, in ArmedCondition c, double now)
    {
        switch (c.Condition.On)
        {
            case ConditionOn.Immediate: return true;
            case ConditionOn.AtTime: return now >= c.Condition.TimeS;
            case ConditionOn.UnitAt:
                return world.IsAlive(c.Unit) && world.HasComponent<SimTransform>(c.Unit)
                       && Vector2.Distance(Xy((Vector3)world.GetComponentRO<SimTransform>(c.Unit).Position), c.At) <= AtMetres;
        }
        if (!world.HasComponent<CommandPicture>(seat.Commander)) return false;
        ref readonly var pic = ref world.GetComponentRO<CommandPicture>(seat.Commander);
        switch (c.Condition.On)
        {
            case ConditionOn.UnitTaskComplete:
            {
                int r = CommandPictureOps.RowOf(in pic, c.UnitNetId);
                if (r < 0) return false;
                var sub = pic.Subordinates.Items[r];
                return sub.LastReportAt.Seconds >= c.ArmedAt
                       && sub.Progress is ProgressKind.Set or ProgressKind.ObjectiveSecure or ProgressKind.BuildingClear or ProgressKind.TargetsDown;
            }
            case ConditionOn.StrengthBelow:
            {
                int r = CommandPictureOps.RowOf(in pic, c.UnitNetId);
                return r >= 0 && Percent(pic.Subordinates.Items[r].Strength) is { } pct && pct < c.Condition.Percent;
            }
            case ConditionOn.Contact:
                for (int i = 0; i < pic.Elements.Count; i++)
                    if (pic.Elements.Items[i].FromNetId == c.UnitNetId && pic.Elements.Items[i].ReceivedAt.Seconds >= c.ArmedAt) return true;
                return false;
            case ConditionOn.EnemySeenAt:
                for (int i = 0; i < pic.Elements.Count; i++)
                {
                    var el = pic.Elements.Items[i];
                    if (el.ReceivedAt.Seconds >= c.ArmedAt && Vector2.Distance(Xy(el.At), c.At) <= SeenMetres) return true;
                }
                return false;
        }
        return false;
    }

    /// <summary>The reported strength colour as a percent (Green 100 · Amber 75 · Red 50 · Black 25); unknown ⇒ null (never "below").</summary>
    public static int? Percent(StatusColour colour) => colour switch
    {
        StatusColour.Green => 100, StatusColour.Amber => 75, StatusColour.Red => 50, StatusColour.Black => 25, _ => null,
    };

    private static Vector2 Xy(Vector3 v) => new(v.X, v.Y);
}

/// <summary>
/// ⭐ <c>CE-4684</c> C-2 — evaluates every seat's <see cref="ConditionWatcher"/> once a frame, serially, after the action system (what it gives is taken
/// on the next frame's action pass). Sim time only: a held or paused clock watches nothing.
/// </summary>
[UpdateInPhase(SystemPhase.Simulation)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class CommanderWatchSystem : IEcsModuleSystem
{
    private CommanderSeats? _seats;

    public CommanderWatchSystem(CommanderSeats? seats = null) => _seats = seats;

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;
        var seats = _seats ??= CommanderSeats.For(world);
        if (seats.Count == 0) return;
        double now = CommanderFeedSystem.Now(world);
        foreach (var seat in seats.All)
            if (world.IsAlive(seat.Commander)) seat.Watch.Evaluate(world, seat, now);
    }
}
