using Hrot.Blueprints.Core.Assets;
using Hrot.Editor.AiShared;  // WHEN-M11-T5: Use canonical ReactiveGuardVocabulary

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Palette NodeKindDescriptor factories for WhenNode, ReadEqsResultNode,
/// SpawnEqsSensorNode.  Call NodeKindRegistry.Register(...) at editor startup.
/// </summary>
public static class WhenNodePaletteEntries
{
    public static NodeKindDescriptor WhenNode() => new()
    {
        Kind        = "When",
        DisplayName = "When",
        Category    = ReactiveGuardVocabulary.CategoryName,
        Tooltip     = ReactiveGuardVocabulary.BlueprintWhenNodeTooltip,
        Icon        = "icons/when.svg",
        CreateInstance = () => new Core.Assets.WhenNode
        {
            Id    = Guid.NewGuid(),
            Mode  = WhenMode.ValueChanged,
            Edges = WhenEdge.RisingEdge,
            Pins  =
            [
                new Pin { Id = Guid.NewGuid(), Name = "In",      Direction = "In",  IsExec = true  },
                new Pin { Id = Guid.NewGuid(), Name = "Out",     Direction = "Out", IsExec = true  },
                new Pin { Id = Guid.NewGuid(), Name = "OnFired", Direction = "Out", IsExec = true  },
            ],
        },
    };

    public static NodeKindDescriptor ReadEqsResult() => new()
    {
        Kind        = "ReadEqsResult",
        DisplayName = "Read EQS Result",
        Category    = "EQS",
        Tooltip     = "Read a ranked result from an EQS sensor's cognitive buffer. " +
                      "Pass an index to read top, second-best, etc.",
        Icon        = "icons/eqs_read.svg",
        CreateInstance = () => new Core.Assets.ReadEqsResultNode
        {
            Id                 = Guid.NewGuid(),
            SensorVariableName = "",
            Pins               =
            [
                new Pin { Id = Guid.NewGuid(), Name = "Handle",      Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle"  } },
                new Pin { Id = Guid.NewGuid(), Name = "ResultIndex", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32"             } },
                new Pin { Id = Guid.NewGuid(), Name = "IsReady",     Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Boolean"           } },
                new Pin { Id = Guid.NewGuid(), Name = "ResultCount", Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Int32"             } },
                new Pin { Id = Guid.NewGuid(), Name = "Entity",      Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity"           } },
                new Pin { Id = Guid.NewGuid(), Name = "Position",    Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Numerics.Vector2"  } },
                new Pin { Id = Guid.NewGuid(), Name = "Score",       Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single"            } },
            ],
        },
    };

    public static NodeKindDescriptor SpawnEqsSensor() => new()
    {
        Kind        = "SpawnEqsSensor",
        DisplayName = "Spawn EQS Sensor",
        Category    = "EQS",
        Tooltip     = "Spawn an EQS sensor as a child entity. Pick a template, " +
                      "set parameters via input pins, get back a handle.",
        Icon        = "icons/eqs_spawn.svg",
        CreateInstance = () => new Core.Assets.SpawnEqsSensorNode
        {
            Id              = Guid.NewGuid(),
            TemplateAssetId = Guid.Empty,
            Pins            =
            [
                new Pin { Id = Guid.NewGuid(), Name = "In",              Direction = "In",  IsExec = true  },
                new Pin { Id = Guid.NewGuid(), Name = "Out",             Direction = "Out", IsExec = true  },
                new Pin { Id = Guid.NewGuid(), Name = "SearchRadius",    Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single"  } },
                new Pin { Id = Guid.NewGuid(), Name = "FactionFilter",   Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.UInt32"  } },
                new Pin { Id = Guid.NewGuid(), Name = "ThreatThreshold", Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Single"  } },
                new Pin { Id = Guid.NewGuid(), Name = "PublishPolicy",   Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Byte"    } },
                new Pin { Id = Guid.NewGuid(), Name = "Priority",        Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "System.Byte"    } },
                new Pin { Id = Guid.NewGuid(), Name = "Handle",          Direction = "Out", IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "FDP.Eqs.EqsSensorHandle" } },
                // Context slots (Self / Target / Leader by convention). The area query's EQS form
                // (EntitiesOfForceInArea) reads its area from ContextSlot1.
                new Pin { Id = Guid.NewGuid(), Name = "ContextSlot0",    Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } },
                new Pin { Id = Guid.NewGuid(), Name = "ContextSlot1",    Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } },
                new Pin { Id = Guid.NewGuid(), Name = "ContextSlot2",    Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } },
                // CE-485: one sensor per key from this node (e.g. per area in a loop); unconnected = the one default sensor.
                new Pin { Id = Guid.NewGuid(), Name = "Key",             Direction = "In",  IsExec = false, TypeRef = new BlueprintTypeRef { TypeId = "Fdp.Core.Entity" } },
            ],
        },
    };
}
