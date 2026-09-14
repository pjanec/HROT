using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Orchestration.Handlers;
using Hrot.ScenarioEditor.Services;

namespace Hrot.ScenarioEditor.Handlers;

/// <summary>
/// ⭐⭐⭐ <b>CE-275 ③ — the ONE declarative scenario save handler, registered by EVERY host.</b>
///
/// <para>Responds to <see cref="NodeOpType.SerializeLocal"/> whose <c>DomainPayload</c> is a
/// <see cref="ScenarioSaveHandlerPayload"/> (so it coexists with <c>ReferenceArchiveHandler</c>, which acts
/// only on an <c>ArchiveHandlerPayload</c> for the <c>.fdp</c> checkpoint recording). It writes this node's
/// slice of the scenario by delegating to the ONE save implementation — <see cref="ScenarioFileService.SaveScenario"/>
/// — over this node's world. That call runs the gated <c>ScenarioSerializer</c> (CE-275 ②:
/// <c>save entity ⇔ IsPrimaryOwner(entity) AND NOT ScenarioIgnoreTag</c>) plus this host's zones, so each host
/// writes exactly what it owns and nothing is duplicated with the editor's own save path.</para>
///
/// <para>⭐ <b>Unification, not an editor special case.</b> The editor, CGF, SimHost and IG all register this
/// same handler with their own <see cref="ScenarioFileService"/> and world. IG usually owns nothing savable
/// (its authored entities carry <c>ScenarioIgnoreTag</c>), so its file is empty BY THE GATE — but if IG owns a
/// persistable entity it saves it, exactly like any other host. There is no "IG registers no save handler"
/// rule any more; passivity is emergent from ownership.</para>
///
/// <para>⛔ The operator never chooses a filesystem path: <see cref="ScenarioSaveHandlerPayload.ScenarioName"/>
/// is a relative name / subfolder under the NAS scenarios root, resolved here against
/// <see cref="_scenariosRoot"/>.</para>
///
/// 📄 docs/DESIGN_Distributed_Scenario_Persistence.md §4 · §6a (zones).
/// </summary>
public sealed class HrotScenarioSaveHandler : IClusterStateHandler
{
    /// <summary>The canonical file name every scenario directory stores its world under (matches the load path).</summary>
    public const string ScenarioFileName = "scenario.json";

    private readonly ScenarioFileService _fileService;
    private readonly EntityRepository    _world;
    private readonly Func<string>        _scenariosRoot;
    private readonly int                 _nodeId;

    public HrotScenarioSaveHandler(
        ScenarioFileService fileService,
        EntityRepository    world,
        Func<string>        scenariosRoot,
        int                 nodeId)
    {
        _fileService   = fileService   ?? throw new ArgumentNullException(nameof(fileService));
        _world         = world         ?? throw new ArgumentNullException(nameof(world));
        _scenariosRoot = scenariosRoot ?? throw new ArgumentNullException(nameof(scenariosRoot));
        _nodeId        = nodeId;
    }

    /// <inheritdoc />
    public bool CanHandle(NodeOpType operation) => operation == NodeOpType.SerializeLocal;

    /// <inheritdoc />
    /// <remarks>
    /// Writes this node's owned slice to <c>&lt;scenariosRoot&gt;/&lt;name&gt;/scenario.json</c>.
    /// <para>⭐ The scenarios root is the SHARED store (the editor's root already IS the NAS scenarios
    /// folder), so the file is written to its final location and NO manifest is reported — there is nothing
    /// to pull. ⚠ <b>Follow-on (multi-process distributed cluster):</b> when several remote processes each
    /// own a slice, the design's per-node staging + NAS pull + per-node file names apply
    /// (docs/DESIGN_Distributed_Scenario_Persistence.md §4); this handler would then write to a local staging
    /// root and return a <see cref="FileManifestResult"/> for the pull. The single-authoritative-node case
    /// (editor / CGF brain owning all persistable, R-A) needs neither and is what runs today.</para>
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

        // The ONE save implementation — gated ScenarioSerializer + this host's zones. Reused verbatim by the
        // editor's own file service, so there is a single scenario-save code path across every host.
        _fileService.SaveScenario(_world, file);

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
