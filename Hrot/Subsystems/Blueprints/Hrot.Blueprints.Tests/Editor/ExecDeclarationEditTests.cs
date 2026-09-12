using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Editor.Host;
using Hrot.Blueprints.Editor.Variables;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-80 / BP-225 — editing a macro's exec declarations without corrupting a working graph.
///
/// <para>
/// ⭐⭐ <b>The batch's central question, and the answer is not the expected one.</b> The premise put to
/// this work was that reordering declarations silently re-targets wires, because
/// <c>Stage2_5_ExpandMacros</c> pairs <c>execIn[k]</c> with <c>entryExecOuts[k]</c> positionally —
/// *"a graph that still compiles and runs the wrong path"*. Checking it against the code says
/// otherwise, for two reasons that hold together:
/// </para>
///
/// <list type="number">
///   <item>A pin's identity is <c>DeterministicIds.PinId(nodeId, name, direction)</c> — <b>a function
///     of the name</b>. A wire follows the named pin, wherever it moves in the list.</item>
///   <item>The boundary node's pins and every call site's pins are projected from the <b>same</b>
///     declaration list in the same order, so index <c>k</c> names the same declaration on both sides
///     and a permutation permutes both together.</item>
/// </list>
///
/// <para>
/// ⇒ ⭐ <b>Reorder is safe; RENAME and DELETE are the destructive edits</b>, and they are destructive
/// for BP-202's reason rather than the splice's: a rename destroys one pin and creates another,
/// leaving every incident link <b>dangling</b>. That breaks the solution build with <c>BP1602</c> from
/// a graph that looks fine on screen — strictly worse than a dropped wire.
/// </para>
///
/// <para>
/// ⚠ These tests assert on <b>which declaration a wire ends up attached to</b>, never on a pin index.
/// An index assertion would pass under exactly the corruption being guarded against.
/// </para>
/// </summary>
public sealed class ExecDeclarationEditTests
{
    // ── fixture: a macro with three entries, and a host that calls it ────────

    private sealed record Fixture(
        BlueprintAsset Asset,
        Graph          Macro,
        Graph          Host,
        Node           MacroEntry,
        Node           CallNode,
        List<Node>     Callers);

    private static Pin ExecPin(Guid nodeId, string name, string dir) => new()
    {
        Id = DeterministicIds.PinId(nodeId, name, dir),
        Name = name, Direction = dir, IsExec = true, TypeRef = new BlueprintTypeRef(),
    };

    /// <summary>
    /// A macro declaring entries Alpha/Beta/Gamma, its entry node carrying the three projected
    /// exec-OUT pins, and a host graph whose call node carries the three matching exec-IN pins with a
    /// distinct upstream node wired into each — so "which entry is this wire on?" has a unique answer.
    /// </summary>
    private static Fixture MakeFixture(params string[] entryNames)
    {
        var macro = new Graph { Id = Guid.NewGuid(), Name = "AimFire", Kind = GraphKind.Macro };
        foreach (var n in entryNames)
            macro.ExecInputs.Add(new ExecInDecl { Id = Guid.NewGuid(), Name = n });

        var entry = new EventEntryNode { Id = Guid.NewGuid() };
        foreach (var n in entryNames)
            entry.Pins.Add(ExecPin(entry.Id, n, "Out"));
        macro.Nodes.Add(entry);

        var ret = new ReturnNode { Id = Guid.NewGuid() };
        ret.Pins.Add(ExecPin(ret.Id, "In", "In"));
        macro.Nodes.Add(ret);

        // Inside the macro: each entry pin runs to its own body node, so the body side is
        // distinguishable too.
        var host = new Graph { Id = Guid.NewGuid(), Name = "Tick", Kind = GraphKind.Function };
        var call = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = macro.Id.ToString() };
        foreach (var n in entryNames)
            call.Pins.Add(ExecPin(call.Id, n, "In"));
        call.Pins.Add(ExecPin(call.Id, "Out", "Out"));
        host.Nodes.Add(call);

        // One distinct caller per entry, wired into that entry's pin on the CALL node.
        var callers = new List<Node>();
        foreach (var n in entryNames)
        {
            var caller = new PrintStringNode { Id = Guid.NewGuid() };
            var outPin = ExecPin(caller.Id, "Out", "Out");
            caller.Pins.Add(outPin);
            host.Nodes.Add(caller);
            callers.Add(caller);

            host.Links.Add(new Link
            {
                FromNodeId = caller.Id, FromPinId = outPin.Id,
                ToNodeId   = call.Id,   ToPinId   = DeterministicIds.PinId(call.Id, n, "In"),
            });
        }

        var asset = new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(), Name = "ExecDeclAsset",
            Dispatch = BlueprintDispatchKind.Instance, Header = new Header(),
            Graphs   = { host, macro },
        };

        return new Fixture(asset, macro, host, entry, call, callers);
    }

    private static ExecSignatureEditModel EntriesModel(Fixture f, Action? onChanged = null)
        => new(f.Asset, f.Macro, isEntry: true, onChanged ?? (() => { }));

    /// <summary>
    /// The declaration a given caller's wire currently lands on, by NAME — resolved through the pin
    /// id, which is the only thing the compiler and the canvas both look at.
    /// </summary>
    private static string? EntryReachedBy(Fixture f, Node caller)
    {
        var link = f.Host.Links.FirstOrDefault(l => l.FromNodeId == caller.Id);
        if (link is null) return null;
        return f.CallNode.Pins.FirstOrDefault(p => p.Id == link.ToPinId)?.Name;
    }

    // ────────────────────────────────────────────────────────────────────────
    // ⭐ The guard the batch cannot ship without
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Reordering must not re-point a call site's wires.</b> Alpha is moved to the end; every
    /// caller must still reach the entry it was wired to. This is the test that would catch a
    /// "fix" that started pairing by index at the editor level.
    /// </summary>
    [Fact]
    public void Reordering_DoesNotRepointCallSiteWires()
    {
        var f = MakeFixture("Alpha", "Beta", "Gamma");
        var before = f.Callers.Select(c => EntryReachedBy(f, c)).ToList();
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, before);

        EntriesModel(f).MoveDeclaration(0, 2);      // Alpha → last

        Assert.Equal(new[] { "Beta", "Gamma", "Alpha" },
            f.Macro.ExecInputs.Select(d => d.Name).ToArray());
        // ⭐ The order changed; the MEANING of every wire did not.
        Assert.Equal(before, f.Callers.Select(c => EntryReachedBy(f, c)).ToList());
    }

    /// <summary>
    /// ⭐ <b>Deleting must not re-point the survivors' wires either</b> — the failure mode the premise
    /// described. Beta goes; Alpha and Gamma must still reach Alpha and Gamma, and Beta's caller must
    /// be left with no wire at all rather than a wire onto someone else's entry.
    /// </summary>
    [Fact]
    public void Deleting_DoesNotRepointSurvivingWires_AndTakesItsOwn()
    {
        var f = MakeFixture("Alpha", "Beta", "Gamma");

        EntriesModel(f).RemoveDeclaration("Beta");

        Assert.Equal(new[] { "Alpha", "Gamma" }, f.Macro.ExecInputs.Select(d => d.Name).ToArray());
        Assert.Equal("Alpha", EntryReachedBy(f, f.Callers[0]));
        Assert.Equal("Gamma", EntryReachedBy(f, f.Callers[2]));
        // ⛔ Not silently re-pointed, and not left dangling either.
        Assert.Null(EntryReachedBy(f, f.Callers[1]));
    }

    /// <summary>
    /// ⛔ <b>No dangling links, ever.</b> A link whose endpoint id names no pin is worse than a dropped
    /// one: <c>BP1602</c> at solution-build time, naming two GUIDs from a graph that looks intact.
    /// </summary>
    [Fact]
    public void Deleting_LeavesNoLinkPointingAtAVanishedPin()
    {
        var f = MakeFixture("Alpha", "Beta", "Gamma");

        EntriesModel(f).RemoveDeclaration("Beta");

        var livePinIds = new HashSet<Guid>(f.Host.Nodes.SelectMany(n => n.Pins).Select(p => p.Id));
        foreach (var link in f.Host.Links)
        {
            Assert.Contains(link.FromPinId, livePinIds);
            Assert.Contains(link.ToPinId,   livePinIds);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Rename — the wire has to come along
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ A rename destroys the pin <c>(node, old, dir)</c> and creates <c>(node, new, dir)</c>. The
    /// wire must arrive at the new pin, not at the old id nobody claims — BP-202's defect, one level
    /// up and with the old→new mapping available, so repointing is possible where BP-202 could only
    /// prune.
    /// </summary>
    [Fact]
    public void Renaming_MovesTheWireToTheNewPin_AtEveryCallSite()
    {
        var f = MakeFixture("Alpha", "Beta");

        Assert.True(EntriesModel(f).RenameDeclaration("Alpha", "Start"));

        Assert.Equal("Start", EntryReachedBy(f, f.Callers[0]));
        Assert.Equal("Beta",  EntryReachedBy(f, f.Callers[1]));

        var expected = DeterministicIds.PinId(f.CallNode.Id, "Start", "In");
        Assert.Contains(f.Host.Links, l => l.ToPinId == expected);
    }

    /// <summary>The macro's own boundary node is a projection site too, not just the call sites.</summary>
    [Fact]
    public void Renaming_AlsoMovesTheMacrosOwnEntryPin()
    {
        var f = MakeFixture("Alpha", "Beta");

        EntriesModel(f).RenameDeclaration("Alpha", "Start");

        Assert.Contains(f.MacroEntry.Pins, p => p.Name == "Start" && p.Direction == "Out");
        Assert.DoesNotContain(f.MacroEntry.Pins, p => p.Name == "Alpha");
        Assert.Contains(f.MacroEntry.Pins,
            p => p.Id == DeterministicIds.PinId(f.MacroEntry.Id, "Start", "Out"));
    }

    /// <summary>
    /// ⚠ <b>The genuinely corrupting edit, and the one the premise did not name.</b> Two declarations
    /// sharing a name project to the SAME pin id, so the second collapses onto the first: two exec
    /// entries, one pin, and a splice pairing index <c>k</c> against a pin two declarations claim.
    /// Refused on both paths.
    /// </summary>
    [Fact]
    public void DuplicateNames_AreRefused_OnAddAndOnRename()
    {
        var f = MakeFixture("Alpha", "Beta");
        var model = EntriesModel(f);

        Assert.False(model.AddDeclaration("Alpha"));
        Assert.False(model.RenameDeclaration("Beta", "Alpha"));
        Assert.Equal(new[] { "Alpha", "Beta" }, f.Macro.ExecInputs.Select(d => d.Name).ToArray());
    }

    // ────────────────────────────────────────────────────────────────────────
    // Undo
    // ────────────────────────────────────────────────────────────────────────

    private static (ExecSignatureEditModel Model, List<Action> Undos) Recording(Fixture f)
    {
        var undos = new List<Action>();
        var model = new ExecSignatureEditModel(
            f.Asset, f.Macro, isEntry: true, () => { },
            record: (_, apply, undo) => { apply(); undos.Add(undo); });
        return (model, undos);
    }

    /// <summary>⭐ Undoing a delete must bring the WIRES back, not just the declaration.</summary>
    [Fact]
    public void UndoingADelete_RestoresTheDeclarationAtItsIndex_AndItsWires()
    {
        var f = MakeFixture("Alpha", "Beta", "Gamma");
        var (model, undos) = Recording(f);

        model.RemoveDeclaration("Beta");
        Assert.Null(EntryReachedBy(f, f.Callers[1]));

        undos[^1]();

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" },
            f.Macro.ExecInputs.Select(d => d.Name).ToArray());
        Assert.Equal("Beta", EntryReachedBy(f, f.Callers[1]));
    }

    /// <summary>
    /// Undoing a rename is the reverse rename, so the wires travel back with it. Replayed twice to
    /// catch an inverse that only works once.
    /// </summary>
    [Fact]
    public void UndoingARename_MovesTheWiresBack_AndSurvivesReplay()
    {
        var f = MakeFixture("Alpha", "Beta");
        var (model, undos) = Recording(f);

        model.RenameDeclaration("Alpha", "Start");
        Assert.Equal("Start", EntryReachedBy(f, f.Callers[0]));

        undos[^1]();
        Assert.Equal("Alpha", EntryReachedBy(f, f.Callers[0]));

        model.RenameDeclaration("Alpha", "Start");
        Assert.Equal("Start", EntryReachedBy(f, f.Callers[0]));
        undos[^1]();
        Assert.Equal("Alpha", EntryReachedBy(f, f.Callers[0]));
    }

    /// <summary>The cost of a delete is reportable before it happens, not discovered afterwards.</summary>
    [Fact]
    public void WireCount_ReportsWhatADeleteWouldRemove()
    {
        var f = MakeFixture("Alpha", "Beta", "Gamma");
        var model = EntriesModel(f);

        Assert.Equal(1, model.WireCount("Beta"));
        Assert.Equal(0, model.WireCount("NoSuchEntry"));
    }

    // ────────────────────────────────────────────────────────────────────────
    // Exits — the same machinery on the other boundary
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠ The pin DIRECTIONS are mirrored between the two boundaries: an exit is an exec-In on the
    /// macro's Return node and an exec-Out on the call node. Getting that backwards would silently
    /// find no pins and repoint nothing, so it is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void ExitDeclarations_RenameAcrossBothBoundaries()
    {
        var f = MakeFixture("Alpha");
        var macro = f.Macro;
        macro.ExecOutputs.Add(new ExecOutDecl { Id = Guid.NewGuid(), Name = "Done" });

        var ret = macro.Nodes.OfType<ReturnNode>().Single();
        ret.Pins.Clear();
        ret.Pins.Add(ExecPin(ret.Id, "Done", "In"));
        f.CallNode.Pins.Add(ExecPin(f.CallNode.Id, "Done", "Out"));

        var exits = new ExecSignatureEditModel(f.Asset, macro, isEntry: false, () => { });
        Assert.True(exits.RenameDeclaration("Done", "Finished"));

        Assert.Contains(ret.Pins,        p => p.Name == "Finished" && p.Direction == "In");
        Assert.Contains(f.CallNode.Pins, p => p.Name == "Finished" && p.Direction == "Out");
    }
}
