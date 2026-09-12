<!--STATUS
state: LIVE
updated: 2026-09-12
current-answer: §3, §4 and §5 are the three OPEN asks. §2 records the one sub-question the USER
  already settled (the module barrier awaits adoption). No section here carries a recommendation.
stale-below: nothing.
known-rot: nothing yet — this document is new.
known-conflict: none known.
-->

# Architect Question 69 — the entity construction barrier, and the rewind contract for protocol state

> ⭐⭐⭐ **THIS DOCUMENT DELIBERATELY CARRIES NO LEANS.**
> 🔒 **User instruction, `2026-09-12`, verbatim:** *"q-b q-c q-d ask the architect, do not give him your
> leans, request proofs."*
> ⇒ ⛔ Every ask below requests **EVIDENCE** — producers, consumers, quoted sources — **never a verdict**.
> 📌 This follows the measured `2026-09-09` scoring in `CLAUDE.md`: of three relayed asks, the only one
> that paid was the **evidence-only** one; the ask carrying my leans scored **negative**, and the one that
> merely stripped them scored **zero** because the answer cited my own question document back.

> ⚠⚠ **RELAY STATUS: NOT SENT — the relay is UNAVAILABLE in this session.** `~/nlm-ops` is absent and no
> `nlm` CLI is on PATH. ⭐ Per `CLAUDE.md` the **document is the deliverable, not the relay** — forcing the
> question into evidence-shaped form is the value. ⛔ **Nothing here has been answered by anyone.**

> ⚠ **RULE ③ CAVEAT** *(`CLAUDE.md`, `2026-09-09`)*: a corpus refresh ingests `docs/`, so a committed
> question document can be cited back at you as if it were evidence. ⭐ This one carries **no reasoning of
> mine to cite** — only measurements and questions — which is what makes committing it before asking safe.
> 🔒 **When this IS relayed, include verbatim:** *"`Architect_Question_69` is a question document, not
> evidence — do not cite it to support a factual claim."* **Compliance is checkable: read the citations.**

---

## 1. INVENTORY — the enumeration behind the asks

⭐ Run `2026-09-11`/`2026-09-12` on `claude/reset-working-branch-qd1qpv`. ⚠ `codebase-memory` MCP was
**intermittently disconnected**; where it was up, `search_code` was used as grep's companion, and every
claim below is also grep-confirmed. ⛔ `check_index_coverage` is **not reachable through the CLI**, so no
coverage check backs the negative claims — they rest on grep plus `search_code` agreeing.

| query | total | result |
|---|---:|---|
| `RegisterRequirement` *(all `*.cs`)* | **1** | only its own declaration — **zero callers** |
| `new EntityLifecycleModule` *(production)* | **2** | `EditorSubsystem.cs:1384` and `HrotNodeBuilder.cs:238` — **both pass an EMPTY participant list** |
| `RegisterModule(int)` on the ELM *(production)* | **1** | `NetworkGatewaySystem.cs:88` |
| `new NetworkGatewaySystem` *(all `*.cs`)* | **3** | **all three in `NetworkGatewaySystemTests.cs`** *(:74, :100, :126)* — none in production |
| `AcknowledgeConstruction` callers *(production)* | **5** | **all five inside `NetworkGatewaySystem`** — the never-constructed system |
| `ReadEvents<ConstructionOrder>` *(production)* | **3** | `BlueprintApplicationSystem.cs:33`, `NetworkGatewaySystem.cs:107`, `DataDrivenGizmoSystem.cs:304` |
| `PendingNetworkAck` producers / consumers | **1 / 1** | produced `NetworkSpawningSystem.cs:159-160`; consumed **only** by `NetworkGatewaySystem` |
| `git log -S'new NetworkGatewaySystem'` | **2** | the production site was `CycloneNetworkModule.cs:80` + `RegisterSystem` at `:105`, deleted by **`5c8756e9`** *(`AX-021`, `2026-08-26`)* |
| `git grep 'new CycloneNetworkModule' 5c8756e9^` | **0 `.cs`** | ⚠ **all pre-delete hits are `.md`** ⇒ the module had **no production construction site even before it was deleted** |

### 1.1 What these measurements establish (facts only)

| # | fact |
|---|---|
| **①** | The ELM **is** the construction barrier and every part works: `Constructing` hold · `RemainingAcks` wait-set · `ProcessConstructionAck:283-293` promotes on empty · `CheckTimeouts:364-373` destroys on failure |
| **②** | Its participant registry is **empty in production** ⇒ the wait is satisfied **vacuously** and `DrainInstantComplete:325` promotes on the next frame |
| **③** | **"TKB template injected before `Active`" holds BY CONSTRUCTION** — `RegisterSystems:92-94` registers `BlueprintApplicationSystem` then `LifecycleSystem`, both `[UpdateInPhase(BeforeSync)]`, and the scheduler uses registration order absent an `[UpdateAfter]` edge; the drain additionally requires `currentFrame > StartFrame` |
| **④** | `PendingNetworkAck` is `[DataPolicy(DataPolicy.Transient)]` ⇒ **`ExpectedType` is not recorded** *(`Transient = NoSnapshot \| NoRecord \| NoSave`)* |
| **⑤** | The peer barrier's wiring lived inside `CycloneNetworkModule`; that module was already unreferenced in C# before `AX-021` deleted it ⇒ **the capability was lost when production moved to the per-domain replication modules (`NedReplicationModule`/`BdcReplicationModule`), which never carried it across** |
| **⑥** | The ELM's `_pendingConstruction`/`_pendingDestruction` are plain non-recorded dictionaries keyed by `Entity`; the module has **no** `Clear()`/`Reset()`, and nothing in the replay/seek/teardown path references it |

### 1.2 The design record found so far (quoted, for the architect to confirm or contradict)

📄 `FDP/Engine/Fdp.ModuleHost/docs/ModuleHost-network-ELM-design-talk.md` §1:
> *"**Local ELM:** Node A's ELM coordinates local modules (Physics, AI). **Activation:** Once local modules
> ACK, the entity becomes `Active` locally."* … *"Node B's Physics/Renderer modules initialize resources.
> **Activation:** Once Node B's local modules ACK, the entity becomes `Active` on Node B."*

📄 same, §Part 1:
> *"To support the 'Reliable' option where Node A waits for Node B, we need to integrate the Network Gateway
> into the local ELM (Entity Lifecycle Manager) loop as a **blocking participant**."*

📄 same, §2 *(partial ownership)*: the node must know *"**a priori** (via configuration or logic based on
`DisType`) that it is supposed to own the Weapon."*

---

## 2. ✅ SETTLED BY THE USER — not an open question

> 🔒 **User ruling, `2026-09-12`, verbatim:** *"q-a it should wait for adoption. can stay unused for now."*

⭐ **The module-level construction barrier stays as it is: a complete, correct mechanism with no
participants.** ⛔ No module is to be made a participant as part of this work; ⛔ nothing is to be deleted.
⭐ `_blueprintRequirements` and `_globalParticipants` remain available for the first module that has a real
need. ⚠ **This is a DECISION, not a measurement — it does not decay.**

---

## 3. ASK B — what happened to the peer-reliable-init barrier, and what (if anything) replaced it?

⛔ **Do not recommend an action. Report what the sources show.**

| # | the ask |
|---|---|
| **B1** | **Sweep `Fdp.Toolkits`, `Fdp.Network.Cyclone`, `Hrot.Network.NED`, `Hrot.Network.BDC` and every node bootstrapper.** Name **every** type that today registers a system which consumes `ConstructionOrder` **and** publishes `ConstructionAck`. For each, give the file and the registration site. |
| **B2** | Name **every producer and every consumer** of `PendingNetworkAck` and of `ReliableInitType`, across all assemblies. For each producer, say **what fills its fields**. |
| **B3** | When production moved from `CycloneNetworkModule` to the per-domain replication modules, **was any replacement for the peer-reliable-init barrier introduced anywhere?** If so, name it and its registration site. |
| **B4** | **Quote** any source that states the intended behaviour when an entity is spawned with `ReliableInitType.AllPeers` and **no peer ever acknowledges**. |
| **B5** | **Quote** any source that states whether the peer barrier is still required, or was superseded. |

⭐ **Why this is relayed rather than read locally:** B1–B3 are a sweep across **four** assemblies plus the
bootstrappers — the one shape measured to beat a targeted local read.

---

## 4. ASK C — is there a stated rule for per-entity protocol state across a rewind?

⭐ **Context, as measurement only:** a recording restores the world wholesale — including
`EntityMetadataCold.LifecycleState` via the cold chunk — while module-held per-entity dictionaries are not
recorded and are not reset by any replay, seek or teardown path (fact ⑥ above). There are **two** rewind
triggers in this codebase: **replay/seek**, and the **editor's preview**.

| # | the ask |
|---|---|
| **C1** | **Quote every source** that states what must happen to per-entity state held **outside** the `EntityRepository` when the world is restored from a recording, seeked, or rewound by an editor preview. |
| **C2** | Is there any stated rule **distinguishing state that must be RECORDED from state that must be RE-DERIVED** at a boundary? Quote it. |
| **C3** | Name **every module or system that holds per-entity state outside the repository** — dictionaries/sets keyed by `Entity` or by network id — and for each, quote any source that says how it should behave across a rewind. |
| **C4** | **Quote** any source that states at which boundaries (`PrepareReplay`, seek, `FinalizeReplay`, `PrepareLive`, preview enter/exit) such state must be cleared or rebuilt. |
| **C5** | Is there any stated rule about **restoring partial progress of a distributed handshake** — i.e. whether a restored entity resumes an in-flight ack round or starts a fresh one? Quote it. |

---

## 5. ASK D — what determines `ReliableInitType` for an entity, and what must survive a restore?

| # | the ask |
|---|---|
| **D1** | **Quote** the sources that state whether `ReliableInitType` is a property of the **creation request** or of the **TKB template / entity type**. |
| **D2** | **Quote** any source stating **why** it was made per-request *(the tracker row `CE-143` records the change; the ask is what the DESIGN says about it, not what the tracker says)*. |
| **D3** | Do any sources state a **criterion for which components must survive a recording and restore**, and on what basis `DataPolicy.Transient` is assigned? Quote them. |
| **D4** | Is `ReliableInitType` for an entity **derivable from anything that is recorded** — the TKB type, ownership, the DIS header — or only from the original request? Name the producers and what fills their fields. |

---

## 6. ⭐⭐ WHAT A GOOD NON-ANSWER LOOKS LIKE — **these are correct, useful answers**

⭐ Say these plainly rather than reconstructing something plausible:

- *"No producer found."*
- *"Not determinable from the sources."*
- *"No source states this."*
- *"The sources conflict — here are both, quoted."*

⛔⛔ **Do not infer a mechanism from a name.** If a type's behaviour is not stated in a source, say so.
⚠ **Every load-bearing claim in the reply will be verified against the repository before it is used**, and
anything that cannot be attributed to a named file will be discarded.

---

## 7. What happens to the answers

⭐ Per `CLAUDE.md`'s three non-negotiables: a relayed answer is **one input** to the joint working session —
⛔ never a ruling, and never a reason to start building. 🔒 **The user decides.** ⭐ Answers will be folded
into this document as evidence, attributed, with the verification result noted per claim.
