using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Tests.Runtime;

/// <summary>A second blittable custom event (beside <see cref="PingDemoEvent"/>) for the typed-event-node rails.</summary>
[EventId(7403)]
public struct PongDemoEvent
{
    public Entity Target;
    public int Value;
}

/// <summary>
/// ⭐ CE-2013 (<c>DESIGN_Typed_Event_Nodes</c>) — hand-wires ONE Event graph that holds several typed event nodes, with
/// explicit pins so the wiring is exact (the fluent builder gives a graph one entry). Each event node carries its payload
/// in <see cref="EventEntryNode.Fields"/> (T-1), and the graph's <see cref="Graph.Inputs"/> stay empty.
/// </summary>
internal sealed class TypedEventGraph
{
    private static readonly BlueprintTypeRef Exec = new();
    public Graph Graph { get; }

    public TypedEventGraph(string name = "OnEvents")
        => Graph = new Graph { Id = Guid.NewGuid(), Name = name, Kind = GraphKind.Event };

    private static Pin P(string name, string dir, string? typeId = null) => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = typeId is null,
        TypeRef = typeId is null ? Exec : new BlueprintTypeRef { TypeId = typeId },
    };

    private T Add<T>(T node, params Pin[] pins) where T : Node
    {
        node.Id = Guid.NewGuid();
        node.Pins.AddRange(pins);
        Graph.Nodes.Add(node);
        return node;
    }

    /// <summary>An event node for <paramref name="fqn"/> with one <c>int</c> field per name.</summary>
    public EventEntryNode Event(string fqn, params string[] intFields)
        => Add(new EventEntryNode
        {
            EventTypeId = fqn,
            Fields = intFields.Select(f => new PublishEventFieldDecl { Name = f, TypeId = "System.Int32" }).ToList(),
        }, new[] { P("Out", "Out") }.Concat(intFields.Select(f => P(f, "Out", "System.Int32")))
              .Append(P("Event", "Out", "global::" + fqn)).ToArray());   // ⭐ CE-2014 — the whole-event pin (T-6)

    /// <summary>Break Struct over <paramref name="fqn"/>: a "Value" struct in, one <c>int</c> out per field.</summary>
    public BreakStructNode BreakStruct(string fqn, params string[] intFields) => BreakStructOf(fqn, "System.Int32", intFields);

    /// <summary>Break Struct over <paramref name="fqn"/> with every field of <paramref name="typeId"/>.</summary>
    public BreakStructNode BreakStructOf(string fqn, string typeId, params string[] fields)
        => Add(new BreakStructNode
        {
            StructTypeId = fqn,
            Fields = fields.Select(f => new StructFieldDecl { Name = f, TypeId = typeId }).ToList(),
        }, new[] { P("Value", "In", "global::" + fqn) }.Concat(fields.Select(f => P(f, "Out", typeId))).ToArray());

    public SetVariableNode Set(VariableDecl variable)
        => Add(new SetVariableNode { VariableId = variable.Id.ToString() },
               P("In", "In"), P("Out", "Out"), P("Value", "In", variable.Type.TypeId), P("Value", "Out", variable.Type.TypeId));

    public GetVariableNode Get(VariableDecl variable)
        => Add(new GetVariableNode { VariableId = variable.Id.ToString() }, P("Value", "Out", variable.Type.TypeId));

    public LiteralNode Literal(int value)
        => Add(new LiteralNode { TypeId = "System.Int32", ValueJson = value.ToString() }, P("Value", "Out", "System.Int32"));

    public BinaryOpNode AddInts()
        => Add(new BinaryOpNode { Operator = ArithmeticOperator.Add },
               P("A", "In", "System.Int32"), P("B", "In", "System.Int32"), P("Result", "Out", "System.Int32"));

    public RunBehaviorNode RunBehavior(string child)
        => Add(new RunBehaviorNode { BehaviorName = child }, P("In", "In"), P("Out", "Out"), P("OnFailure", "Out"));

    /// <summary>Exec wire: <paramref name="from"/>'s exec-out <paramref name="pin"/> (default "Out") → <paramref name="to"/>'s exec-in.</summary>
    public TypedEventGraph Then(Node from, Node to, string pin = "Out")
        => Wire(from.Pins.First(p => p.IsExec && p.Direction == "Out" && p.Name == pin),
                to.Pins.First(p => p.IsExec && p.Direction == "In"), from, to);

    /// <summary>Data wire: <paramref name="from"/>'s data-out <paramref name="outPin"/> → <paramref name="to"/>'s data-in <paramref name="inPin"/>.</summary>
    public TypedEventGraph Data(Node from, string outPin, Node to, string inPin)
        => Wire(from.Pins.First(p => !p.IsExec && p.Direction == "Out" && p.Name == outPin),
                to.Pins.First(p => !p.IsExec && p.Direction == "In" && p.Name == inPin), from, to);

    private TypedEventGraph Wire(Pin a, Pin b, Node from, Node to)
    {
        Graph.Links.Add(new Link { FromNodeId = from.Id, FromPinId = a.Id, ToNodeId = to.Id, ToPinId = b.Id });
        return this;
    }

    /// <summary>A counter tail: <c>Count = Count + 1</c>, entered by exec from <paramref name="from"/>.</summary>
    public SetVariableNode Increment(VariableDecl count)
    {
        var get = Get(count); var one = Literal(1); var add = AddInts(); var set = Set(count);
        Data(get, "Value", add, "A").Data(one, "Value", add, "B").Data(add, "Result", set, "Value");
        return set;
    }
}
