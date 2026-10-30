namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// ⭐⭐ <c>CE-1443</c> part B (AQ108 §2.15) — <b>the ONE rule for what a save item says</b>: it names the kind and the document it writes
/// (<i>Save Scenario 'hill-attack'</i> · <i>Save BTree 'Patrol'</i> · <i>Save HSM 'Guard'</i>), never a plain <i>Save</i>.
/// 🔒 User, <c>2026-10-29</c>: <i>"the save menu item should never be just plain save. It needs to be save scenario, save btree, save HSM etc to Make it clear for the user what is going to be saved."</i>
/// ⭐ Used by the shell's saves (<see cref="ShellSaveCommands"/>) and the scenario's (<c>ScenarioMenuCommands</c>), on every host.
/// </summary>
public static class SaveLabels
{
    /// <summary>The user's word for a document kind.</summary>
    public static string KindName(AssetKind kind) => kind switch
    {
        AssetKind.BTree     => "BTree",
        AssetKind.Hsm       => "HSM",
        AssetKind.Blueprint => "Blueprint",
        _                   => kind.ToString(),
    };

    /// <summary><c>"&lt;verb&gt; &lt;kind&gt; '&lt;name&gt;'"</c>, or <c>"&lt;verb&gt; &lt;kind&gt;…"</c> while it has no name yet (the save then asks for one).</summary>
    public static string Named(string verb, string kind, string? name)
        => string.IsNullOrWhiteSpace(name) ? $"{verb} {kind}…" : $"{verb} {kind} '{name}'";

    /// <summary>The shell's <i>Save</i> label for <paramref name="doc"/>.</summary>
    public static string For(string verb, AiDocument doc) => Named(verb, KindName(doc.Kind), doc.Asset.Name);

    /// <summary><i>Save All (3 changed)</i> — Save All counts what it will write.</summary>
    public static string SaveAll(int changed) => changed == 0 ? "Save All (nothing changed)" : $"Save All ({changed} changed)";
}
