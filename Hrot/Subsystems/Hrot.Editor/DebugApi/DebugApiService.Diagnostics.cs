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
