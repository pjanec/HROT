using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>
/// Fake node catalog with 30+ hard-coded node kinds across multiple categories.
/// </summary>
public sealed class FakeNodeCatalog : INodeCatalog
{
    public IReadOnlyList<NodeCatalogEntry>      All        { get; }
    public IReadOnlyList<NodeCategoryDescriptor> Categories { get; }

    public FakeNodeCatalog()
    {
        var exec  = new PinSignature("",       PinKind.Exec, null, false);
        var execO = new PinSignature("",       PinKind.Exec, null, false);
        var fltA  = new PinSignature("A",      PinKind.Data, new TypeKey("System.Single"), false);
        var fltB  = new PinSignature("B",      PinKind.Data, new TypeKey("System.Single"), false);
        var fltR  = new PinSignature("Result", PinKind.Data, new TypeKey("System.Single"), false);
        var intA  = new PinSignature("A",      PinKind.Data, new TypeKey("System.Int32"),  false);
        var intB  = new PinSignature("B",      PinKind.Data, new TypeKey("System.Int32"),  false);
        var intR  = new PinSignature("Result", PinKind.Data, new TypeKey("System.Int32"),  false);
        var boolV = new PinSignature("Value",  PinKind.Data, new TypeKey("System.Boolean"), false);
        var boolC = new PinSignature("Condition", PinKind.Data, new TypeKey("System.Boolean"), false);
        var strIn = new PinSignature("String", PinKind.Data, new TypeKey("System.String"), false);
        var v3A   = new PinSignature("A",      PinKind.Data, new TypeKey("System.Numerics.Vector3"), false);
        var v3B   = new PinSignature("B",      PinKind.Data, new TypeKey("System.Numerics.Vector3"), false);
        var v3R   = new PinSignature("Result", PinKind.Data, new TypeKey("System.Numerics.Vector3"), false);
        var dly   = new PinSignature("Duration", PinKind.Data, new TypeKey("System.Single"), false);
        var trueBranch  = new PinSignature("True",  PinKind.Exec, null, false);
        var falseBranch = new PinSignature("False", PinKind.Exec, null, false);
        var thenBranch  = new PinSignature("Then",  PinKind.Exec, null, false);
        var completed   = new PinSignature("Completed", PinKind.Exec, null, false);

        var entries = new List<NodeCatalogEntry>
        {
            // ── Events ──────────────────────────────────────────────────────────
            E("Event.BeginPlay", "Begin Play",   "Events",        [],  [execO]),
            E("Event.Tick",      "Tick",          "Events",        [],  [execO, F("Delta Time", "System.Single")]),
            E("Event.EndPlay",   "End Play",      "Events",        [],  [execO]),

            // ── Flow Control ────────────────────────────────────────────────────
            E("Flow.Branch",     "Branch",         "Flow Control", [exec, boolC], [trueBranch, falseBranch]),
            E("Flow.Sequence",   "Sequence",        "Flow Control", [exec],  [thenBranch, thenBranch, thenBranch]),
            E("Flow.ForLoop",    "For Loop",        "Flow Control", [exec, F("First Index","System.Int32"), F("Last Index","System.Int32")],
                                                                     [thenBranch, F("Index","System.Int32"), completed]),
            E("Flow.WhileLoop",  "While Loop",      "Flow Control", [exec, boolC], [thenBranch, completed]),
            E("Flow.Delay",      "Delay",           "Flow Control", [exec, dly],   [completed], isLatent: true),

            // ── Math / Float ─────────────────────────────────────────────────────
            E("Math.Add",        "Float + Float",   "Math",         [fltA, fltB], [fltR], isPure: true),
            E("Math.Subtract",   "Float - Float",   "Math",         [fltA, fltB], [fltR], isPure: true),
            E("Math.Multiply",   "Float × Float",   "Math",         [fltA, fltB], [fltR], isPure: true),
            E("Math.Divide",     "Float ÷ Float",   "Math",         [fltA, fltB], [fltR], isPure: true),
            E("Math.Abs",        "Abs (Float)",      "Math",         [F("Value","System.Single")], [fltR], isPure: true),
            E("Math.Clamp",      "Clamp (Float)",    "Math",         [F("Value","System.Single"), F("Min","System.Single"), F("Max","System.Single")], [fltR], isPure: true),
            E("Math.Lerp",       "Lerp (Float)",     "Math",         [fltA, fltB, F("Alpha","System.Single")], [fltR], isPure: true),
            E("Math.Sin",        "Sin",              "Math/Trig",    [F("Angle","System.Single")], [fltR], isPure: true),
            E("Math.Cos",        "Cos",              "Math/Trig",    [F("Angle","System.Single")], [fltR], isPure: true),

            // ── Math / Int ───────────────────────────────────────────────────────
            E("Math.AddInt",     "Int + Int",        "Math/Int",     [intA, intB], [intR], isPure: true),
            E("Math.SubInt",     "Int - Int",        "Math/Int",     [intA, intB], [intR], isPure: true),
            E("Math.MulInt",     "Int × Int",        "Math/Int",     [intA, intB], [intR], isPure: true),

            // ── Vector ───────────────────────────────────────────────────────────
            E("Math.AddVec",     "Vec3 + Vec3",      "Math/Vector",  [v3A, v3B], [v3R], isPure: true),
            E("Math.MulVec",     "Vec3 × Float",     "Math/Vector",  [v3A, fltB], [v3R], isPure: true),
            E("Math.DotProduct", "Dot Product",       "Math/Vector",  [v3A, v3B], [fltR], isPure: true),
            E("Math.CrossProduct","Cross Product",    "Math/Vector",  [v3A, v3B], [v3R], isPure: true),
            E("Math.Normalize",  "Normalize",         "Math/Vector",  [v3A], [v3R], isPure: true),
            E("Math.Length",     "Vector Length",     "Math/Vector",  [v3A], [fltR], isPure: true),

            // ── Logic ────────────────────────────────────────────────────────────
            E("Logic.And",       "Boolean AND",      "Logic",        [boolV, boolV], [F("Result","System.Boolean")], isPure: true),
            E("Logic.Or",        "Boolean OR",       "Logic",        [boolV, boolV], [F("Result","System.Boolean")], isPure: true),
            E("Logic.Not",       "Boolean NOT",      "Logic",        [boolV],         [F("Result","System.Boolean")], isPure: true),

            // ── Utility ──────────────────────────────────────────────────────────
            E("Util.Print",      "Print String",     "Utility",      [exec, strIn],    [execO]),
            E("Util.SetVar",     "Set Variable",     "Utility",      [exec, F("Value","System.Single")], [execO]),
            E("Util.GetVar",     "Get Variable",     "Utility",      [], [F("Value","System.Single")], isPure: true),
            E("Util.Cast",       "Cast to Float",    "Utility",      [intA], [fltR], isPure: true),

            // â”€â”€ Demo Shapes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            E("Demo.Shapes", "Container Shapes", "Demo",
              [
                  F("Single Item", "System.Single"),
                  F("Float Array", "System.Single"),
                  F("String Map", "System.String"),
                  F("Entity Set", "System.String")
              ],
              [
                  F("Output Array", "System.Single")
              ], isPure: true),
        };

        All = entries;

        Categories = new List<NodeCategoryDescriptor>
        {
            new("Events",       "Events",        null),
            new("Flow Control", "Flow Control",  null),
            new("Math",         "Math",          null),
            new("Math/Trig",    "Trigonometry",  null),
            new("Math/Int",     "Integer Math",  null),
            new("Math/Vector",  "Vector Math",   null),
            new("Logic",        "Logic",         null),
            new("Utility",      "Utility",       null),
            new("Demo",         "Demo",          null),
        };
    }

    public IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q)
    {
        var text = q.Text;
        return All.Where(e =>
            (text.Length == 0 || e.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)
                              || e.Kind.Id.Contains(text, StringComparison.OrdinalIgnoreCase)
                              || (e.CategoryPath?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))
            && (q.CategoryFilter is null || (e.CategoryPath?.StartsWith(q.CategoryFilter, StringComparison.OrdinalIgnoreCase) ?? false))
            && (!q.IncludeDeprecated ? !e.IsDeprecated : true)
        ).ToList();
    }

    public IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q)
    {
        var baseResults = Query(new NodeSearchQuery(q.Text));
        var targetDir = q.SourceDirection == PinDirection.Output
            ? PinDirection.Input
            : PinDirection.Output;

        return baseResults.Where(entry =>
        {
            var targetPins = targetDir == PinDirection.Input ? entry.Inputs : entry.Outputs;
            return targetPins.Any(p =>
                p.Kind == q.SourceKind &&
                (q.SourceKind == PinKind.Exec || p.Type == q.SourceType));
        }).ToList();
    }

    // Helpers
    private static NodeCatalogEntry E(string kindId, string name, string cat,
        IReadOnlyList<PinSignature> inputs, IReadOnlyList<PinSignature> outputs,
        bool isPure = false, bool isLatent = false)
        => new(new NodeKindKey(kindId), name, null, cat,
               Array.Empty<string>(), null, isPure, isLatent, false, inputs, outputs);

    private static PinSignature F(string label, string typeId)
        => new(label, PinKind.Data, new TypeKey(typeId), false);
}
