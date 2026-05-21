using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Emit;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests;

/// <summary>
/// Tests covering Compiler Stage 7 (TASK-CP-004).
/// Test method names are suffixed with Stage7 so they can be filtered:
///   dotnet test --filter "Stage7"
/// </summary>
public sealed class Stage7Tests
{
    private static CompileOptions DefaultOptions() =>
        new CompileOptions(
            Mode:              CompilerMode.Debug,
            NodeRegistry:      BuiltInNodeRegistry.Instance,
            TypeRegistry:      StaticTypeRegistry.Instance,
            EngineEvents:      BuiltInEngineEventCatalog.Instance,
            ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
            WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
            SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static CompileResult Compile(BlueprintAsset asset)
    {
        var compiler = new BlueprintCompiler();
        return compiler.Compile(asset, DefaultOptions());
    }

    // ------------------------------------------------------------------
    // SC1: Library emission structural test
    // ------------------------------------------------------------------

    [Fact]
    public void Stage7_Library_EmitsStructuralExpectedContent()
    {
        var asset = BlueprintAssetBuilder
            .Library("MathUtils")
            .WithGraph("Add", g => g.Entry().Return())
            .Build();

        var result = Compile(asset);

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.GeneratedSource);

        var src = result.GeneratedSource!;

        // Class name has _Bp suffix with 8-char hex (Q-18.4)
        Assert.Contains("public static class MathUtils_", src);
        Assert.Contains("_Bp", src);

        // Constants
        Assert.Contains("public const int BlueprintId", src);

        // Registrar class
        Assert.Contains("BlueprintRegistrar_", src);

        // Register method uses BlueprintRegistryStaging (Patch C1)
        Assert.Contains("public static void Register(global::Fdp.Toolkit.Blueprints.BlueprintRegistryStaging staging)", src);

        // Must NOT use old BlueprintRegistry signature
        Assert.DoesNotContain("BlueprintRegistry registry", src);
    }

    // ------------------------------------------------------------------
    // SC2: AiPrimitive emission test -- class name and registrar
    // ------------------------------------------------------------------

    [Fact]
    public void Stage7_AiPrimitive_EmitsCorrectStructuresAndRegistrar()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("MoveToAndFire")
            .WithHostings(AiPrimitiveHosting.BTreeAction, AiPrimitiveHosting.HsmAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();

        var result = Compile(asset);

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.GeneratedSource);

        var src = result.GeneratedSource!;

        // Class name format Q-18.4
        Assert.Contains("public static class MoveToAndFire_", src);
        Assert.Contains("_Bp", src);

        // Required structs
        Assert.Contains("public struct Params", src);
        Assert.Contains("public struct WorkingState", src);

        // TickCore method
        Assert.Contains("TickCore", src);

        // BTree thunk (BTreeAction hosting)
        Assert.Contains("BTreeTick", src);

        // HSM thunk (HsmAction hosting)
        Assert.Contains("HsmActivity", src);

        // Registrar has BehaviorRegistry (has BTreeAction hosting)
        Assert.Contains("BehaviorRegistry behReg", src);

        // Phase 4 deferred: HSM runtime registration via HsmActionDispatcher is emitted in HR-001.
        // Phase 3 only emits the HsmActivity thunk; registrar body adds only the BlueprintDefinition.
        Assert.DoesNotContain("HsmActionDispatcher hsmDispatcher", src);
    }

    // ------------------------------------------------------------------
    // SC3: Instance emission test -- Tick signature includes instanceVersion
    // ------------------------------------------------------------------

    [Fact]
    public void Stage7_Instance_TickSignatureIncludesInstanceVersion()
    {
        var asset = BlueprintAssetBuilder
            .Instance("HealthRegen")
            .WithVariable("health", typeof(float))
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var result = Compile(asset);

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.GeneratedSource);

        var src = result.GeneratedSource!;

        // State struct with cursor
        Assert.Contains("public struct State", src);
        Assert.Contains("BlueprintLatentCursor Cursor", src);

        // Tick method includes instanceVersion (Q-18.1)
        Assert.Contains("uint instanceVersion)", src);

        // TickThunk also has instanceVersion
        Assert.Contains("TickThunk(", src);

        // StateSize property
        Assert.Contains("StateSize", src);
    }

    // ------------------------------------------------------------------
    // SC4: Determinism test
    // ------------------------------------------------------------------

    [Fact]
    public void Stage7_Library_CompileSameAssetTwice_ProducesSameSource()
    {
        var asset = BlueprintAssetBuilder
            .Library("DeterminismCheck")
            .WithGraph("Compute", g => g.Entry().Return())
            .Build();

        var result1 = Compile(asset);
        var result2 = Compile(asset);

        Assert.True(result1.Succeeded);
        Assert.True(result2.Succeeded);
        Assert.Equal(result1.GeneratedSource, result2.GeneratedSource);
    }

    // ------------------------------------------------------------------
    // SC5: IrTerm_Suspend in lowered IR throws InvalidOperationException
    // ------------------------------------------------------------------

    [Fact]
    public void Stage7_IrTermSuspend_ThrowsInvalidOperationException()
    {
        var graphId = Guid.NewGuid();
        var debug = new IrDebugAnnotation { GraphId = graphId, Synthesized = "test" };
        var debugTerm = new IrDebugAnnotation { GraphId = graphId, Synthesized = "test-term" };

        var suspendBlock = new IrBlock
        {
            Id = new IrBlockId(1),
            Label = "entry",
            Statements = Array.Empty<IrStatement>(),
            Terminator = new IrTerm_Suspend(
                ResumePoint: new IrValue(0, new IrTypeRef { FullName = "System.Int32" }),
                WaitUntilTime: null,
                ResumeBlock: new IrBlockId(2))
            { Debug = debugTerm },
        };

        var graph = new IrGraph
        {
            Id = graphId,
            Name = "Tick",
            Kind = IrGraphKind.Function,
            Blocks = new[] { suspendBlock },
            Entry = new IrBlockId(1),
        };

        var irAsset = new IrAsset
        {
            AssetId = Guid.NewGuid(),
            Name = "SuspendTest",
            SanitizedName = "SuspendTest",
            BlueprintId = unchecked((int)0xDEADBEEF),
            StructureHash = 0UL,
            Dispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind.Library,
            Graphs = new[] { graph },
        };

        var sink = new DiagnosticSink();

        var ex = Assert.Throws<InvalidOperationException>(
            () => Stage7_Emit.Run(irAsset, CompilerMode.Debug, sink));

        Assert.Contains("should have been lowered", ex.Message);
    }

    // ------------------------------------------------------------------
    // SC6: Class name format (Q-18.4)
    // ------------------------------------------------------------------

    [Fact]
    public void Stage7_ClassName_HasHexSuffixFormat()
    {
        var asset = BlueprintAssetBuilder
            .Library("MoveToAndFire")
            .WithGraph("Execute", g => g.Entry().Return())
            .Build();

        var result = Compile(asset);

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.GeneratedSource);

        var src = result.GeneratedSource!;

        // Class name must be MoveToAndFire_XXXXXXXX_Bp where XXXXXXXX is exactly 8 hex chars
        // Extract and verify via regex-like search
        int classIdx = src.IndexOf("public static class MoveToAndFire_", StringComparison.Ordinal);
        Assert.True(classIdx >= 0, "Expected class declaration not found in generated source");

        int nameStart = classIdx + "public static class MoveToAndFire_".Length;
        int nameEnd = src.IndexOf('\n', nameStart);
        var namePart = src.Substring(nameStart, nameEnd - nameStart).Trim();

        // Should end with _Bp and have exactly 8 hex chars before it
        Assert.EndsWith("_Bp", namePart);
        var hexPart = namePart.Substring(0, namePart.Length - "_Bp".Length);
        Assert.Equal(8, hexPart.Length);
        Assert.True(hexPart.All(c => Uri.IsHexDigit(c)),
            $"Class name hex part '{hexPart}' contains non-hex characters");
    }

    // ------------------------------------------------------------------
    // SC7: Instance with custom event -- deltaTime in signature (Q-18.3)
    // ------------------------------------------------------------------

    [Fact]
    public void Stage7_Instance_CustomEvent_HasDeltaTimeInSignature()
    {
        var asset = BlueprintAssetBuilder
            .Instance("SC7_Instance")
            .WithCustomEvent("OnHit")
            .WithEventGraph("OnHit", g => g.Entry().Return())
            .WithGraph("Tick", g => g.Entry().Return())
            .Build();

        var result = Compile(asset);

        Assert.True(result.Succeeded,
            $"Compile failed: {string.Join(", ", result.Diagnostics.Select(d => d.Code))}");
        Assert.NotNull(result.GeneratedSource);

        var src = result.GeneratedSource!;

        // Event_OnHit method exists
        Assert.Contains("Event_OnHit(", src);

        // Event method has float deltaTime parameter (Q-18.3)
        Assert.Contains("float deltaTime", src);
    }
}
