using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Catalog;
using Hrot.Editor.AiShared.Validation;
using Hrot.Editor.AiShared.Windows;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>AIE-053</c> — THE COLLISION WARNING IS A ROW IN THE ISSUE TABLE.</b>
/// 🔒 <b>User ruling, <c>2026-08-22</c>:</b> <i>"if collision strip is a warning about naming collision
/// or something, it need to be routed to where the collision can be seen or fixed."</i>
/// 📄 <c>docs/designs/blueprint-integ-1/DESIGN.md</c> §5.7: <i>"surface <c>SubElementCollision</c>
/// diagnostics … <b>in the shared windows</b>."</i>
///
/// <para>⛔⛔ <b>THE STRIP IT REPLACES WAS DEAD, and that is the load-bearing finding.</b>
/// 📐 <c>InspectorWindow.DrawCollisionDiagnosticStrip</c> called
/// <see cref="SubElementCollisionDetector.GetBindingAmbiguities"/>, which returns <c>Array.Empty</c>
/// <b>unconditionally</b> — so the red strip could never draw on any input. ⇒ ⭐ these rails are the
/// first time the data is asserted to reach a designer at all, and the first one below pins the deadness
/// so nobody "restores" the old call.</para>
/// </summary>
public sealed class TheCollisionWarningIsAnIssueRowTests
{
    /// <summary>
    /// ⛔⛔ <b>The premise, pinned rather than assumed.</b> ⚠ If <c>GetBindingAmbiguities</c> ever starts
    /// returning rows, the diagnostics source below is reporting the WRONG set and this says so —
    /// 📌 the same discipline as <c>EveryDrawerCallSiteOpensItsTableTests</c>'s premise rail.
    /// </summary>
    [Fact]
    public void BindingAmbiguities_IsStillUnconditionallyEmpty_WhichIsWhyTheStripWasDead()
    {
        var exporter = new CollidingExporter(("Ns.A.Wait", "Ns.B.Wait"));

        Assert.NotEmpty(SubElementCollisionDetector.GetCollisions(exporter));
        Assert.Empty   (SubElementCollisionDetector.GetBindingAmbiguities(exporter));
    }

    // ══ the rows ═════════════════════════════════════════════════════════════

    /// <summary>⭐⭐ A collision becomes ONE row that NAMES every claimant — because the fix is a C#
    /// rename and the FQNs are the only thing that points at it.</summary>
    [Fact]
    public void ACollisionBecomesOneRowNamingEveryClaimant()
    {
        var rows = SubElementCollisionDiagnostics.For(
            new CollidingExporter(("Ns.A.Wait", "Ns.B.Wait")));

        var row = Assert.Single(rows);
        Assert.Equal(SubElementCollisionDiagnostics.Code, row.Code);
        Assert.Equal(SubElementCollisionDiagnostics.SchemaScopeName, row.AssetName);
        Assert.Contains("Ns.A.Wait", row.Message, StringComparison.Ordinal);
        Assert.Contains("Ns.B.Wait", row.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>Severity is <c>Info</c>, and the detector is why.</b> 📐 Bindings resolve by full FQN, so
    /// a shared short name is never ambiguous at runtime — ⛔ an Error here would be the exact false
    /// positive <c>GetBindingAmbiguities</c> exists to refuse.
    /// </summary>
    [Fact]
    public void TheRowIsInfo_NotAnError()
    {
        var row = Assert.Single(SubElementCollisionDiagnostics.For(
            new CollidingExporter(("Ns.A.Wait", "Ns.B.Wait"))));

        Assert.Equal(AssetDiagnosticSeverity.Info, row.Severity);
    }

    /// <summary>⭐ And the message says where the fix lives — ⛔ the editor cannot rename a C# symbol,
    /// so it must not imply that it can.</summary>
    [Fact]
    public void TheMessageSaysTheFixIsInCSharp()
        => Assert.Contains("in C#",
            SubElementCollisionDiagnostics.Describe(new ActionCollision("Wait", new[] { "a", "b" })),
            StringComparison.Ordinal);

    /// <summary>⛔ No exporter ⇒ no rows. ⚠ Honest for a host that exports no action schema — nothing
    /// was asked, so nothing is claimed.</summary>
    [Fact]
    public void NoExporter_YieldsNoRows()
        => Assert.Empty(SubElementCollisionDiagnostics.For(null));

    /// <summary>⛔ Distinct short names are NOT a collision — the anti-vacuity half.</summary>
    [Fact]
    public void DistinctShortNames_AreNotACollision()
        => Assert.Empty(SubElementCollisionDiagnostics.For(
            new CollidingExporter(("Ns.A.Wait", "Ns.B.Sleep"))));

    // ══ the window shows them ════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE ROUTING: the Diagnostics window's aggregate CONTAINS the schema rows.</b>
    /// ⛔ Asserted on <c>Collect()</c> — the value <c>DrawClientArea</c> renders — so this is railable
    /// without ImGui *(📌 <c>R-21</c>/<c>R-62</c>)*.
    /// </summary>
    [Fact]
    public void TheDiagnosticsWindowAggregateIncludesTheSchemaRows()
    {
        var exporter = new CollidingExporter(("Ns.A.Wait", "Ns.B.Wait"));
        var window   = new DiagnosticsWindow(
            new AssetCatalog(), Array.Empty<IAssetValidator>(),
            schemaDiagnostics: () => SubElementCollisionDiagnostics.For(exporter));

        Assert.True(window.HasSchemaDiagnostics);
        Assert.Contains(window.Collect(), d => d.Code == SubElementCollisionDiagnostics.Code);
    }

    /// <summary>
    /// ⛔ <b>The anti-vacuity half, and it is the one that matters:</b> a window with NO schema source
    /// shows none of these. ⚠ Without it the rail above would pass against a window that manufactured
    /// collision rows from nowhere.
    /// </summary>
    [Fact]
    public void WithoutASchemaSource_TheWindowShowsNoCollisionRows()
    {
        var window = new DiagnosticsWindow(new AssetCatalog(), Array.Empty<IAssetValidator>());

        Assert.False(window.HasSchemaDiagnostics);
        Assert.Empty(window.Collect());
    }

    // ── fakes ───────────────────────────────────────────────────────────────

    /// <summary>⭐ An exporter whose entries collide on their last dot-segment, or not.</summary>
    private sealed class CollidingExporter : IActionSchemaExporter
    {
        private readonly Dictionary<string, ActionSchemaEntry> _all = new(StringComparer.Ordinal);

        public CollidingExporter((string A, string B) fqns)
        {
            _all[fqns.A] = Entry(fqns.A);
            _all[fqns.B] = Entry(fqns.B);
        }

        private static ActionSchemaEntry Entry(string fqn)
            => new(fqn, typeof(object), ActionHosting.BTree, BlackboardAccess.Unknown, null);

        public IReadOnlyDictionary<string, ActionSchemaEntry> All => _all;
        public ActionSchemaEntry? Lookup(string fqn) => _all.TryGetValue(fqn, out var e) ? e : null;
        public void Rebuild() { }
        public event Action? Changed { add { } remove { } }
    }
}
