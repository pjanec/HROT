<!--STATUS
state: LIVE
updated: 2026-08-20
current-answer: section 4 — RECOMMENDED ANSWERS, awaiting the user's approval.
stale-below: nothing.
known-rot: none.
known-conflict: it EXTENDS Q38's scope. Q38's inventory explicitly fenced these surfaces
  out as "engine / sim, different lifecycle". The user has brought them in for the
  scenario perspective; §1 says so rather than editing the fence silently.
-->
# ⭐ Architect Question 47 — **the ENTITY context in the scenario perspective**

> ⭐⭐ **User, `2026-08-20`:** *"in editor (scenario) perspective if we click an entity, what the detail
> views should be available. not touched yet but extremely important. many to choose from — entity
> component inspector (same as the one openable-pinnable for single selected entity), mission plan
> editor, specific entity-type dependent editors (color for map drawing entity etc.)"*
>
> ⛔⛔ **NOT RELAYED** *(`2026-08-16`)*. ⭐⭐ **I analyse and RECOMMEND, the user APPROVES.**

---

## 1. ⛔⛔ THIS EXTENDS `Q38`'S SCOPE — **stated, not done silently**

📄 `Q38`'s inventory has an explicit fence: *"⛔ **NOT this question — engine / sim, different
lifecycle**: `EntityInspectorPanel` ×2 · `DerEntityInspectorPanel` · `EntityWatchPanel` ·
`FdpEntityInspectorWindow` ×2 · `FdpEntityWatchWindow` · `InspectorPanel` (ExCon) · the `Fake*`
windows"* — ⚠ **and it says it was named so a later sweep would not "discover" them and widen the
scope.**

⇒ ⭐⭐ **The user has now widened it deliberately, for ONE perspective.** ⛔ **The fence is not wrong and
is not deleted** — ⭐ it still holds for the **three AI perspectives**, where an entity is a *value
source*, not a *thing you author*.
⭐⭐⭐ **What changes: in the SCENARIO perspective the entity IS the authored thing** ⇒ it earns a
context row, exactly like a node does on a canvas.

---

## 2. ⭐⭐ INVENTORY *(`R-74` — the graph, `2026-08-20`)*

```
search_graph(name_pattern=".*(EntityInspector|EntityProperty|MissionPanel|MissionPlanEditor|
             MapDraw|Annotation).*", label="Class")                          → total 26
grep -rln "MapDrawing|DrawingEntity|MapAnnotation|TacticalGraphic|MapSymbol" (excl obj/Tests) → 8
grep -rn  "ColorEdit3|ColorEdit4|ColorPicker" --include=*.cs Hrot/ (excl obj/Tests)           → 0
```

### ⭐ What EXISTS

| surface | lines · in-degree | ⭐ what it is |
|---|---|---|
| ⭐⭐⭐ **`EntityInspectorPanel`** *(`Fdp.Presentation/ImGui/Panels/`)* | **570** · 3 | ⭐⭐ **the real component inspector** — ⭐ **and it already supports MULTI-SELECT** *(`EntityInspectorPanelMultiSelectTests`, `DD-P3-T02`)* |
| **`EntityInspectorPanel`** *(`Hrot.IG/UI/`)* | 51 · **9** | ⚠ **a SECOND class of that name** — a thin IG-side host |
| **`EntityInspectorState`** *(`Hrot.IG/UI/`)* | 115 · 3 | its state — `SelectedEntity`, refresh |
| **`DerEntityInspectorPanel`** *(`Fdp.Presentation`)* | 216 · 3 | the **DER** view of an entity *(ExCon uses it)* |
| ⭐⭐⭐ **`MissionPanel`** *(`Hrot.Presentation/Panels/`)* | **792** · **14** | ⭐⭐ **the mission plan editor** — the biggest and most-referenced of them |
| **`FdpEntityInspectorWindow`** ×2 · **`FdpEntityInspectorHelper`** | 30 · 31 · 57 | window wrappers *(one is ReplayBrowser's)* |
| ⚠ **`EntityPropertyInspector`** *(`Hrot.Editor/UI/`)* | 31 · **0** | ⛔ **in-degree 0** — carries only `SetSelectedEntity(long networkId)` |

### ⛔⛔ What does NOT exist — **and it is the important half**

📐 **`ColorEdit3`/`ColorEdit4`/`ColorPicker`: ZERO hits in `Hrot/`.** ⭐ The `MapDrawing`/`MapLayer`
hits are **LAYER config** *(`MapLayerRegistry`, `MapLayerState`, `ConfigPanel`)*, ⛔ **not a per-entity
editor.**

⇒ ⭐⭐⭐ **The user's "colour for a map drawing entity" is a CAPABILITY THAT DOES NOT EXIST**, and that
makes it the **most informative** item in the list: ⛔ this question is **not** mainly about folding
existing panels — ⭐⭐ **it is about the EXTENSION POINT that lets a subsystem contribute a view for an
entity TYPE.**

---

## 3. ⭐ What binds any answer

| id | binds |
|---|---|
| ⭐⭐ **`R-98`** | the toolbar is a **panel switch**; **context decides the OFFER SET and the DEFAULT** |
| ⭐⭐⭐ **`R-115`** | **context = FOCUS + SELECTION**; ⛔ pan changes neither; ⛔ document/perspective switches move only focus |
| ⭐⭐ **`R-110`** | the offer set is a function of `(selection, perspective)`; ⭐ **read-only views may be instanced, editing views prefer sharing** |
| **`R-111`** | the **mode** joins the context; ⭐ **one view, multiple modes** |
| **`R-100`** | a pin is **one instance per pin**, titled, volatile |
| **ruling 9** | ⛔ one implementation per concept |
| ⚠ **`R-13`** | ⛔ no rush removals — say **duplicate CODE · duplicate SURFACE · genuinely dead** |

---

## 4. ⭐⭐⭐ THE SUB-QUESTIONS — **with recommended answers**

### ⭐⭐⭐ `Q47-A` — what views does the ENTITY context offer?

| ⭐⭐⭐ **RECOMMENDED** |
|---|

| view | ⭐ source | notes |
|---|---|---|
| ⭐⭐ **Components** *(DEFAULT)* | **`EntityInspectorPanel`** *(the 570-line one)* | ⭐ the same panel the pinnable single-entity inspector uses — 📌 **`R-100`: a pin is this same view with a frozen context**, ⛔ not a different window |
| ⭐⭐ **Mission plan** | **`MissionPanel`** | ⚠ **offered only when the entity HAS a mission** *(`MissionPlanQueue` / `ActiveMissionPlan`)* — ⛔ otherwise it is a toggle that opens onto nothing |
| **DER** | `DerEntityInspectorPanel` | ⚠ **offered only where DER is meaningful** — ⭐ measure whether that is a perspective or a component test before wiring |
| ⭐⭐⭐ **type-specific editors** | ⛔ **DO NOT EXIST — `Q47-C`** | the colour-for-a-map-drawing case |

### ⭐⭐⭐ `Q47-B` — is the entity context ONE row, or one per entity KIND?

| ⭐⭐⭐ **RECOMMENDED: ONE context row — *"an entity is selected"* — with a VARIABLE offer set.** |
|---|

⭐ `R-98` already says the context decides what is **offered**; ⇒ *"entity"* is the context and the
entity's **components** decide which optional views appear. ⛔ **Not one context per entity type** —
that multiplies contexts without bound and the toolbar becomes a type switch.
⭐⭐ **Mission plan is the worked example**: same context, offered only when the components say so.

### ⭐⭐⭐ `Q47-C` — how does a subsystem contribute an entity-TYPE view?

| ⭐⭐⭐ **RECOMMENDED: a REGISTRY of `(predicate over the entity) → view`, filled at the composition root.** |
|---|

⭐⭐ **This is the same shape `RuntimeInspectorWindow` already uses** — 📐 it *"delegates the
asset-specific pane to the registered `IRuntimeInspectorPane` for the active asset kind."*
⇒ ⭐ **an entity-view registry is that pattern with a PREDICATE instead of an asset kind**, because an
entity has no single "kind" — ⛔ it has components.
⭐ **Feeds registered by whoever needs to show contextual info** *(`R-110`)* — the map-drawing subsystem
registers the colour editor; ⛔ the shared panel never learns about map drawing.

⚠ **This is the item with real design content**, and it is what makes the colour example cheap later
instead of a special case.

### ⭐⭐ `Q47-D` — multi-select?

| ⭐⭐⭐ **RECOMMENDED: YES for the entity context — the panel ALREADY supports it.** |
|---|

📐 `EntityInspectorPanelMultiSelectTests` *(`DD-P3-T02`)* ⇒ ⭐ **the capability exists and is railed.**
⇒ ⭐⭐ **the entity context should NOT throw a multi-pick away** — ⛔ which is exactly the gap `R-115`
left open on the **node** side *(a marquee of two nodes currently resolves to `null`)*.
⚠ **Worth noting the asymmetry out loud:** entities have a multi-select inspector, nodes do not.

### ⚠ `Q47-E` — which of the SEVEN existing surfaces retire?

| ⭐⭐⭐ **RECOMMENDED: DECIDE NOTHING YET — measure each against `R-13`'s three labels first.** |
|---|

⚠ **`EntityPropertyInspector` is in-degree 0 with a single `SetSelectedEntity(long)` method** — ⭐ it
*looks* like the `LiveBlackboardPanel` case, ⛔ **but `R-13` requires the label before the verdict**,
and this question has not measured what it was for.
⛔ **The two `EntityInspectorPanel` classes and the two `FdpEntityInspectorWindow`s are a ruling-9
question** — ⭐ but they belong to **`Fdp.Presentation`/`Hrot.IG`**, which is **not this programme's
assembly**, so retiring them has an owner outside this work. ⚠ **Name it; do not assume it.**

---

## 5. ⭐ Sequencing

⭐⭐ **This is `Q38`'s successor, not a parallel** — ⛔ **the shell, the toolbar and the offer-set
mechanism must exist first**, and `R-27` still gates that on the visual check.
⇒ ⭐ **Answer now, build after `Q38`'s first slice.**
⚠ **`Q47-C`'s registry is the one piece worth designing INTO `Q38`'s toolbar from the start** — ⭐ a
per-context view registry that takes a predicate is barely more than one that takes an asset kind, ⛔
and retrofitting it later means touching every context.
