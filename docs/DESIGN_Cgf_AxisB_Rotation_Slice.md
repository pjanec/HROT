<!--STATUS
state: LIVE
build-state: BUILT — the first cut (AX-001..AX-006), the AX-005 successor (AX-005a/b/c, AX-007, AX-008,
  CE-018, CE-035, CE-036) on 2026-08-25, and AX-011/AX-012 on 2026-08-26 which turned the full
  --mode all round trip GREEN (§13 is the newest AS-BUILT; it supersedes §12.5 F2 and §12.6's red row). ⭐⭐⭐ §12 is the AS-BUILT and carries the LIVE
  classDiagram + sequenceDiagram (§12.2/§12.3). ⛔ §11.3-§11.5 are SUPERSEDED — the plan asked for a NEW
  intent + a NEW translator; both already existed and were EXTENDED (ruling 9). Read §9 and §12.
  Axis-B FIRST CUT (§4/§5, still LIVE for AX-001..006): a subsystem-
  agnostic entity-write path proven with ROTATION. Delivers UXI-30 (the binary authority gate) + a rotation
  attribute id + a subsystem-agnostic write helper the rotator gizmo drives. ⛔ NOT all of Axis B — one slice
  that establishes the owned→direct / unowned→request routing so later attribute gizmos reuse it.
updated: 2026-08-26
stale-below: ⛔⛔ §1's row "BinaryInterpreter.Apply — NO AUTHORITY GATE" and §6 item ①'s framing are
  SUPERSEDED by §9.1: measured, BOTH production installers already gated every handler, so the binary
  path WAS authority-gated. The real defect — and what was built — is that the gate was per-installer
  and therefore forgettable. §7's "rotation round-trips on a real --mode all cluster" is NOT delivered:
  §9.4 measures why (there is no production SENDER of binary attribute records, which §6 ① itself notes).
  ✅✅ §9.4's open item is FULLY DISCHARGED as of 2026-08-26 — see §13. The full --mode all round trip is
  GREEN. ⛔⛔ §12.5 F2 and §12.6's red row are SUPERSEDED: that replication failure was diagnosed
  (AX-011 missing NetworkTransform shadow) and fixed, together with AX-012 (the binary arm was dead in
  production). Read §13 for the as-built.
  ⛔⛔ §11.3/§11.4/§11.5 are SUPERSEDED by §12 — do NOT quote them as the intended shape.
design-basis: UX_Feature_Authority_Aware_Writes.md §3.3b-c (UXI-30/UXI-29 ruling — change-requests apply ONLY
  to owned components; JSON path already gates via CanWrite, binary path does not) · HROT-PROGRAMMERS-GUIDE
  Part 0 rule 8 (the two writer classes; the replication inverse must be preserved) · user ruling 2026-08-25
  (the gizmo is subsystem-agnostic; owned→direct write, unowned→request intent; SimTransform needs no change
  flag — its egress translator diffs lastSent every tick).
known-conflict: ✅ parallel-safe with the MCP create-recipes session (disjoint files; the ONE shared file is
  CgfSubsystem.cs — this session keeps to gizmo registration, MCP keeps to the asset-service dict). ⛔ Do NOT
  add an MCP route (the MCP session owns the generated catalog) — test via integration rails + existing entity ops.
-->
# DESIGN — **Axis-B first cut: the subsystem-agnostic write path, proven with ROTATION** *(UXI-30 + rotation)*

> 🎯 Axis B lets a node manipulate map entities like the editor. The editor never hits authority *(one-node
> cluster, owns everything)*; a distributed node does. This slice builds the **one write path** every attribute
> gizmo will reuse — **owned → write ECS directly; unowned → publish a change-request** — and proves it with the
> motivating case, **Rotate**, which needs a new attribute id and the binary authority gate *(UXI-30)*.

## 1. ⭐⭐ INVENTORY — measured `2026-08-25`
| ✅ exists | where | role |
|---|---|---|
| `EntityRotatorGizmo` *(reads `SimTransform`, `onCommit(yawRad)`)* | 🔴 **`Hrot.SimHost/Gizmos`** *(SimHost-bound)* | ⭐ the gizmo to make **subsystem-agnostic** — its commit must go through the shared write path, not a SimHost-only ECS poke |
| `IEntityPatchContext.CanWrite()` — native component authority | `Fdp.Toolkits/Replication/Patching` | ⭐⭐ **the gate the JSON path uses** *(`JsonAttributeCompiler:40,58`)* — the check to MIRROR |
| 🔴 `BinaryInterpreter.Apply` — **no authority gate** | `Fdp.Toolkits/Replication/Patching/BinaryInterpreter.cs:102-128` | ⛔ **UXI-30**: dispatches every record to its handler with no `CanWrite` |
| `SimTransformAttributeInstaller` *(position)*; `AttributeCompilerFactory.BuildBinaryInterpreter` | `Hrot.Network.NED/Attributes` | ⭐ the installer to MIRROR for heading |
| ⭐⭐⭐ **`SimTransformBridgeSystem.HeadingDegToRotation(headingDeg)`** + `RotationToHeadingDeg` — *"Compass heading in degrees (0=North, 90=East, clockwise)"* | `Fdp.Toolkits/Geographic/Systems` | ⭐⭐ **the convention + conversion ALREADY EXIST** *(user was right)* — the installer REUSES this; ⛔ no new math |
| ⭐ **`EulerOri.Heading`** *(wire)* · `GeoSpatialEgressTranslator` already does **yaw → compass heading** *(`HeadingConversion_YawToCompass`)*; the DebugApi already takes `headingDeg` *(`GeoToLocal_WithHeadingDeg_IncludesRotation`)* | `Hrot.Network.NED/Common` · `Hrot.SimHost` | ⭐ heading is a first-class wire concept already |
| 🔴 `AttributeIds` — `Name/Affiliation/GeoLat=10/GeoLon=11/GeoAlt=12`, **NO `GeoHeading`** | `Fdp.Toolkits/Replication/Patching/AttributeIds.cs` | ⛔ **the ONLY gap: add `GeoHeading` to the Geo* family** — the convention/conversion/wire all exist |
| `UpdateEntityAttributeRequestSystem` *(binary branch)* — the receiving applier | `Hrot.*` | ⭐ where the gated request lands |
| `SimTransform` egress translator — **diffs `lastSent` every tick** | replication egress | ⚠ ⇒ a direct ECS write of rotation needs **NO change flag** *(user)* |
| ⛔ replication ingress *(`GeoSpatialIngressTranslator:85-89`)* writes unowned **by design** | replication | 🔒 **the inverse — do NOT gate it** *(Part 0 rule 8; the trap)* |

## 2. ⭐⭐⭐ THE ROUTING MODEL *(user ruling `2026-08-25`)*
The gizmo is **subsystem-agnostic**: it knows an entity + a target value, ⛔ not who owns it. A shared **write
helper** decides:
| case | action |
|---|---|
| ⭐ target component **owned locally** | **write ECS directly** *(+ set the component's change flag IF its egress translator needs one; ⭐ `SimTransform` does NOT — it diffs `lastSent`)* |
| ⭐ target component **NOT owned** | **publish a change-request** *(an `AttributeRecord` with the new `GeoHeading` id, in degrees → `UpdateEntityAttributeRequest`)* — the OWNER receives it |
| 🔒 the receiver | **UXI-30**: `BinaryInterpreter.Apply` now gates on `CanWrite` ⇒ applies the request **only to the component it owns**; a request for an unowned component is **skipped** |
| ⛔ replication ingress | **untouched** — still writes unowned ghosts by design *(the inverse gate; Part 0 rule 8)* |

## 3. ⭐ SCOPE
| ✅ IN | ⛔ NOT |
|---|---|
| **UXI-30**: the `CanWrite` gate on `BinaryInterpreter.Apply` *(mirror the JSON path)* | ⛔ touching replication ingress translators *(the inverse — breaking ghost updates is the trap)* |
| **`GeoHeading` attribute id** *(degrees, 0=N/90=E)* in `AttributeIds` + a `SimTransformHeading` installer that **reuses `HeadingDegToRotation`** | ⛔ a full attribute vocabulary — just heading, the motivating case; ⛔ no new conversion math *(it exists)* |
| **the subsystem-agnostic write helper** *(owned→direct / unowned→request)* the rotator gizmo drives | ⛔ vertex/route gizmos *(they keep the descriptor channel — ruling)* |
| **make `EntityRotatorGizmo` subsystem-agnostic** *(commit through the helper)* + reusable from CGF | ⛔ selection/symbology/tools *(later Axis-B slices)* |

## 4. ⭐⭐⭐ CLASS DIAGRAM
```mermaid
classDiagram
    direction LR
    class EntityRotatorGizmo {
        <<exists · make subsystem-agnostic · commit via the helper>>
        +OnCommit(yawRad)
    }
    class IEntityComponentWriter {
        <<NEW · the shared owned-vs-unowned router>>
        +Write(entity, attributeId, value)
    }
    class IEntityPatchContext {
        <<exists · the authority gate>>
        +CanWrite() bool
    }
    class BinaryInterpreter {
        <<exists · Apply — UXI-30 adds the CanWrite gate>>
        +Apply(record)
    }
    class AttributeIds {
        <<exists · ADD GeoHeading id · degrees 0=N 90=E>>
    }
    class SimTransformHeadingInstaller {
        <<NEW · reuses HeadingDegToRotation · no new math>>
    }
    class UpdateEntityAttributeRequestSystem {
        <<exists · receives the request, now gated>>
    }
    EntityRotatorGizmo ..> IEntityComponentWriter : OnCommit
    IEntityComponentWriter ..> IEntityPatchContext : CanWrite? (owned)
    IEntityComponentWriter ..> AttributeIds : unowned -> AttributeRecord GeoHeading
    UpdateEntityAttributeRequestSystem ..> BinaryInterpreter : Apply(record)
    BinaryInterpreter ..> IEntityPatchContext : UXI-30 gate
    BinaryInterpreter ..> SimTransformHeadingInstaller : heading handler
    note for IEntityComponentWriter "owned -> direct ECS write (SimTransform needs no change flag; egress diffs lastSent). unowned -> publish change-request. Replication ingress is NOT gated (the inverse)."
```

## 5. ⭐⭐⭐ SEQUENCE DIAGRAM
```mermaid
sequenceDiagram
    autonumber
    participant G as RotatorGizmo
    participant W as WriteHelper
    participant PC as PatchContext
    participant BI as BinaryInterpreter

    G->>W: Write entity GeoHeading degrees
    W->>PC: CanWrite owned locally
    alt owned locally
        W->>W: HeadingDegToRotation then write SimTransform Rotation
        Note over W: no change flag - egress diffs lastSent
    else not owned
        W->>BI: publish AttributeRecord GeoHeading as request
        BI->>PC: CanWrite UXI-30 gate
        alt owner owns it
            BI->>BI: HeadingDegToRotation then write SimTransform
        else not owned here
            BI->>BI: skip - guarded no unowned write
        end
    end
```

## 6. ⭐⭐ ITEMS
| # | task | the one thing not to get wrong |
|---|---|---|
| ⭐ **①** | **UXI-30** — add the `CanWrite` gate to `BinaryInterpreter.Apply` | ⛔ gate **change-request** appliers only; ⛔ **do NOT** touch replication ingress *(the inverse; Part 0 rule 8)*. ⭐ zero production senders ⇒ no compat surface |
| ⭐ **②** | **`AttributeIds.GeoHeading`** *(degrees, 0=N/90=E)* + a `SimTransformHeading` installer that **reuses `HeadingDegToRotation`** *(+ `RotationToHeadingDeg` for read-back)* | ⛔ **no new conversion math — it exists**; a `Float32`/`Float64` heading-degrees scalar in the Geo* family |
| ⭐ **③** | **`IEntityComponentWriter`** — the owned→direct / unowned→request router | ⭐ SimTransform direct write sets **no change flag** *(egress diffs lastSent)*; ⚠ other components may need one — the helper asks the component, it does not assume |
| ⭐ **④** | **`EntityRotatorGizmo` subsystem-agnostic** — commit through the helper; usable from CGF | ⛔ no SimHost-only ECS poke in the commit path |

## 7. GATES
rule 8 + build/test rules *(affected project: `Fdp.Toolkits`, `Hrot.SimHost`, `Hrot.Network.NED`, the integration suite)*. **Row 8 rails:** ⭐ **the UXI-30 gate** — a binary change-request for an UNOWNED component is skipped *(red by removing the gate)*; ⭐ **the replication inverse still works** — a ghost still receives owner state *(anti-regression, 29.23)*; ⭐ **rotation round-trips** — owned node rotates directly; a non-owning node's rotate becomes a request the owner applies *(on a real `--mode all` cluster — the barrier harness shape)*; ⛔ **do NOT add an MCP route** *(the MCP session owns the catalog)* — drive via existing entity ops / integration rails.

## 8. ⭐ WHEN DONE
Fold the as-built here; if the routing deviates, mark the prior state superseded *(obligation ⑤)*. State the ids *(a new prefix, e.g. `AX-`, in a new tracker area — clear of `MA-`/`CE-`)*. Flip the gap-map UXI-30 row + the Axis-B note. The report points here and carries the DECISION LOG.

---

## 9. ⭐⭐⭐ AS-BUILT — `2026-08-25` *(`AX-001`…`AX-006`; supersedes §1's gate row and §6 ①'s framing)*

> ⭐⭐ Written by the implementing session per obligation ⑤. Three of the four items landed as drawn; item
> ① landed for a **different reason than the design gave**, and §7's cluster round-trip is **not
> delivered** — both measured below rather than asserted.

### 9.1 ⛔⛔ Item ① — `UXI-30`'s premise was wrong, and the fix is better for it

| | |
|---|---|
| ⛔ §1 said | *"`BinaryInterpreter.Apply` — **no authority gate** … dispatches every record to its handler with no `CanWrite`"* |
| 📐 **measured** | **Both** production installers — `SimTransformAttributeInstaller` *(4 checks)* and `EntityDataAttributeInstaller` *(2)* — **already opened every handler** with `if (!ctx.PatchContext.CanWrite<T>()) return;` ⇒ ⭐ **the binary path WAS authority-gated**, per handler |
| ⭐⭐ **and that is the JSON path's own shape** | §1's own inventory points at `JsonAttributeCompiler`, whose gate lives in the typed `ValueInvoker<T>` — ⛔ **not** in the router either. ⇒ *"the router has no gate"* was never the defect; it is the architecture |
| ⭐⭐⭐ **the REAL defect** | the gate was **PER-INSTALLER and therefore FORGETTABLE.** ⚠ And this slice adds a **third** installer — exactly when that bites: one omitted line and an unowned component is written **silently** |

⇒ ⭐ **Built:** `BinaryInterpreterBuilder.RegisterHandler<TComponent>(id, handler)` applies the gate at
**registration**, so a handler body needs — and now contains — no guard. Both existing installers are
migrated onto it and their hand-written checks **deleted**, so there is ONE implementation of the check.
⭐ The rail asserts the gate on a handler that is a bare counter: ⛔ it proves the **registration** gates,
not that an author remembered to.

⚠ **The untyped overload deliberately still does NOT gate** — it is the right tool for a handler that
touches no ECS component *(a pure scratchpad accumulator)*. ⭐ Characterised by its own rail so the
boundary is documented rather than a hole.

✅ **§6 ①'s *"zero production senders ⇒ safe to switch on"* — VERIFIED independently:** only the
receiver (`UpdateEntityAttributeRequestSystem`) reads `AttributeRecords`; nothing populates them.

### 9.2 ⭐ Item ② — as drawn, and it really is only a constant plus a route

`AttributeIds.GeoHeading = 13` *(degrees, 0=N/90=E, in the `Geo*` family)* + `SimTransformHeadingInstaller`,
which **calls `SimTransformBridgeSystem.HeadingDegToRotation`** and writes no math of its own. ⭐ The user's
correction was right in full: the convention, the conversion, the wire field and the DebugApi's
`headingDeg` all already existed.

| ⚠ two notes worth keeping | |
|---|---|
| **numbering** | the class doc reserves 100–199 for geo, but the shipped `Geo*` ids are **10/11/12**. `GeoHeading` takes **13** to keep the family contiguous; ⛔ renumbering the existing three to match the prose is a WIRE change and was not attempted |
| **added unconditionally** | ⛔ unlike the position installer, heading needs **no `IGeographicTransform`** — a compass angle is already in the units the bridge takes. ⇒ heading works on a host with no geo transform, and gating it behind one would refuse rotation for no reason |

### 9.3 ⭐⭐⭐ Item ③ — the router, and the mechanism is better than the diagram

⭐ §4 draws `IEntityComponentWriter ..> IEntityPatchContext : CanWrite? (owned)`. ⛔ **The built router
never asks that question**, and deliberately: asking *"do I own `SimTransform`?"* would put the
attribute→component mapping in a **second** place, beside the installers that already hold it.

⇒ ⭐⭐ **It attempts the local apply through the very interpreter the OWNER uses, then asks
`EcsPatchContext.HasAppliedAny` whether anything landed:**

| outcome | meaning |
|---|---|
| something landed | owned ⇒ `Direct` — ⭐ and the conversion that ran was the installer's |
| nothing landed | `UXI-30`'s gate refused ⇒ publish the record as a change-request ⇒ `Requested` |
| no request sink | ⇒ `Refused` — ⛔ *"written"* and *"nobody to ask"* must not collapse into one answer |

⇒ ⭐⭐⭐ **ONE conversion implementation serves both the local and the remote path, and a second attribute
needs no change in the router at all.**

⭐ The change-flag question stays where the design put it: the **installer's handler** marks the
descriptor dirty or does not, so the component decides — the router never assumes.

### 9.4 ⛔⛔ Item ④ built; §7's CLUSTER ROUND-TRIP is NOT delivered

⭐ `EntityRotatorGizmo` commits through the writer in **compass degrees** — reusing
`SimMath.YawRadToCompassDeg`, which this file already used to draw its own label. ⭐ Railed that the two
conversions are **exact inverses** *(algebraically `HeadingDegToRotation(YawRadToCompassDeg(y)) =
FromYaw(y)`)*, because a sign error there rotates entities the wrong way **silently**.

⚠ **The writer is OPT-IN and the existing SimHost call site keeps its direct write** — see §9.5 for the
measured reason that is not merely caution.

🔴 **§7's *"rotation round-trips on a real `--mode all` cluster"* cannot be built yet.** 📐 There is **no
production SENDER** of binary attribute records — §6 ① says so itself, and it is verified. ⇒ the unowned
branch is railed behaviourally at unit level *(the request is published with the right id and value)*, and
the DDS egress for `UpdateEntityAttributeRequest.AttributeRecords` is a **distinct piece of work** beyond
these four items. ⛔ Not attempted; filed.

### 9.5 🔴🔴 THE PREMISE THE DESIGN DID NOT STATE — **"owned" is a bit almost nobody sets**

📐 Measured `2026-08-25`: `EntityRepository.HasAuthority` reads `EntityHeader.AuthorityMask`, and
`SetAuthority` has production callers in **exactly two places** — `Hrot.SimHost`'s bootstrapper *(for
entities it spawns)* and the **NED replication path** *(`DeferredTakeoverSystem`,
`OwnershipUpdateTranslator`)*. ⛔ **`Hrot.Editor` never calls it.**

⇒ ⚠⚠ **On a host that creates entities without granting authority, every attribute write looks UNOWNED**
and becomes a change-request — or, with no sink, a refusal. ⭐ That is the authority model working as
built, ⛔ but §2's routing model reads as though ownership were self-evident, and it is not.

⭐ **Consequences, recorded rather than silently absorbed:**
- the gizmo's writer is **opt-in**, and the SimHost call site is unchanged — ⛔ switching it wholesale
  would change behaviour on hosts whose entities carry no authority bits;
- **a later slice that wires the writer everywhere must first grant authority on the creating host**;
- railed as a characterization *(`TheWriterTreatsAnUngrantedComponentAsUnowned`)* so the next reader meets
  the fact rather than deriving it.

### 9.6 ⭐ The inverse — untouched, and now guarded for the future

📐 `GeoSpatialIngressTranslator` writes through a **command buffer** and only when
`!repo.HasAuthority<SimTransform>(entity)` — i.e. it writes exactly the unowned case, and it never touches
`BinaryInterpreter`. ⇒ ⭐ **this slice could not have gated it even by mistake.** ⭐⭐ A structural rail now
fails the day a replication ingress translator is routed through the change-request builder — the
plausible *"let us unify the two write paths"* refactor — and the shipped
`GeoSpatialIngressTranslatorTests` *(4/4)* remain the behavioural half.

## 11. ⭐⭐⭐ AX-005 SUCCESSOR — the cross-node egress, under **STRICT NETWORK SEPARATION** *(user ruling `2026-08-25`)*

> ⭐⭐⭐ **USER RULING, verbatim intent:** *"the gizmo should NEVER EVER use any DDS structure directly.
> Network must be strictly separated from internal FDP event processing — even at the cost of keeping the
> same enum duplicated in two namespaces (still numerically identical). The intent FDP-bus record uses its
> OWN enum; the egress translator converts to the network enum."*

### 11.1 ⛔⛔ THE RULING — a load-bearing engine invariant
⭐⭐ **No DDS/network type crosses into the FDP-internal event path.** The gizmo → write-router → FDP-bus
intent side speaks **FDP-internal types only**; the **egress translator is the SOLE boundary crosser**,
converting the internal record to the DDS wire message *(and the internal enum to the network enum)*.
⭐ **The precedent is established and named:** `Fdp.Toolkits.Navigation.NavigationIntent` *(internal ECS)*
vs `Hrot.Network.NED…NavigationIntent` *(wire)*, converted only inside `NavigationIntentEgressTranslator`.
⭐⭐⭐ **The two enums ALREADY EXIST for attributes:** **`AttributeValueKind`** *(FDP-internal —
`Fdp.Toolkits/Replication/Patching/AttributeValueKind.cs`)* and **`AttributeValueType`** *(network —
`Hrot.Network.NED/GenericMessages.cs`)* — numerically identical by design. ⛔ **Duplication here is the
CORRECT pattern, not debt.**

### 11.2 🔴 AS-BUILT FINDING — the merged writer took the shortcut this ruling forbids
📐 `AttributeEntityComponentWriter.Write` *(AX-003, merged)* builds a **`AttributeRecord` + `AttributeValueUnion`
+ `AttributeValueType.KindFloat64`** — **network/DDS types** — inside the FDP-internal write path, and applies
them through `BinaryInterpreter<AttributeRecord>`. ⇒ ⛔ **the FDP-internal side is currently coupled to the DDS
record shape.** ⭐ **AX-005 corrects this:** the router/gizmo operate on an **FDP-internal** change record
*(own `AttributeValueKind`)*; ⇒ ⚠ **verify + move** the network types out of the internal path — either the
local-apply mechanism switches to an internal representation, or the internal record converts to the DDS record
**only at the egress boundary**. 📌 Record which, and why, in the report.

### 11.3 ⛔ THE CORRECTED INTENT PATH — **SUPERSEDED by §12, `2026-08-25`** *(kept: the RULING it states is unchanged)*

> ⚠⚠ **The path below was the PLAN. §12 is the AS-BUILT, and it differs in one load-bearing way:**
> ⛔ the new `EntityAttributeChangeIntent` + new egress translator this table asks for were **NOT built** —
> ⭐ measured, both already existed *(`UpdateEntityAttributeCommand` + `UpdateEntityAttributeCommandEgressTranslator`,
> **registered in production**)*, so they were **EXTENDED**, per ruling 9. ⛔ **Do not quote §11.3–§11.5 as
> current.** ⭐ `R-134` itself *(§11.1)* is untouched and still binds.

| step | speaks | where |
|---|---|---|
| ⭐ gizmo commits *(heading deg / position)* → router | ⭐ **FDP-internal only** — an `EntityAttributeChangeIntent { networkId/entity, attributeId, value, `**`AttributeValueKind`**` }` | `Hrot.SimHost`/FDP toolkit |
| ⭐ owned → apply to ECS locally | FDP-internal | the installer/interpreter, internal side |
| ⭐ unowned → publish the **FDP-bus intent** *(NOT a DDS struct)* | ⭐⭐ **FDP-internal enum** | the write router's `_publishRequest` |
| ⭐⭐⭐ **the egress translator** subscribes to the intent, resolves entity→`NetworkId` *(`NetworkEntityMap`)*, and writes DDS `UpdateEntityAttributeRequest{AttributeRecords}` — **converting `AttributeValueKind` → `AttributeValueType`** | ⛔ **the ONLY place DDS appears** | `Hrot.Network.NED/…/Egress` — mirrors `UpdateEntityAttributeCommandEgressTranslator` |
| ⭐ owner receives → applies, ownership-gated | network in, ECS out | `DdsUpdateEntityAttributeRequestSource` → `UpdateEntityAttributeRequestSystem` binary branch *(AX-001 gate)* |

⚠ **Direction note:** this is a **change-request** *(non-owner → owner, event-driven on mouse-release)* — ⛔ NOT
the per-tick replication egress *(owner → ghosts)* like `NavigationIntentEgressTranslator`'s component scan. It
mirrors the **command** egress shape, not the descriptor-scan shape.

### 11.4 ⛔ CLASS DIAGRAM — **SUPERSEDED by §12.2**
```mermaid
classDiagram
    direction LR
    class EntityGizmo {
        <<rotator / drag · FDP-internal only>>
    }
    class WriteRouter {
        <<owned -> local ECS · unowned -> publish FDP intent>>
    }
    class EntityAttributeChangeIntent {
        <<NEW · FDP-internal record · uses AttributeValueKind>>
    }
    class AttributeRequestEgress {
        <<NEW · the ONLY DDS boundary · converts Kind to Type>>
    }
    class UpdateEntityAttributeRequest {
        <<exists · DDS wire · AttributeRecords + AttributeValueType>>
    }
    class UpdateEntityAttributeRequestSystem {
        <<exists · owner applies · gated by AX-001>>
    }
    EntityGizmo ..> WriteRouter : commit value
    WriteRouter ..> EntityAttributeChangeIntent : unowned -> publish on FDP bus
    AttributeRequestEgress ..> EntityAttributeChangeIntent : subscribe (FDP-internal)
    AttributeRequestEgress ..> UpdateEntityAttributeRequest : convert Kind to Type, resolve NetworkId
    UpdateEntityAttributeRequestSystem ..> UpdateEntityAttributeRequest : receive + apply gated
    note for AttributeRequestEgress "STRICT SEPARATION: DDS types (AttributeRecord, AttributeValueType, UpdateEntityAttributeRequest) appear ONLY here. The gizmo/router/intent are FDP-internal (AttributeValueKind). Precedent: NavigationIntent internal-vs-wire, converted in its egress."
```

### 11.5 ⛔ SEQUENCE DIAGRAM — **SUPERSEDED by §12.3**
```mermaid
sequenceDiagram
    autonumber
    participant G as Gizmo (FDP-internal)
    participant R as WriteRouter
    participant Bus as FDP event bus
    participant E as AttributeRequestEgress
    participant O as owner node

    G->>R: commit entity attributeId value
    alt owned locally
        R->>R: apply to ECS directly
    else unowned
        R->>Bus: publish EntityAttributeChangeIntent (AttributeValueKind)
        Bus->>E: intent (FDP-internal)
        E->>E: resolve NetworkId, convert Kind to Type
        E->>O: DDS UpdateEntityAttributeRequest AttributeRecords
        O->>O: apply, ownership-gated (AX-001)
    end
```

### 11.6 ⭐⭐ THE BUNDLED SLICE *(user: "do all at once")*
| # | item | note |
|---|---|---|
| ⭐ **AX-005** | the FDP-internal intent + the request egress *(11.3)* + fix the as-built coupling *(11.2)*; register the egress via the **network module**, ⛔ not `Program.cs` | round-trip rail on a real `--mode all` cluster: a non-owning node rotates a SimHost-owned entity → SimHost applies it, gated |
| ⭐ **AX-007** | **`EntityDragGizmo`** *(exists)* commits **position** through the same router *(`GeoLat`/`GeoLon`)* — move + rotate on one path | ruling 32 already wanted drag on the attribute channel |
| ⭐ **CE-035** | `RequestContinue`-after-step no-op → route through `RequestResume` | neutral-assembly; small |
| ⭐ **CE-036** | the stale `Requires CycloneDDS` skips — real cause: **domain id 250 out of range** | un-skip + fix or re-document |
| ⭐ **CE-018** | `EditorSubsystem`'s two inline `.csproj` walk-ups → `AssetRoots` | ⚠ same file the MCP-diagnostics slice touches for log-sink wiring — **different region**; coordinate |

⚠ **Parallel-safety with the running MCP-diagnostics slice:** disjoint bar `EditorSubsystem.cs` *(CE-018 walk-up
region vs the diagnostics log-sink pass)* — keep to distinct regions; rule 4 re-pull.

---

## 12. ⭐⭐⭐ AS-BUILT — `2026-08-25` *(`AX-005a/b/c`, `AX-007`, `AX-008`, `CE-018`, `CE-035`, `CE-036`)*

> ⭐⭐ **Obligation ⑤.** §11.3–§11.5 were the PLAN and are marked SUPERSEDED above. This section is what
> exists. ⛔ Quote THIS.

### 12.1 ⭐⭐⭐ THE ONE DEVIATION THAT MATTERS — **the intent and its translator ALREADY EXISTED**

📄 §11.3 asked for a **new** `EntityAttributeChangeIntent` and a **new** request egress translator.
📐 **Measured `2026-08-25`** *(`search_graph` + grep, both directions)*:

| what §11.3 asked to build | ⭐ what was measured |
|---|---|
| a new FDP-internal cross-node change intent | ⭐⭐ **`Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand` already is one** — FDP-internal *(no DDS reference in `Fdp.Toolkits`)*, and already published by `ExConOrbatAdapter` |
| a new egress translator writing `UpdateEntityAttributeRequest` | ⭐⭐⭐ **`UpdateEntityAttributeCommandEgressTranslator` exists and is REGISTERED IN PRODUCTION** — `Translators/Map/SharedTranslatorPack.cs:79`, so every NED node already has it |
| ⇒ | ⭐ **only the BINARY ARM was missing.** ⛔ A second intent + a second translator writing the same DDS topic would be two implementations of one concept *(ruling 9)* |

⇒ ⭐⭐⭐ **EXTENDED, not duplicated.** 📌 The seam law again: *"we need a shared X"* meant X existed and was
under-adopted. **25 measured instances.**

### 12.2 ⭐⭐ CLASS DIAGRAM — as built

```mermaid
classDiagram
    direction LR
    class IEntityComponentWriter {
        <<interface · Fdp.Toolkits · MOVED here by AX-007>>
        +Write(entity, attributeId, value) EntityWriteRoute
        +Write(entity, changes) EntityWriteRoute
    }
    class AttributeEntityComponentWriter {
        <<Hrot.Network.NED · the one implementation>>
    }
    class EntityWriteRouter {
        <<NEW · the one composition · For(repo)>>
    }
    class EntityAttributeChange {
        <<NEW · Fdp.Toolkits · AttributeValueKind · NO DDS>>
    }
    class UpdateEntityAttributeCommand {
        <<exists · EXTENDED with AttributeChanges>>
    }
    class EntityAttributeChangeRequests {
        <<NEW · PublishOnto(repo) · derives the bus from the world>>
    }
    class AttributeRecordConversion {
        <<NEW · R-134 SOLE BOUNDARY · both directions · explicit switch>>
    }
    class UpdateEntityAttributeCommandEgressTranslator {
        <<exists · REGISTERED SharedTranslatorPack:79 · EXTENDED with the binary arm>>
    }
    class EntityRotatorGizmo {
        <<exists · writer wired at all 5 sites>>
    }
    class EntityDragGizmo {
        <<Hrot.Presentation · AX-007 · commits GeoLat+GeoLon as ONE change>>
    }
    AttributeEntityComponentWriter ..|> IEntityComponentWriter
    EntityWriteRouter ..> AttributeEntityComponentWriter : builds
    EntityWriteRouter ..> EntityAttributeChangeRequests : publishRequest
    EntityRotatorGizmo ..> IEntityComponentWriter
    EntityDragGizmo ..> IEntityComponentWriter
    AttributeEntityComponentWriter ..> EntityAttributeChange
    EntityAttributeChangeRequests ..> UpdateEntityAttributeCommand : PublishManaged on repo.Bus
    UpdateEntityAttributeCommandEgressTranslator ..> UpdateEntityAttributeCommand : drains
    UpdateEntityAttributeCommandEgressTranslator ..> AttributeRecordConversion : ToNetwork
    note for AttributeRecordConversion "R-134: DDS AttributeRecord / AttributeValueUnion / AttributeValueType appear ONLY here and in the ingress system. Railed by StrictNetworkSeparationTests."
```

### 12.3 ⭐⭐ SEQUENCE DIAGRAM — as built

```mermaid
sequenceDiagram
    autonumber
    participant G as Gizmo (rotator or drag)
    participant W as AttributeEntityComponentWriter
    participant I as BinaryInterpreter of EntityAttributeChange
    participant Bus as world bus (repo.Bus)
    participant T as UpdateEntityAttributeCommandEgressTranslator
    participant O as owner node

    G->>W: Write(entity, changes)
    W->>I: Apply (the OWNER's own path)
    alt HasAppliedAny
        I-->>W: applied
        W-->>G: Direct
    else nothing landed (UXI-30 gate refused)
        W->>Bus: PublishManaged UpdateEntityAttributeCommand with AttributeChanges
        W-->>G: Requested
        Bus->>T: ReadManagedEvents
        T->>T: AttributeRecordConversion.ToNetwork per change
        T->>O: DDS UpdateEntityAttributeRequest with AttributeRecords
        O->>O: UpdateEntityAttributeRequestSystem applies, gated by AX-001
    end
```

### 12.4 ⭐ WHAT CHANGED VS THE PLAN — every deviation, named

| # | deviation | why |
|---|---|---|
| **①** | ⭐⭐⭐ **no new intent, no new translator** — `UpdateEntityAttributeCommand` gained `AttributeChanges`; the shipped translator gained a binary arm | §12.1 — both existed and the translator is registered in production |
| **②** | ⭐⭐ **`IEntityComponentWriter` + `EntityWriteRoute` MOVED to `Fdp.Toolkits`** *(`Fdp.Toolkit.Replication.Patching`)* | `AX-007`: `EntityDragGizmo` lives in `Hrot.Presentation`, which ⛔ must not reference the network assembly. The seam belongs where both sides see it; the IMPLEMENTATION stayed in NED |
| **③** | ⭐⭐ **the interface gained a MULTI-CHANGE overload** | a drag commits `GeoLat`+`GeoLon`; as two single writes the owner applies them a round trip apart and the entity lands on a coordinate pair nobody chose |
| **④** | ⭐⭐ **`EntityWriteRouter.For(repo)` — a factory, not five hand-built writers** | `EntityRotatorGizmo` is constructed in **five** places. Five hand-assembled writers is five chances to forget `publishRequest` — the SILENT-DEFAULT pattern. The dependency is derived, so it cannot be forgotten |
| **⑤** | ⭐ **`EntityAttributeChangeRequests.PublishOnto(repo)` takes the WORLD, not a bus** | the translator drains `view.ReadManagedEvents<T>()` — the WORLD bus. A bus parameter would let a caller pass the ORCHESTRATION bus; the command would publish successfully and be drained by nobody |
| **⑥** | ⭐ **`AX-008` — `NetworkIdResolver.RuntimeNetworkIdOf`** | `CgfSubsystem` and `EditorSubsystem` held **two private line-for-line copies**; the egress needed a third. Collapsed before writing it |
| **⑦** | ⭐ **the drag routes only the COMMIT, never the live preview** | one request per mouse-move would fight replication on an unowned entity every tick. ⚠ Consequence stated: on an unowned entity the preview is visibly reverted until the request lands |
| **⑧** | ⭐ **`CE-018` was FOUR walk-ups, not two** | measured: `EditorSubsystem` ×3 + `EditorApplication` ×1. All routed to `AssetRoots`; `EditorApplication`'s also gained the output-directory arm and ruling 67's config arm it never had |

### 12.5 🔴 FINDINGS

| # | finding |
|---|---|
| **F1** | ⭐⭐⭐ **`EntityRepository.GetSingletonManaged<T>()` THROWS when unset** despite its `T?` return type. 📐 Caught by the `AX-005` egress rail: the **IG never registers `IGeographicTransform`**, so `EntityWriteRouter.For` threw there. ⛔ A unit rail could not have seen it. ⭐ Fixed with `HasSingletonManaged` first — and the absence is normal, not a defect: that host simply has no `Geo*` handlers |
| **F2** | 🔴🔴 **`SimHost → IG` entity replication does not complete in this environment, and it is PRE-EXISTING.** 📐 Measured on a CLEAN tree at `03f92fefe` *(0 build errors)*: `DragDropIntegrationTests` fails with *"IG did not receive entity (netId=1)"*, and **21 of 51** tests in `Hrot.ClusterRunner.Integration.Tests` fail the same way before the host process crashes. ⇒ the full `--mode all` round-trip rail **cannot go green on this base**. ⭐ Kept red *(`R-131` — do not filter around)*; the provable half is green *(§12.6)* |
| **F3** | ⚠ **`CE-035`'s old rail ENCODED the defect.** `RequestContinue_WhenNotPaused_IsNoOp` asserted `ResumeCount == 0` — which is exactly why *step, look, continue* left the operator halted. Superseded, with the reasoning in the new rail's own remarks |
| **F4** | ⚠ **`CE-036`'s skip reason was WRONG.** *"Requires CycloneDDS"* — in an assembly whose other tests boot a real domain. Real cause: CycloneDDS derives ports as `7400 + 250 × domainId`, so `domainId = 250` asks for port `69900`. The usable ceiling is ≈ `232`. Fixed to `200`; all three skips removed |

### 12.6 ⭐ RAILS

| rail | where | state |
|---|---|---|
| ⭐⭐⭐ **`R-134` structural guard** — the internal write path names no DDS type; the boundary set is an EQUALITY | `Hrot.SimHost.Tests/StrictNetworkSeparationTests.cs` *(4)* | ✅ green · red-proved by inverse edit |
| ⭐⭐ **the egress half on a REAL cluster** — an unowned write leaves the node as a DDS request carrying the binary record, with the enum converted | `Hrot.ClusterRunner.Integration.Tests/AttributeChangeRequestRoundTripTests` | ✅ green · red-proved by disabling the binary arm |
| ⭐ **the owner applies the same attribute directly** | same file | ✅ green |
| 🔴 **the FULL `--mode all` round trip** | same file | ⛔ **red — blocked by `F2`**, kept as a live probe |
| ⭐⭐ **`AX-007` drag** — one call carrying both coordinates · no request during the preview · refusal restores · altitude survives · no-writer fallback | `Hrot.Presentation.Tests/EntityDragGizmoTests.cs` *(5)* | ✅ green |
| ⭐⭐ **`CE-035`** — continue-after-step resumes | `Hrot.Diagnostics.Breakpoints.Tests` *(2)* | ✅ green |
| ⭐⭐ **`CE-018`** — the walk-up has ONE implementation in production code | `Hrot.Editor.AiShared.Tests/…/TheWalkUpHasOneImplementationTests.cs` *(2)* | ✅ green |
| ⭐ **`CE-036`** — the three skips removed | `Hrot.ClusterRunner.Integration.Tests/HarnessSmokeTests.cs` *(5)* | ✅ green, 0 skipped |

---

## 13. ⭐⭐⭐ AS-BUILT `2026-08-26` — **`AX-011`/`AX-012`: the round trip is GREEN, and §12.5 `F2` is RESOLVED**

> ⛔⛔ **§12.5 `F2` and §12.6's red row are SUPERSEDED.** They recorded the full `--mode all` round trip as
> blocked on a pre-existing replication failure. 📐 That failure was **diagnosed and fixed**; the rail is
> green. ⭐ The `F2` text stays as history because its *measurement* was correct — only its verdict
> *("cannot go green on this base")* is now false.

### 13.1 ⭐⭐⭐ THE CAUSAL CHAIN — **one missing component, and one omitted argument**

```mermaid
sequenceDiagram
    autonumber
    participant Cat as TKB catalog
    participant Spawn as NetworkSpawningSystem
    participant Hook as SimHost onEntitySpawned
    participant Egr as GeoSpatialEgressTranslator
    participant IG as IG ghost
    participant Req as UpdateEntityAttributeRequestSystem

    Note over Cat,Hook: AX-011 - the egress shadow
    Cat->>Spawn: template declares EntityInfo and SimTransform mandatory
    Note over Cat: never declares NetworkTransform
    Spawn->>Hook: onEntitySpawned with isLocalAuthority
    Hook->>Hook: BEFORE - grant authority only if the shadow exists (dead branch)
    Hook->>Hook: AFTER - attach default NetworkTransform, then grant
    Egr->>Egr: query needs SimTransform + NetworkTransform + NetworkIdentity
    Note over Egr: matched 0 before, matches 1 after
    Egr->>IG: WorldPos (zero samples before)
    IG->>IG: ghost waits for SimTransform - a HARD requirement
    Note over IG: promotion correctly declined forever

    Note over Req: AX-012 - the dead binary arm
    Req->>Req: DDS ctor forwarded no binaryInterpreter
    Note over Req: hasBinaryRecords always false - records silently ignored
```

### 13.2 ⭐⭐ WHAT WAS BUILT

| # | change | where |
|---|---|---|
| ⭐⭐⭐ **`AX-011`** | **attach `default(NetworkTransform)` at birth, on the node that OWNS `SimTransform`**, then grant authority over it | `SimHostNodeBootstrapper`'s `onEntitySpawned` hook |
| ⭐⭐⭐ **`AX-012`** | the request system's **DDS constructor builds the binary interpreter itself** from the `geoTransform` it already takes | `UpdateEntityAttributeRequestSystem` |

### 13.3 ⭐⭐⭐ THE PLACEMENT DECISION — **rejected `NetworkSpawningSystem`, and the measurement is why**

⭐ The user's rule was *"every replicated entity on a subsystem which OWNS the `SimTransform` should get the
shadow at birth, regardless of template"* — ⭐⭐ and that is exactly what shipped. ⛔ **What changed is WHERE.**

| candidate | verdict |
|---|---|
| the **TKB catalog** *(`AddMandatoryComponent<NetworkTransform>`)* | ⛔ **rejected** — the shadow is a translator-internal cache, not a domain fact about a vehicle. It would make every template author responsible for a replication detail, and the next template would forget it exactly as this one did |
| **`NetworkSpawningSystem`** *(engine-level, the first choice)* | ⛔ **rejected on MEASUREMENT.** A bare `AddComponent` there **throws** *"Component NetworkTransform is not registered"* — 📐 the engine-level spawn system imposes a registration contract, and **37** files register `TkbIdentity` while only `HrotSharedComponentRegistry` registers `NetworkTransform`. ⇒ it would have needed 37 registry edits, two of them FDP example scenarios. ⭐ Attempted, measured, reverted |
| ⭐⭐ **`SimHostNodeBootstrapper`'s `onEntitySpawned` hook** *(shipped)* | ⭐⭐⭐ **it was ALREADY WRITTEN FOR THIS.** The hook read `if (world.HasComponent<NetworkTransform>(entity)) SetAuthority(...)` — an authority grant for a component **nothing ever attached**, so the branch was dead. 📌 The dead-affordance shape this programme keeps finding. ⭐ It runs on the one host that owns `SimTransform`, in an assembly where the component IS registered |

⚠⚠ **The cost of the shipped choice, stated:** it is **per-host**. A future host that owns `SimTransform` must
wire the same hook. ⛔ The engine-level placement would have been unforgettable; this one is not. ⭐ That is
why `TheEgressShadowExistsAtBirthTests` asserts the invariant on a **real spawn** rather than trusting the
wiring — and why it reproduces the translator's own query rather than merely checking the component.

### 13.4 ⚠⚠ SEEDING — **zeros, and it is a behavioural requirement**

The translator publishes only when the live pose differs from the shadow, or when the salted heartbeat fires
at `% 600` ticks. ⛔ Seeding from the entity's CURRENT `SimTransform` makes the first comparison say *"has not
moved"*, so a **stationary spawned entity is invisible to every other node for up to 10 s at 60 Hz**.
⭐ Zeros force the first tick to publish. ⚠ **Known residual:** an entity spawned at exactly the origin with
identity rotation still waits for the heartbeat — 📌 not railed as a bug, named here as the one case zero
seeding cannot force.

### 13.5 ⭐ RAILS

| rail | where | state |
|---|---|---|
| ⭐⭐⭐ **the full `--mode all` round trip** *(§9.4's open item)* | `AttributeChangeRequestRoundTripTests` | ✅ **GREEN** — was red on `AX-011`+`AX-012` |
| ⭐⭐ **`AX-011`** — shadow present · the translator's OWN query matches · `WorldPos` reaches the wire · owner has authority over it · first publish is prompt *(not heartbeat-delayed)* · the replica gets its copy from ingress | `TheEgressShadowExistsAtBirthTests` *(6)* | ✅ green · red-proved by removing the attach |
| ⭐⭐ **`AX-012`** — the **production factory's** system carries an interpreter · the JSON arm still does too · the DDS ctor needs no help | `TheBinaryArmIsWiredInProductionTests` *(3)* | ✅ green · red-proved by passing `null` |

⭐⭐ **Both rail sets assert on the CONSTRUCTED OBJECT / a REAL spawn, never on a registrar's source** — 📌 the
control CLAUDE.md's silent-default rule asks for, and the reason `AX-012` survived this long.

### 13.6 ⭐ MEASURED EFFECT

| | before | after |
|---|---|---|
| `WorldPos` samples on the wire *(300 frames)* | ⛔ **0** | ✅ **> 0** |
| the egress query's match count | ⛔ **0** *(1 without the shadow clause)* | ✅ **1** |
| IG ghost lifecycle | ⛔ **`Ghost`** forever | ✅ promoted, carries `SimTransform` |
| `DragDropIntegrationTests` | ⛔ 2 failed | ✅ **2 passed** |
| `SpawnMovingVehicleIntegrationTests.SimHostDrag_…` | ⛔ failed | ✅ **passed** |
| the `--mode all` round trip | ⛔ red | ✅ **green** |

⚠ **The suite total still aborts on the pre-existing test-host crash**, and the raw failure count moved
`21 → 24`. ⛔ **That is NOT a regression** — 📐 verified by diffing the failure SETS: **3 fixed, 0 new**. The
6 apparent additions never RAN in the earlier pass *(the crash truncated it)*; all were re-measured on a
clean tree at the started-marker and fail identically there.
