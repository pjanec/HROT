<!--STATUS
state: SUPERSEDED (2026-10-29, before backend started — rule 1a) by ui's frame CE-1436, NOTE_Ui_To_Backend_One_Cluster_Ingress_And_Read_Model.md (on `ui`, `fcbacaae8`); was: coordinator → backend: the master's op status (ClusterOpStatus) read back on every ECS node, so a CGF / SimHost / IG host can ask CE-4097's unsaved check; answers NOTE_Ui_To_Backend_Op_Status_On_Nodes (on `ui`, `4a1b03227`)
updated: 2026-10-29
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../Architect_Question_108_Run_Mode_Picker.md — D20 / §4b (the master's check round, CE-4093), §2.8a (ui's measurement of this gap)
-->
# HANDOFF — coordinator → backend: every ECS node hears the master's op status

> ⛔ **SUPERSEDED `2026-10-29`, re-dispatched before you started (rule 1a — `1da7a246a` is not in `backend`).** 🔒 The user, on this finding:
> *"How comes cgf is different from other nodes? We need to share and unify, the features need to be available to all nodes by construction."*
> ⇒ ⭐⭐ **Build ui's frame instead: [CE-1436 — one cluster ingress and one cluster read model on every node](NOTE_Ui_To_Backend_One_Cluster_Ingress_And_Read_Model.md)**
> (merge `ui` for the note). This handoff's adding one reader would patch the split the frame removes.
> ⭐ **What still binds from below:** priority — **NEXT, before CE-4089** (a CGF host loses edits silently today); the verified measurement;
> row 4 (every node now hears every op's completion — ignore other requesters', rail it); the done-when's two-process live check; gate row 8.
> ⭐ **Who builds:** your own model designs it (UML in the owning design — the frame names it; the read model on every node is an architectural
> call), then Sonnet for the mechanical moves (callers onto the one ingress, the follow-fields onto the read model, the per-host rails).
> ⚠ The frame's *cost to watch* is load-bearing: measure the read model's memory and per-frame drain on every node before choosing it over a
> lighter shared model.

**Dispatched at** the commit that adds this line (`coordinator`); scope frozen there. ⭐⭐ **Do it NEXT, before CE-4089** — it is a correctness gap,
not a feature: on a CGF host (whose ▶ is armed Live, D12) an edited scenario goes live **without the *Save and go live* prompt**, so the edits are
silently left behind. ⭐ **Sonnet** — the decoder exists; this wires it into one more translator.

✅ **ui's measurement verified** (coordinator, `origin/backend`): `NodeOpSlaveTranslator` reads `NodeOpCommand`, `ClusterStateTopic`,
`AssetInventoryTopic`, `ReplaySessionTopic` — no `ClusterOpStatus` (`Hrot/Network/Hrot.Network.Orchestration/NodeOpSlaveTranslator.cs:30-39`);
the only production `DdsReader<ClusterOpStatus>` is `OrchestrationObserverTranslator.cs:35` (ExCon). The ask crosses (`ClusterOpEgressTranslator`),
the answer does not come back.

| # | what | where |
|---|---|---|
| 1 | ⭐ ONE ingress for `ClusterOpStatus` → `ClusterOpCompletedEvent`: extract the observer's decode (`OrchestrationObserverTranslator.DecodeResult` and the status → event mapping) into one shared class both translators call — ⛔ not a second decoder | `OrchestrationObserverTranslator.cs:113-136` |
| 2 | `NodeOpSlaveTranslator` gets an optional `DdsReader<ClusterOpStatus>` (built where the others are, `NedSlaveOrchestrationTranslator`), publishing `ClusterOpCompletedEvent` on the node's bus through #1 | `NodeOpSlaveTranslator.cs` |
| 3 | `EcsNodeDebugProvider` passes `unsavedCheck:` (and `scenarioDirty:`) from that node's events — ⭐ with ui: it plans to extract `ClusterUiCache`'s outcome fold into one class; agree which lane owns the fold (lean: backend extracts it in this task, ui consumes) | `Hrot/Engine/Hrot.Presentation/DebugApi/EcsNodeDebugProvider.cs` |
| 4 | ⚠ check what else now arrives on every node: every op's completion, for every requester. Nodes must ignore completions for request ids they did not issue (the fold is keyed by request id — confirm, and rail it) | — |

**Done when:** on a multi-process cluster (orchestrator + CGF, no editor), an edit on the CGF host followed by its ▶ Live shows the *Save and go
live* prompt (a rail through the CGF host's provider, plus the live check with two processes); a completion for another host's request changes
nothing on this one. Post a one-line NOTE to ui. Gate report contract as in the run-modes handoff — ⭐ row 8: name and run the integration suite
that crosses the wire. Fold the as-built into AQ108 §4b.

_coordinator lane, 2026-10-29_
