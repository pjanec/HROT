using System.Collections.Generic;
using Hrot.AiEditor.Persistence.Emit;
using Hrot.AiEditor.Persistence.Hsm;
using Xunit;

namespace Hrot.AiEditor.Persistence.Tests.Emit;

/// <summary>
/// ⭐ <c>CE-3040</c> — the ONE assignment of an HSM asset's event ids (emitter and editor share it): an event named after an
/// engine-raised event gets its reserved id, so an author writes <i>On Sensor.FirstThreat</i> and never types an id.
/// 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.3b.
/// </summary>
public sealed class HsmEventIdsTests
{
    [Fact]
    public void CE3040_ABuiltInName_GetsItsReservedId_OthersKeepTheirRule()
    {
        var ids = HsmEventIds.Assign(new List<(string, ushort)>
        {
            ("Go", 0), ("Sensor.FirstThreat", 0), ("Stored", 7), ("Sensor.Hit", 3), ("Next", 0),
        });
        Assert.Equal(1, ids["Go"]);                        // sequential from 1
        Assert.Equal(0xFF04, ids["Sensor.FirstThreat"]);   // reserved, never consumes a sequential id
        Assert.Equal(7, ids["Stored"]);                    // a stored id is kept
        Assert.Equal(0xFF06, ids["Sensor.Hit"]);           // reserved wins over a stored id
        Assert.Equal(2, ids["Next"]);
    }

    [Fact]
    public void CE3040_TheEmitter_WritesTheReservedId()
    {
        var dto = new HsmAssetDto { Name = "SensorDemo" };
        dto.Events.Add(new EventDefinitionDto { Name = "Sensor.AllClear" });
        string code = HsmEmitCore.Emit(dto);
        Assert.Contains("builder.Event(\"Sensor.AllClear\", 65285,", code);
    }
}
