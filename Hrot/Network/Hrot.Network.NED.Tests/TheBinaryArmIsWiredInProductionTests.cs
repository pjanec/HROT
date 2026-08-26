using System.Linq;
using System.Reflection;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Replication.Patching;
using Fdp.Toolkit.Replication.Services;
using Hrot.Core.Network;
using Hrot.Common;
using Hrot.Network.NED.Factory;
using Xunit;

namespace Hrot.Network.NED.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-012</c> — the request system's BINARY ARM is wired in the PRODUCTION composition.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §13 · tracker <c>AX-012</c>.</para>
///
/// <para>🔴 <b>THE DEFECT, measured `2026-08-26`.</b> <c>UpdateEntityAttributeRequestSystem</c>'s DDS
/// constructor forwarded to its interface constructor and stopped at <c>localNodeId</c>, so
/// <c>binaryInterpreter</c> took its <see langword="null"/> default. ⇒ <c>hasBinaryRecords</c> was
/// permanently false and <b>every <c>AttributeRecords</c> payload reaching the owner was silently
/// ignored</b> — only the JSON arm ever ran. The Axis-B cluster round trip reached SimHost and died there
/// with nothing logged.</para>
///
/// <para>📌 <b>THE SILENT-DEFAULT PATTERN, verbatim</b> *(<c>.claude/CLAUDE.md</c>)*: *"a production caller
/// that HAS a dependency must PASS it."* <c>NedNetworkFactory.CreateSimHostAttributeUpdateSystems</c> held
/// the geographic transform and built the JSON compiler from it **one line above** the constructor call.</para>
///
/// <para>⭐⭐⭐ <b>Asserted on the CONSTRUCTED OBJECT, which is the whole point.</b> ⛔ A rail that read the
/// registrar's source, or that constructed the system itself with the argument supplied, would pass while
/// production stayed broken — that is exactly how this survived. ⭐ These rails ask the object that
/// production actually builds whether it carries the dependency.</para>
///
/// <para>⚠ <b>Why not a generic "no optional parameter is ever defaulted" sweep:</b> `CLAUDE.md` records
/// that one was tried and thrown away within a batch — it flags dozens of correctly-defaulted parameters.
/// ⭐ The checkable rule is narrower and it is what these rails encode: <b>a forwarding rail per
/// dependency</b>.</para>
/// </summary>
public class TheBinaryArmIsWiredInProductionTests
{
    /// <summary>⚠ Domain kept low and distinct — CycloneDDS ports are `7400 + 250 × domainId`, ceiling ≈ 232.</summary>
    private const int TestDomain = 171;

    /// <summary>
    /// ⭐⭐⭐ <b>THE RAIL: the system built by the PRODUCTION FACTORY carries a binary interpreter.</b>
    /// </summary>
    [Fact]
    public void TheProductionFactoryBuildsARequestSystemWithABinaryInterpreter()
    {
        using var participant = new DdsParticipant(TestDomain);

        var geo = new WGS84Transform();
        geo.SetOrigin(52.52, 13.405, 0.0);

        var factory = new NedNetworkFactory(
            participant:  participant,
            entityMap:    new NetworkEntityMap(),
            geoTransform: geo,
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.MuscleGround);

        var systems = factory.CreateSimHostAttributeUpdateSystems();

        var requestSystem = systems.FirstOrDefault(s =>
            s.GetType().Name == "UpdateEntityAttributeRequestSystem");

        Assert.NotNull(requestSystem);

        var interpreter = requestSystem!.GetType()
            .GetField("_binaryInterpreter", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(requestSystem);

        Assert.NotNull(interpreter);
        Assert.IsType<BinaryInterpreter<EntityAttributeChange>>(interpreter);
    }

    /// <summary>
    /// ⭐⭐ <b>And the JSON arm is still wired</b> — ⛔ so the fix cannot be read as *"the binary arm replaced
    /// the JSON one"*. Both must be present: `ExConOrbatAdapter` publishes JSON-only commands to this day.
    /// </summary>
    [Fact]
    public void TheJsonArmIsStillWiredAlongsideIt()
    {
        using var participant = new DdsParticipant(TestDomain + 1);

        var geo = new WGS84Transform();
        geo.SetOrigin(52.52, 13.405, 0.0);

        var factory = new NedNetworkFactory(
            participant:  participant,
            entityMap:    new NetworkEntityMap(),
            geoTransform: geo,
            eventBus:     new FdpEventBus(),
            localNodeId:  0,
            role:         NodeRole.MuscleGround);

        var requestSystem = factory.CreateSimHostAttributeUpdateSystems()
            .First(s => s.GetType().Name == "UpdateEntityAttributeRequestSystem");

        var json = requestSystem.GetType()
            .GetField("_jsonCompiler", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(requestSystem);

        Assert.NotNull(json);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>AX-014</c> — BOTH arms are defaulted, and neither can be disabled by omission.</b>
    ///
    /// <para>🔴 <c>AX-012</c>'s fix left an INCONSISTENCY: the binary interpreter was built by the
    /// constructor while the JSON compiler was still passed in by the factory — two sibling dependencies of
    /// one system, from the same factory class and the same <c>geoTransform</c>, obtained two different
    /// ways. ⛔ That ambiguity is what let one of them be forgotten in the first place.</para>
    ///
    /// <para>⭐⭐ This rail constructs the system with the MINIMUM a host would naturally have — participant,
    /// entity map, transform — and asserts <b>both</b> arms are live. ⇒ a future edit that re-introduces the
    /// asymmetry, in either direction, reddens here.</para>
    /// </summary>
    [Fact]
    public void BothArmsAreDefaultedFromTheSameInput()
    {
        using var participant = new DdsParticipant(TestDomain + 3);

        var geo = new WGS84Transform();
        geo.SetOrigin(52.52, 13.405, 0.0);

        var system = new Hrot.Map.Common.Systems.UpdateEntityAttributeRequestSystem(
            participant, new NetworkEntityMap(), geo);

        Assert.NotNull(Field(system, "_binaryInterpreter"));
        Assert.NotNull(Field(system, "_jsonCompiler"));
    }

    /// <summary>
    /// ⭐⭐ <b>…and either may still be OVERRIDDEN</b> — ⛔ the defaults must not have taken the seam away.
    /// ⚠ Not decoration: <c>SimHostAppTests</c> passes its own JSON compiler.
    /// </summary>
    [Fact]
    public void EitherArmCanStillBeOverridden()
    {
        using var participant = new DdsParticipant(TestDomain + 4);

        var geo = new WGS84Transform();
        geo.SetOrigin(52.52, 13.405, 0.0);

        var ownJson   = Hrot.SimHost.AttributeCompilerFactory.Build(geo);
        var ownBinary = Hrot.SimHost.AttributeCompilerFactory.BuildBinaryInterpreter(geo);

        var system = new Hrot.Map.Common.Systems.UpdateEntityAttributeRequestSystem(
            participant, new NetworkEntityMap(), geo, ownJson, default, ownBinary);

        Assert.Same(ownJson,   Field(system, "_jsonCompiler"));
        Assert.Same(ownBinary, Field(system, "_binaryInterpreter"));
    }

    private static object? Field(object target, string name)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                 ?.GetValue(target);

    /// <summary>
    /// ⭐⭐ <b>The DDS constructor supplies the interpreter ITSELF, so no caller can omit it.</b>
    ///
    /// <para>⭐⭐⭐ This is the difference between fixing the caller and removing the failure mode. Passing the
    /// interpreter from the factory would have fixed today's caller and left the next one free to forget —
    /// the same forgettability <c>AX-001</c>/<c>UXI-30</c> moved into the registration. ⛔ Here the parameter
    /// is not exposed on the production constructor at all, so *"forgot to pass it"* is unrepresentable.</para>
    ///
    /// <para>⚠ Constructed directly rather than through the factory on purpose: the claim is about the
    /// CONSTRUCTOR's own behaviour, independent of who calls it.</para>
    /// </summary>
    [Fact]
    public void TheDdsConstructorNeedsNoHelpToGetTheInterpreter()
    {
        using var participant = new DdsParticipant(TestDomain + 2);

        var geo = new WGS84Transform();
        geo.SetOrigin(52.52, 13.405, 0.0);

        // ⭐ Only the arguments a host would naturally have — and deliberately NO interpreter.
        var system = new Hrot.Map.Common.Systems.UpdateEntityAttributeRequestSystem(
            participant,
            new NetworkEntityMap(),
            geo);

        var interpreter = system.GetType()
            .GetField("_binaryInterpreter", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(system);

        Assert.NotNull(interpreter);
    }
}
