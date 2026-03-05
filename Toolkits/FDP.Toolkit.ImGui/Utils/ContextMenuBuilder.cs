using FDP.Toolkit.ImGui.Abstractions;

namespace FDP.Toolkit.ImGui.Utils;

/// <summary>
/// ImGui-backed implementation of <see cref="IContextMenuBuilder"/>.
/// Renders items directly into an active ImGui popup.
///
/// <para>Must be used only while an ImGui popup is open
/// (<c>ImGui.BeginPopup</c> returned <c>true</c>).</para>
/// </summary>
internal sealed class ContextMenuBuilder : IContextMenuBuilder
{
    private readonly bool _insideSubmenu;

    internal ContextMenuBuilder(bool insideSubmenu = false)
    {
        _insideSubmenu = insideSubmenu;
    }

    // ── IContextMenuBuilder ───────────────────────────────────────────────────

    public void AddItem(string label, Action callback, bool enabled = true)
    {
        if (ImGuiNET.ImGui.MenuItem(label, string.Empty, false, enabled))
            callback();
    }

    public IContextMenuBuilder BeginSubmenu(string label)
    {
        bool open = ImGuiNET.ImGui.BeginMenu(label);
        return new SubmenuBuilder(label, open);
    }

    public void EndSubmenu()
    {
        // Root builder cannot close a submenu; callers should call EndSubmenu on the
        // IContextMenuBuilder returned from BeginSubmenu.
    }

    public void AddSeparator() => ImGuiNET.ImGui.Separator();

    // ── Nested submenu builder ────────────────────────────────────────────────

    private sealed class SubmenuBuilder : IContextMenuBuilder
    {
        private readonly bool _open;

        internal SubmenuBuilder(string label, bool open)
        {
            _open = open;
        }

        public void AddItem(string label, Action callback, bool enabled = true)
        {
            if (!_open) return;
            if (ImGuiNET.ImGui.MenuItem(label, string.Empty, false, enabled))
                callback();
        }

        public IContextMenuBuilder BeginSubmenu(string label)
        {
            if (!_open) return new SubmenuBuilder(label, false);
            bool open = ImGuiNET.ImGui.BeginMenu(label);
            return new SubmenuBuilder(label, open);
        }

        public void EndSubmenu()
        {
            if (_open) ImGuiNET.ImGui.EndMenu();
        }

        public void AddSeparator()
        {
            if (_open) ImGuiNET.ImGui.Separator();
        }
    }
}
