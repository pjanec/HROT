using System;
using System.Linq;
using Fdp.Core;
using Hrot.Common.Actions;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Xunit;

namespace Hrot.Presentation.Tests.Gizmos;

/// <summary>⭐ B-11 A1 (docs/UX/DESIGN_Action_Catalogue.md §6) — the catalogue's own rules, on a hand-built catalogue.</summary>
public sealed class TheActionCatalogueComposesMenusTests
{
    private static ActionContext For(ActionTarget target) => new(null!, Entity.Null, target, EditingMode.Live, default);

    /// <summary>⭐ One action, one declaration: declaring it twice for the same targets throws. ✅ Red-proof: Declare not checking ⇒ no throw.</summary>
    [Fact]
    public void DeclaringOneActionTwice_Throws()
    {
        var c = new ActionCatalogue();
        c.Declare(new ActionDecl(10, "Center View", ActionGroup.View, 0, ActionTarget.Unit));
        Assert.Throws<InvalidOperationException>(() => c.Declare(new ActionDecl(10, "Center View", ActionGroup.View, 0, ActionTarget.Unit)));
        c.Declare(new ActionDecl(10, "Center View", ActionGroup.View, 0, ActionTarget.Area));   // the same action for another target is fine
    }

    /// <summary>
    /// ⭐ The separator rule: groups in their fixed order, a separator between two NON-EMPTY groups only — never leading, trailing or doubled when a
    /// group offers nothing; rows within a group by Order; a declaration only for its targets. ✅ Red-proof: a separator after every group ⇒ the
    /// empty Embark group adds a second one.
    /// </summary>
    [Fact]
    public void Separators_StandOnlyBetweenNonEmptyGroups_InTheFixedGroupOrder()
    {
        var c = new ActionCatalogue();
        c.Declare(new ActionDecl(3, "Delete", ActionGroup.Destructive, 0, ActionTarget.Unit));
        c.Declare(new ActionDecl(2, "Select", ActionGroup.View, 1, ActionTarget.Unit));
        c.Declare(new ActionDecl(1, "Center", ActionGroup.View, 0, ActionTarget.Unit));
        c.Declare(new ActionDecl(9, "Embark", ActionGroup.Embark, 0, ActionTarget.Unit) { Offer = _ => null });   // offers nothing here
        c.Declare(new ActionDecl(7, "Edit Route", ActionGroup.Edit, 0, ActionTarget.Route));                      // not this target
        c.Declare(new ActionDecl(0, "Orders", ActionGroup.Orders, 0, ActionTarget.Unit)
        {
            Expand = _ => new[] { new ContextMenuItemDto { Id = 400, Label = "Attack" }, new ContextMenuItemDto { Id = 402, Label = "Move" } },
        });

        var rows = MenuComposer.Compose(For(ActionTarget.Unit), c);
        Assert.Equal(new[] { "Attack", "Move", "—", "Center", "Select", "—", "Delete" },
                     rows.Select(r => r.IsSeparator == true ? "—" : r.Label!).ToArray());
    }
}
