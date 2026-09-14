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
    public const string ScenarioFileName = "scenario.json";

    /// <summary>The <c>$meta.docType</c> stamped on a saved scenario, regardless of which host wrote it.</summary>
    private const string ScenarioDocType = "Hrot.Scenario";

    private readonly ScenarioSerializer   _serializer;
    private readonly IZoneManagerService? _zoneService;
    private readonly ITkbDatabase?        _tkbDb;
    private readonly EntityRepository     _world;
    private readonly Func<string>         _scenariosRoot;
    private readonly int                  _nodeId;

    public HrotScenarioSaveHandler(
        ScenarioSerializer   serializer,
        IZoneManagerService? zoneService,
        ITkbDatabase?        tkbDb,
        EntityRepository     world,
        Func<string>         scenariosRoot,
        int                  nodeId)
    {
        _serializer    = serializer    ?? throw new ArgumentNullException(nameof(serializer));
        _zoneService   = zoneService;   // ⭐ optional — a host may compose none (ruling 49); zones are then skipped.
        _tkbDb         = tkbDb;
        _world         = world          ?? throw new ArgumentNullException(nameof(world));
        _scenariosRoot = scenariosRoot  ?? throw new ArgumentNullException(nameof(scenariosRoot));
        _nodeId        = nodeId;
    }

    /// <inheritdoc />
    public bool CanHandle(NodeOpType operation) => operation == NodeOpType.SerializeLocal;

    /// <inheritdoc />
    /// <remarks>
    /// Writes this node's owned slice to <c>&lt;scenariosRoot&gt;/&lt;name&gt;/scenario.json</c> via
    /// <see cref="ScenarioSaveCore"/>.
    /// <para>⭐ The scenarios root is the SHARED store (the editor's root already IS the NAS scenarios folder),
    /// so the file is written to its final location and NO manifest is reported — there is nothing to pull.
    /// ⚠ <b>Follow-on (multi-process distributed cluster):</b> when several remote processes each own a slice,
    /// the design's per-node staging + NAS pull + per-node file names apply (§4); this handler would then write
    /// to a local staging root and return a <see cref="FileManifestResult"/>. The single-authoritative-node
    /// case (editor / CGF brain owning all persistable, R-A) needs neither and is what runs today.</para>
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

        var dir = Path.Combine(_scenariosRoot(), payload.ScenarioName);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, ScenarioFileName);

        // The ONE host-neutral save implementation — gated ScenarioSerializer + this host's zones.
        var header = new ScenarioHeader(ScenarioDocType, TkbName: _tkbDb?.ActiveTkbName);
        ScenarioSaveCore.Write(_serializer, _world, file, header, _zoneService);

        FdpLog<HrotScenarioSaveHandler>.Info(
            "[HrotScenarioSaveHandler] node {0} wrote scenario slice '{1}'.", _nodeId, payload.ScenarioName);

        // No manifest: the file is already at its final shared location, so there is nothing to pull.
        return Task.FromResult<object?>(null);
    }

    /// <inheritdoc />
    public void Commit(ExecuteNodeOpIntent intent, EntityRepository? repo) { }

    /// <inheritdoc />
    public void Abort(ExecuteNodeOpIntent intent, EntityRepository? repo) { }
}
