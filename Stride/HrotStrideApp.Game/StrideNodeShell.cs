#nullable enable
using System;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tracking;   // SenderIdentityConfig
using Fdp.Core;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Replication.Services;   // NetworkEntityMap
using Hrot.Common;
using Hrot.Map.Common;                    // HrotEnvironment
using Hrot.Network.NED.Factory;           // NedNetworkFactory
using Hrot.Common.Infrastructure;
using Hrot.NodeComposition;
using Hrot.Stride.Core;

namespace HrotStrideApp;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-207</c> / slice <c>S4</c> — MODE 2's composition root: a Stride process that joins an
/// existing cluster as a Muscle + Perception node, beside CGF, in place of SimHost.</b>
///
/// <para>📄 Owning design: <c>DESIGN_Stride_Node_Modes.md</c> §4, §13 slice <c>S4</c> ·
/// <c>Architect_Question_66_Stride_Mode2_Node.md</c> §3A–§3D.</para>
///
/// <para><b>⭐ Why this class is small: almost everything it needs already existed and was dormant.</b>
/// <c>StrideNodeBootstrapper</c> implements all seven <c>SharedApplicationBootstrapper</c> hooks and had
/// <b>zero callers</b>; <c>StrideHrotGame.AttachBootstrapper</c> existed with zero callers;
/// <c>StrideCapabilities</c> already declares the muscle/perception plan mode 1 resolves. What was
/// missing was only the shell that puts them together — which is why <c>Q66</c> called this composition
/// work rather than new machinery.</para>
///
/// <para><b>⭐ §3A — the tick contract.</b> Mode 2 is a TIME SLAVE. <c>StrideNodeBootstrapper.Tick</c>
/// calls <c>Kernel.Update()</c> <b>parameterless</b> — the master supplies time — and uses the frame
/// delta only for the gizmo producer buffer. ⛔ There is deliberately no local
/// <c>TimeController.Step(dt)</c>: that is mode 1's step <c>A</c>, correct only because the editor IS
/// the time authority. The <c>Update(float)</c> overload's own attribute says it "will cause
/// deterministic desync".</para>
///
/// <para><b>⭐ §3C — identity.</b> Node id defaults to <b>700</b>, still free per the retired mock
/// design's §9.2 allocation, and the participant enables sender tracking with it so the cluster can
/// attribute this node's samples.</para>
///
/// <para>⚠⚠ <b>STAGE 1 SCOPE, stated so nothing is claimed that is not built.</b> This shell brings the
/// node up and joins it to the cluster with the muscle + perception capability set. It does <b>not</b>
/// yet construct the physics bracket (<c>§3B</c>'s <c>StridePhysicsBracket</c>) or the view bracket
/// (<c>StrideViewBracket</c>), both of which mode 1 builds inside <c>EditorStrideSubsystem</c>. Until
/// those are hoisted into a shared composer, a mode-2 node replicates and ticks but does not drive
/// Bullet — so vehicles will not move. That is the next stage, and it is the one that decides whether
/// <c>hill-attack-close</c> behaves as it does on SimHost.</para>
/// </summary>
public sealed class StrideNodeShell : IDisposable
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>Default node id for a Stride mode-2 node (mock design §9.2 reserved 700).</summary>
    public const int DefaultNodeId = 700;

    private readonly StrideNodeBootstrapper _bootstrapper = new();
    private DdsParticipant? _participant;

    /// <summary>The bootstrapped node context. Valid after <see cref="Boot"/>.</summary>
    public HrotNodeContext? Context { get; private set; }

    /// <summary>The bootstrapper, so the game can attach it to its frame loop.</summary>
    public StrideNodeBootstrapper Bootstrapper => _bootstrapper;

    /// <summary>The muscle set this node composed, for the physics bracket to borrow (stage 2).</summary>
    public StrideMuscleModuleSet? MuscleSet { get; private set; }

    /// <summary>
    /// Boots the node: resolves the Stride capability plan, creates an isolated DDS participant, and
    /// runs the seven-phase bootstrap.
    /// </summary>
    /// <param name="domainId">DDS domain to join — must match the rest of the cluster.</param>
    /// <param name="nodeId">This node's cluster id (default <see cref="DefaultNodeId"/>).</param>
    public HrotNodeContext Boot(int domainId, int nodeId = DefaultNodeId)
    {
        // ⭐ The SAME declaration mode 1 resolves — StrideCapabilities is the one plan, and resolving
        //   DefaultRole (MuscleGround | Perception) is what CE-233 established as correct. In mode 2
        //   there is no editor to supply perception, so resolving the full role matters even more here.
        var crowd = new DotRecastDtCrowdProvider(maxAgentRadius: 0.4f);
        MuscleSet = StrideMuscleModules.Build(crowd);
        var capabilities = StrideCapabilities.Build(MuscleSet).Resolve(StrideCapabilities.DefaultRole);
        if (capabilities.Count == 0)
            throw new InvalidOperationException(
                "StrideNodeShell: StrideCapabilities resolved to an EMPTY set — the node would compose no " +
                "muscle tier and no perception. StrideNodeBootstrapper refuses this at boot by design.");

        // ⭐ Mirrors Hrot.ClusterRunner's per-subsystem isolation exactly (Program.cs steps 1-4):
        //   isolated entity map / geo transform / event bus, a participant with sender tracking, and a
        //   NED factory carrying this node's id.
        var entityMap    = new NetworkEntityMap();
        var geoTransform = HrotEnvironment.CreateGeoTransform();
        var eventBus     = new FdpEventBus();

        _participant = HrotEnvironment.CreateParticipant(domainId);
        _participant?.EnableSenderTracking(new SenderIdentityConfig
        {
            AppDomainId   = domainId,
            AppInstanceId = nodeId,
        });

        var networkFactory = new NedNetworkFactory(
            _participant, entityMap, geoTransform, eventBus, nodeId, NodeRole.None);

        var config = new HrotNodeConfig
        {
            DomainId            = domainId,
            NodeId              = nodeId,
            SubsystemName       = "Stride",
            Headless            = false,
            ExternalParticipant = _participant,
        };

        Log.Info("[StrideNodeShell] CE-207 mode 2: booting Stride node id={0} on DDS domain {1} with {2} " +
                 "capabilities [{3}].",
                 nodeId, domainId, capabilities.Count,
                 string.Join(", ", System.Linq.Enumerable.Select(capabilities, c => c.Key)));

        Context = _bootstrapper
            .WithCapabilities(capabilities)
            .BootstrapNode(config, StrideCapabilities.DefaultRole, networkFactory);

        Log.Info("[StrideNodeShell] Node booted. World={0} Kernel={1} ClusterSlave={2}",
                 Context.World != null, Context.Kernel != null, Context.ClusterSlave != null);

        return Context;
    }

    public void Dispose()
    {
        try { _bootstrapper.Dispose(); } catch { /* teardown best-effort */ }
        _participant = null;
    }
}
