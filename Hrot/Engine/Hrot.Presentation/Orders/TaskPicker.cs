using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Fdp.Core;
using Hrot.Common.Orders;
using Hrot.Core.Tkb;
using Hrot.Map.Definitions.Behavior;
using Hrot.UI.Common.Facades;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;

namespace Hrot.Presentation.Orders;

/// <summary>What a pick of <see cref="TaskPicker"/> is: Wait · an order of the unit's class · a registered behaviour.</summary>
public enum TaskPickKind { Wait, Order, Behaviour }

/// <summary>⭐ CE-4359 — the picker's answer: <see cref="Name"/> is the order name (<c>Move</c>) or the behaviour's registry name (<c>FightFromCover</c>); empty for Wait.</summary>
public sealed record TaskPick(TaskPickKind Kind, string Name);

/// <summary>What the preview pane shows for one entry — every string built ONCE when the picker opens (the draw allocates nothing).</summary>
public sealed record TaskInfo(TaskPick Pick, string Title, string Subtitle, string Description, IReadOnlyList<(string Label, string Value)> Params, string Fits, bool Hidden);

/// <summary>
/// ⭐ CE-4359 (docs/UX/DESIGN_Mission_Plan_Editor.md §5.13, Q-P5) — a code name as the words an instructor says: <c>MoveToLocation</c> → <i>Move to location</i>; an order without its class
/// (<c>PersonMove</c> is the order <c>Move</c>); <c>ObserveFire</c> → <i>Observe and fire</i>. ONE helper — the code name stays in the preview's grey line.
/// </summary>
public static class TaskNames
{
    private static readonly Regex Split = new(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|_", RegexOptions.Compiled);

    /// <summary><c>MoveToLocation</c> → <c>Move to location</c> (an all-capitals token such as HSM stays as it is).</summary>
    public static string Words(string code)
    {
        if (string.IsNullOrEmpty(code)) return "";
        var parts = Split.Split(code);
        var sb = new StringBuilder(code.Length + 6);
        foreach (var raw in parts)
        {
            if (raw.Length == 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            bool acronym = raw.Length >= 2 && raw == raw.ToUpperInvariant() && !char.IsDigit(raw[1]);
            sb.Append(sb.Length == 0 || acronym ? raw : char.ToLowerInvariant(raw[0]) + raw.Substring(1));
        }
        if (sb.Length > 0) sb[0] = char.ToUpperInvariant(sb[0]);
        return sb.ToString();
    }

    /// <summary>An order's name as the picker shows it: <c>Move</c> · <c>Hold</c> · <c>Attack</c> · <c>ObserveFire</c> → <i>Observe and fire</i>.</summary>
    public static string OrderWords(string orderName) => orderName == "ObserveFire" ? "Observe and fire" : Words(orderName);
}

/// <summary>
/// ⭐⭐ CE-4359 (§5.13, Q-P1…Q-P5; mockup <c>docs/UX/task-picker-visual.svg</c>) — the TASK PICKER behind <i>+ Add task</i>: the ONE <see cref="PickerWindow"/> (Tree layout, fuzzy search, side pane), fed with
/// what FITS the unit (CE-4350) minus the tests / demos / samples (behind the <i>Show tests and demos (N)</i> tick, off) and minus what is not a task (<c>Operator</c>, <c>Library/Sop</c>).
/// Folders in a FIXED order, the generic first: <i>Recent</i> (the picker's own) · <i>Orders — the unit's own</i> (the class's generic orders, found by the order resolver, in the fixed order Move · Hold · Attack ·
/// Observe and fire) · <i>Movement</i> · <i>Combat</i> · <i>Weapons and vehicles</i> · <i>Casualties and engineering</i> · <i>Crowd and surrender</i> · <i>Reactions</i> · <i>My behaviours</i> (authored assets, by their own group).
/// ⭐ A curated behaviour's folder is its <c>Group</c> (<c>Library/Movement</c>) with <c>Library/</c> stripped — ONE field. The tree is built once at open; a drawn frame allocates nothing.
/// </summary>
public static class TaskPicker
{
    public const string ContextKey = "orders.add-task";
    public const string OrdersFolder = "Orders — the unit's own";
    public const string MyBehaviours = "My behaviours";
    public const string HideLabel = "Show tests and demos";

    /// <summary>The root folders, in the fixed order (the unit's generic orders first).</summary>
    public static readonly IReadOnlyList<string> FolderOrder = new[]
    {
        OrdersFolder, "Movement", "Combat", "Weapons and vehicles", "Casualties and engineering", "Crowd and surrender", "Reactions", MyBehaviours,
    };

    private static readonly string[] OrderRank = { "Move", "Hold", "Attack", "ObserveFire" };

    /// <summary>Tests, demos and samples: hidden until the tick (<c>Tests/*</c>, <c>Demos/*</c>, <c>Samples*</c>).</summary>
    public static bool IsHiddenGroup(string? group)
        => group != null && (group.StartsWith("Tests", StringComparison.Ordinal) || group.StartsWith("Demos", StringComparison.Ordinal) || group.StartsWith("Samples", StringComparison.Ordinal));

    /// <summary>Not a task: the operator's possession and the SOPs (they run beside the task, in their own slot).</summary>
    public static bool IsNotATask(string? group) => group is "Operator" || (group != null && group.StartsWith("Library/Sop", StringComparison.Ordinal));

    /// <summary>The picker folder of a behaviour's group: <c>Library/Movement</c> → <c>Movement</c>; any other group is an authored asset's own folder under <i>My behaviours</i>.</summary>
    public static string FolderOf(string? group)
    {
        if (string.IsNullOrEmpty(group) || group == BehaviorChoice.OtherGroup) return MyBehaviours;
        if (group.StartsWith("Library/", StringComparison.Ordinal))
        {
            string f = group.Substring("Library/".Length);
            return f == "Orders" ? OrdersFolder : f;
        }
        return IsHiddenGroup(group) ? group : MyBehaviours + "/" + group;
    }

    /// <summary>The picker's entries for a unit of DIS type <paramref name="dis"/>. Allocates — built once, when the picker opens.</summary>
    public static List<PickerEntry> Entries(DISEntityType dis, IReadOnlyList<BehaviorChoice> behaviours, ParamsDtoRegistry registry)
    {
        var list = new List<PickerEntry>();
        var skip = new HashSet<string>(StringComparer.Ordinal);   // the behaviours the orders resolve to: they are in the Orders folder, once

        // the unit's own orders, in the fixed order (Move · Hold · Attack · Observe and fire), then any other the class has by name
        var orders = new List<OrderInfo>(OrderMenu.OfferedFor(dis));
        orders.Sort((a, b) =>
        {
            int ra = Array.IndexOf(OrderRank, a.Name), rb = Array.IndexOf(OrderRank, b.Name);
            ra = ra < 0 ? int.MaxValue : ra; rb = rb < 0 ? int.MaxValue : rb;
            return ra != rb ? ra.CompareTo(rb) : string.CompareOrdinal(a.Name, b.Name);
        });
        foreach (var o in orders)
        {
            var res = OrderMenu.Resolve(dis, o.Name);
            string code = res.Behaviour ?? o.Name;
            skip.Add(code);
            registry.TryGetBehavior(code, out var decl);
            string? cls = res.Behaviour != null ? OrderResolver.ClassOfBehaviour(EntityClassTable.Default, res.Behaviour, o.Name) : null;
            var info = new TaskInfo(new TaskPick(TaskPickKind.Order, o.Name), TaskNames.OrderWords(o.Name), OrdersFolder + " · " + code,
                decl?.Description ?? "", ParamsOf(o.Dto), cls ?? FitsOf(decl), Hidden: false);
            list.Add(new PickerEntry("order:" + o.Name, info.Title, info.Description, OrdersFolder, new[] { o.Name, code, "order" }, null, info));
        }

        foreach (var b in behaviours)
        {
            if (skip.Contains(b.Name) || IsNotATask(b.Group)) continue;
            registry.TryGetBehavior(b.Name, out var decl);
            string folder = FolderOf(b.Group);
            bool hidden = IsHiddenGroup(b.Group);
            var info = new TaskInfo(new TaskPick(TaskPickKind.Behaviour, b.Name), TaskNames.Words(b.Name), folder + " · " + b.Name,
                decl?.Description ?? "", decl != null ? ParamsOf(decl.ParamsType) : Array.Empty<(string, string)>(), FitsOf(decl), hidden);
            list.Add(new PickerEntry("beh:" + b.Name, info.Title, info.Description, hidden ? b.Group : folder,
                new[] { b.Name, b.Technology ?? "" }, null, info));
        }

        // Wait: the plan's pause — a leaf at the root, after the folders
        list.Add(new PickerEntry("wait", "Wait", "Pauses the plan until its condition holds.", null, new[] { "pause", "delay" }, null,
            new TaskInfo(new TaskPick(TaskPickKind.Wait, ""), "Wait", "Wait", "Pauses the plan until its condition holds.", Array.Empty<(string, string)>(), "any unit", Hidden: false)));
        return list;
    }

    /// <summary>The picker request for <i>+ Add task</i> on <paramref name="unitTitle"/>.</summary>
    public static PickerRequest Request(string unitTitle, DISEntityType dis, IReadOnlyList<BehaviorChoice> behaviours, ParamsDtoRegistry registry)
        => new()
        {
            ContextKey = ContextKey,
            Title = "Add a task — " + unitTitle,
            Layout = PickerLayout.Tree,
            ItemsProvider = () => Entries(dis, behaviours, registry),
            FolderOrder = FolderOrder,
            InitiallyOpenFolders = new[] { OrdersFolder, "Movement" },   // the unit's own orders and the movement tasks are the first thing in view (mockup)
            KeepEntryOrder = true,
            HideToggle = new PickerHideToggle(HideLabel, e => e.Tag is TaskInfo { Hidden: true }),
            DrawPane = DrawPane,
            SidePaneWidth = 340f,
            DefaultSize = new Vector2(900f, 560f),
            Actions = new[] { new PickerAction("add", "Add", IsDefault: true) },
        };

    /// <summary>The answer of a confirmed picker, or null (cancelled).</summary>
    public static TaskPick? PickOf(PickerResult result) => result.First?.Tag is TaskInfo info ? info.Pick : null;

    // ── the preview pane ─────────────────────────────────────────────────────────────────────

    private static void DrawPane(PickerEntry entry, PickerPaneContext ctx)
    {
        if (entry.Tag is not TaskInfo info) { ctx.DrawDefault(); return; }
        ImGui.TextColored(ctx.Theme.TextDefault, info.Title);
        ImGui.TextColored(ctx.Theme.TextMuted, info.Subtitle);   // the folder and the CODE NAME, grey
        if (info.Description.Length > 0) { ImGui.Spacing(); ImGui.TextWrapped(info.Description); }
        if (info.Params.Count > 0)
        {
            ImGui.SeparatorText("Parameters");
            for (int i = 0; i < info.Params.Count; i++)
            {
                ImGui.TextUnformatted(info.Params[i].Label);
                ImGui.SameLine(Math.Max(120f, ctx.Width * 0.4f));
                ImGui.TextColored(ctx.Theme.TextMuted, info.Params[i].Value);
            }
        }
        ImGui.SeparatorText("Fits");
        ImGui.TextColored(ctx.Theme.TextMuted, info.Fits);
    }

    // ── cached text ──────────────────────────────────────────────────────────────────────────

    private static string FitsOf(BehaviorDeclaration? decl)
    {
        if (decl == null) return "any unit";
        if (decl.Classes.Count > 0) return string.Join(" · ", decl.Classes);
        if (decl.Categories == 0) return "any unit";
        var words = decl.Categories.ToString().Split(", ");
        for (int i = 0; i < words.Length; i++) words[i] = TaskNames.Words(words[i]);   // AllMilitary → All military
        return string.Join(" · ", words);
    }

    /// <summary>The DTO's public fields as (words, default value) — what a new task starts with.</summary>
    private static IReadOnlyList<(string Label, string Value)> ParamsOf(Type dto)
    {
        var list = new List<(string, string)>();
        object? def = null;
        try { def = Activator.CreateInstance(dto); } catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or NotSupportedException) { }
        foreach (var f in dto.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            string? value = null;
            try { value = def == null ? null : Format(f.GetValue(def)); } catch (Exception ex) when (ex is TargetInvocationException or FieldAccessException) { }
            list.Add((TaskNames.Words(f.Name), value ?? "—"));
        }
        return list;
    }

    private static string Format(object? v) => v switch
    {
        null => "—",
        string s => s.Length == 0 ? "—" : s,
        bool b => b ? "yes" : "no",
        float f => f.ToString("0.##", CultureInfo.InvariantCulture),
        double d => d.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "—",
    };
}
