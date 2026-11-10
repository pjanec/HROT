using System.Globalization;
using System.Text;

namespace Hrot.Terrain.InputSet;

/// <summary><c>trees/trees.csv</c>: a header <c>x,y,z_ground</c>, one tree per line, two decimals, invariant culture, LF.</summary>
public static class TreesCsv
{
    public static void Write(string path, IReadOnlyList<(double X, double Y, double Z)> t)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var sb = new StringBuilder(t.Count * 24 + 16);
        sb.Append("x,y,z_ground\n");
        foreach (var (x, y, z) in t)
            sb.Append(x.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(y.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(z.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    public static List<(double X, double Y, double Z)> Read(string path)
    {
        var list = new List<(double, double, double)>();
        bool header = true;
        foreach (var line in File.ReadLines(path))
        {
            if (header) { header = false; continue; }
            if (line.Length == 0) continue;
            var p = line.Split(',');
            if (p.Length < 3) throw new InvalidDataException($"{path}: a line with fewer than 3 values: \"{line}\"");
            list.Add((double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture),
                      double.Parse(p[2], CultureInfo.InvariantCulture)));
        }
        return list;
    }
}
