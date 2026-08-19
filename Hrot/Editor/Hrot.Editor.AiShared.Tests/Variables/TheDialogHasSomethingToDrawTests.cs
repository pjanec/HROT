using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Variables;
using StructEdit.Core;
using StructEdit.Reflection;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Variables;

/// <summary>
/// ⭐⭐⭐ <b>Batch 96 (<c>96b</c>) — the dialog has SOMETHING TO DRAW.</b>
///
/// <para>🔴🔴 <b>The user's report:</b> <i>"just 'count' and two horizontal separator lines"</i>.
/// 📐 Two independent causes, and this file covers the second: <c>ScopeFor</c> built
/// <c>EditScope.ForField("$.Count")</c> from the VARIABLE'S NAME, while
/// <c>DefaultValueAuthoring.OpenSession</c> opens the session over <b>the variable's VALUE</b> ⇒ the
/// document root IS the value at <c>$</c> ⇒ <c>"$.Count"</c> asked for a field named <c>Count</c>
/// inside the <c>int</c>, <c>FilterNode</c> matched nothing, and <c>ApplyScope</c> produced an
/// <b>EMPTY <c>SelectionRoot</c></b>.</para>
///
/// <para>⭐⭐⭐ <b>Why THIS rail and not another scope rail.</b> 📌 <c>FINDINGS_…_19b</c> §3b:
/// <i>"the tests assert the DOCUMENT, never the DRAW."</i> ⚠ Half right — the existing rails asserted
/// the <b>SCOPE OBJECT</b>, never the document the scope produces, which is why Batch 75 could
/// "fix" the path from <c>"Count"</c> to <c>"$.Count"</c> and leave it just as empty. ⇒ ⭐ <b>this
/// counts the nodes the drawer would actually visit</b>, through the production launcher.</para>
///
/// <para>⛔ <b>Still not the DRAW</b> *(<c>R-21</c>/<c>R-62</c>)</b> — it proves the drawer is handed
/// something, not that pixels appear. ⭐ The table wrapping that turns those nodes into rows is
/// <see cref="EveryDrawerCallSiteOpensItsTableTests"/>'s half.</para>
/// </summary>
public sealed class TheDialogHasSomethingToDrawTests
{
    /// <summary>⭐ A DTO with the same field names the existing corpus uses, so a real sub-path
    /// exists to narrow to.</summary>
    private struct Params
    {
        // ⚠ Assigned only by StructEdit's reflection, never by this file — hence the suppression.
#pragma warning disable CS0649
        public int   Count;
        public float Speed;
#pragma warning restore CS0649
    }

    private static VariableEditLauncher Launcher()
        => new(new ComponentEditServiceBuilder().Build());

    private static VariableRow Row(string name, Type clr) => new(
        Origin:    new VariableRowOrigin(Guid.NewGuid(), default, "Variables", name, "Alpha"),
        ShortName: name, TypeText: clr.Name, ClrType: clr,
        ReadValue: () => Array.Empty<byte>());

    /// <summary>⭐ The nodes <c>ComponentEditDrawer.DrawEditNode</c> would actually render — a
    /// <c>SelectionRoot</c> is an invisible wrapper that draws nothing itself.</summary>
    private static int DrawableNodes(EditNode node)
        => (node.Kind == EditNodeKind.SelectionRoot ? 0 : 1)
         + node.Children.Sum(DrawableNodes);

    // ══ the scalar the user actually had ═════════════════════════════════════

    /// <summary>
    /// 🔴🔴 <b>RED before this batch: 0 drawable nodes.</b> A plain <c>int</c> variable — the exact
    /// shape the user clicked — must give the drawer at least one node.
    /// </summary>
    [Theory]
    [InlineData(VariableEditAction.EditValue)]
    [InlineData(VariableEditAction.Properties)]
    public void AScalarVariableGivesTheDrawerSomething(VariableEditAction action)
    {
        var entry = new BlackboardVariableEntry("Count", typeof(int), Comment: null, DefaultValueJson: "7");

        using var session = Launcher().Open(
            Row("Count", typeof(int)), action, VariableRunState.Planning, entry);

        Assert.NotNull(session);
        Assert.True(DrawableNodes(session!.Document.Root) > 0,
            $"'{action}' produced an EMPTY document for a scalar variable — the dialog would draw a "
            + "name and two separators with nothing between them, which is what the user reported.");
    }

    /// <summary>⭐⭐ …and the value it shows is the DECLARED DEFAULT, not a zero — ⛔ a document that
    /// merely has nodes could still be seeded from nothing.</summary>
    [Fact]
    public void TheScalarDocumentIsSeededFromTheDeclaredDefault()
    {
        var entry = new BlackboardVariableEntry("Count", typeof(int), Comment: null, DefaultValueJson: "7");

        using var session = Launcher().Open(
            Row("Count", typeof(int)), VariableEditAction.EditValue, VariableRunState.Planning, entry);

        Assert.Equal(7, session!.Commit());
    }

    // ══ the DTO shape every earlier test used ════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>A DTO variable was ALSO empty</b>, and this is the rail that says so. 📌 The old scope
    /// asked for a field named after the VARIABLE inside the DTO — <c>"$.Settings"</c> for a variable
    /// called <c>Settings</c> — which is a different thing from the DTO's own fields. ⛔ It only ever
    /// matched by coincidence, when a variable happened to be named like one of its own members.
    /// </summary>
    [Fact]
    public void ADtoVariableGivesTheDrawerAllItsFields()
    {
        var entry = new BlackboardVariableEntry("Settings", typeof(Params), Comment: null);

        using var session = Launcher().Open(
            Row("Settings", typeof(Params)), VariableEditAction.EditValue,
            VariableRunState.Planning, entry);

        var names = Flatten(session!.Document.Root).Select(n => n.Name).ToList();

        Assert.Contains("Count", names);
        Assert.Contains("Speed", names);
    }

    /// <summary>
    /// ⚠⚠ <b>THE COINCIDENCE THAT HID IT, pinned.</b> 📌 A variable named exactly like one of its own
    /// DTO's fields — <c>Count</c> holding a <c>Params</c> that HAS a <c>Count</c> — is the one case in
    /// which the old <c>"$.Count"</c> scope selected something. ⭐ It selected <b>the wrong thing</b>:
    /// one member instead of the whole value. ⛔ This asserts the whole DTO is offered even then.
    /// </summary>
    [Fact]
    public void AVariableNamedLikeOneOfItsOwnFieldsStillOffersTheWholeValue()
    {
        var entry = new BlackboardVariableEntry("Count", typeof(Params), Comment: null);

        using var session = Launcher().Open(
            Row("Count", typeof(Params)), VariableEditAction.EditValue,
            VariableRunState.Planning, entry);

        var names = Flatten(session!.Document.Root).Select(n => n.Name).ToList();

        Assert.Contains("Count", names);
        Assert.Contains("Speed", names);   // 🔴 absent before 96b — the scope had narrowed to Count
    }

    private static IEnumerable<EditNode> Flatten(EditNode node)
        => new[] { node }.Concat(node.Children.SelectMany(Flatten));
}
