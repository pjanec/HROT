namespace Hrot.Diagnostics.Tuning
{
    using System;
    using Fdp.Toolkit.Utility;

    // Tunable record for UtilityCurve-typed fields.
    // Analogous to Tunable but carries Func<UtilityCurve>/Action<UtilityCurve>
    // delegates instead of float. No min/max -- curve sub-field ranges vary.
    public sealed class CurveTunable
    {
        public TuningKey Key;
        public TuningScope Scope;
        public TuningOwner Owner;
        public string Provenance = string.Empty;
        public required Func<UtilityCurve>   Read;
        public required Action<UtilityCurve> Write;
        // Authored default captured at registration time. Used by TuningRegistry.RevertGroup/RevertAll.
        public UtilityCurve DefaultCurve;
    }
}
