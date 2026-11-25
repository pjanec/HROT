using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;
using Hrot.AI.Behaviors.Brains;

namespace Hrot.CGF.Commander;

/// <summary>⭐ <c>CE-4683</c> ② — an order in force on a squad: its kind (the seat's order vocabulary), where as said, the resolved point, since when, and
/// who gave it (<c>"you"</c> or <c>"platoon order Attack"</c>).</summary>
public sealed record GivenOrder(string Kind, string? Where, System.Numerics.Vector2? Point, double Since, string By)
{
    public string Text => Where is { Length: > 0 } w ? $"{Kind} → {w}" : Kind;
}

/// <summary>⭐ <c>CE-4676</c> — one seat's runtime on the frame thread: its channel, its mind's runner, its places, and what the feed has already said.
/// ⭐ CE-4679 — its identity, mind and mode are also readable from any thread (the HTTP routes).</summary>
public sealed class SeatRuntime : IDisposable
{
    public Entity Commander { get; init; }
    public string Name { get; init; } = "";
    /// <summary>⭐ CE-4679 — the commander's network id: the seat's id on the HTTP surface.</summary>
    public int NetworkId { get; init; }
    public string Side { get; init; } = "";
    public CommanderChannel Channel { get; } = new();
    public Gazetteer Places { get; init; } = new();
    public string Prefix { get; internal set; } = "";
    public MindRunner? Runner { get; set; }
    public ModelLog? Log { get; set; }
    public CommanderMind Mind { get; internal set; }
    public CommanderMode Mode { get; internal set; }
    /// <summary>⭐ CE-4684 (§5c) — the seat's mind answers with decision objects (C-1): its prefix speaks ids only (C-6) and its pace is the model's own
    /// next decision (C-3).</summary>
    public bool Contract { get; internal set; }
    /// <summary>⭐ CE-4684 C-6 — the ids the seat's decisions may use.</summary>
    public SeatVocabulary? Vocabulary { get; internal set; }
    /// <summary>⭐ CE-4684 C-2 — what the last decision left HROT to do without a model call.</summary>
    public ConditionWatcher Watch { get; } = new();
    /// <summary>⭐ CE-4685 A-1 — the commander's echelon and his subordinates' ("platoon" / "squad", "company" / "platoon"), from their classes.</summary>
    public string Echelon { get; internal set; } = "platoon";
    public string Subordinate { get; internal set; } = "squad";
    /// <summary>⭐ CE-4685 A-2 — the order KINDS his own unit takes as a whole (those of Attack / Defend / Bound / Hold the routing table resolves for his
    /// class); the routing table picks the behaviour, ⛔ never the seat.</summary>
    public string[] OwnOrders { get; internal set; } = CommanderActionSystem.OwnOrders;
    /// <summary>⭐ CE-4686 ③ — is a name a registered behaviour (the routing table's argument); null without a registry.</summary>
    internal Func<string, bool>? IsBehaviour;
    /// <summary>⭐ CE-4685 A-3 — the subordinates (by roster index) that were STOCHASTIC when last looked at: a bit cleared is a split said once.</summary>
    internal ulong Aggregated;

    // what the feed already said (frame thread)
    internal double LastItemAt, LastSnapshotAt, LastFightCheckAt;
    /// <summary>⭐ CE-4679 — the last item that asked the mind to think (a wake or a heartbeat): the heartbeat counts from it, not from any item.</summary>
    internal double LastWakeAt;
    internal bool HasHead;
    internal CommandReportEntry Head;
    internal ulong OutOfFight;

    // ⭐ CE-4679 — the think-pause hold (frame thread, CommanderActionSystem)
    internal volatile bool Holding;   // read from any thread (a stepping run's hold, GET /commanders)

    // ⭐ CE-4680 B-1 — the play the seat gave its commander as his task (frame thread), and the direct orders waiting for it to end
    internal string? Play;
    internal uint PlayRun;
    /// <summary>⭐ CE-4683 ② (§5a-4) — the order IN FORCE on each squad: his own last direct order, or the play's (read from <c>SubordinateOrders</c>) —
    /// ONE table the snapshot shows and the action system checks a repeat against. ⛔ Was CE-4680 B-4's <c>Given</c> (text only, own orders only).</summary>
    internal readonly System.Collections.Generic.Dictionary<Entity, GivenOrder> InForce = new();
    /// <summary>⭐ CE-4683 — where the running play aims and since when (the snapshot names it).</summary>
    internal string? PlayWhere;
    internal double PlaySince;
    internal readonly System.Collections.Generic.List<CommanderAction> AfterPlay = new();
    internal long HoldSince;

    /// <summary>True while this seat holds the clock for its mind.</summary>
    public bool HoldsClock => Holding;

    public void Dispose()
    {
        Runner?.Dispose();
        Log?.Dispose();
    }
}

/// <summary>
/// ⭐ <c>CE-4676</c> — the seats of one world, shared by <see cref="CommanderFeedSystem"/> (which opens and closes them) and
/// <see cref="CommanderActionSystem"/> (which drains them). ⭐ CE-4679 — kept WITH the world (<see cref="For"/>, the <c>SquadReportLog</c> way), so the
/// HTTP surface finds them on the brain world; the dictionary is frame-thread only, <see cref="Snapshot"/> is safe from any thread.
/// </summary>
public sealed class CommanderSeats : IDisposable
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EntityRepository, CommanderSeats> s_seats = new();

    /// <summary>The seats of <paramref name="world"/>, made on first use.</summary>
    public static CommanderSeats For(EntityRepository world) => s_seats.GetValue(world, _ => new CommanderSeats());

    /// <summary>The seats only if <paramref name="world"/> has them (readers never create them).</summary>
    public static CommanderSeats? Peek(EntityRepository world) => s_seats.TryGetValue(world, out var seats) ? seats : null;

    private readonly Dictionary<Entity, SeatRuntime> _seats = new();
    private SeatRuntime[] _snapshot = Array.Empty<SeatRuntime>();

    /// <summary>Makes the mind a seat asks for; null = not built yet (the seat then says so and gives no orders). Rails replace it.</summary>
    public Func<CommanderSeat, IMind?> MindFor { get; set; } = seat => seat.Mind switch
    {
        CommanderMind.Scripted => new ScriptedMind { UsePlays = true },   // ⭐ CE-4680 — the scripted commander runs the platoon's plays
        CommanderMind.External => new ExternalMind(),
        CommanderMind.ChatModel => new ChatModelMind(ChatModelSettings.FromEnvironment()),   // ⭐ CE-4681 (L-3) — HROT_COMMANDER_ENDPOINT / _MODEL …
        _ => null,
    };

    /// <summary>⭐ CE-4679 — the host's recording now (the model log is written beside it); null = no log.</summary>
    public Func<string?>? RecordingPath { get; set; }

    /// <summary>⭐ CE-4681 (§5a-3) — where the model log goes when the host records nothing (a verify run): <c>HROT_COMMANDER_LOG_DIR</c>; null = no log.</summary>
    public string? LogDirectory { get; set; } = EnvVars.Read(EnvVars.HrotCommanderLogDir) is { Length: > 0 } d ? d : null;

    /// <summary>⭐ CE-4681 (§5a-3) — a run's choice of mind over the scenario's (<c>HROT_COMMANDER_MIND</c>: the laptop script runs ONE scenario per
    /// model); written to the seat when it opens, so the checks see the mind that ran. Null = the scenario's.</summary>
    public CommanderMind? MindOverride { get; set; } =
        Enum.TryParse<CommanderMind>(EnvVars.Read(EnvVars.HrotCommanderMind), ignoreCase: true, out var m) && Enum.IsDefined(m) ? m : null;

    /// <summary>
    /// ⭐ CE-4679 — does the node's clock run FREE (continuous), so a hold may pause and resume it? null = yes. ⛔ A STEPPED clock (a verify run, run-to,
    /// <c>/sim/step</c>) must never get the seat's <c>ResumeTimeIntent</c> — it switches the master to continuous and ends the stepping. There the seat
    /// only marks its hold (<see cref="AnyHoldsClock"/>) and the one stepping the clock withholds its next step.
    /// </summary>
    public Func<bool>? ClockRunsFree { get; set; }

    // ⭐ The duel (DESIGN_LLM_Commander §8, L-4 pre-test) — the clock is the SEATS' to pause, not each seat's: ONE pause when the first seat holds, ONE
    // resume when the last lets go. ⛔ Was per seat: two seats thinking at once each took the other's pause for a USER's and neither resumed — the clock
    // stayed paused for good (rail TwoSeats_TheClockRunsAgain_OnlyWhenBothMindsHaveAnswered). Frame thread only (CommanderActionSystem).
    internal bool ClockPausedBySeats, ClockUserPaused, OwnPauseUnseen;

    /// <summary>⭐ CE-4679 — some seat is holding the clock for its mind now (any thread): a run that steps the clock waits for it.</summary>
    public bool AnyHoldsClock
    {
        get { foreach (var s in Snapshot) if (s.HoldsClock) return true; return false; }
    }

    public bool TryGet(Entity commander, out SeatRuntime seat) => _seats.TryGetValue(commander, out seat!);
    public int Count => _seats.Count;
    public Dictionary<Entity, SeatRuntime>.ValueCollection All => _seats.Values;

    /// <summary>⭐ CE-4679 — the seats as of the last frame, from any thread.</summary>
    public IReadOnlyList<SeatRuntime> Snapshot => Volatile.Read(ref _snapshot);

    /// <summary>⭐ CE-4679 — a seat by its commander's network id, from any thread.</summary>
    public SeatRuntime? ByNetworkId(int networkId)
    {
        foreach (var s in Snapshot) if (s.NetworkId == networkId) return s;
        return null;
    }

    internal void Add(SeatRuntime seat)
    {
        _seats[seat.Commander] = seat;
        Volatile.Write(ref _snapshot, new List<SeatRuntime>(_seats.Values).ToArray());
    }

    internal void Remove(Entity commander)
    {
        if (!_seats.Remove(commander, out var seat)) return;
        seat.Dispose();
        Volatile.Write(ref _snapshot, new List<SeatRuntime>(_seats.Values).ToArray());
    }

    /// <summary>
    /// ⭐ CE-4679 (<c>POST /commanders/{id}/mind</c>) — frame thread: the seat's mind and mode are changed on the commander (the scenario field) and its
    /// runner is replaced; the feed and the channel go on (seq keeps counting), so the new mind's first call sees what came since.
    /// </summary>
    public string? SwitchMind(EntityRepository world, SeatRuntime seat, CommanderMind? mind, CommanderMode? mode)
    {
        if (!world.IsAlive(seat.Commander) || !world.HasComponent<CommanderSeat>(seat.Commander)) return "the commander is gone";
        var config = world.GetComponentRO<CommanderSeat>(seat.Commander);
        if (mind is { } m) config.Mind = m;
        if (mode is { } md) config.Mode = md;
        world.SetComponent(seat.Commander, config);
        seat.Mode = config.Mode;
        if (mind is null || mind == seat.Mind) return null;
        var next = MindFor(config);
        if (next == null) return $"no {config.Mind} mind in this build";
        seat.Runner?.Dispose();
        seat.Mind = config.Mind;
        seat.Runner = new MindRunner(seat.Channel, next, seat.Name, seat.Prefix) { Log = seat.Log };
        seat.Runner.Start();
        return null;
    }

    public void Dispose()
    {
        foreach (var s in _seats.Values) s.Dispose();
        _seats.Clear();
        Volatile.Write(ref _snapshot, Array.Empty<SeatRuntime>());
    }
}

/// <summary>
/// ⭐⭐ <c>CE-4676</c> (DESIGN_LLM_Commander §3, §5) — the seat's FEED, written on the frame thread after the staff's inbox (this frame's reports are in):
/// a <c>snapshot</c> at the seat's start and every <see cref="CommanderSeat.Snapshot"/> s, a <c>report</c> for each report into the commander
/// (<see cref="CommandReportHistory"/>), an <c>event</c> when a squad falls out of the fight, and a <c>heartbeat</c> when nothing came for
/// <see cref="CommanderSeat.Heartbeat"/> s. Each item: a seq, the sim time, a JSON form and ONE radio line (the places from the <see cref="Gazetteer"/>).
/// ⭐ Opening a seat: the commander's own play is taken away (<c>InertSop</c>, no task — §9 ①), every squad is asked a routine SITREP (§9 ④), and the
/// mind's runner starts. ⭐ 0 B per steady frame: a frame with no new report, no fall and no due heartbeat reads three components and writes nothing.
/// </summary>
[UpdateInPhase(SystemPhase.Simulation)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class CommanderFeedSystem : IEcsModuleSystem
{
    public const string InertSop = "InertSop";
    public const float RoutineSitrepSeconds = 30f;
    private const double FightCheckSeconds = 1.0;
    /// <summary>⭐ CE-4684 C-3 — the model's own next decision is held to at least this after the last wake, and at most this (sim s).</summary>
    public const double NextDecisionFloor = 10.0, NextDecisionCeiling = 300.0;

    private CommanderSeats? _seats;
    private readonly bool _gateOnAuthority;
    [Fdp.Core.FrameScratch("scratch — cleared at the top of each run")]
    private readonly List<Entity> _gone = new();
    [Fdp.Core.FrameScratch("scratch — the new history entries of one seat, oldest first")]
    private readonly List<CommandReportEntry> _fresh = new();
    private EntityQuery? _query;

    /// <param name="seats">The world's seats; null ⇒ <see cref="CommanderSeats.For"/> the world it runs on (production — the HTTP surface finds them there).</param>
    /// <param name="isBehaviour">⭐ CE-4685 A-2 — is a name a registered behaviour (the routing table's argument); null ⇒ the platoon plays.</param>
    /// <param name="registry">⭐ CE-4686 ② — the behaviour registry, to read the running play's roles (<c>OccurrenceSlotCatalog</c>); null ⇒ no roles.</param>
    public CommanderFeedSystem(CommanderSeats? seats = null, bool gateOnAuthority = false, Func<string, bool>? isBehaviour = null,
                               Fdp.Toolkit.Behavior.BehaviorRegistry? registry = null)
    {
        _seats = seats;
        _gateOnAuthority = gateOnAuthority;
        _isBehaviour = isBehaviour;
        _registry = registry;
    }
    private readonly Func<string, bool>? _isBehaviour;
    private readonly Fdp.Toolkit.Behavior.BehaviorRegistry? _registry;

    /// <summary>
    /// ⭐ CE-4686 ② — the roles the commander's RUNNING play gave his subordinates, read from the play's own blackboard (its params slot, boxed by
    /// <c>OccurrenceSlotCatalog</c> — the one read-only way into a running behaviour); null = no play with roles. Read per snapshot, never per frame.
    /// </summary>
    private Hrot.AI.Behaviors.Brains.IPlayRoles? RolesOf(EntityRepository world, Entity commander)
    {
        if (_registry == null || !world.IsAlive(commander)) return null;
        foreach (var v in Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.Read(world, commander, _registry))
            if (v.Owner == Fdp.Toolkit.Behavior.SlotOwnerKind.Task && v.Boxed is Hrot.AI.Behaviors.Brains.IPlayRoles roles) return roles;
        return null;
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (view is not EntityRepository world || !world.IsComponentTypeRegistered<CommanderSeat>()) return;
        var seats = _seats ??= CommanderSeats.For(world);
        double now = Now(world);
        _query ??= world.Query().With<CommanderSeat>().Build();
        foreach (var commander in _query)
        {
            if (_gateOnAuthority && !(world.HasComponent<BehaviorState>(commander) && world.HasAuthority<BehaviorState>(commander))) continue;
            if (!seats.TryGet(commander, out var seat)) seat = Open(world, seats, commander, now);
            Feed(world, seat, now);
        }

        // a seat whose commander is gone (or lost the component) closes, and its mind stops
        _gone.Clear();
        foreach (var s in seats.All)
            if (!world.IsAlive(s.Commander) || !world.HasComponent<CommanderSeat>(s.Commander)) _gone.Add(s.Commander);
        for (int i = 0; i < _gone.Count; i++) seats.Remove(_gone[i]);
    }

    // ── opening a seat ───────────────────────────────────────────────────────────────────────

    private SeatRuntime Open(EntityRepository world, CommanderSeats seats, Entity commander, double now)
    {
        var config = world.GetComponentRO<CommanderSeat>(commander);
        if (seats.MindOverride is { } chosen && chosen != config.Mind)   // ⭐ CE-4681 — the run's mind, written so the seat says what ran
        {
            config.Mind = chosen;
            world.SetComponent(commander, config);
        }
        int netId = CommandPictureOps.NetIdOf(world, commander);
        var seat = new SeatRuntime
        {
            Commander = commander, Name = NameOf(world, commander), Places = Gazetteer.Of(world, world.HasComponent<EntityInfo>(commander) ? world.GetComponentRO<EntityInfo>(commander).ForceId : null), NetworkId = netId,
            Side = world.HasComponent<EntityInfo>(commander) ? world.GetComponentRO<EntityInfo>(commander).ForceId.ToString() : "",
            Mind = config.Mind, Mode = config.Mode,
        };
        // ⭐ CE-4679 (R-4298) — the model log beside the host's recording: <recording>.commander-<id>.jsonl
        if (seats.RecordingPath is { } recording)
            seat.Log = new ModelLog(() => recording() is { Length: > 0 } r ? $"{r}.commander-{netId}.jsonl"
                                          : seats.LogDirectory is { } dir ? System.IO.Path.Combine(dir, $"commander-{netId}.jsonl") : null);
        else if (seats.LogDirectory is { } dir)   // ⭐ CE-4681 — no recording (a verify run): the run's log directory
            seat.Log = new ModelLog(() => System.IO.Path.Combine(dir, $"commander-{netId}.jsonl"));
        seats.Add(seat);
        TakeAwayOwnPlay(world, commander);

        // ⭐ §9 ④ — a routine SITREP from every squad, so the picture fills without the mind asking
        var roster = Squads(world, commander);
        for (int i = 0; i < roster.Count; i++)
            CommandOrders.AskSitrep(world, roster.SubordinateEntities[i], RoutineSitrepSeconds);

        // ⭐ CE-4685 A-1 / A-2 — the seat at ANY echelon: what he is, what his subordinates are, which plays his class runs
        seat.Echelon = EchelonName(world, commander) ?? "platoon";
        seat.Subordinate = roster.Count > 0 && world.IsAlive(roster.SubordinateEntities[0]) ? EchelonName(world, roster.SubordinateEntities[0]) ?? "squad" : "squad";
        seat.OwnOrders = OwnOrdersOf(world, commander, _isBehaviour);
        seat.IsBehaviour = _isBehaviour;
        for (int i = 0; i < roster.Count && i < 64; i++)
            if (world.IsAlive(roster.SubordinateEntities[i]) && Fdp.Toolkit.Aggregate.AggregateUnits.IsStochastic(world, roster.SubordinateEntities[i]))
                seat.Aggregated |= 1UL << i;
        // ⭐ CE-4684 — the mind first: whether it answers with decisions decides the prefix's vocabulary (C-6)
        var mind = seats.MindFor(config);
        seat.Contract = mind is IDecisionMind { AnswersWithDecisions: true };
        var units = new List<string>();
        for (int i = 0; i < roster.Count; i++)
            if (world.IsAlive(roster.SubordinateEntities[i])) units.Add(NameOf(world, roster.SubordinateEntities[i]));
        var placeIds = new List<string>();
        foreach (var (n, _) in seat.Places.Places) placeIds.Add(n);
        seat.Vocabulary = new SeatVocabulary(seat.Name, units, placeIds) { OwnOrders = seat.OwnOrders };
        seat.Prefix = Prefix(world, seat, roster);
        Fdp.Core.Logging.FdpLog<CommanderFeedSystem>.Info("[Commander] seat {0} open — prefix {1} chars", seat.Name, seat.Prefix.Length);   // ⭐ CE-4680 — the briefing's size (an 8 B model's context, L-3)
        if (mind == null)
        {
            seat.Channel.Publish(FeedKind.OrderState, now, false, $"{Clock(now)} SEAT: no {config.Mind} mind in this build — the seat gives no orders",
                                 new JsonObject { ["kind"] = "order-state", ["state"] = "refused", ["reason"] = $"the {config.Mind} mind is not built yet" }.ToJsonString());
        }
        else
        {
            seat.Runner = new MindRunner(seat.Channel, mind, seat.Name, seat.Prefix) { Log = seat.Log, Vocabulary = seat.Contract ? seat.Vocabulary : null };
            seat.Runner.Start();
        }
        Snapshot(world, seat, now, wake: true);   // ⭐ the board at the start: the mind's first call
        return seat;
    }

    /// <summary>⭐ §9 ① (measured: an idle platoon runs only its type's default SOP) — the explicit none, and no task.</summary>
    private static void TakeAwayOwnPlay(EntityRepository world, Entity commander)
    {
        int inert = BehaviorHash.FromName(InertSop);
        bool hasInert = world.HasComponent<SopState>(commander) && world.GetComponentRO<SopState>(commander).SopHash == inert;
        if (!hasInert && world.Bus.IsRegisteredManaged<AssignSopEvent>())
            world.Bus.PublishManaged(new AssignSopEvent { Entity = commander, BehaviorName = InertSop, JsonParams = "{}", Origin = BehaviorOrigin.Superior });
        if (world.HasComponent<BehaviorState>(commander))
        {
            var state = world.GetComponentRO<BehaviorState>(commander);
            int sop = world.HasComponent<SopState>(commander) ? world.GetComponentRO<SopState>(commander).SopHash : 0;
            if (state.ActiveBehaviorHash != 0 && state.ActiveBehaviorHash != sop && state.ActiveBehaviorHash != inert && world.Bus.IsRegistered<ClearBehaviorEvent>())
                world.Bus.Publish(new ClearBehaviorEvent { Entity = commander, Origin = BehaviorOrigin.Superior });
        }
    }

    // ── the feed ─────────────────────────────────────────────────────────────────────────────

    private void Feed(EntityRepository world, SeatRuntime seat, double now)
    {
        var config = world.GetComponentRO<CommanderSeat>(seat.Commander);
        Reports(world, seat, now);
        if (now - seat.LastFightCheckAt >= FightCheckSeconds) Falls(world, seat, now);
        if (now - seat.LastSnapshotAt >= config.Snapshot) Snapshot(world, seat, now);
        // ⭐ CE-4679 (§3 "else on the heartbeat") — the mind is asked at least every Heartbeat s: counted from the last WAKE, not the last item.
        //   📌 Found by the Haiku run: routine SITREPs every 30 s kept "no item for 30 s" from ever holding, so before contact the mind was never asked
        //   again after its first answer — the squads stood at the start.
        // ⭐ CE-4684 C-3 — a seat on the decision contract is asked again when ITS decision said (not_later_than), held between a floor and a ceiling;
        //   before its first decision, and for every other mind, the fixed heartbeat
        if (seat.Contract && seat.Watch.NextDecisionAt > 0)
        {
            double due = Math.Max(seat.LastWakeAt + NextDecisionFloor, Math.Min(seat.Watch.NextDecisionAt, seat.LastWakeAt + NextDecisionCeiling));
            if (now >= due)
                Publish(seat, FeedKind.Heartbeat, now, false, $"{Clock(now)} (your decision time — nothing woke you sooner; the orders in force stand unless you change them)",
                        new JsonObject { ["kind"] = "heartbeat", ["t"] = Round(now), ["nextDecision"] = true }.ToJsonString());
        }
        else if (now - seat.LastWakeAt >= config.Heartbeat)
            //   ⭐ the duel (§8 D-6): ⛔ was "(no news — your squads await orders)" — said with orders in force, it invited a model to give new ones
            Publish(seat, FeedKind.Heartbeat, now, false, $"{Clock(now)} (heartbeat — nothing new; the orders in force stand unless you change them)",
                    new JsonObject { ["kind"] = "heartbeat", ["t"] = Round(now) }.ToJsonString());
    }

    /// <summary>The reports into the commander that the feed has not said yet, oldest first (the history is newest first).</summary>
    private void Reports(EntityRepository world, SeatRuntime seat, double now)
    {
        if (!world.HasComponent<CommandReportHistory>(seat.Commander)) return;
        ref readonly var history = ref world.GetComponentRO<CommandReportHistory>(seat.Commander);
        if (history.Count == 0 || (seat.HasHead && SameEntry(history.Items[0], seat.Head))) return;   // ⭐ the steady frame: one compare
        _fresh.Clear();
        for (int i = 0; i < history.Count; i++)
        {
            var e = history.Items[i];
            if (seat.HasHead && SameEntry(e, seat.Head)) break;
            if (e.Traffic == CommandTraffic.Incoming) _fresh.Add(e);
        }
        seat.Head = history.Items[0];
        seat.HasHead = true;
        for (int i = _fresh.Count - 1; i >= 0; i--) Report(world, seat, _fresh[i], now);
    }

    private static bool SameEntry(in CommandReportEntry a, in CommandReportEntry b)
        => a.At.Equals(b.At) && a.Report.Seq == b.Report.Seq && a.FromNetId == b.FromNetId && a.Report.Kind == b.Report.Kind && a.Traffic == b.Traffic;

    private void Report(EntityRepository world, SeatRuntime seat, in CommandReportEntry e, double now)
    {
        var r = e.Report;
        var from = SenderOf(world, seat.Commander, e.FromNetId);
        string who = from.IsNull ? $"#{e.FromNetId}" : NameOf(world, from);
        var at = new Vector2(r.At.X, r.At.Y);
        string place = seat.Places.Describe(at);
        var json = new JsonObject
        {
            ["kind"] = "report", ["t"] = Round(now), ["from"] = who, ["report"] = r.Kind.ToString(),
            ["at"] = new JsonArray(Round(r.At.X), Round(r.At.Y)), ["place"] = place,
        };
        var radio = new StringBuilder(Clock(now)).Append(' ').Append(who.ToUpperInvariant()).Append(": ");
        bool wake = true;
        switch (r.Kind)
        {
            case ReportKind.Spot:
            case ReportKind.Contact:
                json["count"] = r.Count; json["size"] = r.Size.ToString(); json["activity"] = r.Activity.ToString();
                json["equipment"] = r.Equipment.ToString();
                json["age"] = Round(Math.Max(0d, now - r.ObservedAt.Seconds));
                radio.Append(r.Kind == ReportKind.Spot ? "spot, " : "contact, ")
                     .Append(r.Count > 0 ? r.Count.ToString(CultureInfo.InvariantCulture) + " enemy" : "enemy")
                     .Append(" (").Append(r.Size).Append(')');
                AppendDetail(radio, r.Activity, r.Equipment, now - r.ObservedAt.Seconds);   // ⭐ CE-4680 B-5
                radio.Append(", ").Append(place);
                break;
            case ReportKind.Sitrep:
                json["strength"] = r.Strength.ToString(); json["ammo"] = r.Ammo.ToString();
                radio.Append("sitrep, strength ").Append(r.Strength).Append(", ammo ").Append(r.Ammo).Append(", at ").Append(place);
                wake = false;   // routine: it waits for the next call
                break;
            case ReportKind.Lace:
                json["wia"] = r.Wia; json["kia"] = r.Kia; json["ammo"] = r.Ammo.ToString();
                radio.Append("LACE, ").Append(r.Wia).Append(" wounded, ").Append(r.Kia).Append(" killed, ammo ").Append(r.Ammo);
                break;
            case ReportKind.Progress:
                json["subtype"] = ((ProgressKind)r.Subtype).ToString();
                radio.Append("progress, ").Append((ProgressKind)r.Subtype).Append(", at ").Append(place);
                break;
            case ReportKind.Request:
                json["subtype"] = ((RequestKind)r.Subtype).ToString();
                radio.Append("request, ").Append((RequestKind)r.Subtype).Append(", at ").Append(place);
                break;
            default:
                radio.Append(r.Kind);
                break;
        }
        Publish(seat, FeedKind.Report, now, wake, radio.ToString(), json.ToJsonString());
    }

    /// <summary>The unit a report came from: one of his own (keyed as the picture keys them, <see cref="CommandPictureOps.NetIdOf"/>), else by network id.</summary>
    private static Entity SenderOf(EntityRepository world, Entity commander, int netId)
    {
        var roster = Squads(world, commander);
        for (int i = 0; i < roster.Count; i++)
            if (world.IsAlive(roster.SubordinateEntities[i]) && CommandPictureOps.NetIdOf(world, roster.SubordinateEntities[i]) == netId) return roster.SubordinateEntities[i];
        return Fdp.Toolkit.Replication.Services.NetworkIdResolver.FindEntityByNetworkId(world, netId);
    }

    /// <summary>A squad that falls out of the fight (its members below its minimum) is an event — once.</summary>
    /// <summary>
    /// ⭐ CE-4685 A-1 — a unit's echelon in words, from its CLASS (its header's DIS echelon, <c>UnitDis</c> — what the routing table reads; else its TKB type's), else a stochastic unit's
    /// <c>AggregateUnit.Echelon</c>; null = unknown. Never hard-coded per seat.
    /// </summary>
    public static string? EchelonName(EntityRepository world, Entity unit)
    {
        // the entity header's DIS type first — the one the routing table resolves its plays from (A-2), so echelon and plays agree
        var dis = world.GetDisType(unit);
        if (dis.Kind == Hrot.Core.Tkb.UnitDis.Kind && System.Enum.IsDefined(typeof(Hrot.Core.Tkb.UnitEchelon), dis.Subcategory))
            return ((Hrot.Core.Tkb.UnitEchelon)dis.Subcategory).ToString().ToLowerInvariant();
        if (world.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>() && world.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(unit)
            && world.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>()?.GetByType((long)world.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(unit).TkbType) is { } t
            && t.DisType.Kind == Hrot.Core.Tkb.UnitDis.Kind && System.Enum.IsDefined(typeof(Hrot.Core.Tkb.UnitEchelon), t.DisType.Subcategory))
            return ((Hrot.Core.Tkb.UnitEchelon)t.DisType.Subcategory).ToString().ToLowerInvariant();
        if (world.IsComponentTypeRegistered<Fdp.Toolkit.Aggregate.AggregateUnit>() && world.HasComponent<Fdp.Toolkit.Aggregate.AggregateUnit>(unit))
            return world.GetComponentRO<Fdp.Toolkit.Aggregate.AggregateUnit>(unit).Echelon switch { 0 => "squad", 1 => "platoon", 2 => "company", 3 => "battalion", _ => null };
        return null;
    }

    private static JsonArray OwnOrdersJson(SeatRuntime seat)
    {
        var a = new JsonArray();
        foreach (var p in seat.OwnOrders) a.Add(p);
        return a;
    }

    /// <summary>An echelon's plural ("squads", "companies").</summary>
    public static string Plural(string echelon) => echelon.EndsWith("y", StringComparison.Ordinal) ? echelon[..^1] + "ies" : echelon + "s";

    /// <summary>
    /// ⭐ CE-4685 A-2 — the order KINDS <paramref name="commander"/>'s own unit takes as a whole: each of Attack / Defend / Bound / Hold that the ONE
    /// routing table resolves for his class at his resolution (<c>OrderReissue.PlayFor</c> — a platoon takes all four, a company Attack and Defend).
    /// The seat gives the KIND; the table picks the behaviour (user `2026-11-24`: "We cannot call directly a behavior name"). No registry, or nothing
    /// resolved ⇒ the four kinds.
    /// </summary>
    public static string[] OwnOrdersOf(EntityRepository world, Entity commander, Func<string, bool>? isBehaviour)
    {
        if (isBehaviour == null) return CommanderActionSystem.OwnOrders;
        var resolution = Fdp.Toolkit.Aggregate.AggregateUnits.IsStochastic(world, commander)
            ? Fdp.Toolkit.Aggregate.AggregateResolution.Stochastic : Fdp.Toolkit.Aggregate.AggregateResolution.Physical;
        var kinds = new List<string>(4);
        foreach (var kind in PlayKinds)
            if (Hrot.AI.Behaviors.Orders.OrderReissue.PlayFor(world, commander, resolution, kind, isBehaviour) != null) kinds.Add(kind.ToString());
        return kinds.Count > 0 ? kinds.ToArray() : CommanderActionSystem.OwnOrders;
    }

    private static readonly Hrot.AI.Behaviors.Orders.OrderKind[] PlayKinds =
    {
        Hrot.AI.Behaviors.Orders.OrderKind.Attack, Hrot.AI.Behaviors.Orders.OrderKind.Defend,
        Hrot.AI.Behaviors.Orders.OrderKind.Bound, Hrot.AI.Behaviors.Orders.OrderKind.Hold,
    };

    private void Falls(EntityRepository world, SeatRuntime seat, double now)
    {
        seat.LastFightCheckAt = now;
        var roster = Squads(world, seat.Commander);
        for (int i = 0; i < roster.Count && i < 64; i++)
        {
            ulong bit = 1UL << i;
            var squad = roster.SubordinateEntities[i];
            // ⭐ CE-4685 A-3 — an aggregated subordinate that SPLIT (X-5: it hands over and commands its own units now) is news, said once; it is the same
            //   entity, so the seat's orders to it still land
            if ((seat.Aggregated & bit) != 0 && world.IsAlive(squad) && !Fdp.Toolkit.Aggregate.AggregateUnits.IsStochastic(world, squad))
            {
                seat.Aggregated &= ~bit;
                string split = NameOf(world, squad);
                Publish(seat, FeedKind.Event, now, true,
                        $"{Clock(now)} {split.ToUpperInvariant()}: near the enemy — it has split from an aggregated {seat.Subordinate} into its own units and fights them with its drills; your orders to it stand",
                        new JsonObject { ["kind"] = "event", ["t"] = Round(now), ["event"] = "split", ["unit"] = split }.ToJsonString());
            }
            bool out_ = !world.IsAlive(squad) || !CombatEffective.IsInTheFight(world, squad);
            if (!out_ || (seat.OutOfFight & bit) != 0) continue;
            seat.OutOfFight |= bit;
            string name = world.IsAlive(squad) ? NameOf(world, squad) : $"squad {i + 1}";
            Publish(seat, FeedKind.Event, now, true, $"{Clock(now)} {name.ToUpperInvariant()}: out of the fight",
                    new JsonObject { ["kind"] = "event", ["t"] = Round(now), ["event"] = "combat-ineffective", ["squad"] = name }.ToJsonString());
            seat.Watch.UnitLeft(world, seat, squad, now);   // ⭐ CE-4687 — what waits on it to arrive / finish / make contact can never hold: dropped and said
        }
    }

    private void Snapshot(EntityRepository world, SeatRuntime seat, double now, bool wake = false)
    {
        seat.LastSnapshotAt = now;
        var squads = new JsonArray();
        var radio = new StringBuilder(Clock(now)).Append(" SNAPSHOT — ").Append(seat.Name).Append(':');
        var roster = Squads(world, seat.Commander);
        bool havePicture = world.HasComponent<CommandPicture>(seat.Commander);
        var roles = seat.Play != null ? RolesOf(world, seat.Commander) : null;   // ⭐ CE-4686 ② — his whole unit's order's roles
        for (int i = 0; i < roster.Count; i++)
        {
            var squad = roster.SubordinateEntities[i];
            if (!world.IsAlive(squad)) continue;
            string name = NameOf(world, squad);
            Vector3 at = world.HasComponent<SimTransform>(squad) ? (Vector3)world.GetComponentRO<SimTransform>(squad).Position : Vector3.Zero;
            bool inFight = CombatEffective.IsInTheFight(world, squad);
            var row = new JsonObject
            {
                ["name"] = name, ["at"] = new JsonArray(Round(at.X), Round(at.Y)), ["place"] = seat.Places.Describe(new Vector2(at.X, at.Y)), ["inFight"] = inFight,
            };
            if (havePicture)
            {
                ref readonly var picture = ref world.GetComponentRO<CommandPicture>(seat.Commander);
                int r = CommandPictureOps.RowOf(in picture, CommandPictureOps.NetIdOf(world, squad));
                if (r >= 0)
                {
                    var sub = picture.Subordinates.Items[r];
                    row["strength"] = sub.Strength.ToString(); row["ammo"] = sub.Ammo.ToString();
                    if (sub.LastSitrepAt.Seconds > 0) row["sitrepAge"] = Round(now - sub.LastSitrepAt.Seconds);
                    // ⭐ CE-4680 B-4 — the latest progress on its current order, and how old its last report is
                    if (sub.Progress != 0) row["progress"] = sub.Progress.ToString();
                    if (sub.LastReportAt.Seconds > 0) row["reportAge"] = Round(now - sub.LastReportAt.Seconds);
                }
            }
            CommanderActionSystem.SyncPlayOrders(world, seat, now);   // ⭐ CE-4683 — the play's squad orders into the in-force table
            string? task = TaskOf(world, seat, squad);   // ⭐ CE-4680 B-4 — the order he is on (his commander's record)
            if (task != null) row["task"] = task;
            if (roles?.RoleOf(i) is { } role) row["role"] = role;   // ⭐ CE-4686 ② — the roster slot IS the play's slot
            squads.Add(row);
            radio.Append(' ').Append(name.ToUpperInvariant()).Append(" at ").Append(row["place"]).Append(inFight ? "" : " (out of the fight)");
            var detail = new StringBuilder();   // ⭐ CE-4683 — the order itself is in the ORDERS IN FORCE block below (said once)
            if (row["role"] is { } rl) detail.Append("role ").Append((string)rl!).Append(" in your ").Append(seat.Echelon).Append("'s ").Append(seat.Play);
            if (row["progress"] is { } prog) detail.Append(detail.Length > 0 ? ", " : "").Append((string)prog!);
            if (row["strength"] is { } st) detail.Append(detail.Length > 0 ? ", " : "").Append("strength ").Append((string)st!).Append(" ammo ").Append((string)row["ammo"]!);
            if (row["reportAge"] is { } age) detail.Append(detail.Length > 0 ? ", " : "").Append("last report ").Append(((double)age!).ToString("0", CultureInfo.InvariantCulture)).Append(" s ago");
            if (Fdp.Toolkit.Aggregate.AggregateUnits.IsStochastic(world, squad))   // ⭐ CE-4685 A-1 — an aggregated unit fights as numbers: its men from its slots
            {
                row["aggregated"] = true;
                detail.Append(detail.Length > 0 ? ", " : "").Append("aggregated");
                // ⛔ only once its slot table holds men — 📌 the first scripted run said "0 men" at t = 0: the table is filled from the TKB composite after the seat opens
                if (Fdp.Toolkit.Aggregate.AggregateUnits.Slots(world, squad, standing: false, out int men, out int total, out _) && total > 0)
                {
                    row["men"] = men;
                    detail.Append(", ").Append(men.ToString(CultureInfo.InvariantCulture)).Append(" men");
                }
            }
            if (detail.Length > 0) radio.Append(" (").Append(detail).Append(')');
            radio.Append(';');
        }
        var elements = new JsonArray();
        if (havePicture)
        {
            ref readonly var pic = ref world.GetComponentRO<CommandPicture>(seat.Commander);
            for (int i = 0; i < pic.Elements.Count; i++)
            {
                var el = pic.Elements.Items[i];
                var p = new Vector2(el.At.X, el.At.Y);
                elements.Add(new JsonObject
                {
                    ["at"] = new JsonArray(Round(p.X), Round(p.Y)), ["place"] = seat.Places.Describe(p), ["count"] = el.Count,
                    ["size"] = el.Size.ToString(), ["activity"] = el.Activity.ToString(), ["equipment"] = el.Equipment.ToString(),
                    ["age"] = Round(now - el.ObservedAt.Seconds), ["confidence"] = Confidence(in pic, i, now),
                });
                string? by = ReporterName(world, roster, el.FromNetId);   // ⭐ CE-4686 ① — who reported it (a platoon relays its squads' SPOTs up)
                if (by != null) ((JsonObject)elements[^1]!)["reportedBy"] = by;
                radio.Append(" enemy ").Append(el.Count).Append(" (").Append(el.Size).Append(')');
                AppendDetail(radio, el.Activity, el.Equipment, now - el.ObservedAt.Seconds);   // ⭐ CE-4680 B-5
                radio.Append(", ").Append(Confidence(in pic, i, now));                         // ⭐ CE-4684 C-5
                radio.Append(" at ").Append(seat.Places.Describe(p));
                if (by != null) radio.Append(", reported by ").Append(by);
                radio.Append(';');
            }
        }
        var objectives = Objectives(world, seat, radio);   // ⭐ CE-4680 B-3
        InForceBlock(world, seat, roster, radio);           // ⭐ CE-4683 ② — survives the snapshot: every snapshot carries it
        var places = new JsonArray();
        foreach (var (n, _) in seat.Places.Places) places.Add(n);
        Publish(seat, FeedKind.Snapshot, now, wake, radio.ToString(),
                new JsonObject { ["kind"] = "snapshot", ["t"] = Round(now), ["commander"] = seat.Name, ["echelon"] = seat.Echelon, ["ownOrders"] = OwnOrdersJson(seat), ["squads"] = squads, ["elements"] = elements, ["objectives"] = objectives, ["places"] = places }.ToJsonString());
    }

    /// <summary>
    /// ⭐ CE-4683 ② (§5a-4) — <c>ORDERS IN FORCE: ALPHA SQUAD SupportByFire → house since 00:02:08 (you), Set; …</c> and the play in force: what he has
    /// already ordered, so a snapshot (which clears the radio log) never makes him lose it.
    /// </summary>
    private static void InForceBlock(EntityRepository world, SeatRuntime seat, in UnitRoster roster, StringBuilder radio)
    {
        if (seat.Play != null)
            radio.Append(' ').Append(seat.Echelon.ToUpperInvariant()).Append(" ORDER IN FORCE: ").Append(seat.Play).Append(seat.PlayWhere is { } pw ? " → " + pw : "").Append(" since ").Append(Clock(seat.PlaySince)).Append(';');
        bool first = true;
        for (int i = 0; i < roster.Count; i++)
        {
            var squad = roster.SubordinateEntities[i];
            if (!world.IsAlive(squad) || !seat.InForce.TryGetValue(squad, out var o)) continue;
            // ⭐ CE-4686 — a unit OUT OF THE FIGHT carries no order in force (📌 Haiku: a destroyed platoon's SupportByFire stayed listed as in force)
            if (!CombatEffective.IsInTheFight(world, squad)) { seat.InForce.Remove(squad); continue; }
            radio.Append(first ? " ORDERS IN FORCE:" : ",").Append(' ').Append(NameOf(world, squad).ToUpperInvariant()).Append(' ').Append(o.Text)
                 .Append(" since ").Append(Clock(o.Since)).Append(" (").Append(o.By).Append(')');
            first = false;
        }
        radio.Append(first ? " ORDERS IN FORCE: none;" : ";");
    }

    /// <summary>⭐ CE-4684 C-5 — two reports this close are the same enemy (m); a report this old is no longer fresh (sim s).</summary>
    public const float SameEnemyMetres = 30f;
    public const double FreshSeconds = 60.0;

    /// <summary>
    /// ⭐ CE-4684 (§5c C-5) — how sure the commander can be of element <paramref name="index"/>, from what was REPORTED only: the different squads that
    /// reported an enemy within <see cref="SameEnemyMetres"/> of it, and the age of the newest of those reports. ≥ 2 squads, fresh ⇒ HIGH · one squad
    /// fresh, or ≥ 2 squads stale ⇒ MED · one squad, stale ⇒ LOW. ("heard" joins when behaviors adds the bit — CE-4682's NOTE.)
    /// </summary>
    /// <summary>⭐ CE-4686 ① — the name of the subordinate whose network id sent a report (his roster); null = not one of his.</summary>
    private static string? ReporterName(EntityRepository world, in UnitRoster roster, int netId)
    {
        for (int i = 0; i < roster.Count; i++)
        {
            var sub = roster.SubordinateEntities[i];
            if (world.IsAlive(sub) && CommandPictureOps.NetIdOf(world, sub) == netId) return NameOf(world, sub);
        }
        return null;
    }

    public static string Confidence(in CommandPicture pic, int index, double now)
    {
        var el = pic.Elements.Items[index];
        var at = new Vector2(el.At.X, el.At.Y);
        int first = el.FromNetId, second = int.MinValue;
        double newest = el.ObservedAt.Seconds;
        for (int k = 0; k < pic.Elements.Count; k++)
        {
            if (k == index) continue;
            var other = pic.Elements.Items[k];
            if (Vector2.Distance(at, new Vector2(other.At.X, other.At.Y)) > SameEnemyMetres) continue;
            if (other.ObservedAt.Seconds > newest) newest = other.ObservedAt.Seconds;
            if (other.FromNetId != first && second == int.MinValue) second = other.FromNetId;
        }
        bool fresh = now - newest <= FreshSeconds, two = second != int.MinValue;
        return two && fresh ? "HIGH" : two || fresh ? "MED" : "LOW";
    }

    /// <summary>⭐ CE-4680 B-5 — what an element is doing and has, and how old the word is (only what was reported).</summary>
    private static void AppendDetail(StringBuilder radio, ElementActivity activity, ElementEquipment equipment, double age)
    {
        if (activity != ElementActivity.ActivityUnknown) radio.Append(", ").Append(activity);
        if (equipment != ElementEquipment.NoEquipment) radio.Append(", ").Append(equipment);
        if (age >= 10d) radio.Append(", ").Append(age.ToString("0", CultureInfo.InvariantCulture)).Append(" s old");
    }

    /// <summary>⭐ CE-4680 B-4 — the order the commander holds for <paramref name="squad"/> (his <c>SubordinateOrders</c>): its kind and where.</summary>
    private static string? TaskOf(EntityRepository world, SeatRuntime seat, Entity squad)
    {
        if (seat.InForce.TryGetValue(squad, out var inForce)) return inForce.Text;   // ⭐ CE-4683 — the one table
        if (SubordinateOrders.Of(world, seat.Commander) is not { } held) return null;
        for (int i = 0; i < held.Orders.Count; i++)
        {
            var e = held.Orders[i];
            if (!e.Member.Equals(squad)) continue;
            string kind = e.Behavior.StartsWith("Order.", StringComparison.Ordinal) ? e.Behavior.Substring(6) : e.Behavior;
            if (kind.Length == 0) return null;
            string? where = null;
            try
            {
                if (JsonNode.Parse(e.Json ?? "") is JsonObject j && (j["Position"] ?? j["Location"] ?? j["To"] ?? j["Objective"]) is JsonArray pt && pt.Count >= 2)
                    where = seat.Places.Describe(new Vector2((float)pt[0]!.GetValue<double>(), (float)pt[1]!.GetValue<double>()));
            }
            catch (System.Text.Json.JsonException) { }
            return where == null ? kind : kind + " → " + where;
        }
        return null;
    }

    /// <summary>⭐ CE-4680 B-3 — the objectives the seat's side may see (<c>VisibleSides</c>; 0 / 0xFF = everybody): name, state, owner, progress.</summary>
    private static JsonArray Objectives(EntityRepository world, SeatRuntime seat, StringBuilder radio)
    {
        var list = new JsonArray();
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.ScenarioRules.ObjectiveStatus>()) return list;
        int side = world.HasComponent<EntityInfo>(seat.Commander) ? (int)world.GetComponentRO<EntityInfo>(seat.Commander).ForceId : 0;
        bool first = true;
        foreach (var e in world.Query().With<Fdp.Toolkit.ScenarioRules.ObjectiveStatus>().Build())
        {
            ref readonly var o = ref world.GetComponentRO<Fdp.Toolkit.ScenarioRules.ObjectiveStatus>(e);
            if (o.Role != Fdp.Toolkit.ScenarioRules.ObjectiveRole.Objective) continue;
            if (o.VisibleSides is not (0 or 0xFF) && (side >= 8 || (o.VisibleSides & (1 << side)) == 0)) continue;
            string title = o.Title.ToString();
            if (title.Length == 0) title = NameOf(world, e);
            var row = new JsonObject { ["name"] = title, ["state"] = o.State.ToString(), ["owner"] = o.Owner.ToString() };
            if (o.ProgressNeed > 0) { row["progress"] = o.ProgressCur; row["need"] = o.ProgressNeed; }
            list.Add(row);
            radio.Append(first ? " OBJECTIVES:" : ",").Append(' ').Append(title).Append(' ').Append(o.State);
            if (o.ProgressNeed > 0) radio.Append(' ').Append(o.ProgressCur).Append('/').Append(o.ProgressNeed);
            first = false;
        }
        if (!first) radio.Append(';');
        return list;
    }

    private static string Prefix(EntityRepository world, SeatRuntime seat, in UnitRoster roster)
    {
        var sb = new StringBuilder();
        // ⭐ CE-4685 A-1 — his echelon and his subordinates' from their classes (a company commander commands platoons)
        string subs = Plural(seat.Subordinate);
        sb.Append("You are ").Append(seat.Name).Append(", a ").Append(seat.Echelon).Append(" commander. You command ").Append(subs)
          .Append(", never single soldiers; they fight with their own drills.\n");
        sb.Append("Your ").Append(subs).Append(':');
        for (int i = 0; i < roster.Count; i++)
            if (world.IsAlive(roster.SubordinateEntities[i])) sb.Append(' ').Append(NameOf(world, roster.SubordinateEntities[i]));
        // ⭐ CE-4680 B-2 — the mission, as his scenario gives it
        if (world.IsComponentTypeRegistered<CommanderBrief>() && world.HasComponent<CommanderBrief>(seat.Commander)
            && world.GetComponentRO<CommanderBrief>(seat.Commander).Line() is { Length: > 0 } mission)
            sb.Append('\n').Append(mission);
        // ⭐ CE-4684 C-6 — a decision names places by id ONLY (its schema enumerates them); the "N m <dir>" and grid forms stay for the other minds
        if (seat.Contract) sb.Append("\nPlaces (ids — use exactly these): ").Append(seat.Places.KnownNames()).Append('\n');
        else sb.Append("\nPlaces: ").Append(seat.Places.KnownNames()).Append(" — say a place as a name, \"N m <dir> of <name>\", or \"grid E<i> N<j>\".\n");
        // ⭐ CE-4680 B-6 — the places' terrain notes, one line each · ⭐ CE-4684 C-4 — and the FACTS HROT measured once (sight, cover, vehicles, minutes)
        var facts = TerrainFacts.Of(world, seat.Places);
        foreach (var (n, _) in seat.Places.Places)
        {
            string? note = seat.Places.NoteOf(n);
            facts.TryGetValue(n, out string? fact);
            if (note == null && fact == null) continue;
            sb.Append("  ").Append(n).Append(':');
            if (note != null) sb.Append(' ').Append(note);
            if (fact != null) sb.Append(note != null ? " — " : " ").Append(fact);
            sb.Append('\n');
        }
        if (seat.Contract)
        {
            // ⭐ CE-4684 (§5c) — the decision's tasks: a subordinate's orders, his OWN unit's order kinds (unit = himself, CE-4685), CONTINUE
            sb.Append("Tasks: for a ").Append(seat.Subordinate).Append(' ').Append(string.Join(" | ", CommanderActionSystem.Orders)).Append(" · for your whole ").Append(seat.Echelon)
              .Append(" (unit \"").Append(seat.Name).Append("\") ").Append(string.Join(" | ", seat.OwnOrders)).Append(" · CONTINUE keeps a unit's orders.\n")
              .Append("SupportByFire: objective = what it fires at, support_at = where it stands to fire (without support_at it stands at the objective). ")
              .Append("Bound: support_at = where the covering element sets. Your whole unit's Attack picks its own base of fire (support_at is not used).\n")   // ⭐ CE-4686 ③ ④
              .Append("PREFER an order to your whole ").Append(seat.Echelon).Append(": it coordinates your ").Append(subs).Append(" for you (Attack: support set, then the assault · Defend: they hold an area together) — while it runs, give the ")
              .Append(subs).Append(" CONTINUE; an order to one of them ENDS it. Its ROLES are shown per unit (role assault / base of fire / reserve …): the orders a role")
              .Append(" gives are that order's, not a mistake to undo.\n")   // ⭐ CE-4686 ②
              .Append("The snapshot lists the ORDERS IN FORCE as of its time; orders given since are in the radio log. Repeating an order in force changes nothing.");
            return sb.ToString();
        }
        // ⭐ CE-4683 ③ (§5a-4) — said plainly: with the actions as functions a model reached for direct orders (L-3: one play, then only orders)
        //   ⭐ the duel (§8): said for both sides — the old line described only the attack, and a defending (Red) seat read it too
        sb.Append("PREFER run_play: an order to your whole ").Append(seat.Echelon).Append(" coordinates your ").Append(Plural(seat.Subordinate))
          .Append(" for you (Attack: support set, then the assault · Defend: they hold an area together) — a direct order to one of them ENDS it.")
          .Append(" Its ROLES are shown per unit (role assault / base of fire / reserve …): the orders a role gives are that order's, not a mistake to undo.\n");   // ⭐ CE-4686 ②
        sb.Append("Actions: run_play(play, where[, support]) with play ").Append(string.Join(" | ", seat.OwnOrders)).Append(" (your whole ").Append(seat.Echelon).Append(") · stop_play() · ")
          .Append("order(squad, kind, where) with kind Move | Hold | Attack | SupportByFire | Defend | BreakContact — only when no order to your whole unit fits · ")
          .Append("hold(squad) · ask_sitrep(squad) · note(text) — your running estimate: a new note REPLACES the last (keep it short). ")
          .Append("The snapshot lists the ORDERS IN FORCE as of its time; orders given since are in the radio log. Repeating an order or a play in force changes nothing.");   // ⭐ CE-4680 B-1, CE-4683 ①②
        return sb.ToString();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static void Publish(SeatRuntime seat, FeedKind kind, double now, bool wake, string radio, string json)
    {
        seat.LastItemAt = now;
        if (wake || kind == FeedKind.Heartbeat) seat.LastWakeAt = now;
        seat.Channel.Publish(kind, now, wake, radio, json);
    }

    internal static UnitRoster Squads(EntityRepository world, Entity commander)
        => world.HasComponent<UnitRoster>(commander) ? world.GetComponentRO<UnitRoster>(commander) : default;

    internal static string NameOf(EntityRepository world, Entity e)
        => world.HasComponent<EntityInfo>(e) ? world.GetComponentRO<EntityInfo>(e).Name.ToString() : $"#{e.Index}";

    internal static double Now(EntityRepository world)
        => world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;

    internal static string Clock(double seconds)
        => TimeSpan.FromSeconds(Math.Max(0d, seconds)).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    internal static double Round(double v) => Math.Round(v, 1);
}
