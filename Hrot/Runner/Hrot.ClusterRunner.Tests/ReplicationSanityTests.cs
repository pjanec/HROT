using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// ⭐ RT-6 (R-4279, docs/DESIGN_Scenario_Regression.md §11.4) — the replication sanity judge, on <c>/diagnostics/architecture</c>'s own shape.
/// </summary>
public sealed class ReplicationSanityTests
{
    private static JsonObject Translator(string topic, string direction, int sent, int received) => new()
    {
        ["systemName"] = topic + direction, ["direction"] = direction, ["topic"] = topic, ["sentSamples"] = sent, ["receivedSamples"] = received,
    };

    private static JsonObject Arch(params (string Sub, JsonObject[] Translators, int Errors)[] subs) => new()
    {
        ["subsystems"] = new JsonArray(subs.Select(s => (JsonNode?)new JsonObject
        {
            ["subsystem"] = s.Sub,
            ["translators"] = new JsonArray(s.Translators.Select(t => (JsonNode?)t).ToArray()),
            ["systems"] = new JsonArray(new JsonObject { ["systemName"] = "Sys", ["errors"] = s.Errors }),
            ["modules"] = new JsonArray(new JsonObject { ["name"] = "Mod", ["failureCount"] = 0 }),
        }).ToArray()),
    };

    /// <summary>
    /// ⭐ A topic sent and received is sane; a topic SENT that its cluster ingress never RECEIVED fails, named (the mutation the handoff asks for:
    /// an ingress dropped); a topic nobody in the cluster subscribes to is a NOTE, never a failure; a swallowed exception fails.
    /// 🔴 Red-proof: drop the received == 0 test ⇒ the dropped ingress passes.
    /// </summary>
    [Fact]
    public void RT6_ASentTopicNoIngressReceived_Fails_AnUnsubscribedOneIsANote_ASwallowedExceptionFails()
    {
        var sane = ReplicationSanity.Judge(Arch(
            ("CGF", new[] { Translator("dtAnimalState", "Egress", 12, 0) }, 0),
            ("SimHost", new[] { Translator("dtAnimalState", "Ingress", 0, 12), Translator("dtRecorderOnly", "Egress", 5, 0) }, 0)));
        Assert.Empty(sane.Problems);
        Assert.Contains(sane.Notes, n => n.StartsWith("dtRecorderOnly", System.StringComparison.Ordinal));

        var dropped = ReplicationSanity.Judge(Arch(
            ("CGF", new[] { Translator("dtAnimalState", "Egress", 12, 0) }, 0),
            ("SimHost", new[] { Translator("dtAnimalState", "Ingress", 0, 0) }, 0)));
        Assert.Contains(dropped.Problems, p => p.Contains("dtAnimalState") && p.Contains("none received"));

        var threw = ReplicationSanity.Judge(Arch(("CGF", System.Array.Empty<JsonObject>(), 3)));
        Assert.Contains(threw.Problems, p => p.Contains("threw 3"));

        Assert.NotEmpty(ReplicationSanity.Judge(null).Problems);   // no architecture at all is never "sane"
    }
}
