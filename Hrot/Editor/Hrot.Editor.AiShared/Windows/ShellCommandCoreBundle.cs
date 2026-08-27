using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Runner;
// ⚠ IIconProvider is NodeEditor's, not FDP's — the same using CgfEditorShellToolbar carries.
using NodeEditor.Core.Interfaces;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// ⭐⭐⭐ <b>PHASE 1's FIRST BUNDLE — the shell's common command core, as an <see cref="IUiBundle"/>.</b>
/// 📄 <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5b · <c>DESIGN_Cgf_Shell_Command_Toolbar_Slice.md</c>.
///
/// <para>⭐⭐ <b>Chosen as the first adopter on a MEASUREMENT, and it is NOT the one the design named.</b>
/// §5b.3 item ② proposed <c>SharedAiWindowRegistrar</c> *"the cheapest real adopter"* because it is
/// DI-wired and host-unused. 📐 Measured `2026-08-27`: of its **7** windows, CGF constructs **0** and the
/// editor **3**. ⇒ ⛔ adopting it would not be composing an existing bundle — it would be newly
/// constructing seven windows on CGF, which is a question about **CGF's ROLE**, not a composition
/// mechanic. ⭐ This bundle is the honest cheapest: <b>both hosts already register through
/// <see cref="CgfEditorShellToolbar.RegisterCommonCore"/></b>, byte for byte.</para>
///
/// <para>⭐⭐⭐ <b>What wrapping it actually BUYS — this is not ceremony.</b> 📐 Today each host passes the
/// toolbar and the menu as SEPARATE arguments, and must remember to take both off the same
/// <c>WindowManager</c>:
/// <code>
/// RegisterCommonCore(wm.ShellCommands, wm.MainToolbar, icons, services, wm.GlobalMenu, null)
/// </code>
/// ⛔ Nothing stopped a host passing one host's toolbar and another's menu — and the result would compose
/// perfectly and render half. ⭐ <see cref="UiBundleContext"/> DERIVES both from the one window manager, so
/// that divergence becomes unrepresentable. ⚠ Same argument that made the context derive them rather than
/// accept them; 📌 same shape as <c>BP-487</c>'s present-but-disconnected manifest cell.</para>
///
/// <para>⛔⛔ <b>Compliant with the standing constraint by construction</b> *(§3.2)*: it registers command
/// descriptors, toolbar entries and menu items. ⚠ It CANNOT register a module, system or translator —
/// <see cref="UiBundleContext"/> hands out nothing to register them with.</para>
///
/// <para>⚠ <b>The per-host SUBSET is unchanged and still DERIVED.</b> <c>RegisterCommonCore</c> emits an
/// entry only for a command the host's shell can service, so the editor still gets more buttons than CGF
/// from the same table *(ruling 58: no host list, no <c>if (host==…)</c>)*. ⛔ This bundle adds no
/// filtering of its own.</para>
/// </summary>
public sealed class ShellCommandCoreBundle : IUiBundle
{
    private readonly ShellEditorCommands _shell;
    private readonly IIconProvider? _icons;
    private readonly CgfEditorShellToolbar.HostServices _services;
    private readonly string? _menuPerspective;

    /// <param name="shell">The host's shell command registry — <c>WindowManager.ShellCommands</c>.</param>
    /// <param name="icons">
    /// ⭐ The host's icon provider, or <see langword="null"/> for no icons.
    /// ⚠ Stays a constructor argument rather than coming off the context: it is built from the host's
    /// texture atlas, which is a host concern the bundle should not reach into.
    /// </param>
    /// <param name="services">
    /// ⭐⭐ WHAT THIS HOST CAN SERVICE — the whole subset mechanism. A <see langword="null"/> member means
    /// *"this host cannot do that"*, and the shared table then emits neither a descriptor nor a button for
    /// it. ⛔ Not a feature flag: it is a statement of capability *(ruling 49)*.
    /// </param>
    /// <param name="menuPerspective">
    /// ⭐ Menu scope; <see langword="null"/> *(default)* is GLOBAL, which is the settled decision for the
    /// common core's File items. ⛔ Do not bind today's core to a perspective — see
    /// <c>RegisterCommonCore</c>'s own remarks.
    /// </param>
    public ShellCommandCoreBundle(
        ShellEditorCommands shell,
        IIconProvider? icons,
        CgfEditorShellToolbar.HostServices services,
        string? menuPerspective = null)
    {
        _shell           = shell   ?? throw new ArgumentNullException(nameof(shell));
        _services        = services ?? throw new ArgumentNullException(nameof(services));
        _icons           = icons;
        _menuPerspective = menuPerspective;
    }

    /// <inheritdoc/>
    public string Name => "shell-command-core";

    /// <summary>
    /// ⭐ The ids that actually got a toolbar entry, in sort order — set by <see cref="RegisterInto"/>.
    /// <para>⚠⚠ EXPOSED because <c>RegisterCommonCore</c> RETURNS them and both hosts log them today;
    /// ⛔ <see cref="IUiBundle.RegisterInto"/> is <c>void</c>, so dropping them would delete a diagnostic
    /// the hosts already print. 📌 A bundle seam that silently loses information the direct call gave you
    /// is a worse seam, not a cleaner one.</para>
    /// <para>⭐ <see langword="null"/> until composed.</para>
    /// </summary>
    public IReadOnlyList<string>? RegisteredToolbarIds { get; private set; }

    /// <inheritdoc/>
    public void RegisterInto(UiBundleContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        // ⭐⭐ Toolbar AND menu from the SAME context ⇒ they cannot be two different hosts' registries.
        //    📌 That is the one behavioural improvement this wrapper makes; everything else is unchanged.
        RegisteredToolbarIds = CgfEditorShellToolbar.RegisterCommonCore(
            _shell,
            ctx.Toolbar,
            _icons,
            _services,
            ctx.Menu,
            _menuPerspective);
    }
}
