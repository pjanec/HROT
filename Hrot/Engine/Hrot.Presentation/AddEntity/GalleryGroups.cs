using System.Text.Json;
using Fdp.Core;
using Hrot.Core.Tkb;

namespace Hrot.UI.Common.AddEntity;

/// <summary>Which DIS level a group's chip row is made of.</summary>
public enum ChipLevel
{
    /// <summary>The first NAMED level (Domain, Category) below the deepest level the matching mask pins; none below ⇒ no chip.</summary>
    Next,
    Kind,
    Domain,
    Category,
    /// <summary>No chips for this group.</summary>
    None,
}

/// <summary>One gallery group: a name, the DIS masks (side-bearing types) or placement tools (map graphics) it takes.</summary>
/// <param name="IsRecent">True for the first, virtual group (Recent &amp; favourites): it owns no type, the picker fills it from its stores.</param>
public sealed record GalleryGroup(string Name, IReadOnlyList<DisTypeMask> Masks, IReadOnlyList<PlacementToolKind> Tools,
                                  ChipLevel Chips, bool IsRecent);

/// <summary>
/// ⭐ <c>CE-4142</c> GL1 (G1 + G2) — the Add Entity gallery's GROUPS, read from ONE data file, <c>assets/thumbnails/groups.json</c>.
/// 📄 docs/DESIGN_Add_Entity_Picker.md §2f G1/G2, §2g.
///
/// <para>⭐ A side-bearing type lands in the group of the MOST SPECIFIC mask matching its DIS type (<see cref="DisTypeMask.Specificity"/>;
/// a tie ⇒ the group listed first), so a group can say "all land platforms" while another takes "weapon systems" out of it. A map
/// graphic lands in the group that lists its placement tool. Nothing else matches ⇒ <see cref="OtherName"/>.</para>
///
/// <para>⭐ The chip (G2) is the NEXT DIS level below the winning mask, named by <see cref="DisNameTable"/> (a country is not a chip — it is
/// not a class); a group can pick another level in the file (<c>"chips"</c>).</para>
/// </summary>
public sealed class GalleryGroups
{
    /// <summary>The group a type that matches nothing falls into (shown by the picker only when non-empty).</summary>
    public const string OtherName = "Other";

    private readonly List<GalleryGroup> _groups;

    private GalleryGroups(List<GalleryGroup> groups) => _groups = groups;

    /// <summary>The groups in sidebar order (the file's order).</summary>
    public IReadOnlyList<GalleryGroup> Groups => _groups;

    /// <summary>The virtual Recent &amp; favourites group's name, or null when the file has none.</summary>
    public string? RecentName => _groups.FirstOrDefault(g => g.IsRecent)?.Name;

    /// <summary>Every group name the sidebar lists, in order, <see cref="OtherName"/> last.</summary>
    public IReadOnlyList<string> SidebarOrder => _groups.Select(g => g.Name).Append(OtherName).ToArray();

    /// <summary>Parses the file's JSON; throws <see cref="FormatException"/> naming what is wrong (a bad mask, an unknown tool, a duplicate or empty name).</summary>
    public static GalleryGroups Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e) { throw new FormatException("groups.json: " + e.Message); }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("groups", out var arr) || arr.ValueKind != JsonValueKind.Array)
                throw new FormatException("groups.json: no \"groups\" array.");

            var groups = new List<GalleryGroup>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in arr.EnumerateArray())
            {
                string name = g.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (name.Length == 0) throw new FormatException("groups.json: a group has no name.");
                if (!names.Add(name)) throw new FormatException($"groups.json: the group '{name}' is listed twice.");
                if (name.Equals(OtherName, StringComparison.OrdinalIgnoreCase))
                    throw new FormatException($"groups.json: '{OtherName}' is the built-in catch-all, not a listed group.");
                if (name.Contains('/')) throw new FormatException($"groups.json: the group name '{name}' may not contain '/'.");

                var masks = new List<DisTypeMask>();
                if (g.TryGetProperty("masks", out var ms))
                    foreach (var m in ms.EnumerateArray())
                    {
                        if (!DisTypeMask.TryParse(m.GetString(), out var mask))
                            throw new FormatException($"groups.json: group '{name}': '{m.GetString()}' is not a DIS mask.");
                        masks.Add(mask);
                    }

                var tools = new List<PlacementToolKind>();
                if (g.TryGetProperty("tools", out var ts))
                    foreach (var t in ts.EnumerateArray())
                    {
                        if (!Enum.TryParse<PlacementToolKind>(t.GetString(), ignoreCase: true, out var tool))
                            throw new FormatException($"groups.json: group '{name}': '{t.GetString()}' is not a placement tool.");
                        tools.Add(tool);
                    }

                var chips = ChipLevel.Next;
                if (g.TryGetProperty("chips", out var cp) && cp.GetString() is { Length: > 0 } cs
                    && !Enum.TryParse(cs, ignoreCase: true, out chips))
                    throw new FormatException($"groups.json: group '{name}': chips '{cs}' is not next | kind | domain | category | none.");

                bool recent = g.TryGetProperty("recent", out var r) && r.ValueKind == JsonValueKind.True;
                if (recent && (masks.Count > 0 || tools.Count > 0))
                    throw new FormatException($"groups.json: the recent group '{name}' takes no masks or tools.");
                if (!recent && masks.Count == 0 && tools.Count == 0)
                    throw new FormatException($"groups.json: group '{name}' lists no masks and no tools.");

                groups.Add(new GalleryGroup(name, masks, tools, chips, recent));
            }
            return new GalleryGroups(groups);
        }
    }

    /// <summary>Reads <paramref name="path"/>; null when it is missing or unreadable (the caller then offers the plain tree, not a one-group gallery).</summary>
    public static GalleryGroups? TryLoad(string path, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path)) { error = "missing: " + path; return null; }
            return Parse(File.ReadAllText(path));
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            error = e.Message;
            return null;
        }
    }

    /// <summary>
    /// The group (and chip) of a type. <paramref name="hasSide"/> true ⇒ a physical type placed by DIS mask; false ⇒ a map graphic placed
    /// by its <paramref name="tool"/>. A type with no DIS type (<paramref name="dis"/> null or 0) is <see cref="OtherName"/>.
    /// </summary>
    public (string Group, string? Chip) Classify(DISEntityType? dis, PlacementToolKind tool, bool hasSide, DisNameTable names)
    {
        if (!hasSide)
        {
            foreach (var g in _groups)
                if (g.Tools.Contains(tool)) return (g.Name, null);
            return (OtherName, null);
        }

        if (dis is not { } t || t.Value == 0) return (OtherName, null);

        GalleryGroup? best = null;
        DisTypeMask? bestMask = null;
        foreach (var g in _groups)
            foreach (var m in g.Masks)
                if (m.Matches(t) && (bestMask is null || m.Specificity > bestMask.Specificity))
                {
                    best = g;
                    bestMask = m;
                }
        if (best is null || bestMask is null) return (OtherName, null);
        return (best.Name, ChipOf(best.Chips, bestMask, t, names));
    }

    /// <summary>The chip a type gets under a group's matching mask; null for none.</summary>
    public static string? ChipOf(ChipLevel level, DisTypeMask mask, DISEntityType t, DisNameTable names)
    {
        string? Kind() => t.Kind != 0 ? names.KindName(t.Kind) : null;
        string? Domain() => t.Domain != 0 ? names.DomainName(t.Kind, t.Domain) : null;
        string? Category() => t.Category != 0 ? names.CategoryName(t.Kind, t.Domain, t.Category) : null;

        switch (level)
        {
            case ChipLevel.None: return null;
            case ChipLevel.Kind: return Kind();
            case ChipLevel.Domain: return Domain();
            case ChipLevel.Category: return Category();
            default:
                // The named levels are Kind (0), Domain (1) and Category (3); Country (2) is not a class and is never a chip.
                int pinned = mask.DeepestPinned;
                if (pinned < 1 && Domain() is { } d) return d;
                if (pinned < 3) return Category();
                return null;
        }
    }
}
