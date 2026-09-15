using System.Collections.Generic;
using System.Text.Json;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics;
using Fdp.Toolkit.NetworkSpawning.Events;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ADA-BATCH-02 corrective C1 — EventSerializationHelper verification.
///
/// Proves the inspector-grade DTO path produces readable JSON for the three
/// failure-prone shapes called out in ADA-P0-T03:
///   - a boxed component in <see cref="SpawnEntityCommand.InitialComponents"/>
///     (boxed <c>List&lt;object&gt;</c>), with a readable name;
///   - a struct field backed by a fixed buffer (FixedString) → readable string,
///     not raw bytes;
///   - the no-resolver path (entity-ref resolution stays deferred per ADA-01-D01).
/// </summary>
public sealed class EventSerializationHelperTests
{
    /// <summary>
    /// A SpawnEntityCommand carrying a boxed EntityInfo in InitialComponents must
    /// serialize with the EntityInfo.Name (a FixedString64) readable as a string.
    /// </summary>
    [Fact]
    public void SerializeToJson_BoxedComponentInList_NameIsReadable()
    {
        var cmd = new SpawnEntityCommand
        {
            TkbType           = 1L,
            NetworkId         = 42L,
            InitialComponents = new List<object>
            {
                new EntityInfo { Name = new FixedString64("Alpha") },
            },
        };

        var json = EventSerializationHelper.SerializeToJson(cmd);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // The boxed component list is present and the name is the readable string.
        Assert.True(root.TryGetProperty("InitialComponents", out var comps));
        Assert.Equal(JsonValueKind.Array, comps.ValueKind);

        var entityInfo = comps[0];
        Assert.True(entityInfo.TryGetProperty("Name", out var name));
        Assert.Equal(JsonValueKind.String, name.ValueKind);
        Assert.Equal("Alpha", name.GetString());

        // And the readable name appears verbatim in the JSON text (not as a byte array).
        Assert.Contains("Alpha", json);
    }

    /// <summary>
    /// A struct event whose field is a FixedString (fixed byte buffer) must serialize
    /// to a readable DTO string, not a raw list of 64 byte values.
    /// </summary>
    [Fact]
    public void SerializeToJson_FixedBufferField_ReadableNotRawBytes()
    {
        var info = new EntityInfo { Name = new FixedString64("Bravo") };

        var json = EventSerializationHelper.SerializeToJson(info);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("Name", out var name));
        Assert.Equal(JsonValueKind.String, name.ValueKind);
        Assert.Equal("Bravo", name.GetString());

        // Defensive: the raw-byte rendering would have produced a JSON array under Name.
        Assert.NotEqual(JsonValueKind.Array, name.ValueKind);
    }

    /// <summary>
    /// The no-resolver path is callable off-thread and never throws (entity-ref
    /// resolution to networkId remains deferred per ADA-01-D01).
    /// </summary>
    [Fact]
    public void SerializeToJson_NoResolver_DoesNotThrow_AndProducesJson()
    {
        var cmd = new SpawnEntityCommand { TkbType = 7L, NetworkId = 99L };

        // resolver omitted (no-resolver path)
        var json = EventSerializationHelper.SerializeToJson(cmd);

        Assert.False(string.IsNullOrWhiteSpace(json));
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("TkbType", out var tkb));
        Assert.Equal(7L, tkb.GetInt64());
        Assert.True(doc.RootElement.TryGetProperty("NetworkId", out var nid));
        Assert.Equal(99L, nid.GetInt64());
    }
}
