namespace Hrot.Editor;

/// <summary>⭐ CE-4210 MA-6 — what the editor's window title says about the loaded file: a macro reads <c>Macro: &lt;path&gt;</c>, a scenario its name.</summary>
public static class EditorWindowTitle
{
    public static string? Detail(string? loadedScenarioName)
        => Fdp.Toolkit.Orchestration.ContentLayout.IsMacroName(loadedScenarioName)
            ? "Macro: " + loadedScenarioName!.Replace('\\', '/').Substring(Fdp.Toolkit.Orchestration.ContentLayout.MacroNamePrefix.Length)
            : loadedScenarioName;
}
