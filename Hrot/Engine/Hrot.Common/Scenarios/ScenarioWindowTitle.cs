namespace Hrot.Common.Scenarios;

/// <summary>
/// ⭐ What a host's window title says about the loaded file (CE-1290), ONE rule for the editor and CGF: a macro reads <c>Macro: &lt;path&gt;</c>
/// (CE-4210 MA-6), a scenario its name, and ⭐ New scenario N4 (📄 docs/UX/DESIGN_New_Scenario_And_Recipe.md §4, R-4124) the terrain it is on —
/// <c>my-scenario · on highway-rest</c> — fixed at creation, so the title is where it is read, never changed.
/// <para>Read once a frame by <c>LocalWindowController.UpdateWindowTitle</c>: the text is cached by its two inputs, so an unchanged title allocates nothing.</para>
/// </summary>
public static class ScenarioWindowTitle
{
    private static string? _name, _terrain, _detail;
    private static bool _dirty;

    /// <summary>⭐ B-14 / Edit-mode undo U5 (LC-12, supersedes AQ108 D20) — what leads the title while there are unsaved changes.</summary>
    public const string DirtyMark = "\u25CF ";

    /// <summary>The title detail for <paramref name="loadedName"/> on <paramref name="terrain"/> (either may be null; no name ⇒ null); <paramref name="dirty"/> ⇒ the dot first.</summary>
    public static string? Detail(string? loadedName, string? terrain, bool dirty = false)
    {
        if (string.IsNullOrWhiteSpace(loadedName)) return null;
        if (string.Equals(loadedName, _name) && string.Equals(terrain, _terrain) && dirty == _dirty) return _detail;
        _name = loadedName; _terrain = terrain; _dirty = dirty;
        string detail = Fdp.Toolkit.Orchestration.ContentLayout.IsMacroName(loadedName)
            ? "Macro: " + loadedName.Replace('\\', '/').Substring(Fdp.Toolkit.Orchestration.ContentLayout.MacroNamePrefix.Length)
            : loadedName;
        if (dirty) detail = DirtyMark + detail;
        return _detail = string.IsNullOrWhiteSpace(terrain) ? detail : detail + " · on " + terrain;
    }
}
