#nullable enable
using System.Text.Json;
using Fdp.Core;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// 🔴🔴 <c>CE-225</c> — the spawn transform must actually BIND, and a payload that does not bind must
/// RAISE rather than yield the origin.
/// </summary>
/// <remarks>
/// <para><b>The defect.</b> <c>SimTransform</c> declares public <b>fields</b>, and
/// <c>System.Text.Json</c> ignores fields unless <c>IncludeFields</c> is set. A bare
/// <c>Deserialize&lt;SimTransform&gt;</c> therefore bound NOTHING from ANY payload, returned a
/// default-constructed struct, and <c>POST /entities/spawn</c> created the entity at the origin while
/// answering <c>ok:true</c> — the failure <c>CE-191</c>'s catch was added for, which never fired because
/// unbound members do not throw by default.</para>
///
/// <para>⭐ These rails assert the OPTIONS, not a copy of them: the field is <c>internal</c> so the test
/// exercises the exact instance the endpoint uses. Asserting a locally-built options object would pass
/// while production stayed broken — the shape of blindness this session kept finding.</para>
/// </remarks>
public sealed class SpawnTransformBindsTests
{
    /// <summary>The shape the API's own SKILL documents must bind — lower-case, nested objects.</summary>
    [Fact]
    public void TheDocumentedLowerCaseShapeBindsAPosition()
    {
        const string json = """
        {"position":{"x":480.0,"y":450.0,"z":0.5},"rotation":{"x":0,"y":0,"z":0,"w":1}}
        """;

        var t = JsonSerializer.Deserialize<SimTransform>(json, DebugApiService.SpawnTransformJsonOptions);

        Assert.Equal(480.0f, t.Position.X, 3);
        Assert.Equal(450.0f, t.Position.Y, 3);
        Assert.Equal(0.5f,   t.Position.Z, 3);
    }

    /// <summary>
    /// ⛔ The anti-vacuity half, and the one that encodes the DEFECT: a payload whose members do not map
    /// must THROW, so the endpoint's catch refuses the spawn. Without this the first test could pass
    /// while a non-binding payload still silently produced the origin.
    /// </summary>
    [Fact]
    public void APayloadThatDoesNotBindThrowsRatherThanYieldingTheOrigin()
    {
        const string json = """{"nonsense":{"a":1}}""";

        Assert.ThrowsAny<JsonException>(
            () => JsonSerializer.Deserialize<SimTransform>(json, DebugApiService.SpawnTransformJsonOptions));
    }

    /// <summary>
    /// 📐 The measurement the fix rests on, pinned so nobody "simplifies" the options away:
    /// <c>SimTransform</c> exposes its state as FIELDS, which is why <c>IncludeFields</c> is required.
    /// </summary>
    [Fact]
    public void SimTransformStillExposesItsStateAsFields_WhichIsWhyIncludeFieldsIsRequired()
    {
        var fields = typeof(SimTransform).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        Assert.NotEmpty(fields);
        Assert.True(DebugApiService.SpawnTransformJsonOptions.IncludeFields,
            "CE-225: SimTransform's state is in public fields, so IncludeFields must stay set or every "
          + "spawn silently lands at the origin.");
    }
}
