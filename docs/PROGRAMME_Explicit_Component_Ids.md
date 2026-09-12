<!--STATUS
state: LIVE
updated: 2026-09-12
build-state: DESIGN — the exact inventory (step 1) has NOT been run. Every number below that is marked
  "crude" must be re-measured before any code moves.
current-answer: §2 (what was measured) and §4 (the steps). Start at §4 step 1.
known-rot: none yet — this document was created from the measurement in §2 and nothing has superseded it.
related-designs:
  - DESIGN_Role_Affinity_Ownership.md — §3.9b asks whether a role's component MASK can drive
    registration. That mechanism needs a complete id→Type map, which is exactly what this programme
    establishes. ⇒ this is a PREREQUISITE for mask-driven registration, not a side quest.
-->

# ⭐⭐ Explicit Component Ids — **are they actually explicit, and do tests share production's ids?**

> 🔒 **User, `2026-09-12`:** *"If there are still tests not working with explicit entity id, this is a
> serious issue. worth recording as a programme i can run in another session."*

---

## 1. ⭐ The question, in one line

⛔ **`FdpConfig.EnforceExplicitComponentIds` defaults to `false`**, and when it is `false` a component
struct with no `[ComponentId]` attribute gets a **sequential auto-assigned id**. ⇒ ⚠ **the same type can
hold a different id in a test than in production**, and component ids are not a private detail here:

| 🔒 why an id is load-bearing | source |
|---|---|
| ⭐⭐ ids are **globally unique and partitioned**, and that is **required for multi-process determinism** | `R-44` |
| ⭐⭐ ids **appear in replays and saved scenarios** ⇒ renumbering breaks stored data | `R-42` |
| ⭐ registration **throws** on an explicit-id collision | `ComponentType.cs:128-134` |

---

## 2. 📐 WHAT WAS MEASURED — **`2026-09-12`, and two of these are CRUDE**

| # | measurement | ⭐ verdict |
|---|---|---|
| **①** | **Only THREE call sites set the flag `true`** — `Hrot/Runner/Hrot.ClusterRunner/Program.cs:51`, `FDP/Examples/Fdp.Examples.Showcase/Program.cs:16`, `FDP/Examples/Fdp.Examples.Runner/Program.cs:20` | ✅ exact *(graph + grep agree, 5 files / 5 lines)* |
| **②** | ⛔⛔ **`FdpConfig.cs:98`'s own doc-comment is WRONG about where it is set** — it says *"Set to `true` in production entry-points (SimHost, IG, ExCon `Program.cs`)"*, and **none of those three files sets it**. They are launched through `Hrot.ClusterRunner`, which does ⇒ the statement is true of the PROCESS and false of the FILES it names | ✅ exact — ⭐ fix the comment as step 0, it is actively misleading |
| **③** | ⛔ **NO test sets it** ⇒ every test world runs in legacy auto-assign mode | ✅ exact *(zero test hits in the same sweep)* |
| **④** | **167** `[ComponentId(` declarations in production vs **~281** distinct type tokens inside `RegisterComponent<…>` | ⚠⚠ **CRUDE — do not quote.** The 281 counts generic parameters (`RegisterComponent<T>` inside helpers) and same-named types across namespaces. ⭐ The real number is step 1's job |

### ⚠ What the gap does and does NOT mean

⛔ **It is NOT silent corruption in production.** Under the runner the flag is `true`, so a type with no
attribute **throws at registration** — fail-fast, loud. ⇒ the production process is self-policing.

🔴 **The exposure is elsewhere, and it is real:**

| # | the hazard | why it bites |
|---|---|---|
| **A** | ⭐⭐⭐ **a test asserts id-keyed behaviour against ids production never uses** | masks, `BitMask512` bit positions, serialized component keys, replay frames. ⚠ The test passes on a layout that does not exist in production |
| **B** | ⭐⭐ **a host that is NOT launched through `Hrot.ClusterRunner` never enforces** | ⛔ the Stride app is a separate entry point *(`HrotStrideApp.Game`, not a `Program.cs` in the runner's tree)* — ⚠ **UNVERIFIED whether it sets the flag; step 1 must check** |
| **C** | ⛔ **a type that only tests touch can stay un-attributed indefinitely** | it never reaches the runner, so nothing forces the id. ⇒ the moment it ships, boot throws |

---

## 3. ⛔⛔ THE CONSTRAINT THAT GOVERNS EVERY FIX — **never renumber an existing id**

🔒 `R-42`: ids **appear in replays and saved scenarios**. ⇒ ⭐⭐ **assigning an id to a type that has none
is safe; changing a type's existing id is a DATA-BREAKING change** and needs its own decision.
⚠ And the budget is finite: `R-44` — **`MAX_COMPONENT_TYPES = 256`**, partitioned across
`GlobalComponentIds` / `HrotComponentIds` / … ⇒ step 2 must allocate from the correct partition, not the
next free number.

---

## 4. ⭐⭐⭐ THE STEPS — **each ends in a measurement, not an opinion**

| # | step | ⭐ done when |
|---|---|---|
| **0** | ⭐ **Fix `FdpConfig.cs:98`'s doc-comment** to name `Hrot.ClusterRunner/Program.cs` — the file that actually sets it | the comment matches the tree |
| **1** | ⭐⭐⭐ **EXACT inventory: which registered component types carry no `[ComponentId]`?** ⛔ **Not grep** — a text count cannot tell a generic parameter from a type. ⭐ Use Roslyn *(`find_references` on `RegisterComponent`, resolve each type argument)* **or** a reflection sweep in a throwaway harness: load the production assemblies, enumerate types used as component arguments, report those lacking the attribute. ⚠ **Also answer hazard B: does the Stride entry point set the flag?** | a list of `Type` → `has attribute? y/n`, per assembly |
| **2** | ⭐⭐ **Assign ids to the un-attributed types, from the right partition** *(`GlobalComponentIds` for engine-wide, `HrotComponentIds` for Hrot)* — ⛔ never renumber an existing one | every registered production type has `[ComponentId]`; `MAX_COMPONENT_TYPES` headroom reported |
| **3** | ⭐⭐⭐ **Turn the flag ON for tests** — one assembly-level fixture per test project rather than per-test, so it cannot be forgotten. ⚠ **Expect fallout and treat each red as a FINDING**: a red here means that test was exercising an id layout production does not have | test suites green with `EnforceExplicitComponentIds = true` |
| **4** | ⭐ **Flip the default to `true`** and delete the auto-assign fallback | `FdpConfig.EnforceExplicitComponentIds` defaults `true`; the legacy branch in `ComponentType.cs` is gone |

⚠ **Steps 3 and 4 are the ones with unknown cost** — ⭐ step 1 sizes them, which is why it comes first
and why this document does not estimate them.

---

## 5. ⭐ WHY THIS BLOCKS SOMETHING ELSE — **the link that makes it worth doing now**

📄 `DESIGN_Role_Affinity_Ownership.md` §3.9b asks whether a role's `BitMask512` can **drive**
registration. ⭐⭐ The mechanism already exists in production *(`RecordingExportService.cs:815-850` scans
`[ComponentId]`, resolves `ComponentTypeRegistry.GetType(typeId)` and registers by id)* — ⛔ **but it is
only COMPLETE if every component actually carries an explicit id.** ⇒ 🔒 **this programme is the
prerequisite for mask-driven registration**, and the same scan that step 1 writes is the one that path
needs.
