using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    /// <summary>
    /// <b>W1 / Batch 58 — the HSM dispatcher id space is content-addressed, last-writer-wins, and
    /// until now unguarded.</b>
    ///
    /// <para>
    /// ⛔⛔ <b>What is actually broken.</b> <c>HsmActionDispatcher.RegisterAction(ushort id, IntPtr a)</c>
    /// is literally <c>ActionTable[id] = a;</c> — <b>no guard, no diagnostic, no throw.</b> Two
    /// independent mechanisms write into that one table during a single build:
    /// <list type="bullet">
    ///   <item><b>hashed</b> — <see cref="HsmActionGenerator"/>: <c>ushort id = ComputeHash(name)</c>,
    ///   FNV-1a truncated to 16 bits, so <b>anywhere in 0…65535</b>;</item>
    ///   <item><b>counted</b> — <c>HsmBridgeEmitCore</c>: literal counters from <b>100</b> (actions)
    ///   and <b>200</b> (guards), registering <b>no-op stub bodies</b>. ⚠ That emitter is <b>LIVE</b>
    ///   (<c>HsmJsonGenerator</c>, <c>EditorSubsystem</c>) — not dead like the orchestrators.</item>
    /// </list>
    /// ⇒ 🔴🔴 <b>a real action whose name hashes into the stub window is silently replaced by a body
    /// that does nothing.</b> The HSM does not crash. It behaves correctly everywhere except one
    /// state, forever.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>Why this is an ANALYZER and not a check inside the generator</b> — measured, not assumed.
    /// The two mechanisms are <b>two different source generators over the same compilation</b>
    /// (<c>Fdp.Toolkits.Analyzers</c> and <c>Hrot.AiEditor.Generators</c>, both on
    /// <c>Hrot.AI.Behaviors</c>). ⛔ <b>A generator cannot see another generator's output</b>, so a
    /// check inside <see cref="HsmActionGenerator"/> would range over the hashed set only — exactly
    /// the blind spot <c>W3</c> describes, certified as covered. An analyzer runs over the FINAL
    /// compilation, where <b>both</b> mechanisms have already become literal
    /// <c>Register…(&lt;const&gt;, …)</c> calls, and so does a <b>third</b> the handoff does not
    /// mention: the blueprint compiler's <c>EmitAiPrimitiveRegistration</c> registers under
    /// <c>unchecked((ushort)BlueprintId)</c>, itself a hash of the asset id.
    /// </para>
    ///
    /// <para>
    /// ⚠⚠ <b><see cref="GeneratedCodeAnalysisFlags"/> is load-bearing here, not boilerplate.</b> Every
    /// id this rail exists to see lives in an <c>// &lt;auto-generated/&gt;</c> file. ⛔ The sibling
    /// <c>UtilityAuthoringAnalyzer</c> sets <see cref="GeneratedCodeAnalysisFlags.None"/> — correct for
    /// an authoring rule, and fatal for this one: it would compile, pass its own tests against
    /// hand-written fixtures, and see nothing at all in production.
    /// </para>
    ///
    /// <para>
    /// 📌 <b>Two honest limits, stated rather than papered over.</b>
    /// <list type="bullet">
    ///   <item><b>Non-constant ids are invisible.</b> An id computed at run time cannot be reasoned
    ///   about statically. Every mechanism in the tree today emits a compile-time constant, so the set
    ///   is complete as things stand — but a future <c>Register(ComputeSomething(x), …)</c> would slip
    ///   past in silence.</item>
    ///   <item><b>The scope is one compilation.</b> <c>Fhsm.Kernel</c> seeds
    ///   <c>ActionTable</c>/<c>GuardTable</c> from its own dictionary initialisers, in its own
    ///   assembly; a collision between a kernel built-in and a downstream registration is not visible
    ///   from here. ⭐ It IS visible when the kernel is the compilation being analysed, which is where
    ///   those ids are authored.</item>
    /// </list>
    /// </para>
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class HsmDispatcherIdAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>
        /// ⭐ Reserved ids, and <b>the premise is verified rather than assumed</b>.
        /// <c>HsmKernelCore</c> guards every action invocation with
        /// <c>if (…ActionId != 0 &amp;&amp; …ActionId != 0xFFFF)</c> — <c>:304</c> (entry), <c>:448</c>
        /// (activity), <c>:669</c> (exit), <c>:682</c> (transition effect), <c>:714</c> (entry on the
        /// second path) — and <c>GlobalTransitionDef:19</c> documents <c>// Effect action (0 = none)</c>.
        /// ⚠ For a GUARD, <c>0</c> is sharper still: <c>:540</c> / <c>:583</c> read
        /// <c>if (gt.GuardId == 0 || EvaluateGuard(…))</c>, so a guard that hashes to <c>0</c> is not
        /// merely skipped — <b>the transition it was protecting becomes unconditional.</b>
        /// </summary>
        private const ushort ReservedNone    = 0;
        private const ushort ReservedInvalid = 0xFFFF;

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                SharedBhuDiagnostics.BHU020_DuplicateDispatcherId,
                SharedBhuDiagnostics.BHU021_ReservedDispatcherId);

        public override void Initialize(AnalysisContext context)
        {
            // ⭐⭐ See the class comment: without this the analyzer is blind to every id it exists for.
            context.ConfigureGeneratedCodeAnalysis(
                GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(compilationCtx =>
            {
                var found = new ConcurrentBag<Registration>();

                compilationCtx.RegisterSyntaxNodeAction(
                    nodeCtx => Collect(nodeCtx, found),
                    SyntaxKind.InvocationExpression);

                compilationCtx.RegisterCompilationEndAction(endCtx => Report(endCtx, found));
            });
        }

        // ── collection ──────────────────────────────────────────────────────────

        private static void Collect(SyntaxNodeAnalysisContext ctx, ConcurrentBag<Registration> found)
        {
            var invocation = (InvocationExpressionSyntax)ctx.Node;
            if (invocation.ArgumentList.Arguments.Count < 1) return;

            // ⚠ Cheap syntactic reject first: this action runs on every invocation in the compilation.
            var simpleName = (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.ValueText
                             ?? (invocation.Expression as IdentifierNameSyntax)?.Identifier.ValueText;
            bool isGuard;
            if (simpleName == "RegisterAction")      isGuard = false;
            else if (simpleName == "RegisterGuard")  isGuard = true;
            else return;

            // ⛔ NOT every RegisterAction is this one: FastBTree's ActionRegistry has a string-keyed
            //   `RegisterAction(string, …)` that appears in the same generated files. The containing
            //   type is what separates them, so this is resolved semantically rather than by name.
            if (!(ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken).Symbol is IMethodSymbol method))
                return;
            if (method.ContainingType?.Name != "HsmActionDispatcher") return;

            var idArg = invocation.ArgumentList.Arguments[0].Expression;
            var constant = ctx.SemanticModel.GetConstantValue(idArg, ctx.CancellationToken);
            if (!constant.HasValue) return;   // see the class comment's first stated limit

            ushort id;
            try { id = Convert.ToUInt16(constant.Value); }
            catch { return; }                 // not an integral id — nothing to say about it

            found.Add(new Registration(
                isGuard: isGuard,
                id:      id,
                location: invocation.GetLocation(),
                target:  DescribeTarget(invocation)));
        }

        /// <summary>
        /// What was registered, in words the author can find. ⭐ The location is inside a generated
        /// file, so the message has to carry the identity or the diagnostic names nothing actionable.
        /// </summary>
        private static string DescribeTarget(InvocationExpressionSyntax invocation)
        {
            if (invocation.ArgumentList.Arguments.Count < 2) return "<unknown>";
            var text = invocation.ArgumentList.Arguments[1].Expression.ToString();

            // The emitted forms are `(IntPtr)(delegate*<…>)&Some.Qualified.Name` and, for the JSON
            // bridge, `…&__hsActionStub`. The name after the last '&' is the useful half.
            int amp = text.LastIndexOf('&');
            if (amp >= 0 && amp + 1 < text.Length) text = text.Substring(amp + 1);
            return text.Trim();
        }

        // ── reporting ───────────────────────────────────────────────────────────

        private static void Report(CompilationAnalysisContext ctx, ConcurrentBag<Registration> found)
        {
            // ⚠ Actions and guards are TWO tables (`ActionTable` / `GuardTable`), so an action id equal
            //   to a guard id is not a collision. Grouping by (table, id) is the correctness-relevant
            //   key; grouping by id alone would have been a false positive generator.
            foreach (var group in found.GroupBy(r => new TableKey(r.IsGuard, r.Id)))
            {
                var entries = group
                    .OrderBy(r => r.Location.SourceTree?.FilePath ?? "", StringComparer.Ordinal)
                    .ThenBy(r => r.Location.SourceSpan.Start)
                    .ToList();

                var noun = group.Key.IsGuard ? "guard" : "action";

                if (entries.Count > 1)
                {
                    // ⭐ Reported on EVERY participant, not just the loser. Which one "wins" is
                    //   registration order across two generators — not something an author can read off
                    //   the page — so naming only one would be arbitrary and would also hide the
                    //   collision from whichever file the author happens to open.
                    var who = string.Join(", ", entries.Select(e => e.Target).Distinct(StringComparer.Ordinal));
                    foreach (var e in entries)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            SharedBhuDiagnostics.BHU020_DuplicateDispatcherId,
                            e.Location, noun, Describe(group.Key.Id), who));
                    }
                }

                if (group.Key.Id == ReservedNone || group.Key.Id == ReservedInvalid)
                {
                    var why = group.Key.Id == ReservedNone
                        ? (group.Key.IsGuard
                            ? "0 means \"no guard\", and HsmKernelCore takes the transition WITHOUT "
                              + "evaluating it — the guard becomes unconditionally true"
                            : "0 means \"no action\" and HsmKernelCore skips the call")
                        : "0xFFFF is the kernel's invalid-state sentinel and HsmKernelCore skips the call";

                    foreach (var e in entries)
                    {
                        ctx.ReportDiagnostic(Diagnostic.Create(
                            SharedBhuDiagnostics.BHU021_ReservedDispatcherId,
                            e.Location, noun, Describe(group.Key.Id), why));
                    }
                }
            }
        }

        private static string Describe(ushort id) => id + " (0x" + id.ToString("X4") + ")";

        // ── data ────────────────────────────────────────────────────────────────

        private readonly struct Registration
        {
            public Registration(bool isGuard, ushort id, Location location, string target)
            {
                IsGuard  = isGuard;
                Id       = id;
                Location = location;
                Target   = target;
            }

            public bool     IsGuard  { get; }
            public ushort   Id       { get; }
            public Location Location { get; }
            public string   Target   { get; }
        }

        private readonly struct TableKey : IEquatable<TableKey>
        {
            public TableKey(bool isGuard, ushort id) { IsGuard = isGuard; Id = id; }

            public bool   IsGuard { get; }
            public ushort Id      { get; }

            public bool Equals(TableKey other) => IsGuard == other.IsGuard && Id == other.Id;
            public override bool Equals(object obj) => obj is TableKey k && Equals(k);
            public override int GetHashCode() => (IsGuard ? 0x10000 : 0) | Id;
        }
    }
}
