# 👀 Visual check v2 — after Batch 25

> **Step by step. Do this → expect exactly this.** Report the step number; you never need to diagnose.
> Supersedes [VISUAL_CHECK_StepByStep.md](VISUAL_CHECK_StepByStep.md).
>
> **~20 min.** Shorter than last time — most of what you found is fixed or already registered, so this
> only covers **what is genuinely new and unseen**.

---

## 🛑 Before you start

| ⚠ | |
|---|---|
| **1** | **Everything you reported last round is registered.** Nothing below re-tests it. If you hit a *known* bug mid-step the step says so. |
| **2** | ⚠ **The `Graph Signature` window is still broken** — [BP-125](Blueprint_Issues_Detail.md#bp-102), not yet fixed. **Add outputs from the `Return` node's `Details`, never from `Graph Signature`.** That path works; the other silently does nothing. This is the root cause of your *"could not wire"* reports. |
| **3** | ⚠ **A new function graph still has no `Return` node** (BP-126, not yet fixed). You will keep having to add one from the palette **and wire it** — an unwired one gives `BP3010`. |
| **4** | Delete scratch `.bp.json` from `Assets/` when done. |

---

## A · 🆕 The `Print String` node — brand new, nobody has seen it (~7 min)

| Step | Do | Expect |
|---|---|---|
| **A1** | Open any Instance blueprint's graph. Search the node palette for **`Print String`** | It exists |
| **A2** | Place it, wire exec into it from the entry node | Exec In + Exec Out, **no data pins yet** |
| **A3** | In `Details`, find the **Format** field. Type: `hello` | No data pins — a format with no placeholders takes no arguments |
| **A4** | ⭐ Change the format to `threat={Threat}` | ⭐ **A data-in pin named `Threat` appears on the node**, immediately |
| **A5** | Change it to `threat={Threat} squad={Squad}` | **Two** pins: `Threat`, `Squad`, in that order |
| **A6** | Change it to `{Squad} then {Threat}` | Still two pins, now ordered `Squad`, `Threat` — order follows **first appearance in the text** |
| **A7** | Use the same name twice: `{Threat}/{Threat}` | ⭐ **One** pin, not two |
| **A8** | Type a literal brace: `100{{percent}} {Threat}` | One pin (`Threat`). `{{`/`}}` are escapes, not placeholders |
| **A9** | Type a broken format: `threat={Threat` (no closing brace) | ⭐ **An error naming this node** (`BP2072`), not a silent drop |
| **A10** | Fix it, set each arg's type in `Details`, wire values in, set **Level** | Level offers **all five**: Trace / Debug / Info / Warn / Error |
| **A11** | Build the solution | ✅ 0 errors |
| **A12** | ⭐ Run and look at the **"AI Behaviors"** tab of the message log | ⭐ **Your line appears, with the values substituted.** ⚠ If it does **not**, that is [BP-124](Blueprint_Issues_Detail.md#bp-124) — already registered as untested. **Tell me either way; this is the single most valuable answer in this guide** |

## B · 🆕 The `Format String` node (~4 min)

| Step | Do | Expect |
|---|---|---|
| **B1** | Find **`Format String`** in the palette, place it | ⭐ **No exec pins** — it is a pure node, like Unreal's Format Text |
| **B2** | Set its Format to `t={Threat}` | A `Threat` data-in pin, plus a **`Result`** data-out pin |
| **B3** | In `Details`, find **ResultType** | Offers `FixedString32` / `64` / **`128`** — 128 is new |
| **B4** | ⭐ Wire `Format String`'s `Result` into a `Print String` placeholder pin, build | ✅ Compiles. This is the composition the whole two-node design exists for |
| **B5** | Set ResultType to `FixedString32` and use a format that clearly exceeds 32 chars, build, run | ⚠ **Silently truncated** — expected and documented, but tell me if it surprises you in practice |

## C · 🆕 `FixedString128` (~2 min)

| Step | Do | Expect |
|---|---|---|
| **C1** | Add a parameter, open the `Type` dropdown | `FixedString32`, `FixedString64`, **`FixedString128`** all present |
| **C2** | Pick `FixedString128`, build | ✅ No `BP1500` |

## D · 🆕 The sample assets are openable now (~3 min)

| Step | Do | Expect |
|---|---|---|
| **D1** | Open the asset picker for **existing** assets (not "new from recipe") | ⭐ **`SmokePatrol`, `SmokeGuard`, `SmokeMathLib` now appear.** This is what you could not find last time |
| **D2** | Open `SmokeMathLib` | A `Library` asset with a `Combine` function |
| **D3** | Open `SmokePatrol`, find its `CallPeerBlueprint` node | It targets `SmokeMathLib` |
| **D4** | Build the solution | ✅ 0 errors — ⭐ these are now generator-compiled, so a regression in a sample breaks the build before any test runs |

## E · 🆕 Peer calls, finally authorable (~4 min) — **this failed completely last time**

`BP-116` fixed the editor never recording the peer. This is the first time this path can work at all.

| Step | Do | Expect |
|---|---|---|
| **E1** | In an Instance blueprint, place a **`CallPeerBlueprint`** node | Placed |
| **E2** | In `Details`, pick a peer asset (e.g. `SmokeMathLib`), then pick its function | Both combos populate |
| **E3** | ⭐ **Build** | ✅ **No `BP1300`.** *(Last time this was guaranteed to fail — the editor never wrote `CallablePeers`)* |
| **E4** | Check the asset's `.bp.json` | ⭐ `"CallablePeers"` now contains the peer's GUID |
| **E5** | Point it at a **2-output** library function | **Two named data-out pins**, not one called `Return` |
| **E6** | Delete the node, check the JSON | `CallablePeers` is **emptied** (nothing else referenced it) |
| **E7** | Place a peer node, then **Ctrl+Z** the placement, check the JSON | ⚠ **The peer stays declared.** That is [BP-119](Blueprint_Issues_Detail.md#bp-119), known and registered. Just confirm |

---

## How to report

**Step number** (`A12`, `E3`), what you saw, and the **exact diagnostic code** if any.
A screenshot for anything about wording or layout.

⚠ **The three answers I most want:**
1. **A12** — does a `Print String` line actually reach the "AI Behaviors" tab? **No test covers this**, and it is the seam the whole design was built around.
2. **E3** — does a peer call compile now? It could not, for anyone, ever, before this batch.
3. **A4** — do pins really appear as you type placeholders? That is the Unreal-parity mechanism, and it replaced a design I had wrong.
