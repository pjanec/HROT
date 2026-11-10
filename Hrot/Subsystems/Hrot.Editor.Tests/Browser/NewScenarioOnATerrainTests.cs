using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Recipes;
using System.Linq;
using NodeEditor.UI.Picker;
using Xunit;

namespace Hrot.Editor.Tests.Browser;

/// <summary>
/// ⭐ CE-4378 (handoff D-4) — the OSM wizard's "Open in New Scenario": the SAME New Scenario picker, opened with the just-imported terrain's row selected
/// (<see cref="NewAssetLauncher.OpenScenarioOn"/>), and that row is one the picker lists. 🔴 Red-proof (compiles): <c>initial</c> ignored ⇒ the default row is selected.
/// </summary>
public sealed class NewScenarioOnATerrainTests
{
    private sealed class Svc : INewAssetService
    {
        public AssetKind Kind => AssetKind.Scenario;
        public IReadOnlyList<IEditableAsset> AvailableRecipes() => Array.Empty<IEditableAsset>();
        public IEditableAsset CreateNew(IEditableAsset? recipe, string name, string relPath) => throw new NotSupportedException();
    }

    [Fact]
    public void OpenScenarioOn_SelectsTheTerrainsRow_InTheSamePicker()
    {
        PickerRequest? request = null;
        var launcher = new NewAssetLauncher((r, _) => request = r, new Dictionary<AssetKind, INewAssetService> { [AssetKind.Scenario] = new Svc() }, (_, _) => { },
            terrains: () => new[] { "berlin", "kolin-mini" }, showNewBlankDialog: _ => { });
        launcher.OpenScenarioOn("kolin-mini");
        Assert.NotNull(request);
        Assert.Equal("terrain:kolin-mini", request!.InitialSelectionId);
        Assert.Contains(request.ItemsProvider(), e => e.Id == request.InitialSelectionId);   // a row the picker lists

        launcher.Open(AssetKind.Scenario);
        Assert.NotEqual("terrain:kolin-mini", request!.InitialSelectionId);                   // the plain New Scenario… is unchanged
    }
}
