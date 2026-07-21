# Q#13 implementation plan — WaitForChannel OnSuccess/OnFailure + Status

Architect-approved (see `Architect_Question_13_*`). This note records the **verified in-code
mechanism** so the build is mechanical. Design is settled; do not re-open it.

## Verified current mechanism

| Concern | Where | Today |
|---|---|---|
| Node | `Assets/Nodes.cs` | `WaitForChannelNode { string ChannelType }` |
| Static pins | `Catalogs/BuiltInNodeRegistry.cs` | `{ ExecIn(), ExecOut() }` — exec-in "In", exec-out "Out" |
| Latent scheduling | `Stages/Stage5_Schedule.cs` → `ScheduleLatentNode` + `BuildWaitForChannelOp` (1450) | resolves the single "Out" exec successor → `IrTerm_Suspend.ResumeBlock`; emits `IrOp_WaitForChannel` |
| Phase state machine | `Lowering/WaitLowering_AiPrimitive.cs` (+ `_Instance.cs`) | builds per-phase `check → ret_running / not_running → failure` blocks. **Failure block (lines 394-402): `WriteWorkingStatePhase(0); return Failure`** |
| Validation | `Stages/Stage2_Validate.cs` | latent-node rules; exec-path-terminates checks |
| Editor pins | `Hrot.Blueprints.Editor/Host/NodePinSchema.cs` | mirrors registry |
| Title | `Host/BlueprintNodeModel.cs:159` | `Wait: {ChannelType}` (done) |

Generated failure path today (from `HillAssault2ReverseToBaseline`):
`phase1_channel_check` → Running? ret Running : `not_running` → Failure? `phase1_failure`(`phase=0; return Failure`) : `wait_resume_0` (the "Out" chain).

## Target

- Pins: `In` (exec-in), **`Out`** (exec-out, success — **keep the name "Out" so existing links stay byte-identical**), **`OnFailure`** (exec-out), **`Status`** (data-out `NodeStatus`).
- Failure block: **if `OnFailure` is wired → `phase=0; goto <OnFailure continuation>`; else → `phase=0; return Failure` (today, unchanged).**
- `Status` data-out: lowers to a re-read of `channel.Status` at point of use (`Self → GetComponentRO(channelFqn) → FieldRead("Status")`), valid on both paths (post-completion it's Success or Failure).

## Coupling insight (why this is one change, not two additive slices)

Adding `OnFailure` as a second exec-out means Stage5 can no longer treat WaitForChannel as
"the node with one exec-out." The success-successor resolution must key on the pin **named "Out"**,
and a new failure-successor resolution keys on **"OnFailure"**. So schema + Stage5 + lowering move
together. `IrTerm_Suspend` (or `IrOp_WaitForChannel`) gains a nullable **`FailureBlock`** carrying the
OnFailure continuation block id; `WaitLowering_AiPrimitive`/`_Instance` consult it in the failure block.

## Steps

1. **Schema** — `BuiltInNodeRegistry`: `{ ExecIn(), ExecOut()/*"Out"*/, ExecOut("OnFailure"), Data("Status","…NodeStatus") }`. Mirror in editor `NodePinSchema`. Stage0 enrichment if needed.
2. **IR** — add nullable `FailureBlock` to `IrTerm_Suspend` (thread the OnFailure continuation).
3. **Stage5** — when scheduling WaitForChannel: resolve success successor by pin **name "Out"** (not single-exec-out); resolve `OnFailure` successor by name; if present, schedule its block and set `Suspend.FailureBlock`. Add a `ResolveNodeOutput` case for the `Status` out-pin → `Self`+`GetComponentRO(ResolveChannelTypeFqn(ChannelType))`+`FieldRead("Status", NodeStatus)`.
4. **Lowering** — `WaitLowering_AiPrimitive` + `_Instance`: in the channel `failure` block, if `FailureBlock` set → `WriteWorkingStatePhase(0)` then `Goto(FailureBlock)`; else unchanged (`return Failure`).
5. **Stage2** — extend the exec-path-termination check so an `OnFailure` chain that falls off the end without a `Return` is a **compile error** (architect Q13-B).
6. **Backward-compat gate** — `OnFailure` unwired ⇒ byte-identical; run `Hrot.AiEditor.Generators.Tests` (clean-rebuild + serial, per [[project-blueprint-generator-stale-cache]]) → must stay 183/183.
7. **New proof fixture** — author a small blueprint that WIRES `OnFailure` (→ e.g. PublishEvent + Return) and reads `Status`; add its `*_ProofTests` + golden. **Verify the generated code COMPILES** (build `Hrot.AI.Behaviors` with `--no-incremental`) — the proof text-compare alone does NOT catch non-compiling codegen.
8. **Editor** — pins render/wire; `WaitForChannel` shows OnFailure/Status. Read-only summary already covers it.
9. **Cleanup (optional, blessed)** — rewire `HillAssault2_ReverseToBaseline` OnFailure → publish `ClearBehaviorEvent`, removing its documented deviation.

## Gotchas
- Keep success pin **name = "Out"** (compat). Display can differ; the pin *name* must not.
- Editor pin projection (`NodePinSchema`) must match the registry exactly or the round-trip drifts.
- Fixture golden must be validated by a real **`--no-incremental` compile**, not just the proof text-compare.
