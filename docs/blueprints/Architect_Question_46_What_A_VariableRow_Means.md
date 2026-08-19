<!--STATUS
state: LIVE
updated: 2026-08-19
current-answer: section 2 — THE MODEL. It is the user's own specification, given verbatim
  on 2026-08-19, and it REPLACES the option-shaped question the first draft asked.
stale-below: nothing. The first draft's A/B/C option tables are gone, not moved down —
  the user answered the question, so the options no longer exist.
known-rot: none.
known-conflict: none.
-->
# ⭐ Architect Question 46 — **what is a watch row, and why is the value stuck?** *(`BP-344`)*

> ⛔ **Not relayed** — the architect is unavailable *(`2026-08-16`)*. ⭐ **`§2` is the USER'S OWN
> SPECIFICATION**, given on `2026-08-19`. ⛔ **It is not a recommendation of mine; it is the answer.**
> ⭐ What is left for me is `§4` — *how* to build it, and the two places the user's model meets
> something the code cannot yet do.

---

## 1. ⭐⭐ THE PROBLEM, in plain words

You pin a variable into the Watch window. **It shows the value it had at the moment you pinned it, for
ever.** The Details panel right next to it shows the same variable changing every frame.

⭐⭐ **Why:** a row carries a little function that returns the value. In Details that function is
**rebuilt every frame**, so it always returns the fresh value. When you pin, the row is **copied into
the Watch store** — and the copy keeps the function it had at that moment, which returns *that frame's*
number, not *the current* number.

```
frame1   Details = 10        Watch = 10
frame2   Details = 99        Watch = 10      ⛔ the pin froze
```

⇒ ⭐⭐⭐ **Nothing is broken about pinning, the window, or the store.** The row's value-function is a
**photograph** where it needed to be a **camera**.

### ⭐ The same bug, second face

`(pending)` means *"nothing has written this variable yet"*. That, too, is decided **once, when the row
is built**. ⇒ a variable the run starts writing **after** you pinned it says `(pending)` in the Watch
for ever, while Details shows its value.

---

## 2. ⭐⭐⭐ THE MODEL — **the user's specification, `2026-08-19`** *(this is the ruling)*

| # | rule |
|---|---|
| **1** | ⭐ **One row = one accessor.** Rows are **independent instances of one row class**, filled from the same or different sources. ⛔ **A Watch row and a Details row know nothing about each other** — they are not shared objects |
| **2** | ⭐⭐ **The accessor is called once per brain frame** — *"only when not in planning mode and only when the frame's `dt > 0`"*. ⭐ All rows are evaluated **at the same time**, on that one pulse. ⛔ **Not while the simulation is paused** — only when time actually stepped |
| **3** | ⭐⭐ **The value is CACHED on the row** and rendered **every UI frame from the cache, without calling the accessor** |
| **4a** | ⭐ **Pin while RUNNING-but-PAUSED ⇒ call the accessor immediately**, so the value is known from the very start |
| **4b** | ⭐ **Pin while PLANNING ⇒ do not call it.** The cell shows `(pending)` because the cache has not been filled yet — ⛔ **not because "nobody writes this variable"** |
| **5** | ⭐⭐ **A value the user typed is a SEPARATE cache on the row**, distinct from the value read through the accessor |
| **6** | ⭐⭐ **Change highlighting compares the accessor's value against the value cached from the last brain tick** |
| **7** | ⭐⭐ **It must work for structures of ANY size** — ⛔ not limited to a fixed number of bytes |
| **8** | ⭐⭐⭐ **And for managed values** — a class or a string sitting in a managed component's field, pinned as a watch item |
| **9** | ⛔⛔⛔ **Compare BYTES from the fast pre-compiled binary serializer. NEVER compare rendered text.** *(user, verbatim: "we have fast pre-compiled binary serializer mechanism for any component and i guess it can be used for any class. it produces bytes. we compare these bytes. No way comparing rendered text!")* |

### ⭐ What rule 2 changes that is easy to miss

Details **currently samples on every repaint** — 60×/s whether or not the world moved. Under rule 2 it
samples **once per brain tick** and draws from cache in between. ⇒ ⭐⭐ **less work, and Details and
Watch become the SAME behaviour** *(ruling 9 — one implementation, not two)*.

---

## 3. ⭐⭐ INVENTORY *(`R-74` — the graph and targeted greps, `2026-08-19`)*

```
search_graph(name_pattern=".*(VariableRow|RowSource|ReadValueObject|ReadRawValue|HasEverBeenWritten).*",
             file_pattern="Hrot/**")                                      → total 27
search_graph(name_pattern=".*(Serializer|Serialization).*", label="Class") → total 35
grep -n "AssetTick" over Hrot/ (excl obj)                                  → 4 production files
grep -rn "BlueprintAssetTickSource.Attach|.For" (excl tests)               → total 0
```

### ⭐ The four row sources — **two are affected**

| # | source | affected? |
|---|---|---|
| ⭐ **1** | **`SectionVariableRowSource`** *(Details, blueprint)* — object arm `:105`, byte arm `:118` | ⛔ **YES** |
| ⭐ **2** | **`BlackboardSectionRowSource`** *(Details, AI)* — same shape `:81`; passes `AssetTick: null` at `:95` | ⛔ **YES** |
| **3** | `FixedVariableRowSource` | ⭐ no — the caller supplies finished rows |
| **4** | `PinnedVariableRowSource` *(Watch)* | ⭐ no — it stores what it is given, **and that is correct** |

### ⛔⛔ The clock exists and NOTHING TURNS IT ON — `R-67`, exactly

📌 I previously reported *"no per-asset tick source exists."* ⛔ **Measured false.**
⭐ **`BlueprintAssetTickSource`** *(`Hrot.Blueprints.Editor/Variables/`)* is built, documented and
railed: `For(assetId, entity)` returns the per-`(blueprint, entity)` tick, and `Attach()`/`Detach()`
refcount the counter on.

| | |
|---|---|
| ⛔⛔ **production callers of `Attach()` / `For()`** | **ZERO** — tests only |
| ⛔ **row sources that pass `AssetTick`** | **one**: `WatchRowBridge:58`, and from a different source *(`watch.LastUpdateTick`)* |
| ⇒ | ⭐⭐ **the monitor returns `None` on its first line, in production, always** |

⭐⭐⭐ **This is the silent-default pattern verbatim** *(`R-67`: "a production caller that HAS a
dependency must PASS it")* — ⛔ **not a missing capability. A missing wire.**

### ⭐⭐ The serializer — **measured, it is real, and it has three teeth**

**`Fdp.Core.FlightRecorder.FdpAutoSerializer`** *(1604 lines)* — *"JIT-compiled serializer for managed
types… Expression Trees to generate zero-allocation serialization code at runtime"*. ⭐ **`Fdp.Core` is
already a `ProjectReference` of `Hrot.Editor.AiShared`** ⇒ nothing new to reference.

| ⭐ what it covers | |
|---|---|
| primitives · enums · `string` · `Entity` *(8-byte blit)* | ⭐ direct `BinaryWriter` calls |
| `T[]` · `List<>` · `Dictionary<,>` · `ConcurrentDictionary<,>` · `HashSet<>` · `Queue<>` · `Stack<>` · `ConcurrentBag<>` | ⭐ generated loops |
| **any other class or struct** | ⭐⭐ **CASE Z — recurses into `Serialize<T>` on the member type** |
| members chosen | public instance **fields** + read/write **properties**, `[JsonIgnore]` skipped, ordered by name |
| cost | first call per type ~1–5 ms *(compile)*, cached in a `ConcurrentDictionary` thereafter |

| ⛔ **tooth** | ⭐ **what it means for us** |
|---|---|
| **① it is GENERIC** — `Serialize<T>(T, BinaryWriter)` | ⛔ a watch row holds `object`. ⭐⭐ **The bridge already exists as a pattern**: `FdpPolymorphicSerializer.CompileWriteDelegate` builds `(writer, obj) => FdpAutoSerializer.Serialize<T>((T)obj, writer)` by `MakeGenericMethod` and caches it per `Type` — **~15 lines**. ⛔ But `FdpPolymorphicSerializer` itself only accepts types carrying `[FdpPolymorphicType]`, so we need the bridge **without** its registry |
| **② get-only properties are SKIPPED** *(`CanRead && CanWrite`)* | ⚠ a class exposing state only through computed getters serializes to **nothing** ⇒ its changes would be invisible. ⭐ Public fields are fine |
| **③ no cycle guard** | ⛔⛔ **a back-reference recurses until the stack dies.** *(`DeepClone`'s own doc says circular references are not supported.)* ⚠ **This is the one that can take the editor down, and it must be fenced** |

---

## 4. ⭐⭐ WHAT I RECOMMEND — **how to build `§2`**

### ⭐⭐⭐ `4a` — the accessor becomes a camera *(rules 1–3)*

⭐ Change the **two affected row sources** so their arms close over **the provider**, not over that
frame's value — `~4 lines per arm`, **both arms** *(object and bytes)*, ⛔ **never one**: a fix on the
object arm alone would make pinning work on Blueprint and silently freeze on BTree/HSM, which is exactly
the split `U-6` removed.

⭐ Add the **value cache and the sample pulse** to the row's owner: sample when the row's `AssetTick`
advances, render from cache otherwise.

### ⭐⭐⭐ `4b` — **turn the clock on** *(the whole monitor depends on it)*

| ⭐ | |
|---|---|
| **①** | **`Attach()`/`Detach()` on panel open/close** — Details and Watch both |
| **②** | **pass `BlueprintAssetTickSource.For(assetId, entity)` as `AssetTick`** from the two row sources |
| **③** | ⭐⭐ **rail the CONSTRUCTED row**, not the registrar's source *(`R-67`)* — assert `row.AssetTick is not null` on a row built by the production path |

⚠ **BTree/HSM have no equivalent source yet.** ⭐ Their rows stay `AssetTick: null` ⇒ **inert, never
wrong** — the row contract already says so. ⛔ **Do not invent a second clock for them in this batch;**
file it.

### ⭐⭐⭐ `4c` — change detection *(rules 6–9)*

| value kind | ⭐ how |
|---|---|
| **unmanaged / struct, any size** | ⭐⭐ **already solved** — `ReadRawValue` returns `ReadOnlySpan<byte>` and the monitor already does `SequenceEqual`. ⛔ **No size limit exists to remove**; rule 7 is met today |
| **managed (class, string)** | ⭐⭐⭐ **serialize to bytes with `FdpAutoSerializer` through a cached runtime-`Type` bridge, and compare those bytes** — rule 9 |

**The bridge, concretely:** one `ConcurrentDictionary<Type, Action<BinaryWriter, object>>`, filled by
the same `MakeGenericMethod` expression `FdpPolymorphicSerializer.CompileWriteDelegate` already uses,
plus **one pooled `MemoryStream` + `BinaryWriter` reused per sample** so a per-tick snapshot does not
allocate a stream per row.

**And the fence — ⛔ non-negotiable, because of tooth ③:**

> ⭐⭐ **The first time a type throws or exceeds a depth/size cap, record that TYPE as
> not-comparable and never serialize it again.** ⭐ Such a row simply **never highlights** —
> ⛔ it must never crash the editor, and ⛔ it must never fall back to comparing text.

⭐ **Byte arm first, object arm second**: the monitor reads only `ReadValue` today, so Blueprint's
object-arm values could not highlight at all. Both arms feed the same comparison.

### ⭐ `4d` — the two caches are two fields *(rule 5)*

⭐ `LastSampled` *(from the accessor)* and `PendingEdit` *(what you typed)* are **separate**.
⭐⭐ `RowHighlight(Changed, Pending)` **already carries both booleans** and already refuses to collapse
them — 🔴 *the sim changed it* vs 🟡 *your edit has not landed*. ⛔ Nothing to redesign here.

### ⭐ `4e` — `(pending)` follows the same route *(rule 4b)*

⭐ Add an **optional trailing `ReadHasEverBeenWritten` delegate, `null` by default, preferred when
present** — the exact shape Batch 90 established for `ReadValueObject`.
⭐ **Zero existing construction sites change** *(3 production, ~28 test)*. ⛔ **Do not widen the `bool`.**

### ⭐ `4f` — the rails invert, they do not die

`APinnedRowIsASnapshotTests` **asserts the defect on purpose and says so** ⇒ ⭐⭐ it is the acceptance
test for this fix. ⛔ Deleting it would remove the only proof the fix works.

### ⚠ `4g` — the `ToggleWatch` id *(`BP-346`)*

📐 `CommandCatalog.ToggleWatch = "editor.toggle-watch"` **exists** and is **pin-scoped**
*(`IDebugSession.ToggleWatch(PinId)`)*. ⛔ **My earlier handoff said it did not exist — false.**
⭐ The conclusion held: the **variable** gesture is unbuilt. ⇒ ⭐ **a distinct command id**, so nobody
silently binds the variable gesture to the pin-watch command.
⚠ Whether pin-watch and variable-watch are one concept is a `Q38`/`Q44` question — `R-27` gates it.

---

## 5. ⭐ What binds this

| id | binds |
|---|---|
| ⭐⭐ **`R-76`** | ⛔ **two clocks**: VALUE per tick · **BINDING only on selection change.** ⛔ Re-resolving a binding per tick churns row identity under the cursor |
| ⭐⭐ **`R-67`** | ⛔ **a production caller that HAS a dependency must PASS it** — `§3` is a textbook instance |
| **`BP-338`** | ⭐ `(pending)` is a per-name, per-frame **measurement** — ⛔ never *"a reader exists"* |
| **ruling 9** | ⛔ one implementation per concept — ⛔ **not two kinds of row**, and ⛔ not two sampling behaviours *(§2 note)* |
| **`R-49`** | ⛔ no per-variable codegen |
| ⚠ **spec §10** | ⛔ **watching variables from DIFFERENT assets in one panel is still OPEN** — *"the poll would span debug sessions"*. ⭐ `§4a` does **not** depend on it: a live closure needs no per-`(asset, entity)` source |

---

## 6. ⭐ Cost

⭐ **Small, and mostly already measured:** two arms × ~4 lines *(Batch 93 probed it — 1489/1490 AiShared
rails stayed green)* · one optional trailing delegate · the `Attach`/`For` wiring · one ~15-line
serializer bridge modelled on existing code · inverting five rails.
⛔ **The only genuinely new thinking is the fence in `4c`**, and it is a `try`/`catch` plus a set.
