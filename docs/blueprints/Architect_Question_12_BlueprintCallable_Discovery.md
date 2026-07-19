# Architect question #12 — an editor-only `[BlueprintCallable]` discovery attribute for CLR helpers

**Status: DRAFT — for architect review. This revisits a "no" you already gave; please re-read the framing
below, because we think the objection is being applied to a use we are *not* proposing.**

## The goal (non-negotiable UX)

Designers author blueprints. **They must never type a CLR type FQN or method name** — that fragile,
error-prone free-text entry is exactly what a visual editor exists to eliminate. CLR helpers callable from
a `FunctionCall` node must be **discoverable from a curated, engine-defined, user-non-changeable picker**,
grouped by category. The designer picks; they never type.

We fully accept the curated, engine-owned model. The only question is the **developer-side mechanism** for
declaring "this helper is designer-callable."

## What you've sanctioned so far, and why it's insufficient

| Current option | Problem |
|---|---|
| **Manual entry** — designer types `TargetTypeId` + `MethodName` in the FunctionCall inspector | **Unacceptable UX.** Free-text, no discovery, typo-prone. This is the thing the editor should prevent. |
| **`NodeKindDescriptor` palette factories** (e.g. `BlueprintMathPaletteEntries`) registered in `BlueprintEditorBootstrap` | Works, but it's **hand-written boilerplate in a second place**, easy to forget, and divorced from the method it exposes. Doesn't scale as the curated surface grows. |

## The proposal

An **editor-only** attribute in a game-side assembly:

```csharp
[BlueprintCallable(Category = "Vector")]           // required Category = curation knob
public static Vector3 Vec3(float x, float y, float z) => new(x, y, z);
```

The **editor** reflection-scans loaded game assemblies for `[BlueprintCallable]` at startup and builds the
grouped, read-only picker from it. The designer picks → the `FunctionCall` node is populated with the baked
`TargetTypeId` + `MethodName` → **everything downstream is byte-for-byte identical to today.**

## Why your two objections do not apply here

### Objection 1 — "Roslyn vs reflection divide / discovery built twice."
This objection is correct for an attribute that **drives code generation**. We are **not** proposing that.
The attribute is **pure editor-side discovery metadata**. Trace the data flow:

```
[editor, design time]  reflection scan for [BlueprintCallable]  →  picker list
        designer picks a method  →  FunctionCall.TargetTypeId + MethodName  (baked strings in .bp.json)
[compiler, build time] IClrSignatureResolver (Roslyn semantic model) resolves those STRINGS  →  codegen
```

- The **compiler never reads the attribute.** It resolves the call exactly as it already does for a
  manually-entered method — from the baked `TargetTypeId`/`MethodName`. (You confirmed this: manual entry
  already works via the Roslyn semantic model. The attribute changes nothing on that path.)
- The **editor already reflects over the game assemblies** — `NodePinSchema.ResolveType` scans
  `AppDomain.CurrentDomain.GetAssemblies()` today to project FunctionCall pins. Scanning those same loaded
  assemblies for one more attribute is not new capability; it's the same reflection it already performs.

So there is **no second discovery implementation**. The netstandard2.0 analyzer host never needs the
attribute, because it never needs to *discover* anything — it's handed the FQN/method on a plate. The
"divide" is real for codegen-driving attributes and irrelevant for a pick-list.

### Objection 2 — "palette pollution / curated-kernels philosophy."
Legitimate, but it's a **policy knob, not a technical blocker** — and it's the *same* curation you already
require, with less boilerplate:
- The attribute **requires a `Category`** (no default) — an untagged method never appears; a tagged one is a
  deliberate, reviewed decision *at the method*, which is strictly more visible than a factory in a distant
  bootstrap file.
- Over-tagging is a code-review matter (only genuinely designer-facing helpers get the attribute), exactly as
  over-registering `NodeKindDescriptor`s would be.

## Questions

- **Q-A — do you accept an editor-only `[BlueprintCallable(Category)]` discovery attribute**, given it is
  never read by the compiler and the editor already performs assembly reflection? *(our strong lean: yes)*
- **Q-B — attribute placement / shape.** `[BlueprintCallable(Category, DisplayName?)]` on `public static`
  methods; trailing `Entity self` / `ISimulationView view` context params continue to be recognized and
  hidden (unchanged from the existing `TrailingContext` mechanism). Any constraints you want (e.g. only in a
  designated helpers assembly)?
- **Q-C — manual entry.** Keep the free-text FQN/method entry as a hidden "advanced" mode (dev/debug), or
  remove it from the designer UX entirely? *(our lean: demote to advanced-only, not the default path)*

## Recommendation summary
| Q | Lean | Why |
|---|---|---|
| A | **Accept the editor-only attribute** | compiler never reads it; editor already reflects; removes fragile typing |
| B | `[BlueprintCallable(Category, DisplayName?)]`, reuse existing trailing-context | minimal surface, consistent with FunctionCall today |
| C | Demote manual entry to advanced-only | designers pick from the curated list; never type |

**Reuse-vs-build:** editor gains a startup reflection scan (a few dozen lines, reusing the assembly-scan it
already does) that emits the same picker entries a `NodeKindDescriptor` would. Compiler: **zero change**.
Attribute type: a tiny `[AttributeUsage(Method)]` class in a game-side assembly. No analyzer/Roslyn work.
