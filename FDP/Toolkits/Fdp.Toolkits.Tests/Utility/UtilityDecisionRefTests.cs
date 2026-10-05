using System.Linq;
using System.Text.Json;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Tests
{
    /// <summary>
    /// ⭐ <c>CE-2068</c> — <see cref="UtilityDecisionRef"/>: saved as the decision's ASSET ID, read back to the same id; a bare
    /// number and null still read. 📄 <c>docs/DESIGN_Decision_Layer.md</c> §3.3.
    /// </summary>
    public sealed class UtilityDecisionRefTests
    {
        private sealed class Holder { public UtilityDecisionRef Decision { get; set; } }

        [Fact]
        public void CE2068_ARegisteredDecision_IsSavedAsItsAssetId_AndReadBack()
        {
            UtilityDecisionCatalog.EnsureRegistered();
            var (id, def) = UtilityDecisionCatalog.Shared.Entries.First(e => !string.IsNullOrEmpty(e.Def.AssetId));
            string json = JsonSerializer.Serialize(new Holder { Decision = new UtilityDecisionRef(id) });
            Assert.Contains("\"" + def.AssetId + "\"", json);
            Assert.Equal(id, JsonSerializer.Deserialize<Holder>(json)!.Decision.Id);
        }

        [Fact]
        public void CE2068_ANumberNullAndNone_StillRead()
        {
            Assert.Equal(42, JsonSerializer.Deserialize<Holder>("{\"Decision\":42}")!.Decision.Id);
            Assert.True(JsonSerializer.Deserialize<Holder>("{\"Decision\":null}")!.Decision.IsNone);
            Assert.Contains("\"Decision\":0", JsonSerializer.Serialize(new Holder()));
            Assert.Equal(UtilityDecisionCatalog.ComputeId("abc"), UtilityDecisionRef.FromAssetId("abc").Id);
        }
    }
}
