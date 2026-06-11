# CF1 Node Identity Diagnostic Report


- **Asset**: Count4
- **AssetId**: `47fe9c55-c6ca-4c69-9c5a-d46de25745de`
- **GraphId**: `10000006-0000-0000-0000-000000000001`
- **Compile succeeded**: True
- **Generated**: 2026-06-11T13:43:46Z
- **Source file**: `Hrot/Subsystems/Hrot.AI.Behaviors/Assets/Blueprints/Count4.bp.json`

## Table A — DebugMap entries


| NodeId | NodeKind | DisplayName | StartLine |
|---|---|---|---|
| `da9a9c0b-25f8-4a81-9a52-75c715456f18` | da9a9c0b-25f8-4a81-9a52-75c715456f18 |  | 61 |
| `12d5d9ed-e4f7-444d-af29-db1a16d955f5` | 12d5d9ed-e4f7-444d-af29-db1a16d955f5 |  | 65 |
| `20000006-0000-0000-0000-000000000004` |  |  | 66 |
| `0ec3b253-3c5a-1024-a7bb-bf767fb3130c` |  |  | 67 |
| `20000006-0000-0000-0000-000000000003` |  |  | 68 |
| `20000006-0000-0000-0000-000000000002` |  |  | 69 |
| `4257de0d-2c73-b177-4c58-026cdb5167b1` |  |  | 70 |
| `12d5d9ed-e4f7-444d-af29-db1a16d955f5` |  |  | 71 |
| `12d5d9ed-e4f7-444d-af29-db1a16d955f5` |  |  | 72 |
| `12d5d9ed-e4f7-444d-af29-db1a16d955f5` |  |  | 73 |
| `12d5d9ed-e4f7-444d-af29-db1a16d955f5` |  |  | 74 |
| `12d5d9ed-e4f7-444d-af29-db1a16d955f5` |  |  | 75 |
| `0b561966-b00b-4c84-a1a0-87042220ba9f` | 0b561966-b00b-4c84-a1a0-87042220ba9f |  | 79 |
| `976ef338-34f2-1469-973f-ee53538aab17` |  |  | 80 |
| `0b561966-b00b-4c84-a1a0-87042220ba9f` |  |  | 81 |
| `0b561966-b00b-4c84-a1a0-87042220ba9f` |  |  | 82 |
| `0b561966-b00b-4c84-a1a0-87042220ba9f` |  |  | 83 |
| `0b561966-b00b-4c84-a1a0-87042220ba9f` |  |  | 84 |
| `0b561966-b00b-4c84-a1a0-87042220ba9f` |  |  | 85 |

## Table B — Authored nodes vs DebugMap


| Id | Kind | DebugMap entry keyed by this exact authored Id? |
|---|---|---|
| `20000006-0000-0000-0000-000000000001` | EventEntryNode | NO |
| `20000006-0000-0000-0000-000000000002` | SetVariableNode | YES |
| `20000006-0000-0000-0000-000000000003` | FunctionCallNode | YES |
| `20000006-0000-0000-0000-000000000004` | GetVariableNode | YES |
| `da9a9c0b-25f8-4a81-9a52-75c715456f18` | SequenceNode | YES |
| `0b561966-b00b-4c84-a1a0-87042220ba9f` | LatentDelayNode | YES |
| `7b6da53f-4e11-4bc9-9d0c-bad0e22c7f5c` | ReturnNode | NO |
| `12d5d9ed-e4f7-444d-af29-db1a16d955f5` | LatentDelayNode | YES |

## Table C — Emitted DebugProbe.NodeEnter calls


| # | Probe Id | Matches authored node? |
|---|---|---|
| 1 | `da9a9c0b-25f8-4a81-9a52-75c715456f18` | YES |
| 2 | `12d5d9ed-e4f7-444d-af29-db1a16d955f5` | YES |
| 3 | `0b561966-b00b-4c84-a1a0-87042220ba9f` | YES |

## Section D — Losses: authored exec nodes with no DebugMap entry and no matching probe


### EventEntryNode (`20000006-0000-0000-0000-000000000001`)


- **In DebugMap by exact authored id?** NO
- **Has matching NodeEnter probe by exact authored id?** NO

- **Orphan DebugMap entries** (NodeId not matching any authored node): 3
-   - `0ec3b253-3c5a-1024-a7bb-bf767fb3130c` (Kind: , DisplayName: , StartLine: 67)
-   - `4257de0d-2c73-b177-4c58-026cdb5167b1` (Kind: , DisplayName: , StartLine: 70)
-   - `976ef338-34f2-1469-973f-ee53538aab17` (Kind: , DisplayName: , StartLine: 80)
- **Orphan NodeEnter probes** (id not matching any authored node): 0

- **IR/Synthesized tag analysis** — NOT AVAILABLE from `CompileResult` alone.
-   The lowered IR (`IrBlock` statements with `IrDebugAnnotation.Synthesized`) is internal
-   to the compiler pipeline and not exposed on `CompileResult`. To retrieve it, one would need
-   to run the pipeline stages separately (as `BPF015_DebugProbeEmitTests` does) and inspect
-   the IR directly. The `Synthesized` field on `IrDebugAnnotation` records the lowering tag
-   (e.g. `"stage6-wait-lower-inst"`) responsible for the identity replacement.

- **Known from ground truth (bp-diag.log + prior analysis):**
-   - Sequence `da9a9c0b` → `?` (Stage3_Normalize.SynthesizedGuid or Stage6 lowering)
-   - Delay `0b561966` → `?` (Stage6 WaitLowering_Instance.Synth)

### ReturnNode (`7b6da53f-4e11-4bc9-9d0c-bad0e22c7f5c`)


- **In DebugMap by exact authored id?** NO
- **Has matching NodeEnter probe by exact authored id?** NO

- **Orphan DebugMap entries** (NodeId not matching any authored node): 3
-   - `0ec3b253-3c5a-1024-a7bb-bf767fb3130c` (Kind: , DisplayName: , StartLine: 67)
-   - `4257de0d-2c73-b177-4c58-026cdb5167b1` (Kind: , DisplayName: , StartLine: 70)
-   - `976ef338-34f2-1469-973f-ee53538aab17` (Kind: , DisplayName: , StartLine: 80)
- **Orphan NodeEnter probes** (id not matching any authored node): 0

- **IR/Synthesized tag analysis** — NOT AVAILABLE from `CompileResult` alone.
-   The lowered IR (`IrBlock` statements with `IrDebugAnnotation.Synthesized`) is internal
-   to the compiler pipeline and not exposed on `CompileResult`. To retrieve it, one would need
-   to run the pipeline stages separately (as `BPF015_DebugProbeEmitTests` does) and inspect
-   the IR directly. The `Synthesized` field on `IrDebugAnnotation` records the lowering tag
-   (e.g. `"stage6-wait-lower-inst"`) responsible for the identity replacement.

- **Known from ground truth (bp-diag.log + prior analysis):**
-   - Sequence `da9a9c0b` → `?` (Stage3_Normalize.SynthesizedGuid or Stage6 lowering)
-   - Delay `0b561966` → `?` (Stage6 WaitLowering_Instance.Synth)

## Summary


- **Authored nodes**: 8
- **Authored nodes with DebugMap entry (exact Id match)**: 6/8
- **Authored nodes with NodeEnter probe (exact Id match)**: 3/8
- **Authored nodes MISSING from DebugMap**: 2/8
- **Authored nodes MISSING probe**: 5/8

- **Total DebugMap entries**: 19
- **Total emitted NodeEnter probes**: 3
- **Orphan probe IDs** (not matching any authored node): 0

- **Key finding:** The Sequence and Delay nodes lose their authored IDs during lowering,
- causing their DebugMap entries and NodeEnter probes to be keyed to synthesized IDs
- instead. Additionally, probe mis-attribution (DebugProbeInsertion using block.Statements[0])
- means the probe for an exec node's block may be keyed to a data-input node's ID instead.

