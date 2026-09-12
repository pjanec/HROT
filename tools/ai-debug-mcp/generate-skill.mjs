/**
 * generate-skill.mjs — Assemble SKILL.md from skill-parts/ partials + tool-catalog.mjs.
 *
 * Usage:
 *   node generate-skill.mjs           — write SKILL.md
 *   node generate-skill.mjs --check   — verify SKILL.md is up to date (exit 1 if stale)
 */

import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { TOOLS_CATALOG } from './tool-catalog.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const isCheck = process.argv.includes('--check');

// ── Read partials in filename order ─────────────────────────────────────────

const partsDir = join(__dirname, 'skill-parts');
const partFiles = readdirSync(partsDir)
  .filter(f => f.endsWith('.md'))
  .sort();

function readPart(filename) {
  return readFileSync(join(partsDir, filename), 'utf8');
}

// Parts by prefix number
const parts = {};
for (const f of partFiles) {
  const prefix = f.split('-')[0];
  parts[prefix] = readPart(f);
}

// ── Generate §4 from catalog ─────────────────────────────────────────────────

function formatParam(p) {
  // Req params: Req `name` (type)
  // Optional params: `name?` (type, brief desc, def X)
  if (p.required) {
    const typeStr = p.type ? ` (${p.type})` : '';
    return `Req \`${p.name}\`${typeStr}`;
  } else {
    const parts = [];
    if (p.type) parts.push(p.type);
    if (p.enum) parts.push(p.enum.map(v => `"${v}"`).join('|'));
    if (p.default !== undefined) parts.push(`def ${JSON.stringify(p.default)}`);
    const suffix = parts.length > 0 ? ` (${parts.join(', ')})` : '';
    return `\`${p.name}?\`${suffix}`;
  }
}

function generateSection4() {
  const lines = [];

  lines.push('---');
  lines.push('');
  lines.push('## 4. Full command reference');
  lines.push('');
  lines.push('Conventions: **Req** = required param. Coordinates are local ECS metres unless stated; `networkId` is a long.');
  lines.push('');

  // Group tools by group name, preserving catalog order
  const groups = [];
  const groupMap = new Map();

  for (const entry of TOOLS_CATALOG) {
    if (!groupMap.has(entry.group)) {
      groupMap.set(entry.group, []);
      groups.push(entry.group);
    }
    groupMap.get(entry.group).push(entry);
  }

  for (const groupName of groups) {
    const tools = groupMap.get(groupName);
    lines.push(`### Group ${groupName}`);

    for (const entry of tools) {
      // Build the bullet line
      let bullet = `- **\`${entry.name}\`** — ${entry.summary}`;

      // Add param descriptions inline
      const reqParams = entry.params.filter(p => p.required);
      const optParams = entry.params.filter(p => !p.required);

      const paramParts = [];
      for (const p of reqParams) {
        paramParts.push(formatParam(p));
      }
      for (const p of optParams) {
        paramParts.push(formatParam(p));
      }

      if (paramParts.length > 0) {
        bullet += ' ' + paramParts.join(', ') + '.';
      } else {
        bullet += ' No params.';
      }

      bullet += ` Returns ${entry.returns}`;

      lines.push(bullet);

      // Notes as continuation lines
      if (entry.notes && entry.notes.length > 0) {
        lines.push(`  Notes: ${entry.notes.join('; ')}.`);
      }

      // Example
      if (entry.example) {
        const argsStr = JSON.stringify(entry.example.args);
        lines.push(`  Example: \`${entry.name}(${argsStr})\` — ${entry.example.gist}.`);
      }
    }

    lines.push('');
  }

  return lines.join('\n');
}

// ── Assemble full SKILL.md ───────────────────────────────────────────────────

function generate() {
  const section4 = generateSection4();

  // Assemble: frontmatter(00) + §1(10) + §2(20) + §3(30) + §4(generated) + §5(50) + §6(60)
  const assembled = [
    parts['00'].trimEnd(),
    '',
    parts['10'].trimEnd(),
    '',
    parts['20'].trimEnd(),
    '',
    parts['30'].trimEnd(),
    '',
    section4.trimEnd(),
    '',
    parts['50'].trimEnd(),
    '',
    parts['60'].trimEnd(),
    '',
  ].join('\n');

  return assembled;
}

// ── Main ─────────────────────────────────────────────────────────────────────

const skillPath = join(__dirname, 'SKILL.md');
const generated = generate();

if (isCheck) {
  let current;
  try {
    current = readFileSync(skillPath, 'utf8');
  } catch {
    console.error('gen:skill:check FAILED: SKILL.md does not exist');
    process.exit(1);
  }

  if (current === generated) {
    console.log('gen:skill:check PASSED (SKILL.md is up to date)');
    process.exit(0);
  } else {
    // Show a short diff summary
    const currentLines = current.split('\n');
    const generatedLines = generated.split('\n');
    const maxLines = Math.max(currentLines.length, generatedLines.length);
    let firstDiff = -1;
    let diffCount = 0;
    for (let i = 0; i < maxLines; i++) {
      if (currentLines[i] !== generatedLines[i]) {
        if (firstDiff === -1) firstDiff = i;
        diffCount++;
      }
    }
    console.error(`gen:skill:check FAILED: SKILL.md is stale`);
    console.error(`  First differing line: ${firstDiff + 1}`);
    console.error(`  Total differing lines: ${diffCount}`);
    console.error(`  Current line count: ${currentLines.length}, Generated: ${generatedLines.length}`);
    if (firstDiff >= 0) {
      console.error(`  Current  [${firstDiff + 1}]: ${JSON.stringify(currentLines[firstDiff])}`);
      console.error(`  Generated[${firstDiff + 1}]: ${JSON.stringify(generatedLines[firstDiff])}`);
    }
    console.error('Run: node generate-skill.mjs  to regenerate');
    process.exit(1);
  }
} else {
  writeFileSync(skillPath, generated, 'utf8');
  console.log(`SKILL.md written (${generated.length} bytes, ${generated.split('\n').length} lines)`);
}
