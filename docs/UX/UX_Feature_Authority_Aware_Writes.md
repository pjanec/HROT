<!--STATUS
state: LIVE
build-state: NOT-BUILT
verified: 2026-08-28 (coordinator source scan)
current-answer: NOT-BUILT (design only; also gated on UXI-30). Gizmos still write ECS unguarded (EntityRotatorGizmo.cs:152, EntityDragGizmo.cs:222/228); no PoseIntent* types; the UXI-30 binary authority gate absent.
-->
# Feature design — authority-aware ECS writes

> **Design for [UXI-29](UX_Issues.md#uxi-29) · drafted 2026-08-12.** **Status: ❌ NOT-BUILT (design only; also gated on UXI-30) — gizmos still write ECS unguarded (`EntityRotatorGizmo.cs:152`, `EntityDragGizmo.cs:222/228`); no `PoseIntent*` types; the UXI-30 binary authority gate absent.** Implements [ruling 22](UX_RESUME_INTERACTION.md) (*mutate ECS only where you own it*)
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

#### 🔒 Which gizmos this covers — and which it does not

`AttributeRecord` is `{ AttributeId, SubIndex1, SubIndex2, Value }` and `AttributeValueKind` is
**scalar only** — `Int32 · Int64 · Float32 · Float64 · Bool · String`. The two sub-indices carry *"list
position"* and *"nested list position"*, so indexed elements are expressible; **list structure is not.**

| Gizmo | Change | Records | Same mechanism? |
|---|---|---|:--:|
| **Drag** | position | `GeoLat` + `GeoLon` + `GeoAlt` | ✅ **yes** |
| **Rotate** | heading | `GeoHeading` (new) | ✅ **yes** |
| Inspector field edits (name, affiliation) | scalar | `Name` / `Affiliation` | ✅ yes — already have IDs |
| **Vertex edit** · **Route waypoints** | insert/delete/move **list elements** | — | ❌ **no** |

### 3.3b 🔒 RULED — the wire form is an **economics** decision; the gate is an **invariant**

> **User, 2026-08-12:** *"Whether to update the whole descriptor or just a single attribute is the network
> bandwidth / performance decision. Otherwise both should be applied with the ownership gate on the
> receiver."*

🔒 **This is the organising principle, and it is stronger than the split I drew above.** There are not
*"two architectures"* — there is **one architecture with two encodings**, chosen per payload:

| Payload | Cheaper encoding | Why |
|---|---|---|
| a scalar or three (position, heading, name) | **attribute records** | 3 records beat a whole descriptor |
| a list whose *structure* changes (polyline, route) | **whole descriptor** | ~80-120 records vs one payload; and set-at-index cannot express insert/delete |

⇒ **Expressiveness and bandwidth point the same way here**, which is why the split looks architectural. It
is not: it is an encoding choice, and a future payload could switch sides without touching the design.

#### 🔴 The invariant is not currently held — the applier census

| Applier | Gate | |
|---|---|:--:|
| **JSON attribute patch** — `JsonAttributeCompiler:40,58` | `CanWrite<T>()` → **native component authority** | ✅ |
| **Binary attribute records** — `BinaryInterpreter.Apply:102-128` | 🔴 **none** | 🔴 |
| **Descriptor — geo** `UpdateEntityDescriptorRequestSystem:142` | `HasAuthority(entity, packedKey)` → **descriptor key** | ⚠ |
| **Descriptor — overlay** `:190` | same | ⚠ |
| **`UpdateEntityCommand` local applier** — `NetworkSpawningSystem.ProcessUpdate:162-175` | 🔴 **none** | 🔴 |

🔴 **Two appliers gate on nothing; two gate on a *different notion of authority* than the fourth.** The
geo one carries a **`FIXME` saying exactly that** — *"Check native ECS component authority instead of the
descriptor key"* (`:139-141`).

⇒ 🔒 **[UXI-30](UX_Issues.md#uxi-30) widens accordingly**: it is not *"the binary path is missing a check"*
but *"**every** applier must gate, and on **one** notion of authority."*

### 3.3c 🔒 RULED — the notion is **native component ownership**, and there are exactly two classes of writer

> **User, 2026-08-12:** *"The descriptor applier needs anyway to check component ownership. Writing to
> unowned components is reserved for network replication ingress translators, never for change requests
> like attrib/descriptors. These must be received by any node and applied only to owned ECS components."*

🔒 **This settles the open question** I had left deliberately unresolved — descriptor appliers gate on
**native component authority**, not the descriptor key. The `FIXME` at
`UpdateEntityDescriptorRequestSystem:139-141` was right.

⭐ **And the rule has a second half that must be written down, because it inverts the gate:**

| Writer class | Purpose | Gate | Writes unowned? |
|---|---|---|:--:|
| **Replication ingress translator** | apply the **owner's state** to a local **ghost** | `if (HasAuthority) skip` | ✅ **yes — by definition.** A ghost *is* an unowned copy |
| **Change request applier** (attribute · descriptor · command) | apply a **request** from any node | `if (!HasAuthority) skip` | ❌ **never** |

⚠ **The two gates are exact inverses, and both are correct.** `GeoSpatialIngressTranslator:85-89` writes
`SimTransform` **only when `!isLocallyOwned`** — that is not a bug to be "fixed" by a sweep; it is
replication working as designed.

🔴 **This is the trap in UXI-30.** Someone applying *"everything must gate on ownership"* mechanically
would invert a replication translator and break ghost updates repo-wide. ⇒ **the rule must be stated as a
pair**, and the audit must classify each site before touching it.

✅ **Recorded where it belongs, 2026-08-12** — this is now **Part 0 rule 8** of
`docs/HROT-PROGRAMMERS-GUIDE.md`, beside rule 6 (*single-writer authority*) and rule 7
(*background ≠ main thread*), with a cross-reference from §1.5's loopback guard. It is an engine
invariant that UI work merely happened to surface, so it lives where a translator author would look —
not only in a UX feature doc. The known non-conformances are listed there too, pointing at
[UXI-30](UX_Issues.md#uxi-30).

🔒 **Vertex and route gizmos keep their existing channel.** *Setting a value at an index* cannot express
*inserting* or *deleting* a vertex — there is no list-length attribute, and a 40-vertex polyline would
become ~80-120 records per commit. They already use `UpdateEntityCommand` →
`UpdateEntityDescriptorRequest(dtMapVisualOverlay)`, which replaces the **whole descriptor** and is the
right shape for structural edits. ⭐ This matches the ruling's own wording — *"entity **attribute**
changing gizmos"*; a polyline edit changes geometry, not an attribute.

⚠ **But they still need §3.1 and §3.2**: they must stop writing ECS directly (§1 rows 3-4) and publish an
intent whose *remote half targets the descriptor request instead*. **The routing shape is universal; only
the wire form differs per intent kind.**

⚠ **Drag migrates off a working path.** IG's drag today sends `UpdateEntityDescriptorCommand(dtWorldPos)`
(`IgApplication.cs:2164-2198`) — acked and functioning. Ruling 32 moves it to attribute records. 🔒 That
is a deliberate consolidation, not an accident, but it is a **change to a live IG path** and needs the
same before/after care as any production-map change ([ruling 20](UX_RESUME_INTERACTION.md)).

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

### 3.4b 🔒 RULED — drag is unified: **one registration, no callbacks, no per-host code**

> **User, 2026-08-12:** *"Drag handling needs unification, sharing the same implementation in all map
> subsystems."*

**Today it is registered five different ways:**

| Subsystem | Registration | Send on drag |
|---|---|---|
| **IG** (network on) | `new EntityDragGizmoDefinition(onDragCommitted: …)` `:749` | ✅ → `OnEntityDragEnded` → `SendGeoSpatialUpdate` |
| **IG** (network off) | `new EntityDragGizmoDefinition()` `:757` | ❌ |
| **SimHost** | `new EntityDragGizmoDefinition()` `SimHostApp.cs:347` | ❌ |
| **Editor** | `new EntityDragGizmoDefinition()` `EditorSubsystem.cs:1120` — ⚠ the comment says *"has an optional callback constructor → register manually"*, and then passes none | ❌ |
| **CGF** · **ReplayBrowser** | **not registered at all** | — |

⭐ **The `onDragCommitted` hook has exactly one caller.** Seam-law again: an optional callback added for
one host, which then becomes the only host that behaves correctly.

⇒ 🔒 **After this design there is one form everywhere** — `new EntityDragGizmoDefinition()`, no callback,
because §3.2's egress translator does that job for **every** host. IG's `onDragCommitted`,
`OnEntityDragEnded` and `SendGeoSpatialUpdate` all **retire**.

#### ⭐ And the cadence question answers itself — continuous drag is **not production behaviour**

I expected IG's continuous-drag and shift-immediate modes to conflict with §3.4's *publish-on-commit*.
They do not, because **they are only reachable from a test hook**:

| Fact | Evidence |
|---|---|
| `_continuousDragTimer` is **incremented** only inside `TestHook_SimulateEntityMoved` | `IgApplication.cs:2291-2297` |
| In production it is only ever **reset** | `:2147`, `:2155` |
| `MapUserConfig.ContinuousDragUpdates` has **no production reader** | its only reads are the test hook and a test-only property `:2274-2275` |
| The shift-immediate path (`BUG2-I001`) is in the **same** test hook | `:2300-2303` |

⇒ 🔒 **Production drag is already commit-only in every host**, so §3.4 loses nothing and unification is
behaviour-preserving. ⭐ **Seam-law instance 14** — a config flag whose only reader is a test.

⚠ **If continuous cadence is wanted for real**, it becomes a **shared publisher policy** — how often the
gizmo publishes an intent — and *not* IG-specific network code. The routing below is unaffected either
way, which is the point of separating cadence from delivery.

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
| 29.18 | A **drag** intent encodes to `GeoLat`/`GeoLon`/`GeoAlt` records and round-trips within tolerance | H |
| 29.19 | 🔒 A **vertex/route** intent routes to the **descriptor** request, not attribute records — the wire form is chosen per intent kind, and the routing shape is unchanged | H |
| 29.20 | 🔒 **Every applier gates on ownership** — attribute (JSON + binary) *and* descriptor (geo + overlay) *and* the local `UpdateEntityCommand` consumer. Parameterised over all five; none may pass by omission | H |
| 29.21 | The **same** entity/component decision yields the **same** verdict on every channel — one notion of authority, not two | H |
| 29.22 | 🔒 A **descriptor** request for a component this node does not own is **ignored**, even when the node owns the *descriptor key* — the two notions must not diverge | H |
| 29.23 | 🔴 **Replication ingress still writes unowned components** — the inverse gate is preserved; a ghost keeps receiving owner state. The anti-regression guard for UXI-30's sweep | H |
| 29.24 | 🔒 **Every map subsystem registers drag identically** — same constructor, **no callback**; no host passes `onDragCommitted` | H |
| 29.25 | Drag in **CGF** works at all (it registers no drag gizmo today) | I |
| 29.26 | Production drag emits **one** intent per gesture in every host — the commit-only behaviour is preserved, not newly imposed | H |
| 29.17 | The intent is one **local `Fdp` bus event**; the translator is the only component that knows the wire form | H |

**23 H · 3 I · 0 V.**

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
