using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.CGF.Commander;

/// <summary>Which mind fills a <see cref="CommanderSeat"/> (DESIGN_LLM_Commander §4).</summary>
public enum CommanderMind : byte
{
    /// <summary>Fixed rules (<see cref="ScriptedMind"/>) — the rails' mind: the plumbing proven with no model.</summary>
    Scripted = 1,
    /// <summary>An outside agent over HTTP (L-2 — not built in L-1).</summary>
    External = 2,
    /// <summary>⭐ CE-4681 (L-3) — a language model behind an Ollama-style chat server (<see cref="ChatModelMind"/>; the design's OllamaMind).</summary>
    ChatModel = 3,
}

/// <summary>How the simulation waits for the mind (DESIGN_LLM_Commander §3, "two run modes").</summary>
public enum CommanderMode : byte
{
    /// <summary>⭐ The frame waits for the mind's answer to a wake (bounded) — a run is repeatable. L-1: the frame holds; L-2 pauses the clock instead
    /// for a mind that takes seconds.</summary>
    ThinkPause = 0,
    /// <summary>The simulation never waits; an answer lands whenever it comes (L-4 adds the stale-action refusal).</summary>
    Continuous = 1,
}

/// <summary>
/// ⭐⭐ <c>CE-4676</c> (DESIGN_LLM_Commander §5, §9 ①; R-4298 "a scenario field on the commander") — a commander whose brain is a MIND: while the seat
/// exists his own play does not run (the seat puts <c>InertSop</c> on him and no task), and his orders come only from the mind, through
/// <see cref="CommanderActionSystem"/> → <c>CommandOrders</c>. Set in the scenario (<c>"CommanderSeat": {"Mind": "Scripted"}</c>), saved with it;
/// it never changes during a run, so recording it costs one keyframe value.
/// </summary>
[ComponentId(GlobalComponentIds.CommanderSeat)]
[StructLayout(LayoutKind.Sequential)]
public struct CommanderSeat
{
    public CommanderMind Mind;
    public CommanderMode Mode;
    /// <summary>A heartbeat item when nothing else came for this long (sim s); 0 = the default (30 s).</summary>
    public float HeartbeatSeconds;
    /// <summary>A fresh snapshot item this often (sim s); 0 = the default (120 s).</summary>
    public float SnapshotSeconds;

    public const float DefaultHeartbeatSeconds = 30f, DefaultSnapshotSeconds = 120f;

    public readonly float Heartbeat => HeartbeatSeconds > 0f ? HeartbeatSeconds : DefaultHeartbeatSeconds;
    public readonly float Snapshot => SnapshotSeconds > 0f ? SnapshotSeconds : DefaultSnapshotSeconds;
}
