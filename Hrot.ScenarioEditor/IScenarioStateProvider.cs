namespace Hrot.ScenarioEditor
{
    /// <summary>
    /// Port interface that exposes the current high-level state of the scenario editor
    /// session to UI adapters without coupling them to internal state machines.
    /// </summary>
    public interface IScenarioStateProvider
    {
        /// <summary>
        /// Gets the current operational state of the scenario editor session.
        /// </summary>
        ScenarioEditorState CurrentState { get; }
    }
}
