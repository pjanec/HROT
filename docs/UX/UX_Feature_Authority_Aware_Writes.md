# Feature design — authority-aware ECS writes

> **Design for [UXI-29](UX_Issues.md#uxi-29) · drafted 2026-08-12.** **Status: ✅ designed — ready to break
> into `UXT` tasks.** Implements [ruling 22](UX_RESUME_INTERACTION.md) (*mutate ECS only where you own it*)
> and [ruling 30](UX_RESUME_INTERACTION.md) (*structural changes via the command buffer*).
>
> **User, 2026-08-12:** *"We need the code uniform and shared, so it writes directly (or via ECB) if the
> component is owned, and sends a network request if the ECS component is not owned."*

## 0. Prior art ([rule 6](UX_Issues.md#rules)) — ⭐ **almost all of it already exists**

| Exists? | What | Bearing |
|:--:|---|---|
| ✅ | **`HasAuthority<T>(entity)`** — a **per-component** bit in a per-entity `AuthorityMask` (`EntityRepository.cs:1060-1063, 1120-1127`) | ⭐ **the exact primitive the rule needs.** A Brain can own `BehaviorState` while a Muscle owns `SimTransform` on the *same* entity |
| ✅ | **The complementary-gate precedent** — tactical intent (§2) | ⭐ **the shape to copy**, verbatim |
| ✅ | **`UpdateEntityDescriptorRequest(dtWorldPos)`** — a pose request, owner-side authority-gated, **acked** | ⭐ **the pose channel already exists**, and IG already uses it |
| ✅ | **`UpdateEntityAttributeRequest`** — generic inbound patch (JSON or ATTR2 binary), per-field authority-gated **inside the compiler** | ⭐ generalises beyond pose. `JsonAttributeCompiler.cs:37-41`: `if (!context.CanWrite<T>()) { reader.Skip(); return; }` |
| ⚠ | `AttributeIds` — **five constants total**; `SimTransformAttributeInstaller` registers Lat/Lon/Alt with a flush bit (`:81-86`) | ⇒ **`GeoHeading` is one constant + one handler**, mirroring the existing three |
| ✅ | `DestroyEntityCommand` → `DeleteEntityRequest` → owner → two-phase ack | the request/ack template CGF's *Delete* already follows correctly |
| 🔴 | **Four gizmos write `SimTransform`/managed components with no authority check** | **the gap** — §1 |
| 🔴 | `NetworkSpawningSystem.ProcessUpdate` applies `UpdateEntityCommand` **unconditionally** | `:162-175` — no authority check before `SetComponent` |

⇒ 🔒 **This design builds almost nothing. It routes existing UI writes into an existing channel.**

## 1. 🔴 The verified gaps

| | Site | Writes | Guard |
|---|---|---|---|
| 1 | `EntityDragGizmo.ApplyPosition` `:150-164` | `SimTransform.Position`, **every drag frame** | `IsAlive` + `HasComponent` only |
| 2 | `EntityRotatorGizmo.CommitRotation` `:115-122` | `SimTransform.Rotation` | same — ⚠ **this is CGF's *Rotate*** |
| 3 | `VertexEditGizmo.WriteBackAndPublish` `:194-211` | `SetManagedComponent` (polyline) **then** publishes | local write is unconditional |
| 4 | `RouteWaypointGizmo.WriteBackAndPublish` `:212-230` | mutates `RoutePlan` in place **then** publishes | same |
| 5 | `NetworkSpawningSystem.ProcessUpdate` `:162-175` | `EntityComponentReflector.SetComponent`, unconditional | 🔴 **the local consumer of #3/#4 does not re-check either** |

⚠ **IG is the near-miss.** It registers the drag gizmo with an `onDragCommitted` callback that *does* send
the proper request (`IgApplication.cs:748-753` → `SendGeoSpatialUpdate:2164-2198`). So IG **writes locally
and asks the owner** — accidental client-side prediction, where the local write is silently corrected by
the next ingress sample. ⭐ **The right channel is already wired; it is opt-in, per host, and unguarded.**

## 2. ⭐ The precedent — copy this shape exactly

Two systems read **the same event** each frame; their gates are **logical complements**, so exactly one
acts:

```csharp
// LOCAL half — TacticalIntentResolutionSystem.cs:86-96
foreach (var evt in repo.Bus.ReadManaged<AssignTacticalIntentEvent>()) {
    if (!repo.HasAuthority<BehaviorState>(evt.Entity)) continue;   // I don't own it → not mine
    ... apply locally
}

// REMOTE half — TacticalIntentEgressTranslator.cs:64-90
foreach (var evt in repo.Bus.ReadManaged<AssignTacticalIntentEvent>()) {
    if (repo.HasAuthority<BehaviorState>(evt.Entity)) continue;    // I own it → handled locally
    _writer.Write(new TacticalIntentRequest { ... });               // → owner applies it
}
```

⭐ **This is better than an `if/else` helper** for what the user asked: the **publisher never branches at
all**. UI code states an *intent* and is done; ownership is somebody else's problem. That is what makes
the code uniform — there is nothing per-host left to get wrong.

## 3. The design

### 3.1 🔒 Gizmos publish an intent. They never write ECS.

```csharp
public sealed class SetEntityPoseIntent {         // managed event on the interaction bus
    public Entity   Entity;
    public Vector3? Position;                      // null = unchanged
    public Quaternion? Rotation;
}
```

All four sites in §1 stop calling `GetComponentRW`/`SetManagedComponent` and publish this instead.

### 3.2 Two complementary systems, registered by **every** subsystem

| System | Gate | Action |
|---|---|---|
| **`PoseIntentApplySystem`** | `if (!HasAuthority<SimTransform>) continue` | apply **via the ECB** ([ruling 30](UX_RESUME_INTERACTION.md)) |
| **`PoseIntentEgressTranslator`** | `if (HasAuthority<SimTransform>) continue` | 🔒 send `UpdateEntityAttributeRequest` carrying **binary `AttributeRecords`** (ATTR2) |

🔒 **Identical registration in every subsystem.** No host-specific wiring, no callbacks, no opt-in:

| | Owns `SimTransform`? | Which half fires | Code difference |
|---|---|---|---|
| **Editor** | always ([ruling 22](UX_RESUME_INTERACTION.md) — *"Editor owns all"*) | local | **none** |
| **SimHost** (Muscle) | for its own entities | local | **none** |
| **CGF** (Brain) | never — delegated at spawn | remote | **none** |
| **IG** | depends | either | **none** — replaces its bespoke callback |

⭐ **That is the whole point**: CGF's *Rotate* becomes correct without CGF containing a single line about
ownership.

### 3.3 🔒 RULED — the wire form is the **binary attribute change request**

> **User, 2026-08-12:** *"If intent is a local FDP event translated to a network binary attribute change
> request message, then OK — let's use intents in entity-attribute-changing gizmos."*

🔒 **Adopted.** A local `Fdp` bus event in, `UpdateEntityAttributeRequest.AttributeRecords` out — the
strongly-typed **ATTR2 binary** list keyed by 16-bit attribute IDs, *not* the JSON patch and *not* the
`dtWorldPos` descriptor path. ⭐ One channel for every attribute-changing gizmo, so *"entity attribute
change"* has a single wire form.

⇒ This also **generalises the design past pose by construction**: any gizmo that changes an attribute
publishes an intent, and the egress translator encodes whichever `AttributeRecord`s it owns.

#### 🔴 Two gaps must be closed first — both verified

| # | Gap | Evidence |
|--:|---|---|
| **1** | 🔴 **The binary path has NO authority gate.** `CanWrite<T>()` / `CanWriteManaged<T>()` are called **only** from `JsonAttributeCompiler` (`:40`, `:58`). `BinaryInterpreter.Apply` dispatches every record to its handler with no check — *"Unknown IDs: silently skipped"* is the only filter | `BinaryInterpreter.cs:102-128`; `grep CanWrite` over `Replication/Patching/*.cs` returns JSON only |
| **2** | 🔴 **No rotation attribute ID exists.** `AttributeIds` declares **five** constants total — `Name=1`, `Affiliation=2`, `GeoLat=10`, `GeoLon=11`, `GeoAlt=12`. There is **no heading / pitch / roll / quaternion**, so **CGF's *Rotate* — the motivating case — cannot be expressed on this channel today** | `AttributeIds.cs:36-69` |

🔒 **RULED 2026-08-12 — gap 1 is a severe error and is now [UXI-30](UX_Issues.md#uxi-30)**, a prerequisite
for this design: *"if the binary attrib request applier does not check ownership, it looks like a severe
error that needs fixing — no reason why the binary path should be different from the JSON path in this
aspect."*

⭐ **And the census makes the fix free: there are ZERO production senders of `AttributeRecords` today.**
The only references are on the **receiving** side (`UpdateEntityAttributeRequestSystem.cs:168-182`); no
production code constructs the list. ⇒ **the risk I raised — that switching the gate on would silently
break existing senders — does not exist.** ⚠ It also means the binary path is **receive-only with no
producers** (⭐ **seam-law instance 13**), and this design would be its **first**.

🔒 **Gap 2 — RULED: add `GeoHeading`.** ⭐ It mirrors an existing pattern exactly: `SimTransformAttributeInstaller`
already registers `GeoLat`/`GeoLon`/`GeoAlt` with a pre-apply handler and a flush bit (`:81-86`), so
heading is **one constant + one handler + one line in the flush**.

⚠ **Gap 2's remaining wrinkle is cheap but real** — the file documents an extension pattern (a companion `static class` in the
domain assembly). ⚠ **Also fix the range comment while there**: the section is headed *"Range 100-199:
Geo-spatial / positional"* and then declares `10`, `11`, `12` — the declared range and the actual values
disagree, which will mis-allocate the next ID somebody adds.

⚠ **Consolidation question, not decided here:** pose can now travel **two** ways — `dtWorldPos`
(descriptor, acked, used by IG today) and attribute records. Ruling 32 chooses the latter for gizmo
intents; whether `dtWorldPos` is later retired is a separate call.

### 3.4 🔒 Preview is visual, not a write

A drag must show feedback every frame, but must not write ECS every frame — and on a non-owned entity a
local write would be reverted by the next ingress sample (visible snap-back).

⇒ **The gizmo draws its own preview** (it already emits preview primitives) and publishes the intent
**on commit only**. ⚠ This also removes the per-frame network question: one request per gesture, not per
frame.

### 3.5 Fix the unconditional local applier

`NetworkSpawningSystem.ProcessUpdate` (`:162-175`) must gate on authority too, or #3/#4's local write
simply moves one level down.

## 4. Acceptance

| # | Case | Cls |
|---|---|:--:|
| 29.1 | Owned entity + pose intent → component changes, **no** network request emitted | H |
| 29.2 | Un-owned entity + pose intent → **request emitted, component unchanged locally** | H |
| 29.3 | 🔒 The two gates are **exact complements** — for any entity, exactly one half acts, never both, never neither | H |
| 29.4 | The apply half writes **through the ECB**, not the live repo | H |
| 29.5 | 🔴 **CGF *Rotate* on a SimHost-owned entity emits a request and writes no ECS** — the filed defect | H |
| 29.6 | Editor *Rotate* writes locally and emits **no** request (it owns everything) | H |
| 29.7 | Drag emits **one** request per gesture, not one per frame | H |
| 29.8 | During a drag on a non-owned entity the preview moves and **the component does not** | H |
| 29.9 | `NetworkSpawningSystem.ProcessUpdate` **skips** components it has no authority for | H |
| 29.10 | Authority is honoured **per component**, not per entity — an entity owned for A and not B routes each accordingly | H |
| 29.11 | No gizmo calls `GetComponentRW`/`SetManagedComponent` on a live repo — the regression guard | H |
| 29.12 | CGF: rotate an entity → SimHost applies it → the rotation appears in CGF via ingress | I |
| 29.13 | The owner's ack is observed; a rejected request leaves both nodes consistent | I |
| 29.14 | 🔴 **The binary path honours authority** — a record for a component this node does not own is **skipped, not applied**. ⇒ [UXI-30](UX_Issues.md#uxi-30)'s acceptance, restated here because this design depends on it | H |
| 29.15 | An unknown attribute ID is skipped without error — forward compatibility preserved | H |
| 29.16 | A rotation intent encodes to a **`GeoHeading`** `AttributeRecord` and decodes to the same rotation within tolerance | H |
| 29.17 | The intent is one **local `Fdp` bus event**; the translator is the only component that knows the wire form | H |

**15 H · 2 I · 0 V.**

## 5. 🔒 Out of scope

| | |
|---|---|
| Changing the DDS schema | pose IDs exist (`AttributeIds` 100-199); `dtWorldPos` exists |
| Optimistic prediction / rollback | §3.4 chooses preview-only instead — simpler and flicker-free |
| Ownership transfer on demand (*"let me edit it here"*) | a real feature, unfiled; `DeferredTakeOwnership` would be the vehicle |
| The `NetworkAuthority` vs `NetworkOwnership` duplication | ⚠ **pre-existing debt**, noted in the repo's own batch notes — do not chase it here |

## 6. Risks

| | |
|---|---|
| ⚠ **The owner-side gate checks the descriptor key, not the component** | `UpdateEntityDescriptorRequestSystem.cs:139-141` carries a **`FIXME` saying exactly that** — *"Check native ECS component authority instead of the descriptor key"*. ⚠ Our client-side gate uses `HasAuthority<SimTransform>`; if the two disagree, a request is sent and silently dropped. **Resolve the FIXME as part of this work** |
| ⚠ **Two authority concepts exist** — `NetworkAuthority` vs `NetworkOwnership` | pick `NetworkAuthority` (what `HasAuthority` reads) and say so; do not merge them here |
| ⚠ **Entities with no `NetworkAuthority` component are treated as owned** (`AuthorityExtensions.cs:33-40`) | correct for the offline Editor; ⚠ means a missing component silently grants authority — assert it in tests rather than trusting it |
| ⚠ **Latency changes the feel** — a remote rotate now completes on the owner's tick + round trip | it is the correct behaviour and matches *Delete*; ⭐ but the user sees a delay where they previously saw an instant (wrong) result. Consider a *pending* affordance — [UXI-27](UX_Issues.md#uxi-27)'s progress surface is the natural home |
| ⚠ **Fire-and-forget vs acked** | tactical intent is unacked; `UpdateEntityAttributeRequest` has an opt-in `RequireAck`. 🔒 **Set it** for gizmo intents so 29.13 is testable and a rejection is observable |
| ✅ ~~Adding the binary authority gate could break existing senders~~ | **Withdrawn — census done: zero production senders exist.** The gate can go in ahead of this design with no compatibility surface. Tracked as [UXI-30](UX_Issues.md#uxi-30) |
| ⚠ **This design is blocked on [UXI-30](UX_Issues.md#uxi-30)** | shipping the intent path onto an ungated channel would move the unguarded write from the gizmo to the receiver — **no net safety gain**. Order matters: gate first, then intents |
