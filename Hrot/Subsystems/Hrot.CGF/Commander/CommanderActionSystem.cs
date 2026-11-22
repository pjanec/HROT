using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
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
[UpdateInPhase(SystemPhase.Simulation)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class CommanderActionSystem : IEcsModuleSystem
{
    /// <summary>The most a think-pause frame waits for a mind (wall time — a guard against a hung mind, never a pace).</summary>
    public static readonly TimeSpan ThinkPauseBudget = TimeSpan.FromSeconds(2);

    /// <summary>The orders L-1 takes (a squad fulfils each: Move / Hold / Attack by its class, Defend / BreakContact as the platoon plays send them).</summary>
    public static readonly string[] Orders = { "Move", "Hold", "Attack", "SupportByFire", "Defend", "BreakContact" };

    private readonly CommanderSeats _seats;

    public CommanderActionSystem(CommanderSeats seats) => _seats = seats ?? throw new ArgumentNullException(nameof(seats));

    /// <summary>Actions given / refused since construction (a rail / diagnostics probe).</summary>
    public int Given { get; private set; }
    public int Refused { get; private set; }

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world || _seats.Count == 0) return;
        double now = CommanderFeedSystem.Now(world);
        foreach (var seat in _seats.All)
        {
            if (!world.IsAlive(seat.Commander) || !world.HasComponent<CommanderSeat>(seat.Commander)) continue;
            if (seat.Runner != null && world.GetComponentRO<CommanderSeat>(seat.Commander).Mode == CommanderMode.ThinkPause)
                seat.Channel.WaitAnswered(ThinkPauseBudget);
            while (seat.Channel.TryTake(out var action)) Apply(world, seat, action, now);
        }
    }

    private void Apply(EntityRepository world, SeatRuntime seat, CommanderAction a, double now)
    {
        string? why = Act(world, seat, a);
        string what = a.Kind == CommanderActionKind.Order ? $"{a.Order} {a.Squad}" : $"{a.Kind} {a.Squad}".TrimEnd();
        if (a.Kind == CommanderActionKind.Note)
        {
            seat.Channel.Publish(FeedKind.OrderState, now, false, $"{CommanderFeedSystem.Clock(now)} NOTE: {a.Text}",
                                 new JsonObject { ["kind"] = "order-state", ["state"] = "noted", ["text"] = a.Text, ["seq"] = a.MadeOnSeq }.ToJsonString());
            return;
        }
        var json = new JsonObject
        {
            ["kind"] = "order-state", ["t"] = CommanderFeedSystem.Round(now), ["squad"] = a.Squad, ["order"] = a.Kind == CommanderActionKind.Order ? a.Order : a.Kind.ToString(),
            ["where"] = a.Where, ["state"] = why == null ? "accepted" : "refused", ["seq"] = a.MadeOnSeq,
        };
        if (why != null) json["reason"] = why;
        if (why == null) Given++; else Refused++;
        seat.Channel.Publish(FeedKind.OrderState, now, why != null,
                             why == null ? $"{CommanderFeedSystem.Clock(now)} ORDER {what}{(a.Where is { } w ? " → " + w : "")}: given"
                                         : $"{CommanderFeedSystem.Clock(now)} ORDER {what}: REFUSED — {why}",
                             json.ToJsonString());
    }

    /// <summary>Validates and gives one action; null when given, else why not (nothing changed).</summary>
    private static string? Act(EntityRepository world, SeatRuntime seat, CommanderAction a)
    {
        if (a.Kind == CommanderActionKind.Note) return null;
        var squad = SquadNamed(world, seat.Commander, a.Squad, out string? why);
        if (squad.IsNull) return why;
        if (!CombatEffective.IsInTheFight(world, squad)) return $"{a.Squad} is out of the fight";

        switch (a.Kind)
        {
            case CommanderActionKind.Hold:
                CommandOrders.Order(world, seat.Commander, squad, "Hold", "{}", newOrder: true);
                return null;
            case CommanderActionKind.AskSitrep:
                CommandOrders.AskSitrep(world, squad);
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
            case "SupportByFire":   // ⭐ R-8303 — the base of fire: an Attack that holds back, standing at the position
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

    private static string Point(Vector2 at)
        => string.Create(CultureInfo.InvariantCulture, $"[{Math.Round(at.X, 2)},{Math.Round(at.Y, 2)},0]");
}
