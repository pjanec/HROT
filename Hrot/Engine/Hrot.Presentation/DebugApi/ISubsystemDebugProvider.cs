using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Hrot.UI.Common.Facades;

namespace Hrot.Presentation.DebugApi;

/// <summary>
/// ⭐⭐⭐ <b>ONE SUBSYSTEM'S DEBUG SURFACE — what it can be READ from and DRIVEN through.</b>
/// 📄 <c>docs/blueprints/Architect_Question_54_Cluster_Mcp_Contract.md</c> *(RESOLVED)* — Q54-2 Option B:
/// perspective-scoped dispatch over per-subsystem providers.
///
/// <para>⛔⛔ <b>Why per-subsystem and NOT one <c>ClusterReadDriveService</c>.</b> 📌 Q54: editor-only
/// features do not stay editor-only — they MIGRATE into the subsystems *(charter <c>D3</c>)*, and a single
/// frozen cluster service would have to be re-split every time one lands. ⇒ ⭐ each subsystem contributes
/// its own read+drive surface as its features arrive.</para>
///
/// <para>⭐⭐ <b>Almost everything here already existed.</b> The role-correct *"how do I step"* is each
/// slave's own <see cref="ITimeTransportFacade"/> — <c>ClusterTimeTransportAdapter</c> on a slave
/// *(publishes <c>StepTimeIntent</c> → DDS → the master)*, the direct facade in the editor. ⇒ ⛔ this
/// interface introduces no new stepping mechanism; it selects the existing one **by active
/// perspective**.</para>
///
/// <para>⚠ <b>Nullable members are the point, not sloppiness</b> *(charter <c>D3</c>: the lifted API accepts
/// absent capabilities)*. A subsystem with no ECS world returns <see langword="null"/> for
/// <see cref="World"/>; one that cannot drive time returns <see langword="null"/> for <see cref="Drive"/>.
/// ⛔ The dispatcher then answers <c>NOT_SUPPORTED_HERE</c> — it does NOT fabricate an empty world, which
/// would be the false green <c>D4</c> exists to kill.</para>
/// </summary>
public interface ISubsystemDebugProvider
{
    /// <summary>⭐ The subsystem's own name — <c>ISubsystem.Name</c>, e.g. <c>"CGF"</c>. For diagnostics and the manifest.</summary>
    string SubsystemName { get; }

    /// <summary>
    /// ⭐⭐ <b>The PERSPECTIVE this provider answers for</b> — the finer key
    /// *(<c>DESIGN_Perspective_Unification.md</c> §1b)*.
    /// <para>⚠ It is NOT always the subsystem name: 📐 CGF's perspective is <c>"Scenario"</c>, the one entry
    /// in <c>perspectiveMap</c> whose key and value differ.</para>
    /// </summary>
    string Perspective { get; }

    /// <summary>⭐ The subsystem's authoritative entity repository, or <see langword="null"/> when it has none.</summary>
    EntityRepository? World { get; }

    /// <summary>⭐ Its network-id → entity map, or <see langword="null"/>.</summary>
    NetworkEntityMap? EntityMap { get; }

    /// <summary>
    /// ⭐⭐⭐ <b>The role-correct drive seam.</b> On a slave this is the subsystem's own
    /// <c>ClusterTimeTransportAdapter</c>, so a step issued here travels the SAME path the operator's
    /// button does: <c>StepTimeIntent</c> → its own event bus → DDS → the master.
    /// <para>⛔ <see langword="null"/> when this subsystem cannot drive time.</para>
    /// </summary>
    ITimeTransportFacade? Drive { get; }

    /// <summary>
    /// ⭐⭐ <b>What this subsystem CAN do, measured from wired dependencies — never a hand-authored table.</b>
    /// 📄 Q54 § Manifest scope: <i>"each provider DERIVES its own cells from ground truth"</i>; a
    /// hand-written *"works here / not there"* table is `CLAUDE.md` §M's green-and-false rot.
    /// </summary>
    IReadOnlyDictionary<string, bool> DescribeCapabilities();
}

/// <summary>
/// ⭐⭐ <b>A subsystem that can contribute a debug provider.</b> ⭐ Separate from
/// <see cref="ISubsystemDebugProvider"/> so a subsystem exposes its surface without BEING one — the
/// provider is built after <c>Initialize</c>, when the world and the adapter exist.
/// <para>⚠ Returning <see langword="null"/> is legal and means *"nothing to contribute in this
/// configuration"* — 📌 a subsystem may run in a mode where it has no world at all.</para>
/// </summary>
public interface IProvidesDebugSurface
{
    ISubsystemDebugProvider? CreateDebugProvider();
}

/// <summary>
/// ⭐⭐⭐ <b>The plain implementation every subsystem can hand back</b> — a record of what it wired.
/// ⛔ Deliberately dumb: it holds no logic, so a provider cannot lie about a capability it merely intends
/// to have. 📌 The capability cells are computed from the members being non-null, in ONE place.
/// </summary>
public sealed class SubsystemDebugProvider : ISubsystemDebugProvider
{
    private readonly Func<EntityRepository?>? _world;
    private readonly Func<NetworkEntityMap?>? _entityMap;
    private readonly Func<ITimeTransportFacade?>? _drive;

    /// <summary>
    /// ⭐⭐⭐ <b>THE ACCESSORS ARE LAZY, AND THAT IS MEASURED — NOT DEFENSIVE STYLE.</b>
    ///
    /// <para>📐 Measured `2026-08-24`: a first cut captured the dependencies BY VALUE at provider
    /// construction, and <c>GET /capabilities</c> reported <c>time.drive:false</c> for <b>SimHost and
    /// CGF</b> — the two subsystems that definitely have a drive adapter. 🔴 The reason:
    /// <c>_clusterTimeAdapter</c> is created in <c>RegisterWindows</c>, which runs when the window opens,
    /// i.e. AFTER the composition root builds the providers.</para>
    ///
    /// <para>⇒ ⭐⭐ a value-captured provider would have reported a capability ABSENT that the subsystem
    /// gains seconds later — ⛔ the manifest lying in the safe-looking direction, which is worse than
    /// lying loudly. ⭐ With accessors, <see cref="DescribeCapabilities"/> measures at READ time, so the
    /// matrix is live.</para>
    /// </summary>
    public SubsystemDebugProvider(
        string subsystemName,
        string perspective,
        Func<EntityRepository?>? world = null,
        Func<NetworkEntityMap?>? entityMap = null,
        Func<ITimeTransportFacade?>? drive = null)
    {
        SubsystemName = subsystemName ?? throw new ArgumentNullException(nameof(subsystemName));
        Perspective   = perspective   ?? throw new ArgumentNullException(nameof(perspective));
        _world        = world;
        _entityMap    = entityMap;
        _drive        = drive;
    }

    public string SubsystemName { get; }
    public string Perspective { get; }
    public EntityRepository? World => _world?.Invoke();
    public NetworkEntityMap? EntityMap => _entityMap?.Invoke();
    public ITimeTransportFacade? Drive => _drive?.Invoke();

    /// <summary>
    /// ⭐⭐⭐ <b>MEASURED from what is wired</b> — ⛔ never declared. 📌 Q54's one real risk: a hand-authored
    /// matrix stays green while the code drifts.
    /// </summary>
    public IReadOnlyDictionary<string, bool> DescribeCapabilities() => new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        [DebugCapabilities.WorldRead]   = World is not null,
        [DebugCapabilities.EntityMap]   = EntityMap is not null,
        [DebugCapabilities.TimeDrive]   = Drive is not null,
        // ⭐ Panels and the gizmo frame are PROCESS-WIDE statics (PanelSnapshot / the primitive buffer), so
        //   they are not a per-provider capability — the dispatcher reports them once. ⛔ Claiming them here
        //   per subsystem would suggest a routing that does not exist.
    };
}

/// <summary>⭐ The capability keys, in one place so the manifest and the rails cannot spell them differently.</summary>
public static class DebugCapabilities
{
    public const string WorldRead = "world.read";
    public const string EntityMap = "world.entityMap";
    public const string TimeDrive = "time.drive";
    public const string Panels    = "panels.read";
    public const string GizmoFrame = "panels.gizmo";
    public const string Preview   = "preview.control";
    public const string EditorAuthoring = "editor.authoring";
}
