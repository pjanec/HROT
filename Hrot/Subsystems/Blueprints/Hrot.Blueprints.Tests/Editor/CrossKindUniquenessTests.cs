using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// <b>U-14 / <c>BP-232</c> — one name space across all three declaration kinds.</b>
///
/// <para>
/// ⛔ <b>The defect:</b> <c>IsDuplicateVariableName</c> — the single chokepoint for creating <i>and</i>
/// renaming, and the predicate the create modal gates its Confirm button on — checked
/// <c>asset.Variables</c> <b>only</b>. ⇒ a <c>Parameter</c> and a <c>Variable</c> could both be
/// <c>Health</c>, with nothing objecting.
/// </para>
///
/// <para>
/// ⚠ <b>Reachable, not theoretical.</b> <c>Stage5.FindVariableRef</c>'s <b>name fallback</b> searches
/// <c>Variables → WorkingState → Parameters</c>, so which <c>Health</c> a name-carrying node reaches
/// is decided by <b>list order</b>. ⭐ <c>U-3</c> made the resolution explicit; <c>U-14</c> removes the
/// ambiguity at its source.
/// </para>
///
/// <para>
/// ⭐⭐ <b>This is also the cheap tell that <c>U-9</c> landed well.</b> The rule is now
/// <c>asset.Declarations</c> — <b>one</b> collection instead of three. If cross-kind uniqueness were
/// still awkward here, the projection would be hiding the model rather than presenting it.
/// </para>
/// </summary>
public sealed class CrossKindUniquenessTests
{
    private static BlueprintAsset AssetWith(DeclarationKind kind, string name)
    {
        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "UniquenessHost",
            Dispatch = BlueprintDispatchKind.Instance,
        };
        asset.Declarations.Add(BlueprintDeclaration.Create(kind, Guid.NewGuid(), name,
            new BlueprintTypeRef { TypeId = "System.Single" }));
        return asset;
    }

    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Pass — a name taken by ANY kind is taken.</b> ⛔ Before <c>U-14</c> only the
    /// <c>Variable</c> row was red; the other two compiled clean.
    /// </summary>
    [Theory]
    // ⭐ Batch 86 — RESTATED, not narrowed: the theory still covers EVERY member of
    //   DeclarationKind. There are two now (R-01), and the retired row was a duplicate of Variable.
    [InlineData(DeclarationKind.Parameter)]
    [InlineData(DeclarationKind.Variable)]
    public void ANameTakenByAnyKindIsRefused(DeclarationKind kind)
    {
        var asset = AssetWith(kind, "Health");

        Assert.True(BlueprintDocumentFactory.IsDuplicateVariableName(asset, "Health"));
        Assert.True(BlueprintDocumentFactory.IsDuplicateVariableName(asset, "health"));
        Assert.True(BlueprintDocumentFactory.IsDuplicateVariableName(asset, "  HEALTH  "));
        Assert.False(BlueprintDocumentFactory.IsDuplicateVariableName(asset, "Ammo"));
    }

    /// <summary>The create path refuses rather than producing a second <c>Health</c>.</summary>
    [Theory]
    // ⭐ Batch 86 — "another kind" than Variable is now exactly Parameter (R-01).
    [InlineData(DeclarationKind.Parameter)]
    public void CreateVariableIsRefusedWhenAnotherKindHasTheName(DeclarationKind kind)
    {
        var asset  = AssetWith(kind, "Health");
        var before = asset.Declarations.Count;

        var created = BlueprintDocumentFactory.CreateVariable(asset, "Health", "System.Single");

        Assert.Null(created);
        Assert.Equal(before, asset.Declarations.Count);
    }

    /// <summary>
    /// ⭐ And the RENAME path too — the same chokepoint serves both, so a fix that reached only the
    /// create path would leave the identical collision one gesture away.
    /// </summary>
    [Fact]
    public void RenamingAVariableOntoAParametersNameIsRefused()
    {
        var asset = AssetWith(DeclarationKind.Parameter, "Health");
        var variable = BlueprintDeclaration.Create(
            DeclarationKind.Variable, Guid.NewGuid(), "Ammo", new BlueprintTypeRef { TypeId = "System.Int32" });
        asset.Declarations.Add(variable);

        var renamed = BlueprintDocumentFactory.RenameItem(asset, "var:" + variable.Id, "Health");

        Assert.False(renamed);
        Assert.Equal("Ammo", asset.Variables[0].Name);
    }

    /// <summary>
    /// ⭐ The uniquifier steps over the other kinds too. ⛔ A refusal that is enforced on create but not
    /// respected by auto-naming produces a name the same rule would have rejected.
    /// </summary>
    [Fact]
    public void TheAutoNamerSkipsNamesTakenByAnotherKind()
    {
        var asset = AssetWith(DeclarationKind.Parameter, "NewVar");

        var created = BlueprintDocumentFactory.AddVariable(asset);

        Assert.NotEqual("NewVar", created.Name);
        // ⚠ NOT `IsDuplicateVariableName(created.Name) == false` — the new declaration is in the asset
        //   by now, so it duplicates itself and the predicate is true by construction. The question is
        //   whether the name is unique ACROSS the asset, which is what counting it asks.
        Assert.Single(asset.Declarations,
            d => string.Equals(d.Name, created.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// ⭐⭐ <b>Graph LOCALS stay outside the rule — <c>Q27-C1</c>, and Batch 48 ruled it binding.</b>
    ///
    /// <para>
    /// ⛔ A local may <b>legally shadow</b> an asset variable; they live in disjoint spaces and resolve
    /// as disjoint IR ops. Folding locals into this rule would point it at a space where a duplicate
    /// name is the <i>feature</i>. ⚠ Asserted rather than left to a comment, because the tempting
    /// "unify everything" edit is one line.
    /// </para>
    /// </summary>
    [Fact]
    public void AGraphLocalDoesNotBlockAnAssetVariableOfTheSameName()
    {
        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "ShadowHost", Dispatch = BlueprintDispatchKind.Instance,
        };
        var graph = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function };
        graph.LocalVariables.Add(new VariableDecl
        {
            Id = Guid.NewGuid(), Name = "Health", Type = new BlueprintTypeRef { TypeId = "System.Single" },
        });
        asset.Graphs.Add(graph);

        Assert.False(BlueprintDocumentFactory.IsDuplicateVariableName(asset, "Health"));
        Assert.NotNull(BlueprintDocumentFactory.CreateVariable(asset, "Health", "System.Single"));
    }
}
