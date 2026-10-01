using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.NodeDrawers;
using static Hrot.Blueprints.Core.Assets.ArithmeticOperator;
using static Hrot.Blueprints.Core.Assets.ComparisonOperator;
using Ctx = Hrot.Blueprints.Core.Assets.FunctionCallContextKind;

namespace Hrot.Blueprints.Tests.Authoring;

/// <summary>
/// ⭐ <c>CE-464</c> — authors <c>PlatoonHillAttackBp.bp.json</c>: the hill-attack commander as ONE blueprint behaviour.
/// 📄 <c>docs/blueprints/DESIGN_Hill_Attack_Blueprint_Behaviour.md</c> — §3 is the shape, §5 maps every C# line to a graph here.
/// The reference is <c>HillAttackCommanderNodes.cs</c>; the comments cite its lines.
/// </summary>
internal static class PlatoonHillAttackBpAuthoring
{
    public const string Name = "PlatoonHillAttackBp";

    private const string NS     = "Hrot.AI.Behaviors.Brains";
    private const string Phase  = NS + ".HillAttackPhase";
    private const string SlotE  = NS + ".HillAttackSlot";
    private const string Runner = NS + ".HillAttackRunner";
    private const string Lib    = "Hrot.AI.Behaviors.StandardLibrary.BlueprintWorldLibrary";
    private const string MathT  = "Fdp.Toolkit.Blueprints.BlueprintMath";
    private const string EqsHandle = "FDP.Eqs.EqsSensorHandle";
    // ⭐ The area query is the EQS template EntitiesOfForceInArea — the same AssetId the C# commander asks
    //    (DESIGN_Hill_Attack_Eqs_Migration.md §4). 1 << ForceId.Hostile (= 2) ⇒ faction mask 4.
    private static readonly Guid AreaTemplate = new(Hrot.AI.Behaviors.Brains.HillAttackCommanderNodes.AreaTemplateAssetId);
    private const string HostileMask = "4";
    private const string Geo    = "Fdp.Toolkit.Behavior.Params.PickableGeoPoint";

    private const string Flt = "System.Single", Int = "System.Int32", Lng = "System.Int64", Bool = "System.Boolean",
                         Dbl = "System.Double", Ent = "Fdp.Core.Entity";

    private static readonly (string, string)[] RunnerFields =
        { ("Unit", Ent), ("FiringSlot", Int), ("BaselineSlot", Int), ("Started", Bool) };

    public static BlueprintAsset Build()
    {
        var a = new BpAsset(Name, BlueprintDispatchKind.Behavior);

        // ── Parameters: the scenario's JSON, unchanged (PickableGeoPoint carries its own [lat, lon] converter) ──
        a.Param("FiringLineStart", Geo);
        a.Param("FiringLineEnd", Geo);
        a.Param("BaselineStart", Geo);
        a.Param("BaselineEnd", Geo);
        a.Param("TankSpacing", Flt, "30");
        a.Param("TargetAreaNetworkId", Lng, "0");

        // ── Variables: resolved geometry, phase, slot/runner lists, query state, per-step scratch ──
        foreach (var v in new[] { "StartX", "StartY", "EndX", "EndY", "BaseSX", "BaseSY", "BaseEX", "BaseEY",
                                  "DirX", "DirY", "Spacing", "RequestTime", "BestD", "CurFx", "CurFy", "CurBx", "CurBy" })
            a.Var(v, Flt);
        a.Var("TargetArea", Ent);
        a.Var("Phase", "global::" + Phase);
        foreach (var v in new[] { "TotalSlots", "Wave", "TargetCount", "WaveIndex", "Avail", "K", "Seen", "Pick", "Best",
                                  "CurSlot", "CurBase" })
            a.Var(v, Int);
        a.Var("Sensor", EqsHandle);
        a.Var("CurNet", Lng);
        a.Var("AllArrived", Bool);
        a.Var("FiringSlots", "global::" + SlotE, capacity: 16);
        a.Var("BaselineReserved", Bool, capacity: 16);
        a.Var("Runners", Runner, capacity: 8);
        a.Var("Kept", Runner, capacity: 8);

        // ── graph shells first: a call's pins come from its target's Inputs/Outputs ──
        var tick     = a.Graph("Tick", GraphKind.Function);
        var resolve  = a.Graph("Resolve", GraphKind.Construction);
        var setup    = a.Graph("Setup", GraphKind.Function);
        var orderAll = a.Graph("OrderAllToBaseline", GraphKind.Function);
        var allAt    = a.Graph("AllAtBaseline", GraphKind.Function, outputs: new[] { ("Ok", Bool) });
        var slotT    = a.Graph("SlotT", GraphKind.Function, new[] { ("index", Int), ("count", Int) }, new[] { ("T", Flt) });
        var pickFire = a.Graph("PickFiringSlot", GraphKind.Function, new[] { ("unit", Ent) }, new[] { ("Slot", Int) });
        var pickBase = a.Graph("PickBaselineSlot", GraphKind.Function, new[] { ("x", Flt), ("y", Flt) }, new[] { ("Slot", Int) });
        var netId    = a.Graph("TargetNetId", GraphKind.Function, new[] { ("target", Ent) }, new[] { ("Net", Lng) });
        var dispatch = a.Graph("DispatchWave", GraphKind.Function);
        var update   = a.Graph("UpdateRunners", GraphKind.Function, outputs: new[] { ("Count", Int) });

        BuildResolve(resolve);
        BuildSlotT(slotT);
        BuildSetup(setup);
        BuildOrderAll(orderAll, slotT);
        BuildAllAtBaseline(allAt);
        BuildPickFiringSlot(pickFire);
        BuildPickBaselineSlot(pickBase, slotT);
        BuildTargetNetId(netId);
        BuildDispatchWave(dispatch, slotT, pickFire, pickBase, netId);
        BuildUpdateRunners(update);
        BuildTick(tick, setup, orderAll, allAt, dispatch, update);
        return a.Asset;
    }

    // Lerp(a, b, t) over two float variables.
    private static FunctionCallNode Lerp(BpGraph g, string a, string b, Node t, string? tPin = null)
        => g.Call(MathT, "Lerp", true, Ctx.None, (g.Get(a), null), (g.Get(b), null), (t, tPin));

    private static FunctionCallNode Alive(BpGraph g, Node e, string? pin = null)
        => g.Call(Lib, "IsAliveInCombat", true, Ctx.View, (e, pin));

    // ── Resolve: C# ResolvePlatoonHillAttackParams :667-811 (geo → Cartesian, attack direction, area entity) ──
    private static void BuildResolve(BpGraph g)
    {
        var entry = g.Entry();
        Node last = entry;
        void Then(Node n) { g.X(last, n); last = n; }

        (FunctionCallNode V, BreakStructNode B) Local(string param)
        {
            var geo = g.Break(Geo, ("Latitude", Dbl), ("Longitude", Dbl));
            g.D(g.Param(param), geo, "Value");
            var cart = g.Call(Lib, "LatLonToCartesian", true, Ctx.View, (geo, "Latitude"), (geo, "Longitude"));
            var v = g.Break("System.Numerics.Vector3", ("X", Flt), ("Y", Flt), ("Z", Flt));
            g.D(cart, v, "Value");
            return (cart, v);
        }
        var s = Local("FiringLineStart").B; var e = Local("FiringLineEnd").B;
        var bs = Local("BaselineStart").B;  var be = Local("BaselineEnd").B;
        Then(g.Set("StartX", s, "X")); Then(g.Set("StartY", s, "Y"));
        Then(g.Set("EndX", e, "X"));   Then(g.Set("EndY", e, "Y"));
        Then(g.Set("BaseSX", bs, "X")); Then(g.Set("BaseSY", bs, "Y"));
        Then(g.Set("BaseEX", be, "X")); Then(g.Set("BaseEY", be, "Y"));

        // :799-808 — area entity by network id (unknown ⇒ Entity.Null)
        Then(g.Set("TargetArea", g.Call(Lib, "EntityFromNetworkId", true, Ctx.View, (g.Param("TargetAreaNetworkId"), null))));

        // :714 — spacing = TankSpacing > 0 ? TankSpacing : 30
        var spPos = g.Branch();
        g.D(g.Cmp(GreaterThan, g.Param("TankSpacing"), g.F(0f)), spPos, "Condition");
        Then(spPos);
        var spYes = g.Set("Spacing", g.Param("TankSpacing"));
        var spNo  = g.Set("Spacing", g.F(30f));
        g.X(spPos, spYes, "True"); g.X(spPos, spNo, "False");

        // :755-796 — attack direction: the firing line's normal, flipped AWAY from the baseline centre
        var fx = g.Bin(Subtract, e, s, "X", "X");
        var fy = g.Bin(Subtract, e, s, "Y", "Y");
        var len = g.Call(MathT, "Sqrt", true, Ctx.None, (g.Bin(Add, g.Bin(Multiply, fx, fx), g.Bin(Multiply, fy, fy)), null));
        var half = g.F(0.5f);
        var ax = g.Bin(Subtract, g.Bin(Multiply, g.Bin(Add, s, e, "X", "X"), half), g.Bin(Multiply, g.Bin(Add, bs, be, "X", "X"), half));
        var ay = g.Bin(Subtract, g.Bin(Multiply, g.Bin(Add, s, e, "Y", "Y"), half), g.Bin(Multiply, g.Bin(Add, bs, be, "Y", "Y"), half));

        var hasLine = g.Branch();
        g.D(g.Cmp(GreaterThan, len, g.F(0.0001f)), hasLine, "Condition");
        g.X(spYes, hasLine); g.X(spNo, hasLine);

        // tangent t = f/len; perpendicular p = (t.y, -t.x); flip when p·away < 0
        var tx = g.Bin(Divide, fx, len); var ty = g.Bin(Divide, fy, len);
        var dot = g.Bin(Add, g.Bin(Multiply, ty, ax), g.Bin(Multiply, g.Bin(Subtract, g.F(0f), tx), ay));
        var flip = g.Branch();
        g.D(g.Cmp(LessThan, dot, g.F(0f)), flip, "Condition");
        g.X(hasLine, flip, "True");
        var keepX = g.Set("DirX", ty);
        var keepY = g.Set("DirY", g.Bin(Subtract, g.F(0f), tx));
        var flipX = g.Set("DirX", g.Bin(Subtract, g.F(0f), ty));
        var flipY = g.Set("DirY", tx);
        g.X(flip, keepX, "False"); g.X(keepX, keepY);
        g.X(flip, flipX, "True");  g.X(flipX, flipY);

        // degenerate line: the approach vector, else (1, 0)
        var aLen = g.Call(MathT, "Sqrt", true, Ctx.None, (g.Bin(Add, g.Bin(Multiply, ax, ax), g.Bin(Multiply, ay, ay)), null));
        var hasAway = g.Branch();
        g.D(g.Cmp(GreaterThan, aLen, g.F(0.0001f)), hasAway, "Condition");
        g.X(hasLine, hasAway, "False");
        var awX = g.Set("DirX", g.Bin(Divide, ax, aLen)); var awY = g.Set("DirY", g.Bin(Divide, ay, aLen));
        g.X(hasAway, awX, "True"); g.X(awX, awY);
        var oneX = g.Set("DirX", g.F(1f)); var oneY = g.Set("DirY", g.F(0f));
        g.X(hasAway, oneX, "False"); g.X(oneX, oneY);
    }

    // ── SlotT(index, count) = count > 1 ? index / (count - 1) : 0.5 — the C# slot interpolation (:102, :367, :389) ──
    private static void BuildSlotT(BpGraph g)
    {
        var entry = g.Entry();
        var br = g.Branch();
        g.D(g.Cmp(GreaterThan, entry, g.I(1), "count"), br, "Condition");
        g.X(entry, br);
        var r1 = g.Ret(); var r2 = g.Ret();
        g.D(g.Call(MathT, "Divide", true, Ctx.None, (entry, "index"), (g.Bin(Subtract, entry, g.I(1), "count"), null)), r1, "T");
        g.D(g.F(0.5f), r2, "T");
        g.X(br, r1, "True"); g.X(br, r2, "False");
    }

    // ── Setup: C# Action_CalculateSegments :51-73 ──
    private static void BuildSetup(BpGraph g)
    {
        var entry = g.Entry();
        var dx = g.Bin(Subtract, g.Get("EndX"), g.Get("StartX"));
        var dy = g.Bin(Subtract, g.Get("EndY"), g.Get("StartY"));
        var segLen = g.Call(MathT, "Sqrt", true, Ctx.None, (g.Bin(Add, g.Bin(Multiply, dx, dx), g.Bin(Multiply, dy, dy)), null));
        var ratio = g.Call(MathT, "Divide", true, Ctx.None, (segLen, null), (g.Get("Spacing"), null));
        var trunc = g.Cast(Int, ratio);                                         // (int)(segLen / spacing)
        var total = g.Set("TotalSlots", g.Call(MathT, "ClampInt", true, Ctx.None, (trunc, "Out"), (g.I(1), null), (g.I(16), null)));
        var fClear = g.ListWrite("FiringSlots", CollectionWriteOp.Clear);
        var fSize  = g.ListWrite("FiringSlots", CollectionWriteOp.Resize); g.D(g.Get("TotalSlots"), fSize, "Length");
        var bClear = g.ListWrite("BaselineReserved", CollectionWriteOp.Clear);
        var bSize  = g.ListWrite("BaselineReserved", CollectionWriteOp.Resize); g.D(g.Get("TotalSlots"), bSize, "Length");
        var rClear = g.ListWrite("Runners", CollectionWriteOp.Clear);
        g.Chain(entry, total, fClear, fSize, bClear, bSize, rClear,   // ⚠ Cast is a PURE node (Stage5:3720) — never exec-chained
            g.Set("Wave", g.I(0)), g.Ret());
    }

    // ── OrderAllToBaseline: C# Action_DispatchAllToBaseline :81-130 ──
    private static void BuildOrderAll(BpGraph g, BpGraph slotT)
    {
        var entry = g.Entry();
        var clear = g.ListWrite("BaselineReserved", CollectionWriteOp.Clear);
        var size  = g.ListWrite("BaselineReserved", CollectionWriteOp.Resize); g.D(g.Get("TotalSlots"), size, "Length");
        var fe = g.ForEachRoster();
        g.Chain(entry, clear, size, fe);
        g.X(fe, g.Ret(), "Completed");

        var alive = g.Branch();
        g.D(Alive(g, fe, "CurrentItem"), alive, "Condition");
        g.X(fe, alive, "Body");
        var t = g.CallGraph(slotT, false, (fe, "CurrentIndex"), (fe, "Count"));
        var move = SendMove(g, fe, Lerp(g, "BaseSX", "BaseEX", t, "T"), Lerp(g, "BaseSY", "BaseEY", t, "T"));
        var reserve = g.ListWrite("BaselineReserved", CollectionWriteOp.SetAt);
        g.D(fe, "CurrentIndex", reserve, "Index"); g.D(g.B(true), reserve, "Value");
        g.X(alive, t, "True"); g.X(t, move); g.X(move, reserve);
    }

    private static SendIntentNode SendMove(BpGraph g, FlowForEachNode fe, Node x, Node y)
    {
        var members = ReflectionIntentContractProvider.PinnableMembers(typeof(Hrot.Map.Definitions.Behavior.MoveToLocationParamsJsonDto));
        var send = g.SendIntent("MoveToLocation", "Hrot.Map.Definitions.Behavior.MoveToLocationParamsJsonDto",
            members.Select(m => (m.Name, m.TypeId)).ToArray());
        g.D(fe, "CurrentItem", send, "Target");
        g.D(x, send, "X"); g.D(y, send, "Y");
        g.D(g.Lit(Dbl, "15"), send, "Speed"); g.D(g.Lit(Dbl, "5"), send, "ArrivalRadius");   // :110-111
        return send;
    }

    // ── AllAtBaseline: C# Condition_AreAllAtBaseline :140-180 (dead counts as arrived; no NavigationStatus = not yet) ──
    private static void BuildAllAtBaseline(BpGraph g)
    {
        var entry = g.Entry();
        var fe = g.ForEachRoster();
        g.Chain(entry, g.Set("AllArrived", g.B(true)), fe);
        var ret = g.Ret(); g.D(g.Get("AllArrived"), ret, "Ok");
        g.X(fe, ret, "Completed");

        var alive = g.Branch();
        g.D(Alive(g, fe, "CurrentItem"), alive, "Condition");
        g.X(fe, alive, "Body");
        var nav = g.GetComp("Fdp.Toolkit.Navigation.NavigationStatus", ("Result", "global::Fdp.Toolkit.Navigation.NavigationResult"));
        g.D(fe, "CurrentItem", nav, "Target");
        var moving = g.Or(g.Not(nav, "Found"),
            g.Cmp(Equal, nav, g.Enum("Fdp.Toolkit.Navigation.NavigationResult", "InProgress"), "Result"));
        var waiting = g.Branch();
        g.D(moving, waiting, "Condition");
        g.X(alive, waiting, "True");
        g.X(waiting, g.Set("AllArrived", g.B(false)), "True");
    }

    // ── PickFiringSlot(unit): C# :344-364 — the k-th free slot, k drawn per SUBORDINATE (SimRng seeded by its index) ──
    private static void BuildPickFiringSlot(BpGraph g)
    {
        var entry = g.Entry();
        var count = g.ForEachList("FiringSlots", "global::" + SlotE);
        g.Chain(entry, g.Set("Avail", g.I(0)), count);
        var isFree = g.Branch();
        g.D(g.Cmp(Equal, count, g.Enum(SlotE, "Free"), "CurrentItem"), isFree, "Condition");
        g.X(count, isFree, "Body");
        g.X(isFree, g.Set("Avail", g.Bin(Add, g.Get("Avail"), g.I(1))), "True");

        var none = g.Branch();
        g.D(g.Cmp(Equal, g.Get("Avail"), g.I(0)), none, "Condition");
        g.X(count, none, "Completed");
        var r0 = g.Ret(); g.D(g.I(-1), r0, "Slot");
        g.X(none, r0, "True");

        var k = g.Set("K", g.Call(Lib, "RandomIntSeeded", true, Ctx.View,
            (g.Call(Lib, "EntityIndex", true, Ctx.None, (entry, "unit")), null), (g.Get("Wave"), null), (g.I(0), null), (g.Get("Avail"), null)));
        var pick = g.ForEachList("FiringSlots", "global::" + SlotE);
        g.X(none, k, "False");
        g.Chain(k, g.Set("Seen", g.I(0)), g.Set("Pick", g.I(-1)), pick);
        var r1 = g.Ret(); g.D(g.Get("Pick"), r1, "Slot");
        g.X(pick, r1, "Completed");

        var free2 = g.Branch();
        g.D(g.Cmp(Equal, pick, g.Enum(SlotE, "Free"), "CurrentItem"), free2, "Condition");
        g.X(pick, free2, "Body");
        var hit = g.Branch();
        g.D(g.Cmp(Equal, g.Get("Seen"), g.Get("K")), hit, "Condition");
        g.X(free2, hit, "True");
        var setPick = g.Set("Pick", pick, "CurrentIndex");
        g.X(hit, setPick, "True"); g.X(setPick, g.Set("Seen", g.Bin(Add, g.Get("Seen"), g.I(1))));
        g.X(hit, g.Set("Seen", g.Bin(Add, g.Get("Seen"), g.I(1))), "False");
    }

    // ── PickBaselineSlot(x, y): C# PickClosestBaselineSlot :599-632 ──
    private static void BuildPickBaselineSlot(BpGraph g, BpGraph slotT)
    {
        var entry = g.Entry();
        var pass1 = g.ForEachList("BaselineReserved", Bool);
        g.Chain(entry, g.Set("Best", g.I(-1)), g.Set("BestD", g.F(3e38f)), pass1);

        void Body(ComponentForEachNode fe, Node bodyStart, string bodyPin)
        {
            var t = g.CallGraph(slotT, false, (fe, "CurrentIndex"), (g.Get("TotalSlots"), null));
            g.X(bodyStart, t, bodyPin);
            var ddx = g.Bin(Subtract, Lerp(g, "BaseSX", "BaseEX", t, "T"), entry, null, "x");
            var ddy = g.Bin(Subtract, Lerp(g, "BaseSY", "BaseEY", t, "T"), entry, null, "y");
            var d = g.Bin(Add, g.Bin(Multiply, ddx, ddx), g.Bin(Multiply, ddy, ddy));
            var closer = g.Branch();
            g.D(g.Cmp(LessThan, d, g.Get("BestD")), closer, "Condition");
            g.X(t, closer);
            var setD = g.Set("BestD", d);
            g.X(closer, setD, "True"); g.X(setD, g.Set("Best", fe, "CurrentIndex"));
        }

        var free = g.Branch();
        g.D(g.Not(pass1, "CurrentItem"), free, "Condition");
        g.X(pass1, free, "Body");
        Body(pass1, free, "True");

        var found = g.Branch();
        g.D(g.Cmp(GreaterThanOrEqual, g.Get("Best"), g.I(0)), found, "Condition");
        g.X(pass1, found, "Completed");
        var r1 = g.Ret(); g.D(g.Get("Best"), r1, "Slot");
        g.X(found, r1, "True");

        // :620-631 — every slot reserved: the closest regardless
        var pass2 = g.ForEachList("BaselineReserved", Bool);
        var resetD = g.Set("BestD", g.F(3e38f));
        g.X(found, resetD, "False"); g.X(resetD, pass2);
        Body(pass2, pass2, "Body");
        var r2 = g.Ret(); g.D(g.Get("Best"), r2, "Slot");
        g.X(pass2, r2, "Completed");
    }

    // ── TargetNetId(target): C# :376-386 — the target's network id if it is alive and replicated, else 0 ──
    private static void BuildTargetNetId(BpGraph g)
    {
        var entry = g.Entry();
        var id = g.GetComp("Fdp.Toolkit.Replication.Components.NetworkIdentity", ("Value", Lng));
        g.D(entry, "target", id, "Target");
        var ok = g.Branch();
        g.D(g.And(Alive(g, entry, "target"), id, null, "Found"), ok, "Condition");
        g.X(entry, ok);
        var r1 = g.Ret(); g.D(id, "Value", r1, "Net");
        var r2 = g.Ret(); g.D(g.L(0), r2, "Net");
        g.X(ok, r1, "True"); g.X(ok, r2, "False");
    }

    /// <summary>The area sensor: EntitiesOfForceInArea, hostile mask, ContextSlot1 = the area (find-or-create).</summary>
    private static SpawnEqsSensorNode SpawnAreaSensor(BpGraph g)
    {
        var spawn = g.SpawnEqs(AreaTemplate);
        g.D(g.Lit("System.UInt32", HostileMask), spawn, "FactionFilter");
        g.D(g.Get("TargetArea"), spawn, "ContextSlot1");
        return spawn;
    }

    // ── DispatchWave: C# Action_DispatchWaveWithTargets :289-447 ──
    private static void BuildDispatchWave(BpGraph g, BpGraph slotT, BpGraph pickFire, BpGraph pickBase, BpGraph netId)
    {
        var entry = g.Entry();

        // :292 — WaveUsedSlotsMask = 0
        var reset = g.ForEachList("FiringSlots", "global::" + SlotE);
        g.X(entry, reset);
        var used = g.Branch();
        g.D(g.Cmp(Equal, reset, g.Enum(SlotE, "WaveUsed"), "CurrentItem"), used, "Condition");
        g.X(reset, used, "Body");
        var toFree = g.ListWrite("FiringSlots", CollectionWriteOp.SetAt);
        g.D(reset, "CurrentIndex", toFree, "Index"); g.D(g.Enum(SlotE, "Free"), toFree, "Value");
        g.X(used, toFree, "True");

        // :293, :315 — attackers restart; a zero target count is treated as one
        var fe = g.ForEachRoster();
        var rClear = g.ListWrite("Runners", CollectionWriteOp.Clear);
        g.X(reset, g.Set("WaveIndex", g.I(0)), "Completed");
        var wi = g.G.Nodes.Last();
        g.Chain(wi, rClear, g.Set("TargetCount", g.Call(MathT, "MaxInt", true, Ctx.None, (g.Get("TargetCount"), null), (g.I(1), null))), fe);

        // :334-342 — room for a runner, alive, and this wave's parity (everyone when the platoon is ≤ 3)
        var room   = g.Cmp(LessThan, g.ListCount("Runners"), g.I(8));
        var parity = g.Or(g.Cmp(LessThanOrEqual, fe, g.I(3), "Count"),
            g.Cmp(Equal, g.Call(MathT, "ModInt", true, Ctx.None, (g.Call(Lib, "EntityIndex", true, Ctx.None, (fe, "CurrentItem")), null), (g.I(2), null)), g.Get("Wave")));
        var takes = g.Branch();
        g.D(g.And(g.And(room, Alive(g, fe, "CurrentItem")), parity), takes, "Condition");
        g.X(fe, takes, "Body");

        var pick = g.CallGraph(pickFire, false, (fe, "CurrentItem"));
        g.X(takes, pick, "True");
        var hasSlot = g.Branch();
        g.D(g.Cmp(GreaterThanOrEqual, pick, g.I(0), "Slot"), hasSlot, "Condition");
        g.X(pick, hasSlot);

        var curSlot = g.Set("CurSlot", pick, "Slot");
        var ft = g.CallGraph(slotT, false, (g.Get("CurSlot"), null), (g.Get("TotalSlots"), null));
        var curFx = g.Set("CurFx", Lerp(g, "StartX", "EndX", ft, "T"));
        var curFy = g.Set("CurFy", Lerp(g, "StartY", "EndY", ft, "T"));
        var pb = g.CallGraph(pickBase, false, (g.Get("CurFx"), null), (g.Get("CurFy"), null));
        var curBase = g.Set("CurBase", pb, "Slot");
        var bt = g.CallGraph(slotT, false, (g.Get("CurBase"), null), (g.Get("TotalSlots"), null));
        var curBx = g.Set("CurBx", Lerp(g, "BaseSX", "BaseEX", bt, "T"));
        var curBy = g.Set("CurBy", Lerp(g, "BaseSY", "BaseEY", bt, "T"));
        var target = g.ReadEqs("Sensor");                                       // round-robin over the area sensor's answer
        g.D(g.Get("Sensor"), target, "Handle");
        g.D(g.Call(MathT, "ModInt", true, Ctx.None, (g.Get("WaveIndex"), null), (g.Get("TargetCount"), null)), target, "ResultIndex");
        var net = g.CallGraph(netId, false, (target, "Entity"));
        var curNet = g.Set("CurNet", net, "Net");

        var make = g.Make(Runner, RunnerFields);
        g.D(fe, "CurrentItem", make, "Unit"); g.D(g.Get("CurSlot"), make, "FiringSlot");
        g.D(g.Get("CurBase"), make, "BaselineSlot"); g.D(g.B(false), make, "Started");
        var add = g.ListWrite("Runners", CollectionWriteOp.Add); g.D(make, add, "Value");
        var markUsed = g.ListWrite("FiringSlots", CollectionWriteOp.SetAt);
        g.D(g.Get("CurSlot"), markUsed, "Index"); g.D(g.Enum(SlotE, "WaveUsed"), markUsed, "Value");
        var reserve = g.ListWrite("BaselineReserved", CollectionWriteOp.SetAt);
        g.D(g.Get("CurBase"), reserve, "Index"); g.D(g.B(true), reserve, "Value");
        var nextIdx = g.Set("WaveIndex", g.Bin(Add, g.Get("WaveIndex"), g.I(1)));

        var send = g.SendIntent("HullDownAttack", "Hrot.Map.Definitions.Behavior.Intents.HullDownAttackIntentDto",
            ReflectionIntentContractProvider.PinnableMembers(typeof(Hrot.Map.Definitions.Behavior.Intents.HullDownAttackIntentDto))
                .Select(m => (m.Name, m.TypeId)).ToArray());
        g.D(fe, "CurrentItem", send, "Target");
        g.D(g.Get("CurFx"), send, "SlotX"); g.D(g.Get("CurFy"), send, "SlotY");
        g.D(g.Get("CurBx"), send, "BaselineX"); g.D(g.Get("CurBy"), send, "BaselineY");
        g.D(g.Get("DirX"), send, "AttackDirX"); g.D(g.Get("DirY"), send, "AttackDirY");
        g.D(g.Get("CurNet"), send, "TargetNetworkId");

        g.X(hasSlot, curSlot, "True");
        g.Chain(curSlot, ft, curFx, curFy, pb, curBase, bt, curBx, curBy, net, curNet, add, markUsed, reserve, nextIdx, send);

        // :439-443 — flip the wave (the area sensor stays for the next wave's question — EQS migration §4 D6)
        var flip = g.Set("Wave", g.Bin(Subtract, g.I(1), g.Get("Wave")));
        g.X(fe, flip, "Completed");
        g.Chain(flip, g.Ret());
    }

    // ── UpdateRunners: C# Condition_IsWaveCompleted :457-513 — returns the runners still out ──
    private static void BuildUpdateRunners(BpGraph g)
    {
        var entry = g.Entry();
        var kClear = g.ListWrite("Kept", CollectionWriteOp.Clear);
        var fe = g.ForEachList("Runners", Runner);
        g.Chain(entry, kClear, fe);

        var r = g.Break(Runner, RunnerFields);
        g.D(fe, "CurrentItem", r, "Value");
        var alive = g.Branch();
        g.D(Alive(g, r, "Unit"), alive, "Condition");
        g.X(fe, alive, "Body");

        // :467-472 — knocked out: burn its slot, free its baseline slot, drop it
        var burn = g.ListWrite("FiringSlots", CollectionWriteOp.SetAt);
        g.D(r, "FiringSlot", burn, "Index"); g.D(g.Enum(SlotE, "Burned"), burn, "Value");
        var release = g.ListWrite("BaselineReserved", CollectionWriteOp.SetAt);
        g.D(r, "BaselineSlot", release, "Index"); g.D(g.B(false), release, "Value");
        g.X(alive, burn, "False"); g.X(burn, release);

        var bs = g.GetComp("Fdp.Toolkit.Behavior.Components.BehaviorState", ("ActiveBehaviorHash", Int));
        g.D(r, "Unit", bs, "Target");
        var runHash = g.Call(Lib, "BehaviorHashOf", true, Ctx.None, (g.S("HullDownAttackRun"), null));
        var isRun = g.And(bs, g.Cmp(Equal, bs, runHash, "ActiveBehaviorHash", null), "Found", null);
        var started = g.Branch();
        g.D(r, "Started", started, "Condition");
        g.X(alive, started, "True");

        // :474-485 — not started yet: keep it, marking it started once the run shows up
        var mk = g.Make(Runner, RunnerFields);
        g.D(r, "Unit", mk, "Unit"); g.D(r, "FiringSlot", mk, "FiringSlot"); g.D(r, "BaselineSlot", mk, "BaselineSlot");
        g.D(isRun, mk, "Started");
        var keepNew = g.ListWrite("Kept", CollectionWriteOp.Add); g.D(mk, keepNew, "Value");
        g.X(started, keepNew, "False");

        // :486-499 — started and the run is over (a BehaviorState with another hash): free its baseline slot, drop it
        var over = g.Branch();
        g.D(g.And(bs, g.Not(g.Cmp(Equal, bs, runHash, "ActiveBehaviorHash", null)), "Found", null), over, "Condition");
        g.X(started, over, "True");
        var release2 = g.ListWrite("BaselineReserved", CollectionWriteOp.SetAt);
        g.D(r, "BaselineSlot", release2, "Index"); g.D(g.B(false), release2, "Value");
        g.X(over, release2, "True");
        var keep = g.ListWrite("Kept", CollectionWriteOp.Add); g.D(fe, "CurrentItem", keep, "Value");
        g.X(over, keep, "False");

        // Runners := Kept (a loop's count is read once, so survivors are rebuilt rather than removed in place)
        var rClear = g.ListWrite("Runners", CollectionWriteOp.Clear);
        var copy = g.ForEachList("Kept", Runner);
        g.X(fe, rClear, "Completed"); g.X(rClear, copy);
        var readd = g.ListWrite("Runners", CollectionWriteOp.Add); g.D(copy, "CurrentItem", readd, "Value");
        g.X(copy, readd, "Body");
        var ret = g.Ret(); g.D(g.ListCount("Runners"), ret, "Count");
        g.X(copy, ret, "Completed");
    }

    // ── Tick: the phase machine (the C# BTree :557-586 as Phase transitions) ──
    private static void BuildTick(BpGraph g, BpGraph setup, BpGraph orderAll, BpGraph allAt, BpGraph dispatch, BpGraph update)
    {
        var entry = g.Entry();
        Node prev = entry; string prevPin = "Out";
        BranchNode When(string phase)
        {
            var br = g.Branch();
            g.D(g.Cmp(Equal, g.Get("Phase"), g.Enum(Phase, phase)), br, "Condition");
            g.X(prev, br, prevPin);
            prev = br; prevPin = "False";
            return br;
        }
        Node Go(string phase) => g.Set("Phase", g.Enum(Phase, phase));
        ReturnNode Running() => g.Ret(NodeStatus.Running);

        // Setup → ToBaseline (:565-568)
        var s = When("Setup");
        var callSetup = g.CallGraph(setup, false);
        g.X(s, callSetup, "True"); g.Chain(callSetup, Go("ToBaseline"), Running());

        var tb = When("ToBaseline");
        var order1 = g.CallGraph(orderAll, false);
        g.X(tb, order1, "True"); g.Chain(order1, Go("AwaitBaseline"), Running());

        var ab = When("AwaitBaseline");
        var at1 = g.CallGraph(allAt, false);
        g.X(ab, at1, "True");
        var at1b = g.Branch(); g.D(at1, "Ok", at1b, "Condition"); g.X(at1, at1b);
        var toQuery = Go("Query"); g.X(at1b, toQuery, "True"); g.X(toQuery, Running());
        g.X(at1b, Running(), "False");

        // Query (:190-229) — a missing area ends the waves. ⭐ EQS (DESIGN_Hill_Attack_Eqs_Migration.md §3.2): Spawn EQS Sensor
        // is find-or-create. A sensor that exists is REFRESHED (a new epoch); one created this tick needs none — its creation
        // is the question. Either way the answer is awaited from now.
        var q = When("Query");
        var areaOk = g.Branch();
        g.D(g.Call(Lib, "EntityExists", true, Ctx.View, (g.Get("TargetArea"), null)), areaOk, "Condition");
        g.X(q, areaOk, "True");
        // ⭐ CE-482 — FAIL LOUD, the same as the C# commander: no area ⇒ Fault Behaviour (the run ends Faulted after this tick).
        var faultNoArea = g.Call(Lib, "FaultBehaviour", false, Ctx.SelfAndView,
            (g.I(1), null), (g.S("Hill attack: the target area entity is missing or dead."), null));
        g.X(areaOk, faultNoArea, "False"); g.Chain(faultNoArea, Go("Return"), Running());
        // ⚠ ONE spawn node for both phases: a node's InstanceId — the sensor's identity — is baked from its node id, so a
        //   second spawn node would be a second sensor.
        var spawn = SpawnAreaSensor(g);
        g.X(areaOk, spawn, "True");
        var keep = g.Set("Sensor", spawn, "Handle");
        g.X(spawn, keep);
        var awaiting = g.Branch();
        g.D(g.Cmp(Equal, g.Get("Phase"), g.Enum(Phase, "AwaitQuery")), awaiting, "Condition");
        g.X(keep, awaiting);
        var refresh = g.Call(Lib, "RefreshEqsSensor", false, Ctx.View, (g.Get("Sensor"), null));
        g.X(awaiting, refresh, "False");
        g.Chain(refresh, g.Set("RequestTime", g.Time()), Go("AwaitQuery"), Running());

        // AwaitQuery (:239-280) — 5 s timeout (with no area the sensor answers NOTHING); zero targets ends the waves.
        // The sensor is re-found through the same spawn node first: the tick after its creation, that is where its handle
        // comes from.
        var aq = When("AwaitQuery");
        g.X(aq, spawn, "True");
        var answer = g.ReadEqs("Sensor");
        g.D(g.Get("Sensor"), answer, "Handle"); g.D(g.I(0), answer, "ResultIndex");
        var ready = g.Branch();
        g.D(answer, "IsReady", ready, "Condition");
        g.X(awaiting, ready, "True");
        var late = g.Branch();
        g.D(g.Cmp(GreaterThan, g.Bin(Subtract, g.Time(), g.Get("RequestTime")), g.F(5f)), late, "Condition");
        g.X(ready, late, "False");
        g.X(late, Running(), "False");
        var dropLate = g.Call(Lib, "DestroyEqsSensor", false, Ctx.View, (g.Get("Sensor"), null));
        // ⭐ CE-482 — FAIL LOUD: silence for 5 s means the question cannot be answered (EQS §17.5), never a quiet "clear".
        var faultLate = g.Call(Lib, "FaultBehaviour", false, Ctx.SelfAndView,
            (g.I(2), null), (g.S("Hill attack: the EQS area sensor did not answer within 5 s."), null));
        g.X(late, dropLate, "True"); g.Chain(dropLate, faultLate, Go("Return"), Running());
        var clear = g.Branch(); g.D(g.Cmp(Equal, answer, g.I(0), "ResultCount"), clear, "Condition");
        g.X(ready, clear, "True");
        var dropClear = g.Call(Lib, "DestroyEqsSensor", false, Ctx.View, (g.Get("Sensor"), null));
        g.X(clear, dropClear, "True"); g.Chain(dropClear, Go("Return"), Running());
        var setCount = g.Set("TargetCount", answer, "ResultCount");
        g.X(clear, setCount, "False");
        g.Chain(setCount, Go("Dispatch"), Running());

        var d = When("Dispatch");
        var callDispatch = g.CallGraph(dispatch, false);
        g.X(d, callDispatch, "True"); g.Chain(callDispatch, Go("AwaitWave"), Running());

        // AwaitWave (:457-513) — all runners back or gone ⇒ the next query
        var aw = When("AwaitWave");
        var upd = g.CallGraph(update, false);
        g.X(aw, upd, "True");
        var done = g.Branch(); g.D(g.Cmp(Equal, upd, g.I(0), "Count"), done, "Condition"); g.X(upd, done);
        var again = Go("Query"); g.X(done, again, "True"); g.X(again, Running());
        g.X(done, Running(), "False");

        // Return (:583-585, CE-459) — the platoon goes back to the baseline, then the behaviour succeeds
        var rt = When("Return");
        var order2 = g.CallGraph(orderAll, false);
        g.X(rt, order2, "True"); g.Chain(order2, Go("AwaitReturn"), Running());

        var ar = When("AwaitReturn");
        var at2 = g.CallGraph(allAt, false);
        g.X(ar, at2, "True");
        var home = g.Branch(); g.D(at2, "Ok", home, "Condition"); g.X(at2, home);
        g.X(home, g.Ret(NodeStatus.Success), "True");
        g.X(home, Running(), "False");

        g.X(prev, Running(), prevPin);
    }
}
