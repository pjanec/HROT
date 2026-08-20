using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Fbt;
using Fbt.Runtime;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Blueprints;
using Fdp.Toolkit.Blueprints.Attributes;
using FluentAssertions;
using Hrot.AI.Behaviors.Brains;
using Hrot.AiEditor.Generators;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

// Fully qualify the Roslyn MetadataReferenceResolver to avoid collision.
using RoslynMRR = Hrot.Blueprints.Core.Compiler.Roslyn.MetadataReferenceResolver;

namespace Hrot.AiEditor.Generators.Tests.Demos;

/// <summary>
/// S1-G proof tests — Slice 1 capstone.
/// Proves the end-to-end pipeline for a managed multi-DTO BTree:
/// JSON asset → generator → Roslyn compile → bridge register → interpreter tick → correct bytes.
///
/// Harness mirrors BlueprintRegistrarBridgeIntegrationTests, but uses a compilation that
/// includes Hrot.AI.Behaviors so StructSizeResolver can resolve the nested struct-DTO types
/// (DemoCounterParams, DemoAccumParams).
///
/// Offset arithmetic (same formula as BTreeBlackboardPackHelper.Pack):
///   counter (DemoCounterParams {int,int} = 8 bytes, align 4) → offset 0
///   accum   (DemoAccumParams   {int,int} = 8 bytes, align 4) → offset 8
///
/// Both DTOs are read back via:
///   Unsafe.As&lt;byte, TDto&gt;(ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint)offset))
/// which is exactly what the emitted thunks do.
///
/// Defaults note: managed-asset DefaultValueJson is NOT auto-written in the test harness
/// (BehaviorIngressSystem.ParseParams runs only at live entity assignment, not here).
/// The proof tests therefore seed Threshold/Step manually before ticking.
/// This is recorded as DEBT-AIB-013 in DEBT-TRACKER.md.
/// </summary>
public sealed class T10_MultiAction_ProofTests : IDisposable
{
    // ── Known packed offsets (mirrors BTreeBlackboardPackHelper.Pack output) ─────
    // counter: DemoCounterParams {int Counter; int Threshold} = 8 bytes, align 4 → offset 0
    // accum:   DemoAccumParams   {int Sum;     int Step}      = 8 bytes, align 4 → offset 8
    private const int CounterOffset = 0;
    private const int AccumOffset   = 8;

    private readonly BehaviorRegistry  _liveRegistry      = new();
    private readonly BlueprintRegistry _blueprintRegistry = new();

    public void Dispose() => _liveRegistry.Clear();

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Locates the repo root by walking up from the test assembly directory until a .sln is found,
    /// then returns the full path to the committed .btree.json file.
    /// </summary>
    private static string LoadJsonFromCommittedFile(string assetName)
    {
        string testDir = Path.GetDirectoryName(
            typeof(T10_MultiAction_ProofTests).Assembly.Location)!;

        string? repoRoot = null;
        var dir = new DirectoryInfo(testDir);
        while (dir != null)
        {
            if (dir.GetFiles("*.sln").Length > 0) { repoRoot = dir.FullName; break; }
            dir = dir.Parent;
        }

        if (repoRoot == null)
            throw new InvalidOperationException(
                $"Cannot locate repo root from '{testDir}'. Ensure a .sln file is present.");

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

    /// <summary>
    /// Generates BTree sources using a full-reference compilation so StructSizeResolver
    /// can resolve nested struct-DTO types (DemoCounterParams, DemoAccumParams).
    /// Returns 2-3 source files: [topology-core, (blackboard-struct-file), bridge].
    /// </summary>
    private static string[] GenerateBTreeSourcesWithBehaviorsRef(string json, string assetName)
    {
        var text   = new StringAdditionalText($"/p/{assetName}.btree.json", json);
        var driver = CSharpGeneratorDriver
            .Create(new BTreeJsonGenerator())
            .AddAdditionalTexts(new[] { (AdditionalText)text }.ToImmutableArrayCompat());

        // Full-reference compilation — includes Hrot.AI.Behaviors so StructSizeResolver works.
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
            "(a BTREE0002 means the struct-DTO type was not resolved — " +
            "check Hrot.AI.Behaviors is in the compilation references)");

        result.GeneratedTrees.Length.Should().BeGreaterThanOrEqualTo(2,
            "must produce at least topology core + bridge");

        return result.GeneratedTrees.Select(t => t.ToString()).ToArray();
    }

    /// <summary>Compiles sources into a collectible ALC (mirrors bridge test helper).</summary>
    private static (Assembly Assembly, AssemblyLoadContext Alc) CompileMultiAndLoad(
        string[] sources, string assemblyName)
    {
        // Force System.Text.Json into the AppDomain BEFORE ForRuntimeAssemblies enumerates the
        // loaded set. The emitted ParseParams bridge references System.Text.Json; when this test
        // runs in isolation (nothing else has touched STJ yet) the assembly is otherwise absent
        // from CurrentDomain.GetAssemblies(), so the reference resolver omits it and compilation
        // fails with CS0234. Forcing the type load makes the compile order-independent.
        GC.KeepAlive(typeof(System.Text.Json.JsonSerializer));

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
        var alc = new AssemblyLoadContext($"T10Test_{assemblyName}", isCollectible: true);
        var asm = alc.LoadFromStream(pe, pdb);
        return (asm, alc);
    }

    private AiHotReloadCoordinator CreateCoordinator() =>
        new AiHotReloadCoordinator(_liveRegistry, _blueprintRegistry,
            new AiHotReloadCoordinatorOptions());

    /// <summary>
    /// Full end-to-end: load JSON → generate → compile → bridge-register → return interpreter.
    /// </summary>
    private (Interpreter<BrainBlackboard, BTreeContext> Interpreter, AssemblyLoadContext Alc)
        BuildInterpreterFromJson(string assetName, string registrarName)
    {
        string json = LoadJsonFromCommittedFile(assetName);
        var srcs    = GenerateBTreeSourcesWithBehaviorsRef(json, assetName);
        var (asm, alc) = CompileMultiAndLoad(srcs, $"{assetName}ProofTest");

        using var coordinator = CreateCoordinator();
        var registrars = coordinator.ScanForRegistrars(asm);

        var bridge = registrars.FirstOrDefault(r => r.DeclaringType.Name == registrarName);
        bridge.Should().NotBeNull(
            $"ScanForRegistrars must discover '{registrarName}'");

        var stagingRegistry = new BehaviorRegistry();
        var bpStaging       = _blueprintRegistry.BeginStaging();
        var args = bridge!.Parameters
            .OrderBy(p => p.OrdinalIndex)
            .Select(p => p.ParameterType == typeof(BehaviorRegistry)
                         ? (object)stagingRegistry
                         : p.ParameterType == typeof(ActionRegistry<BrainBlackboard, BTreeContext>)
                           ? new ActionRegistry<BrainBlackboard, BTreeContext>()
                           : (object)bpStaging)
            .ToArray();
        bridge.RegisterMethod.Invoke(null, args);

        stagingRegistry.TryGetId(assetName, out int id)
            .Should().BeTrue($"'{assetName}' must be registered");
        stagingRegistry.TryGetDefinition(id, out var def)
            .Should().BeTrue("definition must be retrievable");
        def!.BTreeInterpreter.Should().NotBeNull("BTree interpreter must be non-null");

        return (def.BTreeInterpreter!, alc);
    }

    /// <summary>
    /// Builds a synthetic T10-matching DTO (no nodes, only the two blackboard variables) and
    /// calls <see cref="BTreeBridgeEmitCore.EmitBridge"/> directly to get the bridge source.
    /// The bridge source is then compiled via Roslyn (full AppDomain references so the real
    /// T10_MultiAction.Build() and DTO types are resolvable) and loaded into a collectible ALC.
    ///
    /// This avoids the full <see cref="BTreeJsonGenerator"/> pipeline — specifically the
    /// <c>BTreeMethodCompatibilityValidator</c> which fails to resolve action methods when
    /// the assembly load order in an isolated test run differs from the full suite run.
    ///
    /// The two DTO types are hardcoded to 8 bytes each
    /// (DemoCounterParams = {int Counter; int Threshold} and DemoAccumParams = {int Sum; int Step}).
    /// </summary>
    private (BehaviorDefinition Definition, AssemblyLoadContext Alc)
        BuildDefinitionViaFixtureBridge()
    {
        // ── 1. Synthetic DTO (T10 variables only, no nodes) ──────────────────────
        // TypeId uses CLR nested-type form with '+' — matches what the emitter receives from
        // the JSON. DtoTypeToGlobal converts '+' to '.' for the emitted C# code.
        const string CounterTypeId = "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCounterParams";
        const string AccumTypeId   = "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoAccumParams";

        var dto = new BehaviorTreeAssetDto
        {
            AssetId            = Guid.Parse("bb000010-0000-0000-0000-000000000000"), // T10 real GUID
            Name               = "T10_MultiAction",
            TargetNamespace    = "Hrot.AI.Behaviors.Trees",
            BlackboardTypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
            ContextTypeName    = "Fdp.Toolkit.Behavior.BTreeContext",
            Blackboard         = new BlackboardBlockDto
            {
                Managed  = true,
                TypeName = "Fdp.Toolkit.Behavior.Components.BrainBlackboard",
                Variables = new List<BlackboardVariableDto>
                {
                    new BlackboardVariableDto
                    {
                        Name             = "counter",
                        Type             = new BlackboardTypeRefDto { TypeId = CounterTypeId },
                        DefaultValueJson = "{\"Counter\":0,\"Threshold\":5}",
                    },
                    new BlackboardVariableDto
                    {
                        Name             = "accum",
                        Type             = new BlackboardTypeRefDto { TypeId = AccumTypeId },
                        DefaultValueJson = "{\"Sum\":0,\"Step\":7}",
                    },
                }
            }
        };

        // ── 2. Hardcoded size resolver (both DTOs are {int, int} = 8 bytes) ─────
        // DemoCounterParams: {int Counter; int Threshold} → 8 bytes, align 4.
        // DemoAccumParams:   {int Sum;     int Step}      → 8 bytes, align 4.
        Func<string, int?> sizeResolver = typeId => typeId switch
        {
            CounterTypeId => 8,
            AccumTypeId   => 8,
            _             => null,
        };

        // ── 3. Emit bridge source ─────────────────────────────────────────────────
        string bridgeSrc = BTreeBridgeEmitCore.EmitBridge(dto, sizeResolver);

        // ── 4. Compile and load ───────────────────────────────────────────────────
        // Force Hrot.AI.Behaviors into the AppDomain before ForRuntimeAssemblies enumerates it.
        // CLR lazy loads assemblies on first type access; when running in isolation only this test
        // method is JITted, so DemoCounterNodes (referenced only in other test methods) may not yet
        // be loaded. GC.KeepAlive triggers the JIT to reference the type and load its assembly.
        GC.KeepAlive(typeof(DemoCounterNodes.DemoCounterParams));
        GC.KeepAlive(typeof(Hrot.AI.Behaviors.Trees.T10_MultiAction));

        var (asm, alc) = CompileMultiAndLoad(new[] { bridgeSrc }, "T10FixtureBridgeProofTest");

        // ── 5. Invoke Register ────────────────────────────────────────────────────
        using var coordinator = CreateCoordinator();
        var registrars = coordinator.ScanForRegistrars(asm);

        var bridge = registrars.FirstOrDefault(r => r.DeclaringType.Name == "T10_MultiActionRegistrar");
        bridge.Should().NotBeNull("ScanForRegistrars must discover 'T10_MultiActionRegistrar'");

        var stagingRegistry = new BehaviorRegistry();
        var bpStaging       = _blueprintRegistry.BeginStaging();
        var args = bridge!.Parameters
            .OrderBy(p => p.OrdinalIndex)
            .Select(p => p.ParameterType == typeof(BehaviorRegistry)
                         ? (object)stagingRegistry
                         : p.ParameterType == typeof(ActionRegistry<BrainBlackboard, BTreeContext>)
                           ? new ActionRegistry<BrainBlackboard, BTreeContext>()
                           : (object)bpStaging)
            .ToArray();
        bridge.RegisterMethod.Invoke(null, args);

        stagingRegistry.TryGetId("T10_MultiAction", out int id)
            .Should().BeTrue("'T10_MultiAction' must be registered after bridge invoke");
        stagingRegistry.TryGetDefinition(id, out var def)
            .Should().BeTrue("definition must be retrievable");
        def.Should().NotBeNull("definition must not be null");

        return (def!, alc);
    }

    // ── Helper: project a DTO at a packed byte offset ─────────────────────────────

    private static unsafe ref T ReadDto<T>(ref BrainBlackboard bb, int byteOffset)
        where T : unmanaged
        => ref System.Runtime.CompilerServices.Unsafe.As<byte, T>(
               ref System.Runtime.CompilerServices.Unsafe.AddByteOffset(
                   ref bb.BehaviorParameters[0], (nint)byteOffset));

    // ── PROOF TEST 1 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// After N ticks with Threshold=N, counter.Counter reaches N then the condition
    /// returns Failure and the Sequence stops (no further increment).
    ///
    /// DEBT-AIB-013: DefaultValueJson is NOT auto-written in the test harness;
    /// Threshold is seeded manually before the first tick.
    /// </summary>
    [Fact]
    public void MultiAction_AfterNTicks_CounterReachesThresholdThenConditionFails()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        MultiAction_AfterNTicks_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe void MultiAction_AfterNTicks_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        const int N = 5;

        var (interpreter, alc) = BuildInterpreterFromJson("T10_MultiAction", "T10_MultiActionRegistrar");
        var bb  = new BrainBlackboard();
        var ctx = new BTreeContext();

        // DEBT-AIB-013: seed Threshold and Step manually.
        ref var counterDto = ref ReadDto<DemoCounterNodes.DemoCounterParams>(ref bb, CounterOffset);
        counterDto.Threshold = N;
        ref var accumDto = ref ReadDto<DemoCounterNodes.DemoAccumParams>(ref bb, AccumOffset);
        accumDto.Step = 3;

        // Ticks 1..N: condition passes → counter increments (Repeater(1)) → accum advances.
        for (int tick = 1; tick <= N; tick++)
        {
            var state = new BehaviorTreeState();
            interpreter.Tick(ref bb, ref state, ref ctx);
            ref var c = ref ReadDto<DemoCounterNodes.DemoCounterParams>(ref bb, CounterOffset);
            c.Counter.Should().Be(tick,
                because: $"tick {tick}: counter.Counter must equal {tick}");
        }

        ref var afterN = ref ReadDto<DemoCounterNodes.DemoCounterParams>(ref bb, CounterOffset);
        afterN.Counter.Should().Be(N,
            because: $"after {N} ticks counter.Counter must equal Threshold={N}");

        // Tick N+1: condition returns Failure → Sequence aborts → no more increments.
        {
            var state = new BehaviorTreeState();
            interpreter.Tick(ref bb, ref state, ref ctx);
        }

        ref var final = ref ReadDto<DemoCounterNodes.DemoCounterParams>(ref bb, CounterOffset);
        final.Counter.Should().Be(N,
            because: $"after condition-fails tick, counter must remain {N} (not incremented)");

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ── PROOF TEST 2 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// accum.Sum advances by Step per tick; counter DTO bytes are NOT modified by
    /// Action_AddStepToSum, and accum DTO bytes are NOT modified by Action_IncrementCounter.
    /// </summary>
    [Fact]
    public void MultiAction_SecondDtoMutatesIndependently()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        MultiAction_SecondDtoMutatesIndependently_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe void MultiAction_SecondDtoMutatesIndependently_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var (interpreter, alc) = BuildInterpreterFromJson("T10_MultiAction", "T10_MultiActionRegistrar");
        var bb  = new BrainBlackboard();
        var ctx = new BTreeContext();

        // Seed: Threshold=3, Step=7.
        ref var counterSetup = ref ReadDto<DemoCounterNodes.DemoCounterParams>(ref bb, CounterOffset);
        counterSetup.Threshold = 3;
        ref var accumSetup = ref ReadDto<DemoCounterNodes.DemoAccumParams>(ref bb, AccumOffset);
        accumSetup.Step = 7;

        // Tick 1: Condition passes (0<3) → Repeater(IncrementCounter) → AddStepToSum.
        {
            var state = new BehaviorTreeState();
            interpreter.Tick(ref bb, ref state, ref ctx);
        }

        ref var counter1 = ref ReadDto<DemoCounterNodes.DemoCounterParams>(ref bb, CounterOffset);
        ref var accum1   = ref ReadDto<DemoCounterNodes.DemoAccumParams>(ref bb, AccumOffset);

        counter1.Counter.Should().Be(1, because: "IncrementCounter ran once");
        counter1.Threshold.Should().Be(3, because: "Threshold must not have been touched by accum action");
        accum1.Sum.Should().Be(7, because: "AddStepToSum(Step=7): Sum = 0+7 = 7");
        accum1.Step.Should().Be(7, because: "Step must not have been touched by counter action");

        // 2 more ticks.
        for (int i = 0; i < 2; i++)
        {
            var state = new BehaviorTreeState();
            interpreter.Tick(ref bb, ref state, ref ctx);
        }

        ref var counterF = ref ReadDto<DemoCounterNodes.DemoCounterParams>(ref bb, CounterOffset);
        ref var accumF   = ref ReadDto<DemoCounterNodes.DemoAccumParams>(ref bb, AccumOffset);

        // After 3 ticks: counter reached Threshold=3.
        counterF.Counter.Should().Be(3,
            because: "counter.Counter must reach Threshold=3 after 3 ticks");
        counterF.Threshold.Should().Be(3, because: "Threshold must remain unchanged");

        // Accum advanced 3 times by Step=7.
        accumF.Sum.Should().Be(21, because: "Sum = 3 ticks × Step=7 = 21");
        accumF.Step.Should().Be(7, because: "Step must not have been modified by IncrementCounter");

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ── PROOF TEST 3 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// T11 aliasing: two Action_IncrementCounter nodes both bound to the same 'counter'
    /// variable at offset 0. Counter advances by +2 per tick (both nodes execute the same bytes).
    /// </summary>
    [Fact]
    public void Aliasing_TwoNodesShareOneVariable()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        Aliasing_TwoNodesShareOneVariable_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe void Aliasing_TwoNodesShareOneVariable_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var (interpreter, alc) = BuildInterpreterFromJson("T11_Aliasing", "T11_AliasingRegistrar");
        var bb  = new BrainBlackboard();
        var ctx = new BTreeContext();

        // T11 has no condition; Sequence[IncrementCounter_A, IncrementCounter_B] always runs both.

        // Tick 1: both nodes increment Counter at offset 0 (+1 +1 = +2).
        {
            var state = new BehaviorTreeState();
            interpreter.Tick(ref bb, ref state, ref ctx);
        }

        ref var c1 = ref ReadDto<DemoCounterNodes.DemoCounterParams>(ref bb, CounterOffset);
        c1.Counter.Should().Be(2,
            because: "two aliased IncrementCounter nodes each add +1; after 1 tick Counter must be 2");

        // Tick 2: another +2.
        {
            var state = new BehaviorTreeState();
            interpreter.Tick(ref bb, ref state, ref ctx);
        }

        ref var c2 = ref ReadDto<DemoCounterNodes.DemoCounterParams>(ref bb, CounterOffset);
        c2.Counter.Should().Be(4, because: "after 2 ticks with 2 aliased nodes, Counter must be 4");

        // Verify via raw bytes: int at CounterOffset=0 must equal 4.
        {
            // Must pin bb before taking pointer (fixed statement not needed for ref/unsafe arithmetic
            // since BrainBlackboard.BehaviorParameters is a fixed buffer; indexing [0] is already safe).
            ref var rawByte = ref bb.BehaviorParameters[CounterOffset];
            int rawCounter  = System.Runtime.CompilerServices.Unsafe.As<byte, int>(ref rawByte);
            rawCounter.Should().Be(4,
                because: "raw int at offset 0 must equal 4 — both thunks address the same bytes");
        }

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
    }

    // ── PROOF TEST 4 — ParseParams defaults (DEBT-AIB-013 fix) ───────────────────

    /// <summary>
    /// DEBT-AIB-013 fix proof: after calling ParseParams on a zeroed 128-byte buffer,
    /// the baked-in defaults from T10's DefaultValueJson are written at the correct offsets:
    ///   counter.Threshold == 5  (DemoCounterParams at offset 0, Threshold is the second int field = byte 4)
    ///   accum.Step        == 7  (DemoAccumParams at offset 8,   Step   is the second int field = byte 12)
    ///
    /// The incoming json argument is empty — the delegate writes baked defaults regardless.
    /// This is exactly what BehaviorIngressSystem.ParseParams does at entity assignment.
    /// </summary>
    [Fact]
    public void ParseParams_WritesDefaultsIntoBuffer_AtPackedOffsets()
    {
        WeakReference<AssemblyLoadContext>[] weakRefs;
        ParseParams_WritesDefaultsIntoBuffer_Body(out weakRefs);
        AwaitAlcCollection(weakRefs);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe void ParseParams_WritesDefaultsIntoBuffer_Body(
        out WeakReference<AssemblyLoadContext>[] weakRefs)
    {
        var (def, alc) = BuildDefinitionViaFixtureBridge();

        def.ParseParams.Should().NotBeNull(
            "T10 has DefaultValueJson on both variables — ParseParams must be non-null");

        // Allocate a zeroed 128-byte buffer (mirrors BrainBlackboard.BehaviorParameters).
        const int BufferSize = 128;
        byte* buf = stackalloc byte[BufferSize];
        for (int i = 0; i < BufferSize; i++) buf[i] = 0;

        // Invoke ParseParams with empty json (baked defaults are written regardless of arg).
        // ⭐ G1/E7: `host` is null — a root behaviour has none (DESIGN_Parameter_Model.md §3.4).
        def.ParseParams!("", buf, null!, default, null);

        // ── counter (DemoCounterParams, offset 0) ─────────────────────────────────
        // Layout: {int Counter, int Threshold} → Counter at +0, Threshold at +4 within the DTO.
        int counter_Counter   = *(int*)(buf + CounterOffset + 0);
        int counter_Threshold = *(int*)(buf + CounterOffset + 4);

        counter_Counter.Should().Be(0,
            "counter.Counter default is 0");
        counter_Threshold.Should().Be(5,
            "counter.Threshold default from T10 DefaultValueJson is 5");

        // ── accum (DemoAccumParams, offset 8) ────────────────────────────────────
        // Layout: {int Sum, int Step} → Sum at +0, Step at +4 within the DTO.
        int accum_Sum  = *(int*)(buf + AccumOffset + 0);
        int accum_Step = *(int*)(buf + AccumOffset + 4);

        accum_Sum.Should().Be(0,
            "accum.Sum default is 0");
        accum_Step.Should().Be(7,
            "accum.Step default from T10 DefaultValueJson is 7");

        alc.Unload();
        weakRefs = new[] { new WeakReference<AssemblyLoadContext>(alc) };
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
