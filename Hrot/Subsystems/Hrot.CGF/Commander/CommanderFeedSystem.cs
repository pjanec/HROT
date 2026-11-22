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

    // what the feed already said (frame thread)
    internal double LastItemAt, LastSnapshotAt, LastFightCheckAt;
    /// <summary>⭐ CE-4679 — the last item that asked the mind to think (a wake or a heartbeat): the heartbeat counts from it, not from any item.</summary>
    internal double LastWakeAt;
    internal bool HasHead;
    internal CommandReportEntry Head;
    internal ulong OutOfFight;

    // ⭐ CE-4679 — the think-pause hold (frame thread, CommanderActionSystem)
    internal volatile bool Holding;   // read from any thread (a stepping run's hold, GET /commanders)
    internal bool PausedByUs, UserPaused, OwnPauseSeen;

    // ⭐ CE-4680 B-1 — the play the seat gave its commander as his task (frame thread), and the direct orders waiting for it to end
    internal string? Play;
    internal uint PlayRun;
    /// <summary>⭐ CE-4680 B-4 — the last direct order the seat gave each squad ("Attack → house"): a commander with no running task records no <c>SubordinateOrders</c> (run 0).</summary>
    internal readonly System.Collections.Generic.Dictionary<Entity, string> Given = new();
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
        _ => null,
    };

    /// <summary>⭐ CE-4679 — the host's recording now (the model log is written beside it); null = no log.</summary>
    public Func<string?>? RecordingPath { get; set; }

    /// <summary>
    /// ⭐ CE-4679 — does the node's clock run FREE (continuous), so a hold may pause and resume it? null = yes. ⛔ A STEPPED clock (a verify run, run-to,
    /// <c>/sim/step</c>) must never get the seat's <c>ResumeTimeIntent</c> — it switches the master to continuous and ends the stepping. There the seat
    /// only marks its hold (<see cref="AnyHoldsClock"/>) and the one stepping the clock withholds its next step.
    /// </summary>
    public Func<bool>? ClockRunsFree { get; set; }

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

    private CommanderSeats? _seats;
    private readonly bool _gateOnAuthority;
    [Fdp.Core.FrameScratch("scratch — cleared at the top of each run")]
    private readonly List<Entity> _gone = new();
    [Fdp.Core.FrameScratch("scratch — the new history entries of one seat, oldest first")]
    private readonly List<CommandReportEntry> _fresh = new();
    private EntityQuery? _query;

    /// <param name="seats">The world's seats; null ⇒ <see cref="CommanderSeats.For"/> the world it runs on (production — the HTTP surface finds them there).</param>
    public CommanderFeedSystem(CommanderSeats? seats = null, bool gateOnAuthority = false)
    {
        _seats = seats;
        _gateOnAuthority = gateOnAuthority;
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
        int netId = CommandPictureOps.NetIdOf(world, commander);
        var seat = new SeatRuntime
        {
            Commander = commander, Name = NameOf(world, commander), Places = Gazetteer.Of(world), NetworkId = netId,
            Side = world.HasComponent<EntityInfo>(commander) ? world.GetComponentRO<EntityInfo>(commander).ForceId.ToString() : "",
            Mind = config.Mind, Mode = config.Mode,
        };
        // ⭐ CE-4679 (R-4298) — the model log beside the host's recording: <recording>.commander-<id>.jsonl
        if (seats.RecordingPath is { } recording)
            seat.Log = new ModelLog(() => recording() is { Length: > 0 } r ? $"{r}.commander-{netId}.jsonl" : null);
        seats.Add(seat);
        TakeAwayOwnPlay(world, commander);

        // ⭐ §9 ④ — a routine SITREP from every squad, so the picture fills without the mind asking
        var roster = Squads(world, commander);
        for (int i = 0; i < roster.Count; i++)
            CommandOrders.AskSitrep(world, roster.SubordinateEntities[i], RoutineSitrepSeconds);

        seat.Prefix = Prefix(world, seat, roster);
        Fdp.Core.Logging.FdpLog<CommanderFeedSystem>.Info("[Commander] seat {0} open — prefix {1} chars", seat.Name, seat.Prefix.Length);   // ⭐ CE-4680 — the briefing's size (an 8 B model's context, L-3)
        var mind = seats.MindFor(config);
        if (mind == null)
        {
            seat.Channel.Publish(FeedKind.OrderState, now, false, $"{Clock(now)} SEAT: no {config.Mind} mind in this build — the seat gives no orders",
                                 new JsonObject { ["kind"] = "order-state", ["state"] = "refused", ["reason"] = $"the {config.Mind} mind is not built yet" }.ToJsonString());
        }
        else
        {
            seat.Runner = new MindRunner(seat.Channel, mind, seat.Name, seat.Prefix) { Log = seat.Log };
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
        if (now - seat.LastWakeAt >= config.Heartbeat)
            Publish(seat, FeedKind.Heartbeat, now, false, $"{Clock(now)} (no news — your squads await orders)",
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
    private void Falls(EntityRepository world, SeatRuntime seat, double now)
    {
        seat.LastFightCheckAt = now;
        var roster = Squads(world, seat.Commander);
        for (int i = 0; i < roster.Count && i < 64; i++)
        {
            ulong bit = 1UL << i;
            var squad = roster.SubordinateEntities[i];
            bool out_ = !world.IsAlive(squad) || !CombatEffective.IsInTheFight(world, squad);
            if (!out_ || (seat.OutOfFight & bit) != 0) continue;
            seat.OutOfFight |= bit;
            string name = world.IsAlive(squad) ? NameOf(world, squad) : $"squad {i + 1}";
            Publish(seat, FeedKind.Event, now, true, $"{Clock(now)} {name.ToUpperInvariant()}: out of the fight",
                    new JsonObject { ["kind"] = "event", ["t"] = Round(now), ["event"] = "combat-ineffective", ["squad"] = name }.ToJsonString());
        }
    }

    private void Snapshot(EntityRepository world, SeatRuntime seat, double now, bool wake = false)
    {
        seat.LastSnapshotAt = now;
        var squads = new JsonArray();
        var radio = new StringBuilder(Clock(now)).Append(" SNAPSHOT — ").Append(seat.Name).Append(':');
        var roster = Squads(world, seat.Commander);
        bool havePicture = world.HasComponent<CommandPicture>(seat.Commander);
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
            string? task = TaskOf(world, seat, squad);   // ⭐ CE-4680 B-4 — the order he is on (his commander's record)
            if (task != null) row["task"] = task;
            squads.Add(row);
            radio.Append(' ').Append(name.ToUpperInvariant()).Append(" at ").Append(row["place"]).Append(inFight ? "" : " (out of the fight)");
            var detail = new StringBuilder();
            if (task != null) detail.Append(task);
            if (row["progress"] is { } prog) detail.Append(detail.Length > 0 ? ", " : "").Append((string)prog!);
            if (row["strength"] is { } st) detail.Append(detail.Length > 0 ? ", " : "").Append("strength ").Append((string)st!).Append(" ammo ").Append((string)row["ammo"]!);
            if (row["reportAge"] is { } age) detail.Append(detail.Length > 0 ? ", " : "").Append("last report ").Append(((double)age!).ToString("0", CultureInfo.InvariantCulture)).Append(" s ago");
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
                    ["age"] = Round(now - el.ObservedAt.Seconds),
                });
                radio.Append(" enemy ").Append(el.Count).Append(" (").Append(el.Size).Append(')');
                AppendDetail(radio, el.Activity, el.Equipment, now - el.ObservedAt.Seconds);   // ⭐ CE-4680 B-5
                radio.Append(" at ").Append(seat.Places.Describe(p)).Append(';');
            }
        }
        var objectives = Objectives(world, seat, radio);   // ⭐ CE-4680 B-3
        var places = new JsonArray();
        foreach (var (n, _) in seat.Places.Places) places.Add(n);
        Publish(seat, FeedKind.Snapshot, now, wake, radio.ToString(),
                new JsonObject { ["kind"] = "snapshot", ["t"] = Round(now), ["commander"] = seat.Name, ["squads"] = squads, ["elements"] = elements, ["objectives"] = objectives, ["places"] = places }.ToJsonString());
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
        if (SubordinateOrders.Of(world, seat.Commander) is not { } held) return seat.Given.TryGetValue(squad, out var mine) ? mine : null;
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
        return seat.Given.TryGetValue(squad, out var given) ? given : null;
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
        sb.Append("You are ").Append(seat.Name).Append(", a platoon commander. You command squads, never single soldiers; they fight with their own drills.\n");
        sb.Append("Your squads:");
        for (int i = 0; i < roster.Count; i++)
            if (world.IsAlive(roster.SubordinateEntities[i])) sb.Append(' ').Append(NameOf(world, roster.SubordinateEntities[i]));
        // ⭐ CE-4680 B-2 — the mission, as his scenario gives it
        if (world.HasManagedComponent<CommanderBrief>(seat.Commander)
            && ((ISimulationView)world).GetManagedComponentRO<CommanderBrief>(seat.Commander) is { } brief && brief.Line() is { Length: > 0 } mission)
            sb.Append('\n').Append(mission);
        sb.Append("\nPlaces: ").Append(seat.Places.KnownNames()).Append(" — say a place as a name, \"N m <dir> of <name>\", or \"grid E<i> N<j>\".\n");
        // ⭐ CE-4680 B-6 — the places' terrain notes, one line each
        foreach (var (n, _) in seat.Places.Places)
            if (seat.Places.NoteOf(n) is { } note) sb.Append("  ").Append(n).Append(": ").Append(note).Append('\n');
        sb.Append("Actions: run_play(play, where[, support]) with play PlatoonAttack | PlatoonDefend | PlatoonBound | PlatoonHold — the play orders your squads for you · stop_play() · ")
          .Append("order(squad, kind, where) with kind Move | Hold | Attack | SupportByFire | Defend | BreakContact (a direct order ends a running play first) · ")
          .Append("hold(squad) · ask_sitrep(squad) · note(text).");   // ⭐ CE-4680 B-1
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
