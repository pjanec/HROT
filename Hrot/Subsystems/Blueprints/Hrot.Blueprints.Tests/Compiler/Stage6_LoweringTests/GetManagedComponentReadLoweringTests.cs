using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Stages;
using AssetDispatchKind = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// CA-05 (Slice 1b) -- GetComponent multi-pin MANAGED read lowering. Proves the Stage5_Schedule
/// <c>GetComponentNode</c> case's managed branch (<c>gcn.IsManaged == true</c>): the component is read
/// ONCE via <c>IrOp_GetManagedComponentRO</c> (NOT <c>IrOp_GetComponentRO</c>), "Found" is driven by
/// <c>HasManagedComponent&lt;T&gt;</c> (NOT the plain <c>HasComponent&lt;T&gt;</c>), and each field
/// projects via a null-safe <c>IrOp_FieldRead</c> (<c>?.</c> + <c>?? default</c>) off that same read --
/// so a managed read is fail-safe/never-throw exactly like the pre-existing unmanaged read, even though
/// the underlying <c>GetManagedComponentRO</c> API throws when called unconditionally on a missing
/// component (see <c>IrOp_GetManagedComponentRO</c>'s doc comment).
/// <para>
/// Uses a fake, never-registered FQN for the "component" -- the compiler is reflection-free by
/// construction (AN2 "trust the string"; Stage3-7 never load the type), so no real managed CLR type is
/// needed to exercise the lowering/emit shape. Runs Stage3-7 directly (skips Stage2_Validate), mirroring
/// <see cref="GetComponentMultiPinLoweringTests"/>'s <c>Compile</c> helper.
/// </para>
/// <para>
/// Deliberately reads a PRIMITIVE field ("Health" : <c>System.Single</c>) off the managed component --
/// a managed (class) component can have primitive-typed fields too (the node-level "IsManaged" flag is
/// about the COMPONENT container, not each field's own type; see <c>ComponentFieldReflector.
/// IsManagedComponent</c> vs. the pre-existing per-field <c>IsManaged</c>). This also sidesteps
/// Stage4_TypeResolve's pre-existing BP1503 (which independently rejects declaring a Variable of a
/// managed TYPE, e.g. <c>System.String</c>) -- BP1503 is a SEPARATE, already-working guarantee this
/// batch does not re-test; see <c>V_ComponentAccessValidatorTests</c>'s BP2063 tests for the NEW
/// flow-rule this batch adds (rejecting a managed-sourced field value wired into a persisting sink).
/// </para>
/// </summary>
public sealed class GetManagedComponentReadLoweringTests
{
    private const string ManagedFqn = "Hrot.Blueprints.Tests.Fixtures.FakeManagedComponent";

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

    /// <summary>
    /// EventEntry -&gt; SetVariable(HealthOut &lt;- GetComponent.Health) -&gt; SetVariable(FoundOut &lt;-
    /// GetComponent.Found) -&gt; Return. GetComponent is a managed multi-pin node (IsManaged = true,
    /// Fields = [Health : System.Single]). Self-default (Target unwired) so <c>self</c> is the resolved
    /// entity for both the read and the Has-check.
    /// </summary>
    private static BlueprintAsset BuildAsset()
    {
        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true, TypeRef = new() };
        entry.Pins.Add(entryOut);

        var gTarget = new Pin { Id = Guid.NewGuid(), Name = "Target", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } };
        var gHealth = new Pin { Id = Guid.NewGuid(), Name = "Health", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var gFound  = new Pin { Id = Guid.NewGuid(), Name = "Found",  Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var getComp = new GetComponentNode
        {
            Id               = Guid.NewGuid(),
            ComponentTypeFqn = ManagedFqn,
            IsManaged        = true,
            Fields = new List<ComponentFieldDecl>
            {
                new ComponentFieldDecl { Name = "Health", TypeId = "System.Single" },
            },
        };
        getComp.Pins.AddRange(new[] { gTarget, gHealth, gFound });

        var healthVarId = Guid.NewGuid();
        var healthVar   = new VariableDecl { Id = healthVarId, Name = "HealthOut", Type = new BlueprintTypeRef { TypeId = "System.Single" } };
        var boolVarId   = Guid.NewGuid();
        var boolVar     = new VariableDecl { Id = boolVarId, Name = "FoundOut", Type = new BlueprintTypeRef { TypeId = "System.Boolean" } };

        var set1ExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var set1ExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true,  TypeRef = new() };
        var set1ValueIn = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single" } };
        var set1 = new SetVariableNode { Id = Guid.NewGuid(), VariableId = healthVarId.ToString() };
        set1.Pins.AddRange(new[] { set1ExecIn, set1ExecOut, set1ValueIn });

        var set2ExecIn  = new Pin { Id = Guid.NewGuid(), Name = "In",  Direction = "In",  IsExec = true,  TypeRef = new() };
        var set2ExecOut = new Pin { Id = Guid.NewGuid(), Name = "Out", Direction = "Out", IsExec = true,  TypeRef = new() };
        var set2ValueIn = new Pin { Id = Guid.NewGuid(), Name = "Value", Direction = "In", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean" } };
        var set2 = new SetVariableNode { Id = Guid.NewGuid(), VariableId = boolVarId.ToString() };
        set2.Pins.AddRange(new[] { set2ExecIn, set2ExecOut, set2ValueIn });

        var retIn = new Pin { Id = Guid.NewGuid(), Name = "In", Direction = "In", IsExec = true, TypeRef = new() };
        var ret   = new ReturnNode { Id = Guid.NewGuid(), Status = NodeStatus.Success };
        ret.Pins.Add(retIn);

        var nodes = new List<Node> { entry, getComp, set1, set2, ret };
        var links = new List<Link>
        {
            new() { FromNodeId = entry.Id,    FromPinId = entryOut.Id,     ToNodeId = set1.Id, ToPinId = set1ExecIn.Id },
            new() { FromNodeId = set1.Id,     FromPinId = set1ExecOut.Id,  ToNodeId = set2.Id, ToPinId = set2ExecIn.Id },
            new() { FromNodeId = set2.Id,     FromPinId = set2ExecOut.Id,  ToNodeId = ret.Id,  ToPinId = retIn.Id },
            new() { FromNodeId = getComp.Id,  FromPinId = gHealth.Id,      ToNodeId = set1.Id, ToPinId = set1ValueIn.Id },
            new() { FromNodeId = getComp.Id,  FromPinId = gFound.Id,       ToNodeId = set2.Id, ToPinId = set2ValueIn.Id },
        };

        var graph = new Graph
        {
            Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function,
            Nodes = nodes, Links = links, Inputs = new(), Outputs = new(),
        };

        return new BlueprintAsset
        {
            AssetId   = Guid.NewGuid(),
            Name      = "GetManagedComponentReadTest",
            Dispatch  = AssetDispatchKind.Instance,
            Variables = new List<VariableDecl> { healthVar, boolVar },
            Graphs    = { graph },
        };
    }

    [Fact]
    public void ManagedRead_EmitsGetManagedComponentRO_NotGetComponentRO()
    {
        var source = Compile(BuildAsset());
        Assert.NotNull(source);

        Assert.Contains($"GetManagedComponentRO<global::{ManagedFqn}>", source);
        Assert.DoesNotContain($"GetComponentRO<global::{ManagedFqn}>", source);
    }

    [Fact]
    public void ManagedRead_FoundUsesHasManagedComponent_NotPlainHasComponent()
    {
        var source = Compile(BuildAsset());
        Assert.NotNull(source);

        Assert.Contains($"HasManagedComponent<global::{ManagedFqn}>", source);
        // The guard on the Get itself ALSO calls HasManagedComponent (same method, different call
        // site) -- so plain (unqualified) HasComponent<T> for this FQN must never appear.
        Assert.DoesNotContain($" HasComponent<global::{ManagedFqn}>", source);
    }

    [Fact]
    public void ManagedRead_GetIsGuardedByHasManagedComponent_TernaryShape()
    {
        var source = Compile(BuildAsset());
        Assert.NotNull(source);

        // The single-statement guarded shape: HasManagedComponent<T>(e) ? GetManagedComponentRO<T>(e) : default!
        Assert.Matches(
            @"var __t\d+ = \S+\.HasManagedComponent<global::" + System.Text.RegularExpressions.Regex.Escape(ManagedFqn)
            + @">\(\S+\) \? \S+\.GetManagedComponentRO<global::" + System.Text.RegularExpressions.Regex.Escape(ManagedFqn)
            + @">\(\S+\) : default!;",
            source);
    }

    [Fact]
    public void ManagedRead_FieldProjectionIsNullSafe()
    {
        var source = Compile(BuildAsset());
        Assert.NotNull(source);

        // Field projection off the managed read must be null-conditional + "?? default", never a bare
        // member access (which would NRE if the component happened to be absent).
        Assert.Matches(@"var __t\d+ = __t\d+\?\.Health \?\? default;", source);
        Assert.DoesNotMatch(@"var __t\d+ = __t\d+\.Health;", source);
    }

    [Fact]
    public void ManagedRead_ComponentReadExactlyOnce()
    {
        var source = Compile(BuildAsset());
        Assert.NotNull(source);

        int readCount = System.Text.RegularExpressions.Regex.Matches(
            source!, System.Text.RegularExpressions.Regex.Escape(
                $"GetManagedComponentRO<global::{ManagedFqn}>")).Count;
        Assert.Equal(1, readCount);
    }
}
