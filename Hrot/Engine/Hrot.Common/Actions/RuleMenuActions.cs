using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Common.Constants;
using Hrot.Common.Editing;

namespace Hrot.Common.Actions
{
    /// <summary>
    /// ⭐ CE-4308 (RX6, docs/UX/DESIGN_Rule_Editor_UX.md §2.5; Action Catalogue Q4 "each feature pack declares its actions") — the rules' entries on the map's
    /// menus: <i>Add rule about this unit…</i> on a unit and <i>When someone enters this area…</i> on an area, authoring only. Each opens the Rules gallery with
    /// the unit / area filled in; the BINDING is the rules bundle's (every host that composes it), so a host without it greys the row (A2).
    /// <para>⭐ CE-2423 (A6b, 📄 docs/UX/DESIGN_Action_Catalogue.md §9) — and the RULE target's rows (a rule row of the Rules window, a rule track of its timeline): the WORDS are here,
    /// ONCE (<see cref="FireNow"/> …); the BINDING (what runs, why greyed) is <c>RuleActionBinding</c> in Hrot.Presentation.</para>
    /// </summary>
    public static class RuleMenuActions
    {
        /// <summary>After the Edit group's own rows.</summary>
        private const int Order = 50;

        // ── the words of the Rule target — ONE place ───────────────────────────────────────────────────────
        public const string FireNow = "Fire now", Disarm = "Disarm", Rearm = "Re-arm", Reset = "Reset", Activate = "Activate", Deactivate = "Deactivate";
        public const string EditRule = "Edit", DisableRule = "Disable", DeleteRule = "Delete", ShowOnMap = "Show on map", ShowHistory = "Show history";

        public static void DeclareInto(ActionCatalogue catalogue)
        {
            DeclareRule(catalogue);
            catalogue.Declare(new ActionDecl(GlobalActionIds.AddRuleAboutUnit, "Add rule about this unit…", ActionGroup.Edit, Order, ActionTarget.Unit) { Modes = ActionModes.InEdit });
            catalogue.Declare(new ActionDecl(GlobalActionIds.AddRuleForArea, "When someone enters this area…", ActionGroup.Edit, Order, ActionTarget.Area) { Modes = ActionModes.InEdit });
        }

        private static ContextMenuItemDto Row(int id, string label) => new() { Id = id, Label = label };

        /// <summary>
        /// ⭐ CE-2423 (Editing UX §3.13 "rule (row or track)"): Edit mode — Edit · Disable · Delete; Live — the controls (a TRIGGER: Fire now · Disarm · Re-arm · Reset; an objective or
        /// measure: Activate · Deactivate · Reset), the role riding in <see cref="ActionContext.RuleRole"/>; Review — Show on map · Show history. ⭐ The controls are declared in EVERY mode
        /// (an authored row, a recording: they stay listed and GREY with the reason — RV-3a / RV-5, the rule window's rails); Show on map in every mode; Show history in Review.
        /// The controls are the INSTRUCTOR's (<see cref="EditingLevel.Instructor"/>, the origin they are written with), not a side controller's.
        /// </summary>
        private static void DeclareRule(ActionCatalogue c)
        {
            const ActionTarget R = ActionTarget.Rule;
            ActionDecl Control(int id, string label, int order, System.Func<ActionContext, bool> offered) => new(id, label, ActionGroup.Orders, order, R)
                { Level = EditingLevel.Instructor, SideScoped = false, Offer = ctx => offered(ctx) ? Row(id, label) : null };
            static bool Trigger(ActionContext x) => x.RuleRole == ObjectiveRole.Trigger;
            static bool NotTrigger(ActionContext x) => x.RuleRole != ObjectiveRole.Trigger;
            static bool Any(ActionContext x) => true;

            c.Declare(Control(GlobalActionIds.RuleFire, FireNow, 10, Trigger));
            c.Declare(Control(GlobalActionIds.RuleDisarm, Disarm, 11, Trigger));
            c.Declare(Control(GlobalActionIds.RuleRearm, Rearm, 12, Trigger));
            c.Declare(Control(GlobalActionIds.RuleActivate, Activate, 10, NotTrigger));
            c.Declare(Control(GlobalActionIds.RuleDeactivate, Deactivate, 11, NotTrigger));
            c.Declare(Control(GlobalActionIds.RuleReset, Reset, 20, Any));
            c.Declare(new ActionDecl(GlobalActionIds.RuleShowOnMap, ShowOnMap, ActionGroup.View, 10, R) { Level = EditingLevel.None });
            c.Declare(new ActionDecl(GlobalActionIds.RuleShowHistory, ShowHistory, ActionGroup.View, 20, R) { Modes = ActionModes.InReview, Level = EditingLevel.None });
            c.Declare(new ActionDecl(GlobalActionIds.RuleEdit, EditRule, ActionGroup.Edit, 10, R) { Modes = ActionModes.InEdit });
            c.Declare(new ActionDecl(GlobalActionIds.RuleDisable, DisableRule, ActionGroup.Edit, 20, R) { Modes = ActionModes.InEdit });
            c.Declare(new ActionDecl(GlobalActionIds.RuleDelete, DeleteRule, ActionGroup.Destructive, 10, R) { Modes = ActionModes.InEdit, Style = "destructive" });
        }
    }
}
