<!--STATUS
state: LIVE
updated: 2026-11-04
current-answer: the three reads below
stale-below: nothing
related-designs:
  - docs/DESIGN_Remote_Edit_Outcome.md — OWNS the outcome (§5 as built)
  - docs/DESIGN_Rules_View_UI.md — §7 RE-7: the editor shows a refused live edit under the field it names
-->
# NOTE — behaviors → ui: the editor's Apply can now read the verdict of a rule edit *(CE-2380 part 2)*

**What you get.** A rule edit or control sent from the editor gets a **request id**; the owner answers **Pending** at once, then the judge answers **Applied** or
**Refused with the WHOLE reason** (the status `Reason` is still cut to 55 characters — that is why you read the outcome, not the status).

| | |
|---|---|
| ① the id | `RuleCommands.Edit(world, entity, json, origin, ref seq, out int seq, out Guid requestId)` / `RuleCommands.Control(…, out int seq, out Guid requestId)`. The old overloads still compile and drop the id |
| ② read it in the editor's own world | `EditOutcomeLog.Of(world).TryGet(requestId, out EditOutcome o)` — `o.Status` (`Pending` / `Applied` / `Refused`), `o.Reason`, `o.RespondingNode`, `o.At`. It returns the LATEST outcome of the request (Pending, then the verdict). A local (owned) edit is Pending the moment it is written; for a remote one `TryGet` returns false until the owner's Pending arrives over the wire. The same outcome is raised as the bus event `EditOutcome` |
| ③ over HTTP | `POST /objectives/{id}/control`, `/edit` and `POST /entities/{id}/attribute` answer `requestId`; `GET /edits/{requestId}` → `{status, reason, respondingNode, at}`, 404 when nothing is known yet |

**Showing it (RE-7).** Poll ② each frame while the edit is Pending; on `Refused` put `o.Reason` under the field the edit named; on `Applied` clear it. A request that
never leaves `None` for a few seconds went nowhere (the owner is away) — say that, do not wait forever.

**Limits.** The log holds the last 64 outcomes per node. A newer edit written before the owner judged an older one leaves the older one Pending forever. Only the latest
unjudged *control* request per rule is remembered.
