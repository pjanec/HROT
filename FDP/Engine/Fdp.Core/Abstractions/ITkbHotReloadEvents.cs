using System;

namespace Fdp.Core.Tkb
{
    /// <summary>
    /// Event raised when a TKB descriptor is hot-reloaded during development.
    /// Allows systems depending on descriptor data to invalidate caches.
    /// </summary>
    public sealed class TkbDescriptorChangedEvent
    {
        /// <summary>
        /// The descriptor name (e.g., "Anim.CharacterDef").
        /// </summary>
        public required string DescriptorName { get; init; }

        /// <summary>
        /// The character class ID that was affected.
        /// </summary>
        public required long ClassId { get; init; }
    }

    /// <summary>
    /// Hot-reload event service for TKB descriptors (DD-4 §7, §9.1).
    /// Allows translators and caches to subscribe to descriptor changes
    /// and invalidate cached data when descriptors are reloaded.
    /// </summary>
    public interface ITkbHotReloadEvents
    {
        /// <summary>
        /// Subscribe to descriptor change events.
        /// </summary>
        /// <param name="handler">Callback invoked when a descriptor changes.</param>
        /// <returns>Disposable subscription token. Dispose to unsubscribe.</returns>
        IDisposable Subscribe(Action<TkbDescriptorChangedEvent> handler);
    }
}
