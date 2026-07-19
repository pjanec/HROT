# Architect question #11 — FunctionCall node visual language + CLR method picker (editor-UX)

**Context.** The editor-UX punch-list has two remaining FunctionCall-node items that need a visual/UX
ruling before building. Both are **editor-only** (no compiler/runtime/asset-format change). Non-programmers
must be able to tell at a glance *what a function node is* and *pick a method safely*.

Today:
- Every `FunctionCallNode` renders with the shared category header color — **blue** (`NodeCategory.Function`,
  `0.07,0.30,0.60`) when impure, **gray** (`NodeCategory.Pure`) when `IsPure` — with **no icon** and **no
  distinction** between a *CLR library method* call and an *in-blueprint Function-graph* call. (Node coloring
  lives in the shared `EngineEditorTheme.GetCategoryHeaderColor` + `NodeRenderer.DrawTitle`; category is chosen
  in `BlueprintNodeModel.BuildCategory`.)
- The inspector's CLR method form is **two free `InputText` fields** (Type ID + Method Name) —
  `FunctionCallNodeDrawer.DrawClrMethodForm`. Nothing validates them, nothing lists available methods.
- Curated helpers (`VectorOps`, `SegmentMath`, `NetworkEntityMapOps`, …) are plain `public static class`es
  with **no marker attribute and no catalog** — there is no existing "blueprint-callable methods" list to reuse.

Reference — the current shared category → header hue map:
| Category | Hue | Category | Hue |
|---|---|---|---|
| Event | dark red | VariableGet/Set | green |
| Function | **blue** | FlowControl | amber |
| Macro | purple | Pure / other | gray |

---

## Group A (punch-list #2) — CLR-vs-BP function visual language

A FunctionCall is one of two very different things and the author should see which:
- **CLR method** — calls curated engine C# (`VectorOps.Vec3`, …). Fixed, reviewed, "trust the FQN".
- **BP function** — calls an in-blueprint Function graph (`TargetGraphId`). Author-editable, navigable.

### Q-A1 — how do we distinguish them?
1. **Icon only.** Add a header icon: gear (`bp/function`) for CLR, a blueprint/node-graph glyph for BP-graph
   calls. Header color stays category-driven (blue/gray). Least visual noise; color stays semantic. *(our lean)*
2. **Distinct header colors.** Override the header hue for FunctionCall: e.g. CLR = steel/teal, BP = blue.
   Strongest at-a-glance signal but spends a second hue and breaks "blue == Function" consistency.
3. **Title-text color.** Keep header per-category; tint the *title text* (e.g. CLR = warm, BP = cool). Subtle;
   risks legibility on the colored header.
4. **Icon + thin accent stripe.** Icon (as #1) plus a 2px left accent stripe colored CLR-vs-BP. Clear without
   repainting the whole header.

- **Our lean: (1) icon-only, with (4) as the fallback if you want a stronger signal.** Reason: the header color
  already carries a meaning (category); overloading it with CLR-vs-BP (option 2) creates two competing color
  languages. An icon is unambiguous and is the same affordance UE/most node editors use. We'd use the existing
  atlas cells (gear for CLR; the `asset/blueprint`/node-graph glyph for BP-graph calls) so no new art.
- **Q-A2 (only if you pick 2/4):** which two hues, and may they override the category color? Our suggestion if
  forced: CLR = the current Function blue; BP-graph = a distinct cyan/teal, applied only to FunctionCall nodes.

### Q-A3 — pure vs impure
Pure FunctionCalls currently go **gray** (`NodeCategory.Pure`), same as `Literal`/`Compare`/etc. Keep pure =
gray (UE-like "pure == no exec"), and let the **icon** (A1) carry CLR-vs-BP on top of the gray? *(our lean: yes)*
Or should pure CLR calls also get the function-blue? (We think no — gray-for-pure is a useful separate axis.)

- **Reuse vs build:** (1) = header icon draw in `NodeRenderer` (needs a per-node icon-key hook on the model,
  which NodeEdit does not have yet — small addition, mirrors the tooltip hook we just added) + a `bp/function`
  key that already exists. (2)/(4) = a theme/renderer color branch keyed on a model flag. All small.

---

## Group B (punch-list #3) — grouped read-only CLR method picker

Replace the two free `InputText`s with a **read-only picker** of known methods (+ per-method summary tooltips,
reusing the #4 XML-doc source), and **lock** the choice when changing it would break existing wires.

### Q-B1 — what is the source of truth for "pickable" methods? (the load-bearing one)
1. **`[BlueprintCallable]` marker attribute** on curated helper methods/classes; the editor reflects for it.
   Explicit, reviewable, self-documenting; author sees exactly the curated surface. Cost: add the attribute
   (in a game-side assembly) + stamp the ~10 curated helper classes. *(our lean)*
2. **Namespace/assembly convention** — list all `public static` methods on classes under
   `Hrot.AI.Behaviors.Brains` (or a designated "curated" namespace). Zero attribute cost, but pulls in
   non-curated/oracle statics and is a fuzzy contract.
3. **Explicit registry list** — a hand-maintained editor-side list of allowed (type, method) pairs. Full
   control, but a second place to maintain and drifts from the code.

- **Our lean: (1) `[BlueprintCallable]`.** It is the only option that makes "this method is part of the visual
  API" an explicit, reviewed decision at the method, matches the reflection-free authoring philosophy (the
  attribute is game-side; the editor reflects at *design* time where reflection is fine), and gives us a place
  to hang grouping/category metadata (see B2). Free-text entry can remain as an "advanced/escape-hatch" toggle.

### Q-B2 — grouping scheme in the picker
1. **By declaring class** (`VectorOps`, `SegmentMath`, …) — matches how the helpers are already organized and
   how authors think ("the vector ops"). *(our lean)*
2. **By a `Category` on the attribute** (e.g. "Math", "Vector", "Targeting") — decouples grouping from class,
   nicer taxonomy, but another field to curate.
3. **Flat, searchable** — no groups, just type-ahead. Simplest; weak for discovery.

- **Our lean: (1) by declaring class**, with the attribute optionally carrying a `Category` override (B2-#2)
  for later polish. Class grouping is free and immediately meaningful.

### Q-B3 — the "lock when wired" rule
When pins are already wired, swapping the method silently orphans/oprhans wires.
1. **Hard-lock when ANY data pin is wired** — the picker is read-only/disabled; author must unwire first.
   Safest, most predictable. *(our lean)*
2. **Allow with confirm + drop incompatible wires** — a dialog lists wires that will break, author confirms.
   More flexible, more UI + a wire-diff computation.
3. **Lock only when the new signature is incompatible** — allow method swaps that keep pin shapes. Cleverest,
   but "compatible" is subtle (name/type/arity) and surprising when it silently keeps/drops.

- **Our lean: (1) hard-lock when any data pin is wired**, with an inline hint ("unwire to change method"). It
  is the least surprising and cheapest; we can relax to (2) later if authors find it annoying.

- **Reuse vs build:** picker UI can reuse the existing NodeEdit **picker** (`IPickerSource`, Tree layout — same
  component the Open-Asset picker uses) fed by a small `ClrMethodCatalog` built from the `[BlueprintCallable]`
  reflection scan; summary tooltips reuse the #4 `ClrXmlDocSource`. Lock rule = a wired-pin check in the drawer.

---

## Recommendation summary
| Q | Lean | Why |
|---|---|---|
| A1 distinguish CLR vs BP | **icon-only** (gear vs graph glyph) | keeps header color = category; icon is unambiguous, no new art |
| A3 pure vs impure | **keep pure = gray**, icon carries CLR/BP | pure/impure is a separate, useful axis |
| B1 method source | **`[BlueprintCallable]` attribute** | explicit reviewed visual-API surface; place for grouping metadata |
| B2 grouping | **by declaring class** | free, matches how helpers are organized/thought-of |
| B3 lock rule | **hard-lock when any data pin wired** | least-surprising, cheapest; relax later if needed |

*Status: DRAFT — awaiting architect answers. Do not build until answered.*
