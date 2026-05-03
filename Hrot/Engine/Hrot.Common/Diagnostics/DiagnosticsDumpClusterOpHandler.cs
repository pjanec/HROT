using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core.Diagnostics;
using Fdp.Core.Serialization;
using Fdp.ModuleHost.Diagnostics;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Serialization;
using Hrot.Common.Infrastructure;
using Hrot.Core.Diagnostics;
using Hrot.Network.Orchestration;

namespace Hrot.Common.Diagnostics
{
    /// <summary>
    /// Node-side 2PC participant for the <c>CollectDiagnostics</c> cluster operation.
    ///
    /// <para>
    /// On <see cref="PrepareAsync"/>, collects all requested diagnostic artefacts
    /// (entities, architecture snapshot, events, and log archive) into a per-transaction
    /// sub-directory under <c>LocalTempRoot/dumps/{transactionId:N}/</c>, then returns
    /// a <see cref="List{T}"/> of <see cref="FileManifestEntry"/> items describing the
    /// produced files.
    /// </para>
    ///
    /// <para>
    /// If the transaction's <c>TargetNodeIds</c> list is set and does not include
    /// this node's ID, <see cref="PrepareAsync"/> returns an empty list immediately.
    /// </para>
    ///
    /// <para>
    /// <see cref="Abort"/> deletes the entire output directory when called after
    /// a successful <see cref="PrepareAsync"/>.
    /// </para>
    /// </summary>
    public sealed class DiagnosticsDumpClusterOpHandler : IClusterStateHandler
    {
        private readonly IDiagnosticEventHistoryService    _eventHistoryService;
        private readonly IArchitectureDiagnosticsService  _architectureService;
        private readonly IEntityStateExtractionService    _entityExtractionService;
        private readonly ILogArchiveExtractionService     _logExtractionService;
        private readonly HrotNodeConfig                   _config;

        // Stored in PrepareAsync so Abort can clean up the directory.
        private string? _outputDir;

        /// <param name="eventHistoryService">Service supplying captured simulation events.</param>
        /// <param name="architectureService">Service supplying architecture diagnostics snapshots.</param>
        /// <param name="entityExtractionService">Service that extracts a full entity-state snapshot.</param>
        /// <param name="logExtractionService">Service that archives filtered NLog output.</param>
        /// <param name="config">Node configuration carrying <c>NodeId</c>, <c>SubsystemName</c>, and <c>LocalTempRoot</c>.</param>
        public DiagnosticsDumpClusterOpHandler(
            IDiagnosticEventHistoryService    eventHistoryService,
            IArchitectureDiagnosticsService   architectureService,
            IEntityStateExtractionService     entityExtractionService,
            ILogArchiveExtractionService      logExtractionService,
            HrotNodeConfig                    config)
        {
            _eventHistoryService    = eventHistoryService    ?? throw new ArgumentNullException(nameof(eventHistoryService));
            _architectureService    = architectureService    ?? throw new ArgumentNullException(nameof(architectureService));
            _entityExtractionService = entityExtractionService ?? throw new ArgumentNullException(nameof(entityExtractionService));
            _logExtractionService   = logExtractionService   ?? throw new ArgumentNullException(nameof(logExtractionService));
            _config                 = config                 ?? throw new ArgumentNullException(nameof(config));
        }

        /// <inheritdoc/>
        public bool CanHandle(NodeOpType operation) => operation == NodeOpType.CollectDiagnostics;

        /// <inheritdoc/>
        public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            // Offload to a long-running background thread to avoid stalling the 60 Hz loop.
            return Task.Factory.StartNew(
                () => CollectDiagnosticsAsync(intent, ct).GetAwaiter().GetResult(),
                ct,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        /// <inheritdoc/>
        public void Commit(ExecuteNodeOpIntent intent, Fdp.Core.EntityRepository? repo)
        {
            // No ECS mutation required for diagnostic dumps.
        }

        /// <inheritdoc/>
        public void Abort(ExecuteNodeOpIntent intent, Fdp.Core.EntityRepository? repo)
        {
            if (_outputDir != null && Directory.Exists(_outputDir))
                Directory.Delete(_outputDir, recursive: true);
            _outputDir = null;
        }

        // ── Private implementation ─────────────────────────────────────────────

        private async Task<object?> CollectDiagnosticsAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
        {
            // Deserialise the payload.  DomainPayload is set by NodeOpSlaveTranslator when
            // the operation is CollectDiagnostics; may be null in headless / unit-test paths.
            DiagnosticDumpPayloadDto dto;
            if (intent.DomainPayload is DiagnosticDumpPayloadDto typedDto)
            {
                dto = typedDto;
            }
            else
            {
                // Fallback: nothing to do.
                return new List<FileManifestEntry>();
            }

            // Skip if this node is not in the target list.
            if (dto.TargetNodeIds != null && !Array.Exists(dto.TargetNodeIds, id => id == _config.NodeId))
                return new List<FileManifestEntry>();

            string timestamp  = dto.RequestedAt.ToString("yyyyMMdd_HHmmss");
            string outputDir  = Path.Combine(_config.LocalTempRoot, "dumps", dto.TransactionId.ToString("N"));
            _outputDir = outputDir;
            Directory.CreateDirectory(outputDir);

            var manifest = new List<FileManifestEntry>();

            // ── Entities ───────────────────────────────────────────────────────
            if (dto.DumpEntities)
            {
                string fileName = $"dump_{timestamp}_entities_{_config.SubsystemName}_{_config.NodeId}.json";
                string filePath = Path.Combine(outputDir, fileName);

                var entityData = _entityExtractionService.ExtractEntities(null);
                string rawJson = JsonSerializer.Serialize(entityData, FdpJsonOptionsRegistry.Indented);
                string content = JsonAestheticFormatter.FlattenNumericArrays(rawJson);
                if (dto.UseMarkdownWrapper)
                    content = "```json\n" + content + "\n```";
                await WriteAllTextAsync(filePath, content, ct).ConfigureAwait(false);

                manifest.Add(new FileManifestEntry
                {
                    SourceUnc    = filePath,
                    RelativeDest = $"dumps/{dto.TransactionId:N}/{fileName}",
                });
            }

            // ── Architecture ───────────────────────────────────────────────────
            if (dto.DumpArchitecture)
            {
                string fileName = $"dump_{timestamp}_architecture_{_config.SubsystemName}_{_config.NodeId}.json";
                string filePath = Path.Combine(outputDir, fileName);

                ArchitectureSnapshotDto snapshot = _architectureService.GetSnapshot();
                string rawJson = JsonSerializer.Serialize((object)snapshot, FdpJsonOptionsRegistry.Indented);
                string content = JsonAestheticFormatter.FlattenNumericArrays(rawJson);
                if (dto.UseMarkdownWrapper)
                    content = "```json\n" + content + "\n```";
                await WriteAllTextAsync(filePath, content, ct).ConfigureAwait(false);

                manifest.Add(new FileManifestEntry
                {
                    SourceUnc    = filePath,
                    RelativeDest = $"dumps/{dto.TransactionId:N}/{fileName}",
                });
            }

            // ── Events ─────────────────────────────────────────────────────────
            if (dto.DumpEvents)
            {
                string fileName = $"dump_{timestamp}_events_{_config.SubsystemName}_{_config.NodeId}.json";
                string filePath = Path.Combine(outputDir, fileName);

                string?[] providers = dto.EventProviders != null && dto.EventProviders.Length > 0
                    ? (string?[])dto.EventProviders
                    : new string?[] { null };

                var eventDict = new Dictionary<string, List<CapturedEventDto>>();
                foreach (string? provider in providers)
                {
                    string key = provider ?? "all";
                    IReadOnlyList<string>? filter = provider != null
                        ? new[] { provider }
                        : null;
                    var events = _eventHistoryService.GetHistory(filter);
                    eventDict[key] = new List<CapturedEventDto>(events);
                }

                string rawJson = JsonSerializer.Serialize(eventDict, FdpJsonOptionsRegistry.Indented);
                string content = JsonAestheticFormatter.FlattenNumericArrays(rawJson);
                if (dto.UseMarkdownWrapper)
                    content = "```json\n" + content + "\n```";
                await WriteAllTextAsync(filePath, content, ct).ConfigureAwait(false);

                manifest.Add(new FileManifestEntry
                {
                    SourceUnc    = filePath,
                    RelativeDest = $"dumps/{dto.TransactionId:N}/{fileName}",
                });
            }

            // ── Logs ───────────────────────────────────────────────────────────
            if (dto.DumpLogs && !string.IsNullOrEmpty(_config.LogDirectory))
            {
                string fileName = $"dump_{timestamp}_logs_{_config.SubsystemName}_{_config.NodeId}.log";
                string filePath = Path.Combine(outputDir, fileName);

                await _logExtractionService.ExtractLogsAsync(
                    filePath,
                    dto.SeverityThreshold,
                    dto.MaxAgeHours,
                    ct).ConfigureAwait(false);

                if (File.Exists(filePath))
                {
                    manifest.Add(new FileManifestEntry
                    {
                        SourceUnc    = filePath,
                        RelativeDest = $"dumps/{dto.TransactionId:N}/{fileName}",
                    });
                }
            }

            return manifest;
        }

        private static async Task WriteAllTextAsync(string path, string content, CancellationToken ct)
        {
            using var writer = new StreamWriter(path, append: false, System.Text.Encoding.UTF8);
            await writer.WriteAsync(content.AsMemory(), ct).ConfigureAwait(false);
        }
    }
}
