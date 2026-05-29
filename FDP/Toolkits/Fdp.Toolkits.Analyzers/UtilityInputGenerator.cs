using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Fdp.Toolkit.Behavior.Analyzers
{
    [Generator]
    public class UtilityInputGenerator : IIncrementalGenerator
    {
        // ---- Diagnostic descriptors -------------------------------------------

        private static readonly DiagnosticDescriptor UT0101 = SharedUtilityDiagnostics.UT0101_MissingName;
        private static readonly DiagnosticDescriptor UT0102 = SharedUtilityDiagnostics.UT0102_DuplicateName;
        private static readonly DiagnosticDescriptor UT0103 = SharedUtilityDiagnostics.UT0103_HashCollision;
        private static readonly DiagnosticDescriptor UT0110 = SharedUtilityDiagnostics.UT0110_NotStatic;
        private static readonly DiagnosticDescriptor UT0111 = SharedUtilityDiagnostics.UT0111_NotFloat;
        private static readonly DiagnosticDescriptor UT0112 = SharedUtilityDiagnostics.UT0112_WrongSignature;

        // ---- Initialize --------------------------------------------------------

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var candidateMethods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                    transform: static (ctx, _) => GetUtilityInputInfo(ctx))
                .Where(static m => m != null);

            var compilationAndMethods = context.CompilationProvider.Combine(candidateMethods.Collect());

            context.RegisterSourceOutput(
                compilationAndMethods,
                static (spc, source) => Execute(spc, source.Left, source.Right!));
        }

        // ---- Collect method information ----------------------------------------

        private static UtilityInputInfo GetUtilityInputInfo(GeneratorSyntaxContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
            if (symbol == null) return null;

            // Check for [UtilityInput] attribute
            AttributeData attr = null;
            foreach (var a in symbol.GetAttributes())
            {
                if (a.AttributeClass?.Name == "UtilityInputAttribute" ||
                    a.AttributeClass?.Name == "UtilityInput")
                {
                    attr = a;
                    break;
                }
            }
            if (attr == null) return null;

            var location = method.GetLocation();
            var methodName = symbol.Name;
            var methodFqn  = symbol.ContainingType.ToDisplayString() + "." + symbol.Name;
            var ns         = symbol.ContainingType.ContainingNamespace.ToDisplayString();

            // Extract Name from first constructor argument
            string inputName = null;
            if (attr.ConstructorArguments.Length > 0)
                inputName = attr.ConstructorArguments[0].Value?.ToString();

            // UT0101: missing Name
            if (string.IsNullOrEmpty(inputName))
            {
                return new UtilityInputInfo
                {
                    MethodName = methodName,
                    Location   = location,
                    ErrorDescriptor = UT0101,
                    ErrorArgs  = new object[] { methodName },
                };
            }

            // UT0110: not static
            if (!symbol.IsStatic)
            {
                return new UtilityInputInfo
                {
                    Name       = inputName,
                    MethodName = methodName,
                    Location   = location,
                    ErrorDescriptor = UT0110,
                    ErrorArgs  = new object[] { methodName },
                };
            }

            // UT0111: return type is not float
            if (symbol.ReturnType.SpecialType != SpecialType.System_Single)
            {
                return new UtilityInputInfo
                {
                    Name       = inputName,
                    MethodName = methodName,
                    Location   = location,
                    ErrorDescriptor = UT0111,
                    ErrorArgs  = new object[] { methodName, symbol.ReturnType.ToDisplayString() },
                };
            }

            // UT0112: wrong signature — must be exactly 1 parameter of type `in UtilityInputCtx`
            bool validSig = symbol.Parameters.Length == 1 &&
                            symbol.Parameters[0].RefKind == RefKind.In &&
                            symbol.Parameters[0].Type.Name == "UtilityInputCtx";
            if (!validSig)
            {
                return new UtilityInputInfo
                {
                    Name       = inputName,
                    MethodName = methodName,
                    Location   = location,
                    ErrorDescriptor = UT0112,
                    ErrorArgs  = new object[] { methodName },
                };
            }

            // All checks passed — valid entry
            return new UtilityInputInfo
            {
                Name                    = inputName,
                MethodName              = methodName,
                FullyQualifiedMethodName = methodFqn,
                Namespace               = ns,
                Location                = location,
            };
        }

        // ---- Execute -----------------------------------------------------------

        private static void Execute(
            SourceProductionContext context,
            Compilation compilation,
            ImmutableArray<UtilityInputInfo> methods)
        {
            // Report per-method diagnostics and collect valid entries
            var valid = new List<UtilityInputInfo>();
            foreach (var m in methods)
            {
                if (m == null) continue;
                if (m.ErrorDescriptor != null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        m.ErrorDescriptor, m.Location, m.ErrorArgs));
                    continue;
                }
                valid.Add(m);
            }

            if (valid.Count == 0) return;

            // UT0102: check for duplicate [UtilityInput] names (keep first, report rest)
            var seenNames = new Dictionary<string, string>(); // name -> first method FQN
            var deduped   = new List<UtilityInputInfo>();
            foreach (var m in valid)
            {
                if (seenNames.TryGetValue(m.Name, out var firstFqn))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UT0102, m.Location, m.FullyQualifiedMethodName, m.Name));
                    continue;
                }
                seenNames[m.Name] = m.FullyQualifiedMethodName;
                deduped.Add(m);
            }

            // UT0103: check for hash collisions (keep first, report rest)
            var hashToInfo = new Dictionary<ushort, UtilityInputInfo>();
            var hashed     = new List<KeyValuePair<ushort, UtilityInputInfo>>();
            foreach (var m in deduped)
            {
                ushort hash = Fnv1a16(m.Name);
                if (hashToInfo.TryGetValue(hash, out var first))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UT0103, m.Location,
                        m.FullyQualifiedMethodName, m.Name, hash,
                        first.FullyQualifiedMethodName, first.Name));
                    continue;
                }
                hashToInfo[hash] = m;
                hashed.Add(new KeyValuePair<ushort, UtilityInputInfo>(hash, m));
            }

            if (hashed.Count == 0) return;

            // Determine namespace for the generated registrar class.
            // Use the namespace of the first valid method's containing type.
            string registrarNamespace = hashed[0].Value.Namespace;

            // Emit UtilityInputRegistrar.g.cs
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable disable");
            sb.AppendLine("using System;");
            sb.AppendLine($"namespace {registrarNamespace}");
            sb.AppendLine("{");
            sb.AppendLine("    [global::Fdp.Toolkit.Utility.UtilityRegistrar]");
            sb.AppendLine("    public static unsafe class UtilityInputRegistrar");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void RegisterAll()");
            sb.AppendLine("        {");
            foreach (var kvp in hashed)
            {
                sb.AppendLine($"            global::Fdp.Toolkit.Utility.UtilityInputReaderStore.Register(");
                sb.AppendLine($"                0x{kvp.Key:X4},");
                sb.AppendLine($"                &{kvp.Value.FullyQualifiedMethodName});");
            }
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            context.AddSource("UtilityInputRegistrar.g.cs", sb.ToString());

            // Emit UtilityInputAccessors.g.cs
            sb.Clear();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable disable");
            sb.AppendLine("namespace Fdp.Toolkit.Utility");
            sb.AppendLine("{");
            sb.AppendLine("    public static partial class In");
            sb.AppendLine("    {");
            foreach (var kvp in hashed)
            {
                sb.AppendLine($"        // Name=\"{kvp.Value.Name}\" hash=0x{kvp.Key:X4}");
                sb.AppendLine($"        public static global::Fdp.Toolkit.Utility.InputRef {kvp.Value.Name}(");
                sb.AppendLine($"            global::Fdp.Toolkit.Utility.InputContext ctx = default)");
                sb.AppendLine($"            => new global::Fdp.Toolkit.Utility.InputRef(0x{kvp.Key:X4}, ctx);");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            context.AddSource("UtilityInputAccessors.g.cs", sb.ToString());
        }

        // ---- Hash function (§3.3) -----------------------------------------------

        // 32-bit FNV-1a, return low 16 bits. Matches StandardInputIds hash constants exactly.
        internal static ushort Fnv1a16(string s)
        {
            uint hash = 2166136261u;
            foreach (char c in s)
            {
                hash ^= (uint)c;
                hash *= 16777619u;
            }
            return (ushort)(hash & 0xFFFF);
        }

        // ---- Data class --------------------------------------------------------

        private class UtilityInputInfo
        {
            public string Name;                      // [UtilityInput] Name value
            public string MethodName;                // symbol.Name (short name, for diagnostics)
            public string FullyQualifiedMethodName;  // ContainingType.FQN + "." + Name
            public string Namespace;                 // ContainingType.ContainingNamespace
            public Location Location;                // source location for diagnostics
            public DiagnosticDescriptor ErrorDescriptor; // non-null means invalid entry
            public object[] ErrorArgs;               // format args for the descriptor
        }
    }
}
