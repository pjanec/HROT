using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Hrot.Presentation.DebugApi;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐ RB-3 / RB-4 (<c>docs/DESIGN_Replay_Browser_Debug_Http.md</c>) — <b>the routes only the standalone Replay Browser answers</b>: the
    /// node / merged view, the playback and selection histories, the JSON export jobs. ⭐ Every other host answers
    /// <c>NOT_SUPPORTED_HERE(replay.browser)</c> — they have no meaning on a cluster host (§0 RB-3).
    /// </summary>
    public sealed partial class DebugApiService
    {
        private IReplayBrowserControl Browser
            => _dispatcher?.ReplayBrowser ?? throw NotSupportedHere(DebugCapabilities.ReplayBrowser);

        /// <summary><c>POST /replay/view {view: "merged" | nodeId}</c></summary>
        public (JsonNode? Result, string? Error, string? HintCategory) SetReplayView(JsonNode? body)
        {
            var view = body?["view"]?.ToString();
            if (string.IsNullOrWhiteSpace(view)) return (null, "view is required: {\"view\": \"merged\"} or {\"view\": \"1\"} (a node id).", DebugApiHints.Scenario);
            var refused = Browser.View(view);
            return refused is null ? (Browser.Status(), null, null) : (null, refused, DebugApiHints.Scenario);
        }

        /// <summary><c>POST /replay/history {direction: "back" | "forward"}</c> — the playback waypoints.</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) StepReplayHistory(JsonNode? body)
            => StepHistory(body, back => Browser.History(back));

        /// <summary><c>POST /selection/history {direction: "back" | "forward"}</c> — the units selected.</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) StepSelectionHistory(JsonNode? body)
            => StepHistory(body, back => Browser.SelectionHistory(back));

        private (JsonNode? Result, string? Error, string? HintCategory) StepHistory(JsonNode? body, Func<bool, string?> step)
        {
            var direction = body?["direction"]?.ToString()?.ToLowerInvariant();
            if (direction is not ("back" or "forward"))
                return (null, "direction is \"back\" or \"forward\".", DebugApiHints.Scenario);
            var refused = step(direction == "back");
            return refused is null ? (Browser.Status(), null, null) : (null, refused, DebugApiHints.Scenario);
        }

        /// <summary><c>POST /replay/export {window, from, to, format, entities, entityIndex, includeEntities, includeEvents, minified, epsilon,
        /// output}</c> — starts a job; <c>GET /replay/export/{job}</c> reads it.</summary>
        public (JsonNode? Result, string? Error, string? HintCategory) StartReplayExport(JsonNode? body)
        {
            ReplayExportRequest request;
            try
            {
                List<long>? entities = null;
                if (body?["entities"] is JsonArray ids)
                {
                    entities = new List<long>();
                    foreach (var id in ids) if (id is not null) entities.Add(Long(id));
                }
                request = new ReplayExportRequest(
                    Window:          body?["window"]?.ToString() ?? "full",
                    From:            body?["from"] is JsonNode from ? double.Parse(from.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture) : null,
                    To:              body?["to"] is JsonNode to ? double.Parse(to.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture) : null,
                    Format:          body?["format"]?.ToString() ?? "absolute",
                    Entities:        entities,
                    EntityIndex:     body?["entityIndex"] is JsonNode ix ? Int(ix) : null,
                    IncludeEntities: body?["includeEntities"]?.GetValue<bool>() ?? true,
                    IncludeEvents:   body?["includeEvents"]?.GetValue<bool>() ?? true,
                    Minified:        body?["minified"]?.GetValue<bool>() ?? false,
                    Epsilon:         body?["epsilon"] is JsonNode eps ? double.Parse(eps.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture) : 0.001,
                    Output:          body?["output"]?.ToString());
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
                return (null, $"Invalid export options: {ex.Message}", DebugApiHints.Scenario);
            }
            var (job, error) = Browser.Export(request);
            return error is null
                ? (Browser.ExportStatus(job!), null, null)
                : (null, error, DebugApiHints.Scenario);
        }

        /// <summary><c>GET /replay/export/{job}</c></summary>
        public (JsonNode? Result, string? Error) GetReplayExport(string job)
            => Browser.ExportStatus(job) is { } status ? (status, null) : (null, $"No export job '{job}'.");
    }
}
