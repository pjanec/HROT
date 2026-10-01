<!--STATUS
state: LIVE — DRAFT FOR THE BACKEND LANE (the user relays it)
updated: 2026-10-01
current-answer: the whole file — a FRAME handoff. The design is already written and APPROVED; you build its wire half.
stale-below: nothing.
known-rot: none.
known-conflict: none.
related-designs:
  - docs/blueprints/DESIGN_Behaviour_Fault_And_Teardown.md — OWNS every decision here (D5, §2 claim table, §3 diagrams,
    §4 work items). This handoff only fences and sequences its backend rows.
  - docs/reference/BDC_NED_SST_Descriptor_Rules.md — the spec D5 obeys: a descriptor is never disposed from a live entity.
  - docs/designs/eqs-2/EQS_Design_v1.3_final.md — owns the EQS topics, the Muscle carrier and the solver this changes.
-->

# HANDOFF — EQS sensor lifecycle, wire half (`CE-486`, `CE-487`, `CE-490`)

**For:** the backend lane (`backend`). **From:** the behaviours lane (`behaviors`). **Dispatched at `acb4ce898`** — your scope
is FROZEN at that sha; documents that change after it are FYI only. A later document that invalidates an item ⇒ STOP that item
and report it (do every other item).

🔒 **User, `2026-10-01`:** *"The system needs to be reliable."* · *"Yes to Delete sensors at behavior end with the per lifetime
number"* · *"add Active flag"* · *"If you know what backend session can do without collision with you, write handoff for them."*

**Start:** `git fetch origin behaviors && git merge origin/behaviors` (at least `acb4ce898`), then push the started-marker.

## 1. What and why — read the design, not this summary

📄 [`DESIGN_Behaviour_Fault_And_Teardown.md`](../DESIGN_Behaviour_Fault_And_Teardown.md) **§1 D5** (approved), **§2** (the
claim table — every line number below is measured there), **§3** (the third sequence diagram is your target behaviour).

In one line: an EQS child sensor is a **multi-instance descriptor of its parent entity**, so per the descriptor rules its
instance is **never disposed while the parent lives**. A sensor ends by a "suspended" write; part ids are reused; the
behaviour run rides in the epoch. Today the brain disposes (`EqsSensorConfigEgressTranslator.cs:141`) and the Muscle reads
that as "destroy carrier" (`EqsSensorConfigIngressTranslator.cs:111`) — both against the spec, and the source of races ①②.

## 2. Items

| id | what | files (yours) |
|---|---|---|
| **CE-486** | ① a suspend flag on `EqsSensor` + `EqsSensorConfigTopic` (non-key; see decision B-1 for its polarity) · ② the config egress **never disposes a child-sensor instance**: when a local sensor is gone it writes that sensor's last config with the flag set — UNLESS a live local sensor now holds the same key, in which case only the live sensor's config is written (so the same-scan write-then-dispose race, §2 ①, cannot happen) · ③ the Muscle ingress stops treating a child-sensor dispose as "destroy carrier" (a dispose now only comes with the parent's death, which `SubEntityCleanupSystem` already handles) · ④ the solver skips suspended carriers — ⚠ today an unknown template still answers empty every solve (`EqsSolverSystem.cs:147-159`); a suspended carrier must publish **nothing** | `EqsComponents.cs`, `EqsDdsTopics.cs`, both config translators, `EqsSolverSystem.cs` |
| **CE-487** | the result ingress cache (`EqsResultIngressTranslator.cs:79`) checks on EVERY hit that the cached entity is alive and still carries that part id; otherwise re-scan. ⭐ REQUIRED by D5: a reused part id maps to a new local sensor while the cache still holds the old one | `EqsResultIngressTranslator.cs` |
| **CE-490** | brain nodes also read `EqsSensorConfig` (TransientLocal hands over every live instance); when this node holds authority over an entity, write the suspend flag to every instance under it that has **no local sensor** — the old owner never ends its behaviour after an authority move (`BrainTickSystem.cs:129` ticks owned entities only), so without this the Muscle solves its sensors forever. Depends on `CE-486` | `SimHostAuxiliaryTranslatorPack.cs` (Brain block), a new or extended brain-side reader |

### Decisions — each with a lean

| | question | lean | what would change it |
|---|---|---|---|
| **B-1** | the flag's polarity. The user approved *"an `Active` flag"*, but a struct default is `false`, and **20+ files** build `new EqsSensor { … }` (measured: 18 `ClusterRunner.Integration.Tests/Eqs/*`, `WhenNodeRuntimeTests`, `EntitiesInAreaGenerator`, plus the behaviours lane's creators) ⇒ `Active` would make every existing sensor silently inactive | ⭐ **`Suspended` (default `false` = running)** — same meaning on the wire, every existing creator stays correct, and the behaviours lane does not have to touch your field. ⚠ say so in your report so the user sees the rename of the approved flag | the user insists on `Active` ⇒ then every creator must set it, which crosses into the behaviours lane — STOP and report |
| **B-2** | what a suspended config carries | the sensor's LAST config with the flag set (template, epoch preserved) — readable in a capture, and the next lifetime overwrites it | — |
| **B-3** | CE-490's reader: new translator or extend the Muscle's ingress | a separate brain-side reader that only collects `(parent, part id)` keys — ⛔ do not build carriers on a brain | — |

## 3. Fences — what the behaviours lane is doing at the same time

| | |
|---|---|
| ⛔ **NOT yours — behaviours lane, in flight now (`CE-485`)** | `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsChildSensor.cs` (part-id allocation, immediate creation, epoch high bits, owner stamp), the new `BehaviorOwnedPart`, `BehaviorIngressSystem.cs`, `BrainTickSystem.cs`, `HillAttackCommanderNodes.cs`, `EqsLifecycleNodes.cs`, the blueprint compiler's `Spawn EQS Sensor` (`Stage5_Schedule.cs`, `StatementEmitter.cs`, `Nodes.cs`), the `HillAttack*` tests |
| ⛔ **NOT in this handoff, and why** | `CE-491` (rename `BlueprintId` → `TemplateId`) — the rename reaches `HillAttackCommanderNodes.cs:61` and the blueprint emitter, which `CE-485` is editing ⇒ guaranteed conflicts; it goes AFTER `CE-485` merges. `CE-484` (fault notification egress) and `CE-483`'s mission-egress half — both need types the behaviours lane has not built yet (`CE-482`/`CE-483`) |
| ⚠ the one shared seam | `EqsSensor`'s field list. You ADD the flag; the behaviours lane only changes what goes INTO `Epoch` (high 16 bits = behaviour run). Neither renames nor reorders. If you need more than an additive field, STOP and report |
| ✅ yours | everything in §2's files column, the EQS integration tests under `ClusterRunner.Integration.Tests/Eqs/` |
| ⛔ STOP paths | `Fdp.Core`, `Hrot.SystemTests`, `Hrot.IG.Tests` |

## 4. Acceptance

① ending a sensor publishes no dispose; the Muscle carrier remains, suspended, and publishes nothing (a rail with a
fake writer/reader, or `EqsDistributedTests`) · ② end a sensor and create one on the same key in the SAME scan ⇒ the Muscle
solves the new one (the §2 ① race, red-proved on the old egress) · ③ dispose + write in one Muscle batch ⇒ the carrier is
updated, not destroyed (§2 ②) · ④ a result for a reused part id reaches the NEW local sensor (§2 ③, `CE-487`) · ⑤ authority
move: an instance with no local sensor on the new owner gets suspended (`CE-490`) · ⑥ `EqsDistributedTests` + the EQS suites
green · ⑦ live `--mode all`, `hill-attack-close` and `hill-attack-close-bp`: unchanged outcome (hostiles `Health 0`, platoon
back).

## 5. Gate report contract

One row per gate: verbatim command · pass/fail/skip · delta vs base; a `--no-build` column (build the **test** project first);
every red proved pre-existing against the base sha; working tree clean after every run; `tracker-counts.py --check`; ⭐ row 8:
name the integration suite that exercises the wire invariant (`ClusterRunner.Integration.Tests` `EqsDistributedTests`) and
report running it. **Ids:** none allocated here beyond `CE-486`/`CE-487`/`CE-490` (rows exist); number anything new yourself
from the next free `CE-` id **across both branches** (`CE-488`/`CE-489` collided once already).

**Fold the as-built** into the design's §1 D5 / §2 / §3 if you deviate, marking the prior text superseded.
