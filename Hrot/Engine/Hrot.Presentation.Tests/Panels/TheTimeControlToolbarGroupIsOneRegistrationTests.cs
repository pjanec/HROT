using System;
using System.Linq;
using Fdp.Presentation.WindowManager;
using Hrot.UI.Common.Facades;
using Hrot.UI.Common.Panels;
using Xunit;

namespace Hrot.Presentation.Tests.Panels;

/// <summary>
/// ⭐⭐⭐ THE EQUIVALENCE RAIL for phase 2 slice ③ — <see cref="ShellTimeControlToolbar"/>.
///
/// <para>🔒 <c>CE-072</c>'s rule: a wrapper needs an equivalence rail the day it is introduced, because
/// once it is the only production path the existing tests stop covering production. ⭐ Four lines moved
/// out of two composition roots; ⛔ if the entry id, sort order or declared height changed, the toolbar
/// silently reshuffles and <b>nothing else in the unit suites would say so</b>.</para>
///
/// <para>⭐⭐ Every expectation is a LITERAL copied from the pre-change hosts — <c>"TimeControlGroup"</c>
/// at sort order 0, <c>"ToolbarSep_TimeToPersp"</c> at 10. ⛔ NOT
/// <c>ShellTimeControlToolbar.EntryId</c> compared against itself, which would pass whatever the
/// constants became.</para>
///
/// <para>📄 Design: <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5c.8 (<c>H1</c>, item ③).</para>
/// </summary>
public sealed class TheTimeControlToolbarGroupIsOneRegistrationTests
{
    // ⭐ REUSES the assembly's existing `FakeTimeTransportFacade` — ⛔ a second fake for one seam is
    //   exactly the duplication this programme is unwinding, and that one's own doc already names
    //   `MainToolbarTimeControlSection` as a target.

    private static MainToolbarManager Register(bool withSeparator)
    {
        var toolbar = new MainToolbarManager();
        ShellTimeControlToolbar.Register(toolbar, new FakeTimeTransportFacade(), withSeparator);
        return toolbar;
    }

    private static MainToolbarEntryView? Item(MainToolbarManager toolbar, string id)
        => toolbar.BuildViewModel(currentPerspective: "Scenario")
                  .Entries.FirstOrDefault(e => e.Id == id);

    // ── the entry, against the literals both hosts used ───────────────────────────

    /// <summary>
    /// ⭐⭐⭐ The transport group keeps the id and sort order both hosts shipped.
    /// ⚠ Sort order is not cosmetic — §7 reserves 0–9 for this group, and a reshuffle is a UX change.
    /// </summary>
    [Fact]
    public void The_transport_group_keeps_the_id_and_sort_order_both_hosts_shipped()
    {
        var entry = Item(Register(withSeparator: false), "TimeControlGroup");

        Assert.NotNull(entry);
        Assert.Equal("entry", entry!.Kind);
        Assert.Equal(0, entry.SortOrder);
        // ⭐ Global, not perspective-filtered: the transport is reachable from every perspective, which
        //   is what both hosts registered (they passed no perspective argument).
        Assert.Null(entry.Perspective);
    }

    /// <summary>
    /// ⭐ The declared height drives the toolbar band's height, so a change here moves the whole
    /// dockspace. ⚠ Asserted through the manager rather than by reading the constant back.
    /// </summary>
    [Fact]
    public void The_group_declares_the_default_entry_height()
    {
        var toolbar = Register(withSeparator: false);
        Assert.Equal(MainToolbarManager.DefaultEntryHeight, toolbar.Height);
    }

    // ── the separator: the ONE difference, now declared ───────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>H1</c>'s whole point.</b> The editor closes the group with a separator; CGF does not
    /// *(<c>CE-016</c> §7 — it stood in front of a perspective group that host did not register)</b>.
    /// ⇒ the difference is a PARAMETER, and this asserts the parameter actually decides it.
    /// </summary>
    [Fact]
    public void The_separator_appears_only_for_the_host_that_asks_for_it()
    {
        Assert.NotNull(Item(Register(withSeparator: true),  "ToolbarSep_TimeToPersp"));
        Assert.Null   (Item(Register(withSeparator: false), "ToolbarSep_TimeToPersp"));
    }

    /// <summary>⭐ When it IS emitted it sits at 10 — the slot that closes §7's 0–9 range.</summary>
    [Fact]
    public void The_separator_keeps_its_id_and_sort_order()
    {
        var sep = Item(Register(withSeparator: true), "ToolbarSep_TimeToPersp");

        Assert.NotNull(sep);
        Assert.Equal("separator", sep!.Kind);
        Assert.Equal(10, sep.SortOrder);
    }

    /// <summary>
    /// ⭐⭐ Anti-vacuity, and the shape of the whole registration: turning the separator off leaves
    /// EXACTLY the one entry — ⛔ so the helper cannot be quietly registering extra items.
    /// </summary>
    [Fact]
    public void Nothing_else_is_registered()
    {
        var withoutSep = Register(withSeparator: false)
            .BuildViewModel("Scenario").Entries.Select(e => e.Id).ToArray();
        Assert.Equal(new[] { "TimeControlGroup" }, withoutSep);

        var withSep = Register(withSeparator: true)
            .BuildViewModel("Scenario").Entries.Select(e => e.Id).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "TimeControlGroup", "ToolbarSep_TimeToPersp" }, withSep);
    }

    /// <summary>⛔ A null facade is a composition-root mistake and says so, rather than throwing later
    /// from inside a render delegate where the stack points at ImGui.</summary>
    [Fact]
    public void A_null_facade_is_refused_at_registration()
        => Assert.Throws<ArgumentNullException>(
            () => ShellTimeControlToolbar.Register(new MainToolbarManager(), null!, withSeparator: false));
}
