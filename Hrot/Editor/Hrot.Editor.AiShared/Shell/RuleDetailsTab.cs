using System;
using Hrot.UI.Common.Rules;

namespace Hrot.Editor.AiShared.Shell;

/// <summary>
/// ⭐ U-18 / RX2 (docs/UX/DESIGN_Rule_Editor_UX.md §2.2, §2.2.1; R-4118) — the <b>Rule</b> Details tab: offered when ONE selected entity is a rule of the host's
/// Rules window, drawn THROUGH that window (<see cref="IRuleDetailsHost"/>) — the tab owns no form, so the editor, CGF and the Replay Browser cannot show
/// different rule details by construction. The window's own pane goes when the tab is wired (<see cref="IRuleDetailsHost.AttachToDetailsPanel"/>).
/// </summary>
public sealed class RuleDetailsTab : IDetailsViewInstance
{
    private readonly Func<IRuleDetailsHost?> _host;

    public RuleDetailsTab(Func<IRuleDetailsHost?> host) { _host = host; }

    public void Draw(DetailsContext context, string idScope)
    {
        if (context.Entities.Count != 1 || _host() is not { } h) return;
        h.DrawDetails(context.Entities[0]);
    }

    public void Dispose() { }
}

/// <summary>⭐ U-18 / RX2 — the descriptor: rank 33 (just above the declared forms, 34); a rule has no other form.</summary>
public static class RuleDetailsTabDescriptor
{
    public const string Id = "details.rule";

    public static DetailsViewDescriptor For(Func<IRuleDetailsHost?> host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var instance = new RuleDetailsTab(host);
        return new DetailsViewDescriptor(
            Id:        Id,
            Title:     "Rule",
            Rank:      33,
            AppliesTo: ctx =>
            {
                if (host() is not { } h) return false;
                h.AttachToDetailsPanel();   // the first time the panel asks, the window learns its detail lives here (idempotent)
                return ctx.Entities.Count == 1 && h.Offers(ctx.Entities[0]);
            },
            Create:    () => instance);
    }
}
