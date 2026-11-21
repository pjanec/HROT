using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Fdp.Toolkit.Orchestration;
using NodeEditor.UI.Picker;

namespace Hrot.Editor.AiShared.Browser;

/// <summary>
/// ⭐ CE-4210 MA-6 — the <b>Macros</b> group of the scenario picker: one entry per macro folder under <c>macros/</c> (the SAME <see cref="Catalog.ScenarioEnumeration"/> over the root the LOAD resolves
/// against — the NAS, <c>ContentLayout.Macros(root)</c> — so the list never offers a name that loads empty). A pick is the ordinary scenario open of <see cref="ContentLayout.MacroName"/> (an edit load).
/// ⛔ Never falls back to the repository: an empty group says so (<see cref="NoMacrosText"/>).
/// </summary>
public static class MacroPickerEntries
{
    public const string GroupName = "Macros";
    public const string NoMacrosId = "macros.none";
    public const string NoMacrosText = "No macros on the server — see macros/ seeding";

    /// <summary>⭐ The macros root the LOAD resolves against: the NAS's <c>macros/</c> (<see cref="ContentLayout.Macros"/>). ⛔ Never the repository.</summary>
    public static string Root() => ContentLayout.Macros(ContentLayout.NasRoot);

    /// <summary>The macro paths on that root — the SAME <see cref="Catalog.ScenarioEnumeration"/> the scenario list uses (the marker is <c>scenario.json</c>), shared by the picker's Macros group and Add Entity's.</summary>
    public static IReadOnlyList<string> Paths() => Catalog.ScenarioEnumeration.EnumerateRelPaths(Root());

    /// <summary>The pick's asset: <see cref="IEditableAsset.Name"/> is the macro's SCENARIO NAME (<c>macros/&lt;path&gt;</c>) — what the load resolves.</summary>
    public sealed class MacroAsset : IEditableAsset
    {
        public MacroAsset(string path, string? macrosRoot)
        {
            Path_ = path;
            Name = ContentLayout.MacroName(path);
            AssetId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(Name)).AsSpan(0, 16));
            SourceFilePath = macrosRoot is null ? "" : System.IO.Path.Combine(macrosRoot, path, ContentLayout.MacroFileName);
        }
        /// <summary>The macro's path under <c>macros/</c>.</summary>
        public string Path_ { get; }
        public Guid AssetId { get; }
        public string Name { get; }
        public AssetKind Kind => AssetKind.Scenario;
        public string SourceFilePath { get; }
        public bool IsDirty => false;
        public bool IsEditorOwned => false;
#pragma warning disable CS0067
        public event Action? Changed;
#pragma warning restore CS0067
    }

    /// <summary>The picker entries of <paramref name="macroPaths"/> (the relative paths under <c>macros/</c>), or the one disabled hint when there are none.</summary>
    public static IReadOnlyList<PickerEntry> Entries(IReadOnlyList<string> macroPaths, string? macrosRoot)
    {
        if (macroPaths.Count == 0)
            return new[] { new PickerEntry(NoMacrosId, NoMacrosText, null, GroupName, null, null, null, IsEnabled: false) };
        var list = new List<PickerEntry>(macroPaths.Count);
        foreach (var path in macroPaths)
        {
            var asset = new MacroAsset(path, macrosRoot);
            int slash = path.LastIndexOf('/');
            string folder = slash > 0 ? path.Substring(0, slash) : "";
            list.Add(new PickerEntry(
                Id: AssetRecents.ItemKey(asset), Name: slash >= 0 ? path.Substring(slash + 1) : path, Description: null,
                Category: folder.Length == 0 ? GroupName : GroupName + "/" + folder,
                Keywords: new[] { path }, IconTextureId: null, Tag: asset,
                IconKey: AssetKindIcons.ResolveIconKey(asset), RecentContext: AssetRecents.ContextFor(AssetKind.Scenario)));
        }
        return list;
    }
}
