using Fdp.Core.Logging;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using NLog;
using NLog.Config;

namespace Hrot.Blueprints.Tests.Integration;

/// <summary>
/// BP-124 -- proves a <c>Print String</c> message actually reaches the sink, not merely that the
/// generated C# compiles.
///
/// <para>
/// BP-108 shipped 19 tests proving the generated code compiles and that the level probe /
/// <c>stackalloc</c>/<c>TryWrite</c> shapes appear in the emitted text (see
/// <c>BP108_PrintAndFormatStringTests</c>). None of them reference <see cref="AiBehaviorLogTarget"/>,
/// so nothing proved a message ever arrives at the sink -- trap #9's shape: two halves of a
/// contract, each tested alone, seam never crossed.
/// </para>
///
/// <para>
/// This test registers the NLog rule that <c>Hrot.ClusterRunner/Program.cs</c> normally sets up at
/// startup (and which never runs headless), compiles a blueprint containing a
/// <see cref="PrintStringNode"/> through real Roslyn, ticks it, and asserts the FORMATTED message --
/// with the argument value substituted, not the raw <c>{Threat}</c> placeholder -- lands in
/// <see cref="AiBehaviorLogTarget.SharedInstance"/>.
/// </para>
///
/// <para>
/// ⚠ <see cref="AiBehaviorLogTarget.SharedInstance"/> and <see cref="LogManager.Configuration"/> are
/// process-wide singletons -- this test saves/restores the configuration and clears the target in a
/// <c>finally</c> block so it does not leak into any other test in the suite.
/// </para>
/// </summary>
[Collection("DebugProbe")]
public sealed class BP124_PrintStringReachesTheLogTests
{
    private static Pin ExecPin(string name, string direction) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = true, TypeRef = new() };

    private static Pin DataPin(string name, string direction, string typeId) =>
        new() { Id = Guid.NewGuid(), Name = name, Direction = direction, IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = typeId } };

    /// <summary>EventEntry -&gt; PrintString(Format="threat={Threat}", literal 7) -&gt; Return.</summary>
    private static BlueprintAsset BuildPrintStringAsset()
    {
        var litOut = DataPin("Value", "Out", "System.Int32");
        var lit = new LiteralNode { Id = Guid.NewGuid(), TypeId = "System.Int32", ValueJson = "7" };
        lit.Pins.Add(litOut);

        var printIn     = ExecPin("In",  "In");
        var printOut    = ExecPin("Out", "Out");
        var printThreat = DataPin("Threat", "In", "System.Int32");
        var print = new PrintStringNode
        {
            Id     = Guid.NewGuid(),
            Format = "threat={Threat}",
            Level  = BlueprintLogLevel.Info,
        };
        print.Pins.AddRange(new[] { printIn, printOut, printThreat });

        var entry    = new EventEntryNode { Id = Guid.NewGuid() };
        var entryOut = ExecPin("ExecOut", "Out");
        entry.Pins.Add(entryOut);

        var ret   = new ReturnNode { Id = Guid.NewGuid() };
        var retIn = ExecPin("ExecIn", "In");
        ret.Pins.Add(retIn);

        var graph = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = "Main",
            Kind  = GraphKind.Function,
            Nodes = { entry, lit, print, ret },
            Links =
            {
                new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = print.Id, ToPinId = printIn.Id },
                new Link { FromNodeId = print.Id, FromPinId = printOut.Id, ToNodeId = ret.Id,   ToPinId = retIn.Id },
                new Link { FromNodeId = lit.Id,   FromPinId = litOut.Id,   ToNodeId = print.Id, ToPinId = printThreat.Id },
            },
        };

        return new BlueprintAsset
        {
            AssetId  = Guid.NewGuid(),
            Name     = "PrintStringReachesLog",
            Dispatch = BlueprintDispatchKind.Instance,
            Graphs   = { graph },
        };
    }

    [Fact]
    public void PrintString_TickedThroughRealRoslyn_FormattedMessageReachesAiBehaviorLogTarget()
    {
        // ── Register the NLog rule the way Hrot.ClusterRunner/Program.cs does at startup ──
        var previousConfig = LogManager.Configuration;
        AiBehaviorLogTarget.SharedInstance.Clear();
        try
        {
            var logConfig = new LoggingConfiguration();
            logConfig.AddRule(LogLevel.Trace, LogLevel.Fatal, AiBehaviorLogTarget.SharedInstance, "AI.Behavior*");
            LogManager.Configuration = logConfig;

            using var fixture = new BlueprintTestFixture(
                new BlueprintTestFixtureOptions { VerifyAlcUnloadOnDispose = false });

            var asset = BuildPrintStringAsset();
            fixture.CompileAndLoad(asset);

            var entity = fixture.CreateEntity();
            fixture.AttachBlueprint(asset, entity);

            // Tick so the generated code actually executes and calls BlueprintLog.Info(...).
            fixture.TickFrame(0.016f);

            var messages = AiBehaviorLogTarget.SharedInstance.GetMessages();

            // ⭐ The point: the substituted value must be present, not the raw placeholder.
            // A message containing the literal "{Threat}" would mean interpolation never happened.
            Assert.Contains(messages, m => m.Message.Contains("threat=7"));
            Assert.DoesNotContain(messages, m => m.Message.Contains("{Threat}"));
        }
        finally
        {
            AiBehaviorLogTarget.SharedInstance.Clear();
            LogManager.Configuration = previousConfig;
        }
    }
}
