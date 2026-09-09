using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Compiler;

/// <summary>
/// ⭐⭐ <b>Q31-A — the one seam the extracted schema assembly cannot type-check for itself.</b>
///
/// <para>
/// <c>BlueprintSchemaV2</c> now lives in <c>Hrot.Blueprints.Schema</c>, a leaf assembly whose whole
/// dependency surface is <c>System.Text.Json</c>. ⛔ <b>It therefore cannot see
/// <see cref="DeclarationKind"/></b> — referencing <c>Hrot.Blueprints.Compiler</c> is exactly the
/// cycle the extraction exists to break, because <c>Hrot.Common</c> must also reach the transform.
/// </para>
///
/// <para>
/// ⇒ ⭐ <b>The three v2 tags are duplicated, deliberately — and pinned here.</b> This is the same shape
/// as <c>BlueprintDeclaration.MembersAParameterDoesNotCarry</c>: a hand-written list cross-checked by
/// reflection so it cannot drift in <b>either</b> direction — a member added to the enum without a tag
/// reddens, and a tag added without a member reddens too.
/// </para>
///
/// <para>
/// ⚠⚠ <b>ORDER is asserted, not just membership, and that is the load-bearing half.</b>
/// <c>BlueprintSchemaV2</c>'s tag order is the on-disk concatenation order, which is
/// <c>DeclarationList.KindOrder</c>, which is the struct layout order — <c>Params</c> @0, working
/// state @8, <c>State</c> @16. ⛔ <b>A reorder that kept the same three strings would silently move
/// every declaration between structs.</b>
/// </para>
/// </summary>
public sealed class DeclarationTagsMatchDeclarationKindTests
{
    /// <summary>
    /// ⭐ <b>Proved to bite (Batch 55):</b> appending a fourth member to <see cref="DeclarationKind"/>
    /// in a scratch run reddens this naming the member, and swapping two members reddens it naming the
    /// positions. ⛔ A reflection test that has never failed is not a test.
    ///
    /// <para>⭐⭐⭐ <b>Batch 86 — RESTATED from an EQUALITY to a TOTAL MAPPING</b>, which is the
    /// handoff's ruling (c). 📌 <c>R-01</c> retires <c>WorkingState</c> as a <b>kind</b>; it does NOT
    /// retire it as an <b>on-disk tag</b> — every shipped v1 file has a <c>WorkingState</c> list and
    /// <c>Down</c> must keep reproducing it, so the tag stays readable forever. ⇒ ⛔ the two sets can
    /// no longer be equal, and demanding equality would have forced a file-format break to satisfy a
    /// test. ⭐ What is still checkable, and is the property that actually matters:
    /// <list type="number">
    ///   <item>every KIND has a tag — a kind with no tag cannot be written at all;</item>
    ///   <item>every TAG maps to a kind — a tag with no kind is a variable lost on load, which is a
    ///   field missing from the emitted struct;</item>
    ///   <item>the tag order still agrees with <see cref="DeclarationList.KindOrder"/> on the kinds
    ///   they share — ⛔ that is the half that moves declarations between structs.</item>
    /// </list></para>
    /// </summary>
    [Fact]
    public void EveryV2TagMapsToADeclarationKind_AndEveryKindHasATag()
    {
        var kinds = Enum.GetNames(typeof(DeclarationKind));
        var tags  = BlueprintSchemaV2.DeclarationTags;

        // ① every kind is writable.
        foreach (var kind in kinds)
            Assert.True(tags.Contains(kind),
                $"DeclarationKind.{kind} has no v2 tag in BlueprintSchemaV2.DeclarationTags — a kind "
                + "with no tag cannot be written to disk at all.");

        // ② every tag is readable. ⚠ A RETIRED tag maps to the kind that absorbed it, not to nothing.
        var retired = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // R-01 — "Variable ≡ WorkingState. Two names, ONE concept."
            ["WorkingState"] = nameof(DeclarationKind.Variable),
        };
        foreach (var tag in tags)
            Assert.True(kinds.Contains(tag) || retired.ContainsKey(tag),
                $"BlueprintSchemaV2 writes the tag '{tag}', which names no DeclarationKind and is not "
                + "listed as a retired tag. A tag with no kind is a declaration silently dropped on "
                + "load — a field missing from the emitted struct.");

        // ③ order agrees on the kinds both sides carry. 🔴 This is the struct-layout half.
        var kindOrder = DeclarationList.KindOrder.Select(k => k.ToString()).ToArray();
        Assert.Equal(kindOrder, tags.Where(kindOrder.Contains).ToArray());

        // ④ and a retired tag sits where the kind that absorbed it sits, so a v1 file's working-state
        //    entries still concatenate BEFORE that kind's own entries (R-24).
        foreach (var (tag, absorber) in retired.Select(kv => (kv.Key, kv.Value)))
            Assert.True(tags.ToList().IndexOf(tag) <= tags.ToList().IndexOf(absorber),
                $"The retired tag '{tag}' must not sort after '{absorber}' — its declarations lead the "
                + "run, and reordering them changes every following field's offset.");
    }

    /// <summary>
    /// ⭐ And the same pin from the other side — the <b>list property names</b> the transform writes
    /// must be the ones <c>BlueprintAsset</c> actually serializes. ⚠ The tags cover the v2 half; this
    /// covers the v1 half, which <c>Down</c> has to reproduce byte-exactly.
    /// </summary>
    [Fact]
    public void TheV1ListNamesAreTheOnesTheAssetSerializes()
    {
        foreach (var name in new[] { "Parameters", "WorkingState", "Variables" })
            Assert.True(typeof(BlueprintAsset).GetProperty(name) is not null,
                $"BlueprintSchemaV2 writes a v1 list called '{name}', but BlueprintAsset has no such "
                + "property — the transform and the model have drifted.");
    }

    /// <summary>
    /// ⛔⛔ <b>The extraction's own invariant: the schema assembly must stay a LEAF.</b>
    ///
    /// <para>
    /// ⭐ The moment it gains a project reference it becomes a candidate for the cycle it was created
    /// to avoid — and the failure would not be a compile error, it would be <c>Hrot.Common</c> quietly
    /// becoming unable to reference it. ⚠ Asserted on the loaded assembly's references rather than on
    /// the <c>.csproj</c>, because that is what actually ships.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSchemaAssemblyDependsOnNothingButTheBcl()
    {
        var refs = typeof(BlueprintSchemaV2).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => !n.StartsWith("System.", StringComparison.Ordinal)
                     && !string.Equals(n, "netstandard", StringComparison.Ordinal)
                     && !string.Equals(n, "mscorlib", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(refs.Count == 0,
            "Hrot.Blueprints.Schema must reference nothing but the BCL — it is the leaf that lets "
            + "Hrot.Common reach the transform without a cycle. It now references:\n  "
            + string.Join("\n  ", refs));
    }
}
