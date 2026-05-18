# BATCH-11: Source Generation & Action Dispatch

**Batch Number:** BATCH-11  
**Tasks:** TASK-SG01 (Source Generator Setup), TASK-SG02 (Action/Guard Binding)  
**Phase:** Phase 3 - Kernel (Source Generation)  
**Estimated Effort:** 7-10 days  
**Priority:** HIGH  
**Dependencies:** BATCH-10

---

## 📋 Onboarding & Workflow

### Developer Instructions

Welcome to **BATCH-11**! This batch implements **source generation** for binding user-defined actions and guards to the runtime without reflection.

This is **advanced C# work** involving Roslyn source generators, incremental generation, and function pointer dispatch.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Definitions:** `.dev-workstream/TASK-DEFINITIONS.md` - See TASK-SG01, TASK-SG02
3. **Design Document:** `docs/design/HSM-Implementation-Design.md` - Section 2.3 (Source Generation)
4. **Architect Review:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` - Q8 (Function ID Stability), Q9 (Action Signature)
5. **Microsoft Docs:** [Source Generators](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)

### Source Code Location

- **New Project:** `src/Fhsm.SourceGen/` (NEW)
- **Kernel Updates:** `src/Fhsm.Kernel/HsmActionDispatcher.cs` (NEW)
- **Test Project:** `tests/Fhsm.Tests/SourceGen/` (NEW)

### Questions File

`.dev-workstream/questions/BATCH-11-QUESTIONS.md`

---

## Context

**Transition execution complete (BATCH-10).** Actions are stubbed (`ExecuteAction` does nothing).

**This batch implements:**
1. **Source Generator** - Roslyn incremental generator
2. **Action/Guard Attributes** - `[HsmAction]`, `[HsmGuard]`
3. **Dispatch Table** - Function pointer lookup by ID
4. **Linker Pattern** - Hash-based binding (Architect Q8)

**Related Tasks:**
- [TASK-SG01](../TASK-DEFINITIONS.md#task-sg01-source-generator-setup) - Source Generator Setup
- [TASK-SG02](../TASK-DEFINITIONS.md#task-sg02-action-guard-binding) - Action/Guard Binding

---

## 🎯 Batch Objectives

Replace `ExecuteAction` stub with real dispatch:
- User marks methods with `[HsmAction]` or `[HsmGuard]`
- Source generator finds methods, generates dispatch table
- Runtime looks up function pointers by ID
- No reflection, zero allocation

---

## ✅ Tasks

### Task 1: Source Generator Project Setup (TASK-SG01)

**Create:** `src/Fhsm.SourceGen/Fhsm.SourceGen.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**Why netstandard2.0:** Source generators must target netstandard2.0 for compatibility.

---

### Task 2: Action/Guard Attributes

**Create:** `src/Fhsm.Kernel/Attributes/HsmActionAttribute.cs`

```csharp
using System;

namespace Fhsm.Kernel.Attributes
{
    /// <summary>
    /// Marks a method as an HSM action.
    /// Signature: void MethodName(void* instance, void* context, ushort eventId)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class HsmActionAttribute : Attribute
    {
        /// <summary>
        /// Unique name for this action. If null, uses method name.
        /// </summary>
        public string? Name { get; set; }
    }
}
```

**Create:** `src/Fhsm.Kernel/Attributes/HsmGuardAttribute.cs`

```csharp
using System;

namespace Fhsm.Kernel.Attributes
{
    /// <summary>
    /// Marks a method as an HSM guard.
    /// Signature: bool MethodName(void* instance, void* context, ushort eventId)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class HsmGuardAttribute : Attribute
    {
        /// <summary>
        /// Unique name for this guard. If null, uses method name.
        /// </summary>
        public string? Name { get; set; }
        
        /// <summary>
        /// If true, this guard uses RNG (Architect Q4).
        /// Triggers debug-only AccessCount increment.
        /// </summary>
        public bool UsesRNG { get; set; }
    }
}
```

---

### Task 3: Incremental Source Generator

**Create:** `src/Fhsm.SourceGen/HsmActionGenerator.cs`

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fhsm.SourceGen
{
    [Generator]
    public class HsmActionGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find all methods with [HsmAction] or [HsmGuard]
            var actionMethods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (s, _) => IsCandidateMethod(s),
                    transform: static (ctx, _) => GetMethodInfo(ctx))
                .Where(static m => m != null);

            // Collect and generate
            context.RegisterSourceOutput(
                actionMethods.Collect(),
                static (spc, methods) => Execute(spc, methods!));
        }

        private static bool IsCandidateMethod(SyntaxNode node)
        {
            return node is MethodDeclarationSyntax method &&
                   method.AttributeLists.Count > 0;
        }

        private static MethodInfo? GetMethodInfo(GeneratorSyntaxContext context)
        {
            var method = (MethodDeclarationSyntax)context.Node;
            var symbol = context.SemanticModel.GetDeclaredSymbol(method);
            
            if (symbol == null) return null;

            // Check for [HsmAction] or [HsmGuard]
            var actionAttr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "HsmActionAttribute");
            var guardAttr = symbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "HsmGuardAttribute");

            if (actionAttr == null && guardAttr == null) return null;

            var isGuard = guardAttr != null;
            var attr = isGuard ? guardAttr : actionAttr;

            // Get name (from attribute or method name)
            string name = symbol.Name;
            var nameArg = attr!.NamedArguments.FirstOrDefault(a => a.Key == "Name");
            if (nameArg.Value.Value is string customName && !string.IsNullOrEmpty(customName))
            {
                name = customName;
            }

            return new MethodInfo
            {
                Name = name,
                FullName = $"{symbol.ContainingType.ToDisplayString()}.{symbol.Name}",
                IsGuard = isGuard,
                IsStatic = symbol.IsStatic
            };
        }

        private static void Execute(SourceProductionContext context, IEnumerable<MethodInfo> methods)
        {
            var actions = methods.Where(m => !m.IsGuard).ToList();
            var guards = methods.Where(m => m.IsGuard).ToList();

            // Generate dispatch table
            var source = GenerateDispatchTable(actions, guards);
            context.AddSource("HsmActionDispatcher.g.cs", source);
        }

        private static string GenerateDispatchTable(List<MethodInfo> actions, List<MethodInfo> guards)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine("namespace Fhsm.Kernel");
            sb.AppendLine("{");
            sb.AppendLine("    internal static unsafe class HsmActionDispatcher");
            sb.AppendLine("    {");
            
            // Action delegate
            sb.AppendLine("        private delegate void ActionDelegate(void* instance, void* context, ushort eventId);");
            sb.AppendLine("        private delegate bool GuardDelegate(void* instance, void* context, ushort eventId);");
            sb.AppendLine();

            // Action table
            sb.AppendLine("        private static readonly Dictionary<ushort, ActionDelegate> ActionTable = new()");
            sb.AppendLine("        {");
            foreach (var action in actions)
            {
                ushort id = ComputeHash(action.Name);
                sb.AppendLine($"            {{ {id}, &{action.FullName} }},");
            }
            sb.AppendLine("        };");
            sb.AppendLine();

            // Guard table
            sb.AppendLine("        private static readonly Dictionary<ushort, GuardDelegate> GuardTable = new()");
            sb.AppendLine("        {");
            foreach (var guard in guards)
            {
                ushort id = ComputeHash(guard.Name);
                sb.AppendLine($"            {{ {id}, &{guard.FullName} }},");
            }
            sb.AppendLine("        };");
            sb.AppendLine();

            // Dispatch methods
            sb.AppendLine("        public static void ExecuteAction(ushort actionId, void* instance, void* context, ushort eventId)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (ActionTable.TryGetValue(actionId, out var action))");
            sb.AppendLine("            {");
            sb.AppendLine("                action(instance, context, eventId);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        public static bool EvaluateGuard(ushort guardId, void* instance, void* context, ushort eventId)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (GuardTable.TryGetValue(guardId, out var guard))");
            sb.AppendLine("            {");
            sb.AppendLine("                return guard(instance, context, eventId);");
            sb.AppendLine("            }");
            sb.AppendLine("            return true; // No guard = always pass");
            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static ushort ComputeHash(string name)
        {
            // Simple hash (FNV-1a)
            uint hash = 2166136261;
            foreach (char c in name)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (ushort)(hash & 0xFFFF);
        }

        private class MethodInfo
        {
            public string Name { get; set; } = "";
            public string FullName { get; set; } = "";
            public bool IsGuard { get; set; }
            public bool IsStatic { get; set; }
        }
    }
}
```

---

### Task 4: Update Kernel to Use Dispatcher

**Update:** `src/Fhsm.Kernel/HsmKernelCore.cs`

Replace `ExecuteAction` and `EvaluateGuard` stubs:

```csharp
private static void ExecuteAction(
    ushort actionId,
    byte* instancePtr,
    void* contextPtr,
    ushort eventId)
{
    HsmActionDispatcher.ExecuteAction(actionId, instancePtr, contextPtr, eventId);
}

private static bool EvaluateGuard(
    ushort guardId,
    byte* instancePtr,
    void* contextPtr,
    ushort eventId)
{
    return HsmActionDispatcher.EvaluateGuard(guardId, instancePtr, contextPtr, eventId);
}
```

---

### Task 5: Wire Source Generator to Projects

**Update:** `src/Fhsm.Kernel/Fhsm.Kernel.csproj`

```xml
<ItemGroup>
  <ProjectReference Include="..\Fhsm.SourceGen\Fhsm.SourceGen.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

**Update:** `tests/Fhsm.Tests/Fhsm.Tests.csproj`

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Fhsm.SourceGen\Fhsm.SourceGen.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

---

## 🧪 Testing Requirements

**File:** `tests/Fhsm.Tests/SourceGen/ActionDispatchTests.cs` (NEW)

**Minimum 15 tests:**

### Attribute Tests (3)
1. `[HsmAction]` on method recognized
2. `[HsmGuard]` on method recognized
3. Custom name via attribute property

### Generation Tests (5)
4. Action dispatch table generated
5. Guard dispatch table generated
6. Hash computation stable (same name → same ID)
7. Multiple actions in same class
8. Actions across multiple classes

### Dispatch Tests (7)
9. Action executed via dispatcher
10. Guard evaluated via dispatcher
11. Unknown action ID handled gracefully
12. Unknown guard ID returns true (default pass)
13. Action receives correct parameters (instance, context, eventId)
14. Guard return value propagated
15. Multiple actions dispatched in sequence

---

## 📊 Success Criteria

- [ ] TASK-SG01 completed (Source generator project, incremental generation)
- [ ] TASK-SG02 completed (Attributes, dispatch table, binding)
- [ ] Source generator runs at compile time
- [ ] `HsmActionDispatcher.g.cs` generated correctly
- [ ] Actions/guards callable via dispatcher
- [ ] 15+ tests, all passing
- [ ] No compiler warnings
- [ ] Generated code visible in `obj/` folder

---

## ⚠️ Common Pitfalls

1. **netstandard2.0:** Source generators MUST target netstandard2.0
2. **Incremental:** Use `IIncrementalGenerator`, not `ISourceGenerator`
3. **Function Pointers:** Requires `unsafe` context
4. **Hash Collisions:** Use good hash (FNV-1a or similar)
5. **Static Methods:** Actions/guards should be static for function pointers
6. **Signature:** Must match `void(void*, void*, ushort)` or `bool(void*, void*, ushort)`

---

## 📚 Reference

- **Tasks:** [TASK-DEFINITIONS.md](../TASK-DEFINITIONS.md) - TASK-SG01, TASK-SG02
- **Design:** `docs/design/HSM-Implementation-Design.md` - §2.3 (Source Gen)
- **Architect:** `docs/design/ARCHITECT-REVIEW-SUMMARY.md` - Q8 (Stability), Q9 (Signature)
- **Microsoft:** [Source Generators](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview)

---

**This is advanced C# work. Take time to understand Roslyn APIs.** 🚀
