using Fdp.Core;

namespace Hrot.UI.Common.Rules;

/// <summary>
/// ⭐ U-18 / RX2 (docs/UX/DESIGN_Rule_Editor_UX.md §2.2, §2.2.1; R-4118) — the rule's detail drawn in the DETAILS panel through the window that owns the ONE
/// <see cref="RuleDetails"/> form (the Rules window). The Details tab owns no form, so the hosts cannot get different rule details by construction.
/// </summary>
public interface IRuleDetailsHost
{
    /// <summary>Whether <paramref name="e"/> is a rule this host can show (a row of its view).</summary>
    bool Offers(Entity e);

    /// <summary>Draws the rule's tabs (or its overview) inside the caller's window; selects it in the host's view.</summary>
    void DrawDetails(Entity e);

    /// <summary>The Details panel hosts the detail: the window drops its own pane and a pick selects the RULE entity (was: its subject). Idempotent.</summary>
    void AttachToDetailsPanel();
}
