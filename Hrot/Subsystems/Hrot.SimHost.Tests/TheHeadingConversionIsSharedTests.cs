using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic.Systems;
using Fdp.Toolkit.Replication.Attributes;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Patching;
using Xunit;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>Q59-C1</c> / <c>F3</c> — ONE heading→rotation formula, and every path CALLS it.</b>
///
/// <para>📄 <c>docs/blueprints/Architect_Question_59_…md</c> §5 <c>F3</c>/<c>F5</c> · §9.</para>
///
/// <para>🔴🔴 <b>THE DEFECT, measured <c>2026-08-26</c>.</b> Three callers needed compass-heading →
/// <c>SimTransform.Rotation</c>. The canonical conversion — <c>SimTransformBridgeSystem.HeadingDegToRotation</c>,
/// documented *"X=East, Y=North. 0=North, 90=East, clockwise"* — is <c>axis Z, angle (90−h)</c>.
/// ⛔ <c>DescriptorMapper</c> had its own: <c>CreateFromYawPitchRoll(−h·π/180, 0, 0)</c> = <b>yaw about Y with
/// no compass offset</b>.</para>
///
/// <para>📐 <b>They disagreed at EVERY heading, and not by a convention:</b> at <c>h=0</c> canonical points
/// <b>North</b> <c>(0,1,0)</c> and the mapper pointed <b>East</b> <c>(1,0,0)</c>; at <c>h=90</c> canonical
/// points East and the mapper pointed <b>straight UP</b> <c>(0,0,1)</c>. ⇒ ⛔ it rotated in the wrong plane.</para>
///
/// <para>🔴 <b>And it was LIVE, not theoretical.</b> <c>NedCgfEntityLifecycleAdapters</c> calls the 2-arg
/// <c>MapToComponents</c> for <c>msg.InitialDescriptors</c>, and <b>nothing overwrote the result</b> — the
/// per-tick <c>SimTransformBridgeSystem.Execute</c> *"has been removed"* per its own class doc. ⇒ every entity
/// CGF created from descriptors carried a wrong persisted initial rotation.</para>
///
/// <para>⚠ <b>Why a rail and not just a fix</b> *(📌 the lesson of <c>F5</c>)*: the JSON arm ALSO had its own
/// copy of the formula. It happened to be numerically identical, so it was not a defect — ⭐ but it is what
/// the third copy looks like just before it drifts. ⇒ this file pins the AGREEMENT, not the formula.</para>
/// </summary>
public class TheHeadingConversionIsSharedTests
{
    /// <summary>⭐ Cardinals plus two off-axis values, so an axis swap cannot hide in a symmetry.</summary>
    public static IEnumerable<object[]> Headings()
        => new[] { 0.0, 45.0, 90.0, 135.0, 180.0, 270.0, 359.0 }.Select(h => new object[] { h });

    // ══ ① the JSON attribute path agrees with the canonical conversion ════════════

    /// <summary>
    /// ⭐⭐⭐ <b>A JSON <c>{"Heading":h}</c> produces exactly <c>HeadingDegToRotation(h)</c>.</b>
    ///
    /// <para>⭐ Asserted against the BRIDGE, not against a literal quaternion — ⚠ a literal would have to be
    /// recomputed here, which is a fourth copy of the formula and defeats the point.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Headings))]
    public void TheJsonPathUsesTheSharedConversion(double headingDeg)
    {
        var (repo, e) = OwnedEntity();
        var compiler = AttributeCompilerFactory.Build(geoTransform: null);
        compiler.Compile($"{{\"Heading\":{headingDeg}}}", compiler.CreatePatchContext(repo, e));

        AssertSameRotation(
            SimTransformBridgeSystem.HeadingDegToRotation((float)headingDeg),
            repo.GetComponent<SimTransform>(e).Rotation);
    }

    // ══ ② the binary attribute path agrees ════════════════════════════════════════

    /// <summary>⭐⭐ And the binary installer, which already called the bridge — pinned so it keeps doing so.</summary>
    [Theory]
    [MemberData(nameof(Headings))]
    public void TheBinaryPathUsesTheSharedConversion(double headingDeg)
    {
        var (repo, e) = OwnedEntity();
        var interpreter = AttributeCompilerFactory.BuildBinaryInterpreter(geoTransform: null);
        var patchCtx    = EcsPatchContext.Create(repo, e);
        var ctx         = interpreter.CreateContext(patchCtx);
        ctx.Repo = repo; ctx.Entity = e;

        interpreter.Apply(ctx, new[]
        {
            new EntityAttributeChange
            {
                AttributeId = AttributeIds.Heading,
                Value       = AttributeValue.FromDouble(headingDeg),
            }
        });

        AssertSameRotation(
            SimTransformBridgeSystem.HeadingDegToRotation((float)headingDeg),
            repo.GetComponent<SimTransform>(e).Rotation);
    }

    // ══ ②b the DESCRIPTOR route — the one that was actually broken ════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL THAT CATCHES <c>F3</c>: the descriptor route agrees too.</b>
    ///
    /// <para>⭐⭐ This is the important one — ①/② cover the ATTRIBUTE paths, which were already correct.
    /// <c>DescriptorMapper</c> is the DESCRIPTOR path *(a NED concept, per <c>Q59</c> §7)*, and it is the one
    /// that had drifted. ⇒ ⛔ a rail over the attribute paths alone would have stayed green through the whole
    /// defect.</para>
    ///
    /// <para>📐 <b>Red-proved by inverse edit:</b> restoring
    /// <c>CreateFromYawPitchRoll(−h·π/180, 0, 0)</c> reddens all three cases.</para>
    ///
    /// <para>⚠ It exercises <c>ApplyGeoSpatialDescriptor</c> — the Phase-6 helper — because that is the arm
    /// whose <c>ATTR-BATCH-03</c> TODO said it *"MUST be updated"* once a <c>Heading</c> JSON delegate
    /// existed. ⭐ <c>AX-018</c> added that delegate; this asserts the obligation was discharged.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Headings))]
    public void TheDescriptorRouteUsesTheSharedConversion(double headingDeg)
    {
        var ctx = new ListPatchContext(null);
        var wp  = new Hrot.NED.Descriptors.WorldPos();
        wp.Ori.Heading = (float)headingDeg;

        Hrot.Map.Common.Replication.Utils.DescriptorMapper
            .ApplyGeoSpatialDescriptor(ctx, wp, new UnitGeoTransform());

        var st = ctx.FlushComponents().OfType<SimTransform>().Single();

        AssertSameRotation(
            SimTransformBridgeSystem.HeadingDegToRotation((float)headingDeg),
            st.Rotation);
    }

    /// <summary>⭐ Identity-ish transform: this rail is about ROTATION, not the coordinate conversion.</summary>
    private sealed class UnitGeoTransform : Fdp.Modules.Geographic.IGeographicTransform
    {
        public void SetOrigin(double lat, double lon, double alt) { }
        public Vector3 ToCartesian(double lat, double lon, double alt)
            => new((float)lon, (float)lat, (float)alt);
        public (double lat, double lon, double alt) ToGeodetic(Vector3 p) => (p.Y, p.X, p.Z);
    }

    // ══ ③ the compass semantics themselves — the claim the doc makes ══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The conversion MEANS what its doc says: <c>0 = North, 90 = East, clockwise</c>, X=East, Y=North.</b>
    ///
    /// <para>⭐⭐ ①/② assert AGREEMENT — they would all stay green if every path adopted the same WRONG
    /// formula. ⛔ This is the rail that pins the semantics, and it is the one <c>DescriptorMapper</c>'s
    /// version fails: <c>h=90</c> gave <c>(0,0,1)</c>, i.e. UP, where East is required.</para>
    /// </summary>
    [Theory]
    [InlineData(0.0,   0.0,  1.0)]   // North
    [InlineData(90.0,  1.0,  0.0)]   // East
    [InlineData(180.0, 0.0, -1.0)]   // South
    [InlineData(270.0, -1.0, 0.0)]   // West
    public void TheConversionIsCompassNotMathYaw(double headingDeg, double expectX, double expectY)
    {
        var forward = Vector3.Transform(
            Vector3.UnitX, SimTransformBridgeSystem.HeadingDegToRotation((float)headingDeg));

        Assert.Equal(expectX, forward.X, 4);
        Assert.Equal(expectY, forward.Y, 4);
        Assert.Equal(0.0, forward.Z, 4);   // ⛔ never a vertical component — F3's signature failure
    }

    // ══ ④ nobody re-derives it — the source guard ═════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>No file outside the canonical one builds a rotation from a heading itself.</b>
    ///
    /// <para>⭐ A SOURCE scan, for the same structural reason <c>StrictNetworkSeparationTests</c> needs one:
    /// a copied formula leaves <b>no signature and no call edge</b> — it compiles to arithmetic. ⇒ reflection
    /// cannot see it, and neither can a call-graph query.</para>
    ///
    /// <para>⚠ <b>What it looks for</b> is the giveaway of the compass conversion specifically —
    /// <c>90f -</c> / <c>90 -</c> next to a degrees→radians factor. ⛔ Not a general "no quaternion math"
    /// ban: plenty of code legitimately builds rotations from other sources *(velocity, gizmo drags,
    /// mission data)*.</para>
    ///
    /// <para>⚠⚠ <b>PRODUCTION ONLY, and the first cut of this rail got that wrong.</b> 📐 It flagged
    /// <c>GeoSpatialEgressTranslatorTests.HeadingRoundTrip_…</c>, which derives <c>(90 − h)·π/180</c> by hand
    /// to check <c>RotationToHeadingDeg</c>. ⭐ That is **double-entry bookkeeping, not duplication** — had it
    /// built its input by calling <c>HeadingDegToRotation</c> it would only prove the two are mutual
    /// inverses, never that either matches the compass convention. ⇒ ⭐⭐ **the rule is "no PRODUCTION file
    /// re-derives it"**; a test that independently derives an expectation is exactly what you want.</para>
    /// </summary>
    [Fact]
    public void NoOneElseDerivesARotationFromAHeading()
    {
        var offenders = new List<string>();
        int scanned = 0;

        foreach (var root in new[] { "Hrot", "FDP" })
        {
            foreach (var file in Directory.EnumerateFiles(
                         Path.Combine(RepoRoot(), root), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    continue;

                var name = Path.GetFileName(file);
                if (Allowed.Contains(name)) continue;

                // ⭐ Production only — see the remarks. A test deriving an expectation by hand is
                //   double-entry, not a duplicate implementation.
                if (name.EndsWith("Tests.cs", StringComparison.Ordinal) ||
                    file.Contains(".Tests", StringComparison.Ordinal))
                    continue;

                scanned++;
                var text = File.ReadAllText(file);

                // the compass offset next to a degrees→radians conversion, on one line
                foreach (var line in text.Split('\n'))
                {
                    if ((line.Contains("90f -", StringComparison.Ordinal) ||
                         line.Contains("90 -", StringComparison.Ordinal) ||
                         line.Contains("- 90f", StringComparison.Ordinal)) &&
                        line.Contains("PI / 180", StringComparison.Ordinal))
                    {
                        offenders.Add($"{name}: {line.Trim()}");
                    }
                }
            }
        }

        // ⚠ the rail's own red-proof: a scan that saw nothing would report green for ever
        Assert.True(scanned > 100, $"the scan only saw {scanned} files — it is not scanning the repo");
        Assert.Empty(offenders);
    }

    /// <summary>
    /// ⭐ The canonical home, plus the places whose JOB is the inverse or a different convention.
    /// ⛔ Adding a name here is a design act: it declares a second heading convention exists.
    /// </summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "SimTransformBridgeSystem.cs",              // ⭐ the ONE canonical conversion (and its inverse)
    };

    // ══ helpers ══════════════════════════════════════════════════════════════════

    private static void AssertSameRotation(Quaternion expected, Quaternion actual)
    {
        // ⭐ Compare the FORWARD VECTOR, not the raw components: q and −q are the same rotation,
        //   so a component-wise assert can red on a sign convention that means nothing.
        var fe = Vector3.Transform(Vector3.UnitX, expected);
        var fa = Vector3.Transform(Vector3.UnitX, actual);

        Assert.Equal(fe.X, fa.X, 4);
        Assert.Equal(fe.Y, fa.Y, 4);
        Assert.Equal(fe.Z, fa.Z, 4);
    }

    private static (EntityRepository, Entity) OwnedEntity()
    {
        var repo = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(repo);
        repo.RegisterComponent<EgressPublicationState>();

        var e = repo.CreateEntity();
        repo.AddComponent(e, default(SimTransform));
        repo.SetAuthority<SimTransform>(e, true);
        return (repo, e);
    }

    private static string RepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var probe = start;
            while (!string.IsNullOrEmpty(probe))
            {
                if (Directory.Exists(Path.Combine(probe, "Hrot")) &&
                    Directory.Exists(Path.Combine(probe, "FDP")))
                    return probe;
                probe = Path.GetDirectoryName(probe);
            }
        }
        Assert.Fail("Could not locate the repo root; this rail scans source.");
        return string.Empty;
    }
}
