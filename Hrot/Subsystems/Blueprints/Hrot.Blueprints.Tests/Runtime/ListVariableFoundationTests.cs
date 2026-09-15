using System.Runtime.InteropServices;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Ir;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// FC-2/LV-1a (Q#19-B, review F3/F4) -- the fixed-list VARIABLE foundation: capacity-carrying type
/// resolution (unmanaged, real computed size, <c>SizeReliable=false</c>), the per-class nested
/// `[InlineArray]` wrapper emission, the State field + InitialLength seeding, the BP1504 declaration
/// guard, and the F3 gate -- a REAL Roslyn-compiled State whose runtime layout answers
/// <c>Marshal.OffsetOf</c> (the exact query the emitted StateFields fallback uses), proven with a
/// NON-8-aligned element (short) as the review demanded.
/// </summary>
public sealed class ListVariableFoundationTests
{
    private static CompileOptions Options() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    // ---- type resolution ----------------------------------------------------

    [Theory]
    [InlineData("System.Int32", 4, "__List_System_Int32_4", 20)]   // 4 (Count) + 4*4
    [InlineData("System.Int16", 3, "__List_System_Int16_3", 12)]   // align(4,2)=4 + 3*2=10 -> alignUp(10,4)=12 (non-8-aligned element)
    [InlineData("System.Double", 2, "__List_System_Double_2", 24)] // align(4,8)=8 + 2*8=24
    public void Resolve_FixedList_UnmanagedRealSize_SizeUnreliable(
        string elem, int capacity, string expectedName, int expectedSize)
    {
        var ok = StaticTypeRegistry.Instance.TryResolve(
            new BlueprintTypeRef { TypeId = elem, Capacity = capacity, InitialLength = 1 }, out var t);

        Assert.True(ok);
        Assert.Equal(expectedName, t.FullName);
        Assert.True(t.IsUnmanaged);                 // passes BP1503, counts against the tier budget
        Assert.Equal(expectedSize, t.SizeBytes);
        Assert.False(t.SizeReliable);               // F3: runtime Marshal.OffsetOf layout, never baked
        Assert.Equal(capacity, t.Capacity);
        Assert.Equal(1, t.InitialLength);
        Assert.Equal(elem, t.ElementType!.FullName);
    }

    [Fact]
    public void Resolve_FixedList_ManagedElement_Fails()
    {
        Assert.False(StaticTypeRegistry.Instance.TryResolve(
            new BlueprintTypeRef { TypeId = "System.String", Capacity = 4 }, out _));
    }

    // ---- S4: the fallback must not drop Capacity ----------------------------

    /// <summary>
    /// ⭐⭐⭐ <b><c>S4</c> — a fixed list of a PROJECT type stays a list.</b>
    ///
    /// <para>
    /// 🔴🔴 <b>It did not.</b> The list branch resolves its element through the same table, so a
    /// dotted project FQN — the spelling the editor writes — failed it, fell through to Stage 4's AN2
    /// <i>"trust the dot"</i> retry, and that retry rebuilt the type ref from <c>TypeId</c> and
    /// <c>IsArray</c> alone. ⛔ <c>Capacity</c> and <c>InitialLength</c> were dropped on the floor, so
    /// <c>List&lt;Foo&gt;[4]</c> resolved as a single <c>Foo</c>: <b>one element where four were
    /// declared</b>, with no diagnostic — <c>BP1504</c> had already passed, and every later stage saw a
    /// well-formed scalar.
    /// </para>
    ///
    /// <para>
    /// ⚠ <c>Hrot.AI.Behaviors.StructDemoData</c> is deliberately a type the registry does NOT carry —
    /// a curated one (<c>MemberSlotList</c>) resolves at the first table hit and never reaches the arm
    /// under test.
    /// </para>
    /// </summary>
    [Fact]
    public void FixedListOfAProjectStruct_KeepsItsCapacity_ThroughTheAn2Fallback()
    {
        var typed = ResolveOneListField("Hrot.AI.Behaviors.StructDemoData", capacity: 4, oracle: null);

        Assert.Equal(4, typed.Capacity);
        Assert.Equal(2, typed.InitialLength);
        Assert.Equal("__List_Hrot_AI_Behaviors_StructDemoData_4", typed.FullName);
        Assert.False(typed.SizeReliable);                      // F3 holds regardless
        Assert.Equal("Hrot.AI.Behaviors.StructDemoData", typed.ElementType!.FullName);
    }

    /// <summary>
    /// ⭐⭐ <b><c>S4</c> + <c>S2</c> — the wrapper is sized from the element's REAL size.</b> Without an
    /// oracle the element is the AN2 4-byte guess, so a 4×12-byte list declares itself
    /// <c>4 + 4×4 = 20</c> bytes and <b>under-counts the tier budget by 32</b>. ⚠ The list's own
    /// <c>SizeReliable</c> stays <c>false</c> either way — an exact size buys a correct budget, not
    /// baked offsets (review <c>F3</c>).
    /// </summary>
    [Fact]
    public void FixedListOfAProjectStruct_IsSizedFromTheOraclesElementSize()
    {
        const int RealElem = 12;   // StructDemoData = 3 × int

        var guessed = ResolveOneListField("Hrot.AI.Behaviors.StructDemoData", 4, oracle: null);
        var exact   = ResolveOneListField("Hrot.AI.Behaviors.StructDemoData", 4,
                          oracle: fqn => fqn.EndsWith("StructDemoData", StringComparison.Ordinal)
                              ? RealElem : (int?)null);

        Assert.Equal(StaticTypeRegistry.ListWrapperSize(4, 4),        guessed.SizeBytes);
        Assert.Equal(StaticTypeRegistry.ListWrapperSize(4, RealElem), exact.SizeBytes);
        Assert.Equal(RealElem, exact.ElementType!.SizeBytes);
        Assert.False(exact.SizeReliable);
    }

    /// <summary>Declares one fixed-list variable, runs Stage 4, and hands back its resolved IR type.</summary>
    private static IrTypeRef ResolveOneListField(string elementTypeId, int capacity, Func<string, int?>? oracle)
    {
        var asset = BlueprintAssetBuilder.Instance("L").WithVariable("Xs", typeof(int), "0").Build();
        asset.Variables[0].Type.TypeId        = elementTypeId;
        asset.Variables[0].Type.Capacity      = capacity;
        asset.Variables[0].Type.InitialLength = 2;

        var sink = new DiagnosticSink();
        var typed = Stage4_TypeResolve.Run(
            asset, new ValidationContext(sink, Options() with { StructSizeOracle = oracle }));

        Assert.DoesNotContain(sink.All, d => d.Severity == DiagnosticSeverity.Error);
        return typed.FieldTypes[asset.Variables[0].Id];
    }

    // ---- BP1504 declaration guard ------------------------------------------

    [Fact]
    [Compiler.CoversDiagnosticCode("BP1504")]
    public void InitialLengthBeyondCapacity_BP1504()
    {
        var asset = BlueprintAssetBuilder.Instance("L").WithVariable("Xs", typeof(int), "0").Build();
        asset.Variables[0].Type.Capacity      = 4;
        asset.Variables[0].Type.InitialLength = 5;

        var sink = new DiagnosticSink();
        Stage4_TypeResolve.Run(
            asset, new ValidationContext(sink, Options()));

        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1504);
    }

    // ---- generated source + real Roslyn layout (the F3 gate) ----------------

    private static BlueprintAsset BuildListAsset(string elemTypeId, int capacity, int initialLength)
    {
        var asset = BlueprintAssetBuilder.Instance("ListVarBp")
            .WithVariable("MyList", typeof(int), "0")
            .WithVariable("After", typeof(int), "0")   // scalar AFTER the list: its baked offset is wrong => runtime layout
            .Build();
        asset.Variables[0].Type = new BlueprintTypeRef
        {
            TypeId = elemTypeId, Capacity = capacity, InitialLength = initialLength,
        };

        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(entryOut);
        var retIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true, TypeRef = new() };
        var ret = new ReturnNode { Id = Guid.NewGuid() };
        ret.Pins.Add(retIn);
        asset.Graphs.Add(new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, ret },
            Links = { new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = ret.Id, ToPinId = retIn.Id } },
        });
        return asset;
    }

    [Fact]
    public void Compile_EmitsNestedWrapper_StateField_Seed_AndRuntimeLayoutFallback()
    {
        var result = new BlueprintCompiler().Compile(BuildListAsset("System.Int32", 4, 2), Options());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;

        Assert.Contains("[global::System.Runtime.CompilerServices.InlineArray(4)]", src);
        Assert.Contains("public struct __Buf_System_Int32_4", src);
        Assert.Contains("public struct __List_System_Int32_4", src);
        Assert.Contains("public __List_System_Int32_4 MyList;", src);
        Assert.Contains("s.MyList.Count = 2;", src);                 // InitialLength seeding

        // F3/LV-5: the list is descriptor-VISIBLE (qualified nested wrapper type, runtime
        // offset/size -- the LV-5 watch reads it), and its unreliable size flips the
        // SCALAR-after-it onto the runtime Marshal.OffsetOf path too.
        Assert.Contains("\"MyList\"] = new", src);
        Assert.Matches(@"typeof\([A-Za-z0-9_]+\.__List_System_Int32_4\)", src);
        Assert.Matches(@"Marshal\.OffsetOf<[^>]+\.State>\(""MyList""\)", src);
        Assert.Matches(@"Marshal\.OffsetOf<[^>]+\.State>\(""After""\)", src);
    }

    [Fact]
    public void RoslynCompiledState_AnswersRuntimeLayoutQueries_NonEightAlignedElement()
    {
        // The F3 round-trip proof: compile + ALC-load a blueprint declaring a fixed list of a
        // NON-8-aligned element (short), then run the exact queries the emitted StateFields
        // fallback performs against the real CLR State type.
        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var asset = BuildListAsset("System.Int16", 3, 1);
        var assembly = fixture.CompileAndLoad(asset);

        var bpClass = assembly.GetTypes().Single(t => t.Name.EndsWith("_Bp") && t.GetNestedType("State") != null);
        var state   = bpClass.GetNestedType("State")!;
        var field   = state.GetField("MyList")!;

        int offset = (int)Marshal.OffsetOf(state, "MyList");         // must not throw, must be sane
        Assert.True(offset >= 16, $"list field offset {offset} overlaps the 16-byte cursor header");

        var listType = field.FieldType;
        Assert.Equal("__List_System_Int16_3", listType.Name);
        Assert.NotNull(listType.GetField("Count"));
        Assert.NotNull(listType.GetField("Items"));
        Assert.Equal(12, Marshal.SizeOf(listType));                  // matches the registry's computed size

        // InitDefault seeds Count = InitialLength over the zeroed blob.
        var bytes = new byte[Marshal.SizeOf(state) + 64];
        var init  = bpClass.GetMethod("InitDefault")!;
        // Span<byte> parameter: invoke through a delegate (reflection Invoke cannot box a ref struct).
        var del = (SpanAction)Delegate.CreateDelegate(typeof(SpanAction), init);
        del(bytes);
        int count = BitConverter.ToInt32(bytes, offset);             // wrapper starts with int Count
        Assert.Equal(1, count);
    }

    private delegate void SpanAction(Span<byte> bytes);

    // ---- LV-1b: AiPrimitive WorkingState lists ------------------------------

    [Fact]
    public void AiPrimitive_WorkingStateList_EmitsWrapperAndCountSeed()
    {
        var asset = BlueprintAssetBuilder
            .AiPrimitive("WsListPrim")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithWorkingStateField("Targets", typeof(int))
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.WorkingState[0].Type = new BlueprintTypeRef
        {
            TypeId = "System.Int32", Capacity = 4, InitialLength = 2,
        };

        var result = new BlueprintCompiler().Compile(asset, Options());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        var src = result.GeneratedSource!;

        Assert.Contains("public struct __List_System_Int32_4", src);      // wrapper nested in the primitive class
        Assert.Contains("public __List_System_Int32_4 Targets;", src);    // WorkingState field
        Assert.Contains("dst->Targets.Count = 2;", src);                  // InitDefaultWorkingState seed
    }
}
