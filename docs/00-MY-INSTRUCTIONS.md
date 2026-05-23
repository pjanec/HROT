# Lead Architect Documentation Instructions

**Role**: Documentation Lead (this is MY operating procedure)
**Purpose**: Systematically produce architectural documentation for every project in the solution.

---

## My Workflow Loop (Execute Until All Done)

1. **READ** `docs/00-PROJECT-CHECKLIST.md` - find the first item marked `[ ]`.
2. **DELEGATE** - use `runSubagent` with model `Claude Sonnet 4.6` to write the doc.
3. **VERIFY** - check the output file exists and has >= 500 lines.
4. **UPDATE** - mark item `[X]` in checklist.
5. **REPEAT** from step 1.

---

## Subagent Instructions Template

When delegating to a subagent, give it:
- The exact project path (csproj location)
- The output file path: `docs/projects/[Category]/[ProjectName].md`
- The documentation standards (see below)
- Instruction to read ALL source files in the project folder
- Instruction to check for existing README and evaluate if it is up-to-date

---

## Documentation Standards (Per Project)

**Output Location**: `docs/projects/[Category]/[ProjectName].md`

### Required Sections (minimum 500 lines):
1. Header (project name, path, date)
2. README Validation: "Up-to-date", "Diverged", or "Missing"
3. Overview: purpose, key features, architectural layer
4. Architecture: design, components, constraints
5. ASCII Diagrams (MANDATORY, minimum 2-3): Block diagram, Flow diagram, State diagram
6. Source Analysis: key files, namespaces, core classes with description
7. Public API: all public types, methods, properties with descriptions
8. Dependencies: internal (project refs) and external (NuGet)
9. Usage Examples: minimum 3 code blocks (init, use, advanced)
10. Best Practices: thread safety, performance, pitfalls
11. Relationships: links to dependent/dependency projects

### ASCII Art Style:
```
+-------------------+       +-------------------+
|   Component A     |------>|   Component B     |
+-------------------+       +-------------------+
```

---

## Categories for Output Paths

| Projects | Category Folder |
|---|---|
| Fdp.Core, Fdp.ModuleHost, Fdp.Presentation, Fdp.Diagnostics.* | `FDP/Core` |
| Fdp.Network.Cyclone | `FDP/Network` |
| Fdp.Toolkits, Fdp.Toolkits.Analyzers, Tkb.SourceGen | `FDP/Toolkits` |
| Fdp.Tools.RecordingDumper | `FDP/Tools` |
| Fdp.Examples.* | `FDP/Examples` |
| Fbt.*, Fhsm.*, GizmoMap.*, NodeEditor.*, StructEdit.* | `FDP/ExtDeps` |
| Hrot.Common, Hrot.Core, Hrot.Presentation, Hrot.UI.Common | `Hrot/Engine` |
| Hrot.Network.* | `Hrot/Network` |
| Hrot.ClusterRunner, Hrot.FakeStrideApp | `Hrot/Runner` |
| Hrot.AI.Behaviors, Hrot.CGF, Hrot.Editor, Hrot.ExCon, Hrot.IG, Hrot.Orchestrator, Hrot.ReplayBrowser, Hrot.SimHost, Hrot.StrideMock | `Hrot/Subsystems` |
| Hrot.Blueprints.* | `Hrot/Blueprints` |
| Hrot.BTree.Editor, Hrot.Hsm.Editor | `Hrot/AI` |
| Hrot.Editor.AiShared | `Hrot/Editor` |
| Relationship docs | `relationships/` |

---

## Success Conditions Per Item

- File exists at `docs/projects/[Category]/[ProjectName].md`
- File has >= 500 lines
- File contains at least 2 ASCII block diagrams
- File has a Usage Examples section with code blocks
- File explicitly mentions README status

---

## Key Rules

- NEVER write code or documentation yourself - always delegate to subagent
- ALWAYS use model `Claude Sonnet 4.6` for subagents (not exploration agent)
- After EVERY document, update the checklist
- Check for emerging relationships while reviewing subagent output
- Add relationship items to Phase 5 of checklist when discovered
- Phase 6 (Master Overview) only starts when ALL other phases are [X]
