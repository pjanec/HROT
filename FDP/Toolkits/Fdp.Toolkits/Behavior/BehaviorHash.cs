using System;
using System.Text;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// Canonical, process-stable identity hash for a behavior <b>name</b>.
    ///
    /// <para>The behavior name is the stable external identity — scenarios reference behaviors by
    /// name (<c>MissionPlan…behaviorName</c>), not by an assigned integer. This maps a name to the
    /// 32-bit integer key used by <see cref="BehaviorRegistry"/> and stored in
    /// <see cref="Components.BehaviorState.ActiveBehaviorHash"/>. Both the runtime and the code
    /// generator MUST call this one method so the id a behavior registers under equals the id any
    /// cross-reference resolves to.</para>
    ///
    /// <para>Algorithm: FNV-1a 32-bit over the UTF-8 bytes of the name (basis 2166136261,
    /// prime 16777619) — the same family as <see cref="Blueprints.BlueprintIdHash"/>. It is
    /// deliberately deterministic across processes and runs. <c>string.GetHashCode()</c> is
    /// <b>forbidden</b> here: it is randomized per process (see DEBT-006), which would make behavior
    /// ids non-reproducible across runs/nodes.</para>
    /// </summary>
    public static class BehaviorHash
    {
        // FNV-1a 32-bit constants (mirrors BlueprintIdHash).
        private const uint OffsetBasis = 2166136261u;
        private const uint FnvPrime    = 16777619u;

        /// <summary>
        /// FNV-1a-32 of the UTF-8 bytes of <paramref name="name"/>, as a signed <see cref="int"/>
        /// (the storage type of <see cref="Components.BehaviorState.ActiveBehaviorHash"/> and the key
        /// type of <see cref="BehaviorRegistry"/>).
        ///
        /// <para>A null or empty name maps to <c>0</c> so "no behavior" stays the zero sentinel
        /// (<see cref="BehaviorIds.None"/>). A non-empty name never returns <c>0</c>: on the
        /// (astronomically unlikely) event the hash is zero, it is nudged to a fixed non-zero value
        /// so a real behavior can never collide with the None sentinel.</para>
        /// </summary>
        public static int FromName(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;

            uint hash = OffsetBasis;
            int byteCount = Encoding.UTF8.GetByteCount(name);
            Span<byte> buffer = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
            Encoding.UTF8.GetBytes(name, buffer);
            foreach (byte b in buffer)
            {
                hash ^= b;
                hash *= FnvPrime;
            }

            if (hash == 0u)
                hash = FnvPrime; // never alias the None (0) sentinel for a real name

            return unchecked((int)hash);
        }
    }
}
