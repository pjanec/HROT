using System;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Core.Logging;

namespace Fdp.Core.FlightRecorder
{
    /// <summary>
    /// Validates a Flight Recorder binary against the component schema manifest saved
    /// at record time.
    ///
    /// <para>
    /// Called by <see cref="PlaybackController"/> immediately after deserialising the
    /// <c>.meta.json</c> file, before any binary frame data is read.  If any component's
    /// struct layout (size or field topology) has changed since recording, an
    /// <see cref="InvalidOperationException"/> is thrown to prevent silent memory
    /// corruption during playback.
    /// </para>
    ///
    /// <para>
    /// <b>Backward compatibility:</b> recordings without a schema manifest produce a
    /// console warning but do not throw.  This allows replaying legacy
    /// <c>.fdp</c> files while still keeping strict validation for modern recordings.
    /// </para>
    /// </summary>
    public static class SchemaValidator
    {
        /// <summary>
        /// Validates the <paramref name="meta"/> schema manifest against the component
        /// types currently registered in <see cref="ComponentTypeRegistry"/>.
        /// </summary>
        /// <param name="meta">
        /// The recording metadata deserialised from the <c>.meta.json</c> file.
        /// Must not be <c>null</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="meta"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown if a component ID in the manifest is not registered in the current binary,
        /// or if the component's struct size or layout hash has changed since recording.
        /// </exception>
        public static void Validate(RecordingMetadata meta)
        {
            if (meta == null) throw new ArgumentNullException(nameof(meta));

            if (meta.SchemaManifest == null)
            {
                // Old recording produced before schema manifest support.
                // Log a warning but do not abort — best-effort backward compatibility.
                FdpLog<PlaybackController>.Warn(
                    "WARNING: Recording has no SchemaManifest. " +
                    "Playback may fail silently if any struct layouts changed since recording.");
                return;
            }

            foreach (var (componentId, recorded) in meta.SchemaManifest)
            {
                // Resolve the current type for this component ID.
                // ⭐ CE-1372 — the type the id names in the loaded assemblies, registered here or not (FindType: the [ComponentId] scan,
                //   which the playback uses too). A type no loaded assembly declares is NOT a reason to refuse the recording: the playback
                //   skips it (CE-4050, UnresolvedTypes). ⛔ Was: refused when this process had merely not REGISTERED it yet — an empty world.
                var currentType = ComponentTypeRegistry.FindType(componentId);
                if (currentType == null)
                {
                    FdpLog<PlaybackController>.Warn(
                        "Recording component ID {0} ('{1}') is in no loaded assembly — it is skipped in playback.", componentId, recorded.Name);
                    continue;
                }

                if (currentType.IsValueType && !currentType.IsEnum)
                {
                    // Validate struct size.
                    int currentSize = TypeLayout.SizeOf(currentType);   // ⭐ CE-2044 — managed, as recorded
                    if (currentSize != recorded.Size)
                    {
                        throw new InvalidOperationException(
                            $"Schema mismatch: component '{recorded.Name}' (ID {componentId}) layout has changed. " +
                            $"Recorded size = {recorded.Size} bytes, current size = {currentSize} bytes. " +
                            "The recording cannot be played back safely.");
                    }

                    // Validate layout hash (detects field reordering / renames even when size is stable).
                    ulong currentHash = ComponentLayoutHasher.ComputeHash(currentType);
                    if (currentHash != recorded.LayoutHash)
                    {
                        throw new InvalidOperationException(
                            $"Schema mismatch: component '{recorded.Name}' (ID {componentId}) layout has changed. " +
                            $"Recorded hash 0x{recorded.LayoutHash:X16}, current hash 0x{currentHash:X16} " +
                            $"(recorded size = {recorded.Size} bytes, current size = {currentSize} bytes). " +
                            "The recording cannot be played back safely.");
                    }
                }
                else if (!currentType.IsValueType)
                {
                    // Validate managed logical hash (detects added/removed fields or [Key] reordering)
                    ulong currentHash = ComponentLayoutHasher.ComputeManagedHash(currentType);
                    if (currentHash != recorded.LayoutHash)
                    {
                        throw new InvalidOperationException(
                            $"Schema mismatch: managed component '{recorded.Name}' (ID {componentId}) logical layout has changed. " +
                            $"Recorded hash 0x{recorded.LayoutHash:X16}, current hash 0x{currentHash:X16}. " +
                            "The recording cannot be played back safely.");
                    }
                }
            }

            if (meta.EventManifest != null)
            {
                foreach (var (eventId, recorded) in meta.EventManifest)
                {
                    // ⭐ CE-1372 — as for components: found by its [EventId] when not registered; an event no loaded assembly declares is
                    //   carried untyped by the playback (InjectIntoCurrentBySize), so it is skipped here, never a refusal.
                    var currentType = EventTypeRegistry.FindType(eventId);
                    if (currentType == null)
                    {
                        FdpLog<PlaybackController>.Warn(
                            "Recording event ID {0} ('{1}') is in no loaded assembly — it is carried untyped in playback.", eventId, recorded.Name);
                        continue;
                    }

                    if (currentType.IsValueType && !currentType.IsEnum)
                    {
                        int currentSize = TypeLayout.SizeOf(currentType);   // ⭐ CE-2044 — managed, as recorded
                        if (currentSize != recorded.Size)
                        {
                            throw new InvalidOperationException(
                                $"Schema mismatch: event '{recorded.Name}' (ID {eventId}) layout has changed. " +
                                $"Recorded size = {recorded.Size} bytes, current size = {currentSize} bytes. " +
                                "The recording cannot be played back safely.");
                        }

                        ulong currentHash = ComponentLayoutHasher.ComputeHash(currentType);
                        if (currentHash != recorded.LayoutHash)
                        {
                            throw new InvalidOperationException(
                                $"Schema mismatch: event '{recorded.Name}' (ID {eventId}) layout has changed. " +
                                $"Recorded hash 0x{recorded.LayoutHash:X16}, current hash 0x{currentHash:X16}. " +
                                "The recording cannot be played back safely.");
                        }
                    }
                    else if (!currentType.IsValueType)
                    {
                        ulong currentHash = ComponentLayoutHasher.ComputeManagedHash(currentType);
                        if (currentHash != recorded.LayoutHash)
                        {
                            throw new InvalidOperationException(
                                $"Schema mismatch: managed event '{recorded.Name}' (ID {eventId}) logical layout has changed. " +
                                $"Recorded hash 0x{recorded.LayoutHash:X16}, current hash 0x{currentHash:X16}. " +
                                "The recording cannot be played back safely.");
                        }
                    }
                }
            }
        }
    }
}
