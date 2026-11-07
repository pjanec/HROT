using System;
using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;

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
                                            EmbarkOffer Embark, Fdp.Toolkit.Behavior.Diagnostics.AiOverlayFlags Pins, bool TraceOn);

    /// <summary>⭐ B-11 A1 — what one menu is composed for: the entity, its target kind, the mode, and (for a unit) its facts.</summary>
    public readonly record struct ActionContext(ISimulationView View, Entity Entity, ActionTarget Target, EditingMode Mode, UnitFacts Unit);

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
            private static ActionCatalogue Build() { var c = new ActionCatalogue(); MapMenuActions.DeclareInto(c); return c; }
        }

        /// <summary>Every declaration, in declaration order.</summary>
        public IReadOnlyList<ActionDecl> All => _all;

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
        private static readonly ActionGroup[] Groups = (ActionGroup[])Enum.GetValues(typeof(ActionGroup));

        /// <summary>The menu rows for <paramref name="context"/>.</summary>
        public static ContextMenuItemDto[] Compose(ActionContext context, ActionCatalogue? catalogue = null)
        {
            var all = (catalogue ?? ActionCatalogue.Shared).All;
            var items = new List<ContextMenuItemDto>();
            var group = new List<(int Order, int Index, ActionDecl Decl)>();
            foreach (var g in Groups)
            {
                group.Clear();
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == g && (all[i].Targets & context.Target) != 0) group.Add((all[i].Order, i, all[i]));
                group.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Index.CompareTo(b.Index));
                int before = items.Count;
                bool needSeparator = before > 0;
                foreach (var (_, _, decl) in group)
                {
                    if (decl.Expand != null)
                    {
                        foreach (var row in decl.Expand(context)) Add(row);
                        continue;
                    }
                    var one = decl.Offer != null ? decl.Offer(context) : decl.Row();
                    if (one != null) Add(one);
                }

                void Add(ContextMenuItemDto row)
                {
                    if (needSeparator) { items.Add(new ContextMenuItemDto { IsSeparator = true }); needSeparator = false; }
                    items.Add(row);
                }
            }
            return items.ToArray();
        }

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
