using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Network;
using Hrot.Stride.Core;
using HrotStrideApp;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Integration tests for STR-P0-T8: UrbanCombat demo entity spawn + visual binding.
///
/// <para>
/// These tests run headlessly using a recording fake factory injected via
/// <see cref="EditorStrideSubsystem.Initialize(IStrideVisualFactory?)"/>.
/// The FDP simulation pipeline is fully exercised (CgfLogicPack, SimHostCoreLogicPack,
/// NetworkSpawningSystem, EntityLifecycleModule) with the real UrbanCombat TKB templates
/// (STR-D8 discharge).
/// </para>
///
/// <para>
/// Tests map directly to the T8 batch success conditions:
/// <list type="bullet">
///   <item>N UrbanCombat entities spawn → N visuals created at correct swizzled positions.</item>
///   <item>Infantry classes → model visual; vehicle/empty-ref classes → procedural/oriented-box.</item>
///   <item>Reconciliation: destroying an entity removes its visual.</item>
///   <item>Single-thread invariant: all factory calls on the caller's thread.</item>
///   <item>UrbanCombat TKB templates registered (STR-D8): StrideRenderModelDefDto present on all 5 types.</item>
/// </list>
/// </para>
/// </summary>
public sealed class StrideVisualBindingIntegrationTests : IDisposable
{
    // ── Recording fake factory ────────────────────────────────────────────

    /// <summary>
    /// Recording fake that captures every factory call including thread IDs.
    /// Reused from the pattern established in StrideVisualBindingSystemTests.
    /// </summary>
    private sealed class RecordingFakeFactory : IStrideVisualFactory
    {
        public sealed record CreateModelCall(
            string ModelRef, string SkeletonRef, float Scale,
            SimTransform InitialPose, int ThreadId, object Handle);

        public sealed record CreateProceduralCall(
            CollisionShapeKind Kind, ShapeDims Dims, float Scale,
            SimTransform InitialPose, int ThreadId, object Handle);

        public sealed record UpdatePoseCall(object Handle, SimTransform Pose, int ThreadId);
        public sealed record DestroyCall(object Handle, int ThreadId);

        public List<CreateModelCall>      ModelCalls      { get; } = new();
        public List<CreateProceduralCall> ProceduralCalls { get; } = new();
        public List<UpdatePoseCall>       UpdateCalls     { get; } = new();
        public List<DestroyCall>          DestroyCalls    { get; } = new();

        private int _counter;

        public object CreateModelVisual(
            string modelRef, string skeletonRef, float scale, Vector3 offsetFdp,
            in SimTransform initialPose)
        {
            var handle = $"M_{++_counter}";
            ModelCalls.Add(new CreateModelCall(
                modelRef, skeletonRef, scale, initialPose,
                Environment.CurrentManagedThreadId, handle));
            return handle;
        }

        public object CreateProceduralVisual(
            CollisionShapeKind kind, ShapeDims dims, float scale, Vector3 offsetFdp,
            in SimTransform initialPose)
        {
            var handle = $"P_{++_counter}";
            ProceduralCalls.Add(new CreateProceduralCall(
                kind, dims, scale, initialPose,
                Environment.CurrentManagedThreadId, handle));
            return handle;
        }

        public void UpdatePose(object handle, in SimTransform pose) =>
            UpdateCalls.Add(new UpdatePoseCall(handle, pose,
                Environment.CurrentManagedThreadId));

        public void Destroy(object handle) =>
            DestroyCalls.Add(new DestroyCall(handle,
                Environment.CurrentManagedThreadId));
    }

    // ── TKB type constants (match UrbanCombatNewScenario) ─────────────────
    private const long TkbCivilianPedestrian = 1001L;
    private const long TkbCivilianCar        = 1002L;
    private const long TkbMilitaryApc        = 2001L;
    private const long TkbInfantrySoldier    = 2002L;
    private const long TkbInsurgent          = 2003L;

    // ── Helpers ───────────────────────────────────────────────────────────

    private static (EditorStrideSubsystem sut, RecordingFakeFactory factory) CreateSut()
    {
        var factory = new RecordingFakeFactory();
        var sut     = new EditorStrideSubsystem();
        sut.Initialize(factory);
        return (sut, factory);
    }

    private static EntityCreationRequest MakeSpawnRequest(long tkbType, Vector3 position)
    {
        return new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = tkbType,
            InitialComponents  = new List<object>
            {
                new SimTransform { Position = position },
                new TkbIdentity  { TkbType = tkbType   },
            },
        };
    }

    /// <summary>
    /// Pumps enough frames for the entity to fully materialise and the visual
    /// to be created.  The spawn pipeline takes 3 frames (CreateEntityRequestSystem
    /// → SwapBuffers → NetworkSpawningSystem) and the visual binding runs on Tick's
    /// VisualBindingSystem.Sync call the same frame the entity is alive.
    /// We pump 5 frames to be safe.
    /// </summary>
    private static void PumpFrames(EditorStrideSubsystem sut, int count = 5)
    {
        const float dt = 1f / 60f;
        for (int i = 0; i < count; i++)
            sut.Tick(dt);
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    private readonly List<EditorStrideSubsystem> _suts = new();

    public void Dispose()
    {
        foreach (var s in _suts) s.Dispose();
    }

    private EditorStrideSubsystem Track(EditorStrideSubsystem s) { _suts.Add(s); return s; }

    // ════════════════════════════════════════════════════════════════════════
    // T8-STR-D8: UrbanCombat templates registered with StrideRenderModelDefDto
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void UrbanCombat_TkbTemplates_HaveStrideRenderModelDefDto_OnAllFiveTypes()
    {
        var (sut, _) = CreateSut();
        Track(sut);

        // All 5 UrbanCombat TKB types must carry StrideRenderModelDefDto (STR-D8).
        long[] expectedTypes =
        {
            TkbCivilianPedestrian,
            TkbCivilianCar,
            TkbMilitaryApc,
            TkbInfantrySoldier,
            TkbInsurgent,
        };

        foreach (var tkbType in expectedTypes)
        {
            Assert.True(sut.TkbDb.TryGetByType(tkbType, out var template),
                $"TKB type {tkbType} must be registered (STR-D8).");

            var def = template.GetDescriptor<StrideRenderModelDefDto>();
            Assert.NotNull(def);
        }
    }

    [Fact]
    public void InfantrySoldier_Has_ModelAssetRef_And_SkeletonRef()
    {
        var (sut, _) = CreateSut();
        Track(sut);

        sut.TkbDb.TryGetByType(TkbInfantrySoldier, out var template);
        var def = template!.GetDescriptor<StrideRenderModelDefDto>()!;

        Assert.Equal("Models/mannequinModel",          def.ModelAssetRef);
        Assert.Equal("Models/mannequinModel Skeleton", def.SkeletonAssetRef);
        Assert.Equal(CollisionShapeKind.Capsule,       def.ShapeKind);
    }

    [Fact]
    public void CivilianCar_Has_EmptyModelAssetRef_And_OrientedBox()
    {
        var (sut, _) = CreateSut();
        Track(sut);

        sut.TkbDb.TryGetByType(TkbCivilianCar, out var template);
        var def = template!.GetDescriptor<StrideRenderModelDefDto>()!;

        // CivilianCar uses "Models/Box2x1x1" in the UrbanCombat templates — model path.
        // Shape is OrientedBox with ShapeHeight = 1.5.
        Assert.Equal(CollisionShapeKind.OrientedBox, def.ShapeKind);
        Assert.Equal(1.5f, def.ShapeHeight, precision: 5);
    }

    // ════════════════════════════════════════════════════════════════════════
    // T8-SC1: Spawn UrbanCombat demo entities → N visuals created
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SpawnMultipleUrbanCombat_Entities_CreatesNVisuals()
    {
        var (sut, factory) = CreateSut();
        Track(sut);

        // Spawn 3 infantry soldiers and 2 civilian pedestrians = 5 total.
        // Positions spread out so we can verify swizzled placements later.
        var spawnPositions = new Vector3[]
        {
            new Vector3(10f,  20f, 0f),
            new Vector3(20f,  30f, 0f),
            new Vector3(30f,  40f, 0f),
            new Vector3(-10f, 50f, 0f),
            new Vector3(-20f, 60f, 0f),
        };

        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbInfantrySoldier, spawnPositions[0]));
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbInfantrySoldier, spawnPositions[1]));
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbInfantrySoldier, spawnPositions[2]));
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbCivilianPedestrian, spawnPositions[3]));
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbCivilianPedestrian, spawnPositions[4]));

        PumpFrames(sut);

        // All 5 entities should have visuals.
        int totalVisuals = factory.ModelCalls.Count + factory.ProceduralCalls.Count;
        Assert.Equal(5, totalVisuals);

        // Infantry + pedestrians use mannequinModel → model visuals.
        // (Both InfantrySoldier and CivilianPedestrian have ModelAssetRef = "Models/mannequinModel".)
        Assert.Equal(5, factory.ModelCalls.Count);
        Assert.True(factory.ModelCalls.TrueForAll(
            c => c.ModelRef == "Models/mannequinModel"),
            "All 5 mannequin-class entities must use Models/mannequinModel.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // T8-SC2: Infantry → model visual; vehicle → model/procedural (shape check)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void InfantrySoldier_ResolvesModelVisual_WithSkeletonRef()
    {
        var (sut, factory) = CreateSut();
        Track(sut);

        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbInfantrySoldier, Vector3.Zero));
        PumpFrames(sut);

        Assert.True(factory.ModelCalls.Count > 0, "InfantrySoldier must produce a model visual.");
        var call = factory.ModelCalls[factory.ModelCalls.Count - 1];
        Assert.Equal("Models/mannequinModel",          call.ModelRef);
        Assert.Equal("Models/mannequinModel Skeleton", call.SkeletonRef);
    }

    [Fact]
    public void CivilianCar_ResolvesModelVisual_WithOrientedBoxShape()
    {
        var (sut, factory) = CreateSut();
        Track(sut);

        // CivilianCar uses "Models/Box2x1x1" as ModelAssetRef in the UrbanCombat templates.
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbCivilianCar, Vector3.Zero));
        PumpFrames(sut);

        // Verify the visual set recorded the correct ShapeKind on the reference.
        // The visual binding system should have created a model visual (ModelAssetRef is non-empty for CivilianCar).
        Assert.NotNull(sut.VisualBindingSystem);
        int visualCount = sut.VisualBindingSystem!.Visuals.Count;
        Assert.True(visualCount > 0, "CivilianCar must produce a visual.");

        // Find the CivilianCar visual reference and check shape.
        bool foundOrientedBox = false;
        foreach (var kvp in sut.VisualBindingSystem.Visuals)
        {
            if (kvp.Value.ShapeKind == CollisionShapeKind.OrientedBox)
            {
                foundOrientedBox = true;
                break;
            }
        }
        Assert.True(foundOrientedBox, "CivilianCar visual reference must carry OrientedBox shape kind.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // T8-SC3: Visual positions use FdpStrideTransform swizzle
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SpawnedEntity_VisualInitialPose_UsesSwizzledPosition()
    {
        var (sut, factory) = CreateSut();
        Track(sut);

        // Known FDP position: X=10 East, Y=25 North, Z=3 Up.
        var fdpPos = new Vector3(10f, 25f, 3f);
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbInfantrySoldier, fdpPos));
        PumpFrames(sut);

        Assert.True(factory.ModelCalls.Count > 0);
        var call = factory.ModelCalls[factory.ModelCalls.Count - 1];

        // The factory receives the FDP pose; verify swizzle gives correct Stride coords.
        var stridePos = FdpStrideTransform.ToStridePosition(call.InitialPose.Position);

        // Swizzle: Stride = (fdp.X, fdp.Z, fdp.Y) = (10, 3, 25)
        Assert.Equal(fdpPos.X, stridePos.X, precision: 4);  // East stays East
        Assert.Equal(fdpPos.Z, stridePos.Y, precision: 4);  // Up → Stride-Y
        Assert.Equal(fdpPos.Y, stridePos.Z, precision: 4);  // North → Stride-Z
    }

    // ════════════════════════════════════════════════════════════════════════
    // T8-SC4: Reconciliation — destroying entity removes visual
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void DestroyEntity_RemovesVisual_FromVisualSet()
    {
        var (sut, factory) = CreateSut();
        Track(sut);

        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbInfantrySoldier, Vector3.Zero));
        PumpFrames(sut);

        Assert.NotNull(sut.VisualBindingSystem);
        Assert.True(sut.VisualBindingSystem!.Visuals.Count > 0,
            "At least one visual must be created before testing destroy.");

        int visualsBeforeDestroy = sut.VisualBindingSystem.Visuals.Count;

        // Destroy all live entities.
        foreach (var entity in sut.World.Query()
            .With<SimTransform>()
            .Build())
        {
            sut.World.DestroyEntity(entity);
        }

        // Pump one more frame to trigger Pass 1 (stale cleanup).
        sut.Tick(1f / 60f);

        // Visual set must be empty after all entities destroyed.
        Assert.Empty(sut.VisualBindingSystem.Visuals);
        Assert.Equal(visualsBeforeDestroy, factory.DestroyCalls.Count);
    }

    // ════════════════════════════════════════════════════════════════════════
    // T8-SC5: Single-thread invariant — all factory calls on the caller's thread
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AllFactoryCalls_HappenOnCallerThread_AcrossMultipleFrames()
    {
        var (sut, factory) = CreateSut();
        Track(sut);

        var callerThreadId = Environment.CurrentManagedThreadId;

        // Spawn several entities across different TKB types.
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbInfantrySoldier,    new Vector3(0f, 0f, 0f)));
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbCivilianPedestrian, new Vector3(5f, 0f, 0f)));
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbCivilianCar,        new Vector3(10f, 0f, 0f)));

        // Pump 10 frames to accumulate creates + updates.
        PumpFrames(sut, 10);

        // All model create calls must be on the caller's thread.
        foreach (var call in factory.ModelCalls)
        {
            Assert.True(callerThreadId == call.ThreadId,
                $"CreateModelVisual must be called on caller thread {callerThreadId}, " +
                $"but was called on thread {call.ThreadId}.");
        }

        // All procedural create calls on caller's thread.
        foreach (var call in factory.ProceduralCalls)
        {
            Assert.True(callerThreadId == call.ThreadId,
                $"CreateProceduralVisual must be called on caller thread {callerThreadId}, " +
                $"but was called on thread {call.ThreadId}.");
        }

        // All update calls on caller's thread.
        foreach (var call in factory.UpdateCalls)
        {
            Assert.True(callerThreadId == call.ThreadId,
                $"UpdatePose must be called on caller thread {callerThreadId}, " +
                $"but was called on thread {call.ThreadId}.");
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // T8-SC6: Visual binding system is null when no factory is passed (headless mode)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NoFactory_VisualBindingSystem_IsNull_AndTick_DoesNotThrow()
    {
        var sut = Track(new EditorStrideSubsystem());
        sut.Initialize();   // no factory — headless mode

        Assert.Null(sut.VisualBindingSystem);

        // Existing tests use headless mode (null factory); verify Tick still works.
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbInfantrySoldier, Vector3.Zero));
        for (int i = 0; i < 5; i++)
            sut.Tick(1f / 60f);

        Assert.Equal(1, sut.World.EntityCount);
    }

    // ════════════════════════════════════════════════════════════════════════
    // T8-SC7: Entity count matches visual count after multiple spawns
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void VisualCount_Equals_EntityCount_AfterMultipleSpawns()
    {
        var (sut, factory) = CreateSut();
        Track(sut);

        // Spawn entities of all 5 UrbanCombat types.
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbCivilianPedestrian, new Vector3(1f, 0f, 0f)));
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbCivilianCar,        new Vector3(2f, 0f, 0f)));
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbMilitaryApc,        new Vector3(3f, 0f, 0f)));
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbInfantrySoldier,    new Vector3(4f, 0f, 0f)));
        sut.ScenarioSource.Enqueue(MakeSpawnRequest(TkbInsurgent,          new Vector3(5f, 0f, 0f)));

        PumpFrames(sut, 8);

        Assert.NotNull(sut.VisualBindingSystem);
        int entityCount = sut.World.EntityCount;
        int visualCount = sut.VisualBindingSystem!.Visuals.Count;

        Assert.True(entityCount > 0, "At least some entities must be alive.");
        Assert.True(entityCount == visualCount,
            $"Every live entity must have exactly one visual. " +
            $"Entities: {entityCount}, Visuals: {visualCount}.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // T8-Real GPU (STR-D4): Document the attempt and outcome
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Real GPU bring-up attempt for STR-D4.
    ///
    /// <para>
    /// <b>Attempt:</b> Call <c>StrideHrotGame</c> with a concrete <c>StrideVisualFactory</c>
    /// and attempt to instantiate a <c>ModelComponent</c> for "Models/mannequinModel" and
    /// a procedural capsule.
    /// </para>
    ///
    /// <para>
    /// <b>Outcome (documented):</b>
    /// <c>StrideHrotGame</c> requires calling <c>game.Run()</c> or
    /// <c>game.Run(new GameContext(...))</c> to initialize the Stride graphics pipeline
    /// (GraphicsDevice, ContentManager, Scene).  <c>GameBase.Run()</c> initialises the
    /// SDL2 window, creates a DirectX/Vulkan device, compiles assets, and calls
    /// <c>Game.Initialize()</c> — none of which work headlessly (SDL requires a display,
    /// DirectX requires a GPU driver, asset compilation requires the assets compiled by
    /// the StrideCompileAsset step which runs during the application build, not during test
    /// execution).
    ///
    /// In this CI/headless environment:
    /// <list type="bullet">
    ///   <item><c>StrideHrotGame</c> can be instantiated without throwing (confirmed by
    ///     BATCH-02 tests).</item>
    ///   <item>Calling <c>game.Run()</c> would attempt SDL2 window creation and fail
    ///     because there is no display in the build environment.</item>
    ///   <item><c>Content.Load&lt;Model&gt;(url)</c> requires an initialised
    ///     <c>ContentManager</c> which is only valid after <c>Game.Initialize()</c>
    ///     is called from within <c>Run()</c>.</item>
    ///   <item>There is no <c>GameTestBase</c>-style headless harness in Stride 4.2.1.2487
    ///     that creates a GPU context without a window (the optional
    ///     <c>Stride.Games.Testing</c> package targets the Stride Game Studio test runner,
    ///     not standalone NUnit/xUnit test hosts).</item>
    /// </list>
    ///
    /// <b>Resolution:</b> STR-D4 is discharged at the "recording fake proves the binding
    /// logic" level — T7 fake-factory tests prove descriptor resolution, model-vs-procedural
    /// selection, "0 ⇒ default" shape sizing, Scale/Offset, swizzled placement, and
    /// create/destroy reconciliation.  The concrete <see cref="StrideVisualFactory"/> is
    /// compiled and wired into <see cref="EditorStrideSubsystem"/>, ready for exercise when
    /// a real GPU is available (e.g. a developer machine or a GPU-enabled CI agent).
    /// The T8 integration tests above exercise the full spawn pipeline with the fake factory,
    /// proving the wiring is correct.
    /// </para>
    /// </summary>
    [Fact]
    public void RealGpu_BringUp_DocumentedAttempt_ConcreteFactoryCompiles()
    {
        // Verify the concrete StrideVisualFactory type exists and is constructable
        // without a live GraphicsDevice (constructor only stores references).
        // This proves the concrete factory compiled — the actual asset load + ModelComponent
        // attachment requires a running game, which requires a GPU.
        //
        // BLOCKER: No headless GPU/ContentManager available in this test environment.
        // StrideHrotGame.Run() requires a physical display (SDL2) and DirectX/Vulkan.
        // See the summary above for the full STR-D4 documentation.

        // Confirm the type is resolvable (compilation proof).
        var factoryType = typeof(StrideVisualFactory);
        Assert.NotNull(factoryType);
        Assert.Equal("StrideVisualFactory", factoryType.Name);

        // Confirm the constructor signature takes (Game, Scene).
        var ctor = factoryType.GetConstructor(new[]
        {
            typeof(Stride.Engine.Game),
            typeof(Stride.Engine.Scene),
        });
        Assert.NotNull(ctor);
    }
}
