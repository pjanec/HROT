using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>One row of a commander's report or order history, newest first.</summary>
/// <param name="At">Sim seconds.</param>
public sealed record BrainHistoryRow(float At, string Text, long CounterpartNetId);

/// <summary>
/// ⭐⭐ <c>CE-1312</c> (Brain View §12d BV-56…BV-58, §12f's commander) — what a COMMANDER's Brain tab shows, from recorded components
/// only (R-1040, so it replays): his reported picture (<see cref="CommandPicture"/>: elements, open requests, one line per subordinate),
/// his units' merged contacts (<see cref="SquadCognitiveState.Contacts"/> — "My units' contacts", at ANY echelon), and the report and
/// order HISTORIES (<see cref="CommandReportHistory"/>, <see cref="CommandOrderHistory"/>, CE-1319). Pure: no ImGui, so a rail reads it.
/// </summary>
public sealed record BrainCommandView(
    bool HasPicture,
    IReadOnlyList<string> Picture,
    IReadOnlyList<string> Subordinates,
    bool HasContacts,
    IReadOnlyList<string> Contacts,
    IReadOnlyList<BrainHistoryRow> Reports,
    IReadOnlyList<BrainHistoryRow> OrdersReceived,
    IReadOnlyList<BrainHistoryRow> OrdersSent)
{
    /// <summary>The unit's commander view, or null when it is no commander (no roster, picture, pool or history).</summary>
    public static BrainCommandView? Read(ISimulationView view, Entity unit, Func<int, string?> behaviourName, float now)
    {
        bool picture = Has<CommandPicture>(view, unit), pool = Has<SquadCognitiveState>(view, unit) && Has<UnitRoster>(view, unit);
        bool reports = Has<CommandReportHistory>(view, unit), orders = Has<CommandOrderHistory>(view, unit);
        if (!picture && !pool && !reports && !orders) return null;

        var pic = new List<string>();
        var subs = new List<string>();
        if (picture)
        {
            ref readonly var p = ref view.GetComponentRO<CommandPicture>(unit);
            for (int i = 0; i < Math.Min(p.Elements.Count, CommandPicture.MaxElements); i++)
            {
                var e = p.Elements.Items[i];
                pic.Add($"Enemy  {Known(e.Size)}x{e.Count}  {Known(e.Activity)}({e.At.X:0}, {e.At.Y:0})  from #{e.FromNetId}  {Age(now, e.ReceivedAt)}");
            }
            for (int i = 0; i < Math.Min(p.Requests.Count, CommandPicture.MaxRequests); i++)
            {
                var r = p.Requests.Items[i];
                pic.Add($"Request  {r.Kind}  from #{r.FromNetId}  {Age(now, r.ReceivedAt)}");
            }
            for (int i = 0; i < Math.Min(p.Subordinates.Count, CommandPicture.MaxSubordinates); i++)
            {
                var s = p.Subordinates.Items[i];
                subs.Add($"#{s.UnitNetId}  {s.Progress}" + Colours(s.Strength, s.Ammo) +
                         (s.LastReportAt > 0 ? $"  last report {Age(now, s.LastReportAt)}" : "  no report yet"));
            }
        }

        var contacts = new List<string>();
        if (pool)
        {
            ref readonly var st = ref view.GetComponentRO<SquadCognitiveState>(unit);
            for (int i = 0; i < Math.Min(st.Contacts.Count, 16); i++)
            {
                var c = st.Contacts.Contacts[i];
                contacts.Add($"({c.PositionX:0}, {c.PositionY:0})  entity {c.EntityId}");
            }
        }

        var rep = new List<BrainHistoryRow>();
        if (reports)
        {
            var h = view.GetComponentRO<CommandReportHistory>(unit);
            for (int i = 0; i < Math.Min(h.Count, CommandReportHistory.Capacity); i++)
            {
                var r = h.Items[i];
                string dir = r.Traffic == CommandTraffic.Outgoing ? $"me -> #{r.ToNetId}" : $"#{r.FromNetId} -> me";
                string fate = r.Fate is ReportFate.Stale ? "  (stale, dropped)" : "";
                rep.Add(new BrainHistoryRow(r.At, $"{dir}  {ReportText(in r.Report)}{fate}",
                    r.Traffic == CommandTraffic.Outgoing ? r.ToNetId : r.FromNetId));
            }
        }

        var received = new List<BrainHistoryRow>();
        var sent = new List<BrainHistoryRow>();
        if (orders)
        {
            var h = view.GetComponentRO<CommandOrderHistory>(unit);
            for (int i = 0; i < Math.Min(h.Count, CommandOrderHistory.Capacity); i++)
            {
                var o = h.Items[i];
                string what = behaviourName(o.Behaviour) ?? $"#{o.Behaviour}";
                string why = o.CauseKind == BrainJournalKind.None ? "" : $"  because {CauseText(o, behaviourName)}";
                string again = o.Repeats > 0 ? $"  (again ×{o.Repeats})" : "";
                if (o.Traffic == CommandTraffic.Incoming)
                    received.Add(new BrainHistoryRow(o.At, $"from #{o.CounterpartNetId}  {what}{why}{again}", o.CounterpartNetId));
                else
                    sent.Add(new BrainHistoryRow(o.At, $"to #{o.CounterpartNetId}  {what}{why}{again}", o.CounterpartNetId));
            }
        }

        return new BrainCommandView(picture, pic, subs, pool, contacts, rep, received, sent);
    }

    /// <summary>The cause an order entry copied from the giver's journal, as words.</summary>
    internal static string CauseText(in CommandOrderEntry o, Func<int, string?> behaviourName) => o.CauseKind switch
    {
        BrainJournalKind.PlayStarted     => "the play started",
        BrainJournalKind.PlayPhase       => $"the play's phase changed ({(Fdp.Toolkit.Squad.Primitives.PhaseEventKind)o.Cause})",
        BrainJournalKind.TaskStarted     => $"his task {behaviourName(o.CauseA) ?? $"#{o.CauseA}"} started",
        BrainJournalKind.DecisionSwitched => "a decision switched",
        BrainJournalKind.StateChanged    => "a state changed",
        BrainJournalKind.SopStarted      => "his SOP started",
        _                                => o.CauseKind.ToString(),
    };

    internal static string ReportText(in CommandReport r) => r.Kind switch
    {
        ReportKind.Contact  => $"CONTACT  ({r.At.X:0}, {r.At.Y:0})",
        ReportKind.Spot     => $"SPOT  {Known(r.Size)}x{r.Count} {Known(r.Activity)}({r.At.X:0}, {r.At.Y:0})",
        ReportKind.Sitrep   => $"SITREP {Colours(r.Strength, r.Ammo)}, phase {r.Phase}",
        ReportKind.Lace     => $"LACE  ammo {r.Ammo}, WIA {r.Wia}, KIA {r.Kia}",
        ReportKind.Progress => $"PROGRESS  {(ProgressKind)r.Subtype}",
        ReportKind.Request  => $"REQUEST  {(RequestKind)r.Subtype}",
        _                   => r.Kind.ToString(),
    };

    /// <summary>An enum value as words, or nothing for its "unknown" member (0) — an unreported field is not news.</summary>
    private static string Known<TEnum>(TEnum v) where TEnum : struct, Enum
        => Convert.ToInt32(v) == 0 ? "" : v + " ";

    private static string Colours(StatusColour strength, StatusColour ammo)
        => (Convert.ToInt32(strength) == 0 ? "" : $"  strength {strength}") + (Convert.ToInt32(ammo) == 0 ? "" : $"  ammo {ammo}");

    private static string Age(float now, float at) => at <= 0 ? "" : $"{Math.Max(0f, now - at):0} s ago";

    private static bool Has<T>(ISimulationView view, Entity e) where T : unmanaged
        => (view is not EntityRepository r || r.IsComponentTypeRegistered<T>()) && view.HasComponent<T>(e);
}
