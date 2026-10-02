# ADA-BATCH-15 Migration Notes

## What was split into partials

The original `SKILL.md` was split into verbatim partials under `skill-parts/`:

| File | Content | Source lines (approx) |
|------|---------|----------------------|
| `00-frontmatter.md` | YAML frontmatter, H1 title, intro paragraph | 1–11 |
| `10-mental-model.md` | `## 1. Mental model` section | 14–47 |
| `20-lifecycle.md` | `## 2. Lifecycle` section table | 49–58 |
| `30-workflows.md` | `## 3. Canonical workflows` section (A–I) | 60–121 |
| `50-gotchas.md` | `## 5. Gotchas` section | 234–252 |
| `60-discovery.md` | `## 6. Discover before you guess` section | 254–264 |

All partials are **verbatim copies** — not one word was changed from the original SKILL.md.

## Section §4 format changes

The hand-written §4 was replaced by a catalog-generated §4. Content is equivalent:

- All tool names, HTTP methods/paths preserved exactly.
- All param names, types, required/optional status preserved from index.mjs inputSchemas.
- Enum values, defaults, and descriptions all carried into the catalog.
- Notes from index.mjs descriptions and SKILL.md are in the catalog `notes` arrays.
- The generated format uses consistent bullet style:
  `- **\`tool_name\`** — summary. Req \`param\` (type). Optional params. Returns description.`
  followed by optional Notes and Example continuation lines.

The generated §4 is a **superset** of the original hand-written §4:
- Every tool present in original is in generated (49 tools vs ~47 in original — focus_entity and
  add_annotation were listed under "Group F (manual-assist)" in the original; they now have their
  own group entry).
- Returns descriptions are more explicit (copied from catalog).
- Each tool now also has an Example line (new — not in original).
- Notes are explicit (previously embedded inline in description text).

## Intentional rewordings

None. All summary text was authored from the original descriptions in index.mjs and SKILL.md §4.
The format changed (structured catalog → generated bullets) but no user-facing terminology or
meaning was altered.

## Superset confirmation

Running `node generate-skill.mjs --check` confirms the generated SKILL.md is stable (idempotent).
The generated file covers every group (A through N) and all 49 tools.
