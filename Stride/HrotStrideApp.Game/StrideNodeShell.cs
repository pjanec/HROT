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
/// <para>⚠⚠ <b>KNOWN DUPLICATION — <c>CE-252</c>, and it is THREE places, not two.</b> This
/// collaborator chain is built at <c>EditorStrideSubsystem.cs:844</c> (mode 1, self-contained arm),
/// <c>EditorStrideSubsystem.cs:1170</c> (mode 1, hosted arm — the same plus
/// <c>StrideVisualBindingSystem</c>) and here (the same plus <c>StrideVisualFactory</c> and
/// <c>BulletPhysicsBodyServiceDeferred</c>, which no other site builds). ⛔ Each arm is a slightly
/// larger SUPERSET of the last — the shape that rots worst, because a fix applied to one silently
/// misses two.</para>
///
/// <para>⭐ Ruling 9 classifies this as duplicate CODE ⇒ <b>route it</b>, not duplicate surface and not
/// dead code, so the extraction is the ruling rather than a judgement call. The fix is a
/// <c>StrideMuscleBracketComposer</c> all three shells call. ⚠ <b>Sequence it with <c>S9</c>/<c>CE-209</c></b>,
/// which DELETES the self-contained arm: before <c>S9</c> it collapses three sites into one and then
/// deletes one caller; after <c>S9</c> it is the same work on a smaller surface. <c>S9</c>'s gate
/// ("after S2+S4 green") is now open.</para>
///
/// <para>⚠⚠ <b>An earlier version of this note said "two places" and claimed the follow-up "is filed
/// rather than assumed". BOTH WERE FALSE</b> — there was no tracker row until the user asked
/// (<c>2026-09-09</c>), and there are three sites. 📌 Recorded rather than quietly corrected because a
/// comment that ASSERTS its own follow-up exists is worse than one that admits debt: it stops the next
/// reader from checking. The <c>BP-355</c> shape — named in a note, never turned into a row.</para>
///
/// <para>⛔ <b>NOT built here:</b> the VIEW bracket (<c>StrideViewBracket</c> — animation, gizmos,
/// selection). A mode-2 node simulates without it; it is what a 3-D operator view would need.</para>
/// </summary>
public sealed class StrideNodeShell : IDisposable, Hrot.Presentation.DebugApi.IProvidesDebugSurface
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
    /// ⭐ <c>CE-248</c> — the deferred DotRecast crowd provider this node handed to its muscle set, so
    /// the game can seed it with the baked Infantry navmesh once the scene exists. It starts in no-op
    /// mode (<c>RegisterAgent</c>/<c>Update</c> return silently) and only becomes real after
    /// <c>TryInitializeNavMesh</c>, which is why it must be reachable from the bake.
    /// ⚠ Mode 1 exposes the same thing as <c>EditorStrideSubsystem.InfantryCrowdProvider</c>.
    /// </summary>
    public Hrot.Stride.Core.DotRecastDtCrowdProvider? InfantryCrowdProvider { get; private set; }

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
        InfantryCrowdProvider = crowd;   // CE-248 — the game seeds it after the navmesh bake.
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

        // ⛔⛔⛔ CE-247 — WITHOUT THIS THE NODE RENDERS AND SIMULATES NOTHING, SILENTLY.
        //
        // 📌 Measured 2026-09-09 through the node's own debug API (CE-245), which is what finally made
        //    this visible. Every earlier stage was working: ownership granted (DescriptorOwnership.Map
        //    = {dtWorldPos: 700, dtNavStatus: 700}), ghosts promoted to their full TKB shape, the nav
        //    intent arriving, VehicleNavIntentSystem wired (CE-246) -- and 1001 still sat at
        //    (446,421) with v=0.0, with ZERO Bullet bodies ever created ("LC-CREATE" count 0).
        //
        // 📐 The chain, each hop measured:
        //    PhysicsBodyLifecycleSystem skips any entity whose visual does not exist yet
        //      ("visual not yet created -- skip; retry next frame", :212)
        //      -> StrideVisualBindingSystem.TryCreateVisual returns null when the TKB template has no
        //         StrideRenderModelDefDto -- "No Stride visual definition for this class -- skip
        //         SILENTLY", by design and with no log
        //      -> GET /tkb/types/100 on this node: M1 Abrams carries TkbMasterDto,
        //         VisualDefinitionDto and VehicleParametersDto, and NO StrideRenderModelDefDto.
        //    ⇒ no visual, so no body, so no motion -- and not one line of output anywhere saying so.
        //
        // ⭐ The descriptor is not in the scenario TKB and never was: in production
        //    StrideRenderModelDefDto is constructed in exactly TWO places -- UrbanCombatTkbCatalog
        //    (the demo catalog, five hard-coded templates) and this class. Everything else that
        //    builds one is a test. HrotEnvironment.CreateTkb()/NedTkbCatalog seeds the platform
        //    catalog with no Stride-specific descriptors, because the Stride models are shipped by
        //    the Stride app rather than by the engine.
        //
        // ⭐⭐ So this is the SAME call mode 1 makes -- EditorStrideSubsystem.cs:1119, whose own
        //    summary says it exists "so StrideVisualBindingSystem and VehicleKinematicsTkbTranslator
        //    resolve the SAME templates the scenario spawns from". Mode 2 composes its TkbDb through
        //    HrotNodeBuilder instead of through the editor, which is how it missed the one line.
        //    ⛔ Not a new mechanism and not a second copy: the augmentation, its dimensions and its
        //    idempotence guard all stay in StrideNedRenderDescriptors.
        //
        // ⚠ Once at boot is CORRECT HERE, and only here: this node's catalog is built once by
        //    HrotEnvironment.CreateTkb() (HrotNodeBuilder.cs:236) and never reloaded, because
        //    TkbLoadClusterStateHandler -- the SimHost handler that CLEARS and re-ingests the catalog
        //    on every PrepareLive/PrepareEdit -- is not registered on the mode-2 node. If that handler
        //    is ever added here, this call must move to after each ingest; Apply is idempotent
        //    (HasDescriptor guard per template) so re-calling it is free.
        StrideNedRenderDescriptors.Apply(Context.TkbDb);

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
            physicsBodyService:   service)
        {
            // ⛔⛔ CE-246 — WITHOUT THIS THE NODE OWNS THE TANKS AND NEVER DRIVES THEM.
            //
            // 📌 Measured 2026-09-09, CGF + Stride mode 2, hill-attack-close: the node took ownership
            //    of all 8 entities, promoted every ghost to its full TKB shape (VehicleState,
            //    VehicleParams, NavState, NavigationStatus present), and received a fresh
            //    NavigationIntent {Mode=DirectPoint, FinalDestination=[523,401,0], TargetSpeed=15}
            //    every few seconds -- and 1001 never left (446,421) with v=0.0. The bracket's own
            //    telemetry named the hole in plain sight: "VehicleNavIntent=0.0" on every
            //    [Bracket breakdown] line, because the property was NULL and `?.Execute` was a no-op.
            //
            // 📐 VehicleNavigationIntentSystem is the ONLY thing that turns an ingressed
            //    NavigationIntent into the steering/throttle the vehicle motor consumes. With it
            //    absent, every upstream stage works and the last one silently does nothing -- which is
            //    exactly why the four defects before this one each looked like "navigation is broken".
            //
            // ⭐ The template is mode 1, EditorStrideSubsystem.cs:857 and :1183, which set this same
            //    property from the same muscle set, in an object initializer, for the same reason. This
            //    shell already built the set (MuscleSet, above) to compose its capabilities; it simply
            //    never lent the bracket the one system that is NOT kernel-resident. It is a
            //    constructor-adjacent property rather than a parameter precisely so it can be omitted
            //    -- which made omitting it silent. A production caller that HAS the dependency must
            //    PASS it.
            VehicleNavIntentSystem = MuscleSet?.VehicleNavIntent,
        };

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
    private Hrot.Editor.DebugApi.MainThreadJobQueue? _debugApiQueue;
    private Hrot.Editor.DebugApi.DebugApiHost?       _debugApiHost;

    /// <summary>
    /// ⭐⭐⭐ <c>CE-251</c> — the node's LAST OBSERVED SIM DELTA, for <c>StrideHrotGame</c> to turn into
    /// <c>GameTime.Factor</c>. Mode 1 publishes the identical value as
    /// <c>EditorStrideSubsystem.CurrentSimDeltaSeconds</c> (<c>:1245</c>); this is mode 2's copy of
    /// that one source of truth for "how far did the world move".
    /// </summary>
    /// <remarks>
    /// ⚠ It is the PREVIOUS frame's delta, deliberately and for exactly mode 1's documented reason
    /// (§11.1a, option B): Stride's <c>base.Update</c> — and therefore its physics step — runs BEFORE
    /// this shell ticks the kernel, so the current frame's delta does not exist yet.
    /// </remarks>
    public float CurrentSimDeltaSeconds { get; private set; }

    // ⭐ CE-241 — the rolling sim-delta window behind the diagnostic in Tick. See its note there.
    private const int DtWindow = 300;
    private readonly float[] _dtSamples = new float[DtWindow];
    private int _dtCount;

    public void Tick(float wallDt)
    {
        var world = Context?.World;
        if (world == null) return;

        // ⛔⛔⛔ CE-251 — MODE 2'S PHYSICS WAS RUNNING ON THE WALL CLOCK.
        //
        // 🔒 R-143, user verbatim: "no wall clock enywhere, whole sim driven by sim time ONLY … not in
        //    stirede, not in editor, not in cgf, not in simhost, never where simulation is related."
        //
        // 📐 The hole: StrideHrotGame.Update sets UpdateTime.Factor = simDelta / wallSeconds, and that
        //    assignment is guarded on `_editorSubsystem != null` — which is NEVER true in mode 2. So
        //    Factor kept its default of 1, WarpElapsed == Elapsed, and Stride's physics game system fed
        //    Bullet WALL seconds on the one host that is a cluster TIME SLAVE.
        //
        // ⛔ Two consequences, neither of which the green hill-attack run would have shown:
        //    ① non-determinism — the node's Bullet advance tracked local frame rate, not cluster time,
        //      so two Stride nodes on one cluster would diverge;
        //    ② CE-227's pause hole, REOPENED for mode 2 — a cluster-wide pause stops the kernel and the
        //      motors, but with Factor at 1 Stride keeps calling Simulate(wallDelta), so gravity and
        //      contacts would go on running in a paused world. That is exactly the defect CE-227 was
        //      raised for, and it never applied to mode 2 because mode 2 did not exist yet.
        //
        // ⭐ The fix is mode 1's, unchanged in shape: publish the KERNEL's own delta and let the game
        //    convert it. Reading Kernel.CurrentTime.DeltaTime is what EditorStrideSubsystem.cs:1238
        //    already does, and on this node it is the CLUSTER's time — the master supplies it, so a
        //    pause propagates as a zero delta with no pause flag of its own (Q66 §3A).
        //
        // ⚠ Published BEFORE the kernel tick on purpose: it is the previous frame's value, which is
        //    what the next base.Update needs, and it matches mode 1's documented option B exactly.
        CurrentSimDeltaSeconds = Context!.Kernel.CurrentTime.DeltaTime;

        // ⭐⭐ CE-241 — THE NUMBER THAT DECIDES THE FIX, logged once per 300 frames.
        //
        // 📌 Measured 2026-09-09 on this node, CGF + Stride, hill-attack-close running:
        //      min 0.0125 p50 0.0222 p90 0.0319 max 3.8023 s
        //      min 0.0047 p50 0.0415 p90 0.1194 max 4.5315 s
        //      min 0.0062 p50 0.0551 p90 0.0907 max 0.3393 s
        //      min 0.0059 p50 0.0592 p90 0.0875 max 0.3220 s
        //    ⇒ ⛔ NOT a stable step. The median MOVES between windows (0.022 -> 0.059) and single
        //    frames carry MULTI-SECOND deltas. Mode 1's own distribution (§13.6: p50 0.0220,
        //    max 0.2251) is tame by comparison, so a slave node is the WORSE case, not the easier one.
        //
        // ⛔⛔ This RULES OUT §11.1 item ③ as written ("FixedTimeStep/MaxSubSteps set from the sim step
        //    so a step integrates once") on a time-slave node, in both readings:
        //      FixedTimeStep = simDelta      ⇒ a single Bullet step of 4.5 s: tunnelling, exploding
        //                                      constraints, bodies through the slab;
        //      FixedTimeStep = 1/60, MaxSubSteps high ⇒ 270 sub-steps in one frame ⇒ the frame takes
        //                                      longer than the delta it is discharging ⇒ spiral of
        //                                      death (the same failure FIX-PERF-1 already documents
        //                                      for the mode-1 loop driver).
        //    ⇒ ⭐ ANY CE-241 fix must first BOUND the per-frame delta, and that is a cluster-time
        //    question (what should a slave do with a 4.5 s catch-up: clamp and drop, clamp and carry,
        //    or refuse to advance?), not a physics-tuning one. ⛔ Left for a decision rather than
        //    guessed at unattended.
        //
        // ⚠ AND THE MULTI-SECOND DELTAS ARE THEMSELVES A FINDING, separate from CE-241: a time SLAVE
        //    receiving a 4.5 s advance in one frame is a time-sync question. Not investigated here.
        //
        // ⭐ Kept as a permanent throttled diagnostic (once per 300 frames, INFO) because it is the
        //    number that decides the fix, and it cost a rebuild to get.
        if (CurrentSimDeltaSeconds > 0f)
        {
            _dtSamples[_dtCount % DtWindow] = CurrentSimDeltaSeconds;
            _dtCount++;
            if (_dtCount % DtWindow == 0)
            {
                var w = new float[DtWindow];
                System.Array.Copy(_dtSamples, w, DtWindow);
                System.Array.Sort(w);
                Log.Info("[StrideNodeShell] CE-241 simDelta over {0} frames — min={1:F4} p50={2:F4} " +
                         "p90={3:F4} max={4:F4} s  (1/60={5:F4})",
                         DtWindow, w[0], w[DtWindow / 2], w[(DtWindow * 9) / 10], w[DtWindow - 1],
                         1f / 60f);
            }
        }

        // ⭐ And the bracket's own gate comes from the SAME number rather than a hard-coded `true`, so
        //    the motors and Bullet cannot disagree about whether the world moved this frame.
        bool simRunning = CurrentSimDeltaSeconds > 0f;

        _physicsBracket?.RunPreKernelStep(world, CurrentSimDeltaSeconds, simRunning);
        _bootstrapper.Tick(wallDt);
        _physicsBracket?.RunPostKernelStep(world);

        // ⭐⭐ CE-245 — the debug API's jobs run HERE, on the node's own frame, after the kernel.
        //    ⛔ Every route body is queued rather than executed on the HTTP thread, so a read sees a
        //    consistent world between frames instead of mid-schedule. ClusterRunner drains at the
        //    equivalent point (Program.cs:601-603).
        _debugApiQueue?.DrainAll();
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-214</c> item ② — <b>mode 2 is the SIXTH <c>IProvidesDebugSurface</c> IMPLEMENTOR</b>,
    /// not merely the sixth provider. 🔒 Approved by the user <c>2026-09-09</c>.
    /// </summary>
    /// <remarks>
    /// ⚠ <c>CE-245</c> shipped this provider CONSTRUCTED INLINE. That worked — mode 2 hosts its own
    /// <c>DebugApiHost</c> and needs no aggregation — ⛔ but it left the node invisible to
    /// <c>ClusterRunner</c>'s <c>Program.cs:388</c> <c>.OfType&lt;IProvidesDebugSurface&gt;()</c> sweep,
    /// which is harmless for exactly as long as <c>ClusterRunner</c> never composes mode 2. ⭐ The
    /// interface is one method; implementing it now costs nothing and removes a trap later.
    ///
    /// <para>⭐ Returning <see langword="null"/> before <see cref="Boot"/> is the interface's OWN
    /// documented contract — <i>"nothing to contribute in this configuration"</i> — so a caller that
    /// asks too early gets the defined answer rather than an exception.</para>
    ///
    /// <para>⚠ <b>"SimHost" for the name AND the perspective</b>, and it is the same <c>CE-242</c>
    /// reasoning as the heartbeat: the perspective is what ROUTES a request to a node's surface, and
    /// mode 2 IS the cluster's SimHost-role node. A tool that can talk to a SimHost perspective talks
    /// to this one unchanged — the whole point of "a node replacing SimHost".</para>
    /// </remarks>
    public Hrot.Presentation.DebugApi.ISubsystemDebugProvider? CreateDebugProvider()
    {
        if (Context == null) return null;
        HrotNodeContext ctx = Context;

        return new Hrot.Presentation.DebugApi.SubsystemDebugProvider(
            subsystemName: "SimHost",
            perspective:   "SimHost",
            world:         () => ctx.World,
            entityMap:     () => ctx.EntityMap,
            tkbDb:         () => ctx.TkbDb,
            clusterState:  Hrot.Presentation.DebugApi.SubsystemDebugProvider
                               .ClusterStateFrom(() => ctx.ClusterSlave),
            architecture:  () => new Fdp.ModuleHost.Diagnostics.ArchitectureDiagnosticsService(() => ctx.Kernel));
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-245</c> — the mode-2 node's own debug/MCP surface.</b> 📄 Owning design:
    /// <c>DESIGN_Stride_Node_Modes.md</c> §7.2b and slice <c>S6</c> (<c>CE-214</c>), which requires the
    /// day-1 operator surface to land WITH <c>S4</c> rather than after it (<c>R-S14</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>📌 Why this was the next thing built and not a nice-to-have.</b> Mode 1 gets this free —
    /// <c>EditorSubsystem</c> wires its own <c>DebugApiHost</c> behind <c>HROT_DEBUG_API_PORT</c>, and
    /// mode 2 has no <c>EditorSubsystem</c>. Measured 2026-09-09: with CGF's API as the only instrument,
    /// a CGF + Stride run could show that entities were not moving but could not show ANYTHING about the
    /// node that owned them — every question about this node's own world had to be inferred from log
    /// greps. <c>CE-242</c>, <c>CE-243</c> and <c>CE-244</c> were each found that way and each cost a
    /// full rebuild-and-rerun cycle to confirm.</para>
    ///
    /// <para><b>⭐ Reuse, not new machinery.</b> This is the same four objects ClusterRunner composes for
    /// every non-editor node (<c>Program.cs:418-460</c>): a <c>MainThreadJobQueue</c>, a
    /// <c>SubsystemDebugProvider</c> over this node's world, a <c>PerspectiveScopedDispatcher</c>, and a
    /// <c>DebugApiHost</c> + <c>DebugApiService</c>. Nothing here is Stride-specific except which
    /// context the lambdas close over.</para>
    ///
    /// <para><b>⚠ Every accessor is a <c>Func</c>, deliberately</b> — the same measured reason
    /// <c>SimHostSubsystem.CreateDebugProvider</c> gives: the provider is built after boot, and
    /// <c>TkbDb</c> in particular is REPLACED on every PrepareLive/PrepareEdit, so a captured value
    /// would report the boot catalog forever.</para>
    ///
    /// <para><b>⛔ What this surface does NOT have, so no report over-claims it:</b> no
    /// <c>behaviorRegistry</c> (this node runs no CGF, so <c>GET /behaviors</c> answers honestly that it
    /// has none rather than fabricating an empty one), no <c>drive</c> facade (mode 2 is a time SLAVE —
    /// the cluster clock is driven through the orchestrator, not through this node), no gizmo buffer and
    /// no mission editor. It answers READS about this node's own world, which is what it exists for.</para>
    ///
    /// <para>Call after <see cref="Boot"/>. A null or unparseable port disables it entirely, exactly as
    /// the editor's and ClusterRunner's own gate does.</para>
    /// </summary>
    public void StartDebugApi(string? port)
    {
        if (Context == null) throw new InvalidOperationException("Boot() before StartDebugApi().");
        if (string.IsNullOrWhiteSpace(port) || !int.TryParse(port, out int p)) return;

        // ⭐ Capture must be ON before any panel draws or every dump is empty (ClusterRunner does the
        //   same, and for the same reason).
        Fdp.Diagnostics.Contracts.Panels.PanelSnapshot.CaptureEnabled = true;

        var provider = CreateDebugProvider();
        if (provider == null) return;

        var dispatcher = new Hrot.Presentation.DebugApi.PerspectiveScopedDispatcher(
            new[] { provider },
            currentPerspective: () => "SimHost",
            // ⛔ null, not false: there is no MasterSyncController on this node, and GET /capabilities
            //   must report "no master here" rather than "the master is idle".
            acksPending: null);

        _debugApiQueue = new Hrot.Editor.DebugApi.MainThreadJobQueue();
        _debugApiHost  = new Hrot.Editor.DebugApi.DebugApiHost(
            p, _debugApiQueue, shutdownCallback: () => { }, mode: "stride-node");
        _debugApiHost.AttachDispatcher(dispatcher);
        _debugApiHost.AttachService(new Hrot.Editor.DebugApi.DebugApiService(
            dispatcher,
            logSinks: () => Fdp.Core.Logging.MessageLogSinks.ForDiagnostics(null),
            behaviorRegistry: () => null,
            // ⭐⭐ CE-236 — PASSED, never defaulted. A defaulted WGS84Transform origin is 0N 0E while
            //    every node simulates on the Berlin origin HrotEnvironment.CreateGeoTransform() sets,
            //    so geo routes would answer against the wrong planet.
            geoTransform: HrotEnvironment.CreateGeoTransform()));
        _debugApiHost.Start();

        Log.Info("[StrideNodeShell] CE-245: debug API listening on {0} (perspective SimHost, node {1}).",
                 p, Context.NodeId);
    }

    public void Dispose()
    {
        try { _debugApiHost?.Dispose(); } catch { /* teardown best-effort */ }
        try { _bootstrapper.Dispose(); } catch { /* teardown best-effort */ }
        _participant = null;
    }
}
