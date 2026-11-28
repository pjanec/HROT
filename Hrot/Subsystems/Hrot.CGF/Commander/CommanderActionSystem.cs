using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Squad;
using Hrot.AI.Behaviors.Brains;
using Hrot.AI.Behaviors.Orders;
using Hrot.Map.Definitions.Behavior;

namespace Hrot.CGF.Commander;

/// <summary>
/// ⭐⭐ <c>CE-4676</c> (DESIGN_LLM_Commander §3, §5, §7) — the ONE door from a mind into the simulation, on the frame thread, before the intent resolver:
/// it drains each seat's actions, VALIDATES them (the squad is his and in the fight, the order is one a squad takes, the place resolves), and gives them
/// through <c>CommandOrders</c> at origin Superior — as if the commander's own play gave them (⛔ no second order path). Every action is answered with an
/// <c>order-state</c> item: accepted, or refused WITH the reason (a refusal changes nothing).
/// ⭐ Think-pause (<see cref="CommanderMode.ThinkPause"/>): before draining, the frame waits (bounded by <see cref="ThinkPauseBudget"/>) until the mind has
/// answered every wake so far — so the scripted mind's orders land on the same frame in every run.
/// </summary>
/// <remarks>⭐⭐ CE-4679 (L-2) — think-pause HOLDS THE CLOCK, not the frame (L-1's decision 1 replaced): a mind that answers within
/// <see cref="FastAnswer"/> lands in the same frame, as before (the scripted mind's runs still repeat); one that takes longer gets the clock paused
/// through the ONE pause path (<c>PauseTimeIntent</c> on the node's bus — the same request ⏸ and <c>/sim/pause</c> make), and resumed
/// (<c>ResumeTimeIntent</c>) when its answer is in — only if the seat paused it and nobody else paused meanwhile: a user's pause outlives the hold. A
/// mind that never answers is bounded (<see cref="MaxHold"/>): the seat says so and resumes with no orders. The frame, the UI and the HTTP server run
/// on throughout. ⚠ The pause lands on the cluster's barrier (a few frames after the wake) — sim time stops then, not on the wake's frame.</remarks>
[UpdateInPhase(SystemPhase.Simulation)]
[TickMode(TickPolicy.EveryFrame)]   // ⭐ CE-4679 — a held clock is paused: the hold must still end, and the answer still land
public sealed class CommanderActionSystem : IEcsModuleSystem
{
    /// <summary>How long a frame waits in place for a fast mind before the seat holds the clock instead (wall time).</summary>
    public static readonly TimeSpan FastAnswer = TimeSpan.FromMilliseconds(50);

    /// <summary>The longest a seat holds the clock for one wake (wall time) — then "no orders" and the clock runs on.</summary>
    public static readonly TimeSpan MaxHold = ExternalMind.DefaultTimeout + TimeSpan.FromSeconds(10);

    /// <summary>The orders L-1 takes (a squad fulfils each: Move / Hold / Attack by its class, Defend / BreakContact as the platoon plays send them).</summary>
    public static readonly string[] Orders = { "Move", "Hold", "Attack", "SupportByFire", "Defend", "BreakContact" };

    /// <summary>
    /// ⭐ CE-4685 (user `2026-11-24`: "We need to introduce the order kinds to higher level echelons. We cannot call directly a behavior name") — the
    /// order KINDS a commander may give HIS OWN unit (the whole platoon / company): the routing table (<c>ResolutionRoutingTable</c>, through
    /// <c>TacticalIntentResolutionSystem</c>) picks the behaviour for his class and resolution — a platoon's Attack runs <c>PlatoonAttack</c>, a
    /// company's <c>CompanyAttack</c>, a stochastic unit's <c>StochasticUnitAttack</c>. ⛔ The seat never names a behaviour. The seat offers the kinds
    /// his class resolves (<see cref="CommanderFeedSystem.OwnOrdersOf"/>); this is the set without a registry. ⛔ SUPERSEDED (CE-4680 B-1): the
    /// platoon play NAMES (PlatoonAttack / PlatoonDefend / PlatoonBound / PlatoonHold).
    /// </summary>
    public static readonly string[] OwnOrders = { "Attack", "Defend", "Bound", "Hold" };

    /// <summary>A direct squad order that waits for the running play to end (not a verdict — it is given or refused once the play is gone).</summary>
    private const string Deferred = "\u0001after-play";

    private CommanderSeats? _seats;
    private readonly Fdp.Core.FdpEventBus? _clockBus;

    /// <param name="seats">The world's seats; null ⇒ <see cref="CommanderSeats.For"/> the world it runs on.</param>
    /// <param name="clockBus">The node's bus — the cluster's ONE pause request rides it (<c>HrotNodeContext.EventBus</c>); null ⇒ the world's bus (a rail).</param>
    public CommanderActionSystem(CommanderSeats? seats = null, Fdp.Core.FdpEventBus? clockBus = null)
    {
        _seats = seats;
        _clockBus = clockBus;
    }

    /// <summary>Actions given / refused since construction (a rail / diagnostics probe).</summary>
    public int Given { get; private set; }
    /// <summary>⭐ CE-4683 ② — repeats of the order in force answered as no-ops (a rail / diagnostics probe).</summary>
    public int NoOps { get; private set; }
    public int Refused { get; private set; }

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world) return;
        var seats = _seats ??= CommanderSeats.For(world);
        if (seats.Count == 0) return;
        double now = CommanderFeedSystem.Now(world);
        foreach (var seat in seats.All)
        {
            if (!world.IsAlive(seat.Commander) || !world.HasComponent<CommanderSeat>(seat.Commander)) { seat.Holding = false; continue; }   // a gone seat holds nothing
            if (seat.Runner != null && world.GetComponentRO<CommanderSeat>(seat.Commander).Mode == CommanderMode.ThinkPause)
                Hold(world, seats, seat, now);
            // ⭐ CE-4680 B-1 — the direct orders that ended a play land once its run is gone (its release would take an order given in its run)
            if (seat.AfterPlay.Count > 0 && !PlayRuns(world, seat) && PlayReleased(world, seat))
            {
                for (int i = 0; i < seat.AfterPlay.Count; i++) Apply(world, seat, seat.AfterPlay[i], now);
                seat.AfterPlay.Clear();
            }
            while (seat.Channel.TryTake(out var action))
            {
                if (seat.AfterPlay.Count > 0 && action.Kind is CommanderActionKind.Order or CommanderActionKind.Hold) { seat.AfterPlay.Add(action); continue; }   // in order behind the ones waiting
                Apply(world, seat, action, now);
            }
        }
        HoldClock(world, seats);   // ⭐ the duel (§8) — after every seat's hold is known: ONE pause / resume for all of them
    }

    // ── ⭐ CE-4679 — the think-pause hold ─────────────────────────────────────────────────────────

    /// <summary>One seat's hold: marks it while its mind thinks (past an in-frame answer), lets go on the answer or past <see cref="MaxHold"/>.</summary>
    private void Hold(EntityRepository world, CommanderSeats seats, SeatRuntime seat, double now)
    {
        if (!seat.Holding)
        {
            if (!seat.Channel.Thinking || seat.Channel.WaitAnswered(FastAnswer)) return;   // answered in the frame (a fast mind): no hold
            seat.Holding = true;
            seat.HoldSince = System.Diagnostics.Stopwatch.GetTimestamp();
            return;
        }
        bool timedOut = System.Diagnostics.Stopwatch.GetElapsedTime(seat.HoldSince) > MaxHold;
        if (seat.Channel.Thinking && !timedOut) return;
        seat.Holding = false;
        if (timedOut)
            seat.Channel.Publish(FeedKind.OrderState, now, false,
                $"{CommanderFeedSystem.Clock(now)} SEAT: the mind gave no answer in {MaxHold.TotalSeconds:0} s — the clock runs on with no orders",
                new JsonObject { ["kind"] = "order-state", ["state"] = "no-answer", ["seconds"] = MaxHold.TotalSeconds }.ToJsonString());
    }

    /// <summary>
    /// ⭐ The seats' ONE clock hold (the duel, §8): a FREE-running clock is paused once when the first seat holds and resumed once when none does; a pause
    /// someone else asked for while the seats held is theirs and outlives the hold. ⛔ A stepped clock is never paused or resumed — its stepper reads
    /// <see cref="CommanderSeats.AnyHoldsClock"/>.
    /// </summary>
    private void HoldClock(EntityRepository world, CommanderSeats seats)
    {
        var bus = _clockBus ?? world.Bus;
        if (seats.ClockPausedBySeats && bus.IsRegisteredManaged<Fdp.Toolkit.Time.Domain.PauseTimeIntent>())
        {
            int pauses = bus.ReadManaged<Fdp.Toolkit.Time.Domain.PauseTimeIntent>().Count;
            if (pauses > 0 && seats.OwnPauseUnseen) { seats.OwnPauseUnseen = false; pauses--; }   // the seats' own is the first one seen
            if (pauses > 0) seats.ClockUserPaused = true;
        }
        bool any = false;
        foreach (var seat in seats.All) if (seat.Holding) { any = true; break; }
        if (any && !seats.ClockPausedBySeats)
        {
            if (!Advancing(world) || !(seats.ClockRunsFree?.Invoke() ?? true)) return;   // already paused (a user's), or stepped
            seats.ClockPausedBySeats = seats.OwnPauseUnseen = true;
            seats.ClockUserPaused = false;
            Publish(bus, new Fdp.Toolkit.Time.Domain.PauseTimeIntent());
        }
        else if (!any && seats.ClockPausedBySeats)
        {
            seats.ClockPausedBySeats = seats.OwnPauseUnseen = false;
            if (!seats.ClockUserPaused) Publish(bus, new Fdp.Toolkit.Time.Domain.ResumeTimeIntent());
        }
    }

    /// <summary>Two place words name the same place: both resolve, within 2 m (the order no-op's tolerance), or both are empty (PlatoonHold).</summary>
    private static bool SamePlace(SeatRuntime seat, string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return true;
        return seat.Places.Resolve(a, out _) is { } pa && seat.Places.Resolve(b, out _) is { } pb && System.Numerics.Vector2.Distance(pa, pb) <= 2f;
    }

    private static bool Advancing(EntityRepository world)
        => world.HasSingleton<GlobalTime>() && world.GetSingleton<GlobalTime>().DeltaTime > 0f;

    private static void Publish<T>(Fdp.Core.FdpEventBus bus, T intent)
    {
        if (!bus.IsRegisteredManaged<T>()) bus.RegisterManaged<T>();
        bus.PublishManaged(intent);
    }

    /// <summary>⭐ CE-4684 — decisions applied, and refused because a newer cycle had come (a rail / diagnostics probe).</summary>
    public int Decisions { get; private set; }
    public int StaleDecisions { get; private set; }

    /// <summary>
    /// ⭐⭐ CE-4684 (§5c C-1, C-3) — ONE decision: ⛔ refused when a newer wake came after the cycle it answered (the model is asked again — a decision on
    /// an old picture never lands); else each order that starts now goes through the SAME path a tool call does (validated, a repeat a no-op, a refusal
    /// a wake), and the watcher takes the rest: orders waiting for their start, completions, aborts, contingencies, the next decision.
    /// </summary>
    private void ApplyDecision(EntityRepository world, SeatRuntime seat, CommanderAction a, double now)
    {
        var d = a.Plan!;
        var vocab = a.Vocabulary ?? seat.Vocabulary;
        long latest = seat.Channel.LatestWake;
        if (latest > d.Cycle || vocab == null)
        {
            StaleDecisions++;
            seat.Log?.Write(new JsonObject
            {
                ["kind"] = "verdict", ["seq"] = a.MadeOnSeq, ["t"] = CommanderFeedSystem.Round(now), ["state"] = "stale", ["cycle"] = d.Cycle, ["newest"] = latest,
            });
            // ⛔ NOT a wake: the newer news it was stale against is already waiting and re-asks the model. 📌 Measured on the first L-3c relay run: as a
            //   wake, each refusal arrived while the model answered the newer news, made THAT decision stale too, and the seat refused 15 in a row
            //   with the clock held at 00:00:36
            seat.Channel.Publish(FeedKind.OrderState, now, false,
                $"{CommanderFeedSystem.Clock(now)} DECISION for cycle {d.Cycle} REFUSED — newer news came since (cycle {latest}): decide again on this briefing",
                new JsonObject { ["kind"] = "order-state", ["state"] = "stale", ["cycle"] = d.Cycle, ["newest"] = latest }.ToJsonString());
            return;
        }
        Decisions++;
        int given = 0, waiting = 0;
        for (int i = 0; i < d.Orders.Count; i++)
        {
            var o = d.Orders[i];
            if (o.Task == DecisionContract.Continue) continue;
            if (o.Start is { On: not ConditionOn.Immediate }) { waiting++; continue; }
            if (DecisionContract.ToAction(o, vocab) is not { } act) continue;
            given++;
            var action = act with { MadeOnSeq = a.MadeOnSeq };
            if (seat.AfterPlay.Count > 0 && action.Kind is CommanderActionKind.Order or CommanderActionKind.Hold) seat.AfterPlay.Add(action);
            else Apply(world, seat, action, now);
        }
        seat.Watch.Take(world, seat, d, now);
        seat.Log?.Write(new JsonObject
        {
            ["kind"] = "verdict", ["seq"] = a.MadeOnSeq, ["t"] = CommanderFeedSystem.Round(now), ["state"] = "decision", ["cycle"] = d.Cycle,
            ["given"] = given, ["waiting"] = waiting, ["contingencies"] = d.Contingencies.Count, ["nextDecisionS"] = d.NextDecisionS, ["wakeOn"] = d.WakeOn.Count,
        });
        seat.Channel.Publish(FeedKind.OrderState, now, false,
            $"{CommanderFeedSystem.Clock(now)} DECISION cycle {d.Cycle}: {given} order(s) given, {waiting} waiting for their start, {d.Contingencies.Count} contingenc{(d.Contingencies.Count == 1 ? "y" : "ies")} armed; next decision by {CommanderFeedSystem.Clock(seat.Watch.NextDecisionAt)}",
            new JsonObject { ["kind"] = "order-state", ["state"] = "decision", ["cycle"] = d.Cycle, ["given"] = given, ["waiting"] = waiting }.ToJsonString());
    }

    private void Apply(EntityRepository world, SeatRuntime seat, CommanderAction a, double now)
    {
        if (a.Kind == CommanderActionKind.Decision) { if (a.Plan != null) ApplyDecision(world, seat, a, now); return; }
        // ⭐⭐ CE-4683 ② (§5a-4) — the same order as the one in force is a NO-OP: answered, nothing sent, a running play not ended
        if (a.Kind is CommanderActionKind.Order or CommanderActionKind.Hold
            && SquadNamed(world, seat.Commander, a.Squad, out _) is { IsNull: false } target)
        {
            SyncPlayOrders(world, seat, now);
            // ⭐ CE-4686 ③ — an order its unit REPORTED failed is no longer in force: the same order again is a new order, not a no-op. 📌 Measured
            //   (Haiku, llm-company-aggregated): 2nd Platoon's support by fire was cleared at its split (TaskFailed) and the model's re-issue was
            //   answered "already in force — nothing sent"
            if (seat.InForce.ContainsKey(target) && ReportedFailed(world, seat, target)) seat.InForce.Remove(target);
            if (seat.InForce.TryGetValue(target, out var held) && Wanted(seat, a, now) is { } wanted && Same(held, wanted))
            {
                NoOps++;
                seat.Log?.Write(new JsonObject
                {
                    ["kind"] = "verdict", ["seq"] = a.MadeOnSeq, ["t"] = CommanderFeedSystem.Round(now), ["action"] = CommanderTools.ToJson(a),
                    ["state"] = "in-force", ["since"] = CommanderFeedSystem.Round(held.Since),
                });
                seat.Channel.Publish(FeedKind.OrderState, now, false,
                    $"{CommanderFeedSystem.Clock(now)} ORDER {held.Text} {a.Squad}: already in force since {CommanderFeedSystem.Clock(held.Since)} ({held.By}) — nothing sent",
                    new JsonObject { ["kind"] = "order-state", ["state"] = "in-force", ["squad"] = a.Squad, ["order"] = held.Kind, ["since"] = CommanderFeedSystem.Round(held.Since), ["by"] = held.By, ["seq"] = a.MadeOnSeq }.ToJsonString());
                return;
            }
        }
        // ⭐ the duel (§8) — the same PLAY on the same place while it runs is a NO-OP too: re-sent, it started the play over from its first phase
        //   (📌 the duel's Blue re-ran PlatoonAttack at 01:05, trusting a 65 s old snapshot's "none")
        if (a.Kind == CommanderActionKind.RunPlay && seat.Play is { } running && running.Equals(a.Order, StringComparison.OrdinalIgnoreCase)
            && PlayRuns(world, seat) && SamePlace(seat, seat.PlayWhere, a.Where))
        {
            NoOps++;
            seat.Log?.Write(new JsonObject
            {
                ["kind"] = "verdict", ["seq"] = a.MadeOnSeq, ["t"] = CommanderFeedSystem.Round(now), ["action"] = CommanderTools.ToJson(a),
                ["state"] = "in-force", ["since"] = CommanderFeedSystem.Round(seat.PlaySince),
            });
            seat.Channel.Publish(FeedKind.OrderState, now, false,
                $"{CommanderFeedSystem.Clock(now)} {seat.Echelon.ToUpperInvariant()} ORDER {running} → {seat.PlayWhere}: already in force since {CommanderFeedSystem.Clock(seat.PlaySince)} — nothing sent",
                new JsonObject { ["kind"] = "order-state", ["state"] = "in-force", ["play"] = running, ["since"] = CommanderFeedSystem.Round(seat.PlaySince), ["seq"] = a.MadeOnSeq }.ToJsonString());
            return;
        }
        string? why = Act(world, seat, a);
        string what = a.Kind is CommanderActionKind.Order or CommanderActionKind.RunPlay ? $"{a.Order} {a.Squad}".TrimEnd() : $"{a.Kind} {a.Squad}".TrimEnd();
        if (ReferenceEquals(why, Deferred) && seat.Play == null) { seat.AfterPlay.Add(a); return; }   // the play ended, its release not yet in
        if (ReferenceEquals(why, Deferred))
        {
            // ⭐ CE-4680 B-1 — no tug of war: the play ends first, said in the feed; the order lands once it has
            seat.Channel.Publish(FeedKind.OrderState, now, false,
                                 $"{CommanderFeedSystem.Clock(now)} {seat.Echelon.ToUpperInvariant()} ORDER {seat.Play} ENDED — a direct order to {a.Squad}; it lands once that order's own orders are released",
                                 new JsonObject { ["kind"] = "order-state", ["state"] = "play-ended", ["play"] = seat.Play, ["for"] = CommanderTools.ToJson(a), ["seq"] = a.MadeOnSeq }.ToJsonString());
            EndPlay(world, seat);
            seat.AfterPlay.Add(a);
            return;
        }
        if (a.Kind == CommanderActionKind.Note)
        {
            // ⭐ CE-4683 ① — his estimate lives in the briefing (replaced, capped); ⛔ no longer echoed into the feed (it was there twice). The node log keeps it
            Fdp.Core.Logging.FdpLog<CommanderActionSystem>.Info("[Commander] {0} {1} NOTE: {2}", seat.Name, CommanderFeedSystem.Clock(now), a.Text ?? "");
            return;
        }
        var json = new JsonObject
        {
            ["kind"] = "order-state", ["t"] = CommanderFeedSystem.Round(now), ["squad"] = a.Squad, ["order"] = a.Kind == CommanderActionKind.Order ? a.Order : a.Kind.ToString(),
            ["where"] = a.Where, ["state"] = why == null ? "accepted" : "refused", ["seq"] = a.MadeOnSeq,
        };
        if (why != null) json["reason"] = why;
        if (why == null) Given++; else Refused++;
        if (why == null && a.Kind is CommanderActionKind.Order or CommanderActionKind.Hold
            && SquadNamed(world, seat.Commander, a.Squad, out _) is { IsNull: false } ordered)   // ⭐ CE-4683 ② — the order now in force, as he gave it
            seat.InForce[ordered] = Wanted(seat, a, now) ?? new GivenOrder(a.Order ?? a.Kind.ToString(), a.Where ?? a.Target, null, now, "you");
        seat.Log?.Write(new JsonObject
        {
            ["kind"] = "verdict", ["seq"] = a.MadeOnSeq, ["t"] = CommanderFeedSystem.Round(now), ["action"] = CommanderTools.ToJson(a),
            ["state"] = why == null ? "accepted" : "refused", ["reason"] = why,
        });
        seat.Channel.Publish(FeedKind.OrderState, now, why != null,
                             why == null ? $"{CommanderFeedSystem.Clock(now)} ORDER {what}{(a.Where is { } w ? " → " + w : "")}: given"
                                         : $"{CommanderFeedSystem.Clock(now)} ORDER {what}: REFUSED — {why}",
                             json.ToJsonString());
    }

    /// <summary>Validates and gives one action; null when given, else why not (nothing changed).</summary>
    private static string? Act(EntityRepository world, SeatRuntime seat, CommanderAction a)
    {
        if (a.Kind == CommanderActionKind.Note) return null;
        if (a.Kind == CommanderActionKind.RunPlay) return RunPlay(world, seat, a);
        if (a.Kind == CommanderActionKind.StopPlay)
        {
            if (!PlayRuns(world, seat)) return "no play is running";
            EndPlay(world, seat);
            return null;
        }
        var squad = SquadNamed(world, seat.Commander, a.Squad, out string? why);
        if (squad.IsNull) return why;
        if (!CombatEffective.IsInTheFight(world, squad)) return $"{a.Squad} is out of the fight";

        // ⭐ CE-4683 — asking for a report is not an order: it never ends a running play. 📌 Found by the L-3a Haiku relay run: an ask_sitrep at
        //   0:59 ENDED PlatoonAttack (it went through the direct-order path below), and the model had to start the play again at 1:29.
        if (a.Kind == CommanderActionKind.AskSitrep)
        {
            CommandOrders.AskSitrep(world, squad);
            return null;
        }
        if (PlayRuns(world, seat) || !PlayReleased(world, seat)) return Deferred;   // ⭐ CE-4680 B-1 — a direct squad order ends the play first
        switch (a.Kind)
        {
            case CommanderActionKind.Hold:
                CommandOrders.Order(world, seat.Commander, squad, "Hold", "{}", newOrder: true);
                return null;
            case CommanderActionKind.Order:
                break;
            default:
                return $"'{a.Kind}' is not an action";
        }

        string? order = Array.Find(Orders, o => o.Equals(a.Order, StringComparison.OrdinalIgnoreCase));
        if (order == null) return $"'{a.Order}' is not an order a squad takes here — {string.Join(", ", Orders)}";
        if (order == "Hold") { CommandOrders.Order(world, seat.Commander, squad, "Hold", "{}", newOrder: true); return null; }

        if (seat.Places.Resolve(a.Where ?? a.Target, out why) is not { } at) return why;
        string p = Point(at);
        switch (order)
        {
            case "Move":
                CommandOrders.Order(world, seat.Commander, squad, "Move", $"{{\"Location\":{p}}}", newOrder: true);
                break;
            case "Attack":
                CommandOrders.Order(world, seat.Commander, squad, "Attack", $"{{\"Position\":{p}}}", newOrder: true);
                break;
            case "SupportByFire":
                // ⭐ CE-4686 ③ — the base of fire STANDS at the place (a decision's support_at, else its objective) and fires at the target (the decision's
                //   objective when a support_at was given). By the form the ONE routing table resolves for THAT subordinate: a physical platoon takes the
                //   KIND SupportByFire (PlatoonSupportByFire: Position = where it sets, Post1 = what it fires at); a squad or a stochastic unit has no
                //   SupportByFire play of its own ⇒ Attack + HoldBack, standing at the place (R-8303, CE-8302 R-8341 — the generic attack play reads HoldBack).
                //   📌 Measured (CE-4685, Haiku): the old form sent a platoon to STAND on Red's position (the objective), and a physical platoon has no
                //   hold-back Attack play (R-8392) — 2nd Platoon stalled ~180 m short
                string post = "";
                if (a.Where != null && a.Target != null)
                {
                    if (seat.Places.Resolve(a.Target, out why) is not { } fireAt) return why;
                    post = $",\"Post1\":{Point(fireAt)}";
                }
                if (TakesKind(world, seat, squad, OrderKind.SupportByFire))
                    CommandOrders.Order(world, seat.Commander, squad, "SupportByFire", $"{{\"Position\":{p}{post}}}", newOrder: true);
                else
                    CommandOrders.Order(world, seat.Commander, squad, "Attack", $"{{\"Position\":{p},\"HoldBack\":true}}", newOrder: true);
                break;
            case "Defend":
                CommandOrders.Route(world, seat.Commander, squad, OrderKind.Defend, BehaviorNames.SquadDefend, $"{{\"Position\":{p},\"SearchRadius\":30}}", newOrder: true);
                break;
            case "BreakContact":
                CommandOrders.Route(world, seat.Commander, squad, OrderKind.BreakContact, BehaviorNames.BreakContact, $"{{\"To\":{p}}}", newOrder: true);
                break;
        }
        return null;
    }

    /// <summary>⭐ CE-4686 ③ — the commander's picture says <paramref name="unit"/> reported its current order FAILED (its latest progress since the order).</summary>
    private static bool ReportedFailed(EntityRepository world, SeatRuntime seat, Entity unit)
    {
        if (!world.HasComponent<Fdp.Toolkit.Squad.Command.CommandPicture>(seat.Commander)) return false;
        ref readonly var picture = ref world.GetComponentRO<Fdp.Toolkit.Squad.Command.CommandPicture>(seat.Commander);
        int r = Fdp.Toolkit.Squad.Command.CommandPictureOps.RowOf(in picture, Fdp.Toolkit.Squad.Command.CommandPictureOps.NetIdOf(world, unit));
        return r >= 0 && picture.Subordinates.Items[r].Progress == Fdp.Toolkit.Squad.Command.ProgressKind.TaskFailed;
    }

    /// <summary>⭐ CE-4686 ③ — does <paramref name="unit"/> take the order KIND <paramref name="kind"/> as a play of its own (the routing table, at its resolution)?</summary>
    private static bool TakesKind(EntityRepository world, SeatRuntime seat, Entity unit, OrderKind kind)
    {
        if (seat.IsBehaviour == null) return false;
        var resolution = Fdp.Toolkit.Aggregate.AggregateUnits.IsStochastic(world, unit)
            ? Fdp.Toolkit.Aggregate.AggregateResolution.Stochastic : Fdp.Toolkit.Aggregate.AggregateResolution.Physical;
        string? play = OrderReissue.PlayFor(world, unit, resolution, kind, seat.IsBehaviour);
        return play != null && !Hrot.Core.Tkb.ResolutionRoutingTable.IsStochasticPlay(play);   // the generic stochastic play reads HoldBack, not the kind
    }

    // ── ⭐ CE-4680 B-1 — the platoon plays as the commander's own task ───────────────────────────────

    /// <summary>
    /// The play as the commander's TASK: the order a superior gives a platoon (<c>AssignTacticalIntentEvent</c> at origin Superior — what
    /// <c>CommandOrders.Assign</c> publishes; the seat has no giver to record it on), with its objective from the gazetteer. The play then orders the
    /// squads (behaviors' code). A new play replaces the running one — its run ends and its squad orders are released (<c>SubordinateOrders.Release</c>).
    /// </summary>
    private static string? RunPlay(EntityRepository world, SeatRuntime seat, CommanderAction a)
    {
        // ⭐ CE-4685 — an order KIND to his own unit (the kinds his class resolves); the routing table picks the behaviour
        string? kind = Array.Find(seat.OwnOrders, k => k.Equals(a.Order, StringComparison.OrdinalIgnoreCase));
        if (kind == null) return $"'{a.Order}' is not an order your {seat.Echelon} takes as a whole — {string.Join(", ", seat.OwnOrders)}";
        string json = "{}";
        if (kind != "Hold")
        {
            if (seat.Places.Resolve(a.Where, out string? why) is not { } at) return why ?? $"{kind} needs a place (where)";
            string p = Point(at);
            if (kind == "Bound")
            {
                string support = p;   // the covering element sets at the objective unless told where
                if (a.Target is { Length: > 0 })
                {
                    if (seat.Places.Resolve(a.Target, out why) is not { } sp) return why;
                    support = Point(sp);
                }
                json = $"{{\"Position\":{p},\"SupportPoint\":{support}}}";   // ⭐ CE-4686 ④ — the neutral BoundOrder (every Bound play reads it, a stochastic unit too)
            }
            else json = $"{{\"Position\":{p},\"SearchRadius\":30}}";   // the neutral Attack / Defend order (R-8300: every echelon's play reads it)
        }
        if (!world.Bus.IsRegisteredManaged<AssignTacticalIntentEvent>()) world.Bus.RegisterManaged<AssignTacticalIntentEvent>();
        world.Bus.PublishManaged(new AssignTacticalIntentEvent
        {
            Entity = seat.Commander, IntentId = Hrot.Map.Definitions.Behavior.Orders.OrderAttribute.IntentFor(kind), JsonParams = json, Origin = BehaviorOrigin.Superior,
        });
        seat.Play = kind;
        seat.PlayWhere = a.Where;
        seat.PlaySince = CommanderFeedSystem.Now(world);
        seat.InForce.Clear();   // ⭐ CE-4683 — his order orders the units now; their orders come into the table as it gives them (SyncPlayOrders)
        return null;
    }

    /// <summary>The play the seat gave still runs as the commander's task (and its run is remembered, so its release can be waited for).</summary>
    private static bool PlayRuns(EntityRepository world, SeatRuntime seat)
    {
        // ⭐ CE-4685 — by its KIND (CurrentOrders: the order a unit runs, read by kind — never by the behaviour's name)
        var current = Hrot.AI.Behaviors.Orders.CurrentOrders.Of(world, seat.Commander);
        bool runs = seat.Play != null && current.Exists && current.Origin == BehaviorOrigin.Superior
                    && current.Kind.ToString().Equals(seat.Play, StringComparison.Ordinal);
        if (runs) seat.PlayRun = BehaviorOwnedParts.OwnerOf(world, seat.Commander);
        return runs;
    }

    /// <summary>
    /// ⭐ The play's run has ended AND released its squad orders. 📌 Measured on the rail: the behaviour hash changes BEFORE the release — an order given
    /// in between is recorded under the play's run (<c>CommandOrders.Order</c> records the CURRENT run) and is cleared with the play's orders.
    /// </summary>
    private static bool PlayReleased(EntityRepository world, SeatRuntime seat)
    {
        if (seat.PlayRun == 0) return true;
        if (BehaviorOwnedParts.OwnerOf(world, seat.Commander) == seat.PlayRun) return false;
        if (SubordinateOrders.Of(world, seat.Commander) is { } held)
            for (int i = 0; i < held.Orders.Count; i++)
                if (held.Orders[i].Run == seat.PlayRun) return false;
        seat.PlayRun = 0;
        return true;
    }

    /// <summary>Ends the play: a clear of the commander's task at origin Superior (its run ends and releases its squad orders).</summary>
    private static void EndPlay(EntityRepository world, SeatRuntime seat)
    {
        if (world.Bus.IsRegistered<ClearBehaviorEvent>())
            world.Bus.Publish(new ClearBehaviorEvent { Entity = seat.Commander, Origin = BehaviorOrigin.Superior });
        seat.Play = null;
        seat.PlayWhere = null;
        // ⭐ CE-4683 — the play's orders are released with it: they are no longer known to be in force
        foreach (var (squad, o) in new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<Entity, GivenOrder>>(seat.InForce))
            if (o.By != "you") seat.InForce.Remove(squad);   // given by his own order (CE-4685: "<echelon> order <kind>")
    }

    /// <summary>The commander's own squad of that name, alive; ⛔ never another commander's unit (that is how another side's unit is refused).</summary>
    internal static Entity SquadNamed(EntityRepository world, Entity commander, string? name, out string? why)
    {
        why = null;
        var roster = CommanderFeedSystem.Squads(world, commander);
        string mine = "";
        for (int i = 0; i < roster.Count; i++)
        {
            var e = roster.SubordinateEntities[i];
            if (!world.IsAlive(e)) continue;
            string n = CommanderFeedSystem.NameOf(world, e);
            if (string.Equals(n, name?.Trim(), StringComparison.OrdinalIgnoreCase)) return e;
            mine += (mine.Length == 0 ? "" : ", ") + n;
        }
        why = string.IsNullOrWhiteSpace(name) ? "no squad named" : $"no squad named '{name}' under {CommanderFeedSystem.NameOf(world, commander)} (his: {(mine.Length == 0 ? "none" : mine)})";
        return Entity.Null;
    }

    /// <summary>⭐ CE-4683 ② — a direct order as an in-force entry (its kind in the seat's vocabulary, the point it resolves to); null = not an order the
    /// seat gives (an unknown kind or place — Act refuses it).</summary>
    private static GivenOrder? Wanted(SeatRuntime seat, CommanderAction a, double now)
    {
        string? kind = a.Kind == CommanderActionKind.Hold ? "Hold" : Array.Find(Orders, o => o.Equals(a.Order, StringComparison.OrdinalIgnoreCase));
        if (kind == null) return null;
        if (kind == "Hold") return new GivenOrder("Hold", null, null, now, "you");
        if (seat.Places.Resolve(a.Where ?? a.Target, out _) is not { } at) return null;
        return new GivenOrder(kind, a.Where ?? a.Target, at, now, "you");
    }

    /// <summary>⭐ CE-4683 ② — the same order: the same kind and a point within 2 m (so <c>"house"</c> and <c>"House"</c> are one place).</summary>
    private static bool Same(GivenOrder held, GivenOrder wanted)
        => held.Kind.Equals(wanted.Kind, StringComparison.OrdinalIgnoreCase)
           && (held.Point is not { } p ? wanted.Point is null : wanted.Point is { } q && Vector2.Distance(p, q) <= 2f);

    /// <summary>
    /// ⭐ CE-4683 ② — the running play's squad orders into the in-force table (its <c>SubordinateOrders</c> under the play's run): a new or changed one
    /// is recorded with <c>since</c> = now (the entries carry no time). Orders the seat cannot name (a behaviour the play chose itself) are shown by
    /// that name and never match a repeat.
    /// </summary>
    internal static void SyncPlayOrders(EntityRepository world, SeatRuntime seat, double now)
    {
        if (seat.Play == null || !PlayRuns(world, seat) || SubordinateOrders.Of(world, seat.Commander) is not { } held) return;
        for (int i = 0; i < held.Orders.Count; i++)
        {
            var e = held.Orders[i];
            if (e.Run != seat.PlayRun || !world.IsAlive(e.Member)) continue;
            var o = Decode(seat, e.Behavior, e.Json, now, seat.Echelon + " order " + seat.Play);
            if (o == null) continue;
            if (seat.InForce.TryGetValue(e.Member, out var had) && Same(had, o) && had.By == o.By) continue;
            seat.InForce[e.Member] = o;
        }
    }

    private static GivenOrder? Decode(SeatRuntime seat, string behavior, string? json, double now, string by)
    {
        if (string.IsNullOrEmpty(behavior)) return null;
        string name = behavior.StartsWith("Order.", StringComparison.Ordinal) ? behavior.Substring(6) : behavior;
        Vector2? at = null;
        bool holdBack = false;
        try
        {
            if (JsonNode.Parse(json ?? "") is JsonObject j)
            {
                if ((j["Location"] ?? j["Position"] ?? j["To"] ?? j["Objective"]) is JsonArray pt && pt.Count >= 2)
                    at = new Vector2((float)pt[0]!.GetValue<double>(), (float)pt[1]!.GetValue<double>());
                holdBack = j["HoldBack"] is JsonValue hb && hb.TryGetValue(out bool b) && b;
            }
        }
        catch (System.Text.Json.JsonException) { }
        string kind = name switch
        {
            "Attack" when holdBack => "SupportByFire",
            _ when name == BehaviorNames.SquadDefend => "Defend",
            _ => name,
        };
        return new GivenOrder(kind, at is { } p ? seat.Places.Describe(p) : null, at, now, by);
    }

    private static string Point(Vector2 at)
        => string.Create(CultureInfo.InvariantCulture, $"[{Math.Round(at.X, 2)},{Math.Round(at.Y, 2)},0]");
}
