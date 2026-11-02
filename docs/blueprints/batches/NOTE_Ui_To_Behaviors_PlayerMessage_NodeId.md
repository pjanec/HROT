<!--STATUS
state: LIVE — ui → behaviors: one rail red at the meeting point of two lanes' work
updated: 2026-11-02
current-answer: the whole note
stale-below: nothing
related-designs:
  - ../../DESIGN_Scenario_Rules.md — SR-121 (PlayerMessage, rules S2-f)
-->
# NOTE — ui → behaviors: **`PlayerMessageEvent.OriginNodeId` is an `int`; the S0 NodeId rail wants `NodeId`**

| | |
|---|---|
| the red | `Fdp.Toolkits.Tests` `S0_EveryNodeIdMember_IsANodeId` fails on `PlayerMessageEvent.OriginNodeId` (`FDP/Toolkits/Fdp.Toolkits/ScenarioRules/ExerciseResult.cs:74`). ui proved it red at `769d486d3`, in a clean worktree, with no ui change involved |
| how it happened | rules S2 ⑧ (`d17905bce`) added the field as `int`; backend's S0 item 7d (CE-4200 SV-3, `332ae3333`, *"a cluster node id is a type, one width"*) added the rail. Each lane was right about its own half |
| ⭐ lean | `public NodeId OriginNodeId;` with `0` = this node, kept. Touches the `Message` verb (`ScenarioVerbs.cs:189`, `OriginNodeId = 0`) and the PlayerMessage translator's ingress / egress (its wire twin can stay an `int` — the rail is about the internal type) |
| why yours | the type and its translator are rules S2's; ui will DRAW the subtitle + log (SR-106) and reads the field only to tell local from remote |

_ui lane, 2026-11-02_
