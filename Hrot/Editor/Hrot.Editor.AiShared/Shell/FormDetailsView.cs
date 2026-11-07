using System;
using Fdp.Core;
using Fdp.Presentation.Forms;
using Hrot.Editor.AiShared.Variables;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐ U-23 (docs/UX/DESIGN_Details_Forms.md §2.1, §2.4, D3) — a Details tab per DECLARED form (<see cref="FormCatalogue"/>): offered when every selected entity
/// carries the form's <c>Requires</c>, drawn by the one <see cref="FormView"/>. ⭐ Editable is ONE function of the run state (§2.4): a recording is read-only on
/// every host by construction (the bundle builds this view for the editor, CGF, SimHost, the player and the Replay Browser alike).
/// </summary>
public sealed class FormDetailsView : IDetailsViewInstance
{
    private readonly FormDecl _form;
    private readonly Func<EntityRepository?> _world;
    private readonly FormView _view = new();

    public FormDetailsView(FormDecl form, Func<EntityRepository?> world) { _form = form; _world = world; }

    /// <summary>The one view (rails, the panel probe).</summary>
    public FormView View => _view;

    /// <summary>§2.4 — the ONE editable rule: not on a recording (the run state, or a world replaying).</summary>
    public static bool Editable(VariableRunState mode, EntityRepository world)
        => mode != VariableRunState.Replay && !Fdp.Toolkit.Replay.ReplayState.IsActive(world);

    public void Draw(DetailsContext context, string idScope)
    {
        if (_world() is not { } world || context.Entities.Count == 0) return;
        _view.Draw(_form, world, context.Entities, Editable(context.Mode, world));
    }

    public void Dispose() { }
}

/// <summary>⭐ U-23 — the descriptor of a form's tab: ranked just above Summary (35) by the form's <c>Order</c>.</summary>
public static class FormDetailsViewDescriptor
{
    public static DetailsViewDescriptor For(FormDecl form, Func<EntityRepository?> world)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(world);
        var instance = new FormDetailsView(form, world);
        return new DetailsViewDescriptor(
            Id:        "details." + form.Id,
            Title:     form.Title,
            Rank:      34,
            AppliesTo: ctx => world() is { } w && ctx.Entities.Count > 0 && OffersAll(form, w, ctx),
            Create:    () => instance);
    }

    private static bool OffersAll(FormDecl form, EntityRepository w, DetailsContext ctx)
    {
        for (int i = 0; i < ctx.Entities.Count; i++) if (!form.OffersFor(w, ctx.Entities[i])) return false;
        return true;
    }
}
