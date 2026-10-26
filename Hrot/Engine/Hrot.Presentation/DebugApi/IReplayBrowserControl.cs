using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Hrot.Presentation.DebugApi;

/// <summary>
/// ⭐⭐ RB-3 (<c>docs/DESIGN_Replay_Browser_Debug_Http.md</c> §0, §3) — <b>the ONE seam only the standalone Replay Browser has</b>:
/// what no cluster host can mean. Everything a cluster host also does (enter / leave / seek a replay, play, pause, step, read the
/// world) goes through the SHARED provider members (<see cref="ISubsystemDebugProvider.Master"/>, <see cref="ISubsystemDebugProvider.Drive"/>,
/// <see cref="ISubsystemDebugProvider.World"/>) — ⛔ never a second way to say "seek".
/// </summary>
/// <remarks>
/// ⚠ Every member runs on the host's main thread (the debug service's job queue), as the panels do.
/// </remarks>
public interface IReplayBrowserControl
{
    /// <summary>
    /// <c>POST /replay {path | paths}</c> — opens a recording file or a node group that need not be in the exercise inventory, at
    /// <paramref name="atSeconds"/> after its zero. ⛔ A cluster host has no such member: it replays only what its master recorded.
    /// </summary>
    /// <returns><see langword="null"/> when opened, else why the group was refused (the federation's own message).</returns>
    string? Open(IReadOnlyList<string> paths, double atSeconds);

    /// <summary>The fields <c>GET /replay/status</c> adds on this host: frame, frames, span, nodes, view, rate.</summary>
    JsonObject Status();
}
