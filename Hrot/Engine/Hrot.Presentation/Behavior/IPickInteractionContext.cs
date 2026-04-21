namespace Hrot.Presentation.Behavior
{
    /// <summary>
    /// Provides per-field pick request coordination for behavior parameter editors.
    ///
    /// <para>Implemented by panels (e.g., MissionPanel) to handle asynchronous
    /// location and entity pick requests, tracking which specific DTO field is
    /// awaiting a pick result so that the UI can show per-field pending state.</para>
    /// </summary>
    public interface IPickInteractionContext
    {
        /// <summary>
        /// Returns <c>true</c> if a pick operation is currently in flight for the
        /// specified task index and property name.
        /// </summary>
        /// <param name="taskIndex">Zero-based mission task index.</param>
        /// <param name="propertyName">Property name in the DTO (e.g., "TargetNetworkId").</param>
        bool IsPickPendingFor(int taskIndex, string propertyName);

        /// <summary>
        /// Requests an entity pick for the given task field.
        /// </summary>
        /// <param name="taskIndex">Zero-based mission task index.</param>
        /// <param name="propertyName">Property name in the DTO.</param>
        /// <param name="filterPresets">Optional entity filter preset strings.</param>
        void RequestEntityPick(int taskIndex, string propertyName, string[]? filterPresets);

        /// <summary>
        /// Requests a world location pick for the given task field.
        /// </summary>
        /// <param name="taskIndex">Zero-based mission task index.</param>
        /// <param name="propertyName">Property name in the DTO.</param>
        void RequestLocationPick(int taskIndex, string propertyName);
    }
}
