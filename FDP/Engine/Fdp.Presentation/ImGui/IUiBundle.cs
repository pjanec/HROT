using System;
using System.Collections.Generic;
using Fdp.Presentation.WindowManager;

namespace Fdp.Toolkit.Runner;

/// <summary>
/// ⭐⭐⭐ <b>A FEATURE-SCOPED unit of UI composition: one bundle registers everything one feature needs, and
/// a HOST composes a LIST of them.</b>
/// 📄 <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5b *(the <c>classDiagram</c> and
/// <c>sequenceDiagram</c> this type is drawn in)*.
///
/// <para>⭐⭐ <b>This names a shape that already worked; it does not invent one.</b> 📐 Measured
/// `2026-08-27`: <c>BlueprintWindowRegistrar</c> already implements a feature seam
/// *(<c>IShellCommandRegistrar</c>, in-degree 24)* <b>and</b> the host seam
/// <see cref="IWindowRegistrar"/>, adapting between them through a private registry, with DI registering it
/// as both. ⇒ ⛔ the bundle contract was not missing — it was unnamed and trapped in one feature
/// assembly.</para>
///
/// <para>⛔⛔⛔ <b>THE STANDING CONSTRAINT — a USER RULING, and the reason this interface is deliberately
/// narrow</b> *(§3.1/§3.2)*: 🔒 <i>"what modules and systems … should run in the subsystem … is also [a]
/// very sensitive topic where the unification does not apply"</i> and the same for network translators.
/// ⇒ ⭐ <b>a bundle registers WINDOWS · PANELS · MENU ITEMS · TOOLBAR ENTRIES and nothing else.</b>
/// ⛔ It may NEVER register a module, a global system, a DDS translator, an egress/ingress system or a
/// participant. ⚠⚠ <b>The trap is invisible from inside:</b> a map bundle that registered
/// <c>MapCullingModule</c> *because the editor does* would silently change what CGF computes every frame —
/// 📌 <b>and would look like a successful unification.</b> That is why <see cref="UiBundleContext"/> hands
/// out no kernel, no bus and no module registry: the constraint is enforced by what a bundle CANNOT
/// REACH, not by a review note.</para>
///
/// <para>⭐ <b>Why a LIST and not one <c>ComposeEditorExperience(deps)</c></b> *(§3.3)*: a monolith cannot
/// serve ExCon, IG, ReplayBrowser and SimHost without a host conditional *(ruling 58 forbids)* or a bag of
/// nullable knobs — ⛔ and that bag is a silent-default generator, the exact shape behind
/// <c>BP-487</c>/<c>CE-065</c>/<c>CE-066</c>. ⇒ ⭐⭐ <b>a smaller list is a SUBSET, never a branch.</b></para>
/// </summary>
public interface IUiBundle
{
    /// <summary>
    /// ⭐ A stable, human-readable name — used in composition diagnostics and in the failure message when
    /// this bundle throws. ⚠ Not an id and not a key: nothing looks a bundle up by name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// ⭐⭐ Register this feature's windows, panels, menu items and toolbar entries into
    /// <paramref name="ctx"/>.
    /// <para>⚠ Called ONCE per host, during that host's <see cref="IWindowRegistrar.RegisterWindows"/>.
    /// ⛔ Not per frame, and not before the host has a <c>WindowManager</c>.</para>
    /// </summary>
    void RegisterInto(UiBundleContext ctx);
}

/// <summary>
/// ⭐⭐ <b>Everything a bundle is allowed to write into — and, by omission, the constraint itself.</b>
/// 📄 §5b.2.
///
/// <para>⭐⭐⭐ <b>The registries are DERIVED from the window manager, never accepted alongside it.</b>
/// 📐 <c>WindowManager</c> already owns <c>GlobalMenu</c> and <c>MainToolbar</c> *(`:374`, `:394`)*, so a
/// context that took them as separate arguments could be built with a menu registry the window manager does
/// not use — ⛔ and it would compose perfectly and render nothing. ⚠ That is the same
/// present-but-disconnected shape as <c>BP-487</c>'s manifest cell; deriving makes it unrepresentable.</para>
///
/// <para>⭐ <b>Why this type exists at all, given it currently wraps one object:</b> it is the seam's stable
/// argument. Adding a registry later *(a status-bar section registry, say)* extends this type instead of
/// changing every bundle's signature.</para>
///
/// <para>⛔⛔ <b>What is deliberately NOT here:</b> no <c>ModuleHostKernel</c>, no <c>FdpEventBus</c>, no
/// system registry, no DDS participant. 📌 A bundle that wanted to register a module would have nothing to
/// register it with — which is how the standing constraint is meant to be enforced.</para>
/// </summary>
public sealed class UiBundleContext
{
    /// <param name="windows">
    /// The host's window manager. ⚠ Must be the SAME instance the host is registering into — the menu and
    /// toolbar registries are read off it.
    /// </param>
    public UiBundleContext(WindowManager windows)
        => Windows = windows ?? throw new ArgumentNullException(nameof(windows));

    /// <summary>⭐ Register a <c>ManagedWindow</c> here.</summary>
    public WindowManager Windows { get; }

    /// <summary>⭐ The global menu trie — DERIVED, so it cannot disagree with <see cref="Windows"/>.</summary>
    public GlobalMenuRegistry Menu => Windows.GlobalMenu;

    /// <summary>⭐ The main toolbar — DERIVED, same argument as <see cref="Menu"/>.</summary>
    public MainToolbarManager Toolbar => Windows.MainToolbar;
}

/// <summary>
/// ⭐⭐⭐ <b>The ONE place a bundle list is walked.</b> 📄 §5b.2's <c>sequenceDiagram</c>.
///
/// <para>⭐ Static and tiny on purpose. ⛔ It holds no state and decides nothing: the HOST decides which
/// bundles it composes *(the <c>ScenarioEditorModule</c> precedent — each host constructs and registers it
/// itself, never ambiently)*.</para>
/// </summary>
public static class UiBundleHost
{
    /// <summary>
    /// ⭐⭐ Compose <paramref name="bundles"/> in order into <paramref name="ctx"/>.
    ///
    /// <para>⛔⛔ <b>A throwing bundle is NAMED, never swallowed.</b> ⚠ This is the whole reason this method
    /// is not a one-line <c>foreach</c> at each call site: a bundle that fails silently is a feature that
    /// silently never appears — 📌 <b>precisely the class of defect this programme exists to end</b>
    /// *(`CE-046`…`CE-066`, six of which the user found by eye)*. ⭐ The bundle's
    /// <see cref="IUiBundle.Name"/> is in the message, so the failure names the feature rather than a stack
    /// frame in a registry.</para>
    ///
    /// <para>⭐ <b>Ordered, and the order is the host's.</b> ⚠ Registration order is observable — the menu
    /// trie has no ordering key, so items render in registration order *(the `global-menu` subset rail says
    /// so explicitly)*. ⇒ ⛔ this must not sort, reorder or parallelise.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// When a bundle throws. The inner exception is preserved.
    /// </exception>
    public static void Compose(IReadOnlyList<IUiBundle> bundles, UiBundleContext ctx)
    {
        if (bundles is null) throw new ArgumentNullException(nameof(bundles));
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        for (int i = 0; i < bundles.Count; i++)
        {
            var bundle = bundles[i];

            // ⛔ A null entry is a composition-root mistake, and saying so beats a NullReferenceException
            //   from inside a registry three frames down.
            if (bundle is null)
                throw new InvalidOperationException(
                    $"UI bundle at index {i} is null — the host's bundle list has a hole in it.");

            try
            {
                bundle.RegisterInto(ctx);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"UI bundle '{bundle.Name}' (index {i} of {bundles.Count}) threw while registering. "
                  + "⭐ Its feature would otherwise have been silently absent on this host.", ex);
            }
        }
    }
}
