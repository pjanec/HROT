using System.Globalization;
using System.IO;
using System.Linq;
using Fdp.Toolkit.Orchestration;
using Xunit;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ <c>CE-4363</c> (L-1, PLAN_Large_Terrain §7.1) — the scale terrain places the shared templates at ANY rotation, kilometres from the
    /// origin. There the float rounding of a wall's length left a sub-millimetre sliver beside an opening, which failed the prism check
    /// ("footprint is degenerate") and the whole terrain refused to load. Every shared template must expand at every such placement.
    /// </summary>
    public class TemplatePlacementTests
    {
        private static readonly string[] Templates =
            { "house-2f", "house-3f", "shop-2f", "courtyard-house", "cqb-house", "office-4f", "market-hall" };

        private static TerrainWorld Place(string template, float x, float y, float rotation)
        {
            var folder = Path.Combine(ContentLayout.TerrainAssets(ContentLayout.TryFindSourceTree()!), "bt-range");
            string json = string.Format(CultureInfo.InvariantCulture,
                """
                {{"type":"FeatureCollection","hrot":{{"schemaVersion":1,"bounds":[0,0,5500,5500],"groundZ":0}},"features":[
                 {{"type":"Feature","properties":{{"kind":"building","template":"{0}","label":"T","rotation":{3}}},
                  "geometry":{{"type":"Point","coordinates":[{1},{2}]}}}}]}}
                """, template, x, y, rotation);
            return TerrainWorldParser.Parse(json, "scale-test", TerrainAssets.ForFolder(folder));
        }

        [Fact]
        public void CE4363_TheFailingScale1House_Expands()
        {
            var w = Place("house-3f", 4517.91f, 5019.17f, 263.624f);   // scale-1's '7a1f07a2fcff'
            Assert.Single(w.Buildings);
            Assert.NotEmpty(w.Prisms);
        }

        [Fact]
        public void CE4363_EveryTemplate_AtEveryRotation_FarFromTheOrigin_Expands()
        {
            foreach (var t in Templates)
                for (int i = 0; i < 360; i++)
                {
                    float rot = i + 0.624f;
                    var w = Place(t, 4517.91f + i, 5019.17f - i, rot);
                    Assert.True(w.Prisms.Count > 0, $"{t} at {rot}°");
                }
        }
    }
}
