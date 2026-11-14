using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// <b>Batch 27, matrix axis 3 — EDIT SEQUENCES, not final states.</b>
///
/// <para>
/// ⭐ <b>Why this file exists, in one sentence:</b> the Batch-25 matrix authors a graph, compiles it and
/// asserts on the result — it can only ever see a <i>final state</i>, and <b>every defect the user found
/// this round lived in a sequence</b>: type a format, <i>change</i> it, reload. A static matrix cannot
/// see those no matter how many cells it has.
/// </para>
///
/// <para>
/// The sequences here are the ones a designer actually performs, and each reproduces a specific field
/// report:
/// <list type="bullet">
///   <item><b>BP-202</b> — place, set a format, wire the derived pin, <b>rename the placeholder</b>.
///     The link to the pin that no longer exists survives into the saved file ⇒
///     <c>BP1602: Link references unknown ToPinId …</c>, which fails the whole solution build while
///     naming two GUIDs and no asset.</item>
///   <item><b>BP-201</b> — wire a typed value into an argument pin and check the <b>value</b> is
///     declared, not merely that the wire was accepted.</item>
///   <item><b>BP-204</b> — type a format character by character; the undo history must gain <b>one</b>
///     entry, not one per keystroke.</item>
///   <item><b>BP-208</b> — a node whose in-memory pin list was frozen at placement must still re-derive
///     its pins when the property they come from changes.</item>
/// </list>
/// </para>
///
/// <para>
/// ⚠ <b>These run through the same <see cref="AuthoringPath"/> as the compile matrix</b>, including its
/// Details sessions (resolved from the real drawer registry) and its save path
/// (<c>SaveActiveBlueprintCommand</c>, which canonicalizes link endpoints). Reaching for the node
/// objects directly would test the model and miss the editor, which is how all four of these shipped.
/// </para>
/// </summary>
public sealed class AuthoringPathEditSequenceTests
{
    // ── BP-202: renaming a placeholder must not leave a dangling link ─────────────────────────────

    /// <summary>
    /// ⭐ <b>The field report, reproduced end to end.</b> Renaming <c>{Threat}</c> to <c>{threat}</c>
    /// does not rename a pin — a pin's identity is <c>DeterministicIds.PinId(node, name, direction)</c>,
    /// so the rename <b>destroys one pin and creates another</b> while the link keeps pointing at the
    /// one that is gone.
    ///
    /// <para>
    /// ⚠ <b>What a dangling link actually does, measured rather than assumed.</b> It does <i>not</i>
    /// reliably raise <c>BP1602</c>: <c>Stage0_Rehydrate.AssignLinkGuids</c> binds a link whose GUID
    /// matches no pin <b>positionally within its direction bucket</b>, and a Print String's exec-In and
    /// data-In pins share that bucket. So the stale data link <b>captures the exec-In pin</b>, the node
    /// is never reached, and <c>Stage3</c> eliminates it as an orphan — the graph silently stops
    /// printing, with no error. ⭐ That is worse than the build break the user reported, not better:
    /// <c>BP1602</c> at least names something. It is also exactly *"the Print String LOST the pins"*.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>So the assertion is orphan-elimination, not merely "compiles".</b> An earlier draft of this
    /// test asserted only <c>result.Clean</c> and <b>stayed green with the fix reverted</b> — the
    /// dangling link produced warnings, not errors. A test that cannot fail is worse than no test,
    /// because it reads as coverage.
    /// </para>
    /// </summary>
    [Fact]
    public void RenamingAPlaceholder_AfterWiringIt_LeavesThePrintNodeReachable()
    {
        var (doc, print, _) = PrintStringWiredToAnIntLiteral("Seq_Rename", "{Threat}", "Threat");

        // The designer renames the placeholder — the whole point of the sequence.
        var session = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
        session.SetFormatForTest("{threat}");
        doc.Model.RebuildAndNotify();

        var result = AuthoringPath.Generate(doc.Asset);

        Assert.True(result.Clean, $"Compile failed:{Environment.NewLine}{result.Report()}");

        var orphaned = result.GeneratorDiagnostics
            .Where(d => d.Id == "BP3010" && d.GetMessage().Contains(print.Id.ToString()))
            .ToList();

        Assert.True(orphaned.Count == 0,
            "The Print String was eliminated as an orphan after a placeholder rename. Its exec wiring "
            + "was intact; what unhooked it was the stale data link binding positionally onto the "
            + $"exec-In pin:{Environment.NewLine}"
            + string.Join(Environment.NewLine, orphaned.Select(d => "  " + d.GetMessage())));
    }

    /// <summary>
    /// The same sequence, asserted structurally: no link on the graph may address a pin the Print String
    /// no longer has. ⚠ Kept beside the compile assertion deliberately — the compile proves the symptom
    /// is gone, this proves the graph is actually consistent rather than merely compiling.
    /// </summary>
    [Fact]
    public void RenamingAPlaceholder_PrunesOnlyTheLinkWhosePinVanished()
    {
        var (doc, print, literal) = PrintStringWiredToAnIntLiteral("Seq_Prune", "{Threat}", "Threat");

        int othersBefore = doc.Graph.Links.Count - WiresBetween(doc, literal, print);
        Assert.Equal(1, WiresBetween(doc, literal, print));

        var session = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
        session.SetFormatForTest("{threat}");

        Assert.Equal(0, WiresBetween(doc, literal, print));
        Assert.Contains(literal, doc.Graph.Nodes);   // the node is untouched; only the wire went

        // ⚠ Every other wire on the graph — the Print String's own exec pair, and whatever the new-graph
        // seeding left — must survive. A prune that took them too would still pass the two assertions
        // above, and would silently unhook the node it was meant to repair.
        Assert.Equal(othersBefore, doc.Graph.Links.Count);
    }

    /// <summary>
    /// ⭐ <b>Undo must put the wire back.</b> Pruning inside the undo record is the whole reason the
    /// prune happens where it does: a designer who renames by accident presses Ctrl+Z and expects the
    /// graph they had, wire included.
    /// </summary>
    [Fact]
    public void UndoingTheRename_RestoresThePrunedLink()
    {
        var (doc, print, literal) = PrintStringWiredToAnIntLiteral("Seq_Undo", "{Threat}", "Threat");

        var session = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
        session.SetFormatForTest("{threat}");
        Assert.Equal(0, WiresBetween(doc, literal, print));

        doc.History.Undo();

        Assert.Equal(1, WiresBetween(doc, literal, print));
        Assert.Equal("{Threat}", ((PrintStringNode)print).Format);

        // …and redo prunes it again, rather than leaving a duplicate or a dangling one.
        doc.History.Redo();
        Assert.Equal(0, WiresBetween(doc, literal, print));
    }

    /// <summary>
    /// A rename that keeps the placeholder name must keep the wire. ⚠ The negative control: a prune
    /// implementation that simply drops every link on any format edit would pass every test above.
    /// </summary>
    [Fact]
    public void EditingTheLiteralTextAroundAPlaceholder_KeepsTheWire()
    {
        var (doc, print, literal) = PrintStringWiredToAnIntLiteral("Seq_Keep", "{Threat}", "Threat");

        var session = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
        session.SetFormatForTest("threat level is {Threat} now");

        Assert.Equal(1, WiresBetween(doc, literal, print));
    }

    // ── BP-204: one undo entry per gesture, not per keystroke ─────────────────────────────────────

    /// <summary>
    /// ⭐ <b>BP-204 — *"undo was going back each typed char"*.</b> The designer types
    /// <c>{Threat}</c>; nine keystrokes reach the widget, and Ctrl+Z must walk back <b>one</b> step.
    ///
    /// <para>
    /// ⚠ <b>This test is also what proves BP-202's prune is in the right place.</b> Typing
    /// <c>{Threat}</c> passes through <c>{T</c>, <c>{Th</c>, <c>{Thr</c> … — each a <i>different</i>
    /// placeholder and so a different derived pin. Had the prune been per keystroke it would have
    /// deleted the wire on the first character of an edit that restores the very same pin, and the two
    /// fixes would have silently fought each other.
    /// </para>
    /// </summary>
    [Fact]
    public void TypingAFormatCharacterByCharacter_RecordsOneUndoEntry_AndKeepsAnUnaffectedWire()
    {
        var (doc, print, literal) = PrintStringWiredToAnIntLiteral("Seq_Coalesce", "{Threat}", "Threat");

        int before = doc.History.Count;

        // One gesture: the widget mutates live per keystroke, then commits once on deactivate.
        var session   = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
        var baseline  = ((PrintStringNode)print).Format;
        const string typed = "hits={Threat}";
        for (int i = 1; i <= typed.Length; i++)
            session.LiveFormatForTest(typed.Substring(0, i));
        session.CommitFormatForTest(baseline);

        Assert.Equal(before + 1, doc.History.Count);
        Assert.Equal(typed, ((PrintStringNode)print).Format);

        // The placeholder survived the gesture (it was mangled at every intermediate keystroke), so
        // its wire must survive too.
        Assert.Equal(1, WiresBetween(doc, literal, print));
    }

    // ── BP-201: the editor must record the argument's declared type ───────────────────────────────

    /// <summary>
    /// ⭐ <b>BP-201 — *"every second I got `[AI.Behavior.Blueprint] 0` — the value NOT following the
    /// Count variable"*.</b> <c>ArgTypes</c> is what types the derived pin, and
    /// <c>grep -rn "ArgTypes" Hrot.Blueprints.Editor/</c> returned <b>nothing</b>: the editor never
    /// wrote it, so every argument pin stayed <c>System.Object</c>.
    ///
    /// <para>
    /// ⚠ <b>Second instance of BP-116's shape in three batches</b> — a node property the compiler needs
    /// that the editor never populates. Wiring is the moment the designer expresses the type, so it is
    /// the moment to record it.
    /// </para>
    /// </summary>
    [Fact]
    public void WiringAnIntLiteralIntoAnArgumentPin_DeclaresThatArgumentsType()
    {
        var (_, print, _) = PrintStringWiredToAnIntLiteral("Seq_ArgType", "{Threat}", "Threat");

        var node = (PrintStringNode)print;
        Assert.True(node.ArgTypes.ContainsKey("Threat"),
            "The editor accepted the wire and never recorded what type it carried, so the pin stays "
            + "System.Object and the printed value is a default.");
        Assert.Equal("System.Int32", node.ArgTypes["Threat"]);
    }

    /// <summary>
    /// ⚠ An explicitly chosen type outranks an inference: a later wire must never silently retype a pin
    /// the designer typed by hand. Silently changing a declared type under a wire is a wrong-values
    /// change nobody made.
    /// </summary>
    [Fact]
    public void AnExplicitlyDeclaredArgumentType_IsNotOverwrittenByAWire()
    {
        var doc   = AuthoringPath.Open(AuthoringPath.NewAsset("Seq_ArgKeep", BlueprintDispatchKind.Instance));
        var print = AuthoringPath.AddNode(doc.Sink, doc.Graph, "PrintString");

        var session = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
        session.SetFormatForTest("{Threat}");
        session.SetArgTypeForTest("Threat", "System.Single");
        doc.Model.RebuildAndNotify();

        var literal = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralInt");
        AuthoringPath.Link(doc, literal, "Value", print, "Threat");

        Assert.Equal("System.Single", ((PrintStringNode)print).ArgTypes["Threat"]);
    }

    /// <summary>
    /// The declared type must reach the projected pin — recording it in <c>ArgTypes</c> and leaving the
    /// pin untyped would fix nothing the designer can see.
    /// </summary>
    [Fact]
    public void DeclaringAnArgumentType_RetypesTheProjectedPin()
    {
        var doc   = AuthoringPath.Open(AuthoringPath.NewAsset("Seq_Retype", BlueprintDispatchKind.Instance));
        var print = AuthoringPath.AddNode(doc.Sink, doc.Graph, "PrintString");

        var session = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
        session.SetFormatForTest("{Threat}");
        doc.Model.RebuildAndNotify();
        Assert.Equal("System.Object", AuthoringPath.Pin(doc, print, "Threat").Type?.Id);

        session.SetArgTypeForTest("Threat", "System.Int32");
        doc.Model.RebuildAndNotify();
        Assert.Equal("System.Int32", AuthoringPath.Pin(doc, print, "Threat").Type?.Id);
    }

    // ── BP-208: a frozen in-memory pin list must not shadow the derived one forever ───────────────

    /// <summary>
    /// ⭐ <b>BP-208 — *"the Print String LOST the pins and no editing of format restored them"*.</b>
    /// <c>NodePinSchema.GetCanonicalPins</c> opens with <c>if (node.Pins.Count > 0) return node.Pins;</c>,
    /// so an in-memory pin list <b>permanently shadows</b> the derived one — and
    /// <c>BlueprintCommandSink.ApplyPinIds</c> gives a node exactly such a list when it is created by
    /// dragging a wire onto empty canvas.
    ///
    /// <para>
    /// ⚠ <b>This is why the same gesture appeared to work sometimes and not others.</b> A node placed
    /// from the palette carries no pins and re-derives every rebuild; a node placed by dragging carries
    /// pins frozen at placement, when the format was still empty. Same node kind, same edit, opposite
    /// outcome — which reads as an editor glitch rather than a rule.
    /// </para>
    /// </summary>
    [Fact]
    public void ANodeWhosePinsWereFrozenAtPlacement_StillRederivesThemWhenTheFormatChanges()
    {
        var doc = AuthoringPath.Open(AuthoringPath.NewAsset("Seq_Frozen", BlueprintDispatchKind.Instance));

        // Reproduce the drag-to-create path: the canvas pre-generates the pin GUIDs and ships them as
        // InitialProperties["PinIds"], which is what makes ApplyPinIds populate node.Pins.
        var pinIds = new List<NodeEditor.Primitives.PinId>
        {
            NodeEditor.Primitives.IdGenerator.NewPinId(),
            NodeEditor.Primitives.IdGenerator.NewPinId(),
        };
        var print = AuthoringPath.AddNode(doc.Sink, doc.Graph, "PrintString",
            new Dictionary<string, object?> { ["PinIds"] = pinIds });

        Assert.NotEmpty(print.Pins);   // the precondition this test is about

        var session = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
        session.SetFormatForTest("{Threat}");

        Assert.Contains(print.Pins, p => p.Name == "Threat" && p.Direction == "In" && !p.IsExec);
    }

    // ── shared sequence ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Place a Print String, give it <paramref name="format"/>, place an int literal and wire it into
    /// the derived <paramref name="argName"/> pin — the prefix every sequence above starts from,
    /// performed entirely through the editor's own commands and Details sessions.
    /// </summary>
    /// <summary>
    /// Wires running from <paramref name="from"/> into <paramref name="to"/> — i.e. the ONE data link
    /// each sequence is about.
    ///
    /// <para>
    /// ⚠ Never assert on <c>Graph.Links.Count</c>, nor on every link touching the Print String. The
    /// graph also carries the seeded Entry -> Return pair (BP-126) and the Print node's own exec wires,
    /// none of which this edit may touch; a coarser count conflates the wire under test with
    /// scaffolding and cannot tell "pruned the right link" from "pruned everything".
    /// </para>
    /// </summary>
    private static int WiresBetween(AuthoringPath.Document doc, Node from, Node to)
        => doc.Graph.Links.Count(l => l.FromNodeId == from.Id && l.ToNodeId == to.Id);

    private static (AuthoringPath.Document Doc, Node Print, Node Literal) PrintStringWiredToAnIntLiteral(
        string assetName, string format, string argName)
    {
        var doc   = AuthoringPath.Open(AuthoringPath.NewAsset(assetName, BlueprintDispatchKind.Instance));
        var print = AuthoringPath.AddNode(doc.Sink, doc.Graph, "PrintString");

        var session = (PrintStringNodeSession)AuthoringPath.Details(doc, print);
        session.SetFormatForTest(format);
        doc.Model.RebuildAndNotify();

        // ⭐ Exec-wire it into the seeded chain: Entry -> Print -> Return. Without this the node is an
        // orphan whatever happens to its data links, and the orphan-elimination assertion above could
        // never distinguish the defect from the fixture. Wiring from Entry's exec-out replaces the
        // seeded Entry -> Return link, which is what the canvas does too.
        var entry  = doc.Graph.Nodes.First(n => n is EventEntryNode);
        var ret    = doc.Graph.Nodes.First(n => n is ReturnNode);
        AuthoringPath.Link(doc, entry, "Out", print, "In");
        AuthoringPath.Link(doc, print, "Out", ret,   "In");

        var literal = AuthoringPath.AddNode(doc.Sink, doc.Graph, "LiteralInt");
        AuthoringPath.Link(doc, literal, "Value", print, argName);

        return (doc, print, literal);
    }
}
