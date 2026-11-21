using CycloneDDS.Runtime;

namespace Fdp.Toolkit.Replication.Services
{
    /// <summary>
    /// ⭐⭐ R-4007 / CE-3329 W5 (DESIGN_Replay_Time_Axis_And_Review §12u) — the ONE take-and-drop every
    /// <see cref="Fdp.Interfaces.IDiscardableIngress"/> translator calls for each reader it owns: a join throws away the events that
    /// arrived while it could not use them, a replay's exit throws away the state and events of the replay era.
    /// ⛔ Never applies anything to the world.
    /// </summary>
    public static class DdsReaderDiscard
    {
        /// <summary>Takes every pending sample of <paramref name="reader"/> and drops it. Returns how many were dropped;
        /// 0 for a null reader (a translator built without a participant, in tests).</summary>
        public static int DiscardAll<T>(this DdsReader<T>? reader) where T : new()
        {
            if (reader is null) return 0;
            int dropped = 0;
            while (true)
            {
                using var loan = reader.Take(256);
                if (loan.Count == 0) return dropped;
                dropped += loan.Count;
            }
        }
    }
}
