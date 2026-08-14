using System.Security.Cryptography;
using System.Text;
using Hrot.Blueprints.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Golden;

/// <summary>
/// <b>U-9 / §0 — the persisted shape did not move.</b>
///
/// <para>
/// ⭐⭐ <b>This is the instrument, and it was captured BEFORE the model change.</b> <c>U-9</c>'s whole
/// external signature is <i>"nothing moved"</i>: the tagged declaration is internal, and if the tag
/// reaches JSON then <c>U-9</c> and <c>U-10</c> collapse into one task and the migrator loses its own
/// revert.
/// </para>
///
/// <para>
/// ⛔⛔ <b>Why the handoff's stated gate — <c>Serialize(Deserialize(j)) == j</c> — cannot prove that,
/// and is not even runnable.</b> Two separate problems, both measured:
/// <list type="number">
///   <item><b>It fails on the corpus before any change.</b> 41 of the 42 files are hand-authored,
///   2-space indented; <c>BlueprintJsonServices</c> sets <c>WriteIndented = false</c>. The comparison
///   fails on whitespace, never reaching the question it was asked. ⭐ <c>U-15</c> exists to fix
///   exactly this, and <c>U-10</c>'s Pass 1 already carries the correction.</item>
///   <item>⭐⭐ <b>Even canonicalised it would prove nothing about the tag.</b> Round-tripping is
///   <b>closed under a leak</b>: if the tag were written, it would also be read back, and
///   <c>Serialize(Deserialize(x)) == x</c> would still hold. A gate that passes either way is not a
///   gate.</item>
/// </list>
/// </para>
///
/// <para>
/// ⭐ <b>So the gate is a recorded baseline instead:</b> the SHA-256 of each asset's canonical
/// serialization, taken on the pre-<c>U-9</c> tree. A leaked property changes those bytes, and the
/// baseline is the only thing that can see it. <see cref="RoundTripIsStable"/> keeps the handoff's
/// intent in the form that is actually true of this corpus (stability of the canonical form), and
/// <see cref="TheTagIsNotSerializable"/> asks the question directly.
/// </para>
/// </summary>
public sealed class PersistenceShapeTests
{
    private const string BaselinePath = "Golden/persistence-shape.txt";

    private static string Canonical(string json)
        => BlueprintJsonServices.Serialize(
            BlueprintJsonServices.Deserialize(json)
            ?? throw new InvalidDataException("Deserialized null."));

    private static string Sha256(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    /// <summary>
    /// ⭐⭐ <b>The whole corpus's canonical bytes, as one baseline file.</b>
    ///
    /// <para>
    /// ⚠ <b>A hash, deliberately, and this is the one place in the golden set where that is right.</b>
    /// U-1's rule — <i>"a hash names the asset; a stored file names the LINE"</i> — buys a readable
    /// diff, and it is worth ~530 KB for emitted source that changes for many reasons. Here there is
    /// exactly ONE thing that can move these bytes during <c>U-9</c>, and it is named in the failure
    /// message. ⛔ Storing 42 canonicalised assets (~1.5 MB) to re-learn a fact the message already
    /// states would be baseline for baseline's sake. <c>U-15</c> canonicalises the corpus in the tree
    /// itself, at which point <c>git diff</c> is the readable form.
    /// </para>
    /// </summary>
    [Fact]
    public void TheCanonicalJsonOfEveryCorpusAssetIsUnchanged()
    {
        var sb = new StringBuilder();
        foreach (var file in GoldenCorpus.EnumerateFiles())
        {
            var canonical = Canonical(File.ReadAllText(file));
            sb.Append(Path.GetFileName(file))
              .Append("  ").Append(Sha256(canonical))
              .Append("  ").Append(canonical.Length)
              .Append('\n');
        }

        try
        {
            TestData.ReadOrRegenerateSnapshot(BaselinePath, sb.ToString());
        }
        catch (Exception ex)
        {
            throw new Exception(
                "⛔ The PERSISTED shape of the corpus moved.\n\n"
                + "During U-9 there is one cause worth checking first: a member of the tagged "
                + "declaration reaching JSON. BlueprintAsset.Declarations and every member of "
                + "BlueprintDeclaration must be invisible to the serializer — see §0 of the batch 48 "
                + "handoff. If the change was intended (U-10/U-15), regenerate with "
                + "BLUEPRINT_REGENERATE_SNAPSHOTS=1 and say so in the commit.\n\n" + ex.Message, ex);
        }
    }

    /// <summary>
    /// The canonical form is a FIXED POINT — <c>Serialize(Deserialize(c)) == c</c> for all 42.
    ///
    /// <para>
    /// ⚠ This is the handoff's Pass 3 in the form the corpus can actually satisfy: not against the
    /// hand-authored file (which loses on indentation alone), but against the canonical form, which is
    /// what <c>U-15</c> will write into the tree and what <c>U-10</c>'s v1→v2→v1 identity gate needs to
    /// stand on.
    /// </para>
    /// </summary>
    [Fact]
    public void RoundTripIsStable()
    {
        var unstable = new List<string>();
        foreach (var file in GoldenCorpus.EnumerateFiles())
        {
            var once  = Canonical(File.ReadAllText(file));
            var twice = Canonical(once);
            if (!string.Equals(once, twice, StringComparison.Ordinal))
                unstable.Add(Path.GetFileName(file));
        }

        Assert.True(unstable.Count == 0,
            "canonical serialization is not a fixed point for:\n  " + string.Join("\n  ", unstable));
    }

    /// <summary>
    /// ⭐⭐ <b>Batch 55 step 3 — <c>TheTagIsNotSerializable</c>, INVERTED. The tag IS the format now.</b>
    ///
    /// <para>
    /// ⛔ <b>This is the second stop marker this batch turned over, and it is worth reading as a pair
    /// with <c>V2ReaderTests.TheWriterNowEmitsV2</c>.</b> Under <c>U-9</c> the tagged view was an
    /// in-memory projection and letting it reach JSON would have collapsed <c>U-9</c> and <c>U-10</c>
    /// into one task, costing the migrator its own revert. So this asserted the opposite of what it
    /// asserts today — and it is flipped rather than deleted so the history reads as a decision.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>The <c>[JsonIgnore]</c> half did NOT flip, and still matters.</b>
    /// <c>BlueprintAsset.Declarations</c> is the model's union VIEW over the store; the v2
    /// <c>Declarations</c> array is produced by <see cref="BlueprintSchemaV2.Up"/> from the serialized
    /// v1 body. ⛔ If the view were serializable, <c>Serialize</c>'s intermediate v1 DOM would already
    /// contain a <c>Declarations</c> key, <c>IsV2</c> would report true, and <c>Up</c> would <b>throw</b>
    /// — so the attribute is now load-bearing in a second, louder way.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTagIsTheFormatNowAndTheModelsViewIsStillNotSerialized()
    {
        var prop = typeof(BlueprintAsset).GetProperty(nameof(BlueprintAsset.Declarations));
        Assert.NotNull(prop);
        Assert.NotEmpty(prop!.GetCustomAttributes(
            typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), inherit: false));

        var asset = new BlueprintAsset
        {
            AssetId = Guid.NewGuid(), Name = "TagShapeProbe", Dispatch = BlueprintDispatchKind.Instance,
            Parameters   = { new ParameterDecl { Id = Guid.NewGuid(), Name = "P" } },
            WorkingState = { new VariableDecl  { Id = Guid.NewGuid(), Name = "W" } },
            Variables    = { new VariableDecl  { Id = Guid.NewGuid(), Name = "V" } },
        };

        var json = BlueprintJsonServices.Serialize(asset);
        var dom  = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();

        // ⭐ The tag is written — once, as the v2 array — and the three v1 lists are gone.
        Assert.True(BlueprintSchemaV2.IsV2(dom));
        var declarations = dom[BlueprintSchemaV2.DeclarationsProperty]!.AsArray();
        Assert.Equal(3, declarations.Count);
        Assert.Equal(new[] { "Parameter", "WorkingState", "Variable" },
            declarations.Select(d => d![BlueprintSchemaV2.KindProperty]!.GetValue<string>()));
        foreach (var list in new[] { "Parameters", "WorkingState", "Variables" })
            Assert.False(dom.ContainsKey(list));

        // ⭐ And it still round-trips into the same three kinds — the tag is the shape, not a loss.
        var back = BlueprintJsonServices.Deserialize(json)!;
        Assert.Single(back.Parameters);
        Assert.Single(back.WorkingState);
        Assert.Single(back.Variables);
    }
}
