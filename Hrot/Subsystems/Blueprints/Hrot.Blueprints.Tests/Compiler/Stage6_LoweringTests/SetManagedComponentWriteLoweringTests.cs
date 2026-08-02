using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// CA-06 (Slice W2, Q#16-C) -- SetComponent MANAGED whole-replace WRITE lowering. Proves the
/// Stage5_Schedule managed <c>SetComponentNode</c> branch + the new <c>IrOp_SetManagedComponent</c>
/// emit: a SINGLE guarded statement -- <c>HasManagedComponent&lt;T&gt;</c> drives both the "Written"
/// data-out and the write guard, and the write itself is a single
/// <c>ecb.SetManagedComponent&lt;T&gt;(self, value)</c> call (NEVER a per-field
/// <c>GetComponentRW</c>/<c>GetManagedComponentRW</c> mutation -- per-field managed write is
/// architect-forbidden, snapshot aliasing). Self-only by construction -- entity is always <c>self</c>
/// via <c>IrOp_Self</c>, and there is no "Target" pin on this node kind at all.
/// <para>
/// Uses a fake, never-registered FQN for the "component" -- the compiler is reflection-free by
/// construction (AN2 "trust the string"; Stage3-7 never load the type), so no real managed CLR type is
/// needed to exercise the lowering/emit shape (mirrors
/// <see cref="GetManagedComponentReadLoweringTests"/>'s rationale exactly). The "Value" pin is fed by a
/// <see cref="LiteralNode"/> typed to the same fake FQN (emits a bare <c>null</c> literal) -- standing
/// in for "a library/function call that constructs a fresh instance" per the editor's drawer
/// guidance; Stage5/StatementEmitter don't care HOW the value was produced, only that it resolves to
/// an <see cref="Hrot.Blueprints.Core.Compiler.Ir.IrValue"/>. Runs Stage3-7 directly (skips
/// Stage2_Validate, mirrors <see cref="SetComponentWriteLoweringTests"/>'s <c>Compile</c> helper) -- no
/// Roslyn/ALC needed.
/// </para>
/// </summary>
public sealed class SetManagedComponentWriteLoweringTests
{
    private const string ManagedFqn = "Hrot.Blueprints.Tests.Fixtures.FakeManagedComponentForWrite";

    private static CompileOptions DefaultOptions() => new CompileOptions(
        Mode:              CompilerMode.Debug,
        NodeRegistry:      BuiltInNodeRegistry.Instance,
        TypeRegistry:      StaticTypeRegistry.Instance,
        EngineEvents:      BuiltInEngineEventCatalog.Instance,
        ChannelCommands:   BuiltInChannelCommandCatalog.Instance,
        WaitPrimitives:    BuiltInWaitPrimitiveCatalog.Instance,
        SiblingSignatures: Array.Empty<BlueprintSignature>());

    /// <summary>Runs all stages (skipping Stage 2) and returns the generated C# source.</summary>
    private static string? Compile(BlueprintAsset asset)
    {
        var opts = DefaultOptions();
        var sink = new DiagnosticSink();
        var ctx  = new ValidationContext(sink, opts);

        asset       = Stage3_Normalize.Run(asset, ctx);
        var typed   = Stage4_TypeResolve.Run(asset, ctx);
        var ir      = Stage5_Schedule.Run(typed, ctx);
        var lowered = Stage6_Lower.Run(ir, opts.Mode, sink);
        var (source, _) = Stage7_Emit.Run(lowered, opts.Mode, sink);
        return sink.HasErrors ? null : source;
    }

    // -----------------------------------------------------------------------
    // Asset builder
    // -----------------------------------------------------------------------

    /// <summary>
    /// EventEntry -&gt; SetComponent(IsManaged=true; "Value" wired from a Literal typed to
    /// <see cref="ManagedFqn"/>; "Written" wired to SetVariable(WrittenOut)) -&gt; Return.
    /// <para>
    /// Both the Literal's out-pin and SetComponent's "Value" in-pin carry a "global::"-prefixed
    /// TypeId -- NOT the bare FQN -- mirroring <c>Stage0_Rehydrate.SharedTypePinTypeId</c>'s AN2
    /// stamping (what the REAL enrichment path would produce for this pin, see
    /// <c>EnrichSetComponentPins</c>'s managed branch). <c>StaticTypeRegistry.TryResolve</c> only
    /// accepts an arbitrary (non-catalog) FQN via that "global::" acceptance path -- an unprefixed
    /// custom FQN pin type fails to resolve (BP1500), which would make <c>Compile</c> return
    /// <c>null</c> here. <see cref="ManagedFqn"/> itself (the baked <c>ComponentTypeFqn</c>) stays
    /// unprefixed -- Stage5 builds its own <c>IrTypeRef</c> for that directly from the raw string
    /// (never through the type registry), exactly like <see cref="GetManagedComponentReadLoweringTests"/>.
    /// </para>
    /// </summary>
    private static BlueprintAsset BuildAsset(bool wireValue = true)
    {
        string stampedFqn = "global::" + ManagedFqn;

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryOut);

        var litOut = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = stampedFqn } };
        var lit    = new LiteralNode { Id = Guid.NewGuid(), TypeId = stampedFqn, ValueJson = "null" };
        lit.Pins.Add(litOut);

        var sExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true, TypeRef = new() };
        var sExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        var sValue   = new Pin { Id = Guid.NewGuid(), Name = "Value",   Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = stampedFqn } };
        var sWritten = new Pin { Id = Guid.NewGuid(), Name = "Written", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var setComp = new SetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ManagedFqn,
            IsManaged        = true,
        };
        setComp.Pins.AddRange(new[] { sExecIn, sExecOut, sValue, sWritten });

        var boolVarId = Guid.NewGuid();
        var boolVar   = new VariableDecl { Id = boolVarId, Name = "WrittenOut", Type = new BlueprintTypeRef { TypeId = "System.Boolean" } };

        var setVarExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true, TypeRef = new() };
        var setVarExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        var setVarValueIn = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var setVar = new SetVariableNode { Id = Guid.NewGuid(), VariableId = boolVarId.ToString() };
        setVar.Pins.AddRange(new[] { setVarExecIn, setVarExecOut, setVarValueIn });

        var retIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true, TypeRef = new() };
        var ret   = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success };
        ret.Pins.Add(retIn);

        var nodes = new List<Node> { entry, lit, setComp, setVar, ret };
        var links = new List<Link>
        {
            new() { FromNodeId = entry.Id,   FromPinId = entryOut.Id,     ToNodeId = setComp.Id, ToPinId = sExecIn.Id },
            new() { FromNodeId = setComp.Id, FromPinId = sExecOut.Id,     ToNodeId = setVar.Id,  ToPinId = setVarExecIn.Id },
            new() { FromNodeId = setVar.Id,  FromPinId = setVarExecOut.Id, ToNodeId = ret.Id,    ToPinId = retIn.Id },
            new() { FromNodeId = setComp.Id, FromPinId = sWritten.Id,     ToNodeId = setVar.Id,  ToPinId = setVarValueIn.Id },
        };
        if (wireValue)
            links.Add(new() { FromNodeId = lit.Id, FromPinId = litOut.Id, ToNodeId = setComp.Id, ToPinId = sValue.Id });

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = nodes, Links = links, Inputs = new(), Outputs = new(),
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "SetManagedComponentWriteTest",
            Dispatch  = AssetDispatchKind.Instance,
            Variables = new List<VariableDecl> { boolVar },
            Graphs    = { graph },
        };
    }

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Write_ValueWired_EmitsHasManagedComponentGuardedSetManagedComponent()
    {
        var source = Compile(BuildAsset(wireValue: true));
        Assert.NotNull(source);

        // Self-only: entity is always `self` (IrOp_Self), never a "Target" resolution.
        Assert.Contains(" = self;", source);

        // HasManagedComponent guards the write; the SAME check drives "Written" (single call site).
        int hasCount = System.Text.RegularExpressions.Regex.Matches(
            source!, System.Text.RegularExpressions.Regex.Escape(
                $"HasManagedComponent<global::{ManagedFqn}>")).Count;
        Assert.Equal(1, hasCount);

        // SetManagedComponent is queued exactly once, guarded by that same bool.
        int setCount = System.Text.RegularExpressions.Regex.Matches(
            source!, System.Text.RegularExpressions.Regex.Escape(
                $"SetManagedComponent<global::{ManagedFqn}>")).Count;
        Assert.Equal(1, setCount);

        // Guarded shape: `var __tN = wv.HasManagedComponent<T>(self); if (__tN) ecb.SetManagedComponent<T>(self, value);`
        Assert.Matches(
            @"var __t\d+ = \S+\.HasManagedComponent<global::" + System.Text.RegularExpressions.Regex.Escape(ManagedFqn) + @">\(__t\d+\);",
            source);
        Assert.Matches(@"if \(__t\d+\)\s*\n\s*ecb\.SetManagedComponent<global::" +
            System.Text.RegularExpressions.Regex.Escape(ManagedFqn) + @">\(__t\d+, __t\d+\);", source);

        // NEVER a per-field mutation path (that would be the architect-forbidden shape).
        Assert.DoesNotContain("GetComponentRW<", source);
        Assert.DoesNotContain("GetManagedComponentRW<", source);
    }

    [Fact]
    public void Write_ValueUnwired_EmitsGuardOnly_NoSetManagedComponentCall()
    {
        var source = Compile(BuildAsset(wireValue: false));
        Assert.NotNull(source);

        // The guard still exists (Written must still reflect HasManagedComponent)...
        Assert.Contains($"HasManagedComponent<global::{ManagedFqn}>", source);
        // ...but SetManagedComponent is never queued (nothing to write).
        Assert.DoesNotContain($"SetManagedComponent<global::{ManagedFqn}>", source);
        // No dangling "if" for the guard when there is nothing to guard.
        Assert.DoesNotMatch(@"if \(__t\d+\)\s*\n\s*ecb\.SetManagedComponent", source);
    }
}
