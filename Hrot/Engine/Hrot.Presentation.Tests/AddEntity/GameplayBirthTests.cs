using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Hrot.Common;
using Hrot.IG.Components;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.ScenarioEditor.Tools;
using Hrot.UI.Common.AddEntity;
using Xunit;

namespace Hrot.Presentation.Tests.AddEntity;

/// <summary>
/// ⭐ Player Join S2 — what the editor BORN-with hooks give the two authored gameplay entities: a drawn Spawn Area (8814) gets <c>SpawnArea</c> (beside its outline and style), a placed Deploy Role (8815) gets
/// <c>DeployRole</c> — each once, and no other type gets either. ✅ Red-proofs: the SpawnArea line removed from <c>AreaAuthoringArm.BornWith</c> ⇒ the area case red; <c>GameplayBirth</c> not adding ⇒ the role case red.
/// </summary>
public sealed class GameplayBirthTests
{
    [Fact]
    public void ADrawnSpawnArea_IsBornWithItsSpawnArea_AndOtherAreasAreNot()
    {
        var poly = new EditablePolyline { Points = new List<Vector2> { new(0, 0), new(1, 0), new(0, 1) } };
        var style = new MapOverlayStyle();
        var born = AreaAuthoringArm.BornWith(TkbEntityTypes.SpawnArea, poly, style);
        Assert.Contains(born, c => c is SpawnArea { Side: ForceId.Friend });
        Assert.DoesNotContain(AreaAuthoringArm.BornWith(TkbEntityTypes.StoneSource, poly, style), c => c is SpawnArea);
    }

    [Fact]
    public void APlacedDeployRole_IsBornWithItsDeployRole_OnceAndOnlyForThatType()
    {
        var comps = new List<object>();
        GameplayBirth.BornWith(TkbEntityTypes.DeployRole, comps);
        GameplayBirth.BornWith(TkbEntityTypes.DeployRole, comps);
        Assert.Single(comps, c => c is DeployRole);

        var other = new List<object>();
        GameplayBirth.BornWith(TkbEntityTypes.Infantry_Rifleman, other);
        Assert.Empty(other);
    }
}
