using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <c>CE-162</c> — <b>a subsystem that HOLDS a capability may not hand the debug provider
    /// <c>null</c> for it.</b>
    ///
    /// <para>📄 <c>docs/DESIGN_Mcp_Diagnostics_Federation.md</c> §1 *(the capability matrix is MEASURED
    /// from wired members — <c>R-133</c>)* · <c>docs/DESIGN_Entity_Creation_Unification.md</c> §2.3c.</para>
    ///
    /// <para>🔴 <b>The defect, measured on a live cluster <c>2026-09-03</c>.</b> <c>IgSubsystem</c> passed
    /// <c>entityMap: null</c> while <c>IgApplication</c> assigns <c>_entityMap = _context.EntityMap</c> and
    /// exposes it as <c>TestHook_EntityMap</c> — the same member name and shape <c>SimHostSubsystem</c>
    /// passes. Because <c>SubsystemDebugProvider</c> computes each capability cell from the member being
    /// non-null, <c>GET /capabilities</c> reported <c>world.entityMap:false</c> for IG and
    /// <c>GET /entities</c> answered <c>NOT_SUPPORTED_HERE</c> there — so the IG side of any cross-node
    /// entity comparison was unreadable.</para>
    ///
    /// <para>⛔⛔ <b>Why a rail and not just the fix.</b> This is the <b>11th</b> instance of the pattern
    /// <c>CLAUDE.md</c> names — <i>"a production caller that HAS a dependency must PASS it"</i> — and the
    /// distinguishing feature of that family is that the <c>null</c> reads as a deliberate,
    /// documented absence. 📌 Here it literally was: the argument sat under a paragraph explaining that IG
    /// <i>"can neither drive time nor map network ids"</i>, of which only the TIME half was ever true. ⇒ ⭐
    /// prose cannot be the control; the rail compares what the app HAS against what the provider PASSES.</para>
    ///
    /// <para>⚠ <b>Deliberately narrow, and honest about it.</b> This gates ONE capability — the entity map —
    /// across every subsystem that has one. ⛔ It is not a generic "no argument may be null" sweep: those
    /// flag dozens of correctly-defaulted arguments and get switched off within a batch
    /// (<c>CLAUDE.md</c>, the silent-default section, records exactly that being tried and thrown away).
    /// ⭐ A genuine absence stays expressible — <c>ExCon</c> has no world at all and is not in the list.</para>
    /// </summary>
    public class TheDebugProvidersDoNotUnderReportTests
    {
        /// <summary>
        /// ⭐ Subsystem composition file → the app file whose member it must forward. Both are read from
        /// source: the provider is built lazily from closures, so nothing about this is observable by
        /// constructing one — the claim is about what the composition PASSES.
        /// </summary>
        public static TheoryData<string, string> SubsystemsWithAnEntityMap() => new()
        {
            { "Hrot/Subsystems/Hrot.IG/IgSubsystem.cs",           "Hrot/Subsystems/Hrot.IG/IgApplication.cs" },
            { "Hrot/Subsystems/Hrot.SimHost/SimHostSubsystem.cs", "Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs" },
        };

        [Theory]
        [MemberData(nameof(SubsystemsWithAnEntityMap))]
        public void ASubsystemHoldingAnEntityMap_PassesItToItsDebugProvider(
            string subsystemPath, string appPath)
        {
            var app = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(appPath));

            // ⛔ Anti-vacuity: if the app stops holding a map, this row is asserting nothing and must be
            //   re-examined rather than silently passing.
            Assert.True(app.Contains("NetworkEntityMap"),
                $"{appPath} no longer mentions NetworkEntityMap, so this row cannot assert anything. " +
                "Either the app genuinely lost its map (drop the row and say why) or the rail is aimed " +
                "at the wrong file.");

            var subsystem = CompositionRootSource.StripComments(
                CompositionRootSource.ReadRepoSource(subsystemPath));

            Assert.Contains("entityMap:", subsystem);

            var afterKey = subsystem[(subsystem.IndexOf("entityMap:", System.StringComparison.Ordinal)
                                      + "entityMap:".Length)..].TrimStart();

            Assert.False(afterKey.StartsWith("null", System.StringComparison.Ordinal),
                $"{subsystemPath} passes `entityMap: null` while {appPath} holds a NetworkEntityMap. " +
                "SubsystemDebugProvider computes the capability cell from the member being non-null, so " +
                "GET /capabilities will report world.entityMap:false and GET /entities will answer " +
                "NOT_SUPPORTED_HERE for this perspective — on its own port too, because the cell comes " +
                "from the provider and not from the hosting topology. Forward the map (CE-162).");
        }
    }
}
