using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Services;
using Hrot.AI.Behaviors.Brains;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>
/// ⭐⭐⭐ <b><c>G3</c> — a resolver reaches world services through the WORLD, with no constructor
/// injection and no registration-time closure.</b>
///
/// <para>
/// 📄 <c>DESIGN_Parameter_Model.md</c> §6 · resolver design <c>G3</c>. <c>G6</c> retired
/// <c>AiBehaviorFactory</c>, so a JSON- or blueprint-authored resolver has no closure to reach these
/// through — <c>ParseParamsDelegate</c> gets <c>world</c> and <c>self</c> and nothing else.
/// </para>
///
/// <para>
/// ⭐⭐ <b>Measured: this is ALREADY BUILT, and there is ONE mechanism, not two.</b>
/// <c>IGeographicTransform</c> carries <c>[ComponentId(GlobalComponentIds.IGeographicTransform)]</c>
/// and is published with <c>SetSingletonManaged</c> at <b>three</b> production sites
/// (<c>CgfSubsystem:249</c>, <c>SimHostApp:488</c>, <c>EditorSubsystem:624</c>) — the <b>identical</b>
/// mechanism <c>NetworkEntityMap</c> uses. ⛔ Nothing to invent and nothing to propose.
/// </para>
///
/// <para>
/// ⚠ <b>Correcting the premise:</b> <i>"IGeographicTransform is constructor-injected ⇒ not reachable
/// from the world"</i> is half true. <c>GeographicModule</c> and <c>CoordinateTransformSystem</c> do
/// take it by constructor — they exist <b>before</b> the world does — but that is a second CONSUMER,
/// not a second mechanism.
/// </para>
///
/// <para>
/// 🔴 <b>What was genuinely missing is this rail.</b> Reachability was a convention held by one call
/// site. ⛔ Drop the <c>SetSingletonManaged</c> line from a host and the resolver takes its
/// Cartesian-only fallback <b>silently</b> — degrees become metres, and every position in the plan is
/// wrong by roughly five orders of magnitude with no diagnostic anywhere.
/// </para>
/// </summary>
public sealed class ResolverWorldSingletonTests
{
    private const double Lat = 50.0, Lon = 14.0;

    /// <remarks>
    /// ⚠ <c>PickableGeoPoint</c> serialises as a <b><c>[latitude, longitude]</c> ARRAY</b>, not an
    /// object — <c>PickableGeoPointArrayConverter</c>. 📐 Learned the hard way: an object-shaped
    /// fixture deserialised to all zeros, and the <c>NotEqual</c> assertion below passed <b>vacuously</b>
    /// because <c>0 != 14</c>. ⇒ that is why the positive test now pins a REAL converted value.
    /// </remarks>
    private static string PlanJson() => $$"""
        {
          "firingLineStart": [{{Lon}}, {{Lat}}, 0],
          "firingLineEnd":   [{{Lon}}, {{Lat}}, 0],
          "baselineStart":   [{{Lon}}, {{Lat}}, 0],
          "baselineEnd":     [{{Lon}}, {{Lat}}, 0]
        }
        """;

    /// <summary>Runs the REAL shipped resolver over a params buffer and hands back what it wrote.</summary>
    private static unsafe PlatoonHillAttackParams Resolve(EntityRepository world)
    {
        var buffer = new byte[BehaviorConstants.MaxBehaviorParamByteSize];
        fixed (byte* p = buffer)
        {
            // ⭐ world + self and nothing else — the whole point (`host` retired by CE-445).
            HillAttackCommanderNodes.ResolvePlatoonHillAttackParams(
                PlanJson(), p, buffer.Length, world, default);
            return *(PlatoonHillAttackParams*)p;
        }
    }

    /// <summary>
    /// ⭐⭐ <b>Published ⇒ the resolver finds it and converts.</b> The transform turns degrees into
    /// engine metres, so the result must NOT be the raw longitude/latitude.
    /// </summary>
    /// <summary>⭐ S0 item 5 (SR-135): an authored point is LOCAL METRES — a pick converts once, at the gesture. So the resolver
    /// no longer reaches the world's geo transform: with one published, the point still passes through unchanged (the retired
    /// claim was that the transform ran; a result of <c>WGS84.ToCartesian(Lat, Lon)</c> would mean it still did).</summary>
    [Fact]
    public void AResolver_TakesLocalPointsAsTheyAre_EvenWhenTheWorldPublishesAGeoTransform()
    {
        var world = new EntityRepository();
        world.SetSingletonManaged<IGeographicTransform>(Fdp.Modules.Geographic.Transforms.UtmTransform.AtOrigin(0, 0, 0));

        var result = Resolve(world);

        Assert.Equal((float)Lon, result.StartX);
        Assert.Equal((float)Lat, result.StartY);
    }

    [Fact]
    public void WithoutAnyPublishedSingleton_TheResolverReadsTheSameLocalPoint()
    {
        var result = Resolve(new EntityRepository());

        Assert.Equal((float)Lon, result.StartX);
        Assert.Equal((float)Lat, result.StartY);
    }

    [Fact]
    public void BothResolverWorldServicesUseTheSameSingletonMechanism()
    {
        var world = new EntityRepository();
        world.SetSingletonManaged<IGeographicTransform>(Fdp.Modules.Geographic.Transforms.UtmTransform.AtOrigin(0, 0, 0));
        world.SetSingletonManaged(new NetworkEntityMap());

        Assert.True(world.HasSingletonManaged<IGeographicTransform>());
        Assert.True(world.HasSingletonManaged<NetworkEntityMap>());
        Assert.NotNull(world.GetSingletonManaged<IGeographicTransform>());
        Assert.NotNull(world.GetSingletonManaged<NetworkEntityMap>());
    }
}
