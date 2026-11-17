using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Motion;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Hrot.Editor.AiShared.Windows;
using Hrot.UI.Common.AddEntity;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐ <c>CE-4647</c> (AC-15 step 6; air-combat-arena.svg, "Details - Air Arena") — the Air Arena Details tab: offered for exactly one arena, titled <i>Air Arena</i>, requiring the air lane's <see cref="AirArena"/>, ranked beside the minefield's (39 → 40);
/// registered by the editor next to it (a source check: the descriptor is added in <c>EditorSubsystem</c>). The writes are <see cref="AirArenaOps"/> (rails in <c>AirArenaGizmoRail</c>). ⚠ The form's pixel layout is NOT measured by a frame rail (the Minefield form's was): a follow-up.
/// </summary>
public sealed class AirArenaDetailsViewTests
{
    private static DetailsContext Context(Entity e) => new(SelectionOrigin.Unknown, Array.Empty<IAssetSubSelection>(), new[] { e }, null, "Scenario", VariableRunState.Planning);

    [Fact]
    public void TheTab_IsOfferedForOneArena_TitledAirArena_RequiringItsComponent_NextToTheMinefields()
    {
        var w = new EntityRepository();
        w.RegisterComponent<AirArena>();
        var e = w.CreateEntity(); w.AddComponent(e, AirArenaOps.Default());
        var d = AirArenaDetailsViewDescriptor.For(() => w);
        Assert.Equal("Air Arena", d.Title);
        Assert.Contains(typeof(AirArena), d.Requires);
        Assert.True(d.AppliesTo(Context(e)));
        Assert.Equal(MinefieldDetailsViewDescriptor.Rank + 1, AirArenaDetailsViewDescriptor.Rank);
        string src = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Subsystems", "Hrot.Editor", "EditorSubsystem.cs"));
        Assert.Contains("AirArenaDetailsViewDescriptor.For(", src);
    }
}
