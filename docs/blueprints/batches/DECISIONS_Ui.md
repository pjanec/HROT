<!--STATUS
state: LIVE — the ui lane's open questions for the coordinator / user (PROTOCOL_Autonomous_Lanes §3), newest at the top
updated: 2026-11-18
current-answer: the whole file
-->

# DECISIONS — ui

## CE-1147 (§7) — where does the shared stance pair live, given the network pack cannot see it? *(asked 2026-11-18)*

**DECIDED `2026-11-18` (user, via coordinator): B — ⛔ NOT A.** 🔒 User: *"keep in mind that network layer needs to be separated from hrot internals - even enums are duplicated"* (R-4295) ⇒ `Hrot.Network.NED` gains NO reference to the animation assemblies. The stance pair stays in `Hrot.Animation.Replication`; ONE identical pair (both egresses gated on ownership — they already are, `StanceIntentEgressTranslator.cs:68-69` — and both ingresses SKIPPING the node's own samples, `EntityPossessedByIngressTranslator.cs:58`'s pattern) is handed by every host to the node builder's existing seam (`HrotNodeBuilderWithReplication.WithTranslators`) — first measure which builder path each host (CGF, SimHost, Player/IG, Stride, editor) takes; `RegisterStanceOn`, `RegisterStanceViewerOn` and the role-split `StanceTranslators(role)` are deleted.
🔒 User: *"can you assure the system stays operable without needing to run 40+ scenarios? changes in network code has always been fragile, are you sure the change is small, is it also safe?"* ⇒ **the SAFETY FENCE (all binding):**
- ⛔ the WIRE does not change: same topics, ordinals 104 / 105, QoS, owner groups (`NedOwnershipGroupBinding`), IDL — if any must change, STOP and write it here;
- ⭐ the one NEW behaviour is the ingress skip-own (a node now receives its own stance samples back) — a rail per ingress, RED-PROVED by removing the skip (an echo must overwrite the local value without it);
- ⭐ a rail per node role (CGF, SimHost, Player/IG, + a second Brain): the BUILT node carries the pair (the forwarding-rail control, CLAUDE.md "silent default");
- ⭐ the feature's own suites (T-1): `StanceTranslatorTests`, `StanceWireTests` (18), and the INTEGRATION suite `Hrot.Animation.Network.Integration.Tests` (`NetworkedAnimationScenarios`, 8 Brain ⇄ Muscle loopback cases) — rule 8 row;
- ⭐ live, ONCE: `--mode all` `ua-posture` — a stance from either station reaches every node (the row's own check), + `bt-grenade-posture` (a stance under a scenario's own checks) — §6a: two scenarios, not forty; the wide set once at the end, in the background;
- ⭐ build it on `ui-wip-ce1147` and merge into `ui` only when all of the above are green.

**The question:** the accepted fix puts the stance egress + ingress pair into `SharedTranslatorPack` (every node). ⚠ Measured: that pack is in
`Hrot.Network.NED`, which references neither `Hrot.MuscleCharacter.Animation` (where `StanceIntent` / `StanceStatus` live) nor
`Hrot.Animation.Replication` (the stance translators) — today only SimHost and Player reference the replication assembly, and CGF reaches it through
`CgfNodeBootstrapper.cs:326`. A new dependency edge between the network and animation assemblies (or a seam) decides the shape.

| option | what it means | cost |
|---|---|---|
| **A (lean)** — `Hrot.Network.NED` references `Hrot.Animation.Replication` (+ `Hrot.MuscleCharacter.Animation`); `SharedTranslatorPack.Create` yields the stance pair (ingress skips the recorded owner of 104 / 105, like `EntityPossessedByIngressTranslator`); the components move into the shared component registry; `RegisterStanceOn` (CGF, SimHost), `RegisterStanceViewerOn` (IG) and the role-split `StanceTranslators` are deleted | ⭐ by construction on every node — the pattern every other one-owner datum uses; no cycle (measured: neither animation assembly references NED) | NED gains two project references; touches backend's pack and three bootstrappers (cross-lane); the look-at pair (`LookAtTranslators`, which rides beside stance in `RegisterStanceOn`) must stay or move with it |
| B — a contribution seam: `NedReplicationModule` takes extra "every node" translators from the host's node builder, and the shared node frame passes the stance pair | no new reference from the network assembly | one more seam; "every node" depends on every builder path passing it — the host-wired shape the row exists to remove |

**Claim table (lean A):** NED cannot see the stance types — ✅ `Hrot.Network.NED.csproj` references (Core, Common, Orchestration, Fdp.*) · no cycle — ✅ neither
animation csproj references NED · the pattern — ✅ `SharedTranslatorPack.cs` (owner egress / skip-own ingress pairs) · the design names the pack — ✅ the row,
`DESIGN_Decision_Layer.md` §3.3g (to be superseded) · the look-at pair's home — ⛔ not decided anywhere (searched the row and §3.3g).

**State:** SKIPPED — needs decision. Nothing built.

## CE-1286 (taken while the queue ran dry) — how does a C# behaviour's param / working-state field get into the Watch? *(asked 2026-11-18)*

**DECIDED `2026-11-18` (user, via coordinator): A — a new Watch row section `Occurrence` addressed by (behaviour hash, slot kind, field path), read through `OccurrenceSlotCatalog`, written through R-1041's one live-write path; the dump's row menu gains Add to Watch.**

**Where it stands:** the display half is DONE (CE-1315: the Brain tab and the inspector dump every running behaviour's params and variables, C#
ones included, through `OccurrenceSlotCatalog`). Left: the row menu *Add to Watch*. ⚠ The Watch's pinned row is an asset-variable identity
`(AssetId, Entity, Section, VariablePath)` (`AiWatchWindow`, `PinnedVariableRowSource`) — a C# behaviour has no asset id and its fields no section.

| option | what it means | cost |
|---|---|---|
| **A (lean)** — a new row SECTION `Occurrence` addressed by `(behaviour hash, slot kind task/SOP/hosted, field path)`, read through `OccurrenceSlotCatalog` (the one reader, R-1037) and written through R-1041's one live-write path | the Watch shows and edits a C# behaviour's fields like an asset variable; the dump's row menu gains *Add to Watch* | a new `VariableRow` kind + its persistence entry (DebugSessionPersistence) + rails; touches the Watch's identity, so it should be agreed first |
| B — *Add to Watch* only for asset-defined behaviours (the existing identity) | no new identity | the user's ask was precisely the C# ones — leaves them out |

**Claim table (lean A):** the display exists — ✅ `CE-1315` (`OccurrenceSlotView.Boxed`); the Watch identity is asset-only — ✅ `AiWatchWindow.cs` header
(`(AssetId, Entity, Section, VariablePath)`); a design rules the C# watch identity — ⛔ searched `docs/DESIGN_Entity_Brain_View.md` §12b (BV-42…45: *"edits through
Add to Watch / the Watch window"*, no identity named) and `docs/DESIGN_Behaviour_Debugging.md` DBG-5, none found.

**State:** SKIPPED — needs decision. Nothing built.

## CE-1067 — should a REMOTE terminal's panel reach another node's GLOBAL gizmo? *(asked 2026-11-18)*

**DECIDED `2026-11-18` (user, via coordinator): A — not a defect today; close the row (reopen when a feature needs a terminal to drive another node's global gizmo).**

**The question:** `GizmoInteractionIngressTranslator` drops a remote StructUpdate / InspectorClosed whose anchor is not an entity in its world. A
global gizmo (the layer control, picker tools — `GlobalGizmoManager.NewId()`, 8 call sites: `MapInteractionPack.cs:337/382/394`, `PickerToolHost.cs:240/354`,
`AreaAuthoringArm.cs:116`, `ScenarioToolRegistrations.cs:139`) has a per-run, per-node id, so a remote terminal cannot name it at all — fixing the drop
alone fixes nothing.

| option | what it means | cost |
|---|---|---|
| **A (lean)** — NOT A DEFECT TODAY: a global gizmo is a host's OWN UI (each map host draws and edits its own layer control; §5e made the layers per view, remembered locally) — close the row; reopen when a feature needs a terminal to drive ANOTHER node's global gizmo | nothing changes; the row stops sitting in the bug queue | none; the drop stays correct for picks and drags (behaviour ②) |
| B — a stable global-gizmo ADDRESS (a registered name per kind, e.g. `layer-control`, resolved per node) and the ingress admits StructUpdate / InspectorClosed for a registered name | a new addressing scheme in the gizmo wire + 8 registrations named | a design (DESIGN_Uniform_Gizmo_Membership) + the wire change; no current flow uses it |

**Claim table (lean A):** a local panel is unaffected — ✅ the row's own measurement (`:120` drops only remote batches); a shipped flow edits a REMOTE global gizmo —
⛔ searched `docs/DESIGN_Terrain_Combat_Tuning.md` §5d/§5e and `docs/DESIGN_Uniform_Gizmo_Membership.md`, none found; ⚠ graph not queried for this one (grep only).

**State:** SKIPPED — needs decision. Nothing built.

## CE-1954 — does a PRESS-AND-DRAG on an UNSELECTED unit move it? *(asked 2026-11-18)*

**DECIDED `2026-11-18` (user, via coordinator): A — a press on an unselected unit selects it and the same drag moves it, in BOTH the threaded and the serial editor.**

**The question:** the threaded editor moves it, the serial one does not (it only selects); the two must agree (R-4224). Which rule?

| option | what it means | cost |
|---|---|---|
| **A (lean)** — press on an unselected unit SELECTS it and the same drag MOVES it, in both modes | the common editor convention (a click-drag grabs what is under the cursor); `GroupTransformTests.P5_ADragOnAnUnselectedUnit_MovesOnlyThatUnit` already asserts the gizmo-level half | the serial host must emit a drag box for EVERY unit under the cursor (not only selected ones — `DataDrivenGizmoSystem.IsSelectedPredicate`), or select-then-drag on the press; a frame rail on both hosts |
| B — the press only SELECTS; a drag needs the unit selected first | what serial does today | the threaded host must stop answering a press with the previous frame's pick boxes; P5's unselected-drag rail is then wrong and changes |

**Claim table (lean A):** threaded drags it — ✅ measured live (the row, `tests/hill-attack-close`); serial selects only — ✅ the drag box is emitted for selected units only (`DataDrivenGizmoSystem` `IsSelectedPredicate`); the design rules it — ⛔ searched `docs/UX/DESIGN_Map_Interaction.md` (§2.5 table, P5 as-built), no rule for an unselected unit's drag.

**State:** SKIPPED — needs decision. Nothing built.
