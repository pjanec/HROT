using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// <b>Matrix axis 2 — WIRING ACCEPTANCE.</b> The authoring-path compile matrix (Batch 25) and the
/// edit-sequence matrix (Batch 27, axis 3) both author a graph and ask "does it compile / does it end
/// up right" — neither ever asks <i>"does the editor let me draw this wire in the first place?"</i>.
///
/// <para>
/// ⭐ <b>That blind spot is exactly how BP-203 shipped.</b> <see cref="BlueprintTypeSystem.AreCompatible"/>
/// compared raw type-id strings, so an editor-authored output typed via the alias <c>"int"</c> (what
/// <see cref="Variables.GraphSignatureEditModel"/> / the Graph Signature window writes) could never be
/// wired to a <c>LiteralInt</c> (which carries the FQN <c>"System.Int32"</c>) — every existing compile
/// test builds its graphs with matching spellings on both ends, so none of them could ever see it.
/// </para>
///
/// <para>
/// ⚠ <b>The sweep computes its expectation from <see cref="StaticTypeRegistry"/> at runtime, not from a
/// hardcoded truth table.</b> A hand-written table of "these pairs should wire" is a second copy of the
/// coercion rules living beside the first — which is the precise shape of defect BP-203 was (the
/// editor's <c>AreCompatible</c> carrying its own one-rung coercion table instead of reading the
/// compiler's 35-rung one). Any drift the sweep finds is therefore a real finding about
/// <see cref="BlueprintTypeSystem.AreCompatible"/>, not a test that needs updating.
/// </para>
/// </summary>
public sealed class AuthoringPathWiringMatrixTests
{
    // ── 1: the headline sweep — every literal kind × every offerable output type ──────────────────

    /// <summary>
    /// The literal palette kinds actually offered by
    /// <see cref="Hrot.Blueprints.Editor.NodeDrawers.BlueprintNodePaletteEntries.All"/> — bool/int/float,
    /// exactly the three the task calls for; no kind is invented. Each pairs the palette's node-kind id
    /// with the literal's OWN out-pin type (always the canonical FQN — a literal is never authored via
    /// an alias, only a declared graph output can be).
    /// </summary>
    private static readonly (string Kind, string TypeId)[] LiteralKinds =
    {
        ("LiteralBool",  BlueprintTypeSystem.Bool),
        ("LiteralInt",   BlueprintTypeSystem.Int32),
        ("LiteralFloat", BlueprintTypeSystem.Single),
    };

    /// <summary>
    /// Declared graph-output type ids spanning the offerable alphabet — deliberately BOTH an alias
    /// spelling (<c>"int"</c>, <c>"ushort"</c> — what a type picker writes) and the canonical FQN
    /// (<c>"System.Int32"</c> — what a literal, a recipe, or the compiler writes) for the numeric types,
    /// since BP-203 was exactly the gap between those two spellings.
    /// </summary>
    private static readonly string[] OutputTypeIds =
    {
        "bool", "int", "float", "ushort",
        "System.Int32", "System.Single", "System.Boolean",
    };

    /// <summary>
    /// ⭐ <b>The headline test.</b> For every (literal kind, declared output type) cell, authors the
    /// output through <see cref="AuthoringPath.AddOutput"/> (a real data-In pin on the seeded Return
    /// node — BP-126), places the literal through the sink, and asks the editor's own command path
    /// whether the wire is accepted. The verdict must match <see cref="StaticTypeRegistry"/> exactly:
    /// compatible iff the resolved FullNames are equal, a coercion rung exists, or either side resolves
    /// to <c>System.Object</c>.
    /// <para>
    /// ⚠ Every mismatch is collected and reported in <b>one</b> failure. A per-cell assert would die on
    /// the first drifted cell and hide how wide the drift actually is — the opposite of what a matrix
    /// is for.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryLiteralKind_WiredToEveryOfferableOutputType_MatchesStaticTypeRegistry()
    {
        var mismatches = new List<string>();
        int cases = 0;

        foreach (var (kind, literalTypeId) in LiteralKinds)
        {
            foreach (var outputTypeId in OutputTypeIds)
            {
                cases++;

                var doc = AuthoringPath.Open(AuthoringPath.NewAsset(
                    $"Wire_{kind}_{outputTypeId.Replace('.', '_')}", BlueprintDispatchKind.Instance));

                AuthoringPath.AddOutput(doc.Graph, "Result", outputTypeId);
                doc.Model.RebuildAndNotify();   // AddOutput's onChanged is a no-op; re-project by hand.

                var literal = AuthoringPath.AddNode(doc.Sink, doc.Graph, kind);
                var ret     = doc.Graph.Nodes.First(n => n is ReturnNode);

                var result   = AuthoringPath.TryLink(doc, literal, "Value", ret, "Result");
                var expected = ExpectedCompatible(literalTypeId, outputTypeId);

                if (result.Success != expected)
                {
                    mismatches.Add(
                        $"{kind} ({literalTypeId}) -> output typed \"{outputTypeId}\": editor said "
                        + $"Success={result.Success} (\"{result.Message}\"), StaticTypeRegistry says "
                        + $"{expected}.");
                }
            }
        }

        Assert.True(cases == LiteralKinds.Length * OutputTypeIds.Length,
            "The sweep's own case count drifted from its two input tables — a bug in this test, not a finding.");

        Assert.True(mismatches.Count == 0,
            $"{mismatches.Count} of {cases} wiring cells disagree with StaticTypeRegistry — these are "
            + "editor AreCompatible defects, not test bugs; do not edit this assertion to accommodate "
            + "them:" + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>
    /// The rule under test, read from <see cref="StaticTypeRegistry"/> — deliberately NOT a second
    /// hand-maintained table. Mirrors the three arms of <c>BlueprintTypeSystem.AreCompatible</c> that
    /// apply once both sides resolve (every id used in this file does): equal <c>FullName</c>, a
    /// coercion rung, or a <c>System.Object</c> wildcard on either side.
    /// </summary>
    private static bool ExpectedCompatible(string fromTypeId, string toTypeId)
    {
        var registry = StaticTypeRegistry.Instance;

        Assert.True(registry.TryResolve(new BlueprintTypeRef { TypeId = fromTypeId }, out var from),
            $"StaticTypeRegistry cannot resolve '{fromTypeId}' — fix this test's type list, not the rule.");
        Assert.True(registry.TryResolve(new BlueprintTypeRef { TypeId = toTypeId }, out var to),
            $"StaticTypeRegistry cannot resolve '{toTypeId}' — fix this test's type list, not the rule.");

        if (from.FullName == to.FullName) return true;
        if (registry.TryGetCoercion(from, to, out _)) return true;
        if (from.FullName == "System.Object" || to.FullName == "System.Object") return true;
        return false;
    }

    // ── 2: the specific BP-203 cell, named ────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>BP-203, called out by name.</b> An output declared as the alias <c>"int"</c> — exactly what
    /// Graph Signature writes — wired to a <c>LiteralInt</c>, which carries the FQN
    /// <c>"System.Int32"</c>. Before BP-203 the raw-string comparison in
    /// <see cref="BlueprintTypeSystem.AreCompatible"/> made these two spellings different types and the
    /// editor refused the wire; this is the one cell the user's report was actually about, on its own so
    /// the regression has an obvious name rather than being buried in the sweep above.
    /// </summary>
    [Fact]
    public void BP203_AliasIntOutput_AcceptsLiteralIntFqn()
    {
        var doc = AuthoringPath.Open(AuthoringPath.NewAsset("Wire_BP203", BlueprintDispatchKind.Instance));

        AuthoringPath.AddOutput(doc.Graph, "Result", "int");
        doc.Model.RebuildAndNotify();

        var literal = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralInt");
        var ret     = doc.Graph.Nodes.First(n => n is ReturnNode);

        var result = AuthoringPath.TryLink(doc, literal, "Value", ret, "Result");

        Assert.True(result.Success,
            "BP-203 regression: an output declared as the alias \"int\" refused a LiteralInt "
            + $"(\"System.Int32\"). Editor said: \"{result.Message}\".");
    }

    // ── 3: exec pins wire to exec pins only ───────────────────────────────────────────────────────

    /// <summary>
    /// An exec-Out may wire to an exec-In (fan-out replaces the seeded Entry -> Return exec link, same
    /// as dropping a new wire on the canvas); an exec pin may NOT wire to a data pin in either
    /// direction. <c>Branch</c> supplies both an exec-In ("In") and a data-In ("Condition",
    /// <c>System.Boolean</c>) on one node, so the two failing directions are tested against the exact
    /// same node pair as the passing one.
    /// </summary>
    [Fact]
    public void ExecOut_WiresToExecIn_ButNeverToADataPin_InEitherDirection()
    {
        var doc     = AuthoringPath.Open(AuthoringPath.NewAsset("Wire_Exec", BlueprintDispatchKind.Instance));
        var entry   = doc.Graph.Nodes.First(n => n is EventEntryNode);
        var branch  = AuthoringPath.AddNode(doc.Sink, doc.Graph, "Branch");     // exec-In "In" + data-In "Condition"
        var literal = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralBool");

        var execToExec = AuthoringPath.TryLink(doc, entry, "Out", branch, "In");
        Assert.True(execToExec.Success, $"exec-Out -> exec-In was refused: \"{execToExec.Message}\".");

        var execToData = AuthoringPath.TryLink(doc, entry, "Out", branch, "Condition");
        Assert.False(execToData.Success, "An exec-Out pin was accepted into a data-In pin.");
        Assert.False(string.IsNullOrWhiteSpace(execToData.Message),
            "The editor refused exec -> data silently.");

        var dataToExec = AuthoringPath.TryLink(doc, literal, "Value", branch, "In");
        Assert.False(dataToExec.Success, "A data-Out pin was accepted into an exec-In pin.");
        Assert.False(string.IsNullOrWhiteSpace(dataToExec.Message),
            "The editor refused data -> exec silently.");
    }

    // ── 4: a genuinely incompatible wire is refused, with a reason ───────────────────────────────────

    /// <summary>
    /// ⚠ <b>UX contract, not just a type rule.</b> Bool has no coercion rung to Int32 anywhere in
    /// <see cref="StaticTypeRegistry"/>'s coercion table, so wiring a <c>LiteralBool</c> into an
    /// <c>int</c>-typed output must be refused — and refused <b>with a reason</b>. A silent refusal
    /// (<c>Success == false</c>, <c>Message == null</c>) is indistinguishable from a bug: the designer's
    /// drag just stops working with no explanation.
    /// </summary>
    [Fact]
    public void LiteralBool_IntoAnIntTypedOutput_IsRefused_WithANonEmptyReason()
    {
        var doc = AuthoringPath.Open(AuthoringPath.NewAsset("Wire_Incompatible", BlueprintDispatchKind.Instance));

        AuthoringPath.AddOutput(doc.Graph, "Result", "int");
        doc.Model.RebuildAndNotify();

        var literal = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralBool");
        var ret     = doc.Graph.Nodes.First(n => n is ReturnNode);

        var result = AuthoringPath.TryLink(doc, literal, "Value", ret, "Result");

        Assert.False(result.Success, "LiteralBool wired into an int-typed output was accepted.");
        Assert.False(string.IsNullOrWhiteSpace(result.Message),
            "The editor refused the wire silently — a designer sees a rejected drag with no reason.");
    }
}
