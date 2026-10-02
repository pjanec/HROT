using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Runner;

namespace Hrot.Presentation.Tests.DebugApi;

/// <summary>
/// ⭐⭐⭐ <b>PHASE 1 — the bundle seam, and the STANDING CONSTRAINT made checkable.</b>
/// 📄 <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5b *(items ④ and ⑤)* · §3.2.
///
/// <para>⭐⭐ <b>The rail that matters most here is <see cref="A_bundle_cannot_reach_the_run_set"/>.</b>
/// 🔒 It encodes a USER RULING: <i>"what modules and systems … should run in the subsystem … is also [a]
/// very sensitive topic where the unification does not apply"</i>. ⚠⚠ The trap is invisible from inside — a
/// map bundle that registered <c>MapCullingModule</c> *because the editor does* would silently change what
/// CGF computes every frame <b>and would look like a successful unification.</b></para>
///
/// <para>⛔⛔ <b>And it is asserted STRUCTURALLY, not by convention.</b> 📌 A comment saying *"do not
/// register modules here"* is what the codebase already had in five other places before
/// <c>BP-487</c>/<c>CE-065</c>/<c>CE-066</c>. ⇒ ⭐ the constraint is enforced by what
/// <see cref="UiBundleContext"/> <b>cannot reach</b>, and this rail asserts that surface stays closed.</para>
/// </summary>
public sealed class TheUiBundleSeamHoldsTests
{
    /// <summary>⭐ A bundle that records what it was handed — nothing more.</summary>
    private sealed class SpyBundle : IUiBundle
    {
        public SpyBundle(string name) => Name = name;
        public string Name { get; }
        public int Calls { get; private set; }
        public UiBundleContext? Seen { get; private set; }

        public void RegisterInto(UiBundleContext ctx)
        {
            Calls++;
            Seen = ctx;
        }
    }

    private sealed class ThrowingBundle : IUiBundle
    {
        public string Name => "deliberately-broken";
        public void RegisterInto(UiBundleContext ctx) => throw new InvalidOperationException("boom");
    }

    /// <summary>⭐ The established headless construction — a null-handle atlas, as
    /// <c>BreakpointSubsystemWiringTests:137</c> already does.</summary>
    private static WindowManager NewWindowManager()
        => new(new IconAtlas(IntPtr.Zero, 512, 512));

    private static UiBundleContext NewContext() => new(NewWindowManager());

    /// <summary>
    /// ⭐⭐ Every bundle in the list is composed, exactly once, IN ORDER.
    /// <para>⚠ Order is asserted because it is OBSERVABLE: the global-menu trie has no ordering key, so
    /// items render in registration order — the `global-menu` subset rail says so in as many words.
    /// ⛔ A future "optimisation" that sorted or parallelised composition would silently reorder menus.</para>
    /// </summary>
    [Fact]
    public void Every_bundle_is_composed_once_in_order()
    {
        var a = new SpyBundle("a");
        var b = new SpyBundle("b");
        var ctx = NewContext();

        UiBundleHost.Compose(new IUiBundle[] { a, b }, ctx);

        // ⭐ Once each, and each handed the SAME context — ⛔ not a fresh one per bundle, which would give
        //   two bundles two different menu registries.
        Assert.Equal(1, a.Calls);
        Assert.Equal(1, b.Calls);
        Assert.Same(ctx, a.Seen);
        Assert.Same(ctx, b.Seen);

        // ⭐⭐ And the ORDER, which `Calls` cannot show.
        var seq = new List<string>();
        UiBundleHost.Compose(
            new IUiBundle[] { new OrderBundle("first", seq), new OrderBundle("second", seq) },
            NewContext());
        Assert.Equal(new[] { "first", "second" }, seq);
    }

    private sealed class OrderBundle : IUiBundle
    {
        private readonly List<string> _seq;
        public OrderBundle(string name, List<string> seq) { Name = name; _seq = seq; }
        public string Name { get; }
        public void RegisterInto(UiBundleContext ctx) => _seq.Add(Name);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A THROWING BUNDLE IS NAMED, NEVER SWALLOWED.</b>
    /// 📌 This is the whole reason <see cref="UiBundleHost.Compose"/> exists instead of a <c>foreach</c> at
    /// each call site: a bundle that fails quietly is a feature that silently never appears — ⭐ the exact
    /// class of defect this programme exists to end *(`CE-046`…`CE-066`, six found by eye)*.
    /// ⚠ The bundle's NAME must be in the message, so the failure names the feature rather than a stack
    /// frame inside a registry.
    /// </summary>
    [Fact]
    public void A_throwing_bundle_is_named_not_swallowed()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => UiBundleHost.Compose(new IUiBundle[] { new ThrowingBundle() }, NewContext()));

        Assert.Contains("deliberately-broken", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.InnerException);
        Assert.Equal("boom", ex.InnerException!.Message);
    }

    /// <summary>
    /// ⭐⭐ A hole in a host's bundle list is reported as one — ⛔ not as a <c>NullReferenceException</c>
    /// three frames down inside a registry.
    /// </summary>
    [Fact]
    public void A_null_entry_names_its_index()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => UiBundleHost.Compose(new IUiBundle?[] { new SpyBundle("a"), null }!, NewContext()));

        Assert.Contains("index 1", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ An EMPTY list composes cleanly — ⛔ it is not an error.
    /// 📌 §3.3: *"a smaller list is a SUBSET, never a branch"*, and the smallest subset is none. ⚠ A host
    /// that legitimately offers no shared UI *(a headless SimHost node)* must not have to special-case.
    /// </summary>
    [Fact]
    public void An_empty_list_is_legal()
        => UiBundleHost.Compose(Array.Empty<IUiBundle>(), NewContext());

    /// <summary>
    /// ⭐⭐⭐ <b>THE CONSTRAINT (§3.2, item ⑤) — a bundle cannot reach the RUN-SET or the NETWORK.</b>
    ///
    /// <para>⭐ Asserted by REFLECTION over <see cref="UiBundleContext"/>'s public surface, because that
    /// surface IS the constraint. ⛔ Not a source scan for forbidden names: a bundle could always reach a
    /// module registry it was handed some other way — ⚠ what this rail pins is that <b>the seam does not
    /// hand it one</b>, which is the part the seam controls.</para>
    ///
    /// <para>⚠ <b>If this rail fails, read it as a DESIGN question, not a test to update.</b> Someone added
    /// a member to the context; the question is whether a bundle should be able to reach it at all. 🔒 The
    /// user's ruling is that modules, systems, translators and participants are per-ROLE and never
    /// unified.</para>
    /// </summary>
    [Fact]
    public void A_bundle_cannot_reach_the_run_set()
    {
        var exposed = typeof(UiBundleContext)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.PropertyType.Name)
            .ToArray();

        // ⭐ The whitelist IS the design: windows, menu, toolbar. Nothing else.
        Assert.Equal(
            new[] { "GlobalMenuRegistry", "MainToolbarManager", "WindowManager" },
            exposed.OrderBy(n => n, StringComparer.Ordinal).ToArray());

        // ⛔ And the forbidden shapes, named so the failure explains itself rather than just diffing a list.
        foreach (var forbidden in new[]
                 {
                     "ModuleHostKernel", "ISystemRegistry", "IEcsModule", "FdpEventBus",
                     "DdsParticipant", "EntityRepository",
                 })
            Assert.DoesNotContain(forbidden, exposed);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The menu and toolbar are DERIVED from the window manager, so they cannot be a different
    /// host's.</b>
    /// 📌 The one behavioural improvement the seam makes over the six-argument static it replaces: that
    /// call took <c>toolbar</c> and <c>menu</c> as SEPARATE arguments, and nothing stopped a host passing
    /// one host's toolbar with another's menu — ⛔ which would compose perfectly and render half.
    /// ⚠ Same present-but-disconnected shape as <c>BP-487</c>'s manifest cell.
    /// </summary>
    [Fact]
    public void The_menu_and_toolbar_come_from_the_one_window_manager()
    {
        var wm  = NewWindowManager();
        var ctx = new UiBundleContext(wm);

        Assert.Same(wm,             ctx.Windows);
        Assert.Same(wm.GlobalMenu,  ctx.Menu);
        Assert.Same(wm.MainToolbar, ctx.Toolbar);

        // ⭐⭐ And a SECOND host's registries are not the same objects — which is what makes the derivation
        //    meaningful rather than tautological. ⛔ With separate arguments, a host could have handed
        //    `other`'s menu alongside `wm`'s toolbar; through the context that pairing cannot be expressed.
        var other = NewWindowManager();
        Assert.NotSame(other.GlobalMenu,  ctx.Menu);
        Assert.NotSame(other.MainToolbar, ctx.Toolbar);
    }
}
