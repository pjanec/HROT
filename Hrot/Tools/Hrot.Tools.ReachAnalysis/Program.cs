// ⭐ A-2 (exp/threaded-analysis) — STATIC REACHABILITY: which fields (instance + static) the SIMULATION side and the PRESENTATION side each
// read and write, from declared roots. An INSTRUMENT: it changes nothing in HROT. 📄 docs/blueprints/batches/STATUS_Threaded_Analysis_Instruments.md
//
//   dotnet run -c Release -- <solution.sln> <roots.json> <outDir>
//
// Method: every method / accessor / constructor body in the solution's non-test projects is indexed once (its callees, the fields it reads and
// writes, the methods it references as delegates). Interface and virtual dispatch is OVER-approximated to every implementation / override declared
// in SOURCE. ⚠ Stated limits: BCL interfaces and virtuals (IEnumerable, IDisposable, object.ToString …) are NOT expanded; a lambda's body counts as
// its enclosing method's (where it runs is not modelled); reflection, DI by string, and events raised through a delegate field are not followed
// beyond the delegate's creation site; writes inside constructors are "construction", not shared mutation.
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Operations;

internal sealed record RootSpec(string Side, string Type, string[] Methods);
internal sealed record RootsFile(RootSpec[] Roots, string[] WorldNamespaces, string[] SkipProjectsContaining, string[]? PresentationInTick = null, string[]? NoDispatch = null);

internal sealed class MethodNode
{
    public string Key = "";
    public string Display = "";
    public string Location = "";
    public bool IsConstructor;
    public readonly HashSet<string> Callees = new();
    public readonly HashSet<string> Reads = new();
    public readonly HashSet<string> Writes = new();
}

internal sealed class FieldInfo
{
    public string Key = "";
    public string Display = "";
    public string Location = "";
    public bool IsStatic;
    public string Namespace = "";
}

internal static class Program
{
    private static readonly ConcurrentDictionary<string, MethodNode> Methods = new();
    private static readonly ConcurrentDictionary<string, FieldInfo> Fields = new();
    private static readonly ConcurrentDictionary<string, ConcurrentBag<string>> Dispatch = new();   // a declared member → its source implementations / overrides
    private static readonly ConcurrentBag<INamedTypeSymbol> SourceTypes = new();

    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: hrot-reach <solution.sln> <roots.json> <outDir>"); return 2; }
        string sln = Path.GetFullPath(args[0]), outDir = Path.GetFullPath(args[2]);
        var roots = JsonSerializer.Deserialize<RootsFile>(File.ReadAllText(args[1]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Directory.CreateDirectory(outDir);

        PresentationInTick = new HashSet<string>(roots.PresentationInTick ?? Array.Empty<string>());
        NoDispatch = new HashSet<string>(roots.NoDispatch ?? Array.Empty<string>());
        roots = roots with { Roots = roots.Roots.Concat(PresentationInTick.Select(t => new RootSpec("ui", t, new[] { "*" }))).ToArray() };
        MSBuildLocator.RegisterDefaults();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var ws = MSBuildWorkspace.Create();
        var failures = new List<string>();
        ws.WorkspaceFailed += (_, e) => { lock (failures) failures.Add(e.Diagnostic.Message); };
        var solution = await ws.OpenSolutionAsync(sln);
        Console.WriteLine($"[reach] opened {solution.Projects.Count()} projects in {sw.Elapsed.TotalSeconds:F0} s ({failures.Count} workspace diagnostics)");

        var projects = solution.Projects.Where(p => p.Language == LanguageNames.CSharp
            && !roots.SkipProjectsContaining.Any(s => p.Name.Contains(s, StringComparison.OrdinalIgnoreCase))).ToList();
        var seenAssemblies = new ConcurrentDictionary<string, bool>();
        int errorsTotal = 0;
        foreach (var project in projects)
        {
            var comp = await project.GetCompilationAsync();
            if (comp == null) continue;
            if (!seenAssemblies.TryAdd(comp.AssemblyName ?? project.Name, true)) continue;   // a multi-targeted project is indexed once
            int errs = comp.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
            errorsTotal += errs;
            if (errs > 0) Console.WriteLine($"[reach] ⚠ {project.Name}: {errs} compile errors — its bindings may be incomplete");
            Parallel.ForEach(comp.SyntaxTrees, tree => IndexTree(comp, tree));
            foreach (var t in AllTypes(comp.Assembly.GlobalNamespace)) SourceTypes.Add(t);
        }
        Console.WriteLine($"[reach] indexed {Methods.Count} methods, {Fields.Count} fields in {sw.Elapsed.TotalSeconds:F0} s ({errorsTotal} compile errors over {projects.Count} projects)");

        BuildDispatch();

        // ⭐ a root counts only if its type is CONSTRUCTED somewhere in non-test code (a `new T(...)`, or named as a generic argument / typeof —
        //   AddSystem<T>() composition); an unregistered implementation is not part of any host. ⚠ composition by reflection / string is missed:
        //   every dropped type is listed in rootsDropped.json for review.
        var mains = Methods.Values.Where(n => n.Key.StartsWith("M:", StringComparison.Ordinal) && (n.Key.Contains(".Main(", StringComparison.Ordinal) || n.Key.EndsWith(".Main", StringComparison.Ordinal))).Select(n => n.Key).ToHashSet();
        var composed = Reach("compose", mains);
        var constructed = new HashSet<string>();
        foreach (var m in composed)
        {
            if (!Methods.TryGetValue(m, out var n)) continue;
            foreach (var c in n.Callees) if (c.StartsWith("M:", StringComparison.Ordinal) && c.Contains(".#ctor", StringComparison.Ordinal)) constructed.Add(c[2..c.IndexOf(".#ctor", StringComparison.Ordinal)]);
            if (Mentions.TryGetValue(m, out var ts)) constructed.UnionWith(ts);
        }
        Console.WriteLine($"[reach] composition: {mains.Count} entry points (Main) → {composed.Count} methods, {constructed.Count} types constructed");
        var dropped = new SortedSet<string>();
        var rootSets = new Dictionary<string, HashSet<string>>();
        var rootReport = new List<object>();
        foreach (var r in roots.Roots)
        {
            var found = RootMethods(r, constructed, dropped).ToList();
            rootReport.Add(new { r.Side, r.Type, r.Methods, Found = found.Count });
            if (!rootSets.TryGetValue(r.Side, out var set)) rootSets[r.Side] = set = new HashSet<string>();
            set.UnionWith(found);
        }
        File.WriteAllLines(Path.Combine(outDir, "rootsDropped.txt"), dropped);
        Console.WriteLine($"[reach] {dropped.Count} root types never constructed in non-test code — dropped (rootsDropped.txt)");
        var reach = rootSets.ToDictionary(kv => kv.Key, kv => Reach(kv.Key, kv.Value));
        foreach (var kv in reach) Console.WriteLine($"[reach] side {kv.Key}: {rootSets[kv.Key].Count} roots → {kv.Value.Count} reachable methods");

        // per side: field → (read witnesses, write witnesses), construction writes excluded
        var access = reach.ToDictionary(kv => kv.Key, kv => Accesses(kv.Value));
        var sides = access.Keys.OrderBy(k => k).ToArray();
        var rows = new List<Dictionary<string, object>>();
        if (sides.Contains("sim") && sides.Contains("ui"))
        {
            var sim = access["sim"]; var ui = access["ui"];
            foreach (var f in sim.Keys.Union(ui.Keys))
            {
                sim.TryGetValue(f, out var s); ui.TryGetValue(f, out var u);
                bool simW = s?.Writes.Count > 0, uiW = u?.Writes.Count > 0, simR = s?.Reads.Count > 0, uiR = u?.Reads.Count > 0;
                if (!((simW && (uiR || uiW)) || (uiW && (simR || simW)))) continue;
                var fi = Fields.TryGetValue(f, out var x) ? x : new FieldInfo { Key = f, Display = f };
                bool world = roots.WorldNamespaces.Any(ns => fi.Namespace.StartsWith(ns, StringComparison.Ordinal));
                rows.Add(new Dictionary<string, object>
                {
                    ["field"] = fi.Display, ["static"] = fi.IsStatic, ["namespace"] = fi.Namespace, ["declared"] = fi.Location, ["world"] = world,
                    ["direction"] = simW && uiW ? "both write" : simW ? "sim writes, ui reads" : "ui writes, sim reads",
                    ["simWriters"] = Witness(s?.Writes), ["simReaders"] = Witness(s?.Reads), ["uiWriters"] = Witness(u?.Writes), ["uiReaders"] = Witness(u?.Reads),
                    ["simPath"] = WitnessPath("sim", simW ? s!.Writes : s?.Reads), ["uiPath"] = WitnessPath("ui", uiR ? u!.Reads : u?.Writes),
                });
            }
        }
        rows.Sort((a, b) => string.CompareOrdinal((string)a["namespace"] + a["field"], (string)b["namespace"] + b["field"]));

        var result = new
        {
            solution = sln, seconds = sw.Elapsed.TotalSeconds, projects = projects.Count, compileErrors = errorsTotal,
            methods = Methods.Count, fields = Fields.Count, roots = rootReport,
            reachable = reach.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
            sharedFields = rows.Count(r => !(bool)r["world"]), worldFields = rows.Count(r => (bool)r["world"]),
            rows,
        };
        File.WriteAllText(Path.Combine(outDir, "reach.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        WriteMarkdown(Path.Combine(outDir, "reach.md"), rows);
        Console.WriteLine($"[reach] {rows.Count} fields cross the line ({result.sharedFields} outside the world, {result.worldFields} world internals) → {outDir}");
        return 0;
    }

    private static List<string> Witness(List<string>? w) => w == null ? new List<string>() : w.Take(3).Select(x => x.Split('\u0001')[0]).ToList();
    private static string WitnessPath(string side, List<string>? w) => w == null || w.Count == 0 ? "" : PathTo(side, w[0].Split('\u0001')[1]);

    private sealed class Acc { public readonly List<string> Reads = new(); public readonly List<string> Writes = new(); }

    private static Dictionary<string, Acc> Accesses(HashSet<string> reachable)
    {
        var d = new Dictionary<string, Acc>();
        foreach (var m in reachable)
        {
            if (!Methods.TryGetValue(m, out var n)) continue;
            string who = $"{n.Display} ({n.Location})\u0001{m}";
            foreach (var f in n.Reads) { if (!d.TryGetValue(f, out var a)) d[f] = a = new Acc(); if (a.Reads.Count < 3) a.Reads.Add(who); }
            if (n.IsConstructor) continue;   // construction is not shared mutation
            foreach (var f in n.Writes) { if (!d.TryGetValue(f, out var a)) d[f] = a = new Acc(); if (a.Writes.Count < 3) a.Writes.Add(who); }
        }
        return d;
    }

    private static readonly Dictionary<string, Dictionary<string, string?>> Parents = new();   // side → method → the method it was reached from (null = a root)

    private static HashSet<string> Reach(string side, HashSet<string> roots)
    {
        var parent = new Dictionary<string, string?>();
        foreach (var r in roots) parent[r] = null;
        var q = new Queue<string>(roots);
        while (q.Count > 0)
        {
            var m = q.Dequeue();
            if (Methods.TryGetValue(m, out var n))
                foreach (var c in n.Callees) Visit(c, m);
            if (Dispatch.TryGetValue(m, out var impls) && !NoDispatch.Contains(m))
                foreach (var c in impls) Visit(c, m + " ⇢dispatch");
        }
        Parents[side] = parent;
        return new HashSet<string>(parent.Keys);
        void Visit(string c, string from)
        {
            if (!parent.ContainsKey(c)) { parent[c] = from; q.Enqueue(c); }
            // constructing a type runs its field / property initialisers, also when its constructor is implicit (no node of its own)
            int i = c.IndexOf(".#ctor", StringComparison.Ordinal);
            if (c.StartsWith("M:", StringComparison.Ordinal) && i > 0)
            {
                string init = "INIT:T:" + c[2..i];
                if (!parent.ContainsKey(init)) { parent[init] = c; q.Enqueue(init); }
            }
        }
    }

    // the call path from a root to a method — what makes a crossing explainable (⇢dispatch marks an over-approximated interface / virtual step)
    private static string PathTo(string side, string method)
    {
        var parent = Parents[side];
        var parts = new List<string>();
        string? cur = method;
        for (int i = 0; cur != null && i < 12; i++)
        {
            bool viaDispatch = cur.EndsWith(" ⇢dispatch", StringComparison.Ordinal);
            string k = viaDispatch ? cur[..^" ⇢dispatch".Length] : cur;
            parts.Add((Methods.TryGetValue(k, out var n) ? n.Display : k) + (viaDispatch ? " ⇢" : ""));
            cur = parent.TryGetValue(k, out var p) ? p : null;
        }
        parts.Reverse();
        return string.Join(" → ", parts);
    }

    private static IEnumerable<string> RootMethods(RootSpec r, HashSet<string> constructed, SortedSet<string> dropped)
    {
        foreach (var t in SourceTypes)
        {
            if (!Matches(t, r.Type)) continue;
            if (!t.IsStatic && !t.IsAbstract && !constructed.Contains(DocName(t))) { dropped.Add(Full(t)); continue; }
            // ⭐ §13.5: presentation that runs INSIDE the tick today is counted on the PRESENTATION side (it moves out first, S-1)
            if (r.Side == "sim" && PresentationInTick.Contains(Full(t))) continue;
            foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
            {
                if (m.IsAbstract) continue;
                string name = m.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet ? m.AssociatedSymbol!.Name : m.Name;
                if (r.Methods.Length == 0 || r.Methods.Contains(name) || r.Methods.Contains("*")) yield return Key(m);
            }
        }
    }

    private static bool Matches(INamedTypeSymbol t, string full)
    {
        if (Full(t) == full) return true;
        if (t.AllInterfaces.Any(i => Full(i.OriginalDefinition) == full)) return true;
        for (var b = t.BaseType; b != null; b = b.BaseType) if (Full(b.OriginalDefinition) == full) return true;
        return false;
    }

    private static HashSet<string> PresentationInTick = new();
    private static HashSet<string> NoDispatch = new();   // member doc ids whose dispatch is NOT expanded (systems are roots of their own; a group's Execute would re-add every system)

    private static string DocName(INamedTypeSymbol t) => (t.OriginalDefinition.GetDocumentationCommentId() ?? "T:" + Full(t))[2..];

    private static readonly ConcurrentDictionary<string, bool> TypeMentions = new();   // types named in typeof(T) or as a generic type argument
    private static readonly ConcurrentDictionary<string, HashSet<string>> Mentions = new();   // method → the types it names that way
    private static void Mention(MethodNode node, INamedTypeSymbol t) { TypeMentions[DocName(t)] = true; var set = Mentions.GetOrAdd(node.Key, _ => new()); lock (set) set.Add(DocName(t)); }

    private static string Full(ITypeSymbol t) => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");

    private static void BuildDispatch()
    {
        foreach (var t in SourceTypes)
        {
            foreach (var i in t.AllInterfaces)
                foreach (var im in i.GetMembers().OfType<IMethodSymbol>())
                {
                    if (!InSource(im)) continue;   // ⚠ BCL interfaces are not expanded (stated limit)
                    if (t.FindImplementationForInterfaceMember(im) is IMethodSymbol impl && InSource(impl))
                        Dispatch.GetOrAdd(Key(im.OriginalDefinition), _ => new()).Add(Key(impl.OriginalDefinition));
                }
            foreach (var m in t.GetMembers().OfType<IMethodSymbol>())
                for (var o = m.OverriddenMethod; o != null; o = o.OverriddenMethod)
                    if (InSource(o)) Dispatch.GetOrAdd(Key(o.OriginalDefinition), _ => new()).Add(Key(m.OriginalDefinition));
        }
        Console.WriteLine($"[reach] dispatch: {Dispatch.Count} source members with implementations / overrides");
    }

    private static bool InSource(ISymbol s) => s.Locations.Any(l => l.IsInSource);

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns)
    {
        foreach (var t in ns.GetTypeMembers()) foreach (var x in WithNested(t)) if (InSource(x)) yield return x;
        foreach (var n in ns.GetNamespaceMembers()) foreach (var t in AllTypes(n)) yield return t;
    }

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol t)
    {
        yield return t;
        foreach (var n in t.GetTypeMembers()) foreach (var x in WithNested(n)) yield return x;
    }

    private static string Key(ISymbol s) => s.OriginalDefinition.GetDocumentationCommentId() ?? s.OriginalDefinition.ToDisplayString();

    private static string Loc(ISymbol s)
    {
        var l = s.Locations.FirstOrDefault(x => x.IsInSource);
        if (l == null) return "";
        var span = l.GetLineSpan();
        string path = span.Path;
        int i = path.IndexOf("/HROT/", StringComparison.Ordinal);
        return (i >= 0 ? path[(i + 6)..] : path) + ":" + (span.StartLinePosition.Line + 1);
    }

    private static void IndexTree(Compilation comp, SyntaxTree tree)
    {
        var model = comp.GetSemanticModel(tree);
        var root = tree.GetRoot();
        // field / property initialisers run with the constructors: one synthetic node per type, called from each of its constructors
        foreach (var tdecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(tdecl) is not INamedTypeSymbol tsym) continue;
            var init = new MethodNode { Key = "INIT:" + Key(tsym), Display = tsym.Name + ".<init>", Location = Loc(tsym), IsConstructor = true };
            foreach (var m in tdecl.Members)
            {
                EqualsValueClauseSyntax? ev = m switch
                {
                    FieldDeclarationSyntax f => null,
                    PropertyDeclarationSyntax pd => pd.Initializer,
                    _ => null,
                };
                var clauses = m is FieldDeclarationSyntax fd ? fd.Declaration.Variables.Select(v => v.Initializer).Where(i => i != null).ToList() : new List<EqualsValueClauseSyntax?> { ev };
                foreach (var c in clauses)
                {
                    if (c == null) continue;
                    IOperation? op = null;
                    try { op = model.GetOperation(c.Value); } catch { }
                    if (op != null) foreach (var o in op.DescendantsAndSelf()) Visit(o, init);
                }
            }
            if (init.Callees.Count > 0 || init.Reads.Count > 0 || init.Writes.Count > 0) Methods.AddOrUpdate(init.Key, init, (_, old) => { old.Callees.UnionWith(init.Callees); old.Reads.UnionWith(init.Reads); return old; });
        }
        foreach (var decl in root.DescendantNodes().Where(n => n is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax or IndexerDeclarationSyntax }))
        {
            IMethodSymbol? sym = decl switch
            {
                ArrowExpressionClauseSyntax a => (model.GetDeclaredSymbol(a.Parent!) as IPropertySymbol)?.GetMethod,
                _ => model.GetDeclaredSymbol(decl) as IMethodSymbol,
            };
            if (sym == null) continue;
            var node = new MethodNode
            {
                Key = Key(sym), Display = sym.ContainingType.Name + "." + (sym.AssociatedSymbol?.Name ?? sym.Name) + (sym.MethodKind == MethodKind.PropertySet ? ".set" : ""),
                Location = Loc(sym), IsConstructor = sym.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor,
            };
            if (node.IsConstructor) node.Callees.Add("INIT:" + Key(sym.ContainingType));
            SyntaxNode? body = decl switch
            {
                BaseMethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
                AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody,
                ArrowExpressionClauseSyntax a => a,
                _ => null,
            };
            if (body != null)
            {
                IOperation? op = null;
                try { op = model.GetOperation(body); } catch { }
                if (op != null) foreach (var o in op.DescendantsAndSelf()) Visit(o, node);
                // field initialisers of the type run with the constructors: counted as construction
            }
            Methods[node.Key] = node;
        }
    }

    private static void Visit(IOperation o, MethodNode node)
    {
        switch (o)
        {
            case IInvocationOperation inv:
                node.Callees.Add(Key(inv.TargetMethod));
                // ⭐ a mutating call on a collection HELD IN A FIELD (`_map.Add(…)`, `_list.Clear()`) mutates the object the field names: a write of that field
                if (IsCollectionMutator(inv.TargetMethod) && FieldOf(inv.Instance) is ISymbol held) Field(node, held, true, held.IsStatic);
                foreach (var ta in inv.TargetMethod.TypeArguments) if (ta is INamedTypeSymbol nt && InSource(nt)) Mention(node, nt);   // AddSystem<T>() / Register<T>()
                break;
            case IObjectCreationOperation oc when oc.Constructor != null: node.Callees.Add(Key(oc.Constructor)); break;
            case IMethodReferenceOperation mr: node.Callees.Add(Key(mr.Method)); break;   // a delegate: it may run — over-approximated
            case ITypeOfOperation to when to.TypeOperand is INamedTypeSymbol tt && InSource(tt): Mention(node, tt); break;
            case IArrayElementReferenceOperation ae when IsWrite(ae) && FieldOf(ae.ArrayReference) is ISymbol arr:
                Field(node, arr, true, arr.IsStatic);   // `_slots[i] = …` writes the array the field holds
                break;
            case IPropertyReferenceOperation pr when pr.Property.IsIndexer && IsWrite(pr) && FieldOf(pr.Instance) is ISymbol coll:
                Field(node, coll, true, coll.IsStatic);   // `_map[key] = …`
                if (pr.Property.SetMethod != null) node.Callees.Add(Key(pr.Property.SetMethod));
                break;
            case IPropertyReferenceOperation pr:
                {
                    var p = pr.Property;
                    bool write = IsWrite(pr) && !InInitializer(pr) && !(p.SetMethod?.IsInitOnly ?? false);
                    if (IsAutoProperty(p)) Field(node, p, write, p.IsStatic);
                    else
                    {
                        if (write && p.SetMethod != null) node.Callees.Add(Key(p.SetMethod));
                        if (!write || IsReadWrite(pr)) { if (p.GetMethod != null) node.Callees.Add(Key(p.GetMethod)); }
                    }
                    break;
                }
            case IFieldReferenceOperation fr:
                {
                    var f = fr.Field;
                    if (f.IsConst) break;
                    Field(node, f, IsWrite(fr) && !InInitializer(fr), f.IsStatic);
                    if (IsReadWrite(fr)) Field(node, f, false, f.IsStatic);
                    break;
                }
        }
    }

    private static readonly HashSet<string> Mutators = new(StringComparer.Ordinal)
    {
        "Add", "AddRange", "Remove", "RemoveAt", "RemoveAll", "RemoveRange", "RemoveWhere", "Clear", "Insert", "InsertRange", "Sort", "Reverse",
        "TryAdd", "TryRemove", "TryUpdate", "GetOrAdd", "AddOrUpdate", "Enqueue", "Dequeue", "TryDequeue", "Push", "Pop", "TryPop", "TrimExcess",
        "EnsureCapacity", "UnionWith", "ExceptWith", "IntersectWith", "SymmetricExceptWith", "Set", "SetAll", "CopyTo",
    };

    // a BCL collection's mutating member (System.Collections.*, arrays' Array.Clear / Fill are static and not tracked)
    private static bool IsCollectionMutator(IMethodSymbol m)
    {
        if (!Mutators.Contains(m.Name) || m.IsStatic) return false;
        var ns = m.ContainingType.ContainingNamespace?.ToDisplayString() ?? "";
        return ns.StartsWith("System.Collections", StringComparison.Ordinal);
    }

    // the field (or auto-property) an expression reads directly — the receiver of a mutation
    private static ISymbol? FieldOf(IOperation? o)
    {
        while (o is IConversionOperation c) o = c.Operand;
        return o switch
        {
            IFieldReferenceOperation f when !f.Field.IsConst => f.Field,
            IPropertyReferenceOperation p when IsAutoProperty(p.Property) => p.Property,
            _ => null,
        };
    }

    private static bool IsAutoProperty(IPropertySymbol p)
        => p.DeclaringSyntaxReferences.Length > 0 && p.DeclaringSyntaxReferences.All(r => r.GetSyntax() is PropertyDeclarationSyntax pd
            && pd.ExpressionBody == null && pd.AccessorList != null && pd.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null));

    private static void Field(MethodNode node, ISymbol member, bool write, bool isStatic)
    {
        if (!InSource(member)) return;
        // a struct's INSTANCE field is not shared state by itself: the copy lives in a local, an array element or a field — the container is what is shared
        if (!isStatic && member.ContainingType.IsValueType) return;
        string key = Key(member);
        Fields.GetOrAdd(key, _ => new FieldInfo
        {
            Key = key, Display = member.ContainingType.Name + "." + member.Name, Location = Loc(member), IsStatic = isStatic,
            Namespace = member.ContainingType.ContainingNamespace?.ToDisplayString() ?? "",
        });
        (write ? node.Writes : node.Reads).Add(key);
    }

    // a write: the target of an assignment, ++ / --, a ref / out argument, or a ref local taken from it; a mutating call on a struct field is not tracked
    private static bool IsWrite(IOperation o)
    {
        var p = o.Parent;
        return p switch
        {
            ISimpleAssignmentOperation a => a.Target == o,
            ICompoundAssignmentOperation c => c.Target == o,
            ICoalesceAssignmentOperation c => c.Target == o,
            IIncrementOrDecrementOperation => true,
            IArgumentOperation arg => arg.Parameter?.RefKind is RefKind.Ref or RefKind.Out,
            _ => false,
        };
    }

    // a write inside `new T { X = … }` / `with { … }` sets a fresh object: construction, not shared mutation
    private static bool InInitializer(IOperation o)
    {
        for (var p = o.Parent; p != null; p = p.Parent)
        {
            if (p is IObjectOrCollectionInitializerOperation or IWithOperation) return true;
            if (p is IBlockOperation or IMethodBodyOperation) return false;
        }
        return false;
    }

    private static bool IsReadWrite(IOperation o)
        => o.Parent is ICompoundAssignmentOperation or ICoalesceAssignmentOperation or IIncrementOrDecrementOperation
           || (o.Parent is IArgumentOperation arg && arg.Parameter?.RefKind == RefKind.Ref);

    private static void WriteMarkdown(string path, List<Dictionary<string, object>> rows)
    {
        using var w = new StreamWriter(path);
        w.WriteLine("| field | static | direction | declared | a sim writer / reader | a UI reader / writer | sim path (⇢ = dispatch) | UI path |");
        w.WriteLine("|---|---|---|---|---|---|---|---|");
        foreach (var r in rows.Where(r => !(bool)r["world"]))
        {
            string first(string k) => ((List<string>)r[k]).FirstOrDefault() ?? "";
            string sim = first("simWriters"); if (sim == "") sim = first("simReaders");
            string ui = first("uiReaders"); if (ui == "") ui = first("uiWriters");
            w.WriteLine($"| `{r["field"]}` | {((bool)r["static"] ? "static" : "")} | {r["direction"]} | `{r["declared"]}` | {sim.Replace("|", "\\|")} | {ui.Replace("|", "\\|")} | {((string)r["simPath"]).Replace("|", "\\|")} | {((string)r["uiPath"]).Replace("|", "\\|")} |");
        }
    }
}
