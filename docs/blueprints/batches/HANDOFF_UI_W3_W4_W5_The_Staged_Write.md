<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-21
current-answer: dispatch pointer for the UI lane — the staged-write UI half (W3/W4/W5). The design is
  DESIGN_Staged_Live_Write.md; build from it. ⚠ ORDER MATTERS — see §0.
-->
# HANDOFF — UI lane · **W3 + W4 + W5: the staged-write UI half**

> 📌 **Dispatched at `b607ac95e`.** ⭐ Branch from it *(rule 7)*. ⛔ **Scope FROZEN at this sha.**
> ⭐ **Lane: UI / variable** *(`claude/hrot-implementation-j1jvin`)* — ids **`BP-`**, tracker `A`–`G`.
> ⭐ **Rule 1b: started-marker FIRST.** ⭐ **Rule 3: your own ids.**
> ⚠ **Independent of the details panel** *(different files)* — pick this up when convenient, before or
> after details, **but respect the ORDER in §0.**

## 0. ⛔⛔⛔ THE ORDER — **this is the whole risk; do not reorder**

📄 **THE DESIGN:** [`DESIGN_Staged_Live_Write.md`](../DESIGN_Staged_Live_Write.md) — read it whole; its
`classDiagram`/`sequenceDiagram` *(§3)*, the `IStagedWrites` seam *(§5, already defined in
`FDP/Engine/Fdp.Core/Abstractions/IStagedWrites.cs`)*, and **§8 the integration + the hazard.**

⛔⛔⛔ **`W3` removes `MIN`'s `WriteFieldNow`. Do NOT do that until the drain is LIVE-WIRED** — else a
paused edit stages and **never applies** *(worse than today)*. ⭐⭐ **Safe order (design §8):**

| step | who | gate |
|---|---|---|
| **`W4`** implement + display | ⭐ **you, anytime** | independent — start here |
| **merge `W1`/`W2`** (the drain) | ⛔ **coordinator** | after the time lane lands the drain |
| **the wire** (`EditorSubsystem`) | ⭐ you *(1 line)* | after the merge + `W4` |
| **`W3`** remove `MIN`, stage everything | ⭐ you | ⛔ **ONLY after the wire is in** |
| **`W5`** move the restore | ⭐ you | `R-63`; the drain rail settles it |

## 1. ⭐⭐ `W4` — **implement `IStagedWrites`, and the SHARED yellow** *(start here — safe now)*

⭐ **`DataBreakpointManager` implements `IStagedWrites`** *(§5)*: `HasPending`/`IsRewound` *(= its
`IsPaused`)* / `DrainInto(view)` *(expose the existing `DrainPendingMutations`)* / `TryGetPending(entity,
typeId, offset, out bytes)` *(a lookup over `_pendingMutations`)*.
⭐⭐ **The shared yellow — fork A** *(§4)*: a **`StagedWriteView` at the composition root** *(`R-120`,
shared — NOT per-`VariableTableModel`)* that both Details and Watch read; the row's `Pending` **and** its
displayed value **derive from `TryGetPending`** ⇒ **both panels show the SAME staged bytes in yellow,
immediately** *(§7)*; it **auto-clears** when the mutation drains. ⛔ **Do NOT wire the old
`MarkPending`/`ClearPending` flag** — it collapses into the query *(`R-13`)*.

## 2. ⭐ `W3` — **uniform staging** *(ONLY after the wire — §0)*

⭐ Drop the `_isPaused`/`LiveWriteRefusal.NotFrozen` refusal and the session's `_isPaused` write gate;
**remove `MIN`'s `WriteFieldNow`**; stage in every writable run state *(`R-126`: running is a reason to
STAGE)*. ⭐ **Keep** `NoSelectedEntity` · `FieldNotResolvable` · `SizeMismatch` *(`Q32` §2.1 corruption
gate)*. Files: `BlueprintDebugSession` · `BlueprintLiveValueWriter` · `VariableEditCommit`.

## 3. ⭐ `W5` — **move the restore** out of `RequestStep`/`RequestContinue` *(`R-63`; §10/§6)*.

## 4. ⛔ LANE & NOT-THIS-BATCH

⛔ No time-lane file *(`Fdp.Toolkits/Time/`, `ModuleHostKernel`, `Hrot.Orchestrator`)* — ⚠ **exception:
the ONE wire in `EditorSubsystem`** references `ResumeAndDrainSystem` *(time lane's class)*, which is why
it waits for the coordinator merge. ⛔ Not the drain itself *(time lane)*; not the details panel *(its own
dispatch)*.

## 5. ⭐ GATES

⭐ Standing contract *(rule 8)*: one row per gate · command · pass/fail/skip · delta · goldens as a diff
shape · `tracker-counts.py --check` · the **`BP-` ids you allocated** · `R-106` verdicts. ⭐⭐ **The new
rail:** *an edit stages, both a Details-shaped and a Watch-shaped row report `Pending` + the same staged
bytes; after a simulated drain, both clear.* ⭐ Rule 4/7: re-sync + pull the coordinator branch around each
batch.
