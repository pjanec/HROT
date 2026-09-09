# ⭐ VISUAL CHECK — the step-by-step guide

> **Coordinator, `2026-08-14`.** ⭐ **Seventeen batches of editor work have never been looked at.**
> Everything below is **already built and headlessly green** — this check asks the one question no
> xunit test can: **is it actually on screen and does the gesture work.**
>
> ⛔ **What this does NOT check:** `U-6` (Details hosts the table), `U-13` (shared-state view) and
> `U-16` (retire the standalone window) — ⚠ **they are not built.** ⭐ **They are waiting on THIS
> check**, because nobody wants a second panel surface stacked on an unverified first one.
>
> 📌 **Each step names the tracker row it reopens if it fails**, so a "no" is immediately actionable.
> ⭐ **A "no" is a good outcome** — it is the whole reason for running this.

---

## 0 · Before you start

| | |
|---|---|
| **Build** | `dotnet build IOS-IG-SimHost.sln` — ✅ coordinator-verified **0 errors / 69 warnings** at `e202dbed5` |
| **Launch** | the Stride editor app as you normally do (`HrotStrideApp.Windows`) |
| ⭐ **The asset to open** | **`LibraryFunctionsDemo`** — it has **three Function graphs**, which is what §A3 needs. Any Instance blueprint works for the rest |
| ⚠ **One thing to know first** | ⭐⭐ **every shipped asset is now `schemaVersion: 2` on disk** (Batch 55). §C1 is where that gets its only human check |

⭐ **Record as you go** — the table in §D is written to be pasted straight back to me.

---

## A · The **Local Variables** section — *the main event*

📌 **Batch 43's entire deliverable is a panel surface.** ⭐ **`BP-57` was closed on it and it has never
been seen.** In **My Blueprint**, the sections must read, top to bottom:

> **Graphs · Functions · Macros · Custom Events · Variables · Local Variables**

| # | do this | ✅ expect | ⛔ if it fails |
|---|---|---|---|
| **A1** | Open `LibraryFunctionsDemo`. Look at **My Blueprint** | **six** sections, in exactly that order. **Local Variables** is **last** and has a **`+`** | `BP-57` reopens — the section never rendered |
| **A2** | Click **`+`** on **Local Variables**. Name it `tmp`, type `int`, confirm | it appears under **Local Variables** — ⛔ **not** under *Variables* | `BP-57` |
| **A3** | ⭐⭐ **Double-click a different graph** in the **Graphs** section | ⭐ **the Local Variables list CHANGES** — `tmp` disappears, because it belongs to the other graph | 🔴 the section captured a stale graph instead of following the canvas |
| **A4** | Double-click back to the first graph | `tmp` is back | as A3 |
| **A5** | Right-click `tmp` → **Rename** → `counter` | renames in place; the canvas still resolves it | `BP-57` / `BP-12b` |
| **A6** | Right-click `counter` → **Duplicate** | a second local appears with a **non-colliding** name | `BP-12b` |
| **A7** | Right-click it → **Delete** | it goes | `BP-12b` |
| **A8** | ⭐ **`Ctrl+Z` once** | ⭐⭐ **the deleted local comes back in ONE undo** — ⛔ not two, not zero | 🔴 `BP-57`'s undo — locals had **no** undo before Batch 43 |
| **A9** | Drag `counter` onto the canvas (or right-click it → **Get**) | a **Get** node appears and wires | `BP-57` |
| **A10** | With that Get node still wired, right-click `counter` → **Delete** | ⭐⭐ **it REFUSES, out loud, in a visible message** naming that it is still referenced | 🔴 the reference count is not reaching the delete |

### ⭐⭐ A11 — the refusal that must **not** disappear

| | |
|---|---|
| **do** | open (or create) a **Macro** graph, then press **`+`** on **Local Variables** |
| ✅ **expect** | ⭐ **the `+` is STILL THERE and REFUSES OUT LOUD**, naming the reason |
| ⛔ **fail** | the `+` is greyed out or the section vanished |
| 📌 **why** | `Q26-B2` — *"the `+` stays and refuses out loud, naming the reason, rather than vanishing and teaching nothing."* ⚠ **A vanished button is indistinguishable from a broken one** |

### A12 — the legal shadow *(`Q27-C1`)*

Create an **asset** variable named `hp` (**Variables** `+`), then a **local** also named `hp` in one
graph. ✅ **Both are legal and both must be accepted** — a graph local deliberately shadows an asset
variable. ⛔ **If the second is refused, that is a wrong refusal**, not a safety net.

Then try a **second local also named `hp` in the SAME graph** ⇒ ✅ **that one must be refused.**

---

## B · The variables table tells the truth

📌 **Batch 46 (`BP-230`) and Batch 47 (`BP-87`/`U-8`).** ⭐⭐ **`BP-230` was a combo that was DRAWN,
LIVE, and whose result was DISCARDED** — the user moved it and nothing happened.

| # | do this | ✅ expect | ⛔ if it fails |
|---|---|---|---|
| **B1** | Select any variable and look at its **Role** and **Scope** | ⭐⭐ **plain TEXT** (`state` / `input`, and the scope name) — ⛔ **NOT dropdowns** | 🔴 `BP-230` reopens. `Q-k` ruled these **read-only for blueprints**; a combo here is a lie |
| **B2** | Open the **type** dropdown on the create-variable modal | ⭐ **exactly 18 entries**: `bool byte sbyte short ushort int uint long ulong float double Vector2 Vector3 Vector4 Quaternion FixedString32 FixedString64 FixedString128` | `BP-87` |
| **B3** | ⭐ **Look for `System.String` and `System.Object`** | ⛔⛔ **they must NOT be there** | 🔴 `BP-87` — `System.String` **was** offered and **can never compile** |
| **B4** | Pick any one of the 18, create the variable, then build | it compiles | 🔴 `U-8`'s claim was *"every offered type compiles"* |

---

## C · Save, and the v2 bump

| # | do this | ✅ expect | ⛔ if it fails |
|---|---|---|---|
| **C1** | ⭐⭐ Open a shipped asset, change **one** thing, **Save**, then open the `.bp.json` in a text editor | ⭐ **still INDENTED and human-readable**, and `"schemaVersion": 2` at the top | 🔴 `BP-227` — saving used to **collapse the whole file to one line** |
| **C2** | `git diff` that file | ⭐ **a small diff around what you changed** — ⛔ not ~160 lines of reformatting | `BP-94` (known-open; ⭐ **confirm whether Batch 49's canonicalisation already fixed it** — that is genuinely unknown) |
| **C3** | Close and reopen the asset in the editor | your change is there and the graph renders | 🔴 the v2 round trip through the **real editor** — the one path the golden harness cannot walk |

---

## D · Paste this back

```
A1  sections + order ......  [ ]     A9  get node ............  [ ]
A2  create under Locals ...  [ ]     A10 delete refuses ......  [ ]
A3  follows the canvas ....  [ ]     A11 macro + refuses .....  [ ]
A4  switches back ..........  [ ]     A12 shadow legal/dup no .  [ ]
A5  rename ................  [ ]
A6  duplicate .............  [ ]     B1  role/scope are TEXT ..  [ ]
A7  delete ................  [ ]     B2  18 types .............  [ ]
A8  ONE undo restores .....  [ ]     B3  no String/Object .....  [ ]
                                      B4  chosen type compiles .  [ ]
C1  indented + v2 .........  [ ]
C2  small diff ............  [ ]     anything odd not listed:
C3  reopens clean .........  [ ]
```

⭐ **Screenshots only where something looks wrong** — the checklist carries the rest.

---

## ⭐ What each outcome unlocks

| result | next |
|---|---|
| **§A all green** | ⭐⭐ **`BP-57` is verified for real** and `U-6` / `U-16` can be built on it — **the last three tasks in the programme** |
| **§A has reds** | ⭐ **better now than after `U-16` deletes the other editing surface** — that is exactly the risk this check exists to retire |
| **§B red** | `U-5`/`U-8` shipped a surface that still lies; ⛔ small fixes, but they belong before `U-6` moves the table into Details |
| **§C red** | 🔴 **stop and tell me immediately** — C1/C3 are the only human check on the v2 bump |
