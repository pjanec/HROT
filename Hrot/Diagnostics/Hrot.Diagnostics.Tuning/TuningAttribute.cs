using System;

namespace Hrot.Diagnostics.Tuning
{
    // Marks a field for automatic discovery by the tuning source-gen (follow-on).
    // In Slice 1 only manual registration is used; this attribute is a forward declaration.
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class TunableAttribute : Attribute
    {
        public float Min { get; set; } = float.MinValue;
        public float Max { get; set; } = float.MaxValue;
        public TuningScope Scope { get; set; } = TuningScope.Global;
        public TuningOwner Owner { get; set; } = TuningOwner.Brain;
    }
}
