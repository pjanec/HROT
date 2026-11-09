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
    private readonly Func<Fdp.Presentation.Editing.IMapPickContext?>? _pick;
    private readonly Action? _after;

    /// <param name="after">⭐ CE-4346 — the host's block under a WORLD form (the editor's Parameters header under the Scenario form); null = none.</param>
    public FormDetailsView(FormDecl form, Func<EntityRepository?> world, Func<Fdp.Presentation.Editing.IMapPickContext?>? pick = null, Action? after = null) { _form = form; _world = world; _pick = pick; _after = after; }

    /// <summary>The one view (rails, the panel probe).</summary>
    public FormView View => _view;

    /// <summary>§2.4 — the ONE editable rule: not on a recording (the run state, or a world replaying).</summary>
    public static bool Editable(VariableRunState mode, EntityRepository world)
        => mode != VariableRunState.Replay && !Fdp.Toolkit.Replay.ReplayState.IsActive(world);

    private readonly Entity[] _target = new Entity[1];

    public void Draw(DetailsContext context, string idScope)
    {
        if (_world() is not { } world) return;
        _view.Pick = _pick?.Invoke();   // ⭐ U-23 / D4 — a list's add picks through the host's one pick context
        if (_form.WorldTarget != null)
        {
            // a WORLD form (Scenario): nothing selected — the world's own entity (one reused array: 0 B)
            _target[0] = _form.WorldTarget(world);
            if (_target[0].IsNull) return;
            _view.Draw(_form, world, _target, Editable(context.Mode, world));
            _after?.Invoke();
            return;
        }
        if (context.Entities.Count == 0) return;
        _view.Draw(_form, world, context.Entities, Editable(context.Mode, world));
    }

    public void Dispose() { }
}

/// <summary>⭐ U-23 — the descriptor of a form's tab: ranked just above Summary (35) by the form's <c>Order</c>.</summary>
public static class FormDetailsViewDescriptor
{
    public static DetailsViewDescriptor For(FormDecl form, Func<EntityRepository?> world, Func<Fdp.Presentation.Editing.IMapPickContext?>? pick = null, Action? after = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(world);
        var instance = new FormDetailsView(form, world, pick, after);
        return new DetailsViewDescriptor(
            Id:        "details." + form.Id,
            Title:     form.Title,
            Rank:      34,
            AppliesTo: ctx => world() is { } w && (form.WorldTarget != null
                ? ctx.Entities.Count == 0 && !form.WorldTarget(w).IsNull                 // the Scenario form: nothing selected
                : ctx.Entities.Count > 0 && (PureRequires(form) || OffersAll(form, w, ctx))),
            Create:    () => instance)
        {
            // ⭐ B-11 A7 — a form offered by its `Requires` alone declares them on the TAB (the registry ANDs them over the selection); a form with an
            //   any-of `Offers` (Shape) keeps that predicate in AppliesTo — a requirement list cannot say "this or that"
            Requires = PureRequires(form) ? form.Requires : System.Array.Empty<Type>(),
        };
    }

    private static bool PureRequires(FormDecl form) => form.WorldTarget == null && form.Offers == null && form.Requires.Count > 0;

    private static bool OffersAll(FormDecl form, EntityRepository w, DetailsContext ctx)
    {
        for (int i = 0; i < ctx.Entities.Count; i++) if (!form.OffersFor(w, ctx.Entities[i])) return false;
        return true;
    }
}
