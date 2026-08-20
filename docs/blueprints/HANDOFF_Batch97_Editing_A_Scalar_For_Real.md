<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: this whole file — the Batch 97 dispatch.
stale-below: nothing.
known-rot: none.
known-conflict: none. It finishes what Batch 96 measured and stopped on: BP-356 (a
  scalar's edit goes nowhere) and BP-358 (no live writer is wired).
-->
# HANDOFF — Batch 97: **editing a scalar, for real**

> 📌 **Dispatched at `d5f18e2b2`.** ⭐ **Branch from the handoff commit** *(rule 7)*.
> ⛔⛔ **YOUR SCOPE IS FROZEN AT THIS SHA.** ⚠ **If a later document INVALIDATES an item — STOP AND
> REPORT.** ⭐ **Rule 3: allocate your own ids.** ⭐ **Rule 1b: push
> `chore: started batch 97 at d5f18e2b2` FIRST.**
> ⭐⭐ **RE-STAMPED after a FEASIBILITY PASS** *(rule 1a — checked first: your remote head was
> `d5f18e2b2` with **no `started batch 97` marker**, so no run was in progress)*. ⭐ **Batch 96 was right
> to flag the un-re-stamped amendment; this is the correct form.** ⛔ **It will not be amended again.**

> ## ⭐⭐⭐ THE USER SPECIFIED THIS BATCH
> ⭐⭐ **User, verbatim:** *"i need a batch that finally allows me to edit plain scalar non-struct fields
> (maybe a special generic struct with a single field passed to StructEdit) and allows me to write and
> disable "Edit…" if value is not editable and will be able to call the blueprint writer."*
>
> ⇒ ⭐ **Three items. All three were MEASURED by Batch 96** *(`BP-356`, `BP-358`)*, so ⛔ **there is no
> design question left in this batch** — and the user's own suggestion is the option Batch 96
> independently costed as the cheaper one.
>
> ## ⭐⭐⭐ FEASIBILITY — **verified by the coordinator BEFORE dispatch**
> ⭐⭐ **User:** *"you need to verify that the stuff is possible at all. otherwise they will return back
> with empty hands again."* ⇒ ⭐ **All three verified against the sources. §2.1, §3.1 and §4.1 carry the
> proof, and §4 got SMALLER because of it.**

---

## 1. ⭐⭐⭐ THE ACCEPTANCE, IN THE USER'S WORDS

> ⭐⭐ **Open `Count4`, right-click `Count`, "Edit value…", type a number, press OK, and the value
> changes.** ⛔ **Anything less is not this batch done.**

⚠ **Batch 96 got the dialog to open, draw and not crash. ⛔ It still cannot change a scalar** — this
batch is the remaining half, and it is the whole reason the feature exists.

---

## 2. 🛠 **`97a` — a scalar's edit must LAND** *(`BP-356`)*

### 📐 The cause, already measured by Batch 96

```csharp
// ReflectionEditDocumentBuilder.CreateLeafBinding — first line
if (fi == null && pi == null) return null;      // ⛔ a binding needs a MEMBER
```

⇒ ⭐ a **DTO** variable's root is a `Struct` whose **children** are bound ⇒ ✅ editing works.
⇒ ⛔⛔ a **scalar** variable **IS** the leaf, and a root has no member ⇒ `Binding == null` ⇒
`DrawLeafNode` ends `node.Binding?.SetBoxed(value)` — ⭐ **a null-conditional that silently discards
the typing**, and `Commit()` can only return the seed.

### ⭐⭐⭐ Build the USER'S option — **the one-field wrapper, inside `Hrot.Editor.AiShared`**

> ⭐⭐ **User:** *"maybe a special generic struct with a single field passed to StructEdit."*
> ⭐ **Approved, and it is also what Batch 96 costed as the cheaper alternative** *(§3.3)*.

| ⭐ | |
|---|---|
| **the shape** | a `struct ScalarBox<T> { public T Value; }` *(name yours)* — ⭐ **a public FIELD**, because that is what `CreateLeafBinding` needs |
| **open** | when the variable's type is **not** a struct/class with editable members, open the session over `ScalarBox<T>` seeded from the value ⇒ ⭐ the root becomes a `Struct` with **one bound child** |
| **commit** | unwrap `.Value` back to the variable's own type **before** `CommitAndSerialize` / before the bytes go to the writer ⇒ ⛔ **the JSON and the live bytes must be the SCALAR, never the wrapper** |
| ⭐ **a bonus, and it is the right look** | it draws as a labelled row rather than a bare unlabeled input |

⛔⛔ **Do NOT change `StructEdit`.** ⭐ It is `FDP/ExtDeps` with its own suite, and a root binding there
touches **every** scalar-rooted edit session in the editor. ⚠ **If the wrapper turns out not to work,
STOP AND REPORT** — ⛔ do not fall back to editing `StructEdit` without saying so first.

### ✅✅ 2.1 FEASIBILITY — **verified, and it needs NO registration**

📐 **The whole chain, read `2026-08-19`:**

| step | `ScalarBox<int>` | ⭐ verdict |
|---|---|---|
| `DefaultComponentMemoryClassifier.Classify` | value type · blittable | ⇒ `UnmanagedBlittableStruct` |
| `ComponentEditService.CreateBuffer` | ⇒ `NativeStructEditBuffer(type, component, RuntimeTypeOpsFactory.Get(type))` | ✅ |
| ⭐⭐⭐ **`RuntimeTypeOpsFactory.Get`** | `_cache.GetOrAdd(type, …)` ⇒ **`typeof(RuntimeTypeOps<>).MakeGenericType(type)`** + a static field read | ✅✅ **works for ANY unmanaged struct — ⛔ NO registration, NO codegen, NO component table** |
| `ReflectionEditDocumentBuilder.DetermineKind` | `IsValueType` ⇒ **`Struct`** | ✅ a container, not a leaf |
| its children | the **public field `Value`** ⇒ `CreateLeafBinding` with **`fi != null`** | ✅✅ **BOUND — which is the whole point** |

⭐ **`ScalarBox<string>` also works, by a different door:** a string field makes it **non**-blittable ⇒
`Classify` returns `NonBlittableStruct` ⇒ `BoxedStructEditBuffer` ⇒ ⛔ **`RuntimeTypeOps` is never
reached**, so its `where T : unmanaged` constraint cannot bite.

⚠ **The one trap:** `MakeGenericType` **throws** if that constraint is violated. ⭐ `Classify` is what
keeps it away — ⛔ **do not call `RuntimeTypeOpsFactory.Get` yourself**; let `CreateBuffer` choose.

### ⭐⭐ Which types take the wrapper — ⛔ **decide by MEASUREMENT, not by a list**

⭐ `DetermineKind` already classifies: `Boolean` · `String` · `Guid` · `DateTime` · `Enum` · `Scalar`
are **leaf** kinds; `Struct`/`Class`/`Record`/arrays are containers.
⇒ ⭐⭐ **wrap exactly the leaf kinds** — ⛔ not "int and float", and ⛔ not a hand-written type list that
the next primitive falls off.

### ⭐ Rails

⭐⭐ **Flip `AScalarVariablesEditGoesNowhere`** *(Batch 96 asserted the defect on purpose)* — ⛔ **do not
delete it.** ⭐ **And assert the ROUND TRIP through the production commit path**: seed a value, change
it through the session, commit, and read back **the scalar** — ⛔ not the wrapper, on both the JSON arm
and the bytes arm.

---

## 3. 🛠 **`97b` — "Edit value…" must be GREYED when the value is not editable**

### 📐 The gap

```csharp
// VariableTableControl.DrawRowMenu — today
bool writable = row.CanEverBeWritten;
if (ImGui.MenuItem("Edit value…",  null, false, writable)) …
if (ImGui.MenuItem("Properties…",  null, false, writable)) …
```

⛔⛔ **The menu consults ONLY the row kind.** ⭐ The real policy is `VariableEditPolicy.Resolve(action,
runState, row)`, which also knows **Replay ⇒ Denied** and **not-writable ⇒ ReadOnly** — ⇒ ⛔ **the entry
is enabled in states the dialog will only ever show read-only, or deny.**

### ✅ 3.1 FEASIBILITY — **trivial**

📐 `VariableEditPolicy.Resolve(action, runState, row)` is a **public static pure function** already in
the same assembly, and 📐 `VariableTableControl` **already holds `RunState`** *(it feeds
`VariableWatchGesture.Decide` two lines below)*. ⇒ ⭐ **nothing to plumb.**

### ⭐⭐⭐ The shape is ALREADY IN THIS METHOD, two lines below

⭐ `VariableWatchGesture.Decide(row, runState, isPinned)` returns `(Enabled, Label, DisabledReason)` and
the menu greys with a tooltip. ⇒ ⭐⭐ **mirror it exactly** — ⛔ a second spelling of the rule is how the
two drift *(ruling 9)*.

| ⭐ | |
|---|---|
| **①** | a **pure `Decide`** for the edit gestures, over `VariableEditPolicy.Resolve` — ⛔ **do not re-implement the matrix**, call it |
| **②** | **greyed + a tooltip that says WHY**, in the designer's terms — 📌 *"same information value, no false expectations"* |
| **③** | ⭐⭐ **`ReadOnly` is NOT `Denied`.** 📌 `VariableEditing.Open`'s own doc-comment: *"`ReadOnly` still OPENS — properties are read-only mid-run, not absent; refusing to open would hide the values a designer wants to read."* ⇒ ⭐ **keep it enabled and open it SHAPED AS A VIEW** *(Batch 96 already did the shaping)*; ⛔ **grey only what is genuinely `Denied`** |
| **④** | ⭐ the tooltip must name the **actual** cause — ⛔ never the three-way *"node-owned, passthrough, or stale"* guess when the row's own kind says which |

⭐ **The rail is `Decide`'s truth table**, host-free — ⛔ and **say in the report that the menu's
RENDERING of it is unrailed** *(`R-21`/`R-62`, `M-29`)*.

---

## 4. 🛠 **`97c` — call the blueprint writer** *(`BP-358` — Blueprint ONLY)*

### 📐 Batch 96 measured it and stopped exactly here

| | |
|---|---|
| ⭐⭐ **the writer EXISTS** | **`IBlueprintDebugSession.TryWriteWorkingStateField`** — real production code *(Batch 84 `59c`)*, stages via `IDataBreakpointManager.StageFieldMutation`, **refuses unless frozen**. ⛔ **ZERO production callers** |
| ⛔⛔ **BTree / HSM** | **no live write path exists at all**, and the component-relative offset is not a seam. ⚠ `TryResolve`'s offset is **within `BehaviorParameters`**, ⛔ **not within the component** |

### ✅✅ 4.1 FEASIBILITY — **verified, and the item is SMALLER than the handoff first said**

📐 **The seam is ALREADY PUBLIC and already on the interface:**

```csharp
// IBlueprintDebugSession.cs:193 — with a `=> false` default, implemented at BlueprintDebugSession:913
bool TryWriteWorkingStateField(Entity entity, Type componentType, int fieldOffsetBytes,
                               ReadOnlySpan<byte> bytes);
```

⇒ ⛔ **"make it public" is NOT the work.** ⭐⭐ **The one missing piece is `name → (componentType,
fieldOffsetBytes)`**, and the walk that has it is `CaptureInstanceStateFromDefinition:1348` —
`mapIndex.StateLayout.Fields` of **`StateLayoutField(Name, Type, OffsetBytes, SizeBytes)`**, with a
fallback to `def.StateFields` when the layout is absent. ⭐ **Both arms are right there and both are
keyed by NAME.**

### 🔴🔴 4.2 THE ONE THING THAT CAN CORRUPT MEMORY — **read this twice**

⭐⭐⭐ **The writer applies the +8 ITSELF.** Its own doc-comment: *"pass the offset as the layout reports
it. ⛔ Do not add the 8-byte header — the implementation owns that (`WorkingStateLayout`), so the read
path and the write path cannot disagree by 8 bytes."*

📐 And `TryWriteWorkingStateField:927` does exactly that: `WorkingStateLayout.ComponentOffsetOf(fieldOffsetBytes)`.
📐 ⚠ **But the READ walk you are copying from ALREADY converted** — `int start = WorkingStateLayout.ComponentOffsetOf(field.OffsetBytes);`

⇒ ⛔⛔⛔ **Your resolver must return the RAW `field.OffsetBytes`, NOT `start`.** ⚠ **Passing `start`
double-applies the header and scribbles on the neighbouring field** — 📌 `Q32` §2.1: *"an out-of-range
offset is MEMORY CORRUPTION, not a wrong value."* ⭐⭐ **Rail this specific mistake**, not just the
happy path.

⚠ **Second detail:** `componentType` is the **COMPONENT** the working state lives in — ⛔ **not the
field's type.** ⭐ Take it from the same walk; if the walk does not name it, **STOP AND REPORT** rather
than guessing.

### ⭐ Build — **Blueprint, and say so**

| ⭐ | |
|---|---|
| **①** | ⭐⭐ **a name → `(componentType, rawOffsetBytes, sizeBytes)` resolver on `IBlueprintDebugSession`**, ⭐ **built from the walk the READ already does** *(`StateLayout.Fields`, `def.StateFields` fallback)* — ⛔ **do not duplicate the walk**; if it is not reusable as-is, say so |
| **②** | ⭐ **wire `writeLive` at the composition root**, the same route `liveValueProvider` takes *(`EditorSubsystem`)* — ⛔ **not a new interface**, and ⛔ **not a default inside the binder** |
| **③** | ⛔⛔ **BTree/HSM keep returning `LiveWriteUnavailable`, and that stays HONEST** — ⭐ its message already says *"no live writer is installed for this host."* ⚠ **Do not fake one.** 📌 `Q32` §2.1: *"an out-of-range offset is MEMORY CORRUPTION, not a wrong value"* |
| **④** | ⭐ the writer **refuses unless frozen** — ⭐⭐ **that is correct and must stay.** ⇒ `97b`'s tooltip must say *"pause the simulation to edit a live value"* rather than letting OK fail |

### ⭐⭐⭐ The rail — **the one that has been missing all week**

> ⭐⭐ **Construct the production binder through the real composition root, commit an edit, and assert
> the WRITE LANDED** — ⛔ **not** that `writeLive` is non-null. 📌 `M-22`, `M-29`.
> ⭐ **Say which layer is faked**, as Batches 95 and 96 both did well.

---

## 5. ⛔ WHAT MUST NOT BE BUILT

| ⛔ | why |
|---|---|
| **a root binding in `StructEdit`** | `97a` — `FDP/ExtDeps`, own suite, blast radius is every scalar-rooted session. ⭐ The wrapper is inside our assembly |
| **a hand-written list of "scalar" types** | `97a` — ⭐ `DetermineKind`'s leaf kinds already are the list |
| **a second copy of the editability matrix** | `97b` — ⭐ call `VariableEditPolicy.Resolve` |
| **greying `ReadOnly`** | `97b` — ⭐ read-only still opens, by design; ⛔ grey only `Denied` |
| **any BTree/HSM live writer** | `97c` — ⛔ the offset seam does not exist; guessing it corrupts memory |
| **`96e`** *(the dead outline watch entry)* | ⭐ still last and still droppable — ⛔ not in this batch |

---

## 6. ⭐ GATES

⭐ **Baseline** = Batch 96's table, base sha **`d5f18e2b2`** · tracker **open 78 / done 217** ·
rulings **70/70**.
⭐ **Same seven-row contract** — `--no-build` column, `EXIT=` unfiltered, revert-goes-red per item.
⚠ `WhenNodePerfTests.Spawn_ZeroAllocation` is a known flake *(`BP-111`)*.
⛔ **`97a` touches nothing in `FDP/ExtDeps`** — ⭐ if it does, that is a deviation and must be reported.

### ⭐ Extra, this batch only

| ⭐ | |
|---|---|
| ⭐⭐⭐ **the round trip** | for `97a`: seed → edit → commit → **read back the SCALAR**, on both the JSON arm and the bytes arm |
| ⭐⭐ **whose object** | per rail, name the object the input came from — 📌 `M-29` |
| ⭐ **`97c`** | whether the read's walk was reusable, and **which layer the write rail fakes** |
| ⭐ **the unrailed draw** | ⛔ **say it plainly again.** ⭐ Batch 96's honesty about this was right |

---

## 7. ⭐⭐ WHAT THE USER DOES NEXT

⭐ **They re-run the visual check** and expect: open `Count4` → right-click `Count` → **"Edit value…"**
→ **type** → **OK** → ⭐⭐ **the value changes.**
⚠ **Still expected, not findings:** a pin does not survive a scenario reload *(`94g`)* · the outline's
watch entry is greyed *(`96e`)* · two entities share one sample cache *(`BP-352`)* · **BTree/HSM refuse
a LIVE edit and say why** *(`97c`③)*.
