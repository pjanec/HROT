using System;
using System.IO;
using System.Linq;
using Hrot.AiEditor.Persistence;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;
using Hrot.BTree.Editor.Persistence;
using Hrot.BTree.Editor.Validation;
using Hrot.AiEditor.Persistence.Hsm;
using Hrot.Hsm.Editor.Persistence;
using Hrot.Hsm.Editor.Validation;
using Hrot.Editor.AiComposition;
using Xunit;

namespace Hrot.Editor.Tests.AiComposition;

/// <summary>
/// ⭐⭐⭐ <c>CE-434</c> — the editor derives, re-derives, binds and invalidates a behaviour's resolver asset.
/// 📄 <c>Architect_Question_76</c> §12.21.
/// </summary>
public sealed class BehaviorResolverAuthoringTests
{
    private static string RepoFile(params string[] parts)
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "HROT.sln")))
            dir = Path.GetDirectoryName(dir)!;
        return Path.Combine(new[] { dir! }.Concat(parts).ToArray());
    }

    private static BehaviorTreeAssetDto LoadT40() => BTreeJsonServices.Deserialize(File.ReadAllText(RepoFile(
        "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "BTrees", "Authoring", "T40_BehaviorResolverAsset.btree.json")))!;

    private static BehaviorTreeAssetDto Behaviour(params (string Name, string Type, bool State, bool NodeScope)[] vars)
    {
        var dto = new BehaviorTreeAssetDto { AssetId = Guid.NewGuid(), Name = "Beh", TargetNamespace = "Demo.Ns" };
        dto.Blackboard.Managed = true;
        foreach (var (n, t, st, node) in vars)
            dto.Blackboard.Variables.Add(new BlackboardVariableDto
            {
                Name = n, Type = new BlackboardTypeRefDto { TypeId = t },
                Role = st ? BlackboardVariableRole.State : BlackboardVariableRole.Input,
                Scope = node ? WorkingStateScope.Node : WorkingStateScope.Behavior,
            });
        return dto;
    }

    /// <summary>
    /// ⭐⭐⭐ THE STRONG ONE: deriving a resolver from the SHIPPED <c>T40_BehaviorResolverAsset</c> yields exactly the
    /// subject the shipped <c>T40Resolver</c> declares — and that one compiles against the GENERATED types in the
    /// real build (CE-428). ⇒ the editor derives names the generator actually emits.
    /// </summary>
    [Fact]
    public void DerivingFromTheShippedBehaviour_MatchesTheShippedResolversSubject()
    {
        var derived = BehaviorResolverAuthoring.Create(LoadT40(), Guid.NewGuid()).ResolverSubject!;
        var shipped = BlueprintJsonServices.Deserialize(File.ReadAllText(RepoFile(
            "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "Blueprints", "T40Resolver.bp.json")))!.ResolverSubject!;

        Assert.Equal(shipped.BehaviorName,   derived.BehaviorName);
        Assert.Equal(shipped.BlockTypeId,    derived.BlockTypeId);
        Assert.Equal(shipped.StateVariables, derived.StateVariables);
    }

    /// <summary>⭐ Create mirrors the BLOCK: In vars + Behavior-scoped State vars, CLR type ids; a Node-scoped
    /// State var (a node-bound side slot, not the block) is excluded. ⭐ CE-443: it seeds one Parameter per Input
    /// variable (the authored input's identity shape). One empty Construction graph.</summary>
    [Fact]
    public void Create_MirrorsTheBlock_AndSeedsOneConstructionGraph()
    {
        var bp = BehaviorResolverAuthoring.Create(Behaviour(
            ("Speed", "float", false, false), ("Doubled", "float", true, false), ("Rally", "Vector3", true, true)), Guid.NewGuid());

        Assert.Equal(BlueprintDispatchKind.Library, bp.Dispatch);
        Assert.Equal("BehResolver", bp.Name);
        Assert.Equal("Demo.Ns.Beh_Block",      bp.ResolverSubject!.BlockTypeId);
        Assert.Equal(new[] { "Doubled" }, bp.ResolverSubject.StateVariables);
        var vars = bp.Declarations.Of(DeclarationKind.Variable).ToList();
        Assert.Equal(new[] { "Speed", "Doubled" }, vars.Select(v => v.Name));
        Assert.All(vars, v => Assert.Equal("System.Single", v.Type.TypeId));
        // ⭐ CE-443: the authored input starts as the identity shape — one Parameter per Input variable.
        var ps = bp.Declarations.Of(DeclarationKind.Parameter).ToList();
        Assert.Equal(new[] { "Speed" }, ps.Select(p => p.Name));
        Assert.Equal("System.Single", ps[0].Type.TypeId);
        Assert.Single(bp.Graphs, g => g.Kind == GraphKind.Construction && g.Inputs.Count == 0 && g.Outputs.Count == 0);
    }

    /// <summary>⭐⭐ Re-derive keeps the id of every variable whose name survives (so graph nodes stay bound),
    /// adds new ones, and drops removed ones.</summary>
    [Fact]
    public void Rederive_KeepsSurvivingIds_AddsAndDrops()
    {
        var bp = BehaviorResolverAuthoring.Create(Behaviour(("Speed", "float", false, false), ("Old", "int", true, false)), Guid.NewGuid());
        Guid speedId = bp.Declarations.Of(DeclarationKind.Variable).Single(v => v.Name == "Speed").Id;

        BehaviorResolverAuthoring.Rederive(bp, Behaviour(("Speed", "double", false, false), ("New", "int", true, false)));

        var vars = bp.Declarations.Of(DeclarationKind.Variable).ToList();
        Assert.Equal(new[] { "Speed", "New" }, vars.Select(v => v.Name));
        Assert.Equal(speedId, vars[0].Id);
        Assert.Equal("System.Double", vars[0].Type.TypeId);
        Assert.Equal(new[] { "New" }, bp.ResolverSubject!.StateVariables);
    }

    /// <summary>
    /// ⭐⭐ Invalidation (§12.10d ④): Bind records the shape hash; changing the behaviour's block raises
    /// <see cref="BTreeDiagnosticCode.ResolverOutOfDate"/> — a WARNING (the compile is the safety net).
    /// ⚠ A node edit does not.
    /// </summary>
    [Fact]
    public void AChangedBlock_MakesTheBindingOutOfDate_ButANodeEditDoesNot()
    {
        var dto = Behaviour(("Speed", "float", false, false));
        var model = BehaviorTreeAssetMapper.FromDto(dto);
        var bp = BehaviorResolverAuthoring.Create(dto, Guid.NewGuid());
        BehaviorResolverAuthoring.Bind(model, dto, bp);

        Assert.DoesNotContain(new BTreeValidator().Validate(model), d => d.Code == BTreeDiagnosticCode.ResolverOutOfDate);

        var changed = Behaviour(("Speed", "double", false, false));
        var changedModel = BehaviorTreeAssetMapper.FromDto(changed);
        changedModel.Resolver = model.Resolver;
        var diag = Assert.Single(new BTreeValidator().Validate(changedModel), d => d.Code == BTreeDiagnosticCode.ResolverOutOfDate);
        Assert.Equal(BTreeDiagnosticSeverity.Warning, diag.Severity);

        BehaviorResolverAuthoring.Clear(changedModel);
        Assert.DoesNotContain(new BTreeValidator().Validate(changedModel), d => d.Code == BTreeDiagnosticCode.ResolverOutOfDate);
    }

    /// <summary>⭐ Candidates are the resolver assets whose DERIVED subject names this behaviour.</summary>
    [Fact]
    public void Candidates_AreTheResolversOfThisBehaviour()
    {
        var mine  = BehaviorResolverAuthoring.Create(Behaviour(("A", "int", false, false)), Guid.NewGuid());
        var other = BehaviorResolverAuthoring.Create(Behaviour(("A", "int", false, false)), Guid.NewGuid());
        other.ResolverSubject!.BehaviorName = "SomeoneElse";
        var plain = new BlueprintAsset { Name = "Plain", Dispatch = BlueprintDispatchKind.Library };

        Assert.Equal(new[] { mine }, BehaviorResolverAuthoring.Candidates(new[] { mine, other, plain }, "Beh"));
    }

    // ── ⭐ CE-503 — the HSM binds a resolver through the SAME derivation (its BlackboardOwner view) ─────────────────────

    private static HsmAssetDto LoadHsmDemo() => HsmJsonServices.Deserialize(File.ReadAllText(RepoFile(
        "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "HSMs", "HsmResolverDemo.hsm.json")))!;

    /// <summary>
    /// ⭐⭐⭐ CE-503 — the strong one, for the HSM: deriving a resolver from the SHIPPED <c>HsmResolverDemo</c> yields exactly the
    /// subject the shipped <c>HsmResolverDemoResolver</c> declares — and that one compiles against the GENERATED HSM block in
    /// the real build. ⇒ the HSM overloads derive names the HSM generator actually emits.
    /// </summary>
    [Fact]
    public void CE503_DerivingFromTheShippedHsm_MatchesTheShippedResolversSubject()
    {
        var derived = BehaviorResolverAuthoring.Create(LoadHsmDemo(), Guid.NewGuid()).ResolverSubject!;
        var shipped = BlueprintJsonServices.Deserialize(File.ReadAllText(RepoFile(
            "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Assets", "Blueprints", "HsmResolverDemoResolver.bp.json")))!.ResolverSubject!;

        Assert.Equal(shipped.BehaviorName,   derived.BehaviorName);
        Assert.Equal(shipped.BlockTypeId,    derived.BlockTypeId);
        Assert.Equal(shipped.StateVariables, derived.StateVariables);
    }

    /// <summary>
    /// ⭐⭐ CE-503 — bind records the hash and survives an editor round-trip (an editor save must not unbind it); a changed
    /// block raises <see cref="HsmDiagnosticCode.ResolverOutOfDate"/> as a WARNING; clear removes it.
    /// <para>✅ Red-proof: drop the resolver from <c>HsmAssetMapper.ToDto</c> ⇒ the round-trip assertion reddens.</para>
    /// </summary>
    [Fact]
    public void CE503_AnHsmBinding_RoundTrips_AndGoesOutOfDate_WhenTheBlockChanges()
    {
        var dto = LoadHsmDemo();
        dto.Resolver = null;
        var model = HsmAssetMapper.FromDto(dto);
        var bp = BehaviorResolverAuthoring.Create(dto, Guid.NewGuid());
        BehaviorResolverAuthoring.Bind(model, dto, bp);

        var again = HsmAssetMapper.FromDto(HsmAssetMapper.ToDto(model));
        Assert.Equal(model.Resolver, again.Resolver);
        Assert.NotEqual(0u, again.Resolver!.ShapeHash);
        Assert.DoesNotContain(new HsmValidator().Validate(again), d => d.Code == HsmDiagnosticCode.ResolverOutOfDate);

        var changed = HsmAssetMapper.ToDto(again);
        changed.Blackboard.Variables[0].Type!.TypeId = "System.Double";
        var changedModel = HsmAssetMapper.FromDto(changed);
        var diag = Assert.Single(new HsmValidator().Validate(changedModel), d => d.Code == HsmDiagnosticCode.ResolverOutOfDate);
        Assert.Equal(HsmDiagnosticSeverity.Warning, diag.Severity);

        BehaviorResolverAuthoring.Clear(changedModel);
        Assert.DoesNotContain(new HsmValidator().Validate(changedModel), d => d.Code == HsmDiagnosticCode.ResolverOutOfDate);
    }
}
