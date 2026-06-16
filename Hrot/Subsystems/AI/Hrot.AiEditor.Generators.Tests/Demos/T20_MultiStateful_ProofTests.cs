using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fbt;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using Fdp.Toolkit.Blueprints.Components;
using Fdp.Toolkit.Blueprints.Partitioning;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Hrot.AiEditor.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

// Fully qualify the Roslyn MetadataReferenceResolver to avoid collision.
using RoslynMRR = Hrot.Blueprints.Core.Compiler.Roslyn.MetadataReferenceResolver;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// S2-G proof tests — Slice 2 capstone.
///
/// Topology: Sequence[ AdvanceCursor_A(LimitA=3), AdvanceCursor_B(LimitB=5), IncrementCounter(Threshold=1000) ]
///
/// Pipeline:  JSON asset → BTreeJsonGenerator → Roslyn compile → bridge register
///            → BehaviorIngressSystem (real provisioning) → BTreeContext tick → assert bytes.
///
/// Per-tick arithmetic (fresh BehaviorTreeState per tick; cursors and counter start at 0):
///   Tick 1: A.Cursor 0→1 (Running). Seq blocked at A.     A=1, B=0, Counter=0.
///   Tick 2: A.Cursor 1→2 (Running). Seq blocked at A.     A=2, B=0, Counter=0.
///   Tick 3: A.Cursor 2→3 (Success). B.Cursor 0→1 (Run).  A=3, B=1, Counter=0.
///   Tick 4: A.Cursor 3→4 (Success). B.Cursor 1→2 (Run).  A=4, B=2, Counter=0.
///   Tick 5: A.Cursor 4→5 (Success). B.Cursor 2→3 (Run).  A=5, B=3, Counter=0.
///   Tick 6: A.Cursor 5→6 (Success). B.Cursor 3→4 (Run).  A=6, B=4, Counter=0.
///   Tick 7: A.Cursor 6→7 (Success). B.Cursor 4→5 (Suc).  A=7, B=5, Counter=1.
///
/// Packed offsets (BTreeBlackboardPackHelper order):
///   cursorA (DemoCursorParams = {int Limit} = 4 bytes) → offset 0
///   cursorB (DemoCursorParams = {int Limit} = 4 bytes) → offset 4
///   counter (DemoCounterParams = {int Counter, int Threshold} = 8 bytes) → offset 8
///
/// Slot keys (FNV-1a-32 of (assetId=bb000020…, nodeVisualId)):
///   Node A (VisualId bb200000-…-0003): slotKey = 1631759884
///   Node B (VisualId bb200000-…-0004): slotKey = 1614982265
///
/// DEBT-AIB-013 resolved: BehaviorIngressSystem.ParseParams runs when the entity is assigned,
/// so LimitA=3, LimitB=5, Threshold=1000 are all seeded before the first tick without
/// manual setup in the test.
/// </summary>
public sealed class T20_MultiStateful_ProofTests : IDisposable
{
    // ── Known slot keys (baked at code-gen time; must match T20_MultiStateful.Registrar.g.cs) ─
    private const int SlotKeyA = 1631759884;  // node VisualId bb200000-...-0003
    private const int SlotKeyB = 1614982265;  // node VisualId bb200000-...-0004

    // ── Known packed offsets ──────────────────────────────────────────────────────
    // cursorA: DemoCursorParams {int Limit}            = 4 bytes → offset 0
    // cursorB: DemoCursorParams {int Limit}            = 4 bytes → offset 4
    // counter: DemoCounterParams {int Counter, int Threshold} = 8 bytes → offset 8
    private const int CursorAParamOffset = 0;
    private const int CursorBParamOffset = 4;
    private const int CounterParamOffset = 8;

    private readonly BehaviorRegistry  _liveRegistry      = new();
    private readonly BlueprintRegistry _blueprintRegistry = new();

    public void Dispose() => _liveRegistry.Clear();

    // ── World factory ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a world with the core behavior components and all BlueprintBlackboard* tiers.
    /// Mirrors the pattern from BehaviorIngressStatefulTests.
    /// </summary>
    private static EntityRepository CreateWorld()
    {
        var world = new EntityRepository();
        world.RegisterComponent<BehaviorState>();
        world.RegisterComponent<BrainBlackboard>();
        world.RegisterComponent<BrainBTreeState>();
        world.RegisterComponent<BlueprintBlackboard1024>();
        world.RegisterComponent<BlueprintBlackboard4096>();
        world.RegisterComponent<BlueprintBlackboard16384>();
        return world;
    }

    // ── File loader ───────────────────────────────────────────────────────────────

    private static string LoadJsonFromCommittedFile(string assetName)
    {
        string testDir = Path.GetDirectoryName(
            typeof(T20_MultiStateful_ProofTests).Assembly.Location)!;

        string? repoRoot = null;
        var dir = new DirectoryInfo(testDir);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0) { repoRoot = dir.FullName; break; }
            dir = dir.Parent;
        }

        if (repoRoot == null)
            throw new InvalidOperationException(
                $"Cannot locate repo root from '{testDir}'.");

        string jsonPath = Path.Combine(
            repoRoot,
            "Hrot", "Subsystems", "Hrot.AI.Behaviors",
            "Assets", "BTrees", "Authoring",
            $"{assetName}.btree.json");

        if (!File.Exists(jsonPath))
            throw new InvalidOperationException(
                $"BTree asset file not found: '{jsonPath}'.");

        return File.ReadAllText(jsonPath);
    }

    // ── Generator + Roslyn pipeline ──────────────────────────────────────────────

    private static string[] GenerateBTreeSourcesWithBehaviorsRef(string json, string assetName)
    {
        var text   = new StringAdditionalText($"/p/{assetName}.btree.json", json);
        var driver = CSharpGeneratorDriver
            .Create(new BTreeJsonGenerator())
            .AddAdditionalTexts(new[] { (AdditionalText)text }.ToImmutableArrayCompat());

        var resolver = RoslynMRR.ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies());
        var refs     = resolver.Resolve();

        var compilation = CSharpCompilation.Create(
            "Gen_" + assetName,
            Array.Empty<SyntaxTree>(),
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        result.Diagnostics.Should().BeEmpty(
            $"generator must produce no diagnostics for '{assetName}' " +
            "(a BTREE0002 means a struct-DTO type was not resolved or the validator rejected a binding)");

        result.GeneratedTrees.Length.Should().BeGreaterThanOrEqualTo(2,
            "must produce at least topology core + bridge (managed asset adds blackboard struct too)");

        return result.GeneratedTrees.Select(t => t.ToString()).ToArray();
    }

    private static (Assembly Assembly, AssemblyLoadContext Alc) CompileMultiAndLoad(
        string[] sources, string assemblyName)
    {
        var resolver = RoslynMRR.ForRuntimeAssemblies(AppDomain.CurrentDomain.GetAssemblies());
        var refs     = resolver.Resolve();

        var syntaxTrees = sources
            .Select((src, i) =>
            {
                var st = Microsoft.CodeAnalysis.Text.SourceText.From(
                    src, System.Text.Encoding.UTF8);
                return CSharpSyntaxTree.ParseText(
                    st, new CSharpParseOptions(LanguageVersion.Latest),
                    path: $"{assemblyName}_{i}.g.cs");
            })
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName, syntaxTrees, refs,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                deterministic: true,
                allowUnsafe: true));

        using var pe  = new System.IO.MemoryStream();
        using var pdb = new System.IO.MemoryStream();
        var result = compilation.Emit(pe, pdb);

        if (!result.Success)
        {
            var errors = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id}({d.Location.GetMappedLineSpan().Path}:" +
                             $"{d.Location.GetMappedLineSpan().StartLinePosition.Line+1}): {d.GetMessage()}"));
            throw new InvalidOperationException(
                $"In-memory compilation of '{assemblyName}' failed:\n{errors}");
        }

        pe.Position  = 0;
        pdb.Position = 0;
        var alc = new AssemblyLoadContext($"T20Test_{assemblyName}", isCollectible: true);
        var asm = alc.LoadFromStream(pe, pdb);
        return (asm, alc);
    }

    // Note: the coordinator is only used for ScanForRegistrars (reflection scan).
    // We pass a throwaway staging registry so that coordinator.Dispose() does NOT
    // clear _liveRegistry (Dispose calls _behaviorRegistry.Clear()).
    private AiHotReloadCoordinator CreateScanCoordinator() =>
        new AiHotReloadCoordinator(new BehaviorRegistry(), _blueprintRegistry,
            new AiHotReloadCoordinatorOptions());

    /// <summary>
    /// Full pipeline: load JSON → generate → compile → bridge-register into _liveRegistry
    /// → return (interpreter, ALC).
    ///
    /// The bridge's Register method is called with _liveRegistry directly (not a staging copy)
    /// so that BehaviorIngressSystem can find the definition when it calls TryGetId / TryGetDefinition.
    /// </summary>
    private (Interpreter<BrainBlackboard, BTreeContext> Interpreter, AssemblyLoadContext Alc)
        BuildInterpreterFromJson(string assetName, string registrarName)
    {
        // Force required assemblies into the AppDomain so Roslyn's ForRuntimeAssemblies picks them up.
        // Fbt.Compiler (BTreeBuilder) is lazily loaded — force it explicitly to avoid CS0234 in
        // Roslyn in-memory compilation. Hrot.AI.Behaviors must also be loaded for DTO type resolution.
        GC.KeepAlive(typeof(Fbt.Compiler.FbtAutoDiscovery));     // forces Fbt.Compiler
        GC.KeepAlive(typeof(DemoCounterNodes.DemoCursorParams)); // forces Hrot.AI.Behaviors
        GC.KeepAlive(typeof(Hrot.AI.Behaviors.Trees.T20_MultiStateful));

        string json    = LoadJsonFromCommittedFile(assetName);
        var srcs       = GenerateBTreeSourcesWithBehaviorsRef(json, assetName);
        var (asm, alc) = CompileMultiAndLoad(srcs, $"{assetName}ProofTest");

        // Use a throwaway coordinator just for ScanForRegistrars.
        // coordinator.Dispose() calls _behaviorRegistry.Clear() — passing a
        // throwaway registry prevents it from clearing _liveRegistry.
        using var coordinator = CreateScanCoordinator();
        var registrars = coordinator.ScanForRegistrars(asm);

        var bridge = registrars.FirstOrDefault(r => r.DeclaringType.Name == registrarName);
        bridge.Should().NotBeNull(
            $"ScanForRegistrars must discover '{registrarName}'");

        // Pass _liveRegistry directly so BehaviorIngressSystem can find the definition.
        var bpStaging = _blueprintRegistry.BeginStaging();
        var actionReg = new ActionRegistry<BrainBlackboard, BTreeContext>();
        var args = bridge!.Parameters
            .OrderBy(p => p.OrdinalIndex)
            .Select(p => p.ParameterType == typeof(BehaviorRegistry)
                         ? (object)_liveRegistry
                         : p.ParameterType == typeof(ActionRegistry<BrainBlackboard, BTreeContext>)
                           ? (object)actionReg
                           : (object)bpStaging)
            .ToArray();
        bridge.RegisterMethod.Invoke(null, args);

        // Verify the definition was registered.
        _liveRegistry.TryGetId(assetName, out int id)
            .Should().BeTrue($"'{assetName}' must be registered in _liveRegistry after bridge invoke");
        _liveRegistry.TryGetDefinition(id, out var def)
            .Should().BeTrue("definition must be retrievable from _liveRegistry");
        def.Should().NotBeNull("definition must not be null");
        def!.BTreeInterpreter.Should().NotBeNull("BTree interpreter must be non-null");
        def.StatefulWorkingSlots.Should().NotBeNull(
            "T20 must carry StatefulWorkingSlots manifest (2 slots: A and B)");
        def.StatefulWorkingSlots!.Count.Should().Be(2,
            "T20 has exactly 2 stateful node instances (AdvanceCursor_A and AdvanceCursor_B)");

        return (def.BTreeInterpreter!, alc);
    }

    // ── Helper: read a DTO at a packed byte offset ────────────────────────────────

    private static unsafe ref T ReadDto<T>(ref BrainBlackboard bb, int byteOffset)
        where T : unmanaged
        => ref Unsafe.As<byte, T>(
               ref Unsafe.AddByteOffset(
                   ref bb.BehaviorParameters[0], (nint)byteOffset));

    // ── PROOF TEST 1 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// TwoStatefulInstances_MaintainIndependentState:
    /// Two AdvanceCursor nodes (A and B) share the same method but have distinct VisualIds →
    /// distinct FNV-1a slot keys → independent DemoCursorState slots in the partition table.
    ///
    /// After 7 ticks:  A.Cursor=7, B.Cursor=5.  A ≠ B proves no cross-talk.
    /// </summary>
    [Fact]
    public void TwoStatefulInstances_MaintainIndependentState()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        TwoStatefulInstances_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe void TwoStatefulInstances_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        // ── Setup ─────────────────────────────────────────────────────────────────
        var (interpreter, alc) = BuildInterpreterFromJson("T20_MultiStateful", "T20_MultiStatefulRegistrar");

        var world  = CreateWorld();
        Fdp.Core.Entity entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        // Run BehaviorIngressSystem — this calls ParseParams (sets LimitA=3, LimitB=5, Threshold=1000)
        // and provisions the two stateful partition slots.
        var ingress = new BehaviorIngressSystem(_liveRegistry);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = "T20_MultiStateful",
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);

        // ── Assert: a tier was provisioned and both slots are attached ────────────
        bool hasTier = world.HasComponent<BlueprintBlackboard1024>(entity)
                    || world.HasComponent<BlueprintBlackboard4096>(entity)
                    || world.HasComponent<BlueprintBlackboard16384>(entity);
        hasTier.Should().BeTrue(
            "BehaviorIngressSystem must have provisioned a BlueprintBlackboard* tier for 2 slots × 4 bytes");

        AssertBothSlotsAttached(world, entity);

        // ── Tick 7 times ──────────────────────────────────────────────────────────
        // ParseParams already seeded LimitA=3 at [0..3], LimitB=5 at [4..7].
        // BTreeContext.Self + World are required for stateful thunks to locate tier components.
        var ctx = new BTreeContext { Self = entity, World = world };
        for (int tick = 1; tick <= 7; tick++)
        {
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
            var state  = new BehaviorTreeState(); // fresh per tick (restart from root)
            interpreter.Tick(ref bb, ref state, ref ctx);
        }

        // ── Assert per-tick arithmetic ────────────────────────────────────────────
        // See class-level doc for full derivation.
        // After 7 ticks: A.Cursor=7, B.Cursor=5, A ≠ B (no cross-talk).
        ReadCursorStates(world, entity, out int cursorA, out int cursorB);

        cursorA.Should().Be(7,
            "Node A (LimitA=3): cursor increments every tick → A.Cursor=7 after 7 ticks");
        cursorB.Should().Be(5,
            "Node B (LimitB=5): blocked until A succeeds; first B-tick is tick 3 → 5 B-ticks → B.Cursor=5");
        cursorA.Should().NotBe(cursorB,
            "distinct slot keys → independent partition slots → no cross-talk; A.Cursor(7) ≠ B.Cursor(5)");

        world.Dispose();
        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ── PROOF TEST 2 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// MixedStatelessAndStateful_Coexist:
    /// Stateless IncrementCounter projects its DTO from BrainBlackboard at baked offset 8.
    /// Stateful cursors project DemoCursorState from partition slots.
    /// These two memory regions are disjoint — cursor advancement does NOT touch the counter
    /// DTO bytes, and counter increments do NOT touch cursor slot bytes.
    ///
    /// After 7 ticks: Counter.Counter=1, Counter.Threshold=1000 (unchanged).
    ///                A.Cursor=7, B.Cursor=5.
    /// </summary>
    [Fact]
    public void MixedStatelessAndStateful_Coexist()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        MixedStatelessAndStateful_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe void MixedStatelessAndStateful_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        // ── Setup (same pipeline as Test 1) ───────────────────────────────────────
        var (interpreter, alc) = BuildInterpreterFromJson("T20_MultiStateful", "T20_MultiStatefulRegistrar");

        var world  = CreateWorld();
        Fdp.Core.Entity entity = world.CreateEntity();
        world.AddComponent(entity, new BehaviorState());
        world.AddComponent(entity, new BrainBlackboard());
        world.AddComponent(entity, new BrainBTreeState());

        var ingress = new BehaviorIngressSystem(_liveRegistry);
        world.Bus.PublishManaged(new AssignBehaviorEvent
        {
            Entity       = entity,
            BehaviorName = "T20_MultiStateful",
            JsonParams   = string.Empty,
        });
        world.Bus.SwapBuffers();
        ingress.Execute(world, 0.016f);

        // ── Assert ParseParams wrote expected defaults ─────────────────────────────
        {
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
            ref var cursorAParams = ref ReadDto<DemoCounterNodes.DemoCursorParams>(ref bb, CursorAParamOffset);
            ref var cursorBParams = ref ReadDto<DemoCounterNodes.DemoCursorParams>(ref bb, CursorBParamOffset);
            ref var counterParams = ref ReadDto<DemoCounterNodes.DemoCounterParams>(ref bb, CounterParamOffset);

            cursorAParams.Limit.Should().Be(3, "ParseParams must have set cursorA.Limit=3");
            cursorBParams.Limit.Should().Be(5, "ParseParams must have set cursorB.Limit=5");
            counterParams.Counter.Should().Be(0, "counter.Counter must start at 0");
            counterParams.Threshold.Should().Be(1000, "ParseParams must have set Threshold=1000");
        }

        // ── Tick 7 times ──────────────────────────────────────────────────────────
        var ctx = new BTreeContext { Self = entity, World = world };
        for (int tick = 1; tick <= 7; tick++)
        {
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
            var state  = new BehaviorTreeState();
            interpreter.Tick(ref bb, ref state, ref ctx);
        }

        // ── Assert: BrainBlackboard DTOs are correct and disjoint ─────────────────
        {
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(entity);
            ref var cursorAParams = ref ReadDto<DemoCounterNodes.DemoCursorParams>(ref bb, CursorAParamOffset);
            ref var cursorBParams = ref ReadDto<DemoCounterNodes.DemoCursorParams>(ref bb, CursorBParamOffset);
            ref var counterParams = ref ReadDto<DemoCounterNodes.DemoCounterParams>(ref bb, CounterParamOffset);

            // Stateless IncrementCounter incremented once (tick 7, when both cursors returned Success).
            counterParams.Counter.Should().Be(1,
                "Counter increments once at tick 7 when both cursors return Success in the same tick");
            counterParams.Threshold.Should().Be(1000,
                "Threshold must remain 1000 — cursor advancement must not perturb counter DTO bytes at [8..15]");

            // CursorParam bytes at [0..7] are read-only by Action_AdvanceCursor; must not be modified.
            cursorAParams.Limit.Should().Be(3,
                "cursorA.Limit must remain 3 — counter advancement must not perturb cursorA DTO bytes at [0..3]");
            cursorBParams.Limit.Should().Be(5,
                "cursorB.Limit must remain 5 — counter advancement must not perturb cursorB DTO bytes at [4..7]");
        }

        // ── Assert: partition-slot cursors are correct ────────────────────────────
        ReadCursorStates(world, entity, out int cursorA, out int cursorB);

        cursorA.Should().Be(7,
            "A.Cursor=7: cursor increments every tick; counter advancement did not affect partition slot A");
        cursorB.Should().Be(5,
            "B.Cursor=5: cursor starts advancing at tick 3; counter advancement did not affect partition slot B");
        cursorA.Should().NotBe(cursorB,
            "slot A and slot B must differ (A=7, B=5) — independent slot regions, no aliasing");

        world.Dispose();
        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ── Shared helpers ────────────────────────────────────────────────────────────

    private static unsafe void AssertBothSlotsAttached(EntityRepository world, Fdp.Core.Entity entity)
    {
        // Check whichever tier the ingress provisioned.
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard16384>(entity);
            fixed (byte* mem = t.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKeyA, out _)
                    .Should().BeTrue($"slot A (key={SlotKeyA}) must be attached in BlueprintBlackboard16384");
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKeyB, out _)
                    .Should().BeTrue($"slot B (key={SlotKeyB}) must be attached in BlueprintBlackboard16384");
            }
            return;
        }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = t.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKeyA, out _)
                    .Should().BeTrue($"slot A (key={SlotKeyA}) must be attached in BlueprintBlackboard4096");
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKeyB, out _)
                    .Should().BeTrue($"slot B (key={SlotKeyB}) must be attached in BlueprintBlackboard4096");
            }
            return;
        }
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = t.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKeyA, out _)
                    .Should().BeTrue($"slot A (key={SlotKeyA}) must be attached in BlueprintBlackboard1024");
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKeyB, out _)
                    .Should().BeTrue($"slot B (key={SlotKeyB}) must be attached in BlueprintBlackboard1024");
            }
            return;
        }
        false.Should().BeTrue("entity must have a BlueprintBlackboard* tier after ingress Execute");
    }

    private static unsafe void ReadCursorStates(
        EntityRepository world, Fdp.Core.Entity entity, out int cursorA, out int cursorB)
    {
        if (world.HasComponent<BlueprintBlackboard16384>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard16384>(entity);
            fixed (byte* mem = t.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKeyA, out int offA)
                    .Should().BeTrue("slot A must exist when reading cursor states (16384)");
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKeyB, out int offB)
                    .Should().BeTrue("slot B must exist when reading cursor states (16384)");
                cursorA = Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + offA).Cursor;
                cursorB = Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + offB).Cursor;
            }
            return;
        }
        if (world.HasComponent<BlueprintBlackboard4096>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard4096>(entity);
            fixed (byte* mem = t.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKeyA, out int offA)
                    .Should().BeTrue("slot A must exist when reading cursor states (4096)");
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKeyB, out int offB)
                    .Should().BeTrue("slot B must exist when reading cursor states (4096)");
                cursorA = Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + offA).Cursor;
                cursorB = Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + offB).Cursor;
            }
            return;
        }
        if (world.HasComponent<BlueprintBlackboard1024>(entity))
        {
            ref var t = ref world.GetComponentRW<BlueprintBlackboard1024>(entity);
            fixed (byte* mem = t.Memory)
            {
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKeyA, out int offA)
                    .Should().BeTrue("slot A must exist when reading cursor states (1024)");
                BlueprintBlackboardPartitions.TryGetSlotOffset(mem, SlotKeyB, out int offB)
                    .Should().BeTrue("slot B must exist when reading cursor states (1024)");
                cursorA = Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + offA).Cursor;
                cursorB = Unsafe.AsRef<DemoCounterNodes.DemoCursorState>(mem + offB).Cursor;
            }
            return;
        }
        throw new InvalidOperationException(
            "entity has no BlueprintBlackboard* tier component — slot states cannot be read");
    }

    // ── ALC GC helper ─────────────────────────────────────────────────────────────

    private static void AwaitAlcCollection(WeakReference<AssemblyLoadContext>[] refs)
    {
        for (int i = 0; i < 50; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (refs.All(w => !w.TryGetTarget(out _))) return;
            System.Threading.Thread.Sleep(50);
        }
    }
}
