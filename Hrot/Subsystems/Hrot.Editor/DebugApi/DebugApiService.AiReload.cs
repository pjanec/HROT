using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Hrot.Editor.DebugApi
{
    public sealed partial class DebugApiService
    {
        /// <summary>⭐ <c>CE-1321</c> — <c>POST /ai/reload</c>: the editor's one C# behaviour rebuild (<see cref="AiBehaviorsRebuild"/>).</summary>
        public Task<AiReloadResult> RebuildAndReloadAi()
            => _editorLogic?.RebuildAndReloadAIAsync()
               ?? Task.FromResult(AiReloadResult.NotWired("This host wires no editor (no C# behaviour hot reload)."));

        public static JsonObject AiReloadJson(AiReloadResult r) => new()
        {
            ["build"] = r.Build, ["exitCode"] = r.ExitCode, ["errors"] = new JsonArray(r.Errors.Select(e => (JsonNode?)e).ToArray()),
            ["reloaded"] = r.Reloaded, ["note"] = r.Note,
            ["added"] = new JsonArray(r.Added.Select(e => (JsonNode?)e).ToArray()),
            ["removed"] = new JsonArray(r.Removed.Select(e => (JsonNode?)e).ToArray()),
            ["behaviours"] = r.Behaviours, ["documentsReconciled"] = r.DocumentsReconciled,
            ["registrarFailures"] = new JsonArray(r.RegistrarFailures.Select(e => (JsonNode?)e).ToArray()),
            ["seconds"] = System.Math.Round(r.Seconds, 1),
        };
    }
}
