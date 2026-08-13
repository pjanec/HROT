# Preserved work — Batch 38's original scope (now Batch 39 §1/§2)

> ⚠⚠ **These two patches are the ONLY surviving copy of this work.** It was built, tested and
> gate-green on `claude/hrot-implementation-j1jvin` at `bec149d`, then reset off the branch when
> Batch 38 was replaced by the [design review](../REVIEW_Unified_Variable_Design.md).
>
> ⛔ **The commits themselves are NOT on the remote.** The force-push left them unreferenced, the
> remote will not serve them by sha, and a preservation **tag was refused with HTTP 403** — this
> session's credentials push branches, not tags. They existed only in one ephemeral container's
> object store, which is why they are checked in as patches instead.

---

## What they are

| file | commit | |
|---|---|---|
| `0001-…-a-local-survives-a-suspension.patch` | `2c1638b` | **BP-57 / Q27-A3** — a suspending graph's locals become graph-scoped blackboard slots reset in the **entry block**, so a value written before a `Delay` survives the resume. Also **`MacroLatency.IsLatent`'s missing `ChannelCommandNode`-with-`ActionFqn` arm** |
| `0002-…-refuse-a-Get-SetVariable-that-targets-nothing.patch` | `bec149d` | **`BP1670`** — an unresolvable `Get`/`SetVariable` is refused by name instead of emitting `s.__var_-1`; `VarFieldName` throws on a negative index as the assertion that the rail is complete. Plus the misplaced `BP-220` doc comment |

Both are **compiler-only** and touch **none** of the surfaces the design review found to be in flux.

## Applying them

They were authored on top of `61e1b5a` (the merge of coordinator `15f4466`). Against a newer base,
expect conflicts only where Batch 39 has since moved the same files.

```bash
git am docs/blueprints/patches/0001-*.patch docs/blueprints/patches/0002-*.patch
# or, to keep them as working-tree changes:
git apply docs/blueprints/patches/*.patch
```

## State when they were reset

| | |
|---|---|
| Blueprints suite | **3259** / 0 failed / 10 skipped — baseline 3243, **+16 new tests** |
| Revert-goes-red | confirmed per item, never delegated |
| Ids allocated | **`BP1670`** (diagnostic). ⚠ **No tracker rows** — those were never written, so nothing collides |

⭐ **`BP-233` came out of this work** and is already filed: `BP1650` carries a **fourth** copy of the
"can this suspend?" predicate with the same `ChannelCommandNode` omission that `0001` fixes in
`MacroLatency`.

## ⛔ Delete this directory once the work is back on a branch

It exists because the commits are unreachable, not because patches belong in the repo.
