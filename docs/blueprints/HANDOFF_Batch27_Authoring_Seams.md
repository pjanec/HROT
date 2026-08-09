# HANDOFF — Batch 27: make the string nodes and functions actually correct

> 📌 **Dispatched at `9f33b411`.** Frozen per `.claude/CLAUDE.md` →
> *Two-session protocol* rule 1. ⭐ **Rule 4 is yours:** pull the coordinator branch again before your
> final commit. ⭐ **Rule 3: the coordinator allocates no ids** — every `BP-2xx` below is a **placeholder**;
> renumber freely when you create the rows and say what you chose (rule 5).
>
> 📄 **Read [DECISIONS_Authoring_UX.md](DECISIONS_Authoring_UX.md) first.** All architect questions are
> now closed — **nothing in this batch is blocked**.
>
> ⭐ **The user has very limited visual-testing capacity right now. Everything here is chosen to be
> provable headlessly.** If an item cannot be proven by a test, say so rather than shipping it unproven.

---

## 0. ⭐ Answering the user's question: *"why could this not be done headlessly?"*

**It can. The matrix you built is right — it just stops three steps short.** Every defect below is
reachable without a UI, and here is exactly what is missing:

| Gap | What it would have caught |
|---|---|
| **1 · The matrix never RUNS the result** | It compiles and asserts 0 diagnostics. It never ticks the blueprint and checks a **value**. ⇒ **BP-201**: `Print String` printed `0` forever while the variable rose. It compiled perfectly |
| **2 · The matrix never tests WIRING ACCEPTANCE** | It authors nodes but never asks *"does the editor let me connect these two pins?"* ⇒ **BP-203**: an editor-authored `int` output cannot be wired to a `System.Int32` literal |
| **3 · ⭐ The matrix tests FINAL STATES, never EDIT SEQUENCES** | Every single defect this round came from a *sequence*: type a format, change it, reload. A static matrix cannot see those. ⇒ **BP-202**, **BP-204**, **BP-205** |

⇒ **Item 4 below extends the matrix along all three axes.** That is the systemic deliverable; the rest
are the specific bugs it should have found.

⚠ **This is the honest answer to a fair complaint.** The user has now found, by clicking, five batches
running. The matrix closed the *compile* class. These are the *run*, *connect* and *edit-sequence*
classes, and they were never covered.

---

## 1. 🔴 BP-201 — the editor never writes `ArgTypes`, so `Print String` prints `default`

> User: *"every second I got `[AI.Behavior.Blueprint] 0` — the value NOT following the Count variable
> (which rises in the Runtime inspector), so the blueprint works but the value is not printed properly."*

Their asset carries:

```json
{ "kind": "PrintString", "Format": "{threat}", "Level": "Info", "ArgTypes": {} }
```

⭐ **`ArgTypes` is empty, and `grep -rn "ArgTypes" Hrot.Blueprints.Editor/` returns NOTHING.** The
editor never writes it. The pin is derived from the format and rendered, the wire is accepted and
saved — and the **declared type of the argument is never recorded**, so emit falls back to a default
and prints `0`.

⚠ **This is BP-116's shape, second instance in three batches:** *a node property the compiler needs
that the editor never populates.* BP-116 was `CallablePeers`; this is `ArgTypes`.

**Fix:** populate `ArgTypes[placeholder] = typeId` from the detail panel, and default it from the
**connected pin's** type when a wire is made. ⚠ **Then assert the printed VALUE** — see item 4, axis 1.
A test asserting "a line was printed" passes today.

---

## 2. 🔴 BP-202 — changing a format leaves a dangling link and an unattributable build break

> `CSC : error BP1602: Link references unknown ToPinId 2f2db7d9… on node 8a6eb895…`
> User: *"I don't know what blueprint it was"* · *"removing the unwired Format String did NOT resolve
> it"* · *"the Print String LOST the pins and no editing of format restored them."*

Renaming a placeholder (`{Threat}` → `{threat}`) changes the derived pin's deterministic id. **The link
to the old pin id survives** ⇒ `BP1602` at build time, naming only GUIDs.

⚠ **The design note flagged this and understated it.** It said *"renaming a placeholder renames a pin,
which can drop a link — acceptable."* It is not a dropped link; it is a **dangling** link that breaks the
solution build and cannot be traced back to a node by eye.

**Fix:** when the derived pin set changes, **prune links whose endpoint no longer exists**, inside the
same undo record as the format edit. ⚠ Also explains *"lost the pins and no editing restored them"* —
the graph rebuild almost certainly aborts on the dangling link; verify that.

---

## 3. 🔴 BP-203 — the editor refuses wires the compiler would accept. **BP-114's sibling.**

> E5: *"Return node pins (shown properly) could not be wired to int and string literals."*

`BlueprintTypeSystem.cs:139-153` compares **raw TypeId strings**:

```csharp
if (from == to) return true;
if (from.Id == Int32 && to.Id == Single) return true;   // the ENTIRE coercion list
```

Two defects in four lines:

| | |
|---|---|
| **Alias vs FQN** | Graph Signature writes bare aliases (`"int"`, `"FixedString32"` — confirmed in their JSON); literals and recipes carry FQNs (`"System.Int32"`). `"int" != "System.Int32"` ⇒ **wire refused.** ⭐ **Anything authored in the editor is unwirable to anything from a recipe** |
| **⭐ A third hand-maintained coercion list** | The editor knows **one** rung (`Int32→Single`). `CoercionTable` has **35** (BP-87). ⇒ **the editor refuses 34 conversions the compiler accepts** — including the `ushort→int` the user's own ruling required |

**Fix:** resolve both sides through `StaticTypeRegistry` and compare canonical `FullName`; then
**delegate coercion to `CoercionTable`** rather than re-listing it. ⭐ **Same lesson as BP-87 item 5 and
BP-114: stop maintaining a second list.** This is the third instance.

---

## 4. 🔴 Item 4 — extend the matrix along the three missing axes

⭐ **The systemic item. Do it with the fixes, not after them.**

| Axis | What to add |
|---|---|
| **1 · Run and assert VALUES** | Tick the compiled blueprint; assert the **printed line's substituted value** and the **returned value**, not that it compiled. Reuses `BlueprintRunHarness` + `AiBehaviorLogTarget` (BP-124's test is the seed) |
| **2 · Wiring acceptance** | For each (output type × literal type) pair, author both, attempt the link, assert accepted/refused **and that the answer matches `CoercionTable`**. ⭐ This single axis catches BP-203 and every future editor/compiler drift |
| **3 · ⭐ Edit SEQUENCES, not final states** | place → set format → **change** format → assert pins re-derived **and no dangling links**; add output → reload → assert pins survive. **Every defect this round lived in a sequence** |

⚠ **Expect it to go red beyond the three bugs above. Register each as a row; do not fix them all here.**

---

## 5. 🟠 Smaller, all confirmed

| ID | |
|---|---|
| **BP-204** | The `Format` field records **one undo entry per keystroke** (*"undo was going back each typed char"*). Wrap the edit, as Batch 26 item 3 requires for Graph Signature — **same fix shape, do them together** |
| **BP-205** | Selecting a different node **does not re-seed the Details text buffer** — the user saw `Format String` keep `Print String`'s text. ⚠ **BP-86's family** (`ImGuiBufferText`); re-seed on selection change |
| **BP-206** | ⭐ **Diagnostics carry GUIDs only.** `BP1602` named a node id and nothing else. **Every diagnostic must carry blueprint name + graph name + node display name.** ⚠ The user could not tell *which asset* failed — with 40 assets that is a search, not a fix. Cheap and it pays back on every future bug report |
| **BP-207** | My Blueprint items open on **double-click** but look like buttons. ⚠ **Check Unreal before changing behaviour** — it also uses double-click to open and single-click to select, so the fix is likely the **affordance**, not the gesture. Third instance of the double-click-with-no-hint pattern (BP-75, BP-90) |

---

## 6. ✅ Confirmed working — do not re-litigate

`Print String` exists, placed with no pins · pins appear **as you type** placeholders (A4–A6) · repeats
collapse to one pin (A7) · `{{`/`}}` escape (A8) · **`BP2072` fires with a clear message** (A9) ·
`FixedString128` selectable · **`SmokePatrol`/`SmokeMathLib` open, and their peer call targets `Combine`**
· ⭐ **E1–E4: a peer call authored in the editor compiles, and `CallablePeers` is written** — BP-116
confirmed fixed in the field.

---

## 7. Gates

Same eight, `--logger "console;verbosity=normal"`. **Baseline: Batch 26's numbers** (measure them on
merge; Batch 25's were build 0 errors · Blueprints **2999 / 0 / 10**).
⚠ Items here touch the editor **and** the type system — run all eight.

## 8. Reporting

Per-suite numbers · revert-goes-red per item · **every BP id you allocated** · what the extended matrix
found · anything here wrong against the code.

---

## 9. 🆕 Newly unblocked by the decisions doc — same batch if there is room

| | |
|---|---|
| **Graph rename** *(was BP-127)* | ⭐ **No longer blocked.** It goes in **My Blueprint's context menu**, where Unreal puts it — not an empty-canvas panel. Small, and headless-testable through the authoring API |
| **`Return.Status` → `Success : bool`** *(was BP-131)* | Settled — see D3. AiPrimitive Return gets one `Success : bool` data-in pin; `Running` comes only from the latent lowering; no status surface anywhere else. ⚠ **The ABI does not change** — the method still returns `NodeStatus`. ⚠ Zero-output Library should become `void`; that is the one genuinely test-locked piece |
| **Retire `Graph Signature`** *(was BP-128)* | ⭐ **Much smaller than the design note assumed.** With rename in My Blueprint and Inputs/Outputs already on the entry/Return nodes, this is mostly deletion. **Do it only after the matrix's edit-sequence axis exists**, so the removal is covered |

⚠ **Deferred, recorded, do not start:** Unreal's `Class Defaults` bulk-defaults view (D2), and the
34-warning triage (D6 — wait for diagnostic names, and separate compiler-synthesized orphans from
authored ones).

⭐ **After this batch: macros.** BP-77 / BP-79 and the Q25 answers are the next feature arc; this batch
is the last of the correctness sweep.
