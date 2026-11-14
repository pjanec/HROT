using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fdp.Core.FlightRecorder.Metadata;
using Fdp.Core.Logging;

namespace Fdp.Core.FlightRecorder
{
    /// <summary>
    /// ⭐⭐ <c>CE-4050</c> — loads the assemblies a recording's types are declared in, before anything resolves a recorded type id.
    /// 📐 The failure it removes: assemblies load LAZILY, so a process that had not touched <c>Hrot.MuscleCharacter.Animation</c> yet
    /// reported <i>"Component type ID 238 not found"</i> for <c>CharacterAnimationDefRuntime</c> and stopped playback — the Replay Browser's
    /// sandbox, the timeline index and the dumper alike. ⭐ The recording names each type's assembly (<see cref="ComponentSchemaInfo.Assembly"/>);
    /// ⛔ not a hand-kept assembly list.
    /// </summary>
    public static class RecordedAssemblies
    {
        /// <summary>Loads every assembly the recording's component and event manifests name that is not loaded yet. An assembly this
        /// install does not carry is said once and skipped — its types' data is then skipped by the playback.</summary>
        /// <returns>The names that could not be loaded.</returns>
        public static IReadOnlyList<string> EnsureLoaded(RecordingMetadata? metadata)
        {
            var missing = new List<string>();
            if (metadata == null) return missing;
            var named = (metadata.SchemaManifest?.Values ?? Enumerable.Empty<ComponentSchemaInfo>())
                .Concat(metadata.EventManifest?.Values ?? Enumerable.Empty<ComponentSchemaInfo>())
                .Select(i => i.Assembly)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.Ordinal);
            var loaded = new HashSet<string>(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? ""), StringComparer.Ordinal);
            foreach (var name in named)
            {
                if (loaded.Contains(name)) continue;
                try
                {
                    Assembly.Load(new AssemblyName(name));
                }
                catch (Exception ex) when (ex is System.IO.FileNotFoundException or System.IO.FileLoadException or BadImageFormatException)
                {
                    missing.Add(name);
                    FdpLog<PlaybackController>.Warn(
                        "[Playback] CE-4050: the recording's types live in '{0}', which this install does not carry — their data is skipped.", name);
                }
            }
            return missing;
        }
    }
}
