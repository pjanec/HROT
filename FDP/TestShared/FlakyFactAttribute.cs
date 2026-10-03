using System;
using Xunit;

namespace Fdp.Testing
{
    /// <summary>
    /// A <see cref="FactAttribute"/> for a test known to be timing-flaky: it is SKIPPED, with a stated reason, unless the
    /// environment sets <c>HROT_RUN_FLAKY=1</c>. Pair it with <c>[Trait("Category","Flaky")]</c> so it can also be selected
    /// (<c>--filter Category=Flaky</c>) or excluded. Mirrors <c>SystemSmokeFactAttribute</c>'s opt-in shape.
    /// ⭐ User, 2026-10-02: <i>"disable the flaky editor test or move it to some group that is not run every time."</i>
    /// ⭐ User, 2026-10-03: <i>"disable or mark the timing tests as flaky to stop them from failing the tests."</i>
    /// <para>
    /// ⭐ ONE source, linked into every test project that needs it (<c>&lt;Compile Include … Link&gt;</c>) — there is no shared
    /// test-helper assembly, and a copy per project would be two implementations of one rule.
    /// ⛔ For WALL-CLOCK and allocation-measurement thresholds only — a test whose failure would mean a real defect (a lost
    /// event, a race) is never marked flaky; fix it.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class FlakyFactAttribute : FactAttribute
    {
        public const string OptInVariable = "HROT_RUN_FLAKY";

        public FlakyFactAttribute(string why)
        {
            if (Environment.GetEnvironmentVariable(OptInVariable) != "1")
                Skip = "Flaky (" + why + ") — not run by default; set " + OptInVariable + "=1 to run it.";
        }
    }
}
