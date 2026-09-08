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
/// <para><b>⭐ STAGE 2 — the physics bracket.</b> <c>AttachPhysics</c> builds the same collaborator
/// chain mode 1 builds *(visual binding → body lifecycle → motors → reverse-sync group → split sync)*
/// and drives <c>StridePhysicsBracket</c> around the node tick: pre-kernel step, then the
/// bootstrapper's <c>Kernel.Update()</c>, then the post-kernel step. Without it the node replicates
/// and ticks but never drives Bullet, so nothing moves.</para>
///
/// <para>⚠⚠ <b>KNOWN DUPLICATION, declared rather than hidden.</b> That collaborator chain is
/// currently built in TWO places — here and in <c>EditorStrideSubsystem.InitializeHosted</c>. Ruling 9
/// says one implementation per concept, so this is debt, not a design. ⛔ It is deliberate for one
/// night only: hoisting mode 1's construction into a shared composer means editing the path that
/// currently works, unattended. ⭐ The follow-up is to extract a <c>StrideMuscleBracketComposer</c>
/// both shells call, and it is filed rather than assumed.</para>
///
/// <para>⛔ <b>NOT built here:</b> the VIEW bracket (<c>StrideViewBracket</c> — animation, gizmos,
/// selection). A mode-2 node simulates without it; it is what a 3-D operator view would need.</para>
/// </summary>
public sealed class StrideNodeShell : IDisposable
{
    private static readonly NLog.Logger Log = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>Default node id for a Stride mode-2 node (mock design §9.2 reserved 700).</summary>
    public const int DefaultNodeId = 700;

    private readonly StrideNodeBootstrapper _bootstrapper = new();
    private DdsParticipant? _participant;
    private StrideVisualBindingSystem? _visualBinding;
    private StridePhysicsBracket? _physicsBracket;

    /// <summary>The physics body service this node wired. Valid after <see cref="AttachPhysics"/>.</summary>
    public IPhysicsBodyService? PhysicsBodyService { get; private set; }

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
            // ⛔⛔ CE-242 — THIS STRING IS A ROLE TOKEN, NOT A PROCESS NAME. It must be "SimHost".
            //
            // 📌 The defect it fixes, measured 2026-09-09 over a 597-second CGF+Stride run of
            //    hill-attack-close: every entity replicated to this node as a GHOST and NOTHING EVER
            //    MOVED (1001 pinned at (446,421) v=0.0 for the whole run), while the SimHost baseline
            //    killed 1007 at t=34 and 1006 at t=39.
            //
            // 📐 The chain, each hop measured:
            //    ClusterSlave.Tick publishes NodeHeartbeat{SubsystemName} at 1 Hz (ClusterSlave.cs:153)
            //      -> CGF reads it in NedCgfEntityLifecycleAdapters.PollNetwork and converts the STRING
            //         to a role via MapSubsystemNameToRole (NedNetworkFactory.cs:423), whose entire
            //         table is { "SimHost" => MuscleGround, "CGF" => Brain, "IG" => ImageGenerator,
            //         _ => NodeRole.None }
            //      -> BrainMuscleOwnershipStrategy asks GetLeastLoadedNode(MuscleGround)
            //         (BrainMuscleOwnershipStrategy.cs:43); with no node mapping to MuscleGround it
            //         returns null and GetInitialGrants returns an EMPTY grant list -- its documented
            //         "safe fallback: the Brain retains physics authority".
            //    ⇒ "Stride" mapped to NodeRole.None, so CGF kept dtWorldPos/dtNavigationStatus and this
            //      node was never granted anything to drive. The empty list is a silent fallback, which
            //      is why the run looked healthy and simply stood still.
            //
            // ⭐ Why "SimHost" is the CORRECT value and not a lie: DESIGN_Stride_Node_Modes.md defines
            //    mode 2 as "a networked node replacing SimHost" (§1 mode table) with a role
            //    "identical to SimHost" (§5.3 / §4.1b). This node IS the cluster's MuscleGround node --
            //    there is no other -- and MapSubsystemNameToRole's existence proves the field is
            //    consumed as a role. The same token also puts this node in the four other rosters that
            //    switch on it: ClusterMaster.cs:354 and OrchestratorSubsystem.cs:339 (the lockstep
            //    roster the master collects ACKs from), ReplaySeekProcessManager.cs:55, and the
            //    mandatory-subsystem readiness check. Naming it "Stride" excluded it from all five.
            //
            // ⚠ THE DEBT, so nobody reads this as an endorsement: one string is doing double duty as
            //    node IDENTITY and node ROLE, and it is switched on by five separate hard-coded lists.
            //    A Stride node therefore cannot coexist with a real SimHost on one domain -- they would
            //    also collide on HrotNodeBuilder.cs:195's SubsystemName+"Allocator" DDS name. That is
            //    acceptable here precisely because mode 2 REPLACES SimHost. The principled fix is
            //    docs/DESIGN_Role_Affinity_Ownership.md (READY-TO-BUILD, not yet built), which derives
            //    ownership locally from each node's own NodeRole and retires this grant path entirely.
            //    Diagnostics cost until then: dump filenames and log names say "SimHost" (see
            //    DiagnosticsDumpClusterOpHandler.cs:129). NodeId 700 still distinguishes this node.
            SubsystemName       = "SimHost",
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

    /// <summary>
    /// ⭐⭐ STAGE 2 — builds and attaches the physics bracket so this node actually drives Bullet.
    /// Call after <see cref="Boot"/>, once the Stride scene and its <c>PhysicsProcessor</c> exist.
    /// </summary>
    /// <returns><c>true</c> when a real Bullet service was wired; <c>false</c> means no-op physics.</returns>
    public bool AttachPhysics(Stride.Engine.Game game, Stride.Engine.Scene scene)
    {
        if (Context == null) throw new InvalidOperationException("AttachPhysics before Boot.");

        var visualFactory = new StrideVisualFactory(game, scene);
        _visualBinding    = new StrideVisualBindingSystem(visualFactory, Context.TkbDb);

        var physicsProcessor = game.SceneSystem.SceneInstance
            .GetProcessor<Stride.Physics.PhysicsProcessor>();

        IPhysicsBodyService service;
        bool physicsIsActive;
        if (physicsProcessor?.Simulation != null)
        {
            service = new BulletPhysicsBodyServiceDeferred(
                physicsProcessor.Simulation,
                () => _visualBinding?.Visuals
                      ?? new System.Collections.Generic.Dictionary<Entity, StrideVisualReference>());
            physicsIsActive = true;
            Log.Info("[StrideNodeShell] Bullet physics wired for the mode-2 node.");
        }
        else
        {
            service = new NoOpPhysicsBodyService();
            physicsIsActive = false;
            Log.Warn("[StrideNodeShell] No PhysicsProcessor at attach time — mode 2 will NOT move bodies.");
        }

        PhysicsBodyService = service;
        var lifecycle      = new PhysicsBodyLifecycleSystem(service, _visualBinding);
        var characterMotor = new BulletCharacterMotor(service, lifecycle);
        var vehicleMotor   = new KinematicVehicleMotor(service, lifecycle);
        var reverseSync    = new Fdp.ModuleHost.Scheduling.TogglablePostSimulationGroup(
            "BulletReverseSync", new BulletReverseSyncSystem(service, lifecycle));
        var splitSync      = new SplitAuthorityStrideSyncScript(_visualBinding, visualFactory);

        _physicsBracket = new StridePhysicsBracket(
            physicsIsActive:      physicsIsActive,
            physicsBodyLifecycle: lifecycle,
            characterMotor:       characterMotor,
            vehicleMotor:         vehicleMotor,
            reverseSyncGroup:     reverseSync,
            splitSync:            splitSync,
            physicsBodyService:   service);

        return physicsIsActive;
    }

    /// <summary>
    /// ⭐⭐ One node frame: physics pre-step → node tick (<c>Kernel.Update()</c>) → physics post-step.
    /// </summary>
    /// <remarks>
    /// ⭐ The ORDER mirrors mode 1's documented frame exactly *(bracket pre, kernel, bracket post)*, which
    /// is what makes the muscle tier read already-reverse-synced state in the same frame.
    /// ⚠ <paramref name="wallDt"/> is the render delta. It reaches the bracket and the gizmo buffer only —
    /// the KERNEL is advanced parameterless because this node is a time slave (Q66 §3A).
    /// </remarks>
    public void Tick(float wallDt)
    {
        var world = Context?.World;
        if (world == null) return;

        _physicsBracket?.RunPreKernelStep(world, wallDt, simRunning: true);
        _bootstrapper.Tick(wallDt);
        _physicsBracket?.RunPostKernelStep(world);
    }

    public void Dispose()
    {
        try { _bootstrapper.Dispose(); } catch { /* teardown best-effort */ }
        _participant = null;
    }
}
