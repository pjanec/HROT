# Visual Asset Comparison — User Guide

This guide explains how to use the **Visual Asset Comparison** feature to review, audit, verify, and investigate changes to behaviour-trees (BTrees), hierarchical state machines (HSMs), Blueprints, and Blackboards.

---

## Use Case 1: PR Review

**Goal:** Understand what an AI-generated edit actually changed before you merge.

**Scenario:** A teammate's PR contains an AI-authored BTree change. You want to see a human-readable summary of every behaviour difference before approving.

**Workflow:**
1. Checkout the PR branch and the base branch locally (or have both `.cs` / `.bp.json` files accessible).
2. In the editor, open **Asset Comparison** (menu: `AI › Compare Asset Versions`).
3. Set **Version A (OLD)** to the base-branch file and **Version B (NEW)** to the PR-branch file.
4. Click **Export Comparison**. Copy the export text to the clipboard (the button is disabled if the export exceeds 8 MB — use **Save to File** in that case).
5. Paste into your LLM using the prompt snippet below.
6. Click **Paste LLM Response** in the editor and paste the model's JSON reply.
7. Review the structured change list. Changes with severity `behavior` or `intent_shift` deserve the most attention.

**Example LLM prompt:**
```
You are reviewing a BTree asset change. Below is the comparison export.
For each change, identify the element, what changed, and assess the severity.
Return your answer as the JSON structure described in the STRUCTURED CHANGES section.

[paste export here]
```

---

## Use Case 2: AI-Agent Edit Audit

**Goal:** Verify what an AI agent changed in an asset after it ran an autonomous edit session.

**Scenario:** An AI coding agent modified a BTree or HSM as part of a larger task. You want to confirm it only changed what it was supposed to.

**Workflow:**
1. Before triggering the agent, save the current asset file somewhere (e.g., copy it to a temp folder or commit it to git).
2. Let the agent run. After it finishes, open **Asset Comparison**.
3. Set **Version A (OLD)** to the pre-agent file, **Version B (NEW)** to the post-agent file.
4. Export and send to the LLM. Ask it to flag any change outside the declared scope.
5. Paste the response back; scan for `behavior` or `intent_shift` changes outside the scope.

**Example LLM prompt:**
```
The agent was instructed to add a patrol sub-tree. Review the comparison export
and flag any changes that are unrelated to patrol behavior.

[paste export here]
```

---

## Use Case 3: Refactor Verification

**Goal:** Confirm that a manual refactor preserved the asset's intended behaviour.

**Scenario:** You restructured a BTree to reduce duplication. You want proof that no logic changed.

**Workflow:**
1. Commit or snapshot the asset before the refactor.
2. Perform the refactor and save.
3. Open **Asset Comparison**, set A = pre-refactor, B = post-refactor.
4. Export and send to the LLM with the prompt below.
5. A clean refactor should show only `cosmetic` or `structural` severity changes. Any `behavior` change is a regression candidate.

**Example LLM prompt:**
```
The author claims this is a pure refactor with no behaviour change.
Analyse the comparison export and list any changes with severity 'behavior'
or 'intent_shift'. Return the structured JSON response.

[paste export here]
```

---

## Use Case 4: Regression Hunt

**Goal:** Find which asset change introduced a behavioural regression between two git revisions.

**Scenario:** A unit test or playtest started failing after a merge. You suspect a BTree or HSM change is responsible.

**Workflow:**
1. `git show <good-commit>:path/to/Asset_BT.cs > /tmp/asset_old.cs`
2. `git show <bad-commit>:path/to/Asset_BT.cs > /tmp/asset_new.cs`
3. Open **Asset Comparison**, set A = `asset_old.cs`, B = `asset_new.cs`.
4. Export and send to the LLM. Ask it to focus on `removal` and `behavior` changes.
5. Use the structured response to identify the offending change and its element ID.

**Example LLM prompt:**
```
A regression was introduced between two commits. Analyse the comparison export
and identify which changes most likely caused a behavioral regression.
Focus on changes with severity 'removal' or 'behavior'.

[paste export here]
```

---

## Export → LLM → Paste Workflow

This section describes the generic end-to-end steps for any comparison.

1. **Open the dialog** — `AI › Compare Asset Versions`.
2. **Select Version A (OLD)** — the earlier or reference file.
3. **Select Version B (NEW)** — the later or candidate file.
   - Use **Reverse A↔B** if you selected them in the wrong order.
4. **Validate** — the dialog shows warnings (e.g., different AssetIds). Errors block export.
5. **Export** — click **Export Comparison**:
   - If the export fits in the clipboard (≤ 8 MB), copy it directly.
   - If it is too large, click **Save to File** and attach the file to your LLM session.
6. **LLM session** — open your preferred LLM (Claude, GPT-4o, Gemini, etc.), paste the export, and include your review instructions.
7. **Paste response** — in the editor, click **Paste LLM Response**, paste the model's reply, and click **Apply**.
8. **Review changes** — the change list appears sorted by severity. Navigate from `intent_shift` → `behavior` → `structural` → `cosmetic`.
9. **Re-run if needed** — if the model truncated its response or returned no changes, re-run with a more capable model or with the **Save to File** export.

---

## Severity Reference

| Severity | Meaning | Typical action |
|---|---|---|
| `intent_shift` | The overall goal or strategy of the asset changed | Requires explicit approval; may indicate a scope violation |
| `behavior` | Observable runtime behaviour changed (transitions, actions, conditions) | Review carefully; regression risk |
| `structural` | Organisation changed without altering behaviour (node order, grouping) | Low risk; verify no accidental side effects |
| `cosmetic` | Names, comments, or formatting changed only | Safe to accept without review |
| `removal` | A node, state, or connection was deleted | Always flag; intentional removals must be confirmed |
