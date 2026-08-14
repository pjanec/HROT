using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// <b>U-9 / D1 — the tagged declaration and its two projections.</b>
///
/// <para>
/// ⭐⭐ <b>Pass 2 is the only gate that can see this task's failure mode</b>, which is why it was
/// written before the projections were. A member the facade forgets to carry reddens <b>nothing
/// else</b>: not the golden corpus (no shipped asset need exercise it), not the round-trip (the
/// storage is untouched either way), not the build. It is <c>BP-226</c>'s shape exactly — a
/// disagreement between two ends that the corpus happens never to put under load.
/// </para>
/// </summary>
public sealed class TaggedDeclarationTests
{
    private static PropertyInfo[] DataMembersOf(Type t)
        => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>A distinct, non-default value per member type, so "carried" is distinguishable from "reset".</summary>
    private static object Probe(Type t, int seed) => t switch
    {
        _ when t == typeof(Guid)             => Guid.Parse($"{seed:D8}-0000-0000-0000-000000000000"),
        _ when t == typeof(string)           => "probe" + seed,
        _ when t == typeof(bool)             => true,
        _ when t == typeof(BlueprintTypeRef) => new BlueprintTypeRef { TypeId = "Probe.Type" + seed },
        _ => throw new NotSupportedException(
            $"TaggedDeclarationTests has no probe value for {t.Name}. A new member type was added to a "
            + "declaration — give it one rather than skipping it, or this gate quietly stops covering it."),
    };

    // ────────────────────────────────────────────────────────────────────────
    // Pass 2 — every member is carried, in both directions, for both shapes.
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Every writable member of <see cref="BlueprintDeclaration"/> writes THROUGH to the
    /// backing <see cref="VariableDecl"/>, and reads back FROM it.</b>
    ///
    /// <para>
    /// ⚠ Both directions, because they fail differently: a forgotten setter discards the designer's
    /// edit, a forgotten getter shows them a stale value. Walking the properties means the NEXT member
    /// added is covered without anyone remembering to add a case.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(DeclarationKind.Variable)]
    [InlineData(DeclarationKind.WorkingState)]
    public void EveryMemberIsCarriedBothWays_ForAVariableBackedDeclaration(DeclarationKind kind)
    {
        var backing = new VariableDecl();
        var decl    = BlueprintDeclaration.For(kind, backing);

        var notWrittenThrough = new List<string>();
        var notReadThrough    = new List<string>();
        var seed = 1;

        foreach (var facade in DataMembersOf(typeof(BlueprintDeclaration)))
        {
            var mirror = typeof(VariableDecl).GetProperty(facade.Name);
            Assert.True(mirror is not null,
                $"BlueprintDeclaration.{facade.Name} has no counterpart on VariableDecl. Either the "
                + "facade grew state of its own — which would be a fourth storage location, the one "
                + "thing U-9 must not create — or VariableDecl lost a member.");

            // Facade → backing.
            var written = Probe(facade.PropertyType, seed++);
            facade.SetValue(decl, written);
            if (!Equals(Describe(mirror!.GetValue(backing)), Describe(written))) notWrittenThrough.Add(facade.Name);

            // Backing → facade.
            var read = Probe(mirror.PropertyType, seed++);
            mirror.SetValue(backing, read);
            if (!Equals(Describe(facade.GetValue(decl)), Describe(read))) notReadThrough.Add(facade.Name);
        }

        Assert.True(notWrittenThrough.Count == 0,
            "these members did not write through to the stored declaration:\n  "
            + string.Join("\n  ", notWrittenThrough)
            + "\n\nA facade member that does not write through accepts the edit and discards it.");
        Assert.True(notReadThrough.Count == 0,
            "these members did not read through from the stored declaration:\n  "
            + string.Join("\n  ", notReadThrough));
    }

    /// <summary>
    /// ⭐⭐ <b>The same sweep for a parameter — and the exclusions are DERIVED, not listed twice.</b>
    ///
    /// <para>
    /// ⛔ <b>The §1 ruling is option (a):</b> <c>IsEditable</c>, <c>IsExposedOnSpawn</c> and
    /// <c>Category</c> are editor-presentation members with no meaning for a call parameter, and
    /// giving <see cref="ParameterDecl"/> three new members would be a <b>persisted-shape</b> change —
    /// <c>U-10</c>'s work, not <c>U-9</c>'s. ⭐ So the drop is declared in
    /// <see cref="BlueprintDeclaration.MembersAParameterDoesNotCarry"/> and checked here against what
    /// reflection says the two backing types actually differ by. A member added to either side lands
    /// in the diff and reddens this test, rather than joining the exclusion unnoticed.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryMemberIsCarriedBothWays_ForAParameterBackedDeclaration_ExceptTheThreeDeclaredDrops()
    {
        // The exclusion, computed rather than trusted.
        var onVariable  = DataMembersOf(typeof(VariableDecl)).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var onParameter = DataMembersOf(typeof(ParameterDecl)).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var actualDrops = onVariable.Except(onParameter).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(
            BlueprintDeclaration.MembersAParameterDoesNotCarry.OrderBy(n => n, StringComparer.Ordinal).ToList(),
            actualDrops);

        // ⚠ And nothing is on ParameterDecl that VariableDecl lacks — the asymmetry runs one way only.
        Assert.Empty(onParameter.Except(onVariable));

        var backing = new ParameterDecl();
        var decl    = BlueprintDeclaration.For(backing);
        Assert.False(decl.CarriesEditorPresentation);

        var problems = new List<string>();
        var seed = 100;

        foreach (var facade in DataMembersOf(typeof(BlueprintDeclaration)))
        {
            var value = Probe(facade.PropertyType, seed++);

            if (actualDrops.Contains(facade.Name))
            {
                // ⭐ Refuses rather than swallowing — trap #5, and the U-5 precedent.
                var ex = Record.Exception(() => facade.SetValue(decl, value));
                if (ex?.InnerException is not NotSupportedException)
                    problems.Add($"{facade.Name}: expected NotSupportedException on write, got {ex?.InnerException?.GetType().Name ?? "no throw"}");
                // ⚠ Reading is not a lie: a ParameterDecl genuinely has no category, and null says so.
                var read = facade.GetValue(decl);
                if (!Equals(Describe(read), Describe(Default(facade.PropertyType))))
                    problems.Add($"{facade.Name}: expected the documented default on read, got '{Describe(read)}'");
                continue;
            }

            var mirror = typeof(ParameterDecl).GetProperty(facade.Name);
            Assert.True(mirror is not null, $"BlueprintDeclaration.{facade.Name} has no ParameterDecl counterpart.");

            facade.SetValue(decl, value);
            if (!Equals(Describe(mirror!.GetValue(backing)), Describe(value))) problems.Add(facade.Name + ": not written through");

            var back = Probe(mirror.PropertyType, seed++);
            mirror.SetValue(backing, back);
            if (!Equals(Describe(facade.GetValue(decl)), Describe(back))) problems.Add(facade.Name + ": not read through");
        }

        Assert.True(problems.Count == 0, string.Join("\n  ", problems));
    }

    private static object? Default(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

    /// <summary>BlueprintTypeRef has no value equality, so compare on what a member carries.</summary>
    private static string? Describe(object? v)
        => v is BlueprintTypeRef r ? $"TypeRef({r.TypeId},{r.IsArray},{r.Capacity},{r.InitialLength})" : v?.ToString();

    // ────────────────────────────────────────────────────────────────────────
    // The view
    // ────────────────────────────────────────────────────────────────────────

    private static BlueprintAsset ThreeKinds()
    {
        var asset = new BlueprintAsset { AssetId = Guid.NewGuid(), Name = "ViewHost" };
        asset.Parameters.Add(new ParameterDecl { Id = Guid.NewGuid(), Name = "P0" });
        asset.Parameters.Add(new ParameterDecl { Id = Guid.NewGuid(), Name = "P1" });
        asset.WorkingState.Add(new VariableDecl { Id = Guid.NewGuid(), Name = "W0" });
        asset.Variables.Add(new VariableDecl { Id = Guid.NewGuid(), Name = "V0" });
        asset.Variables.Add(new VariableDecl { Id = Guid.NewGuid(), Name = "V1" });
        return asset;
    }

    /// <summary>Storage order, and the whole union — Parameter, WorkingState, Variable.</summary>
    [Fact]
    public void TheViewEnumeratesInStorageOrder()
    {
        var asset = ThreeKinds();

        Assert.Equal(5, asset.Declarations.Count);
        Assert.Equal(new[] { "P0", "P1", "W0", "V0", "V1" }, asset.Declarations.Select(d => d.Name));
        Assert.Equal(
            new[]
            {
                DeclarationKind.Parameter, DeclarationKind.Parameter, DeclarationKind.WorkingState,
                DeclarationKind.Variable, DeclarationKind.Variable,
            },
            asset.Declarations.Select(d => d.Kind));

        Assert.Equal(new[] { "V0", "V1" }, asset.Declarations.Of(DeclarationKind.Variable).Select(d => d.Name));
    }

    /// <summary>
    /// ⭐⭐ <b>Edits through the view land in the stored list</b> — the property a materialised copy
    /// could not have, and the one <c>U-11</c>'s ~34 consumer moves depend on.
    /// </summary>
    [Fact]
    public void EditingThroughTheViewMutatesTheStoredDeclaration()
    {
        var asset = ThreeKinds();

        asset.Declarations.First(d => d.Name == "V0").Name = "Renamed";
        Assert.Equal("Renamed", asset.Variables[0].Name);

        asset.Declarations.First(d => d.Name == "P0").Tooltip = "hint";
        Assert.Equal("hint", asset.Parameters[0].Tooltip);

        // And the reverse: a change to the stored list is visible through the view immediately.
        asset.WorkingState[0].Name = "W0'";
        Assert.Equal("W0'", asset.Declarations.Of(DeclarationKind.WorkingState).Single().Name);
    }

    /// <summary>Add / Insert / Remove all land in the right underlying list.</summary>
    [Fact]
    public void AddInsertAndRemoveWriteThroughToTheRightList()
    {
        var asset = ThreeKinds();

        asset.Declarations.Add(BlueprintDeclaration.Create(DeclarationKind.Variable, Guid.NewGuid(), "V2"));
        Assert.Equal(new[] { "V0", "V1", "V2" }, asset.Variables.Select(v => v.Name));

        // ⚠ A union index inside another kind's range clamps into this one rather than changing kind.
        asset.Declarations.Insert(0, BlueprintDeclaration.Create(DeclarationKind.Variable, Guid.NewGuid(), "Vfirst"));
        Assert.Equal(new[] { "Vfirst", "V0", "V1", "V2" }, asset.Variables.Select(v => v.Name));
        Assert.Equal(2, asset.Parameters.Count);

        var removed = asset.Declarations.First(d => d.Name == "P1");
        Assert.True(asset.Declarations.Remove(removed));
        Assert.Equal(new[] { "P0" }, asset.Parameters.Select(p => p.Name));

        // Removing something already gone is false, not a throw and not a silent success.
        Assert.False(asset.Declarations.Remove(removed));
    }

    /// <summary>
    /// ⭐ <b>Identity is the stored declaration, not the facade wrapping it</b> — so
    /// <c>Contains</c>/<c>IndexOf</c>/<c>Remove</c> match a facade the caller built for itself.
    ///
    /// <para>
    /// ⚠⚠ <b><c>U-12</c> changed what makes this necessary, and the test had to change with it.</b> It
    /// used to read the same index twice and assert <c>NotSame</c>: under <c>U-9</c> the view allocated
    /// a fresh facade per read, so two reads genuinely produced two objects. Since the store flip the
    /// facades <b>are</b> the stored elements, so two reads return the same instance and that
    /// assertion became vacuously false — ⛔ <b>a test asserting a mechanism rather than the rule.</b>
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>The rule still has a live production caller, and it is now the only thing keeping it
    /// necessary:</b> <c>BlueprintDocumentFactory</c> deletes a variable with
    /// <c>Declarations.Remove(BlueprintDeclaration.For(kind, decl))</c> — a facade constructed on the
    /// spot around a decl it already holds. So that is what this test constructs, which is a stronger
    /// statement than the one it replaces.
    /// </para>
    /// </summary>
    [Fact]
    public void AFreshFacadeOverAStoredDeclarationEqualsTheStoredOne()
    {
        var asset = ThreeKinds();

        var stored = asset.Declarations[3];
        // Exactly BlueprintDocumentFactory's shape: wrap a decl the caller is already holding.
        var fresh  = BlueprintDeclaration.For(stored.Kind, stored.AsVariableDecl!);

        Assert.NotSame(stored, fresh);
        Assert.Equal(stored, fresh);
        Assert.Equal(stored.GetHashCode(), fresh.GetHashCode());
        Assert.Contains(fresh, asset.Declarations);
        Assert.Equal(3, asset.Declarations.IndexOf(fresh));

        // ⭐ And the operation that would silently no-op without it.
        Assert.True(asset.Declarations.Remove(fresh));
    }

    /// <summary>
    /// ⭐ Removing drops the id from the matching display-order list too. ⛔ A stale id is invisible
    /// until the panel next sorts, and then it silently drops a row — Batch 46 fixed the same thing in
    /// <c>BlueprintVariableSchemaSource.RemoveVariables</c>.
    /// </summary>
    [Fact]
    public void RemovingAlsoDropsTheIdFromTheDisplayOrder()
    {
        var asset = ThreeKinds();
        asset.VariableOrder = asset.Variables.Select(v => v.Id).ToList();

        var doomed = asset.Variables[1].Id;
        asset.Declarations.RemoveAt(asset.Declarations.Count - 1);

        Assert.DoesNotContain(doomed, asset.VariableOrder);
        Assert.Single(asset.VariableOrder);
    }

    /// <summary>
    /// ⭐ <b>Assigning across kinds is REFUSED, naming the reason</b> (<c>Q26-B2</c>): moving a
    /// declaration between lists changes which struct it is laid out in, so it is a move, not an edit
    /// — the same distinction <c>Q-k</c> draws for Role/Scope.
    /// </summary>
    [Fact]
    public void AssigningADeclarationOfAnotherKindIsRefused()
    {
        var asset = ThreeKinds();
        var newVariable = BlueprintDeclaration.Create(DeclarationKind.Variable, Guid.NewGuid(), "V");

        var ex = Assert.Throws<ArgumentException>(() => asset.Declarations[0] = newVariable);
        Assert.Contains("Parameter", ex.Message);

        // Same-kind assignment is a plain replacement.
        asset.Declarations[0] = BlueprintDeclaration.Create(DeclarationKind.Parameter, Guid.NewGuid(), "P0'");
        Assert.Equal("P0'", asset.Parameters[0].Name);
    }

    /// <summary>
    /// <b>U-11 — the indexed accessors the consumer sweep needs.</b> ⭐ <c>At(kind, local)</c> is the
    /// shape <c>VariableRef</c> addresses; ⛔ <c>Of(kind).ElementAt(i)</c> in the emit path would be a
    /// walk plus an iterator allocation per field lookup.
    /// </summary>
    [Fact]
    public void AtAndCountInAddressTheListRelativePosition()
    {
        var asset = ThreeKinds();

        Assert.Equal(2, asset.Declarations.CountIn(DeclarationKind.Parameter));
        Assert.Equal(1, asset.Declarations.CountIn(DeclarationKind.WorkingState));
        Assert.Equal(2, asset.Declarations.CountIn(DeclarationKind.Variable));

        Assert.Equal("P1", asset.Declarations.At(DeclarationKind.Parameter, 1).Name);
        Assert.Equal("W0", asset.Declarations.At(DeclarationKind.WorkingState, 0).Name);
        Assert.Equal("V1", asset.Declarations.At(DeclarationKind.Variable, 1).Name);

        // ⛔ Out of range throws rather than reaching into the next kind's list — which is precisely
        //    the confusion BP-226 was.
        Assert.Throws<ArgumentOutOfRangeException>(() => asset.Declarations.At(DeclarationKind.WorkingState, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => asset.Declarations.At(DeclarationKind.Variable, -1));
    }

    /// <summary>
    /// ⚠⚠ <b><c>ById</c> uses RESOLUTION order, not storage order</b> — <c>Variable</c> →
    /// <c>WorkingState</c> → <c>Parameter</c>, mirroring <c>Stage5.FindVariableRef</c>. ⛔ The two
    /// orders answer different questions, and <c>BP-226</c> is what happened when one integer answered
    /// both.
    /// </summary>
    [Fact]
    public void ByIdFollowsResolutionOrderNotStorageOrder()
    {
        Assert.Equal(
            new[] { DeclarationKind.Variable, DeclarationKind.WorkingState, DeclarationKind.Parameter },
            DeclarationList.ResolutionOrder);
        Assert.NotEqual(DeclarationList.KindOrder, DeclarationList.ResolutionOrder);

        var asset = ThreeKinds();
        var v1 = asset.Variables[1];
        Assert.Equal(v1.Name, asset.Declarations.ById(v1.Id)!.Name);
        Assert.Null(asset.Declarations.ById(Guid.NewGuid()));

        // A shared id across two kinds resolves to the Variable — the priority, stated.
        var shared = asset.Parameters[0].Id;
        asset.Variables[0].Id = shared;
        Assert.Equal(DeclarationKind.Variable, asset.Declarations.ById(shared)!.Kind);
    }

    /// <summary>Clear empties all three lists and all three display orders.</summary>
    [Fact]
    public void ClearEmptiesEveryList()
    {
        var asset = ThreeKinds();
        asset.VariableOrder = asset.Variables.Select(v => v.Id).ToList();

        asset.Declarations.Clear();

        Assert.Empty(asset.Declarations);
        Assert.Empty(asset.Parameters);
        Assert.Empty(asset.WorkingState);
        Assert.Empty(asset.Variables);
        Assert.Empty(asset.VariableOrder!);
    }

    // ────────────────────────────────────────────────────────────────────────
    // The IR bridge
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The union index is NOT the field index.</b> <c>VariableRef</c> carries a
    /// <b>list-relative</b> position (<c>U-3</c>/<c>BP-226</c>), so the bridge must hand back the local
    /// index — handing back the union index would put every AiPrimitive's working state one struct
    /// out, which is the defect <c>U-3</c> closed.
    /// </summary>
    [Fact]
    public void RefOfReturnsTheListRelativeIndex()
    {
        var asset = ThreeKinds();

        var v1 = asset.Declarations.First(d => d.Name == "V1");
        Assert.Equal(4, asset.Declarations.IndexOf(v1));       // union position

        var reference = asset.RefOf(v1);
        Assert.Equal(VariableKind.Variable, reference.Kind);
        Assert.Equal(1, reference.Index);                       // list-relative position

        Assert.Equal(v1, asset.Resolve(reference));
    }

    /// <summary>A declaration from another asset resolves to nothing rather than to index 0.</summary>
    [Fact]
    public void RefOfAForeignDeclarationIsUnresolved()
    {
        var asset = ThreeKinds();
        var alien = BlueprintDeclaration.Create(DeclarationKind.Variable, Guid.NewGuid(), "Alien");

        Assert.False(asset.RefOf(alien).IsResolved);
        Assert.Null(asset.Resolve(VariableRef.Unresolved));
    }

    /// <summary>
    /// ⭐ <b>Both enums are walked, so a member added to either arrives with a mapping or reddens
    /// here.</b> ⛔ <c>Unresolved</c> maps to nothing on purpose — it is the "nobody set this"
    /// sentinel, and giving it a list would restore the quiet-wrong-field defect.
    /// </summary>
    [Fact]
    public void TheTwoKindEnumsAreMappedTotally()
    {
        foreach (DeclarationKind k in Enum.GetValues<DeclarationKind>())
            Assert.Equal(k, k.ToVariableKind().ToDeclarationKind());

        foreach (VariableKind k in Enum.GetValues<VariableKind>())
        {
            if (k == VariableKind.Unresolved)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => k.ToDeclarationKind());
                continue;
            }
            Assert.Equal(k, k.ToDeclarationKind().ToVariableKind());
        }
    }

    /// <summary>
    /// ⭐⭐ <b>The bridge agrees with the compiler's own resolution</b> — asserted against
    /// <c>Stage5.FindVariableRef</c>'s rule rather than assumed, for all three kinds at once.
    /// ⚠ Resolution PRIORITY (Variables → WorkingState → Parameters) is not the same thing as storage
    /// order, and conflating them is what <c>BP-226</c> was.
    /// </summary>
    [Fact]
    public void EveryDeclarationRoundTripsThroughItsRef()
    {
        var asset = ThreeKinds();

        foreach (var decl in asset.Declarations)
        {
            var reference = asset.RefOf(decl);
            Assert.True(reference.IsResolved);
            Assert.Equal(decl, asset.Resolve(reference));

            var expected = decl.Kind switch
            {
                DeclarationKind.Parameter    => asset.Parameters.Select(p => (object)p).ToList(),
                DeclarationKind.WorkingState => asset.WorkingState.Select(v => (object)v).ToList(),
                _                            => asset.Variables.Select(v => (object)v).ToList(),
            };
            Assert.Same(expected[reference.Index], decl.Backing);
        }
    }
}
