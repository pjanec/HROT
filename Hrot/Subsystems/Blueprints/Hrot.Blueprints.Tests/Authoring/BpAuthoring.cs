using System.Security.Cryptography;
using System.Text;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Host;

namespace Hrot.Blueprints.Tests.Authoring;

/// <summary>
/// ⭐ <c>CE-464</c> — a small authoring DSL over the REAL asset model: every node gets its pins from the editor's own
/// <see cref="NodePinSchema.GetCanonicalPins"/> (what placing the node on the canvas produces), links are made by pin
/// NAME, and every id is derived from the asset name + a counter so a re-run writes byte-identical JSON.
/// ⚠ It builds assets; it is not a runtime helper — the output is an ordinary editor-loadable <c>.bp.json</c>.
/// </summary>
internal sealed class BpAsset
{
    public readonly BlueprintAsset Asset;
    private int _seq;

    public BpAsset(string name, BlueprintDispatchKind dispatch)
    {
        Asset = new BlueprintAsset { Name = name, Dispatch = dispatch };
        Asset.AssetId = Id("asset");
    }

    public Guid Id(string salt)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"{Asset?.Name}/{salt}/{_seq++}"));
        return new Guid(bytes);
    }

    public VariableDecl Var(string name, string typeId, int capacity = 0, string? defaultJson = null)
    {
        var v = new VariableDecl { Id = Id("var:" + name), Name = name, DefaultValueJson = defaultJson,
            Type = new BlueprintTypeRef { TypeId = typeId, Capacity = capacity } };
        Asset.Variables.Add(v);
        return v;
    }

    public ParameterDecl Param(string name, string typeId, string? defaultJson = null)
    {
        var p = new ParameterDecl { Id = Id("param:" + name), Name = name, DefaultValueJson = defaultJson,
            Type = new BlueprintTypeRef { TypeId = typeId } };
        Asset.Parameters.Add(p);
        return p;
    }

    public VariableDecl V(string name) => Asset.Variables.Single(v => v.Name == name);

    public BpGraph Graph(string name, GraphKind kind,
                         (string Name, string Type)[]? inputs = null, (string Name, string Type)[]? outputs = null)
    {
        var g = new Graph { Id = Id("graph:" + name), Name = name, Kind = kind };
        foreach (var (n, t) in inputs ?? Array.Empty<(string, string)>())
            g.Inputs.Add(new ParameterDecl { Id = Id("in:" + name + n), Name = n, Type = new BlueprintTypeRef { TypeId = t } });
        foreach (var (n, t) in outputs ?? Array.Empty<(string, string)>())
            g.Outputs.Add(new ParameterDecl { Id = Id("out:" + name + n), Name = n, Type = new BlueprintTypeRef { TypeId = t } });
        Asset.Graphs.Add(g);
        return new BpGraph(this, g);
    }
}

/// <summary>One graph being authored — node factories plus name-based linking.</summary>
internal sealed class BpGraph
{
    private readonly BpAsset _a;
    public readonly Graph G;

    public BpGraph(BpAsset a, Graph g) { _a = a; G = g; }

    public T Add<T>(T node) where T : Node
    {
        node.Id = _a.Id("node:" + G.Name);
        var pins = NodePinSchema.GetCanonicalPins(node, asset: _a.Asset, containingGraph: G);
        node.Pins.Clear();
        foreach (var p in pins)
        {
            node.Pins.Add(new Pin
            {
                Id = _a.Id("pin:" + G.Name + p.Name), Name = p.Name, Direction = p.Direction, IsExec = p.IsExec,
                TypeRef = new BlueprintTypeRef { TypeId = PinType(p.TypeRef?.TypeId ?? ""), IsArray = p.TypeRef?.IsArray ?? false },
            });
        }
        G.Nodes.Add(node);
        return node;
    }

    /// <summary>
    /// A project type on a PIN is spelled <c>global::Ns.T</c> — the compiler's AN2 rule (a bare FQN is BP1500), which the
    /// editor's palette applies when it bakes a node. Declarations keep the dotted id (Stage4's S4 retry sizes them).
    /// </summary>
    private static string PinType(string typeId)
    {
        if (typeId.Length == 0 || typeId.StartsWith("global::", StringComparison.Ordinal) || typeId.IndexOf('.') < 0)
            return typeId;
        return Hrot.Blueprints.Core.Compiler.Catalogs.StaticTypeRegistry.Instance.TryResolve(
                   new BlueprintTypeRef { TypeId = typeId }, out _)
            ? typeId
            : "global::" + typeId;
    }

    private static Pin PinOf(Node n, string name, string dir, bool exec)
    {
        var p = n.Pins.FirstOrDefault(x => x.Name == name && x.Direction == dir && x.IsExec == exec);
        if (p is null && exec)
        {
            // Entry/Return spell their single exec pin differently across kinds ("Out"/"ExecOut", "In"/"ExecIn").
            var only = n.Pins.Where(x => x.IsExec && x.Direction == dir).ToList();
            if (only.Count == 1 && (name is "Out" or "ExecOut" or "In" or "ExecIn")) p = only[0];
        }
        return p ?? throw new InvalidOperationException(
            $"{n.GetType().Name} has no {(exec ? "exec" : "data")} {dir} pin '{name}'. Pins: "
            + string.Join(", ", n.Pins.Select(x => $"{x.Name}/{x.Direction}/{(x.IsExec ? "x" : x.TypeRef.TypeId)}")));
    }

    /// <summary>Data link: <paramref name="from"/>.<paramref name="outPin"/> → <paramref name="to"/>.<paramref name="inPin"/>.</summary>
    public void D(Node from, string outPin, Node to, string inPin)
        => G.Links.Add(new Link { FromNodeId = from.Id, FromPinId = PinOf(from, outPin, "Out", false).Id,
                                  ToNodeId = to.Id, ToPinId = PinOf(to, inPin, "In", false).Id });

    /// <summary>Data link from a node's single data output.</summary>
    public void D(Node from, Node to, string inPin)
        => D(from, from.Pins.Single(p => !p.IsExec && p.Direction == "Out").Name, to, inPin);

    /// <summary>Exec link. Default pin names are the common "Out" → "In"; Entry/Return use their own.</summary>
    public void X(Node from, Node to, string outPin = "Out", string inPin = "In")
    {
        if (from is EventEntryNode && outPin == "Out") outPin = "ExecOut";
        if (to is ReturnNode && inPin == "In") inPin = "ExecIn";
        G.Links.Add(new Link { FromNodeId = from.Id, FromPinId = PinOf(from, outPin, "Out", true).Id,
                               ToNodeId = to.Id, ToPinId = PinOf(to, inPin, "In", true).Id });
    }

    /// <summary>Chains exec through <paramref name="nodes"/> with the default pins.</summary>
    public void Chain(params Node[] nodes)
    {
        for (int i = 0; i + 1 < nodes.Length; i++) X(nodes[i], nodes[i + 1]);
    }

    // ── node factories ─────────────────────────────────────────────────────────────────────────
    public EventEntryNode Entry() => Add(new EventEntryNode());
    public ReturnNode Ret(NodeStatus s = NodeStatus.Success) => Add(new ReturnNode { Status = s });
    public BranchNode Branch() => Add(new BranchNode());

    public LiteralNode Lit(string typeId, string valueJson) => Add(new LiteralNode { TypeId = typeId, ValueJson = valueJson });
    public LiteralNode F(float v) => Lit("System.Single", v.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    public LiteralNode I(int v) => Lit("System.Int32", v.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public LiteralNode L(long v) => Lit("System.Int64", v.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public LiteralNode B(bool v) => Lit("System.Boolean", v ? "true" : "false");
    public LiteralNode S(string v) => Lit("System.String", System.Text.Json.JsonSerializer.Serialize(v));
    public LiteralNode Enum(string enumFqn, string member) => Lit("global::" + enumFqn, $"global::{enumFqn}.{member}");

    public GetVariableNode Get(string var) => Add(new GetVariableNode { VariableId = _a.V(var).Id.ToString() });
    public GetParameterNode Param(string name)
        => Add(new GetParameterNode { ParameterId = _a.Asset.Parameters.Single(p => p.Name == name).Id.ToString() });

    /// <summary>SetVariable <paramref name="var"/> ← <paramref name="value"/>.<paramref name="valuePin"/> (or its single output).</summary>
    public SetVariableNode Set(string var, Node value, string? valuePin = null)
    {
        var s = Add(new SetVariableNode { VariableId = _a.V(var).Id.ToString() });
        if (valuePin is null) D(value, s, "Value"); else D(value, valuePin, s, "Value");
        return s;
    }

    public CompareNode Cmp(ComparisonOperator op, Node a, Node b, string? aPin = null, string? bPin = null)
    {
        var c = Add(new CompareNode { Operator = op });
        Wire(a, aPin, c, "A"); Wire(b, bPin, c, "B");
        return c;
    }

    public BinaryOpNode Bin(ArithmeticOperator op, Node a, Node b, string? aPin = null, string? bPin = null)
    {
        var n = Add(new BinaryOpNode { Operator = op });
        Wire(a, aPin, n, "A"); Wire(b, bPin, n, "B");
        return n;
    }

    public BooleanOpNode And(Node a, Node b, string? aPin = null, string? bPin = null)
    {
        var n = Add(new BooleanOpNode { Operator = BooleanOperator.And });
        Wire(a, aPin, n, "A"); Wire(b, bPin, n, "B");
        return n;
    }

    public BooleanOpNode Or(Node a, Node b, string? aPin = null, string? bPin = null)
    {
        var n = Add(new BooleanOpNode { Operator = BooleanOperator.Or });
        Wire(a, aPin, n, "A"); Wire(b, bPin, n, "B");
        return n;
    }

    public NotNode Not(Node a, string? aPin = null)
    {
        var n = Add(new NotNode());
        Wire(a, aPin, n, "A");
        return n;
    }

    private void Wire(Node from, string? pin, Node to, string inPin)
    {
        if (pin is null) D(from, to, inPin); else D(from, pin, to, inPin);
    }

    /// <summary>A static CLR call. <paramref name="args"/> are (node, outPin-or-null) wired to the method's data-in pins in order.</summary>
    public FunctionCallNode Call(string typeFqn, string method, bool pure, FunctionCallContextKind ctx,
                                 params (Node Node, string? Pin)[] args)
    {
        var n = Add(new FunctionCallNode { TargetTypeId = typeFqn, MethodName = method, IsPure = pure, TrailingContext = ctx });
        var ins = n.Pins.Where(p => !p.IsExec && p.Direction == "In").ToList();
        if (ins.Count != args.Length)
            throw new InvalidOperationException($"{typeFqn}.{method}: {ins.Count} data-in pins, {args.Length} args");
        for (int i = 0; i < args.Length; i++)
        {
            var (src, sp) = args[i];
            var outPin = sp is null ? src.Pins.Single(p => !p.IsExec && p.Direction == "Out") : PinOf(src, sp, "Out", false);
            G.Links.Add(new Link { FromNodeId = src.Id, FromPinId = outPin.Id, ToNodeId = n.Id, ToPinId = ins[i].Id });
        }
        return n;
    }

    /// <summary>A call into one of this asset's Function graphs.</summary>
    public FunctionCallNode CallGraph(BpGraph target, bool pure, params (Node Node, string? Pin)[] args)
    {
        var n = Add(new FunctionCallNode { TargetGraphId = target.G.Id.ToString(), IsPure = pure });
        var ins = n.Pins.Where(p => !p.IsExec && p.Direction == "In").ToList();
        if (ins.Count != args.Length)
            throw new InvalidOperationException($"{target.G.Name}: {ins.Count} data-in pins, {args.Length} args");
        for (int i = 0; i < args.Length; i++)
        {
            var (src, sp) = args[i];
            var outPin = sp is null ? src.Pins.Single(p => !p.IsExec && p.Direction == "Out") : PinOf(src, sp, "Out", false);
            G.Links.Add(new Link { FromNodeId = src.Id, FromPinId = outPin.Id, ToNodeId = n.Id, ToPinId = ins[i].Id });
        }
        return n;
    }

    /// <summary>The single data output pin name of a function-call node.</summary>
    public static string Out(Node n) => n.Pins.Single(p => !p.IsExec && p.Direction == "Out").Name;

    public FlowForEachNode ForEachRoster() => Add(new FlowForEachNode
    {
        SourceComponentFqn = "Fdp.Core.CommandHierarchy.UnitRoster",
        CountAccessorFqn   = "Fdp.Core.CommandHierarchy.UnitRosterSubordinateEntitiesOps.Count",
        ItemAccessorFqn    = "Fdp.Core.CommandHierarchy.UnitRosterSubordinateEntitiesOps.Item",
    });

    /// <summary>ForEach over a fixed-list VARIABLE (wires GetVariable(list) → Collection).</summary>
    public ComponentForEachNode ForEachList(string listVar, string elemType)
    {
        var fe = Add(new ComponentForEachNode
        {
            CollectionKind = CollectionKind.BlackboardFixedList, CollectionFieldName = listVar, ElementTypeFqn = elemType,
        });
        D(Get(listVar), fe, "Collection");
        return fe;
    }

    public ComponentItemCountNode ListCount(string listVar)
    {
        var c = Add(new ComponentItemCountNode { CollectionKind = CollectionKind.BlackboardFixedList, CollectionFieldName = listVar });
        D(Get(listVar), c, "Collection");
        return c;
    }

    public ListWriteNode ListWrite(string listVar, CollectionWriteOp op)
        => Add(new ListWriteNode { VariableId = _a.V(listVar).Id.ToString(), Op = op });

    public GetComponentNode GetComp(string componentFqn, params (string Name, string TypeId)[] fields)
        => Add(new GetComponentNode
        {
            ComponentTypeFqn = componentFqn,
            Fields = fields.Select(f => new ComponentFieldDecl { Name = f.Name, TypeId = f.TypeId }).ToList(),
        });

    public MakeStructNode Make(string fqn, params (string Name, string TypeId)[] fields)
        => Add(new MakeStructNode { StructTypeId = fqn, Fields = fields.Select(f => new StructFieldDecl { Name = f.Name, TypeId = f.TypeId }).ToList() });

    public BreakStructNode Break(string fqn, params (string Name, string TypeId)[] fields)
        => Add(new BreakStructNode { StructTypeId = fqn, Fields = fields.Select(f => new StructFieldDecl { Name = f.Name, TypeId = f.TypeId }).ToList() });

    public SendIntentNode SendIntent(string intentId, string dtoFqn, params (string Name, string TypeId)[] members)
        => Add(new SendIntentNode { IntentId = intentId, DtoTypeFqn = dtoFqn,
            Fields = members.Select(f => new StructFieldDecl { Name = f.Name, TypeId = f.TypeId }).ToList() });

    public GetTimeNode Time() => Add(new GetTimeNode { Kind = TimeKind.SimTime });

    /// <summary>Spawn EQS Sensor (find-or-create, <c>DESIGN_Hill_Attack_Eqs_Migration.md</c> §4 D3) for a template AssetId.</summary>
    public SpawnEqsSensorNode SpawnEqs(Guid templateAssetId) => Add(new SpawnEqsSensorNode { TemplateAssetId = templateAssetId });

    /// <summary>Read EQS Result from the sensor held in <paramref name="sensorVar"/> (an <c>EqsSensorHandle</c> variable).</summary>
    public ReadEqsResultNode ReadEqs(string sensorVar) => Add(new ReadEqsResultNode { SensorVariableName = sensorVar });

    public CastNode Cast(string targetType, Node value, string? pin = null)
    {
        var c = Add(new CastNode { TargetTypeId = targetType });
        if (pin is null) D(value, c, "In"); else D(value, pin, c, "In");
        return c;
    }
}
