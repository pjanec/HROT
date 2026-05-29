using System;

namespace Hrot.Diagnostics.Tuning
{
    public sealed class Tunable
    {
        public TuningKey   Key;
        public TuningKind  Kind;
        public float       Min;
        public float       Max;
        public TuningScope Scope;
        public TuningOwner Owner;
        public required Func<float>    Read;
        public required Action<float>  Write;
        public string      Provenance = string.Empty;
        // GroupKey is the first two segments of Key.Name up to the second dot.
        // e.g. "utility.CombatPosture.0.0.weight" -> group "utility.CombatPosture"
    }
}
