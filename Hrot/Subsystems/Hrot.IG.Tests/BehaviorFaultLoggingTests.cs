using System;
using System.Linq;
using Fdp.Toolkit.Behavior.Events;
using NLog;
using NLog.Config;
using NLog.Targets;
using Xunit;

namespace Hrot.IG.Tests;

/// <summary>
/// ⭐ CE-495 — an IG-only operator sees behaviour faults: IG has no Message Log window, so each fault in
/// <see cref="BehaviorFaultLog.Shared"/> is written to IG's normal NLog log at Error level. 🔒 User,
/// 2026-10-01: "simply write message to its message log using normal nlog log write for the time being".
/// 🔴 Red-proof: remove the subscription in <c>InitializeEmbedded</c> ⇒ the first assertion fails.
/// </summary>
public sealed class BehaviorFaultLoggingTests
{
    [Fact]
    public void CE495_A_behaviour_fault_is_written_once_to_IGs_log()
    {
        var target = new MemoryTarget("ce495") { Layout = "${level}|${logger}|${message}" };
        var previous = LogManager.Configuration;
        var config = new LoggingConfiguration();
        config.AddRule(NLog.LogLevel.Error, NLog.LogLevel.Fatal, target, typeof(IgApplication).FullName!);
        LogManager.Configuration = config;
        try
        {
            var app = new IgApplication();
            app.InitializeEmbedded(headless: true, domainIdOverride: 206, networkFactory: IgTestFactory.CreateHeadless());
            Assert.True(app.TestHook_IsLoggingBehaviorFaults);

            // A unique key per run: the shared log de-duplicates by key.
            var key = new BehaviorFaultLog.FaultKey(Random.Shared.NextInt64(1, long.MaxValue), 7u, 12345);

            // ⭐ EXACTLY one line, even with a second IG alive — the subscription is per process, not per
            //   IG (the first cut logged once per live IG; this assertion is what caught it).
            var second = new IgApplication();
            second.InitializeEmbedded(headless: true, domainIdOverride: 207, networkFactory: IgTestFactory.CreateHeadless());
            BehaviorFaultLog.Shared.Report(key, "Patrol", "entity 42 (node 2)", 1, "boom");

            var line = Assert.Single(target.Logs, l => l.Contains("'Patrol'"));
            Assert.StartsWith("Error|", line);
            Assert.Contains("entity 42 (node 2): behaviour 'Patrol' FAULTED", line);

            second.Dispose();
            app.Dispose();
            Assert.False(app.TestHook_IsLoggingBehaviorFaults);
        }
        finally
        {
            LogManager.Configuration = previous;
        }
    }
}
