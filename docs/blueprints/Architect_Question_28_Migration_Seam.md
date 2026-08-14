# Architect Question #28 — the migration registration seam (`BP-235`)

> **Coordinator, `2026-08-14`.** ⭐ **Relay to the architect.** Claude cannot reach it.
> **Blocks:** `U-10`'s writer — the last task in the variable-unification `D` programme.
> **Everything else in that programme is done and green.** 📄 [PLAN_Variable_Unification_Tasks.md](PLAN_Variable_Unification_Tasks.md)

---

## 1. The situation, measured

⭐ **A blueprint asset's on-disk schema needs to go from v1 to v2.** Both halves of the transform ship
and are proved: `BlueprintSchemaV2.Up`/`.Down`, **`v1 → v2 → v1` byte-identical on all 58 shipped
assets**, adversarially tested against **nine constructed shapes** with four defects found and closed.
✅ **The v2 READER is wired** — all 58 load from their v2 form into the same model as from v1.

⛔⛔ **The WRITER cannot ship, and the obstacle is structural rather than a preference.**

### 1.1 Bumping `$meta.schemaVersion` to 2 forces three things

| | |
|---|---|
| **1** | `BlueprintMigrationModule.CurrentVersion` must become **2** — `PersistentMigrationAdapter`'s Case D **throws** when the disk version exceeds the registry's with no down-chain and no snapshot |
| **2** | a **real** 1→2 migrator must be registered, ⛔ **not a passthrough** — `MigrationPipeline.MigrateTo` returns **immediately** for a passthrough type, **before any version comparison** ⇒ a passthrough at 2 would ⛔ **silently treat a genuine v1 file as v2** |
| 🔴🔴 **3** | ⛔ **that migrator cannot be written where it must be registered** |

### 1.2 🔴 The cycle

```
Hrot.Common                     ← holds the registration (BlueprintMigrationModule, MigrationRegistry)
Hrot.Blueprints.Compiler        ← holds the transform (BlueprintSchemaV2.Up/.Down)
Hrot.Blueprints.Compiler  ──references──▶  Hrot.Common          ✅ exists today
Hrot.Common               ──references──▶  Hrot.Blueprints.Compiler   ⛔ PROJECT-REFERENCE CYCLE
```

### 1.3 ⚠ And a second boundary in the same place

`BlueprintIncrementalGenerator` — ⭐ **the one production reader of every shipped asset** — targets
**netstandard2.0**. `IJsonDocumentMigrator` / `JsonEnvelope` / `MigrationRegistry` are **net8-only**
⇒ ⛔ **the migration framework is unreachable from it.** ⭐ **Today's sidestep is a plain
`System.Text.Json` DOM pair shared by both targets.** ⚠ **That works for the transform and does not
solve the registration.**

📌 **A related latent defect, same boundary:** `BlueprintJsonServices.Serialize` **produces a different
document on each target** — net8 stamps the `$meta` envelope, netstandard2.0 `#if`s it out.
✅ **Harmless today** *(nothing on that target writes)*, ⛔ **not harmless once v2 depends on the
envelope.**

---

## 2. The sub-questions

### Q28-A — Where does the registration live?

| | option | tradeoff |
|---|---|---|
| **A1** | ⭐ **A third assembly** — e.g. `Hrot.Blueprints.Migration`, referenced by `Hrot.Common`, referencing the transform | ✅ **no cycle, clean ownership.** ⛔ **a new project in a solution with six host profiles**, and it must be net8+netstandard2.0 if the generator is ever to use it |
| **A2** | ⭐⭐ **An injection point in `HrotMigrationBootstrap`** — the blueprint side *registers itself* at startup rather than being referenced | ✅ **no new project; inverts the dependency instead of adding one.** ⛔ **six host profiles must each call it**, and ⚠ **a profile that forgets gets Case D's throw at load time — the failure is loud but late** |
| **A3** | **Move the transform INTO `Hrot.Common`** | ✅ trivially resolves the cycle. ⛔ **puts blueprint-shaped knowledge in the shared assembly**, and the transform must then track the declaration model it no longer lives beside |

⚖️ **Coordinator's lean: A2**, on the strength of `.claude/CLAUDE.md`'s *reuse over build* — ⭐ **it adds
a seam rather than a project**, and self-registration is the shape the `[BlueprintRegistrar]`
masquerade already uses elsewhere in this codebase. ⚠ **But A2's failure mode (a profile that forgets)
is exactly the *"silently does nothing"* shape this programme has spent nine batches removing** — 📐 **so
if A2, it needs a rail that makes a missing registration loud at build or startup, not at first load
of a v2 file.**

### Q28-B — Does the generator ever need to READ v2?

⭐ **Today it does not have to:** the writer would emit v2, and the generator's `System.Text.Json` DOM
`Down` could normalise before parsing.

| | |
|---|---|
| **B1** | ⭐ **keep the DOM sidestep permanently** — the generator never uses the migration framework ✅ **no netstandard2.0 problem at all** ⛔ **two code paths that must agree forever** |
| **B2** | make the framework netstandard2.0-compatible | ✅ one path ⛔ **a much larger change, and `Fdp.Core`/`Hrot.Common` are net8-only for reasons this session has not established** |

⚖️ **Lean: B1**, ⚠ **but only with a test that pins the two paths to the same answer** — ⭐ **Batch 44
already proved the in-process and semantic-model compile paths byte-identical across 42 assets; the
same technique applies here.**

### Q28-C — ⭐ What should `--mode migrate` do with a v1 file the transform REFUSES? *(`BP-241`)*

⭐⭐ **Batch 54 made `Up` refuse four non-canonical v1 shapes rather than guess** — including 🔴 **a
declaration carrying its own `Kind` property, which previously overwrote the v2 tag and moved a field
between structs.** ⛔ **Repairing would mean carrying a v1 layout artefact into v2, or inventing a list
that is not there.**

⇒ ⚠ **`ClusterRunner --mode migrate` now has a failure mode with no way forward.**

| | |
|---|---|
| **C1** | **refuse the file and report it** — the operator fixes it by hand ⭐ **honest** ⛔ **no path for a large corpus** |
| **C2** | ⭐ **refuse, and emit a canonicalising pre-pass** — `U-15` already canonicalised 58 assets and proved it a semantic no-op via the golden harness ⇒ **the same tool, pointed at the offending file** |
| **C3** | repair inline with a warning | ⛔ **this is the guess Batch 54 deliberately refused, and one of its four cases silently changed a struct offset** |

⚖️ **Lean: C2.** ⭐ **The canonicaliser exists, is proved, and turns "no way forward" into "one extra
step."** ⛔ **C3 is the option whose failure is a blackboard wipe.**

### Q28-D — Is the bump worth doing at all right now?

⚠ **Worth asking plainly, because everything the unification needed is already delivered without it:**

| | |
|---|---|
| ✅ **already shipped** | the tagged store · the rails · the reader · the proved transform pair |
| ⛔ **the bump adds** | one on-disk format change, ⭐ **and it is the only irreversible step in the programme** |
| **D1** | ⭐ **bump now** — the model and the file agree, and v1 becomes legacy ⛔ needs Q28-A resolved |
| **D2** | ⭐⭐ **hold the bump indefinitely** — keep writing v1, keep the reader, keep the transform tested. ✅ **Zero risk, and the reader means a v2 file would already load if one ever appeared** ⛔ **the tagged store's on-disk shape stays a fiction, and the transform is dead code that must be maintained** |

⚖️ **Coordinator's lean: D2 until Q28-A is answered**, ⭐ **because the current state is coherent and
safe** — ⚠ **but it is a real question whether a proved-but-unused migrator is worth carrying, and the
architect may prefer to either finish it or delete it.**

---

## 3. What would be most useful back

1. ⭐⭐ **Q28-A** — the seam. Everything else waits on it.
2. **Q28-D** — whether to finish or park; ⚠ **if park, for how long and under what trigger.**
3. **Q28-C** — `BP-241`'s answer, which is operator-facing and independent of A.
4. ⭐ **Any correction to §1.** ⚠ **The cycle and the two-target divergence are measured, but the reason
   `Fdp.Core`/`Hrot.Common` are net8-only is not — if that constraint is soft, B2 changes shape.**
