<!--STATUS
state: LIVE
build-state: REPORT — Axis-B first cut, dispatched at 7b1ea7bb9, built 2026-08-25
updated: 2026-08-25
current-answer: this file reports; the DESIGN owns the content. The as-built (and the corrected UXI-30
  premise) is DESIGN_Cgf_AxisB_Rotation_Slice.md §9.
known-conflict: none. Parallel-safe with the MCP create-recipes session — the one shared file
  (CgfSubsystem.cs) was NOT touched by this batch at all; see §0.
-->
# REPORT — **Axis-B first cut: the subsystem-agnostic write path, proven with rotation**

> 📌 **Dispatched at `7b1ea7bb9`** *(the handoff's own stamp is an unfilled `<coordinator HEAD>`
> placeholder; I used the HEAD that carries the `GeoHeading` correction)*. Scope frozen there.
> **Ids allocated: `AX-001` … `AX-006`, tracker Area M** *(a new prefix and a new area, as instructed —
> rule 5)*.
> 📄 **The design is the source; this report points at it:**
> [`DESIGN_Cgf_AxisB_Rotation_Slice.md` §9](../../DESIGN_Cgf_AxisB_Rotation_Slice.md).

## 0. ⭐ PROCESS

| # | |
|---|---|
| ⚠ **branch** | The handoff asks for a fresh branch. ⛔ This session is bound to `claude/reset-working-branch-qd1qpv`. Built there on a clean merge of the coordinator *(rule 7)*, started-marker pushed before any code *(rule 1b)* |
| ⭐ **collision** | ✅ **`CgfSubsystem.cs` was NOT touched at all.** The four items landed in `Fdp.Toolkits/Replication/Patching`, `Hrot.Network.NED/Attributes` and `Hrot.SimHost/Gizmos` — ⇒ **zero shared-file risk** with the MCP create-recipes session, better than the handoff's *"keep to the gizmo-registration region"* |
| ⛔ **no MCP route added** | as instructed; nothing near the generated catalog |
| ⭐ **ids** | `AX-` in Area M. ⚠ `tracker-counts.py` matches `BP-` rows only, so its **102 / 346** is unchanged and correct |

## 1. ⛔⛔ ITEM ① — `UXI-30`'s PREMISE IS FALSE, and that produced a better fix

| | |
|---|---|
| ⛔ **the design said** | *"`BinaryInterpreter.Apply` — **no authority gate** … dispatches every record to its handler with no `CanWrite`"* |
| 📐 **measured `2026-08-25`** | **Both** production installers already opened **every** handler with `if (!ctx.PatchContext.CanWrite<T>()) return;` — `SimTransformAttributeInstaller` *(4 checks / 3 handlers + pre-apply)*, `EntityDataAttributeInstaller` *(2 / 2)*. ⇒ ⭐ **the binary path WAS authority-gated** |
| ⭐⭐ **and that is the JSON path's own shape** | the design's own inventory points at `JsonAttributeCompiler`, whose gate lives in the typed `ValueInvoker<T>` — ⛔ **not in the router either**. ⇒ *"the router has no gate"* was never the defect; it is the architecture |
| ⭐⭐⭐ **the real defect** | the gate was **per-installer and therefore FORGETTABLE** — ⚠ and this slice adds a **third** installer, exactly when that bites: one omitted line writes an unowned component **silently** |

⇒ ⭐ **Built:** `BinaryInterpreterBuilder.RegisterHandler<TComponent>(id, handler)` applies the gate at
**registration**; both installers migrated onto it and their hand-written checks **deleted**, so there is
ONE implementation of the check. ⭐⭐ **The rail asserts it on a handler that is a bare counter** — ⛔ it
proves the *registration* gates, not that an author remembered to.

⚠ The **untyped** overload deliberately still does not gate *(the right tool for a handler touching no ECS
component)*, and has its own characterization rail so the boundary is documented rather than a hole.

✅ **§6 ①'s *"zero production senders ⇒ safe to switch on"* — verified independently, not taken on trust:**
only the receiver reads `AttributeRecords`; nothing populates them.

## 2. ⭐ ITEM ② — heading, and the user's correction was right in full

`AttributeIds.GeoHeading = 13` + `SimTransformHeadingInstaller`, which **calls
`SimTransformBridgeSystem.HeadingDegToRotation`** and writes **no math of its own**. 📐 The convention
*(its own doc: "0=North, 90=East, clockwise")*, the conversion, its inverse, the wire field
`EulerOri.Heading` and the DebugApi's `headingDeg` all already existed — the only gap was the constant.

| ⚠ two notes | |
|---|---|
| **numbering** | the class doc reserves 100–199 for geo but the shipped ids are **10/11/12** ⇒ `GeoHeading` takes **13** to keep the family contiguous. ⛔ Renumbering the shipped three is a WIRE change; not attempted |
| **unconditional** | ⛔ unlike the position installer, heading needs **no `IGeographicTransform`** — a compass angle is already in the bridge's units. ⇒ heading works on a host with no geo transform |

## 3. ⭐⭐⭐ ITEM ③ — the router's mechanism is BETTER than the diagram drew

⛔ §4 draws `IEntityComponentWriter ..> IEntityPatchContext : CanWrite? (owned)`. **The built router never
asks that**, deliberately: asking *"do I own `SimTransform`?"* would put the attribute→component mapping
in a **second** place beside the installers that already hold it.

⇒ ⭐⭐ It **attempts the local apply through the very interpreter the OWNER uses**, then asks
`EcsPatchContext.HasAppliedAny`:

| outcome | route |
|---|---|
| something landed | `Direct` — ⭐ and the conversion that ran was the **installer's** |
| nothing landed | the `UXI-30` gate refused ⇒ publish the change-request ⇒ `Requested` |
| no request sink | `Refused` — ⛔ *"written"* and *"nobody to ask"* must not collapse into one answer |

⇒ ⭐⭐⭐ **ONE conversion implementation serves both the local and the remote path, and a second attribute
needs no change in the router at all.** ⭐ The change-flag decision stays with the installer, as the
design required — the router never assumes.

## 4. ⭐ ITEM ④ — the gizmo, and the silent failure it is railed against

`EntityRotatorGizmo` commits through the writer in **compass degrees**, reusing
`SimMath.YawRadToCompassDeg` — which this file **already used to draw its own label**, so no new unit
enters the path. ⭐⭐ **Railed that the two conversions are exact inverses**
*(`HeadingDegToRotation(YawRadToCompassDeg(y)) = FromYaw(y)`, asserted as a facing vector since `q` and
`−q` are the same rotation)* — 🔴 because a sign or offset error there rotates entities the wrong way with
**no error anywhere**.

## 5. 🔴🔴 TWO THINGS THIS BATCH DOES NOT DELIVER — both measured, neither hidden

| # | |
|---|---|
| **`AX-005`** | ⛔ **§7's *"rotation round-trips on a real `--mode all` cluster"* is NOT delivered.** 📐 **There is no production SENDER of binary attribute records** — §6 ① says so itself and it is verified. ⇒ the unowned branch is railed **behaviourally at unit level** *(the request is published with the right id and value)*; the DDS **egress** for `UpdateEntityAttributeRequest.AttributeRecords` is a distinct piece of work beyond these four items. ⚠ **One of the design's own §7 rails could not be honoured — said plainly rather than reported green** |
| **`AX-006`** | 🔴🔴 **The premise the design did not state: *"owned"* is a bit almost nobody sets.** 📐 `SetAuthority` has production callers in **exactly two places** — `Hrot.SimHost`'s bootstrapper and the NED replication path *(`DeferredTakeoverSystem`, `OwnershipUpdateTranslator`)*. ⛔ **`Hrot.Editor` never calls it.** ⇒ on a host that creates entities without granting authority, **every** attribute write looks unowned. ⭐ That is the authority model working as built, ⛔ but §2's routing model reads as though ownership were self-evident. ⇒ ⭐ **this is why the gizmo's writer is OPT-IN and the SimHost call site keeps its direct write**, and ⭐⭐ a later slice that wires it everywhere **must grant authority on the creating host first** |

## 6. ⭐⭐ DECISION LOG

| # | ambiguity | decision, and why |
|---|---|---|
| 1 | The design says *"add the gate to `Apply`"*, but `Apply` cannot know which component an id maps to. | ⭐ Gate at **registration** (`RegisterHandler<TComponent>`) — the mirror of the JSON path's `ValueInvoker<T>`, which is what the design's own inventory pointed at |
| 2 | Leave the installers' existing inline checks as defence in depth? | ⛔ No — **deleted**. Two checks for one rule is the duplication the structural gate exists to end; leaving them would hide whether the gate works |
| 3 | Should the untyped `RegisterHandler` be removed so nobody can register ungated? | ⛔ No — it is the right tool for a handler that touches no ECS component. ⭐ Kept, **characterised by a rail**, so the boundary is documented |
| 4 | A fourth handler on `SimTransformAttributeInstaller`, or a separate heading installer? | ⭐ **Separate.** That installer accumulates lat/lon/alt into a scratchpad and converts ONCE at flush, because the three are one geodetic point; heading is an independent scalar needing neither |
| 5 | Gate the heading installer behind `IGeographicTransform`, like position? | ⛔ No — heading needs no transform, and gating it would refuse rotation on a host that could serve it |
| 6 | How does the router know ownership? | ⭐⭐ **It does not ask.** Attempt the local apply through the owner's own interpreter and read `HasAppliedAny` — ⇒ one conversion implementation, and no second copy of the attribute→component mapping |
| 7 | Switch the SimHost gizmo call site to the writer? | ⛔ No — `AX-006`: entities without granted authority would start routing as requests. ⭐ Writer is opt-in; the finding is railed and filed |
| 8 | Build the request egress so §7's cluster rail can exist? | ⛔ No — beyond the four items, and the design itself notes there are no senders. **Filed as `AX-005`** |
| 9 | Renumber `GeoLat/Lon/Alt` to the documented 100–199 range? | ⛔ No — they are a WIRE schema. `GeoHeading` takes 13 and the mismatch is documented |

## 7. GATES *(rule 8 contract)*

⭐ Built ONCE per project, then `--no-build`. ⛔ **The full solution was never built.**

| # | gate | verbatim command | `--no-build` | result | Δ vs `c3a598b16` *(started-marker)* |
|---|---|---|:--:|---|---|
| 1 | ⭐⭐ **the Axis-B rails** | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/… --no-build --filter "TheGateCannotBeForgottenTests"` | ✅ | ⭐ **19 / 0** | **+19**, all new |
| 2 | SimHost *(the rails' home)* | `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/… --no-build` | ✅ | ⚠ **691 / 1 / 3** *(run 1)*, **687 / 5 / 3** *(run 2)* | **+19 total**; reds are the characterised order flake |
| 3 | ⭐⭐ **the replication INVERSE** *(anti-regression)* | `dotnet test Hrot/Engine/Hrot.Map.Common.Tests/… --no-build --filter "GeoSpatialIngressTranslatorTests"` | ✅ | ✅ **4 / 0** | 0 — ghosts still receive owner state |
| 4 | affected-project builds | `dotnet build {Fdp.Toolkits, Hrot.Network.NED, Hrot.SimHost} --no-restore` | — | ✅ all green | — |
| 5 | ⭐ **`T3` system suite** | `bash scripts/run-system-tests.sh` **(BACKGROUNDED)** | build-once | ✅✅ **102 / 0 / 0**, exit 0, 6 m 39 s | **0** — unchanged, as predicted |
| 6 | tracker | `python3 scripts/tracker-counts.py --check` | — | ✅ **OK — 102 / 346** | unchanged: `BP-` only |
| 7 | ledger | `python3 scripts/rulings-check.py` | — | ✅ **25/25** | 1 pre-existing staleness WARN |
| 8 | design gate | `python3 scripts/design-digest.py --check` | — | ✅ **OK, 86 docs** | — |
| 9 | mermaid | `MERMAID_PREFIX=/tmp/mm node scripts/mermaid-check.mjs docs/DESIGN_Cgf_AxisB_Rotation_Slice.md` | — | ✅ **2/2 parse** | — |

⭐ **Tree CLEAN after every suite run**; no goldens moved; **no new skips**.

### 7a. Reds attributed — by `git diff`, not rebuild

📐 My production diff is 6 files: `BinaryInterpreterBuilder.cs`, `AttributeIds.cs`,
`{SimTransform,EntityData}AttributeInstaller.cs`, `AttributeCompilerFactory.cs`,
`EntityRotatorGizmo.cs`, plus **new** `SimTransformHeadingInstaller.cs` / `IEntityComponentWriter.cs`.

| red | verdict |
|---|---|
| `Hrot.SimHost` — `EqsModuleTests` *(run 1)*; `MissionControlExecutionSystemTests`, `ComponentRegistryTests`, `EditLoadClusterOpHandlerTests`, `JsonToRecordCompilerTests`, `FullBranchPipelineTests` *(run 2)* | ⭐⭐ **the static-`ComponentTypeRegistry` order flake, DEMONSTRATED**: two runs of the **same binary** gave **1 red then 5 red with entirely different sets**, and `EqsModuleTests` is **8/8 in isolation** *("Component type ID 69 is not registered")*. ⚠ `FullBranchPipelineTests` is the separate missing-file-in-`/tmp` IO test, red in isolation too, untouched since `2026-07-16` |

### 7b. ⭐⭐ REVERT-GOES-RED — inverse edit, never `git checkout --`

| inverse edit | result |
|---|---|
| the `CanWrite<TComponent>()` line removed from `RegisterHandler<TComponent>` | 🔴 **5 red**: `ATypedHandlerNeverRunsForAnUnownedComponent`, `AnUnownedRecordNeverFetchesTheComponent`, `TheWriterTreatsAnUngrantedComponentAsUnowned`, `AnUnownedWriteBecomesAChangeRequest`, `AnUnownedWriteWithNoSinkIsRefusedNotSilentlyDropped` |

⭐ **One inverse edit reddens the gate, the router's unowned branch and the `AX-006` characterization at
once** — which is the honest shape here: they are all consequences of the same gate.

## 8. ✅✅ `T3` — the system suite: **102 / 0**, exit 0, 6 m 39 s

⭐ Backgrounded per the build rules and never sat on; it completed within the batch.

| | |
|---|---|
| ⭐⭐ **`102 / 0 / 0`** | the same count the coordinator's MCP batch reported ⇒ **zero delta, zero reds** |
| ⭐⭐⭐ **and zero delta is the RIGHT result here** | 📐 predicted before the run, on three grounds: the installer migration is **behaviour-preserving** *(the same check, moved)*, the heading installer is purely **additive** *(a previously-unhandled attribute id)*, and the gizmo's writer is **opt-in** with the existing SimHost call site untouched. ⇒ ⭐ a CHANGED count would have meant one of those three claims was wrong |
| ⚠ **what it does NOT prove** | ⛔ it does not exercise the new heading attribute or the router at all — nothing on the `--mode all` path sends an attribute record *(`AX-005`)*. ⭐ Its value here is purely **anti-regression on the paths the migration touched** |

## 9. ⚠ OPEN

| | |
|---|---|
| `AX-005` | no production SENDER of binary attribute records ⇒ the `--mode all` rotation round-trip cannot be railed yet |
| `AX-006` | *"owned"* is an authority bit only SimHost and replication set; a slice that wires the writer everywhere must grant authority on the creating host first |
| ⛔ **untouched, by instruction** | vertex/route gizmos *(they keep the descriptor channel)* · selection/symbology/tools *(later Axis-B slices)* · anything near the MCP catalog |
