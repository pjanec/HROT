<!--STATUS
state: LIVE
updated: 2026-08-20
current-answer: section 4 — ANSWERED IN FULL by the user on 2026-08-20 (R-116, R-117).
stale-below: nothing.
known-rot: section 2's line that type-specific editors 'do not exist' was true of today's
  code and WRONG about the design - the user ruled they exist or arrive soon. Corrected
  inline in section 4.
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

## 4. ✅✅✅ THE ANSWERS — **RULED `2026-08-20` by the user** *(`R-116`, `R-117`)*

### ⭐⭐⭐ HOST SCOPE — **decided first, and it removes an item**

> ⭐⭐ **User:** *"Der is meaningful only for ExCon(IOS) perspective. We may ignore the IOS and IG and
> CGF hosts for now and focus on the editor host only."*

⇒ ⛔ **`DerEntityInspectorPanel` is OUT of scope.** ⭐ **The EDITOR host is the target.**

### ✅ `Q47-A` — the views

| view | ⭐ ruled |
|---|---|
| ⭐⭐ **Components** *(default)* | ✅ **in** — the same panel the pinnable single-entity inspector uses *(`R-100`)* |
| ⭐⭐ **Mission plan** | ✅ **in** — ⭐ **and it is ITSELF an entity-type-specific view**: *"makes sense only for brain-equipped entities"* |
| ⭐⭐⭐ **entity-TYPE-specific views** | ✅✅ **THEY EXIST OR ARRIVE SOON** — *"to allow user friendly setting of various entity properties, not just via component inspector"* |
| ⛔ **DER** | ⛔ **out** — ExCon/IOS only |

> ⛔⛔ **A CORRECTION OF MINE.** §2 measured *"`ColorEdit`/`ColorPicker`: zero hits ⇒ type-specific
> editors do not exist"*. ⭐⭐ **That was true of TODAY'S CODE and WRONG as a statement about the
> design** — 📌 the `.dev/` rule in one line: **a grep answers *"is it used?"*, never *"is it meant to
> exist?"*** ⇒ ⭐ **they are a planned family, and the Mission panel is the first member.**

### ✅ `Q47-B` — ONE context row, VARIABLE offer set

⭐ **Confirmed.** ⭐⭐ **The offer set is computed from the TKB record AND the currently present
component combination** — 📐 `TkbDescriptorRegistry`, keyed by `TkbEntityTypes`, descriptors registered
by a Roslyn source generator. ⛔ **Not one context per entity type.**

### ✅ `Q47-C` — predicate-based, and **the view OWNS its predicate**

> ⭐⭐ **User:** *"each view 'knows' via the predicate what entities it wants to be available for."*

⇒ ⭐⭐⭐ **The registry maps `predicate → view`, and the PREDICATE SHIPS WITH THE VIEW.** ⛔ **The shared
panel never learns about map drawing, or missions, or any entity type** — ⭐ it asks each registered
view *"do you apply to this selection?"*

### ✅✅ `Q47-D` — the predicate reads the SELECTION SET, and an empty offer set MUST SAY SO

> ⭐⭐ **User:** *"the predicate must allow reading the selection set and each view decides itself what
> to show. if no views for specific multi-select set, the detail panel should say so in gray
> informative text (intentionally empty for currently selected entities) — not just empty."*

| ⭐ | |
|---|---|
| **the signature** | ⛔ **over a SET, never a single entity.** ⭐ A view that only handles one entity **says so in its own predicate** — the panel does not special-case it |
| ⭐⭐⭐ **the empty state** | ⛔⛔ **A BLANK PANEL IS A DEFECT, NOT A STATE.** ⭐ **Grey informative text** — *"intentionally empty for the current selection"* |
| ⭐⭐ **and it generalises** | 📌 **this answers the multi-NODE gap `R-115` left open** — a marquee of two nodes is a **real selection with no view yet**, and it renders that grey line rather than resolving to nothing |

### ✅ `Q47-E` — **no retiring yet**

⭐ Confirmed. ⛔ **Nothing in §2's inventory is retired by this question** — 📌 `R-13` labels first, and
the duplicate `EntityInspectorPanel`/`FdpEntityInspectorWindow` pairs live outside this programme's
assemblies anyway.

## 5. ⭐ Sequencing

⭐⭐ **This is `Q38`'s successor, not a parallel** — ⛔ **the shell, the toolbar and the offer-set
mechanism must exist first**, and `R-27` still gates that on the visual check.
⇒ ⭐ **Answer now, build after `Q38`'s first slice.**
⚠ **`Q47-C`'s registry is the one piece worth designing INTO `Q38`'s toolbar from the start** — ⭐ a
per-context view registry that takes a predicate is barely more than one that takes an asset kind, ⛔
and retrofitting it later means touching every context.
