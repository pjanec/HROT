<!--STATUS
state: LIVE
updated: 2026-08-21
current-answer: this whole file — the coordinator's review of Batch 103.
stale-below: nothing.
known-rot: none.
known-conflict: none.
-->
# ⭐⭐⭐ REVIEW — Batch 103 · **merged, with one defect fixed here and two left open**

> ⭐ **Merged at `bd81749e3`.** ⛔ **Gates NOT re-run** *(rule 8)* — the report's table is complete and
> its four contract rows are all present. ⭐ **What I did instead: read the diff, and spot-verified two
> claims.** ⚠ **One of them was false in a way the whole batch turned on.**

| | |
|---|---|
| ⭐⭐ **Verdict** | ✅ **Accept.** The design was followed, the seam-reuse was real *(no `WindowManager` change — the unused path parameter was the answer)*, and **`103b` found three real orphans and fixed the RAIL rather than the assertion** |
| ⭐⭐⭐ **The best thing in it** | 📌 **the ORDER finding** — the reset must precede `SetupImGui` or it silently applies one run late. ⛔ That would have shipped as *"the reset doesn't work"* and cost a session |
| ⛔ **The one defect** | ⭐ **fixed here** *(`012238550`)* — §1 |

---

## 1. ⛔⛔ THE DEFECT — **a destructive default with NO working way off**

⭐ `103a` ships `--reset-layout` defaulting **ON**, force-overwriting the user's arrangement every run.
🔒 **The default is the user's own ruling and is not in question.** ⛔ **The opt-out is.**

### 📐 Measured against `CommandLineParser 2.9.1` — **both documented spellings failed, differently**

| spelling | where it was documented | 📐 what it actually does |
|---|---|---|
| `--no-reset-layout` | ⛔⛔ **the STARTUP LOG** — the one line the user reads | **`UnknownOptionError`** ⇒ ⛔ **the runner refuses to start.** `CommandLineParser` has no negation form |
| `--reset-layout=false` | the `HelpText` | ⛔⛔ **parses cleanly and the value stays `true`.** ⚠ **Silent** — a plain `bool` `[Option]` is a **switch**, so its `=false` is discarded |

⇒ ⭐⭐⭐ **There was no way at all to keep your own layout**, and the user's stated reason for shipping
theirs was *"i would not like to lose it."*

### ⭐ The fix — **the TYPE, not the string**

`bool?` makes the option take a **value**. ⭐ `--reset-layout=false` and `--reset-layout false` both land;
the default stays `true`; `--reset-layout=true` still works.

📌 **Railed** — `TheLayoutResetCanActuallyBeTurnedOffTests`, **6 tests through the real
`HrotRunnerConfiguration` and a real parser**, including one pinning the **absence** of the negated
spelling in production source. ⛔ **A claim about a third-party parser belongs in a test, not a comment**
— ⚠ this one was in three comments and wrong in all three.

### ⚠ Where it came from — **not carelessness**

📄 `UX_Feature_Layout_Defaults.md:120` lists *"`--reset-layout` / `--no-reset-layout`"*. ⭐ **The design
named two spellings and the implementation built one** — ⛔ and nothing checked that either worked,
because a CLI flag has no rail by default.

### ⭐⭐ The lesson worth keeping *(proposed for the ledger — user's call)*

> ⛔ **A DESTRUCTIVE default needs a rail on its OPT-OUT, not on its behaviour.**
> ⭐ The reset itself was railed three ways. ⚠ **The way to switch it off was railed zero ways**, and
> that is the half that decides whether the default is *deliberate* or *unconditional*.

---

## 2. ⚠⚠ OPEN — **`File ▸ Layout ▸ Reset to default on start` is a control that does not act**

📌 The checkable menu item sets `_options.ResetLayoutOnRun = v` and logs *"it applies from the next
run."* 📐 **Measured: nothing persists it.** `RunnerOptions` is constructed fresh in `Program.cs` from
the parsed CLI on every start ⇒ ⛔ **the toggle never survives the run it promises to outlive.**

⚠ **Its own doc comment names the trap it fell into** — *"pretending otherwise would be a control that
lies about when it acts."* ⭐ The intent was right; the persistence was never built.

### ⛔ Why this is NOT a drive-by fix — **it is circular, and that is a design question**

⭐ The obvious home for the flag is `fdp_windows.json`. ⛔⛔ **That file is the thing the reset
overwrites** ⇒ storing the opt-out inside the pair being reset means **the reset erases its own off
switch** on the first run it is turned off.

| ⭐ my recommended answer *(for approval, not built)* | |
|---|---|
| ⭐⭐ **Persist it OUTSIDE the layout pair** — a small `layout-mode.json` in the same user directory, ⛔ **not inside either layout file** | ⭐ it is *about* the layout, not *part of* it — the same distinction that made `LayoutPaths` a directory rather than two special-cased files |
| ⭐ **CLI beats the persisted value for that run**, and says so in the log | ⛔ otherwise a script cannot force either mode |
| ⚠ **Until then, correct the label** to *"Reset to default on start (this run's setting only — use `--reset-layout=false`)"* | ⛔ a lying control is worse than an absent one |

---

## 3. ⚠ OPEN — **three rails can pass VACUOUSLY and say nothing**

📌 `TheDefaultLayoutIsNotStaleTests` opens each of its three tests with:

```csharp
if (ShippedLayoutDirectory() is null) return;      // ⛔ a SILENT pass
```

⇒ ⛔ if `RepoRoot()` ever stops resolving — a different output depth, a packaged run, `layout/default`
renamed — **all three go green while checking nothing**, and 📌 **the gate-report contract's row 6
(*"a new skip is a finding"*) cannot see it**: a silent `return` is not a skip.

⚠ **The same shape is in my own new rail** (`NoProductionSourceTellsTheUserToUseTheNegatedSpelling`) —
⭐ stated rather than excused.

| ⭐ the fix — cheap, either way | |
|---|---|
| ⭐⭐ **`SkippableFact` + `Skip.If`** *(already referenced by `Hrot.Editor.AiShared.Tests`)* ⇒ the skip shows in the counts | ⭐ preferred — it makes the invisible visible **for free** |
| ⭐ **or one `[Fact]` asserting `ShippedLayoutDirectory() != null`** | ⛔ blunter, but it cannot rot silently |

---

## 4. ⭐ WHAT I SPOT-VERIFIED, AND WHAT I DID NOT

| ⭐ claim | verdict |
|---|---|
| ⭐⭐ *"`--reset-layout=false` keeps your arrangement"* | ⛔⛔ **FALSE** — §1. **Ran the real parser** rather than reading it |
| ⭐⭐ **the output flattens `layout/default/` → `layout/`, so `TryFindSourceLayoutDirectory` cannot mistake the OUTPUT dir for the source tree** | ✅ **TRUE, and deliberate.** ⚠ I went looking for this as a hazard *(`Save current as default` writing into `bin/`)* and the `Link=` in the csproj already prevents it |
| ⭐ **no file overlap between their 13 and my in-flight commits** | ✅ **TRUE** — the merge was clean, and `git diff <their-head> HEAD` is **entirely my own files** |
| ⛔ **the 20-row gate table** | **NOT re-run** *(rule 8)*. ⭐ Their `D003_*` worktree proof at the base sha is exactly the contract row 4 that replaces my re-running it — ⚠ **and I reproduced the same two reds incidentally** while running my own T1 |

---

## 5. ⛔ ONE PROCESS INCIDENT — **`git pull --rebase` flattened the merge**

📌 After merging Batch 103, a routine `git pull --rebase` **replayed their three commits as new shas**
⇒ `origin/claude/hrot-implementation-j1jvin` **stopped being an ancestor** of the coordinator branch.
⛔ **Their rule-7 `git merge --ff-only <coordinator>` would have failed** for a reason with nothing to do
with the code.

⭐ **Repaired at `bd81749e3`** with `git merge -s ours <their-head>` — ⚠ **safe only because the tree was
already a strict superset**, which was *verified*, not assumed.

⇒ ⭐⭐ **Rule for the coordinator: after merging the implementation branch, use `git pull` (merge) or
`--rebase-merges`, never a plain `--rebase`.**
