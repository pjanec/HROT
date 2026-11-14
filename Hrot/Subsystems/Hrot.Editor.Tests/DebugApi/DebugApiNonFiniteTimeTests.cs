using System.Text.Json;
using Fdp.Core;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi
{
    /// <summary>
    /// ⭐ CE-1471 / CE-3441 — <c>GET /entities</c> failed AS A WHOLE when one component held ±∞ (<i>"positive and negative infinity cannot be written
    /// as valid JSON"</i>): the dump options' non-finite converters are for <c>float</c> / <c>double</c> / vectors, and a <see cref="SimInstant"/>
    /// (<c>SquadUnitSense.AskedAt = -∞</c>, since the SV-1 migration) went through its own type converter, which wrote the raw double. The one
    /// converter now writes a marker, so the dump options carry it too. 🔴 Red-proof: the marker branch dropped ⇒ this throws.
    /// </summary>
    public sealed class DebugApiNonFiniteTimeTests
    {
        private struct Row { public SimInstant AskedAt; public SimDuration For; public float Plain; }

        [Fact]
        public void CE1471_AComponentHoldingAnInfiniteSimInstant_SerializesThroughTheDumpOptions()
        {
            var row = new Row { AskedAt = (SimInstant)double.NegativeInfinity, For = (SimDuration)float.PositiveInfinity, Plain = float.NaN };
            var opts = new JsonSerializerOptions(DebugApiService.DebugApiDumpOptions) { IncludeFields = true };
            string json = JsonSerializer.Serialize(row, opts);
            Assert.Contains("\"-inf\"", json);
            Assert.Contains("\"+inf\"", json);
        }
    }
}
