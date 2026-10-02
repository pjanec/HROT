<!--STATUS
state: LIVE
build-state: DISPATCH — a pointer, not a design. ⛔ Carries NO UML and NO design content by rule.
updated: 2026-09-17
current-answer: §2 is the item list. §0 is the gate debt that must be paid FIRST. §1 is the ruling that
  unblocked D1.
stale-below: nothing — new document.
known-conflict: none. Batch ②a is MERGED into the coordinator branch; this continues on top of it.
related-designs:
  - ../PLAN_Terrain_Zones_Build.md — the plan (stages C, D, F; §5 records the as-built ②a/②b split).
  - ../../DESIGN_Terrain_Zones_And_Assets.md — THE owning design. §6's enum note is CORRECTED.
  - ../../designs/mgmt-1/DESIGN.md — §11 owns the PrepareZone/CommitZone 2PC protocol.
  - ../../DESIGN_Distributed_Scenario_Persistence.md — §6a owns what Stage F retires.
  - HANDOFF_Terrain_Zones_Batch2_Loader_And_Ops.md — the ②a dispatch; its §1/§3/§6 still apply.
-->

# HANDOFF — **Terrain & zones, Batch ②b: THE OPS AND THE RETIREMENT** *(11 items)*

**Dispatched at `765618636`.** ⛔⛔ **Scope FROZEN at that sha.**

⭐ **Batch ②a is merged and reviewed** — `C1` `C2` `C5` `C6` `C7` and half of `D1` shipped, and `C6`
turned out to be a **live bug** *(`ZoneManagerService.LoadZones` frees the old blob synchronously while a
background solver may be walking it)*. ⭐ The lease-based `RoadNetworkHolder` is the right answer and the
deviation you flagged on `PrepareAsync`/`Commit` was **correct — the handoff's "mirror field for field"
wording was too strong, and §3.1's own sequence draws the split you built.** ⛔ Nothing to undo.

## 0. ⛔⛔ GATE DEBT — pay this FIRST, before any new code

**Batch ②a pushed `24397d44a` with no `REPORT` and no gate table.** ⇒ the coordinator has **no** evidence
of its test state: no pass/fail counts, no pre-existing-RED confirmation, no `tracker-counts`, **no ids**.

⭐ **Item `Z0`: produce the ②a gate table against base `af6b1a071`**, to contract rows 1–7, and **state the
ids you allocated for `C1` `C2` `C5` `C6` `C7`** *(the batch-① sequence ended at `BP-527`)*. ⛔ Do not
start `C3` until `Z0` is written. ⚠ If a suite is red, that is a finding to report — **not** a reason to
delay the rest (`R-106`).

⚠ **And the process point, stated once and not laboured:** the blocking finding reached the coordinator
only because the user mentioned you had stopped. ⭐ The report trigger is the channel — **fire it on a
blocker, immediately**, and keep working the unblocked items meanwhile. `C3`, `C4` and `F1`–`F4` were
**not** blocked by the enum collision; only `D2`–`D5` were.

## 1. ✅ THE RULING THAT UNBLOCKS `D1` — **`ClusterOpType.BuildTerrainAsset = 18`**

🔒 **Confirmed by the user, `2026-09-17`.** ⭐⭐ **You were right and the coordinator was wrong:**
`ClusterOpType.SaveScenario = 17` is live and routed *(`OrchestrationMessages.cs:42`, `CE-277(c0)`,
renamed by `CE-278` with the wire value unchanged)*. The design note said *"next free; 2 is a documented
reserved gap"* — it saw the gap at 2 and missed that the enum already ran to 17.
⭐ **18 is verified free.** ⛔ Leave the reserved gap at **2** empty, and the `NodeOpType` gaps at
**6/17/18/19** empty. ⚠ `R-42` makes 18 permanent — **stopping rather than guessing was the right call.**
📄 Design **§6**'s enum note carries the correction.

## 2. The items — 11

| # | what | notes |
|---|---|---|
| **`Z0`** | ⛔ the ②a gate table + ids *(§0)* | **first** |
| **`C3`** | scenario-load invocation — the load handlers call `EnsureAllLoaded` **locally**, no NodeOp | ⚠ **was not blocked**; design §3.2 |
| **`C4`** | `LoadZoneIntent` consumer starting a `PrepareZone`/`CommitZone` round | ⛔ **ONE OP PER ZONE**, not a multi-zone payload |
| **`D1`** | the remainder: **`ClusterOpType.BuildTerrainAsset = 18`** in **both** enums | `NodeOpType` 29/30 already shipped |
| **`D2`** | purpose-built payload DTOs for the zone op and the build op | no more `ArchivePayloadDto` with `ExerciseId` stuffed into `ZoneId` |
| **`D3`** | ONE `TerrainAssetHandler` on **every** ECS host, shared registrar | ⛔ **the ACK is UNCONDITIONAL** — no role×kind matrix |
| **`D4`** | retire `IgZoneDummyHandler` | rail: IG still never stalls a round |
| **`D5`** | terrain-identity check — **fails loudly** on mismatch | never a silent pass |
| **`F1`** | retire `ZoneDefinitionDto`, the `Zones` section, `ZoneMembership`, `ZoneManagerService`'s DTO half | ⭐ `C5` is green, so `F` is now unblocked |
| **`F2`** | remove the `ScenarioMergeCore` I4 one-`Zones`-source guard | |
| **`F3`** | re-home or delete the five suites + two `SpyZoneManagerService` doubles | ⭐⭐ **the real work** |
| **`F4`** | close `CE-277(a)` as **will-not-build**, with the reason recorded | |

⭐ Success conditions: **`PLAN_Terrain_Zones_Build.md` §2**, one row per item. ⛔ Not restated here.

⚠ **`F1` touches `ZoneManagerService.LoadZones` — the same method `C6` found freeing blobs.** ⭐ Check
that retiring the DTO half does not re-introduce the synchronous dispose the lease model removed.

## 3. ⭐ `F` IS A RE-HOMING, NOT A DELETION

`HN-037`, measured on this exact shape: a deletion verified safe on **production** callers cost a batch
because **8 test callers each asserted a claim that had to be re-homed to a different owning component.**
⇒ **every claim in `F3`'s five suites is re-homed or deleted with a stated reason; none silently
disappears.** ⛔ Estimating it as a mechanical `s/old/new/` is the error the rule exists to prevent.

## 4. Gates

Rows 1–7 against base **`765618636`** *(plus `Z0`'s separate table against `af6b1a071`)*.
⛔⛔ **ROW 8 BINDS** — `D` is cross-node by construction and `F2` changes the merge core. Name the
integration suite that would break if the invariant broke and report **running** it, or state with
base-sha evidence why it cannot gate. ⭐ Batch ①'s argument was the right shape; silence is not.

⭐ Build the affected project, never the solution. Slow things in the background. Ids continue from wherever
`Z0` leaves off, plain numbers, no letter suffixes.

## 5. What to send back

`docs/blueprints/batches/REPORT_Terrain_Zones_Batch2b.md`, and **inline** via your report trigger: `Z0`'s
②a table · this batch's table · **every id from both** · row 8's named suite and result · the UML check
with any deviation **folded back into the design** and the edit cited · what the design got wrong · the sha.
