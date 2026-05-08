namespace Fdp.Toolkit.Diagnostics.Gizmos.Settings
{
    /// <summary>
    /// Controls the persistence lifecycle of a gizmo setting.
    /// </summary>
    public enum SettingScope : byte
    {
        // Persists to a global user preferences file. Survives across all scenarios.
        // Example: measurement unit preference, default visibility toggles.
        Global  = 0,

        // Persists to the current project/scenario file. Overrides Global for this scenario.
        // Example: gizmo visibility configured per-scenario for a mission set.
        Project = 1,

        // In-memory only. Discarded when the scenario ends or the application restarts.
        // Example: temporary "show all gizmos" debug override during a live session.
        Session = 2,
    }
}
