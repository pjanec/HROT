# BATCH-HS-03 Review — TASK-HS-03 draw transition

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED · **Impl:** Zoo

## Verification (independent — read diff, re-ran suite)
- **HsmAsset resolvers:** `FindStateByOutputPin`/`FindStateByInputPin` scan `AllStates` matching `HiddenOutputPinId`/`HiddenInputPinId`. Correct per the `HsmTransitionLink` convention (From=source.output, To=target.input).
- **ApplyAddLink:** (1) `new HsmLinkValidator(_asset).Validate(cmd.From, cmd.To)`; bail if `Verdict != LinkValidity.Valid` (enforces Final-source / History-target rejection via the real validator — no duplicated rules); (2) resolve source/target, bail if either null; (3) `TransitionNode { VisualId = cmd.AssignedId.Value, Source, Target, Kind = External, EventId = 0 }` → `RegisterTransition`. Matches spec; stable VisualId for undo/redo.
- **No cheating:** touched only HsmCommandSink.cs + HsmAsset.cs (resolvers only) + new test file. No other handlers changed.
- **Tests (6, behavioral):** happy path (Source/Target/VisualId/Kind/EventId + AllTransitions + OutgoingTransitions); FindTransitionByVisualId; **HsmGraphModel.Links projection** (rebuild on Changed); validator reject for Final source and History target (count unchanged); unresolvable-pin no-op both directions. Assert refs/counts/enums, not strings.
- **Re-run (no regenerate flag):** `Hrot.Hsm.Editor.Tests` **408/0** (6 new, 0 pre-existing failures). Build 0 errors.

## Issues
None. (EventId 0 = unbound transition; author sets event/guard/action later — consistent with the "default event/kind" spec.)

## Verdict
APPROVED. Transitions can be drawn state→state, are rejected per HSM rules, and project as links.

## Commit message
```
feat(hsm-editor): draw-transition command sink (BATCH-HS-03 / TASK-HS-03)

ApplyAddLink was a stub. Add HsmAsset.FindStateByOutputPin/FindStateByInputPin
(resolve states from the hidden transition pins) and implement ApplyAddLink:
validate via HsmLinkValidator (rejects Final-source / History-target), resolve
source+target, create a TransitionNode (stable VisualId = cmd.AssignedId, default
External kind, unbound EventId) and RegisterTransition it. Projects into
HsmGraphModel.Links. +6 headless tests (happy path, projection, validator
rejections, unresolvable-pin no-op).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
