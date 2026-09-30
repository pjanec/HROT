using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.BTree.Editor.Model;

namespace Hrot.Editor.AiComposition;

/// <summary>
/// ⭐⭐⭐ <c>CE-434</c> — the editor's half of binding a behaviour to its blueprint RESOLVER asset (shape ③):
/// create one derived from the behaviour, re-derive it when the behaviour's block changes, list the ones that
/// fit, and bind / clear the pick. 📄 <c>Architect_Question_76</c> §12.21.
///
/// <para>⭐⭐ <b>Everything a resolver asset says about the behaviour is DERIVED</b> (§12.10e) — the
/// <c>ResolverSubject</c> and the Variables come from <see cref="BehaviorResolverShape"/>, which forwards to the
/// emitter's own namers, so the editor cannot write a type name the generator will not emit.</para>
///
/// <para>⚠ UI-free on purpose: a window calls these, and the rails drive them directly.</para>
/// </summary>
public static class BehaviorResolverAuthoring
{
    /// <summary>The name a newly created resolver asset gets.</summary>
    public static string DefaultName(string behaviourName) => behaviourName + "Resolver";

    /// <summary>
    /// ⭐ Mints a resolver asset for <paramref name="behaviour"/>: a Library with a <c>ResolverSubject</c>, the
    /// block's fields as Variables, and ONE empty <c>Construction</c> graph (Entry → Return). The author then
    /// wires <c>Get</c>/<c>Set Variable</c> in it.
    /// </summary>
    public static BlueprintAsset Create(BehaviorTreeAssetDto behaviour, Guid assetId, string? name = null)
    {
        var asset = new BlueprintAsset
        {
            Header   = new Header(),
            AssetId  = assetId,
            Name     = name ?? DefaultName(behaviour.Name),
            Dispatch = BlueprintDispatchKind.Library,
            EditorMetadata = new AssetMetadata
            {
                Description = $"Resolver for behaviour '{behaviour.Name}' (CE-434/CE-443). Runs once at start INSTEAD of the "
                            + "default copy: read the authored input with Get All Parameters, write the block with Set Variable.",
                Category = "Resolver",
            },
        };
        Rederive(asset, behaviour);
        asset.Declarations.ReplaceAll(DeclarationKind.Parameter, SeedParameters(asset.AssetId, behaviour));
        asset.Graphs.Add(EmptyConstructionGraph(assetId));
        return asset;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-443</c> — the authored input a NEW resolver starts from: one Parameter per Input variable of the
    /// behaviour, with that variable's editor default — the identity shape the default copy uses. ⭐ The author
    /// then changes it (e.g. lat/lon in, metres out); <see cref="Rederive"/> never touches Parameters, because the
    /// authored shape is the resolver's own (<c>DESIGN_Parameter_Model.md</c> §P.7).
    /// </summary>
    private static List<BlueprintDeclaration> SeedParameters(Guid assetId, BehaviorTreeAssetDto behaviour)
    {
        var inputs = new Dictionary<string, BlackboardVariableDto>(StringComparer.Ordinal);
        foreach (var v in behaviour.Blackboard?.Variables ?? new List<BlackboardVariableDto>())
            if (!string.IsNullOrEmpty(v.Name)) inputs[v.Name] = v;
        var list = new List<BlueprintDeclaration>();
        foreach (var v in BehaviorResolverShape.Of(behaviour).Variables.Where(v => !v.IsState))
        {
            var d = BlueprintDeclaration.Create(DeclarationKind.Parameter,
                DeterministicIds.FromString($"resolver-param:{assetId:N}:{v.Name}"), v.Name,
                new BlueprintTypeRef { TypeId = v.ClrTypeId });
            d.AsParameterDecl!.DefaultValueJson = inputs.TryGetValue(v.Name, out var src) ? src.DefaultValueJson : null;
            list.Add(d);
        }
        return list;
    }

    /// <summary>
    /// ⭐⭐ Re-mirrors <paramref name="resolver"/> onto <paramref name="behaviour"/>'s CURRENT block: rewrites the
    /// subject, and replaces the Variables — ⭐ keeping the id of every variable whose NAME survives, so the
    /// graph's Get/Set Variable nodes stay bound. A variable the behaviour dropped is removed (its nodes then
    /// fail to bind and the compiler says so, loudly).
    /// </summary>
    public static void Rederive(BlueprintAsset resolver, BehaviorTreeAssetDto behaviour)
    {
        var shape = BehaviorResolverShape.Of(behaviour);
        resolver.ResolverSubject = new ResolverSubjectDecl
        {
            BehaviorName   = behaviour.Name,
            BlockTypeId    = shape.BlockTypeId,
            StateVariables = shape.Variables.Where(v => v.IsState).Select(v => v.Name).ToList(),
        };

        var existing = resolver.Declarations.Of(DeclarationKind.Variable).ToDictionary(d => d.Name, d => d.Id);
        var mirrored = shape.Variables.Select(v => BlueprintDeclaration.Create(
            DeclarationKind.Variable,
            existing.TryGetValue(v.Name, out var id) ? id : DeterministicIds.FromString($"resolver-var:{resolver.AssetId:N}:{v.Name}"),
            v.Name,
            new BlueprintTypeRef { TypeId = v.ClrTypeId })).ToList();
        resolver.Declarations.ReplaceAll(DeclarationKind.Variable, mirrored);
    }

    /// <summary>⭐ The resolver assets that serve <paramref name="behaviourName"/> — by their derived subject.</summary>
    public static IEnumerable<BlueprintAsset> Candidates(IEnumerable<BlueprintAsset> assets, string behaviourName)
        => assets.Where(a => a.ResolverSubject is { } s
                          && string.Equals(s.BehaviorName, behaviourName, StringComparison.Ordinal));

    /// <summary>⭐ Binds <paramref name="resolver"/> to <paramref name="model"/>, recording the current block shape.</summary>
    public static void Bind(BehaviorTreeAsset model, BehaviorTreeAssetDto modelAsDto, BlueprintAsset resolver)
        => model.Resolver = new BTreeResolverRef(resolver.AssetId, resolver.Name,
                                                 BehaviorResolverShape.Of(modelAsDto).ShapeHash);

    /// <summary>Unbinds the resolver.</summary>
    public static void Clear(BehaviorTreeAsset model) => model.Resolver = null;

    private static Graph EmptyConstructionGraph(Guid assetId)
    {
        var entry = new EventEntryNode { Id = DeterministicIds.FromString($"resolver-entry:{assetId:N}"),
                                         EditorMetadata = new NodeMetadata { X = 60, Y = 200 } };
        var ret   = new ReturnNode     { Id = DeterministicIds.FromString($"resolver-return:{assetId:N}"),
                                         EditorMetadata = new NodeMetadata { X = 600, Y = 200 } };
        var g = new Graph
        {
            Id   = DeterministicIds.FromString($"resolver-graph:{assetId:N}"),
            Name = "Resolve",
            Kind = GraphKind.Construction,
        };
        g.Nodes.Add(entry);
        g.Nodes.Add(ret);
        g.Links.Add(new Link
        {
            FromNodeId = entry.Id, FromPinId = DeterministicIds.PinId(entry.Id, "ExecOut", "Out"),
            ToNodeId   = ret.Id,   ToPinId   = DeterministicIds.PinId(ret.Id, "ExecIn", "In"),
        });
        return g;
    }
}
