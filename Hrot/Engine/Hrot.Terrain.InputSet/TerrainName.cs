using System.Text.RegularExpressions;

namespace Hrot.Terrain.InputSet;

/// <summary>
/// ⭐ CE-4378 — the ONE rule for a terrain's name (a folder under <c>assets/terrain</c>): letters and digits first, then letters, digits, '-' and '_'. The importer
/// refuses anything else and the wizard's Options step says so as you type — one rule, so they can never disagree.
/// </summary>
public static partial class TerrainName
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_\-]*$")]
    private static partial Regex Rule();

    public static bool IsValid(string? name) => name != null && Rule().IsMatch(name);
}
