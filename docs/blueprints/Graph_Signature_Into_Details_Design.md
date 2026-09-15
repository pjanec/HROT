# Folding `Graph Signature` into a context-sensitive `Details` — design note

> **BP-128.** Written Batch 26 by the implementation session, for the architect round.
> ⚠ **Design only — nothing was built.** BP-125 is the tactical fix
> and ships in this batch; this is the structural one.

---

## 0. The ask

> User: *"i do not understand why we are setting inputs and outputs in graph signature. Way more
> intuitive would be to set Detail on Event node (inputs) and Details on Return node (outputs). … The
> whole Graph Signature seems redundant."*
> · *"there should be one context-sensitive Detail for anything."*

⭐ **This is how Unreal works**, and it is also the shape the codebase has been drifting toward on its own.

---

## 1. ⭐ Why this is a correctness argument, not an ergonomics one

**Two editors for one piece of state is what caused BP-125.**

| Path | Routes through | Pins re-project? |
|---|---|---|
| Return node → `Details` → Outputs | `IEditService` → `OnStructureChanged` → `RebuildAndNotify()` | ✅ |
| `Graph Signature` window | `_dirtyTracker.MarkDirty(assetId)` only | 🔴 **never** |

Same state, two writers, one of which forgot a step — and the symptom (*"added an output, no pin
appeared"*, *"added a bool output and a bool literal, could not wire them"*) read for four separate user
reports as a **wiring/type** bug. It was neither. There was no pin.

⇒ **Deleting the second writer dissolves the bug class.** BP-125 patches the divergence; BP-128 removes
the ability to diverge.

---

## 2. Target surface

| Selection | `Details` shows | Status today |
|---|---|---|
| `EventEntry` node | the graph's **Inputs** | ✅ works (user confirmed) |
| `Return` node | the graph's **Outputs** | ✅ works — BP-89, and it is the path that projects correctly |
| **Empty canvas** | **graph + asset properties** | 🔴 **missing** — today: *"no node selected"* |
| `Graph Signature` window | — | retires once the above lands |

⭐ **Two of the three panels already exist and already work.** The work is the empty-canvas case plus the
retirement, not a rebuild.

---

## 3. What the empty-canvas Details must hold

| | |
|---|---|
| **Graph name** | ⭐ this is where **BP-127** (graph rename) belongs — it has no home until this panel exists, which is why it is blocked on this note |
| Graph kind | read-only (`Function` / `Event` / `Construction`) |
| Asset dispatch | read-only — changing it mid-life is a separate question |
| Asset name / id | read-only |
| Asset **Variables** / **Parameters** | ⚠ **open question** — see §5 |

---

## 4. Migration shape

1. Build the empty-canvas `Details` provider (graph + asset properties, rename included).
2. Point `GraphSignatureWindow` at the same `GraphSignatureEditModel` **through `EditService`** — i.e.
   BP-125 — so both paths behave identically *before* anything is removed.
3. Retire the window once the Details panels cover it.

⭐ **Order matters: BP-125 first.** Retiring the window while it is still the only route to some edit
would trade a projection bug for a missing feature.

---

## 5. ⚠ Open questions for the architect

| # | |
|---|---|
| **Q1** | **Does an Event graph's `EventEntry` edit graph Inputs, or the event's declared payload?** For a custom-event handler these may be the same thing; for an engine-event handler the payload is not the designer's to change. Getting this wrong makes an engine event look editable |
| **Q2** | **Where do asset-level Variables and Parameters live?** They are neither graph properties nor node properties. Empty-canvas Details is the natural home, but `My Blueprint` already owns them — two writers again, which is the exact mistake this note exists to undo |
| **Q3** | **Multi-select.** Unreal shows the common subset. Is that in scope, or is single-selection enough for v1? |
| **Q4** | **Does the Graph Signature window get deleted or hidden?** It is referenced by tests and possibly by layout persistence. Deleting is cleaner; hiding is reversible |
| **Q5** | **Construction graphs** — do they get a Return-equivalent outputs surface at all, or is the empty-canvas panel their only Details? |

---

## 6. Cost

| | |
|---|---|
| Empty-canvas Details provider + graph rename | the bulk; a new selection-context case plus a small panel |
| Retiring the window | small, but touches tests and layout |
| **Risk** | ⚠ **Low, and it shrinks after BP-125** — once both paths route through `EditService` they are already behaviourally identical, so retirement is a deletion rather than a migration |

⭐ **Recommendation:** ship **BP-125** now (done this batch), then take **Q1/Q2** to the architect before
building, since both are about *ownership of state* rather than layout — and ownership is precisely what
went wrong the first time.


---

# ✅ SETTLED — precondition now met

> Appended Batch 27 by the implementation session.

`DECISIONS_Authoring_UX.md` §D2 settled the shape, and **it is much smaller than this note assumed**:

- ⭐ **The empty-canvas Details surface is NOT needed.** The note assumed Unreal shows graph/asset
  properties on an empty-canvas click. **It does not** — Unreal clears Details, and function properties
  live on the entry node. So the panel this note proposed building does not need to exist.
- **Graph rename** — the one thing that was waiting on that surface — went to **My Blueprint's context
  menu** instead and **shipped in Batch 27** (BP-127).
- Inputs (entry node) and Outputs (Return node) already work.

⇒ **What remains is mostly deletion**, not design.

⚠ **The stated precondition — *"do it only after the matrix's edit-sequence axis exists, so the removal
is covered"* — is now MET**: `AuthoringPathEditSequenceTests` shipped in Batch 27
(BP-210).

⚠ **One thing to check before deleting**, because Batch 26 spent a whole item on it: `GraphSignatureWindow`
is where BP-125 routed signature edits through `IEditService`, and
`BP125_SignatureEditsReprojectTests` includes a **parity test asserting the two writers are observably
indistinguishable**. Deleting one writer makes that parity test vacuous rather than red — ⭐ **the
failure mode this programme keeps hitting.** Re-point it at the surviving writer, or delete it with the
window and say so.
