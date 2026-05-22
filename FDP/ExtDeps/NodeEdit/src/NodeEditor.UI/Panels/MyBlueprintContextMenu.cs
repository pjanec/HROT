using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.Panels;

/// <summary>
/// Renders the right-click context menu for My Blueprint items.
/// Each item kind gets a different menu per the spec (§D.6.8).
/// Actions are dispatched via <see cref="IEditorCommands"/>.
/// </summary>
internal static class MyBlueprintContextMenu
{
    /// <summary>
    /// Draw the context menu popup for the given item.
    /// Must be called immediately after the row selectable in the same ImGui ID scope.
    /// </summary>
    public static void Draw(
        MyBlueprintItem item,
        IEditorCommands commands,
        System.Action<string, string> navigateToItem)
    {
        if (!ImGui.BeginPopupContextItem($"##ctx_{item.ItemId}"))
            return;

        DrawForSectionId(item, commands, navigateToItem);
        ImGui.EndPopup();
    }

    // ── per-kind menus ────────────────────────────────────────────────────────

    private static void DrawForSectionId(
        MyBlueprintItem item,
        IEditorCommands commands,
        System.Action<string, string> navigateToItem)
    {
        string sectionId = item.SectionId.ToLowerInvariant();

        switch (sectionId)
        {
            case "variables":
                DrawVariableMenu(item, commands);
                break;

            case "functions":
                DrawFunctionMenu(item, commands, navigateToItem);
                break;

            case "macros":
                DrawMacroMenu(item, commands, navigateToItem);
                break;

            case "customevents":
                DrawCustomEventMenu(item, commands, navigateToItem);
                break;

            case "eventdispatchers":
                DrawEventDispatcherMenu(item, commands);
                break;

            case "graphs":
                DrawGraphMenu(item, commands, navigateToItem);
                break;

            default:
                DrawGenericMenu(item, commands);
                break;
        }
    }

    // ── variable ─────────────────────────────────────────────────────────────

    private static void DrawVariableMenu(MyBlueprintItem item, IEditorCommands commands)
    {
        if (ImGui.MenuItem("Get"))
            Invoke(commands, "editor.create-variable-get", item.ItemId);
        if (ImGui.MenuItem("Set"))
            Invoke(commands, "editor.create-variable-set", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Find References", "Ctrl+Shift+F"))
            Invoke(commands, "editor.find-references", item.ItemId);
        if (item.IsRenamable && ImGui.MenuItem("Duplicate", "Ctrl+D"))
            Invoke(commands, "editor.duplicate-item", item.ItemId);
        if (item.IsRenamable && ImGui.MenuItem("Rename", "F2"))
            Invoke(commands, "editor.rename-item", item.ItemId);
        if (item.IsDeletable && ImGui.MenuItem("Delete", "Del"))
            Invoke(commands, "editor.delete-item", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Move to Category\u2026"))
            Invoke(commands, "editor.move-to-category", item.ItemId);
        if (ImGui.MenuItem("Change Type\u2026"))
            Invoke(commands, "editor.change-variable-type", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Copy Reference"))
            ImGui.SetClipboardText(item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Properties\u2026", "F4"))
            Invoke(commands, "editor.show-properties", item.ItemId);
    }

    // ── function ─────────────────────────────────────────────────────────────

    private static void DrawFunctionMenu(MyBlueprintItem item, IEditorCommands commands,
                                          System.Action<string, string> navigateToItem)
    {
        if (ImGui.MenuItem("Go to Function", "\u23ce"))
            navigateToItem(item.SectionId, item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Find References", "Ctrl+Shift+F"))
            Invoke(commands, "editor.find-references", item.ItemId);
        if (item.IsRenamable && ImGui.MenuItem("Duplicate"))
            Invoke(commands, "editor.duplicate-item", item.ItemId);
        if (item.IsRenamable && ImGui.MenuItem("Rename", "F2"))
            Invoke(commands, "editor.rename-item", item.ItemId);
        if (item.IsDeletable && ImGui.MenuItem("Delete", "Del"))
            Invoke(commands, "editor.delete-item", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Move to Category\u2026"))
            Invoke(commands, "editor.move-to-category", item.ItemId);
        if (ImGui.MenuItem("Convert to Pure / Impure"))
            Invoke(commands, "editor.toggle-function-purity", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Add Input"))
            Invoke(commands, "editor.add-function-input", item.ItemId);
        if (ImGui.MenuItem("Add Output"))
            Invoke(commands, "editor.add-function-output", item.ItemId);
        if (ImGui.MenuItem("Add Local Variable"))
            Invoke(commands, "editor.add-local-variable", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Properties\u2026", "F4"))
            Invoke(commands, "editor.show-properties", item.ItemId);
    }

    // ── macro ─────────────────────────────────────────────────────────────────

    private static void DrawMacroMenu(MyBlueprintItem item, IEditorCommands commands,
                                       System.Action<string, string> navigateToItem)
    {
        if (ImGui.MenuItem("Go to Macro", "\u23ce"))
            navigateToItem(item.SectionId, item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Find References", "Ctrl+Shift+F"))
            Invoke(commands, "editor.find-references", item.ItemId);
        if (item.IsRenamable && ImGui.MenuItem("Rename", "F2"))
            Invoke(commands, "editor.rename-item", item.ItemId);
        if (item.IsDeletable && ImGui.MenuItem("Delete", "Del"))
            Invoke(commands, "editor.delete-item", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Add Input"))
            Invoke(commands, "editor.add-macro-input", item.ItemId);
        if (ImGui.MenuItem("Add Output"))
            Invoke(commands, "editor.add-macro-output", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Properties\u2026", "F4"))
            Invoke(commands, "editor.show-properties", item.ItemId);
    }

    // ── custom event ──────────────────────────────────────────────────────────

    private static void DrawCustomEventMenu(MyBlueprintItem item, IEditorCommands commands,
                                             System.Action<string, string> navigateToItem)
    {
        if (ImGui.MenuItem("Go to Event", "\u23ce"))
            navigateToItem(item.SectionId, item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Find References", "Ctrl+Shift+F"))
            Invoke(commands, "editor.find-references", item.ItemId);
        if (item.IsRenamable && ImGui.MenuItem("Rename", "F2"))
            Invoke(commands, "editor.rename-item", item.ItemId);
        if (item.IsDeletable && ImGui.MenuItem("Delete", "Del"))
            Invoke(commands, "editor.delete-item", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Add Parameter"))
            Invoke(commands, "editor.add-event-param", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Properties\u2026", "F4"))
            Invoke(commands, "editor.show-properties", item.ItemId);
    }

    // ── event dispatcher ──────────────────────────────────────────────────────

    private static void DrawEventDispatcherMenu(MyBlueprintItem item, IEditorCommands commands)
    {
        if (ImGui.MenuItem("Call"))    Invoke(commands, "editor.create-dispatcher-call",    item.ItemId);
        if (ImGui.MenuItem("Bind"))    Invoke(commands, "editor.create-dispatcher-bind",    item.ItemId);
        if (ImGui.MenuItem("Unbind"))  Invoke(commands, "editor.create-dispatcher-unbind",  item.ItemId);
        if (ImGui.MenuItem("Unbind All")) Invoke(commands, "editor.create-dispatcher-unbindall", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Find References", "Ctrl+Shift+F"))
            Invoke(commands, "editor.find-references", item.ItemId);
        if (item.IsRenamable && ImGui.MenuItem("Rename", "F2"))
            Invoke(commands, "editor.rename-item", item.ItemId);
        if (item.IsDeletable && ImGui.MenuItem("Delete", "Del"))
            Invoke(commands, "editor.delete-item", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Add Parameter"))
            Invoke(commands, "editor.add-dispatcher-param", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Properties\u2026", "F4"))
            Invoke(commands, "editor.show-properties", item.ItemId);
    }

    // ── graph entry ───────────────────────────────────────────────────────────

    private static void DrawGraphMenu(MyBlueprintItem item, IEditorCommands commands,
                                       System.Action<string, string> navigateToItem)
    {
        if (ImGui.MenuItem("Open Graph", "\u23ce"))
            navigateToItem(item.SectionId, item.ItemId);
        if (ImGui.MenuItem("Find in this Graph", "Ctrl+F"))
            Invoke(commands, "editor.find-in-graph", item.ItemId);
        ImGui.Separator();
        if (ImGui.MenuItem("Properties\u2026", "F4"))
            Invoke(commands, "editor.show-properties", item.ItemId);
    }

    // ── generic ───────────────────────────────────────────────────────────────

    private static void DrawGenericMenu(MyBlueprintItem item, IEditorCommands commands)
    {
        if (item.IsRenamable && ImGui.MenuItem("Rename", "F2"))
            Invoke(commands, "editor.rename-item", item.ItemId);
        if (item.IsDeletable && ImGui.MenuItem("Delete", "Del"))
            Invoke(commands, "editor.delete-item", item.ItemId);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static void Invoke(IEditorCommands commands, string commandId, string itemId)
    {
        commands.Invoke(commandId, new EditorCommandContext(
            ScreenPos: null,
            CanvasPos: null,
            Args: new Dictionary<string, object?> { ["itemId"] = itemId }));
    }
}
