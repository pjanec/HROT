using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Fdp.Core;
using Fdp.Toolkit.ScenarioRules;

namespace Hrot.UI.Common.Map3D;

/// <summary>Who is looking: the possessed unit's network id and side, and the station (the node id this station posts orders as).</summary>
public readonly record struct MessageViewer(int UnitNetId, ForceId Side, int StationId);

/// <summary>
/// ⭐⭐ CE-1461 W-M7 (docs/DESIGN_Node_Features.md §10 M-1; SR-106 / SR-121) — the PLAYER's messages: ONE pure model (no ImGui) that both the subtitle and the log read.
/// Written by <c>EffectSpawnSystem</c> from every <see cref="PlayerMessageEvent"/> on the bus (only while <c>hud.messages</c> is on), read by <c>PossessionStation</c>.
/// The viewer filter is applied at READ time, so holding another unit re-addresses the log. Presentation memory only — no wire.
/// </summary>
public sealed class PlayerMessages
{
    /// <summary>How many subtitles stack on screen.</summary>
    public const int SubtitleMax = 2;
    /// <summary>The log's cap (per viewer, newest kept).</summary>
    public const int LogCap = 100;
    /// <summary>The subtitle fades out over its last this-many seconds.</summary>
    public const float FadeSeconds = 0.5f;
    private const int RawCap = 300;

    /// <summary>One message as the HUD needs it (<see cref="ToKind"/> / <see cref="ToValue"/> kept for the read-time filter).</summary>
    public readonly record struct Entry(int MessageId, int Origin, string ToKind, int ToValue, string Text, float Seconds, byte Priority, double SentAt);

    private readonly List<Entry> _all = new();   // arrival order

    /// <summary>Keeps <paramref name="m"/>; false when it is a duplicate (same <c>MessageId</c> from the same origin) or empty.</summary>
    public bool Record(PlayerMessageEvent m)
    {
        if (m is null || string.IsNullOrWhiteSpace(m.Text)) return false;
        foreach (var e in _all)
            if (e.MessageId == m.MessageId && e.Origin == m.OriginNodeId) return false;
        _all.Add(new Entry(m.MessageId, m.OriginNodeId, m.ToKind ?? "All", m.ToValue, m.Text, m.Seconds, m.Priority, m.SentAt));
        if (_all.Count > RawCap) _all.RemoveRange(0, _all.Count - RawCap);
        return true;
    }

    /// <summary>Is a message to (<paramref name="kind"/>, <paramref name="value"/>) for <paramref name="viewer"/>? Instructor-addressed ones never are, and an unknown kind matches nothing.</summary>
    public static bool Addressed(string kind, int value, MessageViewer viewer) => kind switch
    {
        "All" => true,
        "Unit" => viewer.UnitNetId != 0 && value == viewer.UnitNetId,
        "Side" => value == (int)viewer.Side,
        "Station" => viewer.StationId != 0 && value == viewer.StationId,
        _ => false,   // "Instructor" and anything else
    };

    /// <summary>The subtitles to show at <paramref name="now"/> (sim seconds): up to <see cref="SubtitleMax"/>, higher priority first, then the newer.</summary>
    public IReadOnlyList<Entry> Active(double now, MessageViewer viewer)
        => _all.Where(e => Addressed(e.ToKind, e.ToValue, viewer) && now >= e.SentAt && now - e.SentAt <= e.Seconds)
               .OrderByDescending(e => e.Priority).ThenByDescending(e => e.SentAt).ThenByDescending(e => e.MessageId)
               .Take(SubtitleMax).ToList();

    /// <summary>The log for <paramref name="viewer"/>: newest first, at most <see cref="LogCap"/>.</summary>
    public IReadOnlyList<Entry> Log(MessageViewer viewer)
        => _all.Where(e => Addressed(e.ToKind, e.ToValue, viewer))
               .OrderByDescending(e => e.SentAt).ThenByDescending(e => e.MessageId)
               .Take(LogCap).ToList();

    /// <summary>1 while the subtitle is in its body, falling to 0 over its last <see cref="FadeSeconds"/>.</summary>
    public static float Alpha(in Entry e, double now)
    {
        double left = e.SentAt + e.Seconds - now;
        return left <= 0 ? 0f : left >= FadeSeconds ? 1f : (float)(left / FadeSeconds);
    }

    /// <summary>Word-wraps <paramref name="text"/> to lines no wider than <paramref name="maxWidth"/> by <paramref name="measure"/> (a single word wider than that stays whole).</summary>
    public static List<string> Wrap(string text, Func<string, float> measure, float maxWidth)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var trial = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && measure(trial) > maxWidth) { lines.Add(line); line = word; }
            else line = trial;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }

    /// <summary>The time as <c>m:ss</c>.</summary>
    public static string Clock(double seconds) => $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";

    private static readonly ConditionalWeakTable<EntityRepository, PlayerMessages> ByWorld = new();

    /// <summary>The world's messages (null when none has been recorded since the world was made).</summary>
    public static PlayerMessages? Of(EntityRepository world) => ByWorld.TryGetValue(world, out var m) ? m : null;

    /// <summary>The world's messages, made on first use.</summary>
    public static PlayerMessages In(EntityRepository world) => ByWorld.GetValue(world, _ => new PlayerMessages());
}
