using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Hrot.ClusterRunner.Presentation;
using Xunit;

namespace Hrot.ClusterRunner.Tests
{
    /// <summary>
    /// ⭐ R-4221 (F-5) + U-1b — the <c>[FrameStats]</c> log is culture-INVARIANT by construction: under <c>cs-CZ</c> every number carries a
    /// decimal POINT, and the merged <c>ui</c> fields (CE-1495: the ImGui render time) are present. 📌 A Czech Windows machine printed
    /// <c>mean 8,9 ms</c> and the measuring script read 8.
    /// 🔴 Red-proof: the ImGui-render field written with a plain <c>$"…"</c> (not <c>I(…)</c>) ⇒ <c>0,00</c> under cs-CZ.
    /// </summary>
    [Collection(nameof(PerformanceStartupTests))]   // Console.Out is process-wide: never beside another test that swaps it
    public class FrameStatsTests
    {
        [Fact]
        public void UnderCsCz_EveryNumberHasADecimalPoint_AndTheImGuiRenderFieldIsThere()
        {
            var culture = CultureInfo.CurrentCulture; var ui = CultureInfo.CurrentUICulture; var outWas = Console.Out;
            var sw = new StringWriter();
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("cs-CZ");
                Console.SetOut(sw);
                new FrameStats().ReportNow(12.5);
            }
            finally { Console.SetOut(outWas); CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui; }

            string log = sw.ToString();
            Assert.Contains("[FrameStats] 1 frames  mean 12.5 ms", log);
            Assert.Contains("imgui render 0.00", log);
            Assert.DoesNotMatch(new Regex(@"\d,\d"), log);
        }
    }
}
