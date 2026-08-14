# HANDOFF — Batch 54: ⭐⭐ **`U-10`'s WIRING — the last task in the `D` programme**

> 📌 **Dispatched at `<STAMP>`.** Frozen per `.claude/CLAUDE.md` → *Two-session protocol* rule 1.
> ⭐ **Rule 7:** branch from this branch, re-sync at the **start** of your run.
> ⭐ **Rule 4:** pull it again before your final commit.
> ⭐ **Rule 3: the coordinator allocates no ids.** `BP1674+` is the next free diagnostic.
>
> ⭐⭐ **This is the ONLY batch in the programme where `persistence-shape.txt` is ALLOWED to move** —
> and it must move **exactly once, deliberately, with the diff reviewed.**

---

## 0. Where this sits

| | |
|---|---|
| ✅ **Everything it depends on is done** | `U-9` (tagged declaration) · `U-11` (consumers) · `U-12` (rails **and** the store flip, `persistence-shape` unchanged) |
| ✅ **Half of it already shipped, Batch 49** | `BlueprintSchemaV2.Up`/`.Down`, with **`v1 → v2 → v1` byte-identical on all 58** and **proved to bite** |
| ⛔ **What is missing is only the wiring** | ⭐ **nothing writes v2 and nothing reads it** |
| ⭐⭐ **And your own re-sequencing said WHY it waited** | *"only after `U-12` does the on-disk shape mirror an in-memory shape that exists, so the migrator is a thin mapping rather than a three-lists ⇄ one-array conversion into a shape nothing consumes."* ⇒ **`U-12` landed. It is now that thin mapping.** 📐 **Confirm that, or say what changed** |

---

## 1. The two live obstacles, both yours, both measured

### 1.1 🔴 `BP-235` — the framework wall

`BlueprintIncrementalGenerator` targets **netstandard2.0**; `IJsonDocumentMigrator` / `JsonEnvelope` /
`MigrationRegistry` are **net8-only** ⇒ ⛔ **unreachable from the one production reader of every shipped
asset.** ⭐ **Batch 49 sidestepped it with a plain `System.Text.Json` DOM pair shared by both targets.**

📐 **Decide and say:** does the wiring keep that sidestep, or does `BP-235` have to be solved here?
⚖️ **Keeping the sidestep is the lean** — ⛔ **a new seam through a bootstrap shared by six host
profiles is its own batch.** ⭐ **But say which, because `BP-235`'s row should either close or state
plainly that it stays open by choice.**

### 1.2 ⚠⚠ `ClusterRunner --mode migrate` is a REAL production consumer

⭐ **Contrary to a first reading, and you found it:** it walks every `*.json` and
`BuildClusterRunnerMigrate` registers the blueprint doc type. ⇒ ⛔ **bumping `$meta.schemaVersion` to 2
while `BlueprintMigrationModule.CurrentVersion` stays `1`-passthrough is a LIVE inconsistency, not a
cosmetic one.**

⇒ ⭐ **Whatever else this batch does, those two numbers must agree at the end** — 📐 **and if they
cannot, that is a finding worth stopping for.**

---

## 2. Gates

| | |
|---|---|
| ⭐⭐ **Pass 1 — `v1 → v2 → v1` is the identity, byte for byte** | ✅ **already proved on all 58** — ⭐ **re-run it against the FLIPPED store**, because `U-12` changed what `Up` reads from |
| ✅ **Pass 2** | a **v1** file loads through the **v2** reader |
| 🔴🔴 **Pass 3 — `StructureHash` unchanged for EVERY shipped asset** | ⛔ **the no-blackboard-wipe gate. A failure here re-initialises every deployed entity's state** |
| ⭐⭐ **Pass 4 — `persistence-shape.txt` moves, ONCE, deliberately** | ⭐ **regenerate it and REVIEW THE DIFF.** ⛔ **Say in the commit that you did and what changed** — this is the one gate in the programme that guards persistence, and a silent regeneration of it is unauditable later |
| ⭐ **Pass 5 — golden 42/42 both tiers UNCHANGED** | ⚠ **the on-disk shape changes; the compiled output must not.** ⭐ **That separation is the whole claim** |
| 🔴 **Revert** | ⛔ **`git revert` does not work — the down-migrator IS the revert.** ✅ **It shipped in Batch 49; ⭐ prove it still works against the flipped store, not against Batch 49's** |
| ⭐⭐ **Both ways** | full suite **and** isolated filters, as Batches 52/53 |

**Baseline — coordinator-run on the merged Batch-53 tree (`7974b3eb`), ⭐ green both ways:**

| | |
|---|---|
| Solution build | **0 errors**, **69 warnings** |
| Blueprints | **3538 total / 3528 passed / 0 failed / 10 skipped** |
| ⭐ **AiShared 1216** · BTree **612** · Breakpoints **130** · Generators **193** · NodeEdit Core **208** · UI **131** | ⛔ **none should move** |
| ⭐ **Golden Tier 1 + Tier 2** | ⛔ **UNCHANGED** |
| ⭐⭐ **`persistence-shape.txt`** | 📌 **THIS BATCH MOVES IT — the only one that may** |
| `tracker-counts.py --check` | clean **twenty-two** batches running |

---

## 3. 📌 `BP-240` is the question to carry, and it is yours

⭐⭐ **You filed it, and it is the sharpest finding in the programme:** *a gate can be green because of
what the corpus happens to do, not because the code is right.* ⛔ **Breaking the grouping invariant
left BOTH `persistence-shape` and golden green** — because deserialization sets the properties in an
order that **is already `KindOrder`**, so the corpus exercises **exactly one path and no other.**

⇒ 📐 **Ask it of the migration:** ⛔ **what does `Up`/`Down` do correctly only because all 58 shipped
assets happen to be shaped a certain way?** ⚠ **Candidates worth constructing rather than assuming:**

| | |
|---|---|
| an asset with **zero** declarations of one kind — or of **all three** | |
| a **v1** file whose three lists are in an order the writer never produces | ⭐ **exactly `BP-240`'s shape, at the file level** |
| an asset where a `*Order` list references an id **not present** in its kind | ⚠ `BP-231` made this rarer, ⛔ **not impossible in a hand-authored file** |
| ⭐ **an asset with a name collision across kinds** | `BP1673` refuses it at Stage 2 — 📐 **but does the MIGRATOR survive reading one?** |

⭐ **The corpus cannot answer any of these. Constructed fixtures can.**

---

## 4. ⚡ How to work

**You are on Opus, and ⛔ all of it stays there.** ⭐ **`Pass 3`'s failure mode is every deployed
entity's blackboard.**

⚠ **Sub-agents share ONE working tree** — sequential only:
```bash
while [ "$(ps aux | grep -c '[d]otnet build\|[d]otnet test')" != "0" ]; do sleep 5; done
```

| | |
|---|---|
| **Push to** | your implementation branch, **branched from this one** (rule 7) |
| **Rule 6** | the tracker is yours — ⭐ **`U-10` closes here; `BP-235` closes or states why not** |
| ⚠ **Stop point** | ⭐ **before bumping `$meta.schemaVersion`.** Everything up to that point is reversible by `git revert`; ⛔ **the bump is not** |

---

## 5. Reporting

⭐⭐ **`StructureHash` unchanged for all 42, stated FIRST** · ⭐⭐ **the `persistence-shape` diff — what
changed and why** · **golden 42/42 both tiers unchanged** · ⭐ **that `Down` still round-trips against
the FLIPPED store** · **your `BP-235` ruling** · ⭐ **the `$meta.schemaVersion` / `CurrentVersion`
agreement** · ⭐⭐ **§3 — what the migrator does right only because of the corpus's shape, even if the
answer is "nothing, and here are the fixtures that prove it"** · per-suite numbers **full and
filtered** · `tracker-counts.py --check` · ⭐ **every id you allocated**.

⭐⭐⭐ **Batch 53's best move was running a revert probe, GETTING GREEN, and treating that as the
finding rather than as permission.** ⛔ **Two gates agreed the invariant did not matter; the corpus was
the reason, not the code.**

⚠ **This batch is where that habit pays or does not.** ⭐ **The migration's gates are the strongest in
the programme — `v1→v2→v1` byte identity and `StructureHash` unchanged — and both are still only as
good as the 58 files they run on.**
