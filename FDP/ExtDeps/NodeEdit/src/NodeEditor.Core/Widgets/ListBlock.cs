using System.Numerics;
using ImGuiNET;

namespace NodeEditor.Core.Widgets;

/// <summary>What the user asked of a list this frame. The host applies it to ITS data (the kit never mutates a model).</summary>
public enum ListOpKind { None, Add, InsertAbove, InsertBelow, Duplicate, MoveUp, MoveDown, Move, Copy, Paste, Reset, Delete }

/// <summary><c>Index</c> = the item acted on (for <see cref="ListOpKind.Add"/>: the item count — append); <c>Target</c> = the destination index of a <see cref="ListOpKind.Move"/> / the kind index of an <see cref="ListOpKind.Add"/>.</summary>
public readonly record struct ListOp(ListOpKind Kind, int Index = -1, int Target = -1);

/// <summary>An entry of the <i>Add ▾</i> popup (B5 — polymorphic: the kind picked when adding).</summary>
public readonly record struct ListKind(IconGlyph Icon, string Name);

/// <summary>The commands of the ⋮ menu that a host can switch off (<see cref="ListOptions.Disabled"/>).</summary>
[Flags]
public enum ListCmd
{
    None = 0, InsertAbove = 1, InsertBelow = 2, Duplicate = 4, MoveUp = 8, MoveDown = 16, Copy = 32, Paste = 64, Reset = 128, Delete = 256,
    /// <summary>B4 — a fixed-size list: no insert / duplicate / delete.</summary>
    FixedSize = InsertAbove | InsertBelow | Duplicate | Delete,
}

/// <summary>A list's static configuration — built ONCE by the host and reused every frame (no per-frame allocation).</summary>
public sealed class ListOptions
{
    /// <summary>The header title (<c>Spawn points</c>); null = no header (unless the list is long).</summary>
    public string? Title;
    public string AddLabel = "Add";
    /// <summary>The kinds the <i>Add ▾</i> popup lists (B5); one kind ⇒ <i>Add</i> adds it directly; none ⇒ a plain <i>Add</i>.</summary>
    public ListKind[] Kinds = Array.Empty<ListKind>();
    /// <summary>B4 — slots: no <i>Add</i>, no insert / delete, a "n / n — fixed size" footer.</summary>
    public bool FixedSize;
    /// <summary>Commands greyed out (e.g. <see cref="ListCmd.Paste"/> while nothing is copied).</summary>
    public ListCmd Disabled;
    /// <summary>The shortcut label each command shows, by <c>(int)</c> bit position of <see cref="ListCmd"/> (0 = InsertAbove … 8 = Delete); null = none. The kit never reads keys — the keymap supplies the labels.</summary>
    public readonly string?[] Shortcuts = new string?[ListBlock.CommandCount];
    /// <summary>B6 — over this many items the list starts closed, filters and pages.</summary>
    public int LongThreshold = 50;
    public int PageSize = 50;

    public ListOptions Shortcut(ListCmd cmd, string? label) { Shortcuts[ListBlock.BitIndex(cmd)] = label; return this; }
}

/// <summary>
/// ⭐ B2 / B5 / B6 (docs/DESIGN_Struct_Editor.md §8, struct-editor-visual.svg) — a list of structures as ONE SUMMARY ROW per item: <b>⋮⋮ handle · number · ▸ · kind glyph · summary · ⋮ menu</b>; ▸ (or the row) opens the item,
/// whose fields the HOST draws between <see cref="ItemBegin"/> and <see cref="ItemEnd"/> — Begin / Item / End calls, no closures. The ⋮ menu and the right-click (<c>Insert above · Insert below · Duplicate · Move up · Move down ·
/// Copy value · Paste value · Reset to default · Delete</c>) show the caller's shortcut labels and grey out what is not allowed; <i>Add ▾</i> lists the kinds (B5); over <see cref="ListOptions.LongThreshold"/> items the list starts
/// closed with a filter and pages (B6). One <see cref="ListOp"/> per frame comes back from <see cref="End"/>; the open flags are owned here (<see cref="OpenSet"/>) and MOVE with their items on a move / insert / remove.
/// ⭐ R-2084: zero allocation per steady frame (strings cached per list; the filtered index list is rebuilt only on a keystroke / page / count / version change).
/// <code>
/// ListBlock.Begin("steps", summaries, opts);
/// for (int v = 0; v &lt; ListBlock.VisibleCount; v++) { int i = ListBlock.VisibleIndex(v); if (ListBlock.ItemBegin(i, kinds[i])) DrawFields(i); ListBlock.ItemEnd(); }
/// var op = ListBlock.End();
/// </code>
/// </summary>
public static class ListBlock
{
    public const int CommandCount = 9;
    private const float HandleW = 14f, MenuW = 20f, ArrowW = 16f;

    internal sealed class ListState
    {
        public readonly OpenSet Open = new();
        public readonly TextBuffer FilterBox = new(96);
        public bool Init, Collapsed, Dirty = true;
        public int Selected = -1;
        public bool LastCollapsed;
        public int Page, LastCount = -1, LastVersion = int.MinValue, LastPage = -1;
        public string LastFilter = "";
        public int[] Filtered = Array.Empty<int>();
        public int FilteredCount;
        public int VisStart, VisCount;
        public bool UseFiltered;
        public int FooterCount = -1, TextCount = -1, TextShown = -1, TextPages = -1;
        public string CountText = "", PageText = "", FooterText = "";
    }

    private sealed class Frame
    {
        public ListState St = null!;
        public ListOptions Opt = null!;
        public IReadOnlyList<string> Summaries = null!;
        public int Count;
        public ListOp Op;
        public uint DragOwner;
        public bool ItemOpen;
    }

    private static readonly Dictionary<uint, ListState> States = new();
    private static readonly Frame[] Stack = { new(), new(), new(), new() };
    private static int _depth;
    private static string[] _numbers = new string[64];

    private const string PrevId = KitGlyph.ChevronLeft + "##pv", NextId = KitGlyph.ChevronRight + "##nx";   // constants: no concatenation per frame

    private static Frame Cur => Stack[_depth - 1];

    /// <summary>How many items are drawn this page (0 while the list is collapsed).</summary>
    public static int VisibleCount => Cur.St.VisCount;

    /// <summary>The selected item of the current list (the last one clicked; -1 = none) — what the keys of a hosting table act on. Valid between <see cref="Begin"/> and <see cref="End"/>.</summary>
    public static int SelectedIndex => Cur.St.Selected;

    private static uint _lastSelectedOwner;

    /// <summary>Whether the current list is the one whose item was selected last (of every list on screen) — the one a table's keys belong to.</summary>
    public static bool OwnsSelection => Cur.DragOwner == _lastSelectedOwner;

    /// <summary>Asks for <paramref name="op"/> as if the user chose it from the item menu this frame (a key command of the hosting table): <see cref="End"/> applies it to the open flags and the selection and returns it.
    /// Ignored when an op is already pending. Call between <see cref="Begin"/> and <see cref="End"/>.</summary>
    public static void Inject(in ListOp op) { var f = Cur; if (f.Op.Kind == ListOpKind.None) f.Op = op; }

    /// <summary>The real item index of the <paramref name="v"/>-th visible item (filter and page applied).</summary>
    public static int VisibleIndex(int v) => Cur.St.UseFiltered ? Cur.St.Filtered[Cur.St.VisStart + v] : Cur.St.VisStart + v;

    /// <summary>Whether item <paramref name="i"/>'s fields are open (state owned here).</summary>
    public static bool IsOpen(string id, int i) => States.TryGetValue(IdOf(id), out var s) && s.Open.Get(i);

    private static uint IdOf(string id) => ImGui.GetID(id);

    internal static int BitIndex(ListCmd cmd) => cmd switch
    {
        ListCmd.InsertAbove => 0, ListCmd.InsertBelow => 1, ListCmd.Duplicate => 2, ListCmd.MoveUp => 3, ListCmd.MoveDown => 4,
        ListCmd.Copy => 5, ListCmd.Paste => 6, ListCmd.Reset => 7, _ => 8,
    };

    /// <summary>The cached text of a 1-based item number.</summary>
    internal static string NumberText(int n)
    {
        if (n >= _numbers.Length) Array.Resize(ref _numbers, Math.Max(n + 1, _numbers.Length * 2));
        return _numbers[n] ??= n.ToString();
    }

    // ── Begin: header, filter, pages ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Starts a list: draws the header (title · count · and for a long list ▸/▾ · filter · pages) and works out which items are visible. <paramref name="version"/> is bumped by the host when summary
    /// TEXTS change without the count changing (the filter is rebuilt then).</summary>
    public static void Begin(string id, IReadOnlyList<string> summaries, ListOptions opt, int version = 0)
    {
        uint sid = ImGui.GetID(id);
        ImGui.PushID(id);
        if (_depth >= Stack.Length) throw new InvalidOperationException("ListBlock nested too deep");
        if (!States.TryGetValue(sid, out var st)) States[sid] = st = new ListState();
        var f = Stack[_depth++];
        f.St = st; f.Opt = opt; f.Summaries = summaries; f.Count = summaries.Count; f.Op = default; f.DragOwner = sid; f.ItemOpen = false;

        bool isLong = f.Count > opt.LongThreshold;
        if (st.Selected >= f.Count) st.Selected = -1;
        if (!st.Init) { st.Init = true; st.Collapsed = isLong; }
        if (!isLong) st.Collapsed = false;

        if (st.LastCount != f.Count || st.LastVersion != version) { st.Dirty = true; st.LastCount = f.Count; st.LastVersion = version; }
        if (opt.Title is not null || isLong) DrawHeader(f, st, isLong);
        if (st.LastCollapsed != st.Collapsed) { st.LastCollapsed = st.Collapsed; st.Dirty = true; }
        if (st.Dirty || st.LastPage != st.Page) Rebuild(f, st, isLong);
        if (st.Collapsed) st.VisCount = 0;
    }

    private static void DrawHeader(Frame f, ListState st, bool isLong)
    {
        float h = ImGui.GetFrameHeight();
        if (isLong)
        {
            var pos = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton("##fold", new Vector2(ArrowW, h))) st.Collapsed = !st.Collapsed;
            KitProbe.Set(KitProbe.HeaderFold, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
            DrawArrow(ImGui.GetWindowDrawList(), pos, h, open: !st.Collapsed);
            ImGui.SameLine(0f, 2f);
        }
        if (f.Opt.Title is not null) { ImGui.TextUnformatted(f.Opt.Title); ImGui.SameLine(0f, 10f); }
        if (st.TextCount != f.Count) { st.TextCount = f.Count; st.CountText = f.Count == 1 ? "1 item" : f.Count.ToString() + " items"; }
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        ImGui.TextUnformatted(st.CountText);
        ImGui.PopStyleColor();
        if (!isLong || st.Collapsed) return;

        ImGui.SameLine(0f, 24f);
        if (st.FilterBox.Draw("##filter", "filter", 170f)) { st.Dirty = true; st.Page = 0; }
        KitProbe.Set(KitProbe.HeaderFilter, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine(0f, 10f);
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        ImGui.TextUnformatted(st.PageText);
        ImGui.PopStyleColor();
        ImGui.SameLine(0f, 6f);
        int pages = PageCount(st, f);
        if (ImGui.SmallButton(PrevId) && st.Page > 0) st.Page--;
        KitProbe.Set(KitProbe.HeaderPrev, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        ImGui.SameLine(0f, 2f);
        if (ImGui.SmallButton(NextId) && st.Page < pages - 1) st.Page++;
        KitProbe.Set(KitProbe.HeaderNext, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    }

    private static int PageCount(ListState st, Frame f)
    {
        int total = st.UseFiltered ? st.FilteredCount : f.Count;
        return Math.Max(1, (total + f.Opt.PageSize - 1) / f.Opt.PageSize);
    }

    /// <summary>The visible window — rebuilt only when the filter text / page / count / version changed.</summary>
    private static void Rebuild(Frame f, ListState st, bool isLong)
    {
        string filter = isLong ? st.FilterBox.Text : "";
        st.UseFiltered = filter.Length > 0;
        if (st.UseFiltered)
        {
            if (st.Filtered.Length < f.Count) st.Filtered = new int[Math.Max(f.Count, 16)];
            int n = 0;
            for (int i = 0; i < f.Count; i++)
                if (f.Summaries[i].Contains(filter, StringComparison.OrdinalIgnoreCase)) st.Filtered[n++] = i;
            st.FilteredCount = n;
        }
        int total = st.UseFiltered ? st.FilteredCount : f.Count;
        int size = isLong ? f.Opt.PageSize : int.MaxValue;
        int pages = isLong ? Math.Max(1, (total + size - 1) / size) : 1;
        if (st.Page >= pages) st.Page = pages - 1;
        if (st.Page < 0) st.Page = 0;
        st.VisStart = isLong ? st.Page * size : 0;
        st.VisCount = isLong ? Math.Min(size, total - st.VisStart) : total;
        if (st.VisCount < 0) st.VisCount = 0;
        st.LastPage = st.Page;
        st.Dirty = false;
        if (isLong)
        {
            int from = total == 0 ? 0 : st.VisStart + 1, to = st.VisStart + st.VisCount;
            if (st.TextShown != from || st.TextPages != to) { st.TextShown = from; st.TextPages = to; st.PageText = from.ToString() + " – " + to.ToString(); }
        }
    }

    // ── one item ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Draws item <paramref name="i"/>'s summary row. True when the item is OPEN — the host then draws its fields (and ⛔ always calls <see cref="ItemEnd"/>).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]   // per-row, per-frame: never run the tier-0 code (it boxes the nullable kind)
    public static bool ItemBegin(int i, IconGlyph? kind = null)
    {
        var f = Cur; var st = f.St;
        ImGui.PushID(i);
        bool open = st.Open.Get(i);
        float h = ImGui.GetFrameHeight(), width = ImGui.GetContentRegionAvail().X;
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        if (open) dl.AddRectFilled(pos, pos + new Vector2(width, h), ImGui.GetColorU32(IconPalette.Blue with { W = 0.55f }), ImGui.GetStyle().FrameRounding);

        // handle
        if (ImGui.InvisibleButton("##h", new Vector2(HandleW, h))) { st.Selected = i; _lastSelectedOwner = f.DragOwner; }
        KitProbe.Set(KitProbe.ListHandle, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        string summary = f.Summaries[i];
        if (KitDrag.Reorder(f.DragOwner, i, summary, asSource: true, asTarget: false, out _)) { }
        DrawGlyph(dl, KitGlyph.Grip, pos, HandleW, h, ImGui.GetColorU32(IconPalette.Muted));
        ImGui.SameLine(0f, 0f);

        // body: number · ▸ · kind glyph · summary
        float bodyW = width - HandleW - MenuW;
        var bpos = ImGui.GetCursorScreenPos();
        bool toggled = ImGui.InvisibleButton("##b", new Vector2(bodyW, h));
        bool hovered = ImGui.IsItemHovered();
        bool right = ImGui.IsItemClicked(ImGuiMouseButton.Right);
        KitProbe.Set(KitProbe.ListBody, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (hovered && !open) dl.AddRectFilled(bpos, bpos + new Vector2(bodyW, h), ImGui.GetColorU32(ImGuiCol.HeaderHovered, 0.35f), ImGui.GetStyle().FrameRounding);
        if (KitDrag.Reorder(f.DragOwner, i, summary, asSource: false, asTarget: true, out int from))
            f.Op = new ListOp(ListOpKind.Move, from, i);
        if (toggled) { st.Open.Toggle(i); open = !open; st.Selected = i; _lastSelectedOwner = f.DragOwner; }
        else if (right) { st.Selected = i; _lastSelectedOwner = f.DragOwner; }
        if (st.Selected == i && !open) dl.AddRectFilled(bpos, bpos + new Vector2(bodyW, h), ImGui.GetColorU32(ImGuiCol.Header, 0.30f), ImGui.GetStyle().FrameRounding);
        float ty = bpos.Y + (h - ImGui.GetTextLineHeight()) * 0.5f, x = bpos.X + 2f;
        string num = NumberText(i + 1);
        dl.AddText(new Vector2(x, ty), ImGui.GetColorU32(ImGuiCol.TextDisabled), num);
        x += ImGui.CalcTextSize(NumberText(Math.Max(f.Count, 1))).X + 6f;
        DrawArrow(dl, new Vector2(x, bpos.Y), h, open);
        x += ArrowW;
        if (kind is { } k && k.Glyph.Length > 0)
        {
            dl.AddText(new Vector2(x, ty), ImGui.GetColorU32(k.Color), k.Glyph);
            x += ImGui.CalcTextSize(k.Glyph).X + 6f;
        }
        dl.PushClipRect(new Vector2(x, bpos.Y), new Vector2(bpos.X + bodyW, bpos.Y + h), true);
        dl.AddText(new Vector2(x, ty), ImGui.GetColorU32(ImGuiCol.Text), summary);
        dl.PopClipRect();
        ImGui.SameLine(0f, 0f);

        // ⋮ menu (the button, and the right-click on the body)
        if (right) ImGui.OpenPopup("##menu");
        DrawMenuButton(i, f.Count, f.Opt, ref f.Op);

        f.ItemOpen = open;
        if (open) ImGui.Indent(HandleW + 8f);
        return open;
    }

    /// <summary>Ends the item started by <see cref="ItemBegin"/> (always — open or not).</summary>
    public static void ItemEnd()
    {
        var f = Cur;
        if (f.ItemOpen) ImGui.Unindent(HandleW + 8f);
        ImGui.PopID();
    }

    // ── End: Add ▾ / footer, then the op ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Ends the list: <i>Add ▾</i> (or the fixed-size footer), applies the frame's op to the open flags and returns it (<see cref="ListOpKind.None"/> when nothing happened).</summary>
    public static ListOp End()
    {
        var f = Cur; var st = f.St;
        if (!st.Collapsed) DrawAddAndFooter(f.Opt, f.Count, st, ref f.Op);
        var op = f.Op;
        ApplyToState(st.Open, in op);
        if (op.Kind != ListOpKind.None) { st.Selected = NextSelected(st.Selected, in op, f.Count); _lastSelectedOwner = f.DragOwner; }
        if (op.Kind != ListOpKind.None) st.Dirty = true;
        _depth--;
        ImGui.PopID();
        return op;
    }

    /// <summary>The selected item after <paramref name="op"/> (pure): it follows its item through a move, lands on the new item of an insert / duplicate / add, and stays near the removed one.</summary>
    public static int NextSelected(int selected, in ListOp op, int countBefore) => op.Kind switch
    {
        ListOpKind.Add => op.Index,
        ListOpKind.InsertAbove => op.Index,
        ListOpKind.InsertBelow or ListOpKind.Duplicate => op.Index + 1,
        ListOpKind.MoveUp or ListOpKind.MoveDown or ListOpKind.Move => op.Target,
        ListOpKind.Delete => countBefore <= 1 ? -1 : Math.Min(op.Index, countBefore - 2),
        _ => selected,
    };

    /// <summary>Updates an <see cref="OpenSet"/> for <paramref name="op"/> so each flag stays with its item (pure; the host's list is changed by the host).</summary>
    public static void ApplyToState(OpenSet open, in ListOp op)
    {
        switch (op.Kind)
        {
            case ListOpKind.InsertAbove: open.Insert(op.Index); break;
            case ListOpKind.InsertBelow:
            case ListOpKind.Duplicate: open.Insert(op.Index + 1); break;
            case ListOpKind.MoveUp: open.Move(op.Index, op.Index - 1); break;
            case ListOpKind.MoveDown: open.Move(op.Index, op.Index + 1); break;
            case ListOpKind.Move: open.Move(op.Index, op.Target); break;
            case ListOpKind.Delete: open.RemoveAt(op.Index); break;
        }
    }

    // ── shared with GridBlock: the ⋮ menu, Add ▾, the footer ───────────────────────────────────────────────────────────────

    private struct MenuDef
    {
        public ListOpKind Kind; public ListCmd Cmd; public string Label, Glyph; public Vector4 Color; public bool SeparatorBefore;
    }

    private static readonly MenuDef[] Menu =
    {
        new() { Kind = ListOpKind.InsertAbove, Cmd = ListCmd.InsertAbove, Label = "Insert above", Glyph = KitGlyph.Plus, Color = IconPalette.Green },
        new() { Kind = ListOpKind.InsertBelow, Cmd = ListCmd.InsertBelow, Label = "Insert below", Glyph = KitGlyph.Plus, Color = IconPalette.Green },
        new() { Kind = ListOpKind.Duplicate, Cmd = ListCmd.Duplicate, Label = "Duplicate", Glyph = KitGlyph.Copy, Color = IconPalette.Grey },
        new() { Kind = ListOpKind.MoveUp, Cmd = ListCmd.MoveUp, Label = "Move up", Glyph = "", Color = IconPalette.Grey },
        new() { Kind = ListOpKind.MoveDown, Cmd = ListCmd.MoveDown, Label = "Move down", Glyph = "", Color = IconPalette.Grey },
        new() { Kind = ListOpKind.Copy, Cmd = ListCmd.Copy, Label = "Copy value", Glyph = KitGlyph.Copy, Color = IconPalette.Grey, SeparatorBefore = true },
        new() { Kind = ListOpKind.Paste, Cmd = ListCmd.Paste, Label = "Paste value", Glyph = KitGlyph.Paste, Color = IconPalette.Grey },
        new() { Kind = ListOpKind.Reset, Cmd = ListCmd.Reset, Label = "Reset to default", Glyph = KitGlyph.Reset, Color = IconPalette.Amber, SeparatorBefore = true },
        new() { Kind = ListOpKind.Delete, Cmd = ListCmd.Delete, Label = "Delete", Glyph = KitGlyph.Trash, Color = IconPalette.Red, SeparatorBefore = true },
    };

    /// <summary>The ⋮ button at the cursor and the item menu popup it (and a right-click, via <c>OpenPopup("##menu")</c> by the caller) opens. Records the chosen command in <paramref name="op"/>. ONE implementation for list rows and grid rows.</summary>
    internal static void DrawMenuButton(int i, int count, ListOptions opt, ref ListOp op)
    {
        float h = ImGui.GetFrameHeight();
        var pos = ImGui.GetCursorScreenPos();
        bool clicked = ImGui.InvisibleButton("##m", new Vector2(MenuW, h));
        KitProbe.Set(KitProbe.ListMenuButton, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        bool hovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();
        if (hovered) dl.AddRectFilled(pos, pos + new Vector2(MenuW, h), ImGui.GetColorU32(ImGuiCol.HeaderHovered, 0.5f), ImGui.GetStyle().FrameRounding);
        DrawGlyph(dl, KitGlyph.Ellipsis, pos, MenuW, h, ImGui.GetColorU32(hovered ? ImGuiCol.Text : ImGuiCol.TextDisabled));
        if (clicked) ImGui.OpenPopup("##menu");
        if (!ImGui.BeginPopup("##menu")) return;

        var disabled = opt.Disabled | (opt.FixedSize ? ListCmd.FixedSize : ListCmd.None);
        if (i <= 0) disabled |= ListCmd.MoveUp;
        if (i >= count - 1) disabled |= ListCmd.MoveDown;
        float rowW = MathF.Max(ImGui.GetContentRegionAvail().X, 210f);
        for (int m = 0; m < Menu.Length; m++)
        {
            ref readonly var d = ref Menu[m];
            if (d.SeparatorBefore) ImGui.Separator();
            bool allowed = (disabled & d.Cmd) == 0;
            ImGui.PushID(m);
            if (MenuRow(m, in d, opt.Shortcuts[m], allowed, rowW) && op.Kind == ListOpKind.None)
                op = d.Kind == ListOpKind.MoveUp ? new ListOp(ListOpKind.MoveUp, i, i - 1)
                   : d.Kind == ListOpKind.MoveDown ? new ListOp(ListOpKind.MoveDown, i, i + 1)
                   : new ListOp(d.Kind, i);
            ImGui.PopID();
        }
        ImGui.EndPopup();
    }

    private static bool MenuRow(int slot, in MenuDef d, string? shortcut, bool allowed, float width)
    {
        var pos = ImGui.GetCursorScreenPos();
        float h = ImGui.GetTextLineHeightWithSpacing();
        if (!allowed) ImGui.BeginDisabled();
        bool clicked = ImGui.Selectable("##r", false, ImGuiSelectableFlags.None, new Vector2(width, h - ImGui.GetStyle().ItemSpacing.Y + 2f));
        if (!allowed) ImGui.EndDisabled();
        KitProbe.Set(KitProbe.MenuRow + slot, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        var dl = ImGui.GetWindowDrawList();
        float ty = pos.Y + 1f, a = allowed ? 1f : 0.4f;
        if (d.Glyph.Length > 0) { var c = d.Color; c.W *= a; dl.AddText(new Vector2(pos.X + 4f, ty), ImGui.GetColorU32(c), d.Glyph); }
        dl.AddText(new Vector2(pos.X + 28f, ty), ImGui.GetColorU32(ImGuiCol.Text, a), d.Label);
        if (shortcut is not null)
            dl.AddText(new Vector2(pos.X + width - ImGui.CalcTextSize(shortcut).X - 8f, ty), ImGui.GetColorU32(ImGuiCol.TextDisabled, a), shortcut);
        return clicked && allowed;
    }

    /// <summary><i>Add ▾</i> (B5: kinds popup · one kind: direct) or the fixed-size footer (B4), under the rows.</summary>
    internal static void DrawAddAndFooter(ListOptions opt, int count, ListState? st, ref ListOp op)
    {
        if (opt.FixedSize)
        {
            if (st is not null && st.FooterCount != count) { st.FooterCount = count; st.FooterText = count.ToString() + " / " + count.ToString() + " — fixed size"; }
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
            ImGui.TextUnformatted(st?.FooterText ?? "fixed size");
            ImGui.PopStyleColor();
            return;
        }
        bool pick = opt.Kinds.Length > 1;
        if (Chip.Draw("##add", opt.AddLabel, new IconGlyph(KitGlyph.Plus, IconPalette.Green), 220f, pick ? ChipEnd.Dropdown : ChipEnd.None) == ChipHit.Body)
        {
            if (pick) ImGui.OpenPopup("##kinds");
            else if (op.Kind == ListOpKind.None) op = new ListOp(ListOpKind.Add, count, 0);
        }
        if (pick && ImGui.BeginPopup("##kinds"))
        {
            float rowW = MathF.Max(ImGui.GetContentRegionAvail().X, 170f);
            for (int k = 0; k < opt.Kinds.Length; k++)
            {
                ImGui.PushID(k);
                var pos = ImGui.GetCursorScreenPos();
                bool clicked = ImGui.Selectable("##k", false, ImGuiSelectableFlags.None, new Vector2(rowW, ImGui.GetTextLineHeight() + 2f));
                if (k < 16) KitProbe.Set(KitProbe.KindRow + k, ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                var dl = ImGui.GetWindowDrawList();
                var kind = opt.Kinds[k];
                dl.AddText(new Vector2(pos.X + 4f, pos.Y + 1f), ImGui.GetColorU32(kind.Icon.Color), kind.Icon.Glyph);
                dl.AddText(new Vector2(pos.X + 28f, pos.Y + 1f), ImGui.GetColorU32(ImGuiCol.Text), kind.Name);
                if (clicked && op.Kind == ListOpKind.None) op = new ListOp(ListOpKind.Add, count, k);
                ImGui.PopID();
            }
            ImGui.EndPopup();
        }
    }

    /// <summary>The footer / state holder of a grid keyed by its id (GridBlock shares the list's footer cache).</summary>
    internal static ListState StateFor(uint id)
    {
        if (!States.TryGetValue(id, out var st)) States[id] = st = new ListState();
        return st;
    }

    internal static void DrawGlyph(ImDrawListPtr dl, string glyph, Vector2 pos, float w, float h, uint col)
        => dl.AddText(new Vector2(pos.X + (w - ImGui.CalcTextSize(glyph).X) * 0.5f, pos.Y + (h - ImGui.GetTextLineHeight()) * 0.5f), col, glyph);

    /// <summary>▸ closed / ▾ open, a small triangle in the muted text colour, in an <see cref="ArrowW"/>-wide slot at <paramref name="slot"/>.</summary>
    private static void DrawArrow(ImDrawListPtr dl, Vector2 slot, float h, bool open)
    {
        float cx = slot.X + ArrowW * 0.5f, cy = slot.Y + h * 0.5f, r = h * 0.17f;
        uint col = ImGui.GetColorU32(ImGuiCol.Text);
        if (open) dl.AddTriangleFilled(new Vector2(cx - r, cy - r * 0.6f), new Vector2(cx + r, cy - r * 0.6f), new Vector2(cx, cy + r * 0.8f), col);
        else dl.AddTriangleFilled(new Vector2(cx - r * 0.6f, cy - r), new Vector2(cx - r * 0.6f, cy + r), new Vector2(cx + r * 0.8f, cy), col);
    }
}
