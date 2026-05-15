using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.Serialization;
using Fdp.Presentation.Abstractions;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Serialization;
using Hrot.Network.Orchestration;
using Hrot.Orchestrator.Events;
using ImGuiNET;

namespace Hrot.Orchestrator.Panels;

/// <summary>
/// ImGui panel for triggering cluster-wide diagnostic dumps and browsing the resulting
/// file manifest.
///
/// <para>CQRS read-model: reads cluster node state from <see cref="ClusterUiCache"/>;
/// sends commands via <see cref="FdpEventBus"/>. No direct DDS type references.</para>
/// </summary>
public sealed class ClusterDiagnosticsPanel
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    private readonly ClusterUiCache       _uiCache;
    private readonly FdpEventBus          _bus;
    private readonly IFileDialogService   _fileDialogService;
    private readonly string               _nasBasePath;

    // ── Configuration section state ───────────────────────────────────────────
    private bool   _useMarkdownWrapper;
    private bool   _dumpEvents       = true;
    private bool   _dumpEntities     = true;
    private bool   _dumpArchitecture = true;
    private bool   _dumpLogs         = true;
    private string _eventProvidersInput = "World";
    private float  _maxAgeHours      = 24f;
    private int    _severityThreshold = 0;

    // ── Results tree state ────────────────────────────────────────────────────
    private List<FileManifestEntry>              _manifest      = new();
    private IReadOnlyList<FileManifestEntry>     _lastCacheManifest
        = Array.Empty<FileManifestEntry>();

    private string  _operationStatus = string.Empty;
    private string? _inlineError;

    // Clipboard marshal: background threads write here; Render() calls SetClipboardText.
    private volatile string? _pendingClipboardText;
    private bool             _copyInProgress;

    // Merge log state
    private string? _mergedLogPath;
    private bool    _mergeInProgress;

    // Subsystem column selection (key = subsystem type name, e.g. "SimHost")
    private readonly Dictionary<string, bool> _subsystemSelected = new();

    // ── Static colours ────────────────────────────────────────────────────────
    private static readonly Vector4 ColRed    = new(1f, 0.3f, 0.3f, 1f);
    private static readonly Vector4 ColYellow = new(1f, 0.9f, 0.1f, 1f);
    private static readonly string[] s_severityNames = Enum.GetNames<Fdp.Core.Logging.LogSeverity>();

    /// <param name="uiCache">CQRS read-side: cluster node state and dump manifests.</param>
    /// <param name="bus">Event bus for publishing intents and reading local events.</param>
    /// <param name="fileDialogService">Save-as dialog service injected by the subsystem.</param>
    /// <param name="nasBasePath">Base NAS directory used to resolve file manifest paths.</param>
    public ClusterDiagnosticsPanel(
        ClusterUiCache     uiCache,
        FdpEventBus        bus,
        IFileDialogService fileDialogService,
        string             nasBasePath)
    {
        _uiCache           = uiCache          ?? throw new ArgumentNullException(nameof(uiCache));
        _bus               = bus              ?? throw new ArgumentNullException(nameof(bus));
        _fileDialogService = fileDialogService ?? throw new ArgumentNullException(nameof(fileDialogService));
        _nasBasePath       = nasBasePath      ?? string.Empty;
    }

    // ── Main render entry point ───────────────────────────────────────────────

    /// <summary>Renders the full diagnostics panel for the current ImGui frame.</summary>
    public void Render()
    {
        SyncManifestFromCache();
        DrainLogMergeEvents();
        FlushClipboard();

        RenderConfigurationSection();
        ImGui.Separator();
        RenderStatusSection();
        ImGui.Separator();
        RenderResultsSection();
    }

    // ── Frame-level state updates ─────────────────────────────────────────────

    private void SyncManifestFromCache()
    {
        var cached = _uiCache.LastDiagnosticManifest;
        if (!ReferenceEquals(cached, _lastCacheManifest))
        {
            _lastCacheManifest = cached;
            if (cached.Count > 0)
            {
                _manifest         = cached.ToList();
                _mergedLogPath    = null;
                _mergeInProgress  = false;
                _inlineError      = null;
            }
        }
    }

    private void DrainLogMergeEvents()
    {
        foreach (var ev in _bus.ReadManaged<LogMergeCompletedEvent>())
        {
            _mergedLogPath   = ev.NasPath;
            _mergeInProgress = false;
        }
    }

    private void FlushClipboard()
    {
        string? pending = _pendingClipboardText;
        if (pending != null)
        {
            ImGui.SetClipboardText(pending);
            _pendingClipboardText = null;
        }
    }

    // ── Section 1: Configuration ──────────────────────────────────────────────

    private void RenderConfigurationSection()
    {
        ImGui.Text("Diagnostic Dump Configuration");

        ImGui.Checkbox("##markdown", ref _useMarkdownWrapper);
        ImGui.SameLine();
        ImGui.Text("Markdown wrapper");

        ImGui.Separator();

        ImGui.Text("Dump kinds:");
        ImGui.SameLine();
        ImGui.Checkbox("Events##kind",       ref _dumpEvents);
        ImGui.SameLine();
        ImGui.Checkbox("Entities##kind",     ref _dumpEntities);
        ImGui.SameLine();
        ImGui.Checkbox("Architecture##kind", ref _dumpArchitecture);
        ImGui.SameLine();
        ImGui.Checkbox("Logs##kind",         ref _dumpLogs);

        ImGui.InputText(
            "Event providers (comma-sep)##evtprov",
            ref _eventProvidersInput,
            512);

        ImGui.SliderFloat("Max log age (hours)##maxage", ref _maxAgeHours, 0f, 168f);
        ImGui.Combo("Severity threshold##sev", ref _severityThreshold, s_severityNames, s_severityNames.Length);

        ImGui.Separator();
        RenderSubsystemMatrix();
        ImGui.Separator();
        RenderExecuteButton();
    }

    private void RenderSubsystemMatrix()
    {
        ImGui.Text("Target subsystems:");

        var subsystems = _uiCache.ActiveNodes
            .Values
            .Select(n => n.SubsystemName)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        if (subsystems.Count == 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(no active nodes)");
            return;
        }

        foreach (var sub in subsystems)
        {
            if (!_subsystemSelected.ContainsKey(sub))
                _subsystemSelected[sub] = false;

            bool sel = _subsystemSelected[sub];
            if (ImGui.Checkbox(sub + "##subsys", ref sel))
                _subsystemSelected[sub] = sel;
            ImGui.SameLine();
        }
        ImGui.NewLine();
    }

    private void RenderExecuteButton()
    {
        // Collect selected node IDs.
        var targetNodeIds = _uiCache.ActiveNodes
            .Where(kvp => _subsystemSelected.GetValueOrDefault(kvp.Value.SubsystemName))
            .Select(kvp => kvp.Key)
            .ToArray();

        bool canExecute = targetNodeIds.Length > 0;
        if (!canExecute)
            ImGui.BeginDisabled();

        if (ImGui.Button("Execute Diagnostic Dump"))
        {
            var providers = _eventProvidersInput
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();

            var dto = new DiagnosticDumpPayloadDto
            {
                TransactionId    = Guid.NewGuid(),
                RequestedAt      = DateTime.UtcNow,
                TargetNodeIds    = targetNodeIds,
                DumpEvents       = _dumpEvents,
                DumpEntities     = _dumpEntities,
                DumpArchitecture = _dumpArchitecture,
                DumpLogs         = _dumpLogs,
                EventProviders   = providers.Length > 0 ? providers : null,
                UseMarkdownWrapper = _useMarkdownWrapper,
                MaxAgeHours      = _maxAgeHours,
                SeverityThreshold = _severityThreshold,
            };

            string payloadJson = JsonSerializer.Serialize(dto, FdpJsonOptionsRegistry.DefaultRelaxed);
            _bus.PublishManaged(new ExecuteDiagnosticDumpIntent
            {
                RequestId   = dto.TransactionId,
                PayloadJson = payloadJson,
            });
            _operationStatus = $"Dump triggered: {dto.TransactionId:N}";
            _inlineError     = null;
        }

        if (!canExecute)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextColored(ColRed, "Select at least one subsystem");
        }
    }

    // ── Section 2: Status ─────────────────────────────────────────────────────

    private void RenderStatusSection()
    {
        if (!string.IsNullOrEmpty(_operationStatus))
            ImGui.Text(_operationStatus);

        if (!string.IsNullOrEmpty(_inlineError))
            ImGui.TextColored(ColYellow, _inlineError);
    }

    // ── Section 3: Results tree ───────────────────────────────────────────────

    private void RenderResultsSection()
    {
        if (_manifest.Count == 0)
        {
            ImGui.TextDisabled("No diagnostic data yet.");
            return;
        }

        // Group files by subsystem name (extracted from the RelativeDest path).
        var bySubsystem = new Dictionary<string, List<FileManifestEntry>>();
        foreach (var entry in _manifest)
        {
            var sub = ExtractSubsystemFromPath(entry.RelativeDest);
            if (!bySubsystem.TryGetValue(sub, out var list))
                bySubsystem[sub] = list = new List<FileManifestEntry>();
            list.Add(entry);
        }

        foreach (var (subsystemName, entries) in bySubsystem.OrderBy(kv => kv.Key))
        {
            if (ImGui.TreeNode(subsystemName + "##sub"))
            {
                // Per-subsystem context menu.
                RenderSubsystemContextMenu(subsystemName, entries);

                // Group by node within the subsystem.
                var byNode = new Dictionary<string, List<FileManifestEntry>>();
                foreach (var e in entries)
                {
                    var nodeKey = ExtractNodeKeyFromPath(e.RelativeDest);
                    if (!byNode.TryGetValue(nodeKey, out var nlist))
                        byNode[nodeKey] = nlist = new List<FileManifestEntry>();
                    nlist.Add(e);
                }

                foreach (var (nodeKey, nodeEntries) in byNode.OrderBy(kv => kv.Key))
                {
                    if (ImGui.TreeNode(nodeKey + "##node"))
                    {
                        foreach (var entry in nodeEntries)
                            RenderFileEntry(entry);
                        ImGui.TreePop();
                    }
                }
                ImGui.TreePop();
            }
            else
            {
                // Context menu available even when tree is collapsed.
                RenderSubsystemContextMenu(subsystemName, entries);
            }
        }

        // Merge logs button.
        RenderMergeLogsButton();

        // Cluster aggregates section (post-merge).
        RenderClusterAggregates();
    }

    // ── File entry rendering ──────────────────────────────────────────────────

    private void RenderFileEntry(FileManifestEntry entry)
    {
        string fileName = Path.GetFileName(entry.RelativeDest);
        ImGui.Selectable(
            fileName + "##file_" + entry.RelativeDest.GetHashCode(),
            false,
            ImGuiSelectableFlags.AllowDoubleClick);

        if (ImGui.BeginPopupContextItem("##ctx_file_" + entry.RelativeDest.GetHashCode()))
        {
            RenderFileContextMenu(entry);
            ImGui.EndPopup();
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            OpenFileFromNas(entry);
        }
    }

    private void RenderFileContextMenu(FileManifestEntry entry)
    {
        string fullPath = string.IsNullOrEmpty(_nasBasePath)
            ? entry.RelativeDest
            : Path.Combine(_nasBasePath, entry.RelativeDest);

        if (ImGui.MenuItem("Copy NAS Path"))
        {
            ImGui.SetClipboardText(entry.RelativeDest);
            _inlineError = null;
        }

        if (ImGui.MenuItem("Copy Content"))
        {
            if (!File.Exists(fullPath))
            {
                _inlineError = "File not found: " + fullPath;
            }
            else
            {
                var info = new FileInfo(fullPath);
                if (info.Length > 10 * 1024 * 1024)
                {
                    _inlineError = "File too large (>10 MB)";
                }
                else
                {
                    ImGui.SetClipboardText(File.ReadAllText(fullPath));
                    _inlineError = null;
                }
            }
        }

        if (ImGui.MenuItem("Open from NAS"))
        {
            OpenFileFromNas(entry);
        }

        if (ImGui.MenuItem("Save Local Copy As"))
        {
            var capturedFull = fullPath;
            var capturedName = Path.GetFileName(entry.RelativeDest);
            var capturedExt  = "*" + Path.GetExtension(entry.RelativeDest);

            async void SaveLocal()
            {
                var dest = await _fileDialogService.ShowSaveAsDialogAsync("Orchestrator_SaveLocalCopy", capturedName, capturedExt);
                if (dest != null)
                    File.Copy(capturedFull, dest, overwrite: true);
            }
            SaveLocal();
        }
    }

    // ── Subsystem context menu ────────────────────────────────────────────────

    private void RenderSubsystemContextMenu(string subsystemName, List<FileManifestEntry> entries)
    {
        string popupId = "##ctx_sub_" + subsystemName.GetHashCode();
        if (ImGui.BeginPopupContextItem(popupId))
        {
            bool canCopy = !_copyInProgress;
            if (!canCopy)
            {
                ImGui.TextDisabled("Copying...");
            }
            else if (ImGui.MenuItem("Copy Aggregated JSON"))
            {
                _copyInProgress = true;
                var capturedName    = subsystemName;
                var capturedEntries = new List<FileManifestEntry>(entries);
                var capturedNas     = _nasBasePath;
                Task.Run(() => BuildAggregatedJson(capturedName, capturedEntries, capturedNas))
                    .ContinueWith(t =>
                    {
                        _pendingClipboardText = t.IsCompletedSuccessfully ? t.Result : string.Empty;
                        _copyInProgress       = false;
                    }, TaskScheduler.Default);
            }
            ImGui.EndPopup();
        }
    }

    // ── Merge logs section ────────────────────────────────────────────────────

    private void RenderMergeLogsButton()
    {
        bool hasLogs = _manifest.Any(e => e.RelativeDest.EndsWith(".log",
            StringComparison.OrdinalIgnoreCase));
        bool disabled = !hasLogs || _mergeInProgress;

        if (disabled) ImGui.BeginDisabled();

        if (ImGui.Button("Generate Merged Cluster Log"))
        {
            var logPaths = _manifest
                .Where(e => e.RelativeDest.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.RelativeDest)
                .ToArray();

            _mergeInProgress = true;
            _bus.PublishManaged(new MergeLogsIntent
            {
                LogRelativePaths = logPaths,
                NasBasePath      = _nasBasePath,
                DumpTimestamp    = ExtractTimestampFromManifest(),
            });
        }

        if (disabled) ImGui.EndDisabled();
    }

    private void RenderClusterAggregates()
    {
        if (_mergedLogPath == null) return;

        if (ImGui.TreeNode("Cluster Aggregates"))
        {
            var relDest = string.IsNullOrEmpty(_nasBasePath)
                ? _mergedLogPath
                : Path.GetRelativePath(_nasBasePath, _mergedLogPath);

            var entry = new FileManifestEntry
            {
                SourceUnc    = _mergedLogPath,
                RelativeDest = relDest,
            };
            RenderFileEntry(entry);
            ImGui.TreePop();
        }
    }

    // ── Background aggregation ────────────────────────────────────────────────

    // Runs on thread pool — NO ImGui calls allowed here.
    private string BuildAggregatedJson(
        string subsystemName,
        List<FileManifestEntry> entries,
        string nasBasePath)
    {
        var entityLists = new List<JsonElement>();
        var eventDicts  = new Dictionary<string, List<JsonElement>>();

        var docOptions = new JsonDocumentOptions { AllowTrailingCommas = true };

        foreach (var entry in entries)
        {
            var fullPath = string.IsNullOrEmpty(nasBasePath)
                ? entry.RelativeDest
                : Path.Combine(nasBasePath, entry.RelativeDest);

            if (!File.Exists(fullPath)) continue;
            var info = new FileInfo(fullPath);
            if (info.Length > 10 * 1024 * 1024) continue;

            string json;
            try { json = File.ReadAllText(fullPath); }
            catch { continue; }

            try
            {
                if (entry.RelativeDest.Contains("_entities_", StringComparison.OrdinalIgnoreCase))
                {
                    using var doc = JsonDocument.Parse(json, docOptions);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        foreach (var el in doc.RootElement.EnumerateArray())
                            entityLists.Add(el.Clone());
                }
                else if (entry.RelativeDest.Contains("_events_", StringComparison.OrdinalIgnoreCase))
                {
                    using var doc = JsonDocument.Parse(json, docOptions);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            if (!eventDicts.ContainsKey(prop.Name))
                                eventDicts[prop.Name] = new List<JsonElement>();
                            if (prop.Value.ValueKind == JsonValueKind.Array)
                                foreach (var ev in prop.Value.EnumerateArray())
                                    eventDicts[prop.Name].Add(ev.Clone());
                        }
                }
            }
            catch { /* Malformed JSON — skip file. */ }
        }

        // Build result object.
        using var ms     = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WritePropertyName(subsystemName);

        if (entityLists.Count > 0)
        {
            writer.WriteStartArray();
            foreach (var el in entityLists) el.WriteTo(writer);
            writer.WriteEndArray();
        }
        else if (eventDicts.Count > 0)
        {
            writer.WriteStartObject();
            foreach (var (provider, events) in eventDicts)
            {
                writer.WritePropertyName(provider);
                writer.WriteStartArray();
                foreach (var ev in events) ev.WriteTo(writer);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WriteEndObject();
        writer.Flush();

        string raw = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        return JsonAestheticFormatter.FlattenNumericArrays(raw);
    }

    // ── Path helper utilities ─────────────────────────────────────────────────

    /// <summary>
    /// Extracts a subsystem identifier from a RelativeDest path.
    /// Path format: <c>dumps/{txId}/{fileName}</c> where fileName is like
    /// <c>dump_20260503_120000_entities_SimHost_400.json</c>.
    /// Returns the subsystem name segment (e.g. "SimHost") or "Unknown".
    /// </summary>
    private static string ExtractSubsystemFromPath(string relDest)
    {
        var fileName = Path.GetFileNameWithoutExtension(relDest);
        // Expected: dump_DATE_TIME_KIND_SUBSYSTEM_NODEID
        var parts = fileName.Split('_');
        // parts[0] = "dump", [1] = date, [2] = time, [3] = kind, [4] = subsystem, [5] = nodeId
        if (parts.Length >= 5)
            return parts[4];
        return "Unknown";
    }

    /// <summary>
    /// Extracts a node key (e.g. "node-400") from a RelativeDest path, or
    /// falls back to the transaction directory name.
    /// </summary>
    private static string ExtractNodeKeyFromPath(string relDest)
    {
        var fileName = Path.GetFileNameWithoutExtension(relDest);
        var parts = fileName.Split('_');
        // parts[5] = nodeId
        if (parts.Length >= 6 && int.TryParse(parts[5], out _))
            return "node-" + parts[5];

        // Fallback: use the immediate parent directory name.
        var dir = Path.GetDirectoryName(relDest);
        return string.IsNullOrEmpty(dir) ? "unknown" : Path.GetFileName(dir);
    }

    /// <summary>
    /// Extracts the dump timestamp string from the manifest file names.
    /// Returns "yyyyMMdd_HHmmss" format, or a current UTC timestamp if not found.
    /// </summary>
    private string ExtractTimestampFromManifest()
    {
        foreach (var entry in _manifest)
        {
            var fn    = Path.GetFileNameWithoutExtension(entry.RelativeDest);
            var parts = fn.Split('_');
            // parts[0] = "dump", [1] = date (yyyyMMdd), [2] = time (HHmmss)
            if (parts.Length >= 3 && parts[0] == "dump"
                && parts[1].Length == 8 && parts[2].Length == 6)
            {
                return parts[1] + "_" + parts[2];
            }
        }
        return DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
    }

    private static List<FileManifestEntry> GetManifestForSubsystem(
        string subsystemName,
        List<FileManifestEntry> manifest)
    {
        return manifest
            .Where(e => ExtractSubsystemFromPath(e.RelativeDest) == subsystemName)
            .ToList();
    }

    private void OpenFileFromNas(FileManifestEntry entry)
    {
        string fullPath = string.IsNullOrEmpty(_nasBasePath)
            ? entry.RelativeDest
            : Path.Combine(_nasBasePath, entry.RelativeDest);

        if (File.Exists(fullPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName       = fullPath,
                UseShellExecute = true,
            });
            _inlineError = null;
        }
        else
        {
            _inlineError = "File not found: " + fullPath;
        }
    }
}
