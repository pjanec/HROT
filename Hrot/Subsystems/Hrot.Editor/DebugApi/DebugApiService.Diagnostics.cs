using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.ModuleHost.Diagnostics;

namespace Hrot.Editor.DebugApi
{
    /// <summary>
    /// ⭐⭐⭐ <b><c>MD-002</c> — the DIAGNOSTICS surface, on the SHARED service.</b>
    /// 📄 <c>docs/DESIGN_Mcp_Diagnostics_Federation.md</c> §1 · §2.2.
    ///
    /// <para>⭐⭐ <b>Why "shared" is the load-bearing word.</b> 📐 Every node runs its own
    /// <c>DebugApiHost</c>, built either by <c>EditorSubsystem</c> *(full surface)* or by
    /// <c>ClusterRunner/Program.cs</c> *(cluster-limited)*. ⛔ A diagnostics route added to the
    /// editor-only path would leave a SimHost node unable to report what it is running — which defeats
    /// the federation this design exists to document.</para>
    /// </summary>
    public sealed partial class DebugApiService
    {
        // ⭐ The editor path has no dispatcher, so it hands its own kernel snapshot over directly.
        //   ⚠ A Func for the same reason the log sinks are one: the editor's kernel outlives neither
        //   the composition root nor a hot reload, and a captured service would answer for a dead one.
        private Func<IArchitectureDiagnosticsService?>? _editorArchitecture;
        private string _editorArchitectureName = "Editor";

        /// <summary>
        /// ⭐ Hands this service the EDITOR host's own architecture snapshot source.
        /// ⛔ Not needed on the cluster path — there the per-subsystem providers already carry it
        /// *(<c>ISubsystemDebugProvider.Architecture</c>)*, which is the seam the four subsystems fill.
        /// </summary>
        public void AttachArchitectureDiagnostics(
            Func<IArchitectureDiagnosticsService?> architecture, string subsystemName = "Editor")
        {
            _editorArchitecture     = architecture ?? throw new ArgumentNullException(nameof(architecture));
            _editorArchitectureName = string.IsNullOrWhiteSpace(subsystemName) ? "Editor" : subsystemName;
        }

        /// <summary>⭐ Exposed for the forwarding rail — ⛔ a rail must reach the CONSTRUCTED object.</summary>
        internal bool HasArchitectureDiagnostics => ArchitectureSources().Count > 0;

        /// <summary>
        /// ⭐⭐ Every kernel this NODE can report on, newest composition first.
        /// ⚠ Resolved per call, never cached: subsystems acquire their kernel in <c>Initialize</c>, which
        /// runs after the composition root built the providers.
        /// </summary>
        private List<(string Subsystem, string Perspective, IArchitectureDiagnosticsService Service)>
            ArchitectureSources()
        {
            var sources = new List<(string, string, IArchitectureDiagnosticsService)>();

            if (_editorArchitecture?.Invoke() is { } editorSvc)
                sources.Add((_editorArchitectureName, _editorArchitectureName, editorSvc));

            if (_dispatcher != null)
                foreach (var p in _dispatcher.AllProviders)
                    if (p.Architecture is { } svc)
                        sources.Add((p.SubsystemName, p.Perspective, svc));

            return sources;
        }

        private const string NoArchitectureDiagnostics =
            "This host reports no architecture diagnostics. A subsystem contributes them by passing "
          + "`architecture:` to its SubsystemDebugProvider (the cluster path) or by the composition root "
          + "calling AttachArchitectureDiagnostics (the editor path). A host with no ECS kernel at all "
          + "— an ExCon or orchestrator-only node — correctly has none: check GET /capabilities for the "
          + "'diagnostics.architecture' cell rather than reading this as a wiring bug.";

        // ══ MD-006 / MD-007: the CLUSTER-WIDE dump ════════════════════════════
        //
        // ⭐⭐⭐ NOTHING IS COLLECTED HERE. 📐 The dump-diag pipeline already fans out over CQRS intent,
        //    gathers on every selected node and pulls to NAS over SMB — that is what the operator's
        //    "Execute Diagnostic Dump" button drives, and it works. ⇒ ⛔ these two routes are a SECOND
        //    SURFACE on that one mechanism, never a second mechanism.
        // 📌 The first cut of this slice claimed the status half was unreachable, having measured
        //    `DiagnosticsDumpProcessManager` (which exposes only Tick()) instead of the read model the
        //    PANEL actually renders. ⚠ The panel reads `ClusterUiCache` — so that is what this reads.

        private const string NoClusterDump =
            "This host cannot trigger a cluster diagnostic dump: no subsystem here exposes an "
          + "orchestration bus. The dump is published as a CQRS intent that any node's bus carries to the "
          + "ClusterMaster, so a host with no such bus cannot ask. Check the 'diagnostics.clusterDump' "
          + "cell in GET /capabilities.";

        /// <summary>
        /// <c>POST /cluster/diagnostics/dump</c> — collect diagnostics on the selected nodes and pull them
        /// to the NAS, exactly as the ExCon's Execute button does.
        /// </summary>
        /// <remarks>
        /// ⚠⚠ <b>ASYNCHRONOUS and cluster-wide.</b> The response confirms the intent was PUBLISHED, ⛔ not
        /// that files exist — the gather runs on every selected node and the NAS pull follows. ⭐ Poll
        /// <c>GET /cluster/diagnostics/status</c> for the manifest.
        /// <para>⚠ Ruling 53: a headless origin never pre-flights a confirmation, so the request is
        /// LOGGED with its transaction id and target nodes — that log is the whole safety net.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) TriggerClusterDump(JsonNode? body)
        {
            var publish = _dispatcher?.RequestDiagnosticDumpAnyNode;
            if (publish == null) return (null, NoClusterDump, DebugApiHints.Panel);

            // ⭐ The node set. ⛔ An EMPTY list is refused rather than treated as "all": the panel disables
            //   its own button on an empty selection, and a dump of everything is a very different
            //   operation from a dump of one node.
            var nodes = new List<int>();
            if (body?["nodes"] is JsonArray nodeArr)
                foreach (var n in nodeArr)
                    if (n != null && int.TryParse(n.ToString(), out var id)) nodes.Add(id);

            if (nodes.Count == 0)
                return (null,
                        "Body must name at least one node: {\"nodes\": [1, 2]}. List the live ones with "
                      + "GET /cluster/diagnostics/status. An empty selection is refused rather than read as "
                      + "\"every node\" — the editor's own panel disables its button on the same condition.",
                        DebugApiHints.Panel);

            bool Flag(string name, bool fallback)
                => body?[name] is { } v && bool.TryParse(v.ToString(), out var b) ? b : fallback;

            var providers = new List<string>();
            if (body?["eventProviders"] is JsonArray provArr)
                foreach (var pn in provArr)
                    if (!string.IsNullOrWhiteSpace(pn?.ToString())) providers.Add(pn!.ToString());

            var dto = new Hrot.Network.Orchestration.DiagnosticDumpPayloadDto
            {
                TransactionId    = Guid.NewGuid(),
                RequestedAt      = DateTime.UtcNow,
                TargetNodeIds    = nodes.ToArray(),
                // ⭐ The same defaults the panel opens with: all four kinds on. An agent that wants less
                //   says so; ⛔ defaulting to nothing would produce an empty archive and look like a bug.
                DumpEvents       = Flag("dumpEvents",       true),
                DumpEntities     = Flag("dumpEntities",     true),
                DumpArchitecture = Flag("dumpArchitecture", true),
                DumpLogs         = Flag("dumpLogs",         true),
                EventProviders   = providers.Count > 0 ? providers.ToArray() : null,
                UseMarkdownWrapper = Flag("useMarkdown", false),
                MaxAgeHours      = body?["maxAgeHours"] is { } ma && float.TryParse(
                                       ma.ToString(), System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out var mah)
                                   ? mah : 24f,
                SeverityThreshold = body?["severityThreshold"] is { } st
                                    && int.TryParse(st.ToString(), out var sev) ? sev : 0,
            };

            var payloadJson = System.Text.Json.JsonSerializer.Serialize(
                dto, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);

            publish(new Fdp.Toolkit.Orchestration.ExecuteDiagnosticDumpIntent
            {
                RequestId   = dto.TransactionId,
                PayloadJson = payloadJson,
            });

            Console.WriteLine(
                $"[DebugApi] cluster diagnostic dump requested over MCP — tx={dto.TransactionId:N}, "
              + $"nodes=[{string.Join(", ", nodes)}], kinds=[events={dto.DumpEvents} entities={dto.DumpEntities} "
              + $"architecture={dto.DumpArchitecture} logs={dto.DumpLogs}]");

            return (new JsonObject
            {
                ["transactionId"] = dto.TransactionId.ToString(),
                ["nodes"]         = new JsonArray(nodes.Select(n => (JsonNode)n).ToArray()),
                ["queued"]        = true,
                ["note"]          = "the dump is CLUSTER-WIDE and asynchronous: every selected node gathers, "
                                  + "then the orchestrator pulls to the NAS. Poll GET /cluster/diagnostics/"
                                  + "status until manifestPaths is non-empty; this response only confirms "
                                  + "the request was published.",
            }, null, null);
        }

        /// <summary>
        /// <c>GET /cluster/diagnostics/status</c> — the live nodes, whether a cluster op is in flight, and
        /// the manifest of the last successful dump.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ Reads <c>ClusterUiCache</c> through the provider seam — **the same read model
        /// <c>ClusterDiagnosticsPanel</c> renders**, so this answers what a human at the console sees.
        /// <para>⚠ <c>manifestPaths</c> is EMPTY until the first successful dump completes: ⛔ empty means
        /// "none yet", not "the dump failed".</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) GetClusterDumpStatus()
        {
            var status = _dispatcher?.DumpStatusAnyNode;
            if (status == null)
                return (null,
                        "No node on this host caches cluster diagnostics state. The read model is "
                      + "ClusterUiCache, which only a subsystem that builds and pumps one can supply "
                      + "(in --mode all that is ExCon). A host without one cannot observe a dump it can "
                      + "still trigger.",
                        DebugApiHints.Panel);

            return (new JsonObject
            {
                ["inFlight"]      = status.InFlight,
                ["manifestPaths"] = new JsonArray(
                                        status.ManifestPaths.Select(x => (JsonNode)x).ToArray()),
                ["manifestCount"] = status.ManifestPaths.Count,
                ["note"]          = "manifestPaths are relative to the NAS base directory and describe the "
                                  + "LAST SUCCESSFUL dump. Empty means none has completed yet, not that one "
                                  + "failed. inFlight covers any cluster transaction, not only a dump.",
            }, null, null);
        }

        /// <summary>
        /// <c>GET /diagnostics/architecture[?subsystem=SimHost]</c> — this node's modules, systems and
        /// translators, per subsystem.
        /// </summary>
        /// <remarks>
        /// ⭐⭐ <b>Per SUBSYSTEM, not per node.</b> 📐 The design said *"each node answers for its own
        /// kernel"*; measuring found a <c>--mode all</c> node holds FOUR — SimHost, IG, CGF and the
        /// orchestrator — so a single snapshot would have had to pick one and drop the rest silently.
        /// <para>⚠ <b>Read-only and allocating.</b> <c>GetSnapshot()</c> allocates the whole DTO graph on
        /// every call; that is fine for an operator query and ⛔ wrong in a loop.</para>
        /// </remarks>
        public (JsonNode? Result, string? Error, string? HintCategory) GetArchitecture(string? subsystem)
        {
            var sources = ArchitectureSources();
            if (sources.Count == 0)
                return (null, NoArchitectureDiagnostics, DebugApiHints.Panel);

            if (!string.IsNullOrWhiteSpace(subsystem))
            {
                var matched = sources
                    .Where(s => string.Equals(s.Subsystem, subsystem, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(s.Perspective, subsystem, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matched.Count == 0)
                    return (null,
                            $"No subsystem '{subsystem}' reports architecture on this node. It reports: "
                          + string.Join(", ", sources.Select(s => $"'{s.Subsystem}'"))
                          + ". Omit the filter to get them all.",
                            DebugApiHints.Panel);

                sources = matched;
            }

            var subsystems = new JsonArray();
            foreach (var (name, perspective, svc) in sources)
            {
                ArchitectureSnapshotDto snapshot;
                try
                {
                    snapshot = svc.GetSnapshot();
                }
                catch (Exception ex)
                {
                    // ⛔ One subsystem throwing must not blank the whole node's answer — 📌 the same
                    //   no-silent-truncation rule the cluster aggregator follows: report WHICH failed.
                    subsystems.Add(new JsonObject
                    {
                        ["subsystem"] = name,
                        ["perspective"] = perspective,
                        ["error"] = $"snapshot failed: {ex.Message}",
                    });
                    continue;
                }

                subsystems.Add(new JsonObject
                {
                    ["subsystem"]   = name,
                    ["perspective"] = perspective,
                    ["modules"]     = new JsonArray(snapshot.Modules.Select(m => (JsonNode)new JsonObject
                    {
                        ["name"]              = m.ModuleName,
                        ["type"]              = m.ModuleTypeName,
                        ["runMode"]           = m.RunMode,
                        ["dataStrategy"]      = m.DataStrategy,
                        ["targetFrequencyHz"] = m.TargetFrequencyHz,
                        ["lifecycleState"]    = m.LifecycleState,
                        ["circuitState"]      = m.CircuitState,
                        ["executionCount"]    = m.ExecutionCount,
                        ["failureCount"]      = m.FailureCount,
                    }).ToArray()),
                    ["systems"] = new JsonArray(snapshot.Systems.Select(sys => (JsonNode)new JsonObject
                    {
                        ["phase"]      = sys.Phase,
                        ["module"]     = sys.ModuleName,
                        ["systemName"] = sys.Profile?.SystemName,
                    }).ToArray()),
                    ["translators"] = new JsonArray(snapshot.Translators.Select(t => (JsonNode)new JsonObject
                    {
                        ["systemName"]        = t.SystemName,
                        ["direction"]         = t.Direction,
                        ["topic"]             = t.TopicName,
                        ["descriptorOrdinal"] = t.DescriptorOrdinal,
                        ["receivedSamples"]   = t.ReceivedSamples,
                        ["sentSamples"]       = t.SentSamples,
                    }).ToArray()),
                    ["moduleCount"]     = snapshot.Modules.Count,
                    ["systemCount"]     = snapshot.Systems.Count,
                    ["translatorCount"] = snapshot.Translators.Count,
                });
            }

            return (new JsonObject
            {
                ["subsystems"] = subsystems,
                ["note"]       = "one entry per SUBSYSTEM on this node — a --mode all node runs several, "
                               + "each with its own ModuleHostKernel. Other nodes host their own MCP "
                               + "endpoint; ask each one for its own architecture.",
            }, null, null);
        }
    }
}
