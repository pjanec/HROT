using System;
using Xunit;

namespace Hrot.Editor.Tests
{
    /// <summary>
    /// A <see cref="FactAttribute"/> for a test known to be timing-flaky: it is SKIPPED, with a stated reason, unless the
    /// environment sets <c>HROT_RUN_FLAKY=1</c>. Pair it with <c>[Trait("Category","Flaky")]</c> so it can also be selected
    /// (<c>--filter Category=Flaky</c>) or excluded. Mirrors <c>SystemSmokeFactAttribute</c>'s opt-in shape.
    /// ⭐ User, 2026-10-02: <i>"disable the flaky editor test or move it to some group that is not run every time."</i>
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
