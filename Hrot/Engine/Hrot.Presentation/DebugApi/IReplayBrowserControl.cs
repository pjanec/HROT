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
public interface IReplayBrowserControl : IRecordingOpener
{
    /// <summary>
    /// <c>POST /replay {path | paths}</c> — opens a recording file or a node group that need not be in the exercise inventory, at
    /// <paramref name="atSeconds"/> after its zero. ⛔ A cluster host has no such member: it replays only what its master recorded.
    /// ⭐ CE-1391 — a spelling of <see cref="IRecordingOpener.Open"/> (the ONE opener), not a second path.
    /// </summary>
    /// <returns><see langword="null"/> when opened, else why the group was refused (the federation's own message).</returns>
    string? Open(IReadOnlyList<string> paths, double atSeconds) => Open(RecordingName.Files(paths), atSeconds);

    /// <summary>The fields <c>GET /replay/status</c> adds on this host: frame, frames, span, nodes, view, rate.</summary>
    JsonObject Status();

    /// <summary><c>POST /replay/view {view}</c> — one node's world (<c>"1"</c>, its node id) or the merged world (<c>"merged"</c>), as the
    /// Federation panel switches. Returns why it refused, or <see langword="null"/>.</summary>
    string? View(string view);

    /// <summary><c>POST /replay/history {direction}</c> — back / forward through the playback waypoints (the Timeline's ◀ ▶).</summary>
    string? History(bool back);

    /// <summary><c>POST /selection/history {direction}</c> — back / forward through the units selected.</summary>
    string? SelectionHistory(bool back);

    /// <summary><c>POST /replay/export</c> — starts a JSON export of the shown node's recording, every option the Timeline panel has, on
    /// a thread of its own (never the GUI's sandbox). Returns the job id, or why it refused.</summary>
    (string? Job, string? Error) Export(ReplayExportRequest request);

    /// <summary><c>GET /replay/export/{job}</c> — the job's state, file and size; <see langword="null"/> for an unknown job.</summary>
    JsonObject? ExportStatus(string job);
}

/// <summary>
/// ⭐ RB-4 — one JSON export, every option of the Timeline panel's (<c>JsonExportOptions</c>): the window (<c>full</c> · <c>frames</c>
/// <c>from</c>–<c>to</c> · <c>time</c> seconds), the format (<c>absolute</c> · <c>changelog</c> · <c>incremental</c>), the units (network ids;
/// none = all), one entity by index, what to include, minified, the float tolerance, and where to write (default: a temp file).
/// </summary>
public sealed record ReplayExportRequest(
    string Window = "full",
    double? From = null,
    double? To = null,
    string Format = "absolute",
    IReadOnlyList<long>? Entities = null,
    int? EntityIndex = null,
    bool IncludeEntities = true,
    bool IncludeEvents = true,
    bool Minified = false,
    double Epsilon = 0.001,
    string? Output = null);
