// ⭐ CE-4445 M-1 — every site that READS a managed component (a class / record / interface component) and then MUTATES the object it got IN PLACE,
// instead of building a new instance and SetManagedComponent (CLAUDE.md: a managed component is immutable and replaced whole). Today a managed read
// stamps the chunk version, so such a mutation is recorded by accident; once reads stop stamping (R-4210) it would not be.
//
//   dotnet run --project Hrot/Tools/Hrot.Tools.ManagedMutationAudit -c Release -- HROT.sln <out.md>
//
// How: the semantic model of every non-test project of the solution. A READ is an invocation of GetComponent / GetComponentRO /
// GetManagedComponentRO / GetManagedComponent / TryGetComponent / GetSingletonManaged / GetSingleton whose type argument is a reference type. The
// object it returns is TRACKED: the local it is stored in (var x = …; x = …; out var x) or the member chain hanging off the call itself. A MUTATION
// is an assignment / ++ / -- whose target is a member chain rooted at a tracked object, a collection mutator (Add, Remove, Clear, …) called on such
// a chain, a chain passed by ref, or (reported separately, weaker) an instance method of the component type called on it.
// ⚠ Limits: one method at a time (an object handed to another method is not followed); aliases through a second local are followed one level.
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

internal static class Program
{
    private static readonly HashSet<string> Reads = new(StringComparer.Ordinal)
    {
        "GetComponent", "GetComponentRO", "GetManagedComponentRO", "GetManagedComponent", "TryGetComponent", "GetSingletonManaged", "GetSingleton",
        "TryGetManagedComponent", "GetManagedComponentRefRO",
    };

    private static readonly HashSet<string> Mutators = new(StringComparer.Ordinal)
    {
        "Add", "AddRange", "Remove", "RemoveAt", "RemoveAll", "RemoveRange", "RemoveWhere", "Clear", "Insert", "InsertRange", "Enqueue", "Dequeue",
        "Push", "Pop", "TryAdd", "TryRemove", "TryDequeue", "TryPop", "AddOrUpdate", "GetOrAdd", "Sort", "Reverse", "SetAll", "UnionWith",
        "ExceptWith", "IntersectWith", "SymmetricExceptWith", "Fill", "EnsureCapacity", "TrimExcess", "AddFirst", "AddLast", "RemoveFirst",
        "RemoveLast", "Set",
    };

    private static readonly string[] SkipProjects = { "Test", "Benchmark", "Example", "Sample", "Demo", "Showcase" };

    private sealed record Hit(string File, int Line, string Component, string Read, string Mutation, string Kind, string Method);

    private static readonly ConcurrentBag<Hit> Hits = new();
    private static int s_reads;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: hrot-mutation-audit <solution.sln> <out.md>"); return 2; }
        string sln = Path.GetFullPath(args[0]), outPath = Path.GetFullPath(args[1]);
        string root = Path.GetDirectoryName(sln)!;
        MSBuildLocator.RegisterDefaults();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var ws = MSBuildWorkspace.Create();
        var solution = await ws.OpenSolutionAsync(sln);
        Console.WriteLine($"[audit] opened {solution.Projects.Count()} projects in {sw.Elapsed.TotalSeconds:F0} s");
        var seen = new HashSet<string>();
        int projects = 0, errors = 0;
        foreach (var project in solution.Projects)
        {
            if (project.Language != LanguageNames.CSharp || SkipProjects.Any(s => project.Name.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            var comp = await project.GetCompilationAsync();
            if (comp == null || !seen.Add(comp.AssemblyName ?? project.Name)) continue;
            projects++;
            int errs = comp.GetDiagnostics().Count(d => d.Severity == DiagnosticSeverity.Error);
            errors += errs;
            if (errs > 0) Console.WriteLine($"[audit] ⚠ {project.Name}: {errs} compile errors");
            Parallel.ForEach(comp.SyntaxTrees, tree => Scan(comp, tree, root));
        }
        var hits = Hits.Distinct().OrderBy(h => h.Kind).ThenBy(h => h.File).ThenBy(h => h.Line).ToList();
        var sb = new StringBuilder();
        sb.Append($"# Managed component READ then IN-PLACE MUTATION — {hits.Count} sites\n\n");
        sb.Append($"`hrot-mutation-audit` over `{Path.GetFileName(sln)}`: {projects} projects ({errors} compile errors), {s_reads} managed reads tracked, {sw.Elapsed.TotalSeconds:F0} s.\n\n");
        foreach (var kind in hits.Select(h => h.Kind).Distinct())
        {
            var group = hits.Where(h => h.Kind == kind).ToList();
            sb.Append($"## {kind} — {group.Count}\n\n| file:line | component | read | mutation | method |\n|---|---|---|---|---|\n");
            foreach (var h in group)
                sb.Append($"| `{h.File}:{h.Line}` | `{h.Component}` | `{h.Read}` | `{Esc(h.Mutation)}` | `{h.Method}` |\n");
            sb.Append('\n');
        }
        File.WriteAllText(outPath, sb.ToString());
        Console.WriteLine($"[audit] {hits.Count} sites ({hits.Count(h => h.Kind.StartsWith("A"))} direct mutations) → {outPath}");
        return 0;
    }

    private static string Esc(string s) => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");

    private static void Scan(Compilation comp, SyntaxTree tree, string root)
    {
        SemanticModel model;
        try { model = comp.GetSemanticModel(tree); } catch { return; }
        string file = Path.GetRelativePath(root, tree.FilePath).Replace('\\', '/');
        foreach (var inv in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            string? name = inv.Expression switch
            {
                MemberAccessExpressionSyntax m => (m.Name as GenericNameSyntax)?.Identifier.Text ?? m.Name.Identifier.Text,
                GenericNameSyntax g => g.Identifier.Text,
                _ => null,
            };
            if (name == null || !Reads.Contains(name)) continue;
            var sym = model.GetSymbolInfo(inv).Symbol as IMethodSymbol ?? model.GetSymbolInfo(inv).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
            if (sym == null || sym.TypeArguments.Length != 1) continue;
            var t = sym.TypeArguments[0];
            if (!t.IsReferenceType || t.SpecialType == SpecialType.System_String || t is ITypeParameterSymbol) continue;
            var owner = sym.ContainingType?.Name ?? "";
            if (!(owner.Contains("Repository") || owner.Contains("View") || owner.Contains("World") || owner.Contains("Shim") || owner.Contains("Extensions") || owner.Contains("PatchContext"))) continue;
            Interlocked.Increment(ref s_reads);
            var body = (SyntaxNode?)inv.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>() ?? inv.FirstAncestorOrSelf<LocalFunctionStatementSyntax>()
                       ?? (SyntaxNode?)inv.FirstAncestorOrSelf<AccessorDeclarationSyntax>() ?? inv.FirstAncestorOrSelf<LambdaExpressionSyntax>();
            if (body == null) continue;
            string method = (inv.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>() as MethodDeclarationSyntax)?.Identifier.Text
                            ?? inv.FirstAncestorOrSelf<TypeDeclarationSyntax>()?.Identifier.Text ?? "?";
            string comp1 = t.Name;
            string read = name + "<" + t.Name + ">";

            // the chain on the call itself: repo.GetComponent<T>(e).X = …
            CheckUse(inv, model, file, comp1, read, method, t, direct: true);

            // the tracked local(s)
            var locals = new List<ISymbol>();
            if (inv.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax vd } && model.GetDeclaredSymbol(vd) is { } l1) locals.Add(l1);
            if (inv.Parent is AssignmentExpressionSyntax { Left: IdentifierNameSyntax id } asg && asg.Right == inv && model.GetSymbolInfo(id).Symbol is ILocalSymbol l2) locals.Add(l2);
            if (inv.Parent is RefExpressionSyntax { Parent: EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax vd2 } } && model.GetDeclaredSymbol(vd2) is { } l3) locals.Add(l3);
            foreach (var arg in inv.ArgumentList.Arguments)
            {
                if (!arg.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) continue;
                if (arg.Expression is DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax sv } && model.GetDeclaredSymbol(sv) is { } l4) locals.Add(l4);
                else if (arg.Expression is IdentifierNameSyntax oid && model.GetSymbolInfo(oid).Symbol is ILocalSymbol l5) locals.Add(l5);
            }
            foreach (var local in locals)
                foreach (var use in body.DescendantNodes().OfType<IdentifierNameSyntax>())
                {
                    if (use.Identifier.Text != local.Name || !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(use).Symbol, local)) continue;
                    CheckUse(use, model, file, comp1, read, method, t, direct: false);
                    // one alias level: var list = x.Items;  list.Add(…)
                    if (use.Parent is MemberAccessExpressionSyntax ma && ma.Expression == use && ma.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax avd }
                        && model.GetDeclaredSymbol(avd) is ILocalSymbol alias && alias.Type.IsReferenceType && alias.Type.SpecialType != SpecialType.System_String)
                        foreach (var ause in body.DescendantNodes().OfType<IdentifierNameSyntax>())
                            if (ause.Identifier.Text == alias.Name && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(ause).Symbol, alias))
                                CheckUse(ause, model, file, comp1, read, method, t, direct: false, via: alias.Name + " = " + ma);
                }
        }
    }

    /// <summary>Climbs from a use of the tracked object through its member / element chain and classifies what the top of the chain does.</summary>
    private static void CheckUse(ExpressionSyntax start, SemanticModel model, string file, string comp, string read, string method, ITypeSymbol t, bool direct, string? via = null)
    {
        ExpressionSyntax top = start;
        int depth = 0;
        while (true)
        {
            if (top.Parent is MemberAccessExpressionSyntax ma && ma.Expression == top) { top = ma; depth++; continue; }
            if (top.Parent is ElementAccessExpressionSyntax ea && ea.Expression == top) { top = ea; depth++; continue; }
            if (top.Parent is ConditionalAccessExpressionSyntax ca && ca.Expression == top) { top = ca; depth++; continue; }
            if (top.Parent is MemberBindingExpressionSyntax mb) { top = mb; depth++; continue; }
            if (top.Parent is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } bang) { top = bang; continue; }
            if (top.Parent is ParenthesizedExpressionSyntax par) { top = par; continue; }
            break;
        }
        int line = start.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        string pre = via != null ? "(via " + via + ") " : "";
        // ① an assignment / compound assignment / ++ / -- to a member of the object (depth ≥ 1; depth 0 is the local itself being replaced)
        if (depth >= 1 && top.Parent is AssignmentExpressionSyntax asg && asg.Left == top)
        { Add(file, line, comp, read, pre + asg.ToString(), "A · field / property / element SET", method); return; }
        if (depth >= 1 && top.Parent is PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax
            && top.Parent.RawKind is (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression
                or (int)SyntaxKind.PostIncrementExpression or (int)SyntaxKind.PostDecrementExpression)
        { Add(file, line, comp, read, pre + top.Parent.ToString(), "A · field / property / element SET", method); return; }
        // ② passed by ref
        if (depth >= 1 && top.Parent is ArgumentSyntax arg && arg.RefKindKeyword.IsKind(SyntaxKind.RefKeyword))
        { Add(file, line, comp, read, pre + "ref " + top, "A · passed by ref", method); return; }
        // ③ a call on the chain: a collection mutator, or an instance method of the component type itself
        if (top is MemberAccessExpressionSyntax call && top.Parent is InvocationExpressionSyntax ci && ci.Expression == top)
        {
            string m = call.Name.Identifier.Text;
            var target = model.GetSymbolInfo(ci).Symbol as IMethodSymbol;
            if (Mutators.Contains(m) && depth >= 2)   // x.Items.Add(…) — the collection lives INSIDE the component
            { Add(file, line, comp, read, pre + Short(ci.ToString()), "A · collection mutated", method); return; }
            if (depth == 1 && target != null && !target.IsStatic && SymbolEqualityComparer.Default.Equals(target.ContainingType, t)
                && target.MethodKind == MethodKind.Ordinary && !target.IsReadOnly && !m.StartsWith("Get") && !m.StartsWith("Try") && !m.StartsWith("Is")
                && !m.StartsWith("Has") && !m.StartsWith("Find") && !m.StartsWith("Contains") && !m.StartsWith("To") && m != "Equals")
            { Add(file, line, comp, read, pre + Short(ci.ToString()), "B · an instance method of the component (may mutate — read it)", method); return; }
            if (Mutators.Contains(m) && depth == 1 && target != null && !target.IsStatic)
            { Add(file, line, comp, read, pre + Short(ci.ToString()), "A · collection mutated", method); return; }
        }
    }

    private static string Short(string s) => s.Length > 120 ? s[..117] + "…" : s;

    private static void Add(string file, int line, string comp, string read, string mutation, string kind, string method)
        => Hits.Add(new Hit(file, line, comp, read, mutation.Length > 160 ? mutation[..157] + "…" : mutation, kind, method));
}
