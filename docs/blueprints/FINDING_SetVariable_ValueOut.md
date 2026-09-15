# The printed `0` — root cause found. It is **not** the Print String node.

> **Settles BP-209**, using the user's real `Count4.bp.json` (2026-08-10). The user's own hunch —
> *"it might not be the problem of printstring node"* — is correct.
>
> ⚠ **No IDs allocated here** (rule 3). The implementation session numbers these when it makes the rows.

---

## The chain, verified link by link

### 1 · The wiring is **perfect**. Every pin id resolves.

Pin identity is `SHA-256("pin:{nodeId:N}:{name}:{direction}")` with v5 bits
(`DeterministicIds.PinId`). Recomputed against the asset:

| Link in the asset | Resolves to | |
|---|---|---|
| `85b1cf0e…` → `4aaa2777…` | SetVariable **`Out`** (exec) → PrintString **`In`** (exec) | ✅ |
| `d0c61372…` → `2f2db7d9…` | SetVariable **`Value` Out** (data) → PrintString **`threat` In** (data) | ✅ |

⇒ **Not BP-202.** No dangling link, no positional rebinding. The `2f2db7d9…` GUID that appeared in
BP-206's example `BP1602` was an *earlier* state of this asset; it is valid now.

### 2 · Both projection halves agree, and the pin is correctly typed

| | |
|---|---|
| Editor | `NodePinSchema.SetVariablePins:792-799` → `MakeData("Value", "Out", typeId)` |
| Compiler | `Stage0_Rehydrate.EnrichSetVariablePins:333-345` → `MakePin("Value", "Out", …, typeId)` |

`typeId` = `System.Int32` from the `Count` variable. ⇒ **This is why the build is clean** — the pin
exists on both sides and the types match, so nothing complains.

### 3 · 🔴 Nothing implements reading it

`SetVariableNode` appears **exactly once** in `Stage5_Schedule.cs` — line **1186**, the *statement*
switch:

```csharp
case SetVariableNode sv:
{
    int idx = FindVariableIndex(sv.VariableId);
    var dataPin = node.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "In");
    if (dataPin is null) break;
    var val = ResolveDataPin(node.Id, dataPin.Id, stmts);
    stmts.Add(new IrStatement {
        Operation = new IrOp_WriteVariable(idx, val),   // ⚠ no ResultValue
        Debug     = DebugOf(node),
    });
    break;
}
```

**No `ResultValue`. No `_statementPinCache` entry.** (Contrast `SetSharedNode` immediately below,
which *does* allocate a `writtenResult` for its out pin.)

So when Print String pulls `threat`, `ResolveNodeOutput` finds **no case** for `SetVariableNode` and
lands here (`:3589`):

```csharp
default:
{
    // Unknown pure source -- dummy value.
    result = AllocValue(pinType);
    stmts.Add(new IrStatement {
        ResultValue = result,
        Operation   = new IrOp_Const("default", pinType),        // ⇒ default(int) == 0
        Debug       = new IrDebugAnnotation {
            Synthesized = $"unknown-source-{sourceNode.GetType().Name}",   // "unknown-source-SetVariableNode"
        },
    });
    break;
}
```

⇒ `default(int)` = **`0`**, emitted **silently, every tick, forever.**

### 4 · Every observation is now accounted for

| Observed | Explained by |
|---|---|
| `Count` rises in the Runtime inspector | the write at `:1186` works — that half is fine |
| the log prints `0`, always | `IrOp_Const("default", Int32)` |
| the build is clean | the `default:` arm emits **no diagnostic** |
| BP-209 measured *"only a numerically **typed AND unwired** pin prints 0"* | ⭐ **the measurement was right.** The pin here *is* typed `Int32`; it is wired, but to a producer with no resolver case — **a second route into the same `default(...)`** that nobody had tested |

---

## ⭐ The systemic finding is larger than this node

`SetVariable.Value`-Out is **a pin that exists purely as a promise**: projected by both halves,
wirable, correctly typed — and unreadable. The `default:` arm makes that indistinguishable from a
working wire.

**This is Trap #5 in its purest form yet** — a `default:` arm returning a plausible value instead of
reporting. It is the same shape as `Stage5:4497`'s `_ => IrGraphKind.Function`, which
[BP-79](Blueprint_Issues_Detail.md#bp-79) exists to close for macros.

### Audit — data-out projections vs `ResolveNodeOutput` cases

`ResolveNodeOutput` (`:2253-3606`) has **24** node cases. Only `FunctionCallNode` (impure),
`CallPeerBlueprintNode` and `ScoreDecisionNode` populate `_statementPinCache` (`:1642`, `:1711`,
`:2158`), which is the only other way a pull can be served.

| Node type | Projects a data-Out | Resolver case | Statement cache | |
|---|---|---|---|---|
| **`SetVariableNode`** | ✅ `Value` | ❌ | ❌ | 🔴 **confirmed — this bug** |
| `SetSharedNode` | ✅ `Written` | ❌ | ❌ | 🔴 same signature ⇒ reads `false` |
| `SetComponentNode` | ✅ | ❌ | ❌ | 🟠 same signature |
| `CollectionWriteNode` | ✅ | ❌ | ❌ | 🟠 same signature |
| `ListWriteNode` | ✅ | ❌ | ❌ | 🟠 same signature |
| `ComponentForEachNode` | ✅ | ❌ | ❌ | ⚠ may be served by loop lowering — **check before claiming** |

⚠ **Only the first row is traced end to end.** The rest share the static signature; each needs the
same three-step check before being called a bug. Stated this way deliberately — the last two batches
each caught me asserting a mechanism I had inferred rather than measured.

---

## Recommended work

| | |
|---|---|
| **1 · Fix** | In the `:1186` case, allocate a `ResultValue` for the `Value`-Out pin and record it in `_statementPinCache`, a pass-through of the written value. ⭐ **Mirror `SetSharedNode`'s `writtenResult` shape exactly** — the precedent is eight lines below. **Unreal parity:** `Set` has precisely this pass-through output |
| **2 · 🔴 Make the trap loud** | The `default:` arm must **report**. It already computes the message — `Synthesized = "unknown-source-SetVariableNode"` — and then throws it away. A diagnostic naming node + pin + type turns this whole family from silent-wrong-value into a build error. ⚠ **Do this second**: it will fail every node in the audit table, which is the point, but the fixes must land with it |
| **3 · Audit** | Complete the table above — three-step check per row |
| **4 · Test-lock** | `AuthoringPathRunValueTests` already has the harness: wire `SetVariable.Value` → `Print String`, tick twice, assert the log reads **11** then **22** — not `0`. ⭐ **Fully headless**, and it would have caught this |

⚠ **Do not "fix" this by removing the pin.** It is genuinely useful and Unreal ships it. The defect is
the missing implementation, not the pin.

---

## Process note

Three sessions in a row diagnosed this wrong before the asset arrived:

| Attempt | Claim | Verdict |
|---|---|---|
| my Batch 27 handoff | empty `ArgTypes` ⇒ pin `System.Object` ⇒ default | ❌ refuted by measurement |
| BP-209's candidates | wrong variable · BP-202 rebinding · `SetVariable` never ran | ❌ all three refuted by the pin-id computation |
| this | `SetVariable.Value`-Out has no resolver case | ✅ traced end to end |

⭐ **What actually closed it was computing the pin GUIDs**, which takes about a minute and converts
"probably the link is stale" into a yes/no. **Do that first** on any future report that names a pin
GUID. BP-206 now puts asset + graph names in the diagnostic, so the input is cheaper to get than it
was.
