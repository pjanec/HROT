<!--STATUS
state: LIVE
build-state: DISPATCH
updated: 2026-08-24
current-answer: dispatch pointer for HN-028 — expose the Orchestrator's MasterSyncController so the
  conformance harness's ack-gate can confirm a cluster-wide step. TIME lane, one accessor. The host wiring +
  the rail flip are the coordinator's integration step, so this batch touches ONLY a TIME-lane file and is
  fully independent of the HN-029 load work.
known-conflict: none. Independent of the harness/editor lane (HN-029) and of anything in flight.
-->
# HANDOFF — **HN-028: expose the master so the ack-gate confirms cluster-wide** *(TIME lane)*

> 📌 **Dispatched at `3d5743a84`.** ⛔ **Scope FROZEN at that sha.** ⭐ Branch fresh from
> **`claude/blueprint-authoring-status-6sr5ld`** *(rule 7)*; **rule 1b: push the started-marker BEFORE any code.**
> ⛔ **No PR.** ⭐ ids **`TM-`**, tracker **Area H** *(the TIME lane's partition)* — you allocate the id (rule 3),
> state it (rule 5). ⚠ **The `HN-028` label is the harness lane's tracking id for the GAP**; the CODE change is
> a **`TM-`** row in Area H.

## 0. WHY — the one-line gap, measured

📐 The conformance harness ships an **ack-gated** `POST /sim/step` *(it returns only when the master reports the
tick acknowledged cluster-wide)*. It works in the editor. In `--mode all` the truth —
`MasterSyncController.IsAwaitingStepAcks` — lives on the controller **instance**, and the only production
instance is a **private field of `OrchestratorSubsystem`** *(`_masterSync`, `OrchestratorSubsystem.cs:61`)* — a
**TIME-lane file** the conformance handoff was forbidden to touch. ⇒ the dispatcher was built with `master:
null`, `GET /capabilities` reports **`hasMaster:false`**, and a conformance rail **asserts** it does *(so the gap
is visible, not silent)*.

📄 **Design basis:** [`Architect_Question_54`](../Architect_Question_54_Cluster_Mcp_Contract.md) § AS-BUILT
*(deviation ②)* · [`DESIGN_Headless_Testability.md`](../../DESIGN_Headless_Testability.md) §6c/§6e ·
tracker row **`HN-028`**.

## 1. ⭐⭐⭐ THE ITEM — one accessor, mirroring the pattern already there

⭐ Expose the controller read-only, **mirroring the existing `TestHook_` accessors** in the same file
*(`TestHook_CurrentSimTime` `:78`, `TestHook_TimeScale` `:85`)*:

```csharp
// OrchestratorSubsystem.cs — beside the other TestHook_ accessors
internal MasterSyncController? TestHook_MasterSync => _masterSync;
```

⚠ **Name/visibility is yours to fit the lane's conventions** — a read-only property or a narrower surface
*(e.g. `internal bool IsAwaitingStepAcks => _masterSync?.IsAwaitingStepAcks ?? false;`)* is equally fine, and a
narrower one is arguably cleaner *(it exposes the ONE fact the gate needs, not the whole controller)*. ⭐ **Pick
the narrowest thing that lets a caller answer *"is the cluster still awaiting step ACKs?"*** State which you chose.

⭐ **If `internal` needs a consumer visibility grant**, add the `InternalsVisibleTo` the host assembly needs —
that is a TIME-lane-owned csproj/assembly-info edit and in scope.

## 2. ⛔ LANE & SCOPE — **this is why it is independently dispatchable**

⭐ **Yours (TIME lane, Area H):** `OrchestratorSubsystem.cs` *(the accessor)* + any `InternalsVisibleTo`/assembly
grant it needs + a small TIME-lane rail if you want one *(e.g. after a paused cluster step, `IsAwaitingStepAcks`
is observable and clears)*.

⛔⛔ **NOT yours — the coordinator's integration step, do NOT touch:**
- ⛔ `Hrot/Runner/Hrot.ClusterRunner/Program.cs` *(harness/host lane — wires the exposed master into the
  `PerspectiveScopedDispatcher`)*
- ⛔ `Hrot.SystemTests/Conformance/ClusterConformanceRails.cs` *(the `hasMaster:false` assertion flips to
  `true` — the coordinator does this on integration, and its reddening is the proof the wiring landed)*
- ⛔ anything in `Fdp.Toolkits/Time` production beyond what the accessor needs.

⇒ ⭐⭐ **Because the consumption is the coordinator's, this batch collides with nothing** — it is a single
read-only accessor on one file. That is the whole point of dispatching it in parallel.

## 3. GATES

⭐ Standing contract *(rule 8)*: verbatim command · pass/fail/skip · **delta vs `3d5743a84`** · a `--no-build`
column · every RED pre-existing **by name** · `tracker-counts.py --check` · `rulings-check.py` ·
`design-digest.py --check` · **the `TM-` id you allocated** *(rule 5, same commit)*.

⭐⭐ **Row 8 — the invariant this must not break:** the accessor is read-only and changes no time behaviour, so
the risk is purely that exposing it does not perturb the barrier/step protocol. Gate the TIME lane's own time
suites — `Fdp.ModuleHost.Tests` + the sync/lockstep rails *(`SimTimeSyncIntegrationTests` /
`TimeControlIntegrationTests` — the suites that prove nodes stay time-synced)* — and report them. ⚠ If the
`ClusterRunner.Integration.Tests` DDS-allocator crash makes a suite un-gateable, that is a reported finding with
the base sha, not a silent skip.

⚠ **Known quirks — not yours:** `tracker-counts.py` blind to `HN-`/`MX-` rows · `Fdp.Presentation.Tests`
~18–20 pre-existing *(`BP-419`)* · known `rulings-check` staleness WARNs · `Fdp.Toolkits.Tests` rotating-flaky
*(`DEBT-AIB-030`)* — confirm by `--filter`, do not quote a total.

## 4. ⭐ WHEN YOU ARE DONE

⭐ Report the `TM-` id and the accessor's final shape. ⛔ Do NOT edit the harness rail or `Program.cs` — say in
the report *"the accessor is `TestHook_MasterSync` / `IsAwaitingStepAcks`; coordinator wires it and flips the
`hasMaster` assertion."* ⭐⭐ **On merge the coordinator does the one-line `Program.cs` wiring and flips
`ClusterConformanceRails`' `hasMaster:false` → `true`; that rail reddening-then-greening is the proof the gap is
closed.** Fold nothing into a design — this is a mechanical exposure; the design already describes the gate
*(Q54 § AS-BUILT)*.
