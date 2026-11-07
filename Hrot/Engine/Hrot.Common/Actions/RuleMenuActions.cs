using Hrot.Common.Constants;

namespace Hrot.Common.Actions
{
    /// <summary>
    /// ⭐ CE-4308 (RX6, docs/UX/DESIGN_Rule_Editor_UX.md §2.5; Action Catalogue Q4 "each feature pack declares its actions") — the rules' entries on the map's
    /// menus: <i>Add rule about this unit…</i> on a unit and <i>When someone enters this area…</i> on an area, authoring only. Each opens the Rules gallery with
    /// the unit / area filled in; the BINDING is the rules bundle's (every host that composes it), so a host without it greys the row (A2).
    /// </summary>
    public static class RuleMenuActions
    {
        /// <summary>After the Edit group's own rows.</summary>
        private const int Order = 50;

        public static void DeclareInto(ActionCatalogue catalogue)
        {
            catalogue.Declare(new ActionDecl(GlobalActionIds.AddRuleAboutUnit, "Add rule about this unit…", ActionGroup.Edit, Order, ActionTarget.Unit) { Modes = ActionModes.InEdit });
            catalogue.Declare(new ActionDecl(GlobalActionIds.AddRuleForArea, "When someone enters this area…", ActionGroup.Edit, Order, ActionTarget.Area) { Modes = ActionModes.InEdit });
        }
    }
}
