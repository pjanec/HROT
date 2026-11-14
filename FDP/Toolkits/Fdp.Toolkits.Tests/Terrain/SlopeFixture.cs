using System.Globalization;
using System.Text;

namespace Fdp.Toolkit.Terrain.Tests
{
    /// <summary>
    /// ⭐ CE-1034 H1 (TH-G, R-248) — the sloped test terrain: a 200 m square whose ground rises northwards, z = <see cref="Rise"/> × y
    /// (10 % — a hill a vehicle drives up), as an ESRI ASCII grid at 10 m cells beside a world file, plus whatever features a test
    /// adds. Rails run on it so nothing is built against the flat bed.
    /// </summary>
    public static class SlopeFixture
    {
        public const float Rise = 0.1f;
        public const float Cell = 10f;
        public const int Samples = 21;   // 0 … 200 m

        /// <summary>The grid file: corner-free (centre) origin at (0, 0), the NORTH row first.</summary>
        public static string Grid()
        {
            var sb = new StringBuilder();
            sb.Append("ncols ").Append(Samples).Append('\n').Append("nrows ").Append(Samples).Append('\n')
              .Append("xllcenter 0\nyllcenter 0\ncellsize ").Append(Cell.ToString(CultureInfo.InvariantCulture)).Append("\nNODATA_value -9999\n");
            for (int row = 0; row < Samples; row++)
            {
                float y = (Samples - 1 - row) * Cell;
                for (int c = 0; c < Samples; c++) sb.Append((Rise * y).ToString(CultureInfo.InvariantCulture)).Append(c + 1 < Samples ? " " : "\n");
            }
            return sb.ToString();
        }

        /// <summary>⭐ H2 — a RIDGE: an east–west crest at y = 100, <see cref="RidgeTop"/> high, falling to 0 at y = 0 and y = 200.</summary>
        public const float RidgeTop = 10f;

        public static string RidgeGrid()
        {
            var sb = new StringBuilder();
            sb.Append("ncols ").Append(Samples).Append("\nnrows ").Append(Samples).Append("\nxllcenter 0\nyllcenter 0\ncellsize ")
              .Append(Cell.ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int row = 0; row < Samples; row++)
            {
                float y = (Samples - 1 - row) * Cell;
                float z = RidgeTop - System.MathF.Abs(y - 100f) / 10f;
                for (int c = 0; c < Samples; c++) sb.Append(z.ToString(CultureInfo.InvariantCulture)).Append(c + 1 < Samples ? " " : "\n");
            }
            return sb.ToString();
        }

        /// <summary>The ridge world (no features).</summary>
        public static TerrainWorld Ridge()
        {
            const string geojson = "{ \"type\": \"FeatureCollection\", \"hrot\": { \"schemaVersion\": 1, \"bounds\": [0, 0, 200, 200], \"groundZ\": 0, \"heightGrid\": \"ridge.height.asc\" }, \"features\": [] }";
            var grid = RidgeGrid();
            return TerrainWorldParser.Parse(geojson, "ridge", new TerrainAssets { ReadFile = n => n == "ridge.height.asc" ? grid : null });
        }

        /// <summary>The world: the grid named in <c>hrot.heightGrid</c>, with <paramref name="featuresJson"/> (a comma-separated list, may be empty).</summary>
        public static TerrainWorld World(string featuresJson = "")
        {
            string geojson = "{ \"type\": \"FeatureCollection\", \"hrot\": { \"schemaVersion\": 1, \"bounds\": [0, 0, 200, 200], \"groundZ\": 0, "
                + "\"heightGrid\": \"slope.height.asc\" }, \"features\": [" + featuresJson + "] }";
            var grid = Grid();
            return TerrainWorldParser.Parse(geojson, "slope", new TerrainAssets { ReadFile = n => n == "slope.height.asc" ? grid : null });
        }
    }
}
