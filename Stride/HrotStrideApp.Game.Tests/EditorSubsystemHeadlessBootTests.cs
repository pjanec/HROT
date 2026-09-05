#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CarKinem.Core;
using DotRecast.Detour;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Runner;
using Fdp.Toolkit.Spatial;
using Fdp.Toolkit.Tkb;
using Hrot.CGF.Systems;
using Hrot.Core.Network;
using Hrot.Editor;
using Hrot.Stride.Core;
using HrotStrideApp;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// STRIDE-INTEG (CPU-only de-risk) — headless boot test for the REAL
/// <see cref="EditorSubsystem"/> with the Stride muscle injected via
/// <see cref="EditorSubsystem.MuscleModuleFactory"/>.
///
/// <para>
/// <b>Purpose:</b>
/// Prove that the real <c>EditorSubsystem</c> can boot headless with the Stride kernel-resident
/// muscle wired in through the <c>MuscleModuleFactory</c> seam (Stage-3 de-risk), and that the
/// muscle ticks and processes <see cref="NavigationIntent"/> exactly as it does today inside
/// <see cref="EditorStrideSubsystem"/>. No GPU / Bullet / Raylib required.
/// </para>
///
/// <para>
/// <b>What is asserted vs. deliberately omitted:</b>
/// <list type="bullet">
///   <item><b>Asserted:</b> <c>EditorSubsystem.Initialize(config)</c> does not throw;
///     the <c>StrideMuscleModule</c> is among the kernel's registered module types.</item>
///   <item><b>Asserted (SI3):</b> After spawning an InfantrySoldier and driving frames,
///     <see cref="CrowdAgent"/> is present on the entity (bridge auto-registered via
///     <see cref="NavigationIntentBridgeSystem"/>).</item>
///   <item><b>Asserted (SI4):</b> After spawning an APC and setting a <see cref="NavigationIntent"/>,
///     <see cref="VehicleNavigationIntentSystem"/> planned at least 1 corner.</item>
///   <item><b>Deliberately NOT asserted:</b> physical movement (<see cref="SimTransform"/>
///     position change) — requires the Bullet physics bracket (GPU/host-deferred).
///     <see cref="CrowdMotorIntent.Velocity"/> magnitude is advisory only (same caveat as
///     <c>B26-F6-2</c>: DotRecast cross-test GC state can yield dvel=0).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Frame-driving strategy:</b>
/// <see cref="EditorSubsystem.Update"/> calls <c>_kernel.Update()</c> which reads the
/// <see cref="Fdp.Toolkit.Time.Controllers.MasterSyncController"/> for the delta. The controller
/// starts in BarrierPending (dt=0 for ~200 ms real time), then transitions to Stepping. In both
/// modes all registered systems execute; navigation bridge registration and path planning are
/// dt-independent (they gate on component presence, not elapsed time). Physical velocity is
/// advisory only.
/// </para>
/// </summary>
public sealed class EditorSubsystemHeadlessBootTests : IDisposable
{
    // ── TKB constants (match UrbanCombatNewScenario + AssembledNavIntegrationTests) ──
    private const long TkbInfantrySoldier = 2002L;
    private const long TkbMilitaryApc     = 2001L;

    // ── Known-good on-navmesh coordinates (FDP space: X=East, Y=North, Z=Up) ──
    private static readonly Vector3 F6StartFdp = new(-4f,  2f, 0f);
    private static readonly Vector3 F6GoalFdp  = new( 4f, 13f, 0f);

    // SI4 vehicle coords: start well West-of-center so path routes East around the wall
    // (wall blocks FDP X=-5..5 at Y≈5; Vehicle agent radius=1.5 m erodes to X=-6.5..6.5).
    // Start at (-10, 3) faces East (Quaternion.Identity), first corner is to the East →
    // heading error ≈ 0° → cos(0)=1 → alignFactor=1 → speed = CruiseSpeed > 0.
    private static readonly Vector3 F7StartFdp = new(-10f,  3f, 0f);
    private static readonly Vector3 F7GoalFdp  = new(  5f, 12f, 0f);

    // The muscle set is captured so SI4 can reach VehicleNavIntentSystem.
    private readonly StrideMuscleModuleSet _muscleSet;
    private readonly DotRecastDtCrowdProvider _crowd;
    private readonly EditorSubsystem _editor;

    // ── Constructor: build EditorSubsystem with injected Stride muscle ─────────────

    public EditorSubsystemHeadlessBootTests()
    {
        _crowd = new DotRecastDtCrowdProvider(maxAgentRadius: 0.4f);

        // Capture the muscle set so tests can inspect it (e.g. VehicleNavIntentSystem).
        StrideMuscleModuleSet? capturedSet = null;

        _editor = new EditorSubsystem();

        // Set MuscleModuleFactory BEFORE Initialize so the Stride muscle is injected.
        // The lambda captures _crowd and writes capturedSet so tests can inspect the systems.
        // ToEditorModuleList() returns a single StrideMuscleModule whose RegisterSystems()
        // reproduces EXACTLY the same kernel-phase composition as EditorStrideSubsystem steps 7–7c.
        //
        // Additionally: register the extra ECS component types that EditorStrideSubsystem
        // registers in its Initialize (lines 422–437) but EditorSubsystem does not include
        // in its standard component registries. These are required by the Stride navigation
        // systems (NavigationIntentBridgeSystem.RegisterAgent checks HasComponent<CrowdAgent>,
        // CrowdAgentUpdateSystem guards on IsComponentTypeRegistered<CrowdAgent>, etc.).
        _editor.MuscleModuleFactory = ctx =>
        {
            // Mirror EditorStrideSubsystem.Initialize step 2: extra muscle-specific components.
            // ctx.World is the live EntityRepository, available before Kernel.Initialize().
            if (!ctx.World.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.CrowdMotorIntent>())
                ctx.World.RegisterComponent<Fdp.Toolkit.Navigation.CrowdMotorIntent>();
            if (!ctx.World.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.CrowdAgent>())
                ctx.World.RegisterComponent<Fdp.Toolkit.Navigation.CrowdAgent>();
            if (!ctx.World.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.NavAgentProfile>())
                ctx.World.RegisterComponent<Fdp.Toolkit.Navigation.NavAgentProfile>();

            var ms = StrideMuscleModules.Build(_crowd);
            capturedSet = ms;
            return ms.ToEditorModuleList();
        };

        // Boot the real EditorSubsystem headlessly.
        // Headless = true skips all Raylib/ImGui calls (DrawWorld, DrawUI canvas-dependent adapters).
        var config = new SubsystemConfig
        {
            Headless         = true,
            OwnWindow        = false,
            Deterministic    = true,
            SubsystemName    = "Editor",
            NodeId           = 0,
            IsActiveMapOwner = () => false,
        };

        _editor.Initialize(config);

        // capturedSet is assigned by the factory lambda above (called during Initialize).
        _muscleSet = capturedSet
            ?? throw new InvalidOperationException(
                "MuscleModuleFactory was not invoked during Initialize — capturedSet is null. " +
                "Check that EditorSubsystem calls MuscleModuleFactory when MuscleModuleFactory != null.");
    }

    public void Dispose()
    {
        // Shutdown cleanly — flushes regeneration scheduler and disposes kernel/world.
        try { _editor.Shutdown(); } catch { /* ignore dispose-time errors in tests */ }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SI1 — Boot: EditorSubsystem boots headlessly with Stride muscle
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// STRIDE-INTEG-1 (headless boot):
    /// The real <see cref="EditorSubsystem"/> initializes cleanly with
    /// <c>MuscleModuleFactory</c> pointing to the Stride muscle adapter — no exception,
    /// and 3 Update frames tick without error.
    /// </summary>
    [Fact]
    public void SI1_EditorSubsystem_BootsHeadless_WithStrideMuscleFactory()
    {
        // Constructor already called Initialize — reaching here proves it succeeded.
        Assert.NotNull(_editor);

        // Drive 3 frames — confirms the kernel advances without exception.
        _editor.Update(1f / 60f);
        _editor.Update(1f / 60f);
        _editor.Update(1f / 60f);
        // If we reach here: headless boot + 3 kernel ticks succeeded cleanly.
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SI2 — Kernel module list contains StrideMuscleModule
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// STRIDE-INTEG-2 (module registration):
    /// <see cref="StrideMuscleModule"/> is present in the kernel's registered module list
    /// after booting with <c>MuscleModuleFactory</c>.
    ///
    /// <para>
    /// Uses <see cref="Fdp.ModuleHost.ModuleHostKernel.GetRegisteredModuleTypeNames()"/>
    /// accessed via the public <see cref="EditorSubsystem.Kernel"/> property.
    /// </para>
    /// </summary>
    [Fact]
    public void SI2_KernelContainsStrideMuscleModule_AfterMuscleFactoryBoot()
    {
        var kernel = _editor.Kernel;
        Assert.NotNull(kernel);

        var moduleTypeNames = kernel.GetRegisteredModuleTypeNames();
        Assert.NotNull(moduleTypeNames);

        bool hasStrideModule = moduleTypeNames.Any(
            n => n.Contains("StrideMuscle", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasStrideModule,
            $"StrideMuscleModule must be registered in the kernel's module list after booting with " +
            $"MuscleModuleFactory. Registered module types: [{string.Join(", ", moduleTypeNames)}]. " +
            "If absent: ToEditorModuleList() was not called or the factory was not invoked.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SI3 — NavigationIntentBridgeSystem registers CrowdAgent on MoveTo
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// STRIDE-INTEG-3 (infantry navigation bridge):
    /// Spawn an InfantrySoldier, inject the DotRecast navmesh, issue a production MoveTo,
    /// drive N frames, and assert <see cref="NavigationIntentBridgeSystem"/> auto-registered
    /// the entity (<see cref="CrowdAgent"/> present).
    ///
    /// <para>
    /// This exercises the SAME kernel-resident <see cref="NavigationIntentBridgeSystem"/>
    /// path that runs in <c>EditorStrideSubsystem</c> today, now wired through
    /// <c>MuscleModuleFactory</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Deliberately NOT asserted:</b> <see cref="CrowdMotorIntent.Velocity"/> magnitude
    /// (advisory-only DotRecast GC caveat) and physical position change (needs Bullet).
    /// </para>
    /// </summary>
    [Fact]
    public void SI3_InfantryMoveTo_BridgeRegisters_CrowdAgent_InEditorSubsystem()
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        // ── Arrange: bake Infantry navmesh and inject into crowd provider ──────────
        var navMesh = BakeLCorridorInfantry();
        bool crowdInit = _crowd.TryInitializeNavMesh(navMesh);
        Assert.True(crowdInit, "SI3: DotRecastDtCrowdProvider must accept the Infantry navmesh.");

        bool startSnapped = _crowd.TrySnapToNavmesh(F6StartFdp, out _);
        bool goalSnapped  = _crowd.TrySnapToNavmesh(F6GoalFdp,  out _);
        Assert.True(startSnapped, $"SI3: F6 start {F6StartFdp} must snap to navmesh.");
        Assert.True(goalSnapped,  $"SI3: F6 goal {F6GoalFdp} must snap to navmesh.");

        // ── Get world and scenario source ───────────────────────────────────────────
        var world          = _editor.World;
        var scenarioSource = _editor.EntityCreationRequestSource;

        // ── Spawn InfantrySoldier (TKB 2002) via production spawn pipeline ─────────
        scenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = TkbInfantrySoldier,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = F6StartFdp, Rotation = Quaternion.Identity },
            },
        });

        // Pump 5 frames to materialise the entity.
        DriveFrames(5);

        var entity = world.Query().With<SimTransform>().Build().FirstOrNull();
        Assert.True(entity != Entity.Null,
            "SI3: InfantrySoldier must have spawned (SimTransform present) after 5 frames.");
        var e = entity;

        // Precondition: no VehicleState on infantry (BATCH-26 translator fix).
        bool hasVehicleState = world.IsComponentTypeRegistered<VehicleState>()
                               && world.HasComponent<VehicleState>(e);
        Assert.False(hasVehicleState,
            "SI3 precondition: InfantrySoldier must NOT carry VehicleState " +
            "(VehicleKinematicsTkbTranslator Capsule-scoped fix).");

        // Precondition: entity has BehaviorState (is a BTree entity — production type).
        bool hasBehaviorState = world.IsComponentTypeRegistered<BehaviorState>()
                                && world.HasComponent<BehaviorState>(e);
        Assert.True(hasBehaviorState,
            "SI3 precondition: InfantrySoldier must have BehaviorState (BrainTierBTree).");

        // ── Add NavAgentProfile + CrowdMotorIntent + NavigationStatus ─────────────
        if (world.IsComponentTypeRegistered<NavAgentProfile>()
            && !world.HasComponent<NavAgentProfile>(e))
        {
            world.AddComponent(e, new NavAgentProfile
            {
                AgentRadius        = 0.3f,
                AgentHeight        = 1.8f,
                MaxSlopeDeg        = 60f,
                PreferredLayerMask = (uint)NavLayerMask.Infantry,
            });
        }
        if (world.IsComponentTypeRegistered<CrowdMotorIntent>()
            && !world.HasComponent<CrowdMotorIntent>(e))
            world.AddComponent(e, new CrowdMotorIntent());
        if (world.IsComponentTypeRegistered<NavigationStatus>()
            && !world.HasComponent<NavigationStatus>(e))
            world.AddComponent(e, new NavigationStatus { Result = NavigationResult.InProgress });

        // ── Issue production MoveTo (mirrors B27-F6) ──────────────────────────────
        uint issuedActionId = FdpNavigationOrders.IssueMoveTo(
            world, e, F6GoalFdp, speed: 2.0f, arrivalRadius: 1.5f, NavLayerMask.Infantry);
        Assert.True(issuedActionId > 0,
            "SI3: IssueMoveTo must return a non-zero ActionInstanceId. " +
            "LocomotionChannel must be registered in the world.");

        // ── Drive N frames — bridge registers the crowd agent ────────────────────
        const int TicksToSettle = 30;
        DriveFrames(TicksToSettle);

        // ── PRIMARY ASSERT: CrowdAgent present (bridge auto-registered) ──────────
        bool hasCrowdAgent = world.IsComponentTypeRegistered<CrowdAgent>()
                             && world.HasComponent<CrowdAgent>(e);
        Assert.True(hasCrowdAgent,
            "SI3: CrowdAgent must be present after NavigationIntentBridgeSystem ran inside " +
            "the real EditorSubsystem kernel. " +
            "If absent: (a) StrideMuscleModule was not registered; " +
            "(b) ChannelArbitrationSystem cleared the LocomotionChannel; " +
            "(c) VehicleState on infantry prevented bridge enrollment.");

        // ── PRIMARY ASSERT: crowd agent registered in the provider ───────────────
        bool agentInProvider = _crowd.TryGetAgentSnapshot(e, out var snapshot);
        Assert.True(agentInProvider,
            "SI3: DtCrowd must have an agent snapshot — bridge registered it.");

        // ── ADVISORY: velocity > 0 when crowd computed dvel ──────────────────────
        // Same DotRecast cross-test GC caveat as B26-F6-2; do NOT fail when dvel=0.
        if (world.HasComponent<CrowdMotorIntent>(e) && snapshot.DesiredVelocity.Length() > 0.01f)
        {
            var motorIntent = world.GetComponent<CrowdMotorIntent>(e);
            float speed     = motorIntent.Velocity.Length();
            Assert.True(speed > 0.05f,
                $"SI3 advisory: CrowdMotorIntent.Velocity must be non-zero when crowd computed dvel. " +
                $"Got magnitude={speed:F3} dvel={snapshot.DesiredVelocity}. " +
                "If dvel>0 but Velocity=0: CrowdAgentUpdateSystem not writing back velocity.");
        }
        // NOTE: SimTransform.Position change NOT asserted — requires Bullet bracket.
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SI4 — VehicleNavigationIntentSystem plans path in EditorSubsystem
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// STRIDE-INTEG-4 (vehicle navigation intent):
    /// Spawn an APC, inject the vehicle navmesh as <c>INavmeshProvider</c> singleton,
    /// set a production <see cref="NavigationIntent"/>, drive one frame, and assert that
    /// <see cref="VehicleNavigationIntentSystem"/> planned ≥1 corner AND
    /// <see cref="VehicleState.Speed"/> &gt; 0.
    ///
    /// <para>
    /// Mirrors <c>AssembledNavIntegrationTests.B26_F7_2</c> but uses the real
    /// <c>EditorSubsystem</c> kernel. In the headless NoOp-physics path
    /// <c>BulletReverseSyncSystem</c> never runs, so <c>SimTransform</c> retains
    /// the spawn position when <c>VehicleNavigationIntentSystem</c> calls <c>PlanPath</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Deliberately NOT asserted:</b> physical position change (requires Bullet).
    /// </para>
    /// </summary>
    [Fact]
    public void SI4_VehicleIntent_PlansPath_AndWritesNonzeroVehicleState_InEditorSubsystem()
    {
        // ── Arrange: bake vehicle navmesh ─────────────────────────────────────────
        var navMesh  = BakeFloorWallVehicle();
        var provider = new DotRecastNavmeshProvider(
            new Dictionary<NavLayerMask, DtNavMesh> { [NavLayerMask.Vehicle] = navMesh });

        var world = _editor.World;
        world.SetSingletonManaged<INavmeshProvider>(provider);

        // ── Spawn APC ─────────────────────────────────────────────────────────────
        var scenarioSource = _editor.EntityCreationRequestSource;
        scenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = TkbMilitaryApc,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = F7StartFdp, Rotation = Quaternion.Identity },
            },
        });

        DriveFrames(3);

        var entity = world.Query().With<SimTransform>().With<VehicleState>().Build().FirstOrNull();
        Assert.True(entity != Entity.Null,
            "SI4: MilitaryAPC must have spawned with VehicleState present.");
        var e = entity;

        // Verify spawn position preserved (NoOp physics — BulletReverseSyncSystem inactive).
        var tf = world.GetComponent<SimTransform>(e);
        float distToSpawn = (tf.Position - F7StartFdp).Length();
        Assert.True(distToSpawn < 0.5f,
            $"SI4: SimTransform.Position after spawn must be near F7StartFdp ({F7StartFdp}). " +
            $"Got {tf.Position} (dist={distToSpawn:F3} m).");

        // ── Set production NavigationIntent (mirrors B26-F7-2) ──────────────────
        var currentIntent = world.HasComponent<NavigationIntent>(e)
            ? world.GetComponent<NavigationIntent>(e) : default;
        currentIntent.IntentId++;
        currentIntent.Mode             = NavigationMode.DirectPoint;
        currentIntent.FinalDestination = F7GoalFdp;
        currentIntent.TargetSpeed      = 3f;
        currentIntent.ArrivalRadius    = 1.5f;

        if (world.HasComponent<NavigationIntent>(e))
            world.SetComponent(e, currentIntent);
        else
            world.AddComponent(e, currentIntent);

        if (world.IsComponentTypeRegistered<NavigationStatus>()
            && !world.HasComponent<NavigationStatus>(e))
            world.AddComponent(e, new NavigationStatus { Result = NavigationResult.InProgress });

        // ── Drive one frame ───────────────────────────────────────────────────────
        DriveFrames(1);

        // ── PRIMARY ASSERT: corners > 0 ──────────────────────────────────────────
        Assert.NotNull(_muscleSet.VehicleNavIntent);
        int corners = _muscleSet.VehicleNavIntent.GetCornerCount(e);
        Assert.True(corners > 0,
            $"SI4: VehicleNavigationIntentSystem must plan ≥1 corner for the DirectPoint intent. " +
            $"Got {corners} corners. " +
            $"If 0: PlanPath started from wrong position (off-navmesh) or system did not run. " +
            $"SimTransform.Position={world.GetComponent<SimTransform>(e).Position}");

        // ── PRIMARY ASSERT: VehicleState.Speed > 0 ───────────────────────────────
        var vs = world.GetComponent<VehicleState>(e);
        Assert.True(vs.Speed > 0.01f,
            $"SI4: VehicleState.Speed must be non-zero after the first steering tick. " +
            $"Got speed={vs.Speed:F3}.");

        // ── ASSERT: NavigationStatus echoes the intent ────────────────────────────
        if (world.HasComponent<NavigationStatus>(e))
        {
            var ns = world.GetComponent<NavigationStatus>(e);
            Assert.Equal(currentIntent.IntentId, ns.IntentId);
            Assert.NotEqual(NavigationResult.NoPath, ns.Result);
        }
        // NOTE: SimTransform.Position change NOT asserted — requires Bullet bracket.
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Private helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Drives <paramref name="count"/> frames through <see cref="EditorSubsystem.Update"/>.
    /// Systems execute regardless of whether the time controller is in BarrierPending or
    /// Stepping mode; navigation bridge registration and path planning are dt-independent.
    /// </summary>
    private void DriveFrames(int count)
    {
        const float dt = 1f / 60f;
        for (int i = 0; i < count; i++)
            _editor.Update(dt);
    }

    // ── Navmesh geometry helpers (mirrors AssembledNavIntegrationTests) ────────────

    /// <summary>L-corridor Infantry navmesh — same geometry as AssembledNavIntegrationTests.</summary>
    private static DtNavMesh BakeLCorridorInfantry()
    {
        var verts = new List<float>();
        var idx   = new List<int>();

        void AddQuad(float x0, float z0, float x1, float z1)
        {
            int b = verts.Count / 3;
            verts.AddRange(new[] { x0, 0f, z0, x1, 0f, z0, x1, 0f, z1, x0, 0f, z1 });
            idx.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }

        AddQuad(-12f, -5f, 0f, 15f);
        AddQuad(0f, 5f, 12f, 15f);

        var baker  = new StrideNavmeshBaker();
        var meshes = baker.Bake(verts.ToArray(), idx.ToArray(), NavLayerMask.Infantry);
        Assert.True(meshes.ContainsKey(NavLayerMask.Infantry),
            "Infantry navmesh must bake for STRIDE-INTEG test.");
        return meshes[NavLayerMask.Infantry];
    }

    /// <summary>Floor + E-W wall Vehicle navmesh — same geometry as AssembledNavIntegrationTests.</summary>
    private static DtNavMesh BakeFloorWallVehicle()
    {
        var verts = new List<float>();
        var idx   = new List<int>();

        void AddGroundQuad(float minX, float maxX, float minZ, float maxZ, float y)
        {
            int b = verts.Count / 3;
            verts.AddRange(new[] { minX, y, minZ, maxX, y, minZ, maxX, y, maxZ, minX, y, maxZ });
            idx.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }

        AddGroundQuad(-15f, 15f, -1f, 20f, 0f);
        BoxGeometryHelper.ExtractBoxTriangles(
            Stride.Core.Mathematics.Matrix.Translation(new Stride.Core.Mathematics.Vector3(0f, 1f, 5f)),
            new Stride.Core.Mathematics.Vector3(5f, 1f, 0.25f),
            verts, idx);

        var baker  = new StrideNavmeshBaker();
        var meshes = baker.Bake(verts.ToArray(), idx.ToArray(), NavLayerMask.Vehicle);
        Assert.True(meshes.ContainsKey(NavLayerMask.Vehicle),
            "Vehicle navmesh must bake for STRIDE-INTEG test.");
        return meshes[NavLayerMask.Vehicle];
    }
}
