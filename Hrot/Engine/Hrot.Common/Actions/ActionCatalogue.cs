using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Hrot.Common.Editing;

namespace Hrot.Common.Actions
{
    /// <summary>
    /// ⭐ B-11 A1 — ONE user action, declared once: its id (<c>GlobalActionIds</c>), label, group, the targets it applies to, its shortcut. What varies
    /// per entity is the declaration's <see cref="Offer"/> (one row, or null = not offered) or <see cref="Expand"/> (many rows — the orders).
    /// The BINDING — what runs — stays the host's <c>GlobalActionRegistry</c>.
    /// </summary>
    public sealed record ActionDecl(int Id, string Label, ActionGroup Group, int Order, ActionTarget Targets)
    {
        /// <summary>The key the menu shows and <c>MenuShortcuts</c> answers ("C", "Space"); null = none.</summary>
        public string? Shortcut { get; init; }

        /// <summary>⭐ B-11 A3 — the modes it is offered in (hidden elsewhere); default every mode.</summary>
        public ActionModes Modes { get; init; } = ActionModes.Always;

        /// <summary>⭐ A4 (R-4162, 📄 docs/UX/DESIGN_Action_Catalogue.md §4 A4) — whom a click on a SELECTED entity runs for: <see cref="ActionExecution.Selection"/>
        /// (default) = ONE request with the clicked entity, the handler reads the selection itself (a tool, a pick, a camera, Copy); <see cref="ActionExecution.PerEntity"/>
        /// = one request PER selected entity (the ingress fans out — Delete, Disembark).</summary>
        public ActionExecution Execution { get; init; } = ActionExecution.Selection;

        /// <summary>Whether it is offered in <paramref name="mode"/>.</summary>
        public bool OfferedIn(EditingMode mode) => (Modes & ModeBit(mode)) != 0;

        /// <summary>⭐ Seats H2 (📄 docs/UX/DESIGN_Seats_And_Hats.md §3.2, Action catalogue §2.1 <c>Level</c>, D4) — the LEAST privileged station level that may run
        /// it; a station below it sees the row GREYED with the reason (never missing, R-4119). Default by group (<see cref="DefaultLevel"/>).</summary>
        public EditingLevel Level { get; init; } = DefaultLevel(Group);

        /// <summary>⭐ Seats H2 — whether a side-scoped seat (a side controller, a player with a side) may run it only on ITS side's units. Default: the
        /// Orders and Embark groups.</summary>
        public bool SideScoped { get; init; } = Group is ActionGroup.Orders or ActionGroup.Embark;

        /// <summary>⭐ Seats H2 — a group's level: Edit and Destructive the Instructor (who brings in / removes units on a RUNNING exercise — the gate keeps
        /// Edit mode the Author's); Orders and Embark a side Controller; Run (the run-mode moves) the Instructor; Debug the Author (Q7: the Author's Expert
        /// switches); View anyone.</summary>
        public static EditingLevel DefaultLevel(ActionGroup group) => group switch
        {
            ActionGroup.Edit or ActionGroup.Destructive or ActionGroup.Run => EditingLevel.Instructor,
            ActionGroup.Orders or ActionGroup.Embark => EditingLevel.Controller,
            ActionGroup.Debug => EditingLevel.Full,
            _ => EditingLevel.None,
        };

        /// <summary>The <see cref="ActionModes"/> bit of one mode.</summary>
        public static ActionModes ModeBit(EditingMode mode) => mode switch
        {
            EditingMode.Edit => ActionModes.InEdit,
            EditingMode.Preview => ActionModes.InPreview,
            EditingMode.Live => ActionModes.InLive,
            _ => ActionModes.InReview,
        };

        /// <summary>The row's style ("destructive"), or null.</summary>
        public string? Style { get; init; }

        /// <summary>A dynamic row for this context (label, enabled, tooltip, children, checked), or null when not offered here.</summary>
        public Func<ActionContext, ContextMenuItemDto?>? Offer { get; init; }

        /// <summary>One declaration, many rows (the orders the entity's class fulfils); its rows replace the plain row.</summary>
        public Func<ActionContext, IEnumerable<ContextMenuItemDto>>? Expand { get; init; }

        /// <summary>The plain row: id, label, shortcut, style.</summary>
        public ContextMenuItemDto Row() => new() { Id = Id, Label = Label, Shortcut = Shortcut, Style = Style };
    }

    /// <summary>⭐ B-11 A1 — the facts about a unit that its menu depends on, read ONCE per menu (exactly what <c>MenuJsonFor</c> read).</summary>
    public readonly record struct UnitFacts(bool Degraded, bool Brain, bool Orders, DISEntityType Dis, bool IsUnit, bool Form, bool Manned,
                                            EmbarkOffer Embark, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags Pins, bool TraceOn,
                                            bool Plan = false);   // ⭐ W-1 / P-8 8d-2 — the unit has a mission plan (Edit plan on the map)

    /// <summary>⭐ B-11 A1 — what one menu is composed for: the entity, its target kind, the mode, and (for a unit) its facts.</summary>
    public readonly record struct ActionContext(ISimulationView View, Entity Entity, ActionTarget Target, EditingMode Mode, UnitFacts Unit)
    {
        /// <summary>⭐ A6 — a moment's target (timeline, playhead): the clicked time as the timeline labels it ("3:12"); null elsewhere.</summary>
        public string? Time { get; init; }
        /// <summary>⭐ A6 — the clicked moment in the timeline's own ticks (the binding seeks / reviews at it).</summary>
        public long AtTicks { get; init; }
        /// <summary>⭐ A6 — the lane of a clicked timeline event (its display name), or null.</summary>
        public string? Lane { get; init; }

        /// <summary>⭐ Seats H2 — the station's editing level (its seat); <see cref="EditingLevel.Full"/> where no seat is installed.</summary>
        public EditingLevel Level { get; init; }
        /// <summary>⭐ Seats H2 — the station's side scope (a side controller's side), null = every side.</summary>
        public ForceId? Side { get; init; }
        /// <summary>⭐ Seats H2 — the clicked unit's side, when the target is a unit.</summary>
        public ForceId? UnitSide { get; init; }

        /// <summary>⭐ Seats H2 — why this station may not run <paramref name="decl"/> here (greyed with it), or null. The ONE rule the composer applies.</summary>
        public string? SeatRefusal(ActionDecl decl)
        {
            if ((int)Level > (int)decl.Level) return SeatText(Level);
            if (Level == EditingLevel.Instructor && Mode == EditingMode.Edit && decl.Group is ActionGroup.Edit or ActionGroup.Destructive)
                return HrotEditGate.InstructorDoesNotAuthor;
            if (decl.SideScoped && Side is ForceId side && Target == ActionTarget.Unit && UnitSide != side)
                return "another side's unit: this station commands " + side.ToString();
            return null;
        }

        /// <summary>⭐ Seats H2 — the greyed row's reason for a station at <paramref name="level"/>.</summary>
        public static string SeatText(EditingLevel level) => level switch
        {
            EditingLevel.Instructor => "not for an instructor station",
            EditingLevel.Controller => "not for a side controller station",
            EditingLevel.Observer => "this station is an observer",
            EditingLevel.None => "not for this station's seat (a player or a reviewer)",
            _ => "not for this station's seat",
        };

        /// <summary>⭐ A6 — a context for a MOMENT (no view, no entity): the timeline's moment / event, the mode indicator's playhead.</summary>
        public static ActionContext ForMoment(ActionTarget target, EditingMode mode, string? time, long atTicks = 0, string? lane = null)
            => new(null!, Entity.Null, target, mode, default) { Time = time, AtTicks = atTicks, Lane = lane };
    }

    /// <summary>⭐ A4 — how a menu command on a multi-selection runs (<see cref="ActionDecl.Execution"/>).</summary>
    public enum ActionExecution : byte
    {
        /// <summary>One request with the clicked entity; the handler acts on the selection itself where it should (the safe default).</summary>
        Selection = 0,
        /// <summary>One request per selected entity when the clicked one is selected.</summary>
        PerEntity = 1,
    }

    /// <summary>⭐ B-11 A1 — the ONE catalogue of user actions in this process. Today's map menus are declared into it at first use.</summary>
    public sealed class ActionCatalogue
    {
        private readonly List<ActionDecl> _all = new();
        private readonly HashSet<(int, ActionTarget, string)> _keys = new();

        /// <summary>The process-wide catalogue, with today's map menu actions declared (<see cref="MapMenuActions"/>).</summary>
        public static ActionCatalogue Shared => SharedHolder.Instance;

        private static class SharedHolder
        {
            internal static readonly ActionCatalogue Instance = Build();
            private static ActionCatalogue Build() { var c = new ActionCatalogue(); MapMenuActions.DeclareInto(c); RuleMenuActions.DeclareInto(c); RunModeActions.DeclareInto(c); return c; }   // ⭐ CE-4308 — each feature declares its own (Q4)
        }

        /// <summary>Every declaration, in declaration order.</summary>
        public IReadOnlyList<ActionDecl> All => _all;

        /// <summary>⭐ A4 — whether action <paramref name="id"/> runs once per selected entity (any declaration of it says so). An undeclared id runs once.</summary>
        public bool RunsPerEntity(int id)
        {
            if (id == 0) return false;
            for (int i = 0; i < _all.Count; i++)
                if (_all[i].Id == id && _all[i].Execution == ActionExecution.PerEntity) return true;
            return false;
        }

        /// <summary>
        /// Declares an action. ⛔ The same id, targets and label declared twice throws — two declarations of one action is the duplicate the
        /// catalogue exists to prevent. (One id may appear with different targets — Center View on a unit and on an area.)
        /// </summary>
        public void Declare(ActionDecl decl)
        {
            if (!_keys.Add((decl.Id, decl.Targets, decl.Label)))
                throw new InvalidOperationException($"action {decl.Id} '{decl.Label}' for {decl.Targets} is already declared");
            _all.Add(decl);
        }
    }

    /// <summary>
    /// ⭐⭐ B-11 A1 (design §6) — builds a menu from the catalogue: the groups in their fixed order, each group's declarations for this target by
    /// <see cref="ActionDecl.Order"/>, a separator between non-empty groups. ⭐ Called on a right-click or a key press, never per frame (R-2084).
    /// </summary>
    public static class MenuComposer
    {
        private static readonly JsonSerializerOptions Options = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
        // ⭐ B-11 A3 (Editing UX §3.13 "order of groups: the most likely action first") — Edit first in Edit mode, Orders first while running,
        //   View first in Review; Debug and Destructive last. A group hidden in a mode simply offers nothing there (ActionDecl.Modes).
        //   ⭐ A6 — Run (the run-mode moves on a moment) first in every mode; no unit / graphic declaration is in it, so their menus do not move.
        private static readonly ActionGroup[] EditOrder = { ActionGroup.Run, ActionGroup.Edit, ActionGroup.Embark, ActionGroup.View, ActionGroup.Orders, ActionGroup.Debug, ActionGroup.Destructive };
        private static readonly ActionGroup[] RunningOrder = { ActionGroup.Run, ActionGroup.Orders, ActionGroup.Embark, ActionGroup.View, ActionGroup.Edit, ActionGroup.Debug, ActionGroup.Destructive };
        private static readonly ActionGroup[] ReviewOrder = { ActionGroup.Run, ActionGroup.View, ActionGroup.Orders, ActionGroup.Embark, ActionGroup.Edit, ActionGroup.Debug, ActionGroup.Destructive };

        /// <summary>The order the groups appear in, in <paramref name="mode"/>.</summary>
        public static ActionGroup[] GroupOrder(EditingMode mode)
            => mode == EditingMode.Edit ? EditOrder : mode == EditingMode.Review ? ReviewOrder : RunningOrder;

        /// <summary>The menu rows for <paramref name="context"/>.</summary>
        public static ContextMenuItemDto[] Compose(ActionContext context, ActionCatalogue? catalogue = null)
        {
            var all = (catalogue ?? ActionCatalogue.Shared).All;
            var items = new List<ContextMenuItemDto>();
            var group = new List<(int Order, int Index, ActionDecl Decl)>();
            foreach (var g in GroupOrder(context.Mode))
            {
                group.Clear();
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == g && (all[i].Targets & context.Target) != 0 && all[i].OfferedIn(context.Mode)) group.Add((all[i].Order, i, all[i]));
                group.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Index.CompareTo(b.Index));
                int before = items.Count;
                bool needSeparator = before > 0;
                foreach (var (_, _, decl) in group)
                {
                    var refusal = context.SeatRefusal(decl);   // ⭐ Seats H2 — below the seat: greyed with why, never missing
                    if (decl.Expand != null)
                    {
                        foreach (var row in decl.Expand(context)) Add(row, refusal);
                        continue;
                    }
                    var one = decl.Offer != null ? decl.Offer(context) : decl.Row();
                    if (one != null) Add(one, refusal);
                }

                void Add(ContextMenuItemDto row, string? refusal)
                {
                    if (needSeparator) { items.Add(new ContextMenuItemDto { IsSeparator = true }); needSeparator = false; }
                    items.Add(refusal == null || row.IsSeparator == true ? row : Greyed(row, refusal));
                }
            }
            return items.ToArray();
        }

        /// <summary>⭐ Seats H2 — <paramref name="row"/> greyed with <paramref name="reason"/> (its submenu dropped: nothing under it can run).</summary>
        public static ContextMenuItemDto Greyed(ContextMenuItemDto row, string reason) => new()
        {
            Id = row.Id, Label = row.Label, Icon = row.Icon, Style = row.Style, Shortcut = row.Shortcut, IsSeparator = row.IsSeparator,
            AllowedInIdle = row.AllowedInIdle, Priority = row.Priority, IsChecked = row.IsChecked, Enabled = false, Tooltip = reason,
        };

        /// <summary>⭐ B-11 A2 — the reason a row this station cannot run is greyed with.</summary>
        public const string NotOnThisStation = "Not available on this station";

        /// <summary>
        /// ⭐⭐ B-11 A2 (design §2.1 "binding", §7) — the rows as THIS station offers them: a row whose action <paramref name="runsHere"/> says no is
        /// GREYED with <see cref="NotOnThisStation"/> (a gap to fix, R-4119 — ⛔ never silently clickable, never silently hidden); a row already greyed
        /// keeps its own reason; submenus are walked. Returns new rows; the input is untouched.
        /// </summary>
        public static ContextMenuItemDto[] ForStation(ContextMenuItemDto[] rows, Func<int, bool> runsHere)
        {
            var result = new ContextMenuItemDto[rows.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                bool grey = r.Id != 0 && r.IsSeparator != true && !runsHere(r.Id) && r.Enabled != false;
                var children = r.Children is { } c ? ForStation(c, runsHere) : null;
                result[i] = !grey && children == null ? r : new ContextMenuItemDto
                {
                    Id = r.Id, Label = r.Label, Icon = r.Icon, Style = r.Style, Shortcut = r.Shortcut, IsSeparator = r.IsSeparator,
                    AllowedInIdle = r.AllowedInIdle, Priority = r.Priority, IsChecked = r.IsChecked, Children = children,
                    Enabled = grey ? false : r.Enabled,
                    Tooltip = grey ? NotOnThisStation : r.Tooltip,
                };
            }
            return result;
        }

        /// <summary>The menu JSON for <paramref name="context"/> — what <c>ContextMenuProjectorGizmo.MenuJsonFor</c> returns.</summary>
        public static string Json(ActionContext context, ActionCatalogue? catalogue = null)
            => JsonSerializer.Serialize(Compose(context, catalogue), Options);

        /// <summary>⭐ B-11 A2 — the menu JSON as this station offers it (<see cref="ForStation"/>).</summary>
        public static string Json(ActionContext context, Func<int, bool> runsHere, ActionCatalogue? catalogue = null)
            => JsonSerializer.Serialize(ForStation(Compose(context, catalogue), runsHere), Options);
    }
}
