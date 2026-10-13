using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using static Hrot.Blueprints.Core.Assets.ArithmeticOperator;
using static Hrot.Blueprints.Core.Assets.ComparisonOperator;

namespace Hrot.Blueprints.Tests.Authoring;

/// <summary>
/// ⭐ <c>R-2006</c> — authors <c>SubordinateForEachDemo.bp.json</c>: a small, readable blueprint BEHAVIOUR that keeps — through the
/// REAL <c>Hrot.AI.Behaviors</c> generator and at runtime — the node usages <c>PlatoonHillAttackBp</c> was the only corpus asset to
/// carry. 📄 <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.5.
/// <list type="bullet">
///   <item><c>FlowForEach</c> over the commander's <c>UnitRoster</c>, with all three outs (<c>CurrentItem</c>, <c>CurrentIndex</c>, <c>Count</c>)</item>
///   <item><c>SendIntent</c> with a full order DTO (<c>MoveToLocation</c>)</item>
///   <item>fixed-list variables: an ENUM list (<c>Slots</c>, <c>SubordinateSlot</c>) and a STRUCT list (<c>Orders</c>, <c>SubordinateOrder</c>,
///         holding an <c>Entity</c>), written with <c>ListWrite</c> Clear / Resize / SetAt / Add, read with <c>ComponentItemGet</c> and
///         iterated with <c>ComponentForEach</c>; <c>MakeStruct</c> / <c>BreakStruct</c> over the struct</item>
///   <item><c>Cast</c>, <c>Not</c>, <c>GetTime</c>, <c>Compare</c>, an enum-typed variable (<c>LastOrder</c>)</item>
/// </list>
/// <para><b>Tick 1</b> — order every subordinate to <c>X = index × 10</c>, <c>Y = count</c>; mark its slot Ordered; record the order.
/// <b>Tick 2</b> — walk the recorded orders: for each whose slot reads Ordered, send a confirming order (<c>X = index × 10</c>,
/// <c>Y = -1</c>) and mark the slot Confirmed. <b>Afterwards</b> — hold (Running), ordering nothing. No parameters (<c>NoParams</c>, R-2004).</para>
/// </summary>
internal static class SubordinateForEachDemoAuthoring
{
    public const string Name = "SubordinateForEachDemo";

    private const string NS = "Hrot.AI.Behaviors.Brains";
    private const string SlotE = NS + ".SubordinateSlot";
    private const string OrderS = NS + ".SubordinateOrder";
    private const string OrderKindE = "Hrot.AI.Behaviors.Orders.OrderKind";
    private const string Flt = "System.Single", Int = "System.Int32", Bool = "System.Boolean", Dbl = "System.Double",
                         Ent = "Fdp.Core.Entity";
    private const string MoveDto = "Hrot.Map.Definitions.Behavior.MoveToLocationParamsJsonDto";

    private static readonly (string, string)[] OrderFields = { ("Unit", Ent), ("Index", Int), ("Count", Int), ("Confirmed", Bool) };

    public static BlueprintAsset Build()
    {
        var a = new BpAsset(Name, BlueprintDispatchKind.Behavior);
        a.Asset.ParamsDtoTypeId = "Hrot.Map.Definitions.Behavior.NoParams";
        a.Var("Sent", Bool);
        a.Var("Confirmed", Bool);
        a.Var("StartedAt", Flt);
        a.Var("LastOrder", "global::" + OrderKindE);
        a.Var("Slots", "global::" + SlotE, capacity: 8);
        a.Var("Orders", OrderS, capacity: 8);

        var g = a.Graph("Tick", GraphKind.Function);
        var entry = g.Entry();

        var first = g.Branch();
        g.D(g.Not(g.Get("Sent")), first, "Condition");
        g.X(entry, first);
        BuildOrderAll(g, first);

        var second = g.Branch();
        g.D(g.Not(g.Get("Confirmed")), second, "Condition");
        g.X(first, second, "False");
        g.X(second, g.Ret(NodeStatus.Running), "False");   // afterwards: hold
        BuildConfirm(g, second);
        return a.Asset;
    }

    private static SendIntentNode Move(BpGraph g, Node target, string? targetPin, Node x, Node y)
    {
        var members = ReflectionIntentContractProvider.PinnableMembers(typeof(Hrot.Map.Definitions.Behavior.MoveToLocationParamsJsonDto));
        var send = g.SendIntent("MoveToLocation", MoveDto, members.Select(m => (m.Name, m.TypeId)).ToArray());
        if (targetPin is null) g.D(target, send, "Target"); else g.D(target, targetPin, send, "Target");
        g.D(x, send, "X"); g.D(y, send, "Y");
        g.D(g.Lit(Dbl, "5"), send, "Speed");
        g.D(g.Lit(Dbl, "2"), send, "ArrivalRadius");
        return send;
    }

    // ── Tick 1: the roster loop — one order each, the enum slot marked, the order recorded ──
    private static void BuildOrderAll(BpGraph g, BranchNode first)
    {
        var stamp = g.Set("StartedAt", g.Time());
        var clearSlots = g.ListWrite("Slots", CollectionWriteOp.Clear);
        var sizeSlots = g.ListWrite("Slots", CollectionWriteOp.Resize); g.D(g.I(8), sizeSlots, "Length");
        var clearOrders = g.ListWrite("Orders", CollectionWriteOp.Clear);
        var fe = g.ForEachRoster();
        g.X(first, stamp, "True");
        g.Chain(stamp, clearSlots, sizeSlots, clearOrders, fe);

        var send = Move(g, fe, "CurrentItem",
            g.Bin(Multiply, g.Cast(Flt, fe, "CurrentIndex"), g.F(10f)), g.Cast(Flt, fe, "Count"));
        var mark = g.ListWrite("Slots", CollectionWriteOp.SetAt);
        g.D(fe, "CurrentIndex", mark, "Index"); g.D(g.Enum(SlotE, "Ordered"), mark, "Value");
        var rec = g.Make(OrderS, OrderFields);
        g.D(fe, "CurrentItem", rec, "Unit"); g.D(fe, "CurrentIndex", rec, "Index"); g.D(fe, "Count", rec, "Count");
        g.D(g.B(false), rec, "Confirmed");
        var add = g.ListWrite("Orders", CollectionWriteOp.Add); g.D(rec, add, "Value");
        g.X(fe, send, "Body"); g.X(send, mark); g.X(mark, add);

        var sent = g.Set("Sent", g.B(true));
        var last = g.Set("LastOrder", g.Enum(OrderKindE, "Move"));
        g.X(fe, sent, "Completed"); g.X(sent, last); g.X(last, g.Ret(NodeStatus.Running));
    }

    // ── Tick 2: iterate the recorded orders; confirm each whose slot reads Ordered ──
    private static void BuildConfirm(BpGraph g, BranchNode second)
    {
        var fe = g.ForEachList("Orders", OrderS);   // a struct list: element type without global:: (as BpAuthoring.ForEachList is used for structs)
        g.X(second, fe, "True");
        var o = g.Break(OrderS, OrderFields);
        g.D(fe, "CurrentItem", o, "Value");

        var slot = g.Add(new ComponentItemGetNode
        {
            CollectionKind = CollectionKind.BlackboardFixedList, CollectionFieldName = "Slots", ElementTypeFqn = "global::" + SlotE,
        });
        g.D(g.Get("Slots"), slot, "Collection");
        g.D(o, "Index", slot, "Index");
        var ordered = g.Branch();
        g.D(g.Cmp(Equal, slot, g.Enum(SlotE, "Ordered"), "Element", null), ordered, "Condition");
        g.X(fe, ordered, "Body");

        var send = Move(g, o, "Unit", g.Bin(Multiply, g.Cast(Flt, o, "Index"), g.F(10f)), g.F(-1f));
        var mark = g.ListWrite("Slots", CollectionWriteOp.SetAt);
        g.D(o, "Index", mark, "Index"); g.D(g.Enum(SlotE, "Confirmed"), mark, "Value");
        g.X(ordered, send, "True"); g.X(send, mark);

        var done = g.Set("Confirmed", g.B(true));
        g.X(fe, done, "Completed"); g.X(done, g.Ret(NodeStatus.Running));
    }
}
