using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐ <b><c>U-12</c> / D4 — the store flip's own gates.</b>
///
/// <para>
/// ⛔⛔ <b>Why this file exists, stated plainly: a revert probe stayed GREEN.</b> Breaking the store's
/// grouping invariant — making <c>ReplaceWith</c> append instead of inserting at the kind's run —
/// moved neither <c>persistence-shape.txt</c> nor golden. ⭐ Not because the invariant does not matter,
/// but because <b>deserialization happens to set the three properties in the order
/// <c>Parameters, WorkingState, Variables</c></b>, which is already
/// <see cref="DeclarationList.KindOrder"/>. ⇒ appending and inserting agree on exactly the path the
/// corpus exercises, and on no other.
/// </para>
///
/// <para>
/// ⭐ <b>So the gates below drive the paths the corpus cannot.</b> A green revert probe is a finding
/// about the tests, never evidence the code was fine.
/// </para>
/// </summary>
public sealed class StoreFlipTests
{
    private static VariableDecl Var(string name) => new()
        { Id = Guid.NewGuid(), Name = name, Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

    private static ParameterDecl Param(string name) => new()
        { Id = Guid.NewGuid(), Name = name, Type = new BlueprintTypeRef { TypeId = "System.Int32" } };

    /// <summary>
    /// ⭐⭐ <b>The invariant the whole design rests on: one kind is ONE contiguous run, in
    /// <see cref="DeclarationList.KindOrder"/>, however the properties were populated.</b>
    ///
    /// <para>
    /// ⛔ Assigned here in <b>reverse</b> order — <c>Variables</c>, then <c>WorkingState</c>, then
    /// <c>Parameters</c> — because that is the one thing the 42-asset corpus never does, and therefore
    /// the one thing neither existing gate can see. ⚠ Without the invariant the three windows overlap:
    /// <c>Parameters</c> would compute a start of 0 and a count of 1 and hand back whatever sits at
    /// store[0], which is a <see cref="VariableDecl"/>.
    /// </para>
    /// </summary>
    [Fact]
    public void TheStoreStaysGroupedByKindWhateverOrderThePropertiesAreSetIn()
    {
        var asset = new BlueprintAsset { Name = "ReverseOrder" };

        asset.Variables    = new List<VariableDecl>  { Var("V0"),   Var("V1") };
        asset.WorkingState = new List<VariableDecl>  { Var("W0") };
        asset.Parameters   = new List<ParameterDecl> { Param("P0"), Param("P1") };

        Assert.Equal(new[] { "P0", "P1" }, asset.Parameters.Select(p => p.Name));
        Assert.Equal(new[] { "W0" },       asset.WorkingState.Select(w => w.Name));
        Assert.Equal(new[] { "V0", "V1" }, asset.Variables.Select(v => v.Name));

        // ⭐ And the union enumerates in storage order — which is the struct layout order.
        Assert.Equal(new[] { "P0", "P1", "W0", "V0", "V1" },
                     asset.Declarations.Select(d => d.Name));
        Assert.Equal(
            new[]
            {
                DeclarationKind.Parameter, DeclarationKind.Parameter, DeclarationKind.WorkingState,
                DeclarationKind.Variable,  DeclarationKind.Variable,
            },
            asset.Declarations.Select(d => d.Kind));
    }

    /// <summary>⭐ Same invariant, reached through the union rather than the windows: interleaved
    /// <c>Add</c>s must still land in their own run.</summary>
    [Fact]
    public void AddingThroughTheUnionInInterleavedOrderStaysGrouped()
    {
        var asset = new BlueprintAsset { Name = "Interleaved" };

        asset.Declarations.Add(BlueprintDeclaration.Create(DeclarationKind.Variable,     Guid.NewGuid(), "V0"));
        asset.Declarations.Add(BlueprintDeclaration.Create(DeclarationKind.Parameter,    Guid.NewGuid(), "P0"));
        asset.Declarations.Add(BlueprintDeclaration.Create(DeclarationKind.Variable,     Guid.NewGuid(), "V1"));
        asset.Declarations.Add(BlueprintDeclaration.Create(DeclarationKind.WorkingState, Guid.NewGuid(), "W0"));
        asset.Declarations.Add(BlueprintDeclaration.Create(DeclarationKind.Parameter,    Guid.NewGuid(), "P1"));

        Assert.Equal(new[] { "P0", "P1", "W0", "V0", "V1" }, asset.Declarations.Select(d => d.Name));
        Assert.Equal(new[] { "P0", "P1" }, asset.Parameters.Select(p => p.Name));
        Assert.Equal(new[] { "V0", "V1" }, asset.Variables.Select(v => v.Name));
    }

    /// <summary>
    /// ⭐⭐ <b>The windows are LIVE, in both directions.</b> ⛔ This is the assertion that would have
    /// caught the tempting cheap flip — three <c>List&lt;T&gt;</c> snapshots rebuilt on every get —
    /// under which every line below still compiles and every one of them is false.
    /// </summary>
    [Fact]
    public void WritingThroughAWindowIsVisibleInTheUnionAndViceVersa()
    {
        var asset = new BlueprintAsset { Name = "Live" };

        asset.Variables.Add(Var("V0"));
        Assert.Single(asset.Declarations);
        Assert.Equal("V0", asset.Declarations[0].Name);

        asset.Declarations.Add(BlueprintDeclaration.Create(DeclarationKind.Variable, Guid.NewGuid(), "V1"));
        Assert.Equal(2, asset.Variables.Count);
        Assert.Equal("V1", asset.Variables[1].Name);

        // ⭐ And the same OBJECT is reached both ways — a rename through one is seen by the other.
        Assert.Same(asset.Variables[0], asset.Declarations[0].AsVariableDecl);
        asset.Variables[0].Name = "renamed";
        Assert.Equal("renamed", asset.Declarations[0].Name);

        asset.Variables.RemoveAt(0);
        Assert.Single(asset.Declarations);
    }

    /// <summary>
    /// ⭐ <b>The store must stay invisible to the serializer</b> — the single most likely mistake in
    /// this change is someone making it <c>public</c> because "it is the model now".
    /// ⚠ <c>PersistenceShapeTests</c> catches that through the corpus baseline; this catches it by
    /// name, at the moment it happens, rather than as a wall of moved hashes.
    /// </summary>
    [Fact]
    public void TheStoreIsNotSerialized()
    {
        var prop = typeof(BlueprintAsset).GetProperty(
            "DeclarationStore",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Assert.True(prop is null,
            "BlueprintAsset.DeclarationStore is public — System.Text.Json will write it as a fourth "
            + "declaration property and every shipped asset's bytes move. It must stay internal.");

        var asset = new BlueprintAsset { Name = "StoreLeakProbe", Dispatch = BlueprintDispatchKind.Instance };
        asset.Variables.Add(Var("V"));
        Assert.DoesNotContain("DeclarationStore", BlueprintJsonServices.Serialize(asset));
    }

    /// <summary>
    /// ⭐ <b>Assignment absorbs, it does not rebind.</b> The property setter takes a detached view —
    /// which is exactly what System.Text.Json hands it — and copies it into the store, so the asset
    /// keeps one window object for its lifetime and a caller holding an earlier read still sees the
    /// new contents.
    /// </summary>
    [Fact]
    public void AssigningAWindowAbsorbsItRatherThanReplacingIt()
    {
        var asset  = new BlueprintAsset { Name = "Absorb" };
        var window = asset.Variables;

        asset.Variables = new List<VariableDecl> { Var("A"), Var("B") };

        Assert.Same(window, asset.Variables);
        Assert.Equal(new[] { "A", "B" }, window.Select(v => v.Name));

        asset.Variables = new List<VariableDecl>();
        Assert.Empty(window);
        Assert.Empty(asset.Declarations);
    }

    /// <summary>
    /// ⭐⭐ <b><c>U-2</c>'s guarantee, extended to declarations by the flip.</b> <c>Compile</c> used to
    /// share the caller's actual <c>List</c> objects; it now copies the store's entries. So a
    /// structural change inside the compiler cannot reach the asset the designer is looking at —
    /// ⚠ while the declaration OBJECTS stay shared, because Stage 4 writes resolved types back through
    /// them and the caller is meant to see that.
    /// </summary>
    [Fact]
    public void TheCompilersCopyOwnsItsContainerButSharesTheDeclarations()
    {
        var asset = new BlueprintAsset { Name = "OwnedStore", Dispatch = BlueprintDispatchKind.Instance };
        asset.Variables.Add(Var("V0"));

        var copy = new BlueprintAsset { Name = asset.Name };
        copy.DeclarationStore.AddRange(asset.DeclarationStore);   // what BlueprintCompiler.Compile does

        copy.Variables.Add(Var("AddedByCompiler"));

        Assert.Single(asset.Variables);              // ⭐ the caller does not see the structural change
        Assert.Equal(2, copy.Variables.Count);
        Assert.Same(asset.Variables[0], copy.Variables[0]);   // ⚠ but the declaration itself is shared
    }
}
