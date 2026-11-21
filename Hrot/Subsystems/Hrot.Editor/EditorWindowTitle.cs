namespace Hrot.Editor;

/// <summary>⭐ CE-4210 MA-6 — what the editor's window title says about the loaded file: a macro reads <c>Macro: &lt;path&gt;</c>, a scenario its name, and (N4)
/// the terrain it is on. ⭐ The rule is <see cref="Hrot.Common.Scenarios.ScenarioWindowTitle"/>, shared with CGF.</summary>
public static class EditorWindowTitle
{
    public static string? Detail(string? loadedScenarioName, string? terrain = null, bool dirty = false)
        => Hrot.Common.Scenarios.ScenarioWindowTitle.Detail(loadedScenarioName, terrain, dirty);
}
