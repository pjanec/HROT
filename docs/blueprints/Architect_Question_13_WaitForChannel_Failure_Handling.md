# Architect question #13 — `WaitForChannel` success/failure handling (exec split + status data-out)

**Status: ✅ APPROVED (architect, 2026-07-19).** All four points confirmed:
- **Q13-A APPROVED** — implement both the `OnSuccess`/`OnFailure` exec split **and** the `Status` data-out.
- **Q13-B APPROVED** — lowering table accepted; `unwired-OnFailure = auto-Failure` is non-negotiable (keeps the
  proofs byte-identical). **Open question resolved: an unterminated `OnFailure` chain is a COMPILE ERROR** —
  do NOT default to `Failure`. Extend the Stage 2 validator to require every new exec path terminate in a
  `Return` node.
- **Q13-C APPROVED** — `Status` = existing `NodeStatus`, readable on both paths; richer channel-specific
  payload deferred (anti-speculative).
- **Q13-D APPROVED** — `WaitForChannel` only this slice; `WaitForEvent` is a blessed fast-follow once this
  ships and the `HillAssault2_ReverseToBaseline` deviation is removed.

Cleared to implement. The original proposal follows.

## The goal

`WaitForChannel` (latent: suspend until a channel command completes) currently has **exec-in + exec-out
only**. On channel **Failure** the AiPrimitive lowering **auto-returns `Failure` for the whole action and the
`Out` chain never runs**. A blueprint therefore **cannot react to a failed command** — no cleanup, no
publish-on-both-paths, no fallback, no retry.

This is not hypothetical. The `HillAssault2_ReverseToBaseline` design doc records a **documented deviation**
caused by exactly this: the C# oracle publishes `ClearBehaviorEvent` on **both** Success and Failure, but the
blueprint can only publish on Success because the failure path auto-aborts before any node runs.

The user's ask (confirmed): **a Success/Failure exec split** *and* **a status data-out** (the latter called
"a must"). This doc decides the exact shape and — critically — the latent-lowering semantics, because that is
the load-bearing part.

## Current state (verified in code)

| Aspect | Today |
|---|---|
| Node fields | `WaitForChannelNode { string ChannelType }` |
| Static pin schema | `ExecIn(), ExecOut()` — one exec-out, no data-out |
| Failure behavior | AiPrimitive lowering: channel `Running`→stay Running; `Success`→continue to `Out`; `Failure`→**auto-return `Failure`, skip `Out`** |
| In-graph failure handling | **impossible** |
| `NodeStatus` enum | already exists: `{ Success, Failure, Running }` |
| Reference topology that already exists | `FlowForEachNode` has named exec-outs (`Body`/`Completed`); `BranchNode` has `True`/`False` |

## Sub-questions

### Q13-A — Exec topology
- **A1** — Two named exec-outs: `OnSuccess` + `OnFailure`. Wire each path directly (UE "OnSucceeded/OnFailed").
- **A2** — Keep the single `Out`, add a `Status` (NodeStatus) data-out; designer forks with a `Compare`+`Branch`.
- **A3** — **Both**: `OnSuccess`/`OnFailure` exec-outs **and** a `Status` data-out.

**Claude's lean: A3.** The user asked for both explicitly, and they serve different jobs: the exec split is
the ergonomic 90% case (matches the proven UE latent-node idiom); the `Status` data-out lets a graph *store /
log / pass on* the outcome (e.g. into a variable or a published event's payload) without re-deriving it.
**Reuse vs build:** exec-outs reuse the existing named-exec-out scheduling (already load-bearing for
`FlowForEach`/`Branch` in Stage5); `Status` reuses the data-pin machinery + the existing `NodeStatus` enum.
No new vocabulary — only a new pin set on one node.

### Q13-B — Latent lowering semantics (the load-bearing decision)
Adding `OnFailure` changes what the primitive does when the channel fails. Proposal:

| Channel result | Today | Proposed |
|---|---|---|
| `Running` | stay Running | unchanged |
| `Success` | run `Out` | run `OnSuccess` |
| `Failure`, **`OnFailure` unwired** | auto-return `Failure` | **auto-return `Failure`** (byte-identical to today) |
| `Failure`, **`OnFailure` wired** | — | **run the `OnFailure` chain**; primitive's final status = whatever that chain's `Return` yields; default `Failure` if it reaches the end with no explicit `Return` |

The **unwired-`OnFailure` = today's auto-`Failure`** rule is deliberate: it keeps every existing blueprint
(and the 52 isolated proofs + the integration proofs) **byte-identical**, since none of them wire `OnFailure`.
Only graphs that opt in by wiring the pin change behavior.

**Claude's lean: adopt the table above.** **Reuse vs build:** reuses the existing latent suspend/resume +
exec scheduling; the only new logic is a conditional at the resume point that dispatches to one of two exec
successors based on the channel result the runtime already has. **Question for the architect:** is
"default `Failure` when the `OnFailure` chain falls off the end without a `Return`" the right default, or
should an unterminated `OnFailure` chain be a **compile error** (force an explicit `Return`)?

### Q13-C — `Status` data-out type & validity
- Type `NodeStatus` (`Success`/`Failure`); `Running` is never emitted (the pin is only read post-completion).
- Readable on both exec branches (so a shared downstream node can log it).

**Claude's lean: `NodeStatus`, valid on both paths.** **Reuse vs build:** enum already exists; pin is a
plain data-out. Open sub-point: do we also want a richer channel-specific *result payload* (e.g. arrival
distance, failure reason) later? **Out of scope now** — flag as a future round-out, don't build speculatively.

### Q13-D — Consistency across the other latent nodes
The other latent nodes are `WaitForEventNode` and `LatentDelayNode`.
- `WaitForEvent` has a comparable failure/timeout mode → same treatment plausibly applies.
- `LatentDelay` cannot fail → single `Out` only, no change.

**Claude's lean: scope THIS change to `WaitForChannel`** (the node with the real failure mode the user hit),
and treat `WaitForEvent` as a fast-follow round-out **once the WaitForChannel pattern is blessed** — not in
the same slice, to keep the semantic change reviewable. **Reuse vs build:** once built for WaitForChannel the
machinery is identical for WaitForEvent; `LatentDelay` is untouched. Confirm you agree with deferring
WaitForEvent rather than doing all latent nodes at once.

## Backward-compatibility guarantee (non-negotiable)
- Unwired `OnFailure` ⇒ today's auto-`Failure`. Existing assets and all proofs stay byte-identical.
- Migration opportunity (not required): once shipped, `HillAssault2_ReverseToBaseline` can wire `OnFailure`
  → publish `ClearBehaviorEvent` there too, **removing the documented deviation** and matching the oracle.

## What we're asking you to bless
1. **Q13-A:** shape = A3 (both exec split + `Status` data-out)?
2. **Q13-B:** the lowering table, incl. the unwired-`OnFailure`=auto-`Failure` compat rule — and the
   fall-off-the-end default (`Failure` vs compile error)?
3. **Q13-C:** `Status` = `NodeStatus`, both paths; richer payload deferred?
4. **Q13-D:** WaitForChannel-only now, WaitForEvent as a blessed fast-follow?
