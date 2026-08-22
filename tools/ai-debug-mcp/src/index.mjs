/**
 * ai-debug-mcp — Thin stdio MCP proxy for the Hrot ClusterRunner AI Debug HTTP API.
 *
 * Design: strictly 1:1 mapping of MCP tools ↔ HTTP endpoints (groups A–N, BATCHes 02–05).
 * No business logic — the API owns all semantics; this server relays the
 * {ok, data, error, awaited} envelope verbatim.
 *
 * Tools for not-yet-built endpoints (breakpoints, checkpoint, recording, logs, traces,
 * mutation) are intentionally absent — they will be added in their own batches as
 * those API endpoints land.
 *
 * Usage:
 *   Launch mode (server spawns runner):
 *     node src/index.mjs --runner-dll <path/to/Hrot.ClusterRunner.dll> --port <N> [--headless]
 *
 *   Attach mode (runner already running):
 *     node src/index.mjs --url http://localhost:<N>
 */

import { Server } from '@modelcontextprotocol/sdk/server/index.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import {
  CallToolRequestSchema,
  ListToolsRequestSchema,
} from '@modelcontextprotocol/sdk/types.js';
import { spawn } from 'node:child_process';
import { parseArgs } from 'node:util';
import { TOOLS_CATALOG } from '../tool-catalog.mjs';

// ── Config parsing ──────────────────────────────────────────────────────────

const { values: args } = parseArgs({
  args: process.argv.slice(2),
  options: {
    url: { type: 'string' },
    'runner-dll': { type: 'string' },
    port: { type: 'string' },
    headless: { type: 'boolean', default: false },
  },
  strict: false,
});

// ── Process lifecycle state ─────────────────────────────────────────────────

/** The spawned runner child process (null in attach mode or before start_simulation). */
let runnerChild = null;
/** Base URL for the Debug API (set after launch or from --url). */
let baseUrl = args.url || null;

const LAUNCH_POLL_INTERVAL_MS = 1000;
const LAUNCH_TIMEOUT_MS = 60_000;
const GRACEFUL_KILL_TIMEOUT_MS = 10_000;

/**
 * Spawn the runner, poll /status until ready, set baseUrl, own the child.
 * @param {string} runnerDll  Path to Hrot.ClusterRunner.dll
 * @param {number} port       Debug API port
 * @param {boolean} headless  Pass --headless flag
 */
async function launchRunner(runnerDll, port, headless) {
  const dllArgs = [
    runnerDll,
    '-m', 'editor',
    '--debug-api',
    '--debug-api-port', String(port),
  ];
  if (headless) dllArgs.push('--headless');

  runnerChild = spawn('dotnet', dllArgs, {
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true,
  });

  runnerChild.stdout.on('data', (d) => process.stderr.write(`[runner stdout] ${d}`));
  runnerChild.stderr.on('data', (d) => process.stderr.write(`[runner stderr] ${d}`));

  runnerChild.on('exit', (code, signal) => {
    process.stderr.write(`[runner] exited code=${code} signal=${signal}\n`);
    runnerChild = null;
  });

  baseUrl = `http://localhost:${port}`;

  // Poll until /status returns 200 or timeout
  const deadline = Date.now() + LAUNCH_TIMEOUT_MS;
  while (Date.now() < deadline) {
    try {
      const resp = await fetch(`${baseUrl}/status`, { signal: AbortSignal.timeout(2000) });
      if (resp.ok) {
        process.stderr.write(`[mcp] runner ready at ${baseUrl}\n`);
        return;
      }
    } catch {
      // not ready yet
    }
    await sleep(LAUNCH_POLL_INTERVAL_MS);
  }
  throw new Error(`Runner did not become ready within ${LAUNCH_TIMEOUT_MS / 1000}s`);
}

/**
 * Graceful→hard kill the launched runner.
 * 1. POST /shutdown (empty body so Content-Length is set)
 * 2. Wait up to GRACEFUL_KILL_TIMEOUT_MS
 * 3. SIGKILL if still alive
 */
async function killRunner() {
  if (!runnerChild) return;
  const child = runnerChild;

  // Graceful: POST /shutdown
  if (baseUrl) {
    try {
      await fetch(`${baseUrl}/shutdown`, {
        method: 'POST',
        body: '',           // explicit empty body → Content-Length: 0 (avoids 411)
        headers: { 'Content-Type': 'application/json' },
        signal: AbortSignal.timeout(5000),
      });
    } catch {
      // ignore — process may have already gone
    }
  }

  // Wait for process exit
  const exited = await Promise.race([
    new Promise((res) => child.on('exit', () => res(true))),
    sleep(GRACEFUL_KILL_TIMEOUT_MS).then(() => false),
  ]);

  if (!exited && child.exitCode === null) {
    process.stderr.write('[mcp] runner did not exit gracefully — sending SIGKILL\n');
    child.kill('SIGKILL');
  }
  runnerChild = null;
}

// Tear down runner on server exit
const teardown = async () => {
  await killRunner();
  process.exit(0);
};
process.on('SIGINT', teardown);
process.on('SIGTERM', teardown);
process.on('exit', () => {
  // Synchronous fallback: if child is still alive, kill it hard
  if (runnerChild && runnerChild.exitCode === null) {
    try { runnerChild.kill('SIGKILL'); } catch { /* ignore */ }
  }
});

// ── Generic API helper ──────────────────────────────────────────────────────

/**
 * Call a Debug API endpoint.
 *
 * Rules:
 * - GET: no body
 * - POST with body: JSON stringify + Content-Type: application/json
 * - POST with no body: send empty string so fetch sets Content-Length: 0 (avoids 411)
 *
 * Returns the parsed {ok, data, error, awaited} envelope verbatim on success.
 * Throws a structured MCP tool error on HTTP error or ok:false.
 */
async function callApi(method, path, body) {
  if (!baseUrl) throw new McpToolError('No runner URL configured. Use start_simulation first or pass --url.');

  const url = `${baseUrl}${path}`;
  const init = { method };

  if (method === 'POST') {
    if (body !== undefined && body !== null) {
      init.body = JSON.stringify(body);
      init.headers = { 'Content-Type': 'application/json' };
    } else {
      // Bodyless POST — must set Content-Length: 0 to avoid 411 from HttpListener
      init.body = '';
      init.headers = { 'Content-Type': 'application/json' };
    }
  }

  let resp;
  try {
    resp = await fetch(url, init);
  } catch (err) {
    throw new McpToolError(`Network error calling ${method} ${path}: ${err.message}`);
  }

  let envelope;
  try {
    envelope = await resp.json();
  } catch {
    throw new McpToolError(`Non-JSON response from ${method} ${path} (HTTP ${resp.status})`);
  }

  if (!resp.ok || envelope?.ok === false) {
    const msg = envelope?.error || `HTTP ${resp.status} from ${method} ${path}`;
    throw new McpToolError(msg, envelope);
  }

  return envelope;
}

/** Structured MCP tool error that surfaces the API error message. */
class McpToolError extends Error {
  constructor(message, envelope) {
    super(message);
    this.envelope = envelope;
  }
}

function sleep(ms) {
  return new Promise((res) => setTimeout(res, ms));
}

// ── Catalog-driven schema/description helpers ────────────────────────────────

/**
 * Build an MCP inputSchema from a catalog params array.
 * Mirrors the param shapes exactly from the catalog.
 */
function buildInputSchema(params) {
  const properties = {};
  const required = [];
  for (const p of params) {
    const schema = {};
    if (p.type) schema.type = p.type;
    schema.description = p.description;
    if (p.enum) schema.enum = p.enum;
    if (p.type === 'array' && p.items) schema.items = p.items;
    if (p.type === 'object' && p.properties) schema.properties = p.properties;
    properties[p.name] = schema;
    if (p.required) required.push(p.name);
  }
  const result = { type: 'object', properties };
  if (required.length > 0) result.required = required;
  return result;
}

/**
 * Build an MCP tool description from a catalog entry.
 */
function buildDescription(entry) {
  const httpLine = entry.http ? `${entry.http.method} ${entry.http.path} — ` : '';
  return httpLine + entry.summary;
}

// Build TOOL_DEFS map from catalog
const TOOL_DEFS = Object.fromEntries(TOOLS_CATALOG.map(t => [t.name, {
  description: buildDescription(t),
  inputSchema: buildInputSchema(t.params),
}]));

// Build HINTS map from catalog
const HINTS = Object.fromEntries(TOOLS_CATALOG.map(t => [t.name, t.hint]));

// ── MCP tool result helpers ─────────────────────────────────────────────────

function toolSuccess(envelope) {
  return {
    content: [{ type: 'text', text: JSON.stringify(envelope, null, 2) }],
  };
}

function toolError(message, envelope, toolName) {
  const hint = toolName ? HINTS[toolName] : undefined;
  return {
    content: [{ type: 'text', text: JSON.stringify({
      ok: false,
      error: message,
      ...(envelope || {}),
      ...(hint ? { hint } : {}),
      docs: 'ai-debug-sim skill — see the tool reference',
    }, null, 2) }],
    isError: true,
  };
}

// ── Tool definitions ────────────────────────────────────────────────────────

/**
 * Each tool is { name, description, inputSchema, handler(args) }.
 * handler should return a CallToolResult.
 * Tools are 1:1 with currently-implemented HTTP endpoints (Groups A–N, BATCHes 02–05).
 * Descriptions and inputSchemas are now catalog-driven via TOOL_DEFS.
 */
const TOOLS = [

  // ── Group A — Lifecycle & Status ──────────────────────────────────────────

  {
    name: 'start_simulation',
    description: TOOL_DEFS['start_simulation'].description,
    inputSchema: TOOL_DEFS['start_simulation'].inputSchema,
    async handler(toolArgs) {
      try {
        const dll = toolArgs.runnerDll || args['runner-dll'];
        if (!dll) throw new McpToolError('runnerDll is required (or pass --runner-dll to the server)');
        const port = toolArgs.port ?? (args.port ? Number(args.port) : 8099);
        const headless = toolArgs.headless ?? args.headless ?? false;
        await launchRunner(dll, port, headless);
        return toolSuccess({ ok: true, data: { url: baseUrl, pid: runnerChild?.pid } });
      } catch (err) {
        return toolError(err.message, err.envelope, 'start_simulation');
      }
    },
  },

  {
    name: 'stop_simulation',
    description: TOOL_DEFS['stop_simulation'].description,
    inputSchema: TOOL_DEFS['stop_simulation'].inputSchema,
    async handler() {
      try {
        // Call /shutdown first (envelope passthrough), then tear down child
        let envelope = { ok: true };
        try {
          envelope = await callApi('POST', '/shutdown', null);
        } catch (err) {
          // If runner is already gone that's fine; still try to kill
          if (!runnerChild) return toolSuccess({ ok: true, data: { note: 'runner already gone' } });
        }
        await killRunner();
        return toolSuccess(envelope);
      } catch (err) {
        return toolError(err.message, err.envelope, 'stop_simulation');
      }
    },
  },

  {
    name: 'get_status',
    description: TOOL_DEFS['get_status'].description,
    inputSchema: TOOL_DEFS['get_status'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('GET', '/status')); }
      catch (err) { return toolError(err.message, err.envelope, 'get_status'); }
    },
  },

  // ── Group B — Queries ─────────────────────────────────────────────────────

  {
    name: 'list_entities',
    description: TOOL_DEFS['list_entities'].description,
    inputSchema: TOOL_DEFS['list_entities'].inputSchema,
    async handler(toolArgs) {
      try {
        const params = new URLSearchParams();
        if (toolArgs.component) params.set('component', toolArgs.component);
        if (toolArgs.near) params.set('near', toolArgs.near);
        const qs = params.toString() ? `?${params}` : '';
        return toolSuccess(await callApi('GET', `/entities${qs}`));
      } catch (err) { return toolError(err.message, err.envelope, 'list_entities'); }
    },
  },

  {
    name: 'get_entity',
    description: TOOL_DEFS['get_entity'].description,
    inputSchema: TOOL_DEFS['get_entity'].inputSchema,
    async handler(toolArgs) {
      try { return toolSuccess(await callApi('GET', `/entities/${toolArgs.networkId}`)); }
      catch (err) { return toolError(err.message, err.envelope, 'get_entity'); }
    },
  },

  {
    name: 'list_component_types',
    description: TOOL_DEFS['list_component_types'].description,
    inputSchema: TOOL_DEFS['list_component_types'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('GET', '/components')); }
      catch (err) { return toolError(err.message, err.envelope, 'list_component_types'); }
    },
  },

  {
    name: 'list_scenarios',
    description: TOOL_DEFS['list_scenarios'].description,
    inputSchema: TOOL_DEFS['list_scenarios'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('GET', '/scenarios')); }
      catch (err) { return toolError(err.message, err.envelope, 'list_scenarios'); }
    },
  },

  // ── Group C — Event History ───────────────────────────────────────────────

  {
    name: 'get_event_history',
    description: TOOL_DEFS['get_event_history'].description,
    inputSchema: TOOL_DEFS['get_event_history'].inputSchema,
    async handler(toolArgs) {
      try {
        const params = new URLSearchParams();
        if (toolArgs.bus) params.set('bus', toolArgs.bus);
        if (toolArgs.type) params.set('type', toolArgs.type);
        if (toolArgs.since != null) params.set('since', String(toolArgs.since));
        if (toolArgs.max != null) params.set('max', String(toolArgs.max));
        const qs = params.toString() ? `?${params}` : '';
        return toolSuccess(await callApi('GET', `/events${qs}`));
      } catch (err) { return toolError(err.message, err.envelope, 'get_event_history'); }
    },
  },

  // ── Group D — Sim / Preview / Time Control ────────────────────────────────

  {
    name: 'get_sim_state',
    description: TOOL_DEFS['get_sim_state'].description,
    inputSchema: TOOL_DEFS['get_sim_state'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('GET', '/sim/state')); }
      catch (err) { return toolError(err.message, err.envelope, 'get_sim_state'); }
    },
  },

  {
    name: 'play',
    description: TOOL_DEFS['play'].description,
    inputSchema: TOOL_DEFS['play'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('POST', '/sim/play', null)); }
      catch (err) { return toolError(err.message, err.envelope, 'play'); }
    },
  },

  {
    name: 'pause',
    description: TOOL_DEFS['pause'].description,
    inputSchema: TOOL_DEFS['pause'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('POST', '/sim/pause', null)); }
      catch (err) { return toolError(err.message, err.envelope, 'pause'); }
    },
  },

  {
    name: 'step',
    description: TOOL_DEFS['step'].description,
    inputSchema: TOOL_DEFS['step'].inputSchema,
    async handler(toolArgs) {
      try {
        const body = toolArgs.count != null ? { count: toolArgs.count } : {};
        return toolSuccess(await callApi('POST', '/sim/step', body));
      } catch (err) { return toolError(err.message, err.envelope, 'step'); }
    },
  },

  {
    name: 'set_time_scale',
    description: TOOL_DEFS['set_time_scale'].description,
    inputSchema: TOOL_DEFS['set_time_scale'].inputSchema,
    async handler(toolArgs) {
      try { return toolSuccess(await callApi('POST', '/sim/timescale', { scale: toolArgs.scale })); }
      catch (err) { return toolError(err.message, err.envelope, 'set_time_scale'); }
    },
  },

  {
    name: 'enter_preview',
    description: TOOL_DEFS['enter_preview'].description,
    inputSchema: TOOL_DEFS['enter_preview'].inputSchema,
    async handler(toolArgs) {
      try {
        const body = toolArgs.startPaused != null ? { startPaused: toolArgs.startPaused } : {};
        return toolSuccess(await callApi('POST', '/preview/enter', body));
      } catch (err) { return toolError(err.message, err.envelope, 'enter_preview'); }
    },
  },

  {
    name: 'stop_preview',
    description: TOOL_DEFS['stop_preview'].description,
    inputSchema: TOOL_DEFS['stop_preview'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('POST', '/preview/exit', null)); }
      catch (err) { return toolError(err.message, err.envelope, 'stop_preview'); }
    },
  },

  // ── Group E — Scenario Load ───────────────────────────────────────────────

  {
    name: 'load_scenario',
    description: TOOL_DEFS['load_scenario'].description,
    inputSchema: TOOL_DEFS['load_scenario'].inputSchema,
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/scenario/load', {
          name: toolArgs.name,
          waitForReady: toolArgs.waitForReady ?? false,
        }));
      } catch (err) { return toolError(err.message, err.envelope, 'load_scenario'); }
    },
  },

  {
    name: 'save_scenario',
    description: TOOL_DEFS['save_scenario'].description,
    inputSchema: TOOL_DEFS['save_scenario'].inputSchema,
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/scenario/save', { name: toolArgs.name }));
      } catch (err) { return toolError(err.message, err.envelope, 'save_scenario'); }
    },
  },

  // ── Group F — Entity Commands ─────────────────────────────────────────────

  {
    name: 'list_commands',
    description: TOOL_DEFS['list_commands'].description,
    inputSchema: TOOL_DEFS['list_commands'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('GET', '/commands')); }
      catch (err) { return toolError(err.message, err.envelope, 'list_commands'); }
    },
  },

  {
    name: 'send_entity_command',
    description: TOOL_DEFS['send_entity_command'].description,
    inputSchema: TOOL_DEFS['send_entity_command'].inputSchema,
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/entities/command', {
          eventType: toolArgs.eventType,
          payload: toolArgs.payload ?? {},
          wait: toolArgs.wait ?? false,
        }));
      } catch (err) { return toolError(err.message, err.envelope, 'send_entity_command'); }
    },
  },

  {
    name: 'spawn_entity',
    description: TOOL_DEFS['spawn_entity'].description,
    inputSchema: TOOL_DEFS['spawn_entity'].inputSchema,
    async handler(toolArgs) {
      try {
        const body = { tkbType: toolArgs.tkbType };
        if (toolArgs.transform != null) body.transform = toolArgs.transform;
        if (toolArgs.components != null) body.components = toolArgs.components;
        if (toolArgs.attributesJson != null) body.attributesJson = toolArgs.attributesJson;
        return toolSuccess(await callApi('POST', '/entities/spawn', body));
      } catch (err) { return toolError(err.message, err.envelope, 'spawn_entity'); }
    },
  },

  // ── Group M — TKB Catalog ─────────────────────────────────────────────────

  {
    name: 'list_entity_types',
    description: TOOL_DEFS['list_entity_types'].description,
    inputSchema: TOOL_DEFS['list_entity_types'].inputSchema,
    async handler(toolArgs) {
      try {
        const qs = toolArgs.category ? `?category=${encodeURIComponent(toolArgs.category)}` : '';
        return toolSuccess(await callApi('GET', `/tkb/types${qs}`));
      } catch (err) { return toolError(err.message, err.envelope, 'list_entity_types'); }
    },
  },

  {
    name: 'get_entity_type',
    description: TOOL_DEFS['get_entity_type'].description,
    inputSchema: TOOL_DEFS['get_entity_type'].inputSchema,
    async handler(toolArgs) {
      try { return toolSuccess(await callApi('GET', `/tkb/types/${toolArgs.tkbType}`)); }
      catch (err) { return toolError(err.message, err.envelope, 'get_entity_type'); }
    },
  },

  // ── Group N — World / Coordinate Info ────────────────────────────────────

  {
    name: 'get_world_info',
    description: TOOL_DEFS['get_world_info'].description,
    inputSchema: TOOL_DEFS['get_world_info'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('GET', '/world/info')); }
      catch (err) { return toolError(err.message, err.envelope, 'get_world_info'); }
    },
  },

  {
    name: 'geo_to_local',
    description: TOOL_DEFS['geo_to_local'].description,
    inputSchema: TOOL_DEFS['geo_to_local'].inputSchema,
    async handler(toolArgs) {
      try {
        const body = { lat: toolArgs.lat, lon: toolArgs.lon, alt: toolArgs.alt };
        if (toolArgs.headingDeg != null) body.headingDeg = toolArgs.headingDeg;
        return toolSuccess(await callApi('POST', '/world/geo-to-local', body));
      } catch (err) { return toolError(err.message, err.envelope, 'geo_to_local'); }
    },
  },

  {
    name: 'local_to_geo',
    description: TOOL_DEFS['local_to_geo'].description,
    inputSchema: TOOL_DEFS['local_to_geo'].inputSchema,
    async handler(toolArgs) {
      try {
        const body = { x: toolArgs.x, y: toolArgs.y, z: toolArgs.z };
        if (toolArgs.rotation != null) body.rotation = toolArgs.rotation;
        return toolSuccess(await callApi('POST', '/world/local-to-geo', body));
      } catch (err) { return toolError(err.message, err.envelope, 'local_to_geo'); }
    },
  },

  // ── Group G — Breakpoints (ADA-BATCH-07) ─────────────────────────────────

  {
    name: 'set_breakpoint',
    description: TOOL_DEFS['set_breakpoint'].description,
    inputSchema: TOOL_DEFS['set_breakpoint'].inputSchema,
    async handler(toolArgs) {
      try {
        const body = { condition: toolArgs.condition };
        if (toolArgs.filterNetworkId != null) body.filterNetworkId = toolArgs.filterNetworkId;
        if (toolArgs.occurrenceThreshold != null) body.occurrenceThreshold = toolArgs.occurrenceThreshold;
        if (toolArgs.name != null) body.name = toolArgs.name;
        return toolSuccess(await callApi('POST', '/breakpoints', body));
      } catch (err) { return toolError(err.message, err.envelope, 'set_breakpoint'); }
    },
  },

  {
    name: 'list_breakpoints',
    description: TOOL_DEFS['list_breakpoints'].description,
    inputSchema: TOOL_DEFS['list_breakpoints'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('GET', '/breakpoints')); }
      catch (err) { return toolError(err.message, err.envelope, 'list_breakpoints'); }
    },
  },

  {
    name: 'remove_breakpoint',
    description: TOOL_DEFS['remove_breakpoint'].description,
    inputSchema: TOOL_DEFS['remove_breakpoint'].inputSchema,
    async handler(toolArgs) {
      try { return toolSuccess(await callApi('DELETE', `/breakpoints/${encodeURIComponent(toolArgs.id)}`)); }
      catch (err) { return toolError(err.message, err.envelope, 'remove_breakpoint'); }
    },
  },

  {
    name: 'get_breakpoint_status',
    description: TOOL_DEFS['get_breakpoint_status'].description,
    inputSchema: TOOL_DEFS['get_breakpoint_status'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('GET', '/breakpoints/hits')); }
      catch (err) { return toolError(err.message, err.envelope, 'get_breakpoint_status'); }
    },
  },

  // ── Group H — Checkpoint / Restore / Diff (ADA-BATCH-08) ─────────────────

  {
    name: 'checkpoint',
    description: TOOL_DEFS['checkpoint'].description,
    inputSchema: TOOL_DEFS['checkpoint'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('POST', '/checkpoint', null)); }
      catch (err) { return toolError(err.message, err.envelope, 'checkpoint'); }
    },
  },

  {
    name: 'restore_checkpoint',
    description: TOOL_DEFS['restore_checkpoint'].description,
    inputSchema: TOOL_DEFS['restore_checkpoint'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('POST', '/checkpoint/restore', null)); }
      catch (err) { return toolError(err.message, err.envelope, 'restore_checkpoint'); }
    },
  },

  {
    name: 'capture_diff_baseline',
    description: TOOL_DEFS['capture_diff_baseline'].description,
    inputSchema: TOOL_DEFS['capture_diff_baseline'].inputSchema,
    async handler(toolArgs) {
      try {
        const body = {};
        if (toolArgs.entities != null) body.entities = toolArgs.entities;
        return toolSuccess(await callApi('POST', '/diff/capture', body));
      } catch (err) { return toolError(err.message, err.envelope, 'capture_diff_baseline'); }
    },
  },

  {
    name: 'diff_state',
    description: TOOL_DEFS['diff_state'].description,
    inputSchema: TOOL_DEFS['diff_state'].inputSchema,
    async handler(toolArgs) {
      try {
        const body = { baselineId: toolArgs.baselineId };
        if (toolArgs.entities != null) body.entities = toolArgs.entities;
        return toolSuccess(await callApi('POST', '/diff/compare', body));
      } catch (err) { return toolError(err.message, err.envelope, 'diff_state'); }
    },
  },

  // ── Group I — Recording + Replay (ADA-BATCH-10) ──────────────────────────

  {
    name: 'start_recording',
    description: TOOL_DEFS['start_recording'].description,
    inputSchema: TOOL_DEFS['start_recording'].inputSchema,
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/recording/start', { mode: toolArgs.mode ?? 'preview' }));
      } catch (err) { return toolError(err.message, err.envelope, 'start_recording'); }
    },
  },

  {
    name: 'stop_recording',
    description: TOOL_DEFS['stop_recording'].description,
    inputSchema: TOOL_DEFS['stop_recording'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('POST', '/recording/stop', null)); }
      catch (err) { return toolError(err.message, err.envelope, 'stop_recording'); }
    },
  },

  {
    name: 'load_replay',
    description: TOOL_DEFS['load_replay'].description,
    inputSchema: TOOL_DEFS['load_replay'].inputSchema,
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/replay/load', { fdpPath: toolArgs.fdpPath }));
      } catch (err) { return toolError(err.message, err.envelope, 'load_replay'); }
    },
  },

  {
    name: 'seek_replay',
    description: TOOL_DEFS['seek_replay'].description,
    inputSchema: TOOL_DEFS['seek_replay'].inputSchema,
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/replay/seek', { frame: toolArgs.frame }));
      } catch (err) { return toolError(err.message, err.envelope, 'seek_replay'); }
    },
  },

  {
    name: 'step_replay',
    description: TOOL_DEFS['step_replay'].description,
    inputSchema: TOOL_DEFS['step_replay'].inputSchema,
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/replay/step', { dir: toolArgs.dir ?? 'forward' }));
      } catch (err) { return toolError(err.message, err.envelope, 'step_replay'); }
    },
  },

  {
    name: 'get_replay_status',
    description: TOOL_DEFS['get_replay_status'].description,
    inputSchema: TOOL_DEFS['get_replay_status'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('GET', '/replay/status')); }
      catch (err) { return toolError(err.message, err.envelope, 'get_replay_status'); }
    },
  },

  {
    name: 'list_replay_entities',
    description: TOOL_DEFS['list_replay_entities'].description,
    inputSchema: TOOL_DEFS['list_replay_entities'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('GET', '/replay/entities')); }
      catch (err) { return toolError(err.message, err.envelope, 'list_replay_entities'); }
    },
  },

  {
    name: 'unload_replay',
    description: TOOL_DEFS['unload_replay'].description,
    inputSchema: TOOL_DEFS['unload_replay'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('POST', '/replay/unload', null)); }
      catch (err) { return toolError(err.message, err.envelope, 'unload_replay'); }
    },
  },

  // ── Group J — Logs (ADA-BATCH-11) ────────────────────────────────────────

  {
    name: 'get_logs',
    description: TOOL_DEFS['get_logs'].description,
    inputSchema: TOOL_DEFS['get_logs'].inputSchema,
    async handler(toolArgs) {
      try {
        const params = new URLSearchParams();
        if (toolArgs.level) params.set('level', toolArgs.level);
        if (toolArgs.logger) params.set('logger', toolArgs.logger);
        if (toolArgs.since) params.set('since', toolArgs.since);
        if (toolArgs.max != null) params.set('max', String(toolArgs.max));
        const qs = params.toString() ? `?${params}` : '';
        return toolSuccess(await callApi('GET', `/logs${qs}`));
      } catch (err) { return toolError(err.message, err.envelope, 'get_logs'); }
    },
  },

  // ── Group K — AI Behavior Traces (ADA-BATCH-12) ──────────────────────────

  {
    name: 'observe_trace',
    description: TOOL_DEFS['observe_trace'].description,
    inputSchema: TOOL_DEFS['observe_trace'].inputSchema,
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/trace/observe', {
          networkId: toolArgs.networkId,
          on: toolArgs.on,
        }));
      } catch (err) { return toolError(err.message, err.envelope, 'observe_trace'); }
    },
  },

  {
    name: 'get_entity_trace',
    description: TOOL_DEFS['get_entity_trace'].description,
    inputSchema: TOOL_DEFS['get_entity_trace'].inputSchema,
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('GET', `/entities/${toolArgs.networkId}/trace`));
      } catch (err) { return toolError(err.message, err.envelope, 'get_entity_trace'); }
    },
  },


  // ── Group L — Live Mutation / Fault Injection (ADA-BATCH-13) ────────────────

  {
    name: 'get_attributes_schema',
    description: TOOL_DEFS['get_attributes_schema'].description,
    inputSchema: TOOL_DEFS['get_attributes_schema'].inputSchema,
    async handler() {
      try { return toolSuccess(await callApi('GET', '/attributes/schema')); }
      catch (err) { return toolError(err.message, err.envelope, 'get_attributes_schema'); }
    },
  },

  {
    name: 'patch_attribute',
    description: TOOL_DEFS['patch_attribute'].description,
    inputSchema: TOOL_DEFS['patch_attribute'].inputSchema,
    async handler(toolArgs) {
      try {
        // Accept patchJson as either a nested object or a string.
        // Pass it directly — the server handles both forms.
        return toolSuccess(await callApi('POST', `/entities/${toolArgs.networkId}/attribute`, {
          patchJson: toolArgs.patchJson,
        }));
      } catch (err) { return toolError(err.message, err.envelope, 'patch_attribute'); }
    },
  },

  {
    name: 'edit_component',
    description: TOOL_DEFS['edit_component'].description,
    inputSchema: TOOL_DEFS['edit_component'].inputSchema,
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', `/entities/${toolArgs.networkId}/component`, {
          componentType: toolArgs.componentType,
          patch: toolArgs.patch,
        }));
      } catch (err) { return toolError(err.message, err.envelope, 'edit_component'); }
    },
  },

  // ── Group M — Focus + Annotations (ADA-BATCH-14) ────────────────────────────

  {
    name: 'focus_entity',
    description: TOOL_DEFS['focus_entity'].description,
    inputSchema: TOOL_DEFS['focus_entity'].inputSchema,
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', `/entities/${toolArgs.networkId}/focus`, {}));
      } catch (err) { return toolError(err.message, err.envelope, 'focus_entity'); }
    },
  },

  {
    name: 'add_annotation',
    description: TOOL_DEFS['add_annotation'].description,
    inputSchema: TOOL_DEFS['add_annotation'].inputSchema,
    async handler(toolArgs) {
      try {
        return toolSuccess(await callApi('POST', '/annotations', toolArgs));
      } catch (err) { return toolError(err.message, err.envelope, 'add_annotation'); }
    },
  },
];

// ── MCP Server setup ────────────────────────────────────────────────────────

const server = new Server(
  { name: 'ai-debug-mcp', version: '0.1.0' },
  { capabilities: { tools: {} } },
);

server.setRequestHandler(ListToolsRequestSchema, async () => ({
  tools: TOOLS.map((t) => ({
    name: t.name,
    description: t.description,
    inputSchema: t.inputSchema,
  })),
}));

server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const tool = TOOLS.find((t) => t.name === request.params.name);
  if (!tool) {
    return toolError(`Unknown tool: ${request.params.name}`);
  }
  return tool.handler(request.params.arguments || {});
});

// ── Entry point ─────────────────────────────────────────────────────────────

const transport = new StdioServerTransport();
await server.connect(transport);
process.stderr.write(`[mcp] ai-debug-mcp started (${TOOLS.length} tools)\n`);
