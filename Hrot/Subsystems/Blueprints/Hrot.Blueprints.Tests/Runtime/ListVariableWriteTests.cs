using System.Runtime.InteropServices;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Tests.Builders;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// FC-2/LV-3 -- fixed-list VARIABLE writes via <see cref="ListWriteNode"/> (the six
/// <see cref="CollectionWriteOp"/> verbs bound to a declared list variable by VariableId):
/// <list type="bullet">
///   <item>Stage5 lowers to <c>IrOp_ListWrite</c> -- no entity, no accessor; emit mutates the
///   state field in place through a scoped Span cast (R3), with the F2 clamp on the working
///   count and G6 zeroing on every shrink/remove/clear path;</item>
///   <item>required operands are resolved WIRED-ONLY -- an unwired required operand degrades to
///   the safe no-write Ok=false (never a dangling IrValue);</item>
///   <item>BP1505 rejects a non-list write target; BP1506 fences the list value off generic
///   pins, with the identical-shape SetVariable whole-list clone as the one exception (which
///   lowers to flat struct copies -- no loop, no Span);</item>
///   <item>editor pin projection is byte-for-byte parity with Stage0 for all six ops.</item>
/// </list>
/// </summary>
public sealed class ListVariableWriteTests
{
    private static CompileOptions Options() => new(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    private static Pin ExecPin(string name, string dir) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = true, TypeRef = new() };

    private static Pin DataPin(string name, string dir, string typeId, bool isArray = false) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId, IsArray = isArray } };

    /// <summary>
    /// Asset with list var "MyList" (int × 4, initial 2); graph is Entry → ListWrite(op) → Return,
    /// with Int32 literals wired into the op's required operands ("Index"/"Length" ←
    /// <paramref name="intLiteral"/>, "Value" ← <paramref name="valueLiteral"/>; pass null to
    /// leave an operand deliberately unwired).
    /// </summary>
    private static BlueprintAsset BuildWriteAsset(
        CollectionWriteOp op, string? valueLiteral = null, string? intLiteral = null)
    {
        var asset = BlueprintAssetBuilder.Instance("ListWriteBp")
            .WithVariable("MyList", typeof(int), "0")
            .Build();
        asset.Variables[0].Type = new BlueprintTypeRef { TypeId = "System.Int32", Capacity = 4, InitialLength = 2 };

        var lwIn  = ExecPin("In", "In");
        var lwOut = ExecPin("Out", "Out");
        var lw = new ListWriteNode { Id = Guid.NewGuid(), VariableId = asset.Variables[0].Id.ToString(), Op = op };
        lw.Pins.AddRange(new[] { lwIn, lwOut });

        var entryOut = ExecPin("Out", "Out");
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(entryOut);
        var retIn = ExecPin("In", "In");
        var ret = new ReturnNode { Id = Guid.NewGuid() };
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, lw, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = lw.Id,  ToPinId = lwIn.Id },
                new Link { FromNodeId = lw.Id,    FromPinId = lwOut.Id,    ToNodeId = ret.Id, ToPinId = retIn.Id },
            },
        };

        if (intLiteral is not null)
        {
            string pinName = op == CollectionWriteOp.Resize ? "Length" : "Index";
            var pin = DataPin(pinName, "In", "System.Int32");
            lw.Pins.Add(pin);
            var litOut = DataPin("Value", "Out", "System.Int32");
            var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = intLiteral };
            lit.Pins.Add(litOut);
            graph.Nodes.Add(lit);
            graph.Links.Add(new Link { FromNodeId = lit.Id, FromPinId = litOut.Id, ToNodeId = lw.Id, ToPinId = pin.Id });
        }
        if (valueLiteral is not null)
        {
            var pin = DataPin("Value", "In", "System.Int32");
            lw.Pins.Add(pin);
            var litOut = DataPin("Value", "Out", "System.Int32");
            var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = valueLiteral };
            lit.Pins.Add(litOut);
            graph.Nodes.Add(lit);
            graph.Links.Add(new Link { FromNodeId = lit.Id, FromPinId = litOut.Id, ToNodeId = lw.Id, ToPinId = pin.Id });
        }
        if (op != CollectionWriteOp.Clear)
            lw.Pins.Add(DataPin("Ok", "Out", "System.Boolean"));

        asset.Graphs.Add(graph);
        return asset;
    }

    private static string Compile(BlueprintAsset asset)
    {
        var result = new BlueprintCompiler().Compile(asset, Options());
        Assert.True(result.Succeeded,
            "Compile failed: " + string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}:{d.Message}")));
        return result.GeneratedSource!;
    }

    // ---- per-op lowering/emit ----------------------------------------------

    [Fact]
    public void Add_EmitsSpanForm_ClampedCount_CapacityGuard_AppendAndOkTrue()
    {
        var src = Compile(BuildWriteAsset(CollectionWriteOp.Add, valueLiteral: "5"));

        Assert.Contains("(global::System.Span<int>)s.MyList.Items;", src);            // R3 span form
        Assert.Matches(@"int __lwc\d+ = global::System\.Math\.Min\(s\.MyList\.Count, 4\);", src); // F2 clamp
        Assert.Matches(@"if \(__lwc\d+ < 4\)", src);                                  // capacity guard
        Assert.Matches(@"s\.MyList\.Count = __lwc\d+ \+ 1;", src);                    // append
        Assert.Matches(@"__t\d+ = true;", src);                                       // Ok on success
    }

    [Fact]
    public void SetAt_EmitsGuardedUnsignedIndexCheck()
    {
        var src = Compile(BuildWriteAsset(CollectionWriteOp.SetAt, valueLiteral: "9", intLiteral: "1"));

        Assert.Matches(@"if \(\(uint\)__t\d+ < \(uint\)__lwc\d+\)", src);             // never-throw bound check
        Assert.Matches(@"__lws\d+\[__t\d+\] = __t\d+;", src);                         // in-place element write
        Assert.DoesNotContain(".Count =", src.Substring(src.IndexOf("__lws")));       // SetAt never resizes
    }

    [Fact]
    public void InsertAt_EmitsShiftUpAndAppendCount()
    {
        var src = Compile(BuildWriteAsset(CollectionWriteOp.InsertAt, valueLiteral: "9", intLiteral: "0"));

        Assert.Matches(@"if \(__lwc\d+ < 4 && \(uint\)__t\d+ <= \(uint\)__lwc\d+\)", src);
        Assert.Matches(@"__lws\d+\[__t\d+\.\.__lwc\d+\]\.CopyTo\(__lws\d+\[\(__t\d+ \+ 1\)\.\.\]\);", src); // shift up
        Assert.Matches(@"s\.MyList\.Count = __lwc\d+ \+ 1;", src);
    }

    [Fact]
    public void RemoveAt_EmitsShiftDown_ZeroesVacatedSlot_G6()
    {
        var src = Compile(BuildWriteAsset(CollectionWriteOp.RemoveAt, intLiteral: "0"));

        Assert.Matches(@"__lws\d+\[\(__t\d+ \+ 1\)\.\.__lwc\d+\]\.CopyTo\(__lws\d+\[__t\d+\.\.\]\);", src); // shift down
        Assert.Matches(@"__lws\d+\[__lwc\d+ - 1\] = default;", src);                  // G6: vacated slot re-zeroed
        Assert.Matches(@"s\.MyList\.Count = __lwc\d+ - 1;", src);
    }

    [Fact]
    public void Clear_ZeroesUsedPrefix_G6_NoOkPin()
    {
        var asset = BuildWriteAsset(CollectionWriteOp.Clear);
        var src = Compile(asset);

        Assert.Matches(@"__lws\d+\[\.\.__lwc\d+\]\.Clear\(\);", src);                 // G6: whole prefix re-zeroed
        Assert.Contains("s.MyList.Count = 0;", src);

        // Stage0 projects NO "Ok" pin for Clear (it cannot fail).
        var lw = new ListWriteNode { Id = Guid.NewGuid(), VariableId = asset.Variables[0].Id.ToString(), Op = CollectionWriteOp.Clear };
        var probe = BlueprintAssetBuilder.Instance("P").WithVariable("MyList", typeof(int), "0").Build();
        probe.Variables[0].Type = new BlueprintTypeRef { TypeId = "System.Int32", Capacity = 4 };
        lw.VariableId = probe.Variables[0].Id.ToString();
        probe.Graphs.Add(new Graph { Id = Guid.NewGuid(), Name = "G", Kind = GraphKind.Function, Nodes = { lw } });
        Stage0_Rehydrate.Run(probe, Options());
        Assert.DoesNotContain(lw.Pins, p => p.Name == "Ok");
    }

    [Fact]
    public void Resize_ClampsToCapacity_ZeroesDroppedTail_G6()
    {
        var src = Compile(BuildWriteAsset(CollectionWriteOp.Resize, intLiteral: "1"));

        Assert.Matches(@"if \(\(uint\)__t\d+ <= \(uint\)4\)", src);                   // 0..Capacity accepted
        Assert.Matches(@"if \(__t\d+ < __lwc\d+\)", src);                             // shrink-only zeroing
        Assert.Matches(@"__lws\d+\[__t\d+\.\.__lwc\d+\]\.Clear\(\);", src);           // G6: dropped tail re-zeroed
        Assert.Matches(@"s\.MyList\.Count = __t\d+;", src);
    }

    [Fact]
    public void Add_UnwiredRequiredValue_DegradesToOkFalse_NoMutation()
    {
        // "Value" deliberately unwired -- Stage5 must degrade to the safe no-write (Ok=false
        // const), never emit a dangling operand reference.
        var src = Compile(BuildWriteAsset(CollectionWriteOp.Add, valueLiteral: null));

        Assert.DoesNotContain("__lws", src);
        // The only Count assignment left is InitDefault's InitialLength seed -- no Tick mutation.
        Assert.DoesNotContain("s.MyList.Count = __lwc", src);
        Assert.Contains("s.MyList.Count = 2;", src);
    }

    // ---- runtime round-trip (real Roslyn + ALC) -----------------------------

    private delegate void SpanAction(Span<byte> bytes);
    private delegate void TickThunkDel(
        Span<byte> bytes,
        Fdp.ModuleHost.Abstractions.ISimulationView view,
        Fdp.Interfaces.IEntityCommandBuffer ecb,
        Fdp.Core.Entity self,
        float time,
        float deltaTime,
        uint instanceVersion);

    [Fact]
    public void AddTwice_Runtime_AppendsElements_And_CountReadSeesThem()
    {
        // Entry → Add(5) → Add(7) → SetVariable(Cnt ← ItemCount over MyList) → Return, executed
        // through the REAL generated TickThunk against a state blob: proves the in-place Span
        // write lands in the state field and the LV-2 read path sees the new logical length.
        var asset = BlueprintAssetBuilder.Instance("ListWriteRuntimeBp")
            .WithVariable("MyList", typeof(int), "0")
            .WithVariable("Cnt", typeof(int), "0")
            .Build();
        asset.Variables[0].Type = new BlueprintTypeRef { TypeId = "System.Int32", Capacity = 4, InitialLength = 0 };
        var listVarId = asset.Variables[0].Id;
        var cntVarId  = asset.Variables[1].Id;

        ListWriteNode MakeAdd(string literal, out Pin execIn, out Pin execOut, out LiteralNode lit, out Pin litOut, out Pin valueIn)
        {
            execIn  = ExecPin("In", "In");
            execOut = ExecPin("Out", "Out");
            valueIn = DataPin("Value", "In", "System.Int32");
            var okOut = DataPin("Ok", "Out", "System.Boolean");
            var n = new ListWriteNode { Id = Guid.NewGuid(), VariableId = listVarId.ToString(), Op = CollectionWriteOp.Add };
            n.Pins.AddRange(new[] { execIn, execOut, valueIn, okOut });
            litOut = DataPin("Value", "Out", "System.Int32");
            lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = literal };
            lit.Pins.Add(litOut);
            return n;
        }

        var add1 = MakeAdd("5", out var a1In, out var a1Out, out var lit1, out var lit1Out, out var a1Val);
        var add2 = MakeAdd("7", out var a2In, out var a2Out, out var lit2, out var lit2Out, out var a2Val);

        var gvOut = DataPin("Value", "Out", "System.Int32", isArray: true);
        var gv = new GetVariableNode { Id = Guid.NewGuid(), VariableId = listVarId.ToString() };
        gv.Pins.Add(gvOut);
        var countCollIn = DataPin("Collection", "In", "System.Object", isArray: true);
        var countOut    = DataPin("Count", "Out", "System.Int32");
        var count = new CollectionItemCountNode
        {
            Id = Guid.NewGuid(),
            CollectionKind = CollectionKind.BlackboardFixedList,
            CollectionFieldName = "MyList",
        };
        count.Pins.AddRange(new[] { countCollIn, countOut });

        var svIn  = ExecPin("In", "In");
        var svOut = ExecPin("Out", "Out");
        var svVal = DataPin("Value", "In", "System.Int32");
        var setCnt = new SetVariableNode { Id = Guid.NewGuid(), VariableId = cntVarId.ToString() };
        setCnt.Pins.AddRange(new[] { svIn, svOut, svVal });

        var entryOut = ExecPin("Out", "Out");
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(entryOut);
        var retIn = ExecPin("In", "In");
        var ret = new ReturnNode { Id = Guid.NewGuid() };
        ret.Pins.Add(retIn);

        asset.Graphs.Add(new Graph
        {
            Id = Guid.NewGuid(), Name = "Main", Kind = GraphKind.Function,
            Nodes = { entry, add1, lit1, add2, lit2, gv, count, setCnt, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id,  FromPinId = entryOut.Id, ToNodeId = add1.Id,   ToPinId = a1In.Id },
                new Link { FromNodeId = add1.Id,   FromPinId = a1Out.Id,    ToNodeId = add2.Id,   ToPinId = a2In.Id },
                new Link { FromNodeId = add2.Id,   FromPinId = a2Out.Id,    ToNodeId = setCnt.Id, ToPinId = svIn.Id },
                new Link { FromNodeId = setCnt.Id, FromPinId = svOut.Id,    ToNodeId = ret.Id,    ToPinId = retIn.Id },
                new Link { FromNodeId = lit1.Id,   FromPinId = lit1Out.Id,  ToNodeId = add1.Id,   ToPinId = a1Val.Id },
                new Link { FromNodeId = lit2.Id,   FromPinId = lit2Out.Id,  ToNodeId = add2.Id,   ToPinId = a2Val.Id },
                new Link { FromNodeId = gv.Id,     FromPinId = gvOut.Id,    ToNodeId = count.Id,  ToPinId = countCollIn.Id },
                new Link { FromNodeId = count.Id,  FromPinId = countOut.Id, ToNodeId = setCnt.Id, ToPinId = svVal.Id },
            },
        });

        using var fixture = new BlueprintTestFixture(
            new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });
        var assembly = fixture.CompileAndLoad(asset);
        var bpClass = assembly.GetTypes().Single(t => t.Name.EndsWith("_Bp") && t.GetNestedType("State") != null);
        var state = bpClass.GetNestedType("State")!;

        int listOffset = (int)Marshal.OffsetOf(state, "MyList");
        int cntOffset  = (int)Marshal.OffsetOf(state, "Cnt");

        var bytes = new byte[Marshal.SizeOf(state) + 64];
        var init = (SpanAction)Delegate.CreateDelegate(typeof(SpanAction), bpClass.GetMethod("InitDefault")!);
        init(bytes);
        Assert.Equal(0, BitConverter.ToInt32(bytes, listOffset));                     // InitialLength 0

        var tick = (TickThunkDel)Delegate.CreateDelegate(typeof(TickThunkDel), bpClass.GetMethod("TickThunk")!);
        tick(bytes, fixture.View, fixture.Ecb, default, 0f, 0.016f, 0);

        Assert.Equal(2, BitConverter.ToInt32(bytes, listOffset));                     // Count after 2 Adds
        Assert.Equal(5, BitConverter.ToInt32(bytes, listOffset + 4));                 // Items[0]
        Assert.Equal(7, BitConverter.ToInt32(bytes, listOffset + 8));                 // Items[1]
        Assert.Equal(2, BitConverter.ToInt32(bytes, cntOffset));                      // ItemCount read-back
    }

    // ---- BP1505: write target must be a fixed-list variable -----------------

    [Fact]
    [Compiler.CoversDiagnosticCode("BP1505")]
    public void BP1505_ScalarTarget_And_UnboundInFlow_Error_UnboundLooseSilent()
    {
        // (a) bound to a SCALAR variable -> error.
        var asset = BlueprintAssetBuilder.Instance("V")
            .WithVariable("NotAList", typeof(int), "0")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        var lw = new ListWriteNode { Id = Guid.NewGuid(), VariableId = asset.Variables[0].Id.ToString(), Op = CollectionWriteOp.Add };
        asset.Graphs[0].Nodes.Add(lw);

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, Options()));
        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1505);

        // (b) UNBOUND but wired into an exec chain -> error.
        lw.VariableId = "";
        var execIn = ExecPin("In", "In");
        lw.Pins.Add(execIn);
        var entryOut = ExecPin("Out", "Out");
        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        entry.Pins.Add(entryOut);
        asset.Graphs[0].Nodes.Add(entry);
        asset.Graphs[0].Links.Add(new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = lw.Id, ToPinId = execIn.Id });

        var sink2 = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink2, Options()));
        Assert.Contains(sink2.All, d => d.Code == DiagnosticCodes.BP1505);

        // (c) UNBOUND and loose (fresh palette drop) -> silent.
        asset.Graphs[0].Links.RemoveAt(asset.Graphs[0].Links.Count - 1);
        var sink3 = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink3, Options()));
        Assert.DoesNotContain(sink3.All, d => d.Code == DiagnosticCodes.BP1505);
    }

    // ---- BP1506: list value fenced off generic pins -------------------------

    private static BlueprintAsset BuildTwoListAsset(int capB = 4, string typeB = "System.Int32")
    {
        var asset = BlueprintAssetBuilder.Instance("V")
            .WithVariable("ListA", typeof(int), "0")
            .WithVariable("ListB", typeof(int), "0")
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Variables[0].Type = new BlueprintTypeRef { TypeId = "System.Int32", Capacity = 4, InitialLength = 2 };
        asset.Variables[1].Type = new BlueprintTypeRef { TypeId = typeB, Capacity = capB, InitialLength = 0 };
        return asset;
    }

    [Fact]
    [Compiler.CoversDiagnosticCode("BP1506")]
    public void BP1506_ListWiredToGenericPin_Errors()
    {
        var asset = BuildTwoListAsset();
        var graph = asset.Graphs[0];

        var gvOut = DataPin("Value", "Out", "System.Int32", isArray: true);
        var gv = new GetVariableNode { Id = Guid.NewGuid(), VariableId = asset.Variables[0].Id.ToString() };
        gv.Pins.Add(gvOut);

        var cmpA = DataPin("A", "In", "System.Int32");
        var cmpB = DataPin("B", "In", "System.Int32");
        var cmpR = DataPin("Result", "Out", "System.Boolean");
        var cmp = new CompareNode { Id = Guid.NewGuid() };
        cmp.Pins.AddRange(new[] { cmpA, cmpB, cmpR });

        graph.Nodes.Add(gv);
        graph.Nodes.Add(cmp);
        graph.Links.Add(new Link { FromNodeId = gv.Id, FromPinId = gvOut.Id, ToNodeId = cmp.Id, ToPinId = cmpA.Id });

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, Options()));
        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1506);
    }

    [Fact]
    [Compiler.CoversDiagnosticCode("BP1506")]
    public void BP1506_ShapeMismatchedClone_Errors_CapacityAndElementType()
    {
        foreach (var (capB, typeB) in new[] { (8, "System.Int32"), (4, "System.Single") })
        {
            var asset = BuildTwoListAsset(capB, typeB);
            var graph = asset.Graphs[0];

            var gvOut = DataPin("Value", "Out", "System.Int32", isArray: true);
            var gv = new GetVariableNode { Id = Guid.NewGuid(), VariableId = asset.Variables[0].Id.ToString() };
            gv.Pins.Add(gvOut);
            var svVal = DataPin("Value", "In", "System.Int32", isArray: true);
            var sv = new SetVariableNode { Id = Guid.NewGuid(), VariableId = asset.Variables[1].Id.ToString() };
            sv.Pins.Add(svVal);
            graph.Nodes.Add(gv);
            graph.Nodes.Add(sv);
            graph.Links.Add(new Link { FromNodeId = gv.Id, FromPinId = gvOut.Id, ToNodeId = sv.Id, ToPinId = svVal.Id });

            var sink = new DiagnosticSink();
            Stage2_Validate.Run(asset, new ValidationContext(sink, Options()));
            Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1506);
        }
    }

    [Fact]
    public void WholeListClone_IdenticalShape_NoDiagnostic_EmitsFlatStructCopy()
    {
        // SetVariable(ListB ← GetVariable(ListA)) with IDENTICAL shape: the ONE whole-value
        // exception. Must pass Stage2 clean and lower to flat struct copies -- NO Span cast,
        // NO per-element loop, NO accessor call.
        var asset = BuildTwoListAsset();
        var graph = asset.Graphs[0];

        var gvOut = DataPin("Value", "Out", "System.Int32", isArray: true);
        var gv = new GetVariableNode { Id = Guid.NewGuid(), VariableId = asset.Variables[0].Id.ToString() };
        gv.Pins.Add(gvOut);
        var svIn  = ExecPin("In", "In");
        var svOut = ExecPin("Out", "Out");
        var svVal = DataPin("Value", "In", "System.Int32", isArray: true);
        var sv = new SetVariableNode { Id = Guid.NewGuid(), VariableId = asset.Variables[1].Id.ToString() };
        sv.Pins.AddRange(new[] { svIn, svOut, svVal });

        graph.Nodes.Add(gv);
        graph.Nodes.Add(sv);

        // Splice the clone into the Entry → Return exec chain built by the builder.
        var entry = graph.Nodes.OfType<EventEntryNode>().Single();
        var ret   = graph.Nodes.OfType<ReturnNode>().Single();
        var oldExec = graph.Links.Single(l => l.FromNodeId == entry.Id && l.ToNodeId == ret.Id);
        graph.Links.Remove(oldExec);
        graph.Links.Add(new Link { FromNodeId = entry.Id, FromPinId = oldExec.FromPinId, ToNodeId = sv.Id, ToPinId = svIn.Id });
        graph.Links.Add(new Link { FromNodeId = sv.Id, FromPinId = svOut.Id, ToNodeId = ret.Id, ToPinId = oldExec.ToPinId });
        graph.Links.Add(new Link { FromNodeId = gv.Id, FromPinId = gvOut.Id, ToNodeId = sv.Id, ToPinId = svVal.Id });

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, Options()));
        Assert.DoesNotContain(sink.All, d => d.Code == DiagnosticCodes.BP1506);

        var src = Compile(asset);
        Assert.Matches(@"var __t\d+ = s\.ListA;", src);                               // flat read copy
        Assert.Matches(@"s\.ListB = __t\d+;", src);                                   // flat store copy
        Assert.DoesNotContain("__lws", src);                                          // no Span write path
        Assert.DoesNotContain("for (", src.Substring(src.IndexOf("s.ListB")));        // no element loop
    }

    // ---- BP1507: no list-typed Parameters; Shared fenced by BP1506 (R5) -----

    [Fact]
    [Compiler.CoversDiagnosticCode("BP1507")]
    public void BP1507_ListTypedParameter_Rejected_NamingSupportedHomes()
    {
        // Parameters exist on AiPrimitive dispatch (Instance rejects the section outright, BP1031).
        var asset = BlueprintAssetBuilder.AiPrimitive("V")
            .WithHostings(AiPrimitiveHosting.BTreeAction)
            .WithGraph("Main", g => g.Entry().Return())
            .Build();
        asset.Parameters.Add(new ParameterDecl
        {
            Id   = Guid.NewGuid(),
            Name = "BadList",
            Type = new BlueprintTypeRef { TypeId = "System.Int32", Capacity = 4 },
        });

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, Options()));

        var d = Assert.Single(sink.All, x => x.Code == DiagnosticCodes.BP1507);
        Assert.Contains("Variable", d.Message);                // names the supported homes
        Assert.Contains("WorkingState", d.Message);

        // A scalar parameter stays clean.
        asset.Parameters[0].Type = new BlueprintTypeRef { TypeId = "System.Int32" };
        var sink2 = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink2, Options()));
        Assert.DoesNotContain(sink2.All, x => x.Code == DiagnosticCodes.BP1507);
    }

    [Fact]
    public void SharedHome_FencedAtWireLevel_ListIntoSetShared_TripsBP1506()
    {
        // R5's Shared half: there is no list-typed shared DECLARATION surface, so the fence is
        // the wire rule -- a list variable feeding SetShared's "Value" pin is not in BP1506's
        // allowlist (consumers' "Collection" / identical-shape SetVariable clone only).
        var asset = BuildTwoListAsset();
        var graph = asset.Graphs[0];

        var gvOut = DataPin("Value", "Out", "System.Int32", isArray: true);
        var gv = new GetVariableNode { Id = Guid.NewGuid(), VariableId = asset.Variables[0].Id.ToString() };
        gv.Pins.Add(gvOut);
        var ssVal = DataPin("Value", "In", "System.Int32", isArray: true);
        var ss = new SetSharedNode { Id = Guid.NewGuid(), VariableId = "sharedSlot", SharedTypeId = "System.Int32" };
        ss.Pins.Add(ssVal);
        graph.Nodes.Add(gv);
        graph.Nodes.Add(ss);
        graph.Links.Add(new Link { FromNodeId = gv.Id, FromPinId = gvOut.Id, ToNodeId = ss.Id, ToPinId = ssVal.Id });

        var sink = new DiagnosticSink();
        Stage2_Validate.Run(asset, new ValidationContext(sink, Options()));
        Assert.Contains(sink.All, d => d.Code == DiagnosticCodes.BP1506);
    }

    // ---- editor pin parity --------------------------------------------------

    [Theory]
    [InlineData(CollectionWriteOp.Add)]
    [InlineData(CollectionWriteOp.SetAt)]
    [InlineData(CollectionWriteOp.InsertAt)]
    [InlineData(CollectionWriteOp.RemoveAt)]
    [InlineData(CollectionWriteOp.Clear)]
    [InlineData(CollectionWriteOp.Resize)]
    public void ListWrite_EditorPinProjection_ParityWithStage0_AllOps(CollectionWriteOp op)
    {
        var asset = BlueprintAssetBuilder.Instance("P")
            .WithVariable("MyList", typeof(int), "0").Build();
        asset.Variables[0].Type = new BlueprintTypeRef { TypeId = "System.Int32", Capacity = 4 };
        var lw = new ListWriteNode { Id = Guid.NewGuid(), VariableId = asset.Variables[0].Id.ToString(), Op = op };

        var editorPins = NodePinSchema.GetCanonicalPins(lw, asset: asset)
            .Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId ?? "")).ToList();

        var graph = new Graph { Id = Guid.NewGuid(), Name = "G", Kind = GraphKind.Function, Nodes = { lw } };
        asset.Graphs.Add(graph);
        Stage0_Rehydrate.Run(asset, Options());
        var stage0Pins = lw.Pins
            .Select(p => (p.Name, p.Direction, p.IsExec, p.TypeRef?.TypeId ?? "")).ToList();

        Assert.Equal(editorPins, stage0Pins);
        Assert.Contains(editorPins, p => p.Item1 == "In" && p.Item3);                 // exec In
        Assert.Equal(op != CollectionWriteOp.Clear, editorPins.Any(p => p.Item1 == "Ok"));
    }
}
