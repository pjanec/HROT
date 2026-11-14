using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Interfaces;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Fdp.Toolkit.Scenario;
using Hrot.Map.Common.Scenario;
using Hrot.Map.Common.Services;

namespace Hrot.ScenarioEditor.Handlers;

/// <summary>
/// ⭐⭐⭐ <b>CE-275 ③ — the ONE declarative scenario save handler, registered by EVERY host.</b>
///
/// <para>Responds to <see cref="NodeOpType.SerializeLocal"/> whose <c>DomainPayload</c> is a
/// <see cref="ScenarioSaveHandlerPayload"/> (so it coexists with <c>ReferenceArchiveHandler</c>, which acts
/// only on an <c>ArchiveHandlerPayload</c> for the <c>.fdp</c> checkpoint recording). It writes this node's
/// slice of the scenario through the ONE host-neutral save implementation, <see cref="ScenarioSaveCore"/> —
/// the gated <c>ScenarioSerializer</c> (CE-275 ②) plus this host's zones. There is NO editor-specific save
/// path: the editor's thin <c>ScenarioFileService</c> shim calls the same <see cref="ScenarioSaveCore"/>.</para>
///
/// <para>⭐ <b>Unification, not an editor special case.</b> The editor, CGF, SimHost and IG all register this
/// same handler with their own serializer / zone service / world. IG usually owns nothing savable (its
/// authored entities carry <c>ScenarioIgnoreTag</c>), so its file is empty BY THE GATE — but if IG owns a
/// persistable entity it saves it, exactly like any other host. Passivity is emergent from ownership, not a
/// missing handler.</para>
///
/// <para>⛔ The operator never chooses a filesystem path: <see cref="ScenarioSaveHandlerPayload.ScenarioName"/>
/// is a relative name / subfolder under the NAS scenarios root, resolved here against <see cref="_scenariosRoot"/>.</para>
///
/// 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4 · §6a (zones).
/// </summary>
public sealed class HrotScenarioSaveHandler : IClusterStateHandler
{
    /// <summary>The canonical file name every scenario directory stores its world under (matches the load path).</summary>
    public const string ScenarioFileName = ContentLayout.ScenarioFileName;

    /// <summary>The <c>$meta.docType</c> stamped on a saved scenario, regardless of which host wrote it.</summary>
    private const string ScenarioDocType = Hrot.Common.Scenario.HrotSubsystemTypes.Scenario;

    private readonly ScenarioSerializer   _serializer;
    private readonly EntityRepository     _world;
    private readonly NodeId                  _nodeId;
    private readonly Fdp.Toolkit.Behavior.ScenarioBehaviorRemapper? _behaviorRemapper;

    public HrotScenarioSaveHandler(
        ScenarioSerializer   serializer,
        EntityRepository     world,
        int                  nodeId,
        Fdp.Toolkit.Behavior.ScenarioBehaviorRemapper? behaviorRemapper = null)
    {
        // ⭐ CE-4210 W-M6 — the SAME remapper (so the same behaviour registry) the node loads scenarios with: Save selection as macro rebinds a reference inside a params blob through it.
        _behaviorRemapper = behaviorRemapper;
        _serializer    = serializer    ?? throw new ArgumentNullException(nameof(serializer));
        _world         = world          ?? throw new ArgumentNullException(nameof(world));
        _nodeId        = (Fdp.Core.NodeId)(nodeId);
    }

    /// <inheritdoc />
    public bool CanHandle(NodeOpType operation) => operation == NodeOpType.SerializeLocal;

    /// <inheritdoc />
    /// <remarks>
    /// ⭐ CE-279 Layer C — payload-aware so this scenario handler is not shadowed by the co-registered
    /// <c>ReferenceArchiveHandler</c> (both claim <c>SerializeLocal</c>; the slave picks the FIRST
    /// <c>CanHandle</c>-true handler). Claims ONLY a <see cref="ScenarioSaveHandlerPayload"/>.
    /// </remarks>
    public bool CanHandle(ExecuteNodeOpIntent intent)
        => intent.Operation == NodeOpType.SerializeLocal && intent.DomainPayload is ScenarioSaveHandlerPayload;

    /// <inheritdoc />
    /// <remarks>
    /// CE-277(c1) — writes this node's owned slice to its OWN per-node staging root
    /// (<see cref="OrchestrationConstants.GetNodeScenariosRoot(int)"/> → <c>nodes/node-N/scenarios/&lt;name&gt;/scenario.json</c>)
    /// via the shared <see cref="ScenarioSaveCore"/>, and returns a <see cref="FileManifestResult"/> so the
    /// orchestrator PULLS it to the NAS staging slice <c>scenarios/&lt;name&gt;/.slices/node_N.json</c>. The
    /// per-node <see cref="ScenarioMergeCore"/> then combines the format-compatible slices into the one canonical
    /// <c>scenarios/&lt;name&gt;/scenario.json</c> (§4a/§4b). ⭐ EVERY host does this identically, editor included —
    /// on a single box the pull is a local copy and the merge of one slice is the identity no-op.
    /// </remarks>
    public Task<object?> PrepareAsync(ExecuteNodeOpIntent intent, CancellationToken ct)
    {
        if (intent.DomainPayload is not ScenarioSaveHandlerPayload payload)
            return Task.FromResult<object?>(null);
        if (string.IsNullOrWhiteSpace(payload.ScenarioName))
        {
            FdpLog<HrotScenarioSaveHandler>.Warn("[HrotScenarioSaveHandler] empty ScenarioName; nothing saved.");
            return Task.FromResult<object?>(null);
        }

        // ⭐⭐ CE-4099 (AQ108 §4c A2/A4, §4c.1) — an AUTOSAVE: capture on THIS (frame) thread, extract on a worker from a private sandbox, the slice
        //   beside the scenario's .autosave/ — the same round, the same manifest, a different destination.
        if (payload.Autosave) return AutosaveAsync(payload);

        // Write this node's slice to its OWN staging root (never the shared store directly).
        var dir = Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(_nodeId), payload.ScenarioName);
        Directory.CreateDirectory(dir);
        var localFile = Path.Combine(dir, ScenarioFileName);

        // The ONE host-neutral save implementation — gated ScenarioSerializer + this host's zones.
        // ⭐ CE-3015 — the terrain is stamped from what this node has RESIDENT. ⛔ R-4018: never a TKB name.
        // ⭐ CE-4210 MA-4 — terrain + description + the macro header fields, ALL from the world through the ONE helper.
        // ⭐ CE-4210 MA-9 — Save selection as macro: the payload's optional header (macro fields) and entity filter go through the SAME core; null / empty = the whole scenario from the resident, exactly as before.
        var header = ScenarioSaveCore.HeaderOf(_world, ScenarioDocType, payload.HeaderJson);
        ScenarioSaveCore.Write(_serializer, _world, localFile, header, payload.OnlyGuids, _behaviorRemapper);

        // The orchestrator pulls this to a per-node NAS slice; the merge combines slices → scenario.json.
        // ⭐ CE-4210 MA-4 — beside the CANONICAL file wherever the name resolves (the `macros/` arm too), so the merge finds the slices and cleans them up.
        var relativeDest = SliceRelativePath(payload.ScenarioName, _nodeId);

        FdpLog<HrotScenarioSaveHandler>.Info(
            "[HrotScenarioSaveHandler] node {0} wrote scenario slice '{1}' ({2}).",
            _nodeId, payload.ScenarioName, ScenarioDocType);

        return Task.FromResult<object?>(
            new[] { new FileManifestResult(localFile, relativeDest, ScenarioDocType) });
    }

    /// <summary>
    /// ⭐ CE-4099 — the autosave half: <see cref="ScenarioAutosaveCore.Take"/> here (the frame thread, ~20–30 ms), the JSON on a worker
    /// (<see cref="ScenarioAutosaveCore.Write"/>: a sandbox, the ONE save core), the slice at <see cref="AutosaveSliceRelativePath"/>. A hot reload during the
    /// extraction fails the node's part (the master only logs a failed autosave).
    /// </summary>
    private Task<object?> AutosaveAsync(ScenarioSaveHandlerPayload payload)
    {
        var registry = _behaviorRemapper?.Registry;
        var capture  = ScenarioAutosaveCore.Take(_world, ScenarioDocType, registry);
        var dir      = Path.Combine(OrchestrationConstants.GetNodeScenariosRoot(_nodeId), payload.ScenarioName, AutosaveFolder);
        var file     = Path.Combine(dir, ScenarioFileName);
        var relative = AutosaveSliceRelativePath(payload.ScenarioName, _nodeId);
        var serializer = _serializer;
        var remapper   = _behaviorRemapper;
        var nodeId     = _nodeId;
        return Task.Run<object?>(() =>
        {
            Directory.CreateDirectory(dir);
            ScenarioAutosaveCore.Write(serializer, capture, file, registry, remapper);
            FdpLog<HrotScenarioSaveHandler>.Info("[HrotScenarioSaveHandler] node {0} wrote the AUTOSAVE slice of '{1}'.", nodeId, payload.ScenarioName);
            return new[] { new FileManifestResult(file, relative, ScenarioDocType) };
        });
    }

    /// <summary>⭐ CE-4099 (AQ108 §4c A5) — the scenario's autosave folder: a dot-folder, so no scenario enumerator lists it and CE-4091's copy skips it.</summary>
    public const string AutosaveFolder = AutosaveFiles.Folder;

    /// <summary>⭐ CE-4099 — the NAS-relative path of this node's AUTOSAVE slice: <c>&lt;scenario folder&gt;/.autosave/.slices/node_N.json</c> (never the real
    /// save's <c>.slices</c>, so an autosave and a save never share staging).</summary>
    public static string AutosaveSliceRelativePath(string scenarioName, NodeId nodeId)
        => Path.Combine(ContentLayout.ScenarioRelativeFolder(OrchestrationConstants.GetSharedRoot(), scenarioName), AutosaveFolder, ".slices", $"node_{nodeId}.json");

    /// <summary>⭐ CE-4210 MA-4 — the NAS-relative path of this node's slice: <c>&lt;scenario folder&gt;/.slices/node_N.json</c>, the folder resolved by
    /// <see cref="ContentLayout.ResolveScenarioDirectory"/> (<c>scenarios/x</c>, or <c>macros/x</c>) — the SAME resolver the merge reads.</summary>
    public static string SliceRelativePath(string scenarioName, NodeId nodeId)
        => Path.Combine(ContentLayout.ScenarioRelativeFolder(OrchestrationConstants.GetSharedRoot(), scenarioName), ".slices", $"node_{nodeId}.json");

    /// <inheritdoc />
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

    /// <inheritdoc />
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
}
