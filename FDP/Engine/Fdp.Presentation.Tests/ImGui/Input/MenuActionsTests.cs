using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Fdp.Presentation.Icons;
using Fdp.Presentation.WindowManager;
using NodeEditor.Core.Action;
using NodeEditor.Primitives;
using Xunit;
using WM = Fdp.Presentation.WindowManager.WindowManager;

namespace Fdp.Presentation.Tests.ImGui.Input;

/// <summary>
/// ⭐⭐ B-10 K5 (docs/UX/DESIGN_Input_And_Commands.md §6e) — a menu item that runs code NAMES AN ACTION: the closure is the handler of a command in the ONE shell catalogue, so a menu click,
/// a key (<c>DefaultKey</c>) and <c>Invoke(id)</c> run the same code, and no production code registers a closure-only menu item.
/// </summary>
[Collection("ImGui Sequential")]
public sealed class MenuActionsTests
{
    private static void Click(GlobalMenuRegistry menu, string path)
    {
        var node = MenuCommandAdapter.FindNode(menu.Root, path)!;
        node.ResolveBinding(null)!.OnClick!();
    }

    /// <summary>🔴 Red-proof (compiles): <c>RegisterAction</c> registering the menu closure without declaring the command ⇒ <c>Invoke</c> finds nothing ⇒ red.</summary>
    [Fact]
    public void AMenuItemAndItsCommand_RunTheSameHandler_ADisabledOneRunsNothing()
    {
        var menu = new GlobalMenuRegistry();
        var commands = new ShellEditorCommands();
        int ran = 0;
        bool enabled = true;
        MenuCommandAdapter.RegisterAction(menu, commands, "file.test", "File/Test…", () => ran++, enabled: () => enabled,
            key: new KeyBinding(EditorKey.K, KeyModifiers.Ctrl));

        Click(menu, "File/Test…");
        Assert.Equal(1, ran);
        Assert.True(commands.Invoke("file.test").Success);
        Assert.Equal(2, ran);
        Assert.Equal(new KeyBinding(EditorKey.K, KeyModifiers.Ctrl), commands.Get("file.test")!.DefaultKey);   // the keymap reads it from here
        Assert.Equal("Ctrl+K", MenuCommandAdapter.FindNode(menu.Root, "File/Test…")!.Shortcut);               // and the menu shows the SAME binding

        enabled = false;
        Click(menu, "File/Test…");
        Assert.Equal(2, ran);
    }

    [Fact]
    public void AToggle_IsACheckableCommand_TheMenuAndInvokeFlipTheSameState()
    {
        var menu = new GlobalMenuRegistry();
        var commands = new ShellEditorCommands();
        bool on = false;
        MenuCommandAdapter.RegisterToggle(menu, commands, "settings.test", "View/Settings/Test", () => on, v => on = v);
        Assert.False(commands.Get("settings.test")!.IsChecked!());
        MenuCommandAdapter.FindNode(menu.Root, "View/Settings/Test")!.ResolveBinding(null)!.OnCheckedChanged!(true);   // the menu flips it …
        Assert.True(on);
        Assert.True(commands.Get("settings.test")!.IsChecked!());
        Assert.True(commands.Invoke("settings.test").Success);      // … and so does the command (a key, a remote call)
        Assert.False(on);
    }

    /// <summary>The shell's own item, on a real window manager: an action in the catalogue, bound to its menu leaf.</summary>
    [Fact]
    public void TheShellsSettingsItem_IsACommandInTheOneCatalogue()
    {
        using var atlas = new IconAtlas(new IntPtr(1), 256f, 256f, 16f);
        var wm = new WM(atlas);
        Assert.NotNull(wm.ShellCommands.Get("shell.ui-settings"));
        Assert.NotNull(MenuCommandAdapter.FindNode(wm.GlobalMenu.Root, GlobalMenuRegistry.SettingsMenuPath + "/UI Scale & Fonts…")!.ResolveBinding(null));
    }

    /// <summary>
    /// 🔴 Red-proof (compiles): a <c>GlobalMenu.RegisterItem(</c> call added to any production file ⇒ the scan names it ⇒ red.
    /// ⛔ The registry itself, the adapter (which binds the command) and the tests are the only places that may call it.
    /// </summary>
    [Fact]
    public void NoProductionMenuItemIsAClosureOnly()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "HROT.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var call = new Regex(@"\.(RegisterItem|RegisterCheckableItem)\(");
        var offenders = new System.Collections.Generic.List<string>();
        foreach (var top in new[] { "Hrot", "FDP" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root!.FullName, top), "*.cs", SearchOption.AllDirectories))
            {
                var rel = file.Substring(root.FullName.Length + 1).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("Tests/") || rel.Contains(".Tests") || rel.Contains("/Examples/") || rel.Contains("/NodeEditor.Demo/")) continue;
                if (rel.EndsWith("GlobalMenuRegistry.cs") || rel.EndsWith("MenuCommandAdapter.cs")) continue;
                int n = 0;
                foreach (var line in File.ReadLines(file))
                {
                    n++;
                    var t = line.TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("///") || t.StartsWith("*")) continue;
                    if (call.IsMatch(line) && line.Contains("GlobalMenu", StringComparison.Ordinal)) offenders.Add($"{rel}:{n}");
                }
            }
        Assert.True(offenders.Count == 0, "closure-only menu items (declare an action: MenuCommandAdapter.RegisterAction): " + string.Join(", ", offenders));
    }
}
