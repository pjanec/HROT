/**
 * tool-catalog.mjs — Single-source-of-truth tool catalog for ai-debug-mcp.
 *
 * Each entry describes one MCP tool: HTTP binding, parameters (mirroring inputSchema
 * exactly), returns, notes, an example, and a short educating-error hint.
 *
 * Generated SKILL.md §4 and the index.mjs inputSchemas/descriptions are both driven
 * from this catalog via generate-skill.mjs and the buildInputSchema/buildDescription
 * helpers in index.mjs.
 */

export const TOOLS_CATALOG = [

  // ── Group A — Lifecycle & status ────────────────────────────────────────────

  {
    name: 'start_simulation',
    group: 'A — Lifecycle & status',
    summary: 'Launch the Hrot ClusterRunner in editor mode with the AI Debug API enabled. Polls /status until ready.',
    http: null,
    params: [
      { name: 'runnerDll', type: 'string', required: false, description: 'Absolute path to Hrot.ClusterRunner.dll (overrides --runner-dll CLI arg)' },
      { name: 'port', type: 'number', required: false, description: 'Debug API port (overrides --port CLI arg). Default: 8099', default: 8099 },
      { name: 'headless', type: 'boolean', required: false, description: 'Pass --headless to the runner. Default: false', default: false },
    ],
    returns: '{ url, pid }',
    notes: [
      'MCP-side lifecycle tool — no HTTP endpoint.',
      'runnerDll is required unless the server was started with --runner-dll.',
    ],
    example: { args: { runnerDll: '/path/to/Hrot.ClusterRunner.dll', port: 8099, headless: true }, gist: 'launch runner headless on default port' },
    hint: 'Required (if no --runner-dll on server): runnerDll (string). Example: start_simulation({runnerDll:"/path/to/dll", headless:true})',
    manualVerify: false,
  },

  {
    name: 'stop_simulation',
    group: 'A — Lifecycle & status',
    summary: 'Shut down the runner gracefully via POST /shutdown, then hard-kill if needed.',
    http: { method: 'POST', path: '/shutdown' },
    params: [],
    returns: 'The /shutdown envelope, or { note: "runner already gone" }',
    notes: [
      'MCP-side lifecycle tool — also calls the /shutdown HTTP endpoint.',
      'Always call when done to avoid orphan runner processes.',
    ],
    example: { args: {}, gist: 'graceful runner shutdown' },
    hint: 'No params. Example: stop_simulation({})',
    manualVerify: false,
  },

  {
    name: 'get_status',
    group: 'A — Lifecycle & status',
    summary: 'Runner liveness + sim state summary.',
    http: { method: 'GET', path: '/status' },
    params: [],
    returns: '{ scenario, clusterState, simTime, timeScale, isPaused, inPreview, entityCount, recording }',
    notes: [
      'Use this to verify the runner is alive and check current run state before driving the sim.',
    ],
    example: { args: {}, gist: 'check runner liveness and sim state' },
    hint: 'No params. Example: get_status({})',
    manualVerify: false,
  },

  // ── Group B — Queries ────────────────────────────────────────────────────────

  {
    name: 'list_entities',
    group: 'B — Queries',
    summary: 'List all entities with networkId, name, and component names.',
    http: { method: 'GET', path: '/entities' },
    params: [
      { name: 'component', type: 'string', required: false, description: 'Filter: only entities that have this component type' },
      { name: 'near', type: 'string', required: false, description: 'Spatial filter: "x,y,r" (comma-separated floats)' },
    ],
    returns: '[{networkId, name, components:[names]}]',
    notes: [
      'Optional filters compose: component (only entities having it), near ("x,y,r" within radius r of (x,y)).',
    ],
    example: { args: { component: 'SimTransform' }, gist: 'list only entities with SimTransform component' },
    hint: 'No required params. Optional: component (string), near ("x,y,r"). Example: list_entities({component:"SimTransform"})',
    manualVerify: false,
  },

  {
    name: 'get_entity',
    group: 'B — Queries',
    summary: 'Full component dump for one entity.',
    http: { method: 'GET', path: '/entities/{networkId}' },
    params: [
      { name: 'networkId', type: 'number', required: true, description: 'Network entity ID (long)' },
    ],
    returns: 'Full component dump for the entity. Non-finite floats render as "NaN"/"Infinity"/"-Infinity".',
    notes: [
      'Non-finite floats appear as string sentinels "NaN"/"Infinity"/"-Infinity" — valid JSON, not a bug.',
    ],
    example: { args: { networkId: 1000 }, gist: 'get full component dump for entity 1000' },
    hint: 'Req: networkId (number/long). Example: get_entity({networkId:1000})',
    manualVerify: false,
  },

  {
    name: 'list_component_types',
    group: 'B — Queries',
    summary: 'Enumerate registered ECS component types with field schemas.',
    http: { method: 'GET', path: '/components' },
    params: [],
    returns: 'All registered component types + field schemas (for use with edit_component).',
    notes: [
      'Use this to discover component type names before calling edit_component.',
    ],
    example: { args: {}, gist: 'list all ECS component types and their schemas' },
    hint: 'No params. Example: list_component_types({})',
    manualVerify: false,
  },

  {
    name: 'list_scenarios',
    group: 'B — Queries',
    summary: 'List available scenarios by relative path.',
    http: { method: 'GET', path: '/scenarios' },
    params: [],
    returns: 'Available scenario names (relative paths) for use with load_scenario.',
    notes: [],
    example: { args: {}, gist: 'discover loadable scenario names' },
    hint: 'No params. Example: list_scenarios({})',
    manualVerify: false,
  },

  // ── Group C — Event history ──────────────────────────────────────────────────

  {
    name: 'get_event_history',
    group: 'C — Event history',
    summary: 'Query the diagnostic event history.',
    http: { method: 'GET', path: '/events' },
    params: [
      { name: 'bus', type: 'string', required: false, description: 'Event bus to query', enum: ['world', 'orchestration'], default: 'world' },
      { name: 'type', type: 'string', required: false, description: 'Filter by event type name' },
      { name: 'since', type: 'number', required: false, description: 'Return events since this frame number' },
      { name: 'max', type: 'number', required: false, description: 'Maximum events to return (default 200)', default: 200 },
    ],
    returns: 'Recent diagnostic events from the specified bus.',
    notes: [
      'bus: "world" (default) or "orchestration".',
      'Read-only; safe to call any time.',
    ],
    example: { args: { bus: 'world', type: 'CenterOnEntityCommand', max: 10 }, gist: 'query world bus for recent CenterOnEntityCommand events' },
    hint: 'No required params. Optional: bus ("world"|"orchestration"), type (string), since (frame), max (number). Example: get_event_history({bus:"world",max:50})',
    manualVerify: false,
  },

  // ── Group D — Sim / preview / time ──────────────────────────────────────────

  {
    name: 'get_sim_state',
    group: 'D — Sim / preview / time',
    summary: 'Current sim state: isPaused, inPreview, totalTime, timeScale.',
    http: { method: 'GET', path: '/sim/state' },
    params: [],
    returns: '{ isPaused, inPreview, totalTime, timeScale }',
    notes: [
      'Check this before driving — most mistakes are run-state mistakes.',
    ],
    example: { args: {}, gist: 'check current paused/preview/time state' },
    hint: 'No params. Example: get_sim_state({})',
    manualVerify: false,
  },

  {
    name: 'play',
    group: 'D — Sim / preview / time',
    summary: 'Enter preview and/or resume if paused. Time advances after this.',
    http: { method: 'POST', path: '/sim/play' },
    params: [],
    returns: 'ok:true envelope.',
    notes: [
      'Time advances after play (until pause or a breakpoint fires).',
    ],
    example: { args: {}, gist: 'start or resume simulation' },
    hint: 'No params. Example: play({})',
    manualVerify: false,
  },

  {
    name: 'pause',
    group: 'D — Sim / preview / time',
    summary: 'Pause the simulation. Time freezes; commands queue until step/play.',
    http: { method: 'POST', path: '/sim/pause' },
    params: [],
    returns: 'ok:true envelope.',
    notes: [
      'Commands and spawns while paused are queued and take effect on the next step/play.',
    ],
    example: { args: {}, gist: 'pause the running simulation' },
    hint: 'No params. Example: pause({})',
    manualVerify: false,
  },

  {
    name: 'step',
    group: 'D — Sim / preview / time',
    summary: 'Advance simulation by N discrete steps. Only meaningful in preview.',
    http: { method: 'POST', path: '/sim/step' },
    params: [
      { name: 'count', type: 'number', required: false, description: 'Number of steps to advance (default 1)', default: 1 },
    ],
    returns: 'ok:true envelope.',
    notes: [
      'Only advances time when inPreview==true. In Edit state this is a no-op.',
    ],
    example: { args: { count: 5 }, gist: 'advance 5 simulation ticks' },
    hint: 'No required params. Optional: count (number, def 1). Example: step({count:5})',
    manualVerify: false,
  },

  {
    name: 'set_time_scale',
    group: 'D — Sim / preview / time',
    summary: 'Set simulation time scale.',
    http: { method: 'POST', path: '/sim/timescale' },
    params: [
      { name: 'scale', type: 'number', required: true, description: 'Time scale factor (1.0 = real-time)' },
    ],
    returns: 'ok:true envelope.',
    notes: [
      '1.0 = real-time, >1.0 = faster, <1.0 = slower.',
    ],
    example: { args: { scale: 2.0 }, gist: 'run simulation at 2x real-time' },
    hint: 'Req: scale (number, 1.0=real-time). Example: set_time_scale({scale:2.0})',
    manualVerify: false,
  },

  {
    name: 'enter_preview',
    group: 'D — Sim / preview / time',
    summary: 'Enter preview mode. Snapshots the world (revertible via stop_preview).',
    http: { method: 'POST', path: '/preview/enter' },
    params: [
      { name: 'startPaused', type: 'boolean', required: false, description: 'Start preview in paused state' },
    ],
    returns: 'ok:true envelope.',
    notes: [
      'Snapshots the world; stop_preview rewinds to this snapshot.',
      'Single preview slot — mutually exclusive with checkpoint and start_recording{preview}.',
    ],
    example: { args: { startPaused: true }, gist: 'enter preview paused for deterministic step-based control' },
    hint: 'No required params. Optional: startPaused (bool). Example: enter_preview({startPaused:true})',
    manualVerify: false,
  },

  {
    name: 'stop_preview',
    group: 'D — Sim / preview / time',
    summary: 'Exit preview mode; rewinds to the pre-preview snapshot.',
    http: { method: 'POST', path: '/preview/exit' },
    params: [],
    returns: 'ok:true envelope.',
    notes: [
      'Rewinds all changes made during preview back to the snapshot taken at enter_preview.',
    ],
    example: { args: {}, gist: 'exit preview and revert all changes since entering preview' },
    hint: 'No params. Example: stop_preview({})',
    manualVerify: false,
  },

  // ── Group E — Scenario ───────────────────────────────────────────────────────

  {
    name: 'load_scenario',
    group: 'E — Scenario',
    summary: 'Load a scenario by name. Puts the world into Edit state.',
    http: { method: 'POST', path: '/scenario/load' },
    params: [
      { name: 'name', type: 'string', required: true, description: 'Scenario name (relative path)' },
      { name: 'waitForReady', type: 'boolean', required: false, description: 'Wait for cluster to reach OperatingEdit before returning', default: false },
    ],
    returns: 'ok:true envelope.',
    notes: [
      'Set waitForReady:true to block until the cluster reaches OperatingEdit (recommended).',
      'Loads into Edit state — sim is static until enter_preview or play.',
    ],
    example: { args: { name: 'test-move', waitForReady: true }, gist: 'load test-move scenario and wait for ready' },
    hint: 'Req: name (string). Optional: waitForReady (bool, use true). Example: load_scenario({name:"test-move",waitForReady:true})',
    manualVerify: false,
  },

  {
    name: 'save_scenario',
    group: 'E — Scenario',
    summary: 'Save the current authored world as a scenario.',
    http: { method: 'POST', path: '/scenario/save' },
    params: [
      { name: 'name', type: 'string', required: true, description: 'Scenario file name to save as' },
    ],
    returns: 'ok:true envelope.',
    notes: [],
    example: { args: { name: 'my-scenario' }, gist: 'save current world as my-scenario' },
    hint: 'Req: name (string). Example: save_scenario({name:"my-scenario"})',
    manualVerify: false,
  },

  // ── Group F — Commands, discovery, spawn ────────────────────────────────────

  {
    name: 'list_commands',
    group: 'F — Commands, discovery, spawn',
    summary: 'Enumerate publishable FDP event types with field schemas.',
    http: { method: 'GET', path: '/commands' },
    params: [],
    returns: 'Publishable FDP event types + field schemas; each tagged managed:true/false.',
    notes: [
      'Call this to discover what send_entity_command accepts.',
      'managed:true events have server-side handling; managed:false are raw FDP events.',
    ],
    example: { args: {}, gist: 'discover available FDP event types before sending a command' },
    hint: 'No params. Example: list_commands({})',
    manualVerify: false,
  },

  {
    name: 'send_entity_command',
    group: 'F — Commands, discovery, spawn',
    summary: 'Publish an FDP event by type name.',
    http: { method: 'POST', path: '/entities/command' },
    params: [
      { name: 'eventType', type: 'string', required: true, description: 'FDP event type name (e.g. MissionControlIntent)' },
      { name: 'payload', type: 'object', required: false, description: 'Event fields as JSON object' },
      { name: 'wait', type: 'boolean', required: false, description: 'Attempt to wait for correlated ack' },
    ],
    returns: 'ok:true envelope. awaited:false if sim not running (not an error).',
    notes: [
      'Set wait:true to attempt correlated-ack wait — only effective while time advances, else awaited:false.',
      'awaited:false is NOT an error — it means time was not advancing.',
    ],
    example: { args: { eventType: 'MissionControlIntent', payload: { targetId: 1000 }, wait: false }, gist: 'publish MissionControlIntent event' },
    hint: 'Req: eventType (string from list_commands). Optional: payload (object), wait (bool). Example: send_entity_command({eventType:"MissionControlIntent",payload:{}})',
    manualVerify: false,
  },

  {
    name: 'spawn_entity',
    group: 'F — Commands, discovery, spawn',
    summary: 'Spawn an entity from a TKB type.',
    http: { method: 'POST', path: '/entities/spawn' },
    params: [
      { name: 'tkbType', type: 'number', required: true, description: 'TKB type ID (long)' },
      { name: 'transform', type: 'object', required: false, description: 'Transform: { position: {x,y,z}, rotation: {x,y,z,w} }' },
      { name: 'components', type: 'array', required: false, description: 'Additional component overrides' },
      { name: 'attributesJson', type: 'string', required: false, description: 'JSON string of attribute overrides (JsonAttributeCompiler patch)' },
    ],
    returns: 'ok:true envelope. Spawn is processed on the next tick (step to realize it).',
    notes: [
      'Spawn is queued and processed on the next tick — call step to realize it.',
      'Use list_entity_types to discover valid tkbType values.',
    ],
    example: { args: { tkbType: 1001, transform: { position: { x: 100, y: 0, z: 50 }, rotation: { x: 0, y: 0, z: 0, w: 1 } } }, gist: 'spawn entity type 1001 at position (100,0,50)' },
    hint: 'Req: tkbType (number/long from list_entity_types). Optional: transform ({position,rotation}), components (array), attributesJson (string). Example: spawn_entity({tkbType:1001})',
    manualVerify: false,
  },

  // ── Group G — Breakpoints ────────────────────────────────────────────────────

  {
    name: 'set_breakpoint',
    group: 'G — Breakpoints',
    summary: 'Register a run-until-condition breakpoint.',
    http: { method: 'POST', path: '/breakpoints' },
    params: [
      {
        name: 'condition',
        type: 'object',
        required: true,
        description: 'SearchPredicateDto with $type discriminator (e.g. {"$type":"Lifecycle","IdentifierType":"NameSubstring","TargetValue":"Alpha","NamePropertyPath":"Name"})',
      },
      { name: 'filterNetworkId', type: 'number', required: false, description: 'Optional: only trigger for this entity (network ID)' },
      { name: 'occurrenceThreshold', type: 'number', required: false, description: 'Number of hits before pausing (default 1)', default: 1 },
      { name: 'name', type: 'string', required: false, description: 'Human-readable label for the breakpoint' },
    ],
    returns: '{ breakpointId } (e.g. "BP#1").',
    notes: [
      'condition is a polymorphic SearchPredicateDto JSON object (use $type discriminator: Lifecycle, PropertyMatch, TransientEvent, Compound, Structural, SpatialBounding, etc.).',
      'Poll get_breakpoint_status after play to detect when the breakpoint fires.',
    ],
    example: {
      args: {
        condition: { '$type': 'PropertyMatch', ComponentType: 'SimTransform', PropertyPath: 'Position.X', Operator: 'GreaterThan', Predicate: { '$type': 'Numeric', MinValue: 100, MaxValue: 1e9 } },
        name: 'moved-east',
      },
      gist: 'pause when entity SimTransform.Position.X > 100',
    },
    hint: 'Req: condition (SearchPredicateDto with $type). Optional: filterNetworkId, occurrenceThreshold, name. Example: set_breakpoint({condition:{"$type":"Lifecycle",...}})',
    manualVerify: false,
  },

  {
    name: 'list_breakpoints',
    group: 'G — Breakpoints',
    summary: 'List all registered breakpoints.',
    http: { method: 'GET', path: '/breakpoints' },
    params: [],
    returns: '[{ id, conditionSummary, enabled, occurrenceThreshold, hitCount, name }]',
    notes: [],
    example: { args: {}, gist: 'list all active breakpoints and their hit counts' },
    hint: 'No params. Example: list_breakpoints({})',
    manualVerify: false,
  },

  {
    name: 'remove_breakpoint',
    group: 'G — Breakpoints',
    summary: 'Remove a breakpoint by its ID string.',
    http: { method: 'DELETE', path: '/breakpoints/{id}' },
    params: [
      { name: 'id', type: 'string', required: true, description: 'Breakpoint ID string (e.g. "BP#1" from set_breakpoint or list_breakpoints)' },
    ],
    returns: 'ok:true envelope.',
    notes: [],
    example: { args: { id: 'BP#1' }, gist: 'remove breakpoint BP#1' },
    hint: 'Req: id (string, e.g. "BP#1" from set_breakpoint). Example: remove_breakpoint({id:"BP#1"})',
    manualVerify: false,
  },

  {
    name: 'get_breakpoint_status',
    group: 'G — Breakpoints',
    summary: 'Current pause state and last breakpoint hit.',
    http: { method: 'GET', path: '/breakpoints/hits' },
    params: [],
    returns: '{ isPaused, pausedTick, lastHit: { breakpointId, networkId } | null }',
    notes: [
      'Poll this after play to detect when a breakpoint fires.',
    ],
    example: { args: {}, gist: 'poll for breakpoint hit after calling play' },
    hint: 'No params. Example: get_breakpoint_status({})',
    manualVerify: false,
  },

  // ── Group H — Checkpoint / diff ─────────────────────────────────────────────

  {
    name: 'checkpoint',
    group: 'H — Checkpoint / diff',
    summary: 'Take a single-slot RAM snapshot via IPreviewController.EnterPreviewMode(startPaused:true).',
    http: { method: 'POST', path: '/checkpoint' },
    params: [],
    returns: 'ok:true with inPreview:true. Returns 409 if a live run is active; 400 if already in preview/checkpointed.',
    notes: [
      'Single slot: mutually exclusive with enter_preview and start_recording{preview}.',
      'Restore with restore_checkpoint to rewind all changes.',
    ],
    example: { args: {}, gist: 'take a checkpoint before an experiment' },
    hint: 'No params. Must NOT be in preview. Example: checkpoint({})',
    manualVerify: false,
  },

  {
    name: 'restore_checkpoint',
    group: 'H — Checkpoint / diff',
    summary: 'Rewind the simulation to the checkpointed state via IPreviewController.ExitPreviewMode().',
    http: { method: 'POST', path: '/checkpoint/restore' },
    params: [],
    returns: 'ok:true with inPreview:false. Returns 400 if no checkpoint is active.',
    notes: [
      'Returns 400 if no checkpoint is active.',
    ],
    example: { args: {}, gist: 'revert all changes since the last checkpoint' },
    hint: 'No params. Requires an active checkpoint. Example: restore_checkpoint({})',
    manualVerify: false,
  },

  {
    name: 'capture_diff_baseline',
    group: 'H — Checkpoint / diff',
    summary: 'Serialize current entity states server-side and return a baselineId.',
    http: { method: 'POST', path: '/diff/capture' },
    params: [
      {
        name: 'entities',
        type: 'array',
        required: false,
        description: 'Optional list of networkIds to capture (default: all entities)',
        items: { type: 'number' },
      },
    ],
    returns: '{ baselineId } (e.g. "BL#1")',
    notes: [
      'Use before mutating the world, then call diff_state with the baselineId to see what changed.',
      'Optional entities array (networkId list) scopes which entities to capture (default: all).',
    ],
    example: { args: { entities: [1000] }, gist: 'capture baseline for entity 1000 before mutation' },
    hint: 'No required params. Optional: entities (array of networkIds). Example: capture_diff_baseline({entities:[1000]})',
    manualVerify: false,
  },

  {
    name: 'diff_state',
    group: 'H — Checkpoint / diff',
    summary: 'Compare a previously captured baseline against current entity state.',
    http: { method: 'POST', path: '/diff/compare' },
    params: [
      { name: 'baselineId', type: 'string', required: true, description: 'Baseline ID from capture_diff_baseline (e.g. "BL#1")' },
      {
        name: 'entities',
        type: 'array',
        required: false,
        description: 'Optional list of networkIds to diff (default: all entities in baseline)',
        items: { type: 'number' },
      },
    ],
    returns: 'A DiffNode tree showing only what changed (token-efficient). Includes entity births/deaths.',
    notes: [
      'baselineId comes from capture_diff_baseline.',
      'Returns only changed components — token-efficient for AI consumption.',
    ],
    example: { args: { baselineId: 'BL#1', entities: [1000] }, gist: 'diff entity 1000 against baseline BL#1' },
    hint: 'Req: baselineId (string from capture_diff_baseline). Optional: entities (array). Example: diff_state({baselineId:"BL#1"})',
    manualVerify: false,
  },

  // ── Group I — Recording / replay ────────────────────────────────────────────

  {
    name: 'start_recording',
    group: 'I — Recording / replay',
    summary: 'Start recording. Enters preview and begins writing a .fdp file.',
    http: { method: 'POST', path: '/recording/start' },
    params: [
      {
        name: 'mode',
        type: 'string',
        required: false,
        description: 'Recording mode: "preview" (revertible) or "live" (not supported in editor mode). Default: "preview"',
        enum: ['preview', 'live'],
        default: 'preview',
      },
    ],
    returns: '{ recording:true, mode, fdpPath }',
    notes: [
      'mode="preview" (default): revertible, uses EnterPreviewMode→PrepareRecordingAsync.',
      'mode="live": not supported in editor mode.',
      'Mutually exclusive with checkpoint (both use the preview slot).',
    ],
    example: { args: { mode: 'preview' }, gist: 'start a revertible preview recording' },
    hint: 'No required params. Optional: mode ("preview"|"live", def "preview"). Example: start_recording({mode:"preview"})',
    manualVerify: false,
  },

  {
    name: 'stop_recording',
    group: 'I — Recording / replay',
    summary: 'Stop the active recording. Finalizes BEFORE the exit rewind.',
    http: { method: 'POST', path: '/recording/stop' },
    params: [],
    returns: '{ recording:false, fdpPath }',
    notes: [
      'For preview mode: finalizes BEFORE the exit rewind (hard ordering rule).',
    ],
    example: { args: {}, gist: 'stop recording and get the .fdp file path' },
    hint: 'No params. Example: stop_recording({})',
    manualVerify: false,
  },

  {
    name: 'load_replay',
    group: 'I — Recording / replay',
    summary: 'Load a .fdp recording into an ISOLATED ReplayBrowserContext.',
    http: { method: 'POST', path: '/replay/load' },
    params: [
      { name: 'fdpPath', type: 'string', required: true, description: 'Absolute path to the .fdp recording file' },
    ],
    returns: '{ loaded:true, fdpPath, totalFrames, currentFrame }',
    notes: [
      'While replay is active, /replay/entities returns entities from the sandbox (not the live world).',
      'Use list_replay_entities (not list_entities) while replaying.',
    ],
    example: { args: { fdpPath: '/path/to/recording.fdp' }, gist: 'load a .fdp recording for inspection' },
    hint: 'Req: fdpPath (string, absolute path to .fdp file). Example: load_replay({fdpPath:"/path/to/recording.fdp"})',
    manualVerify: false,
  },

  {
    name: 'seek_replay',
    group: 'I — Recording / replay',
    summary: 'Seek to a specific frame in the ISOLATED sandbox. Does NOT touch the live world.',
    http: { method: 'POST', path: '/replay/seek' },
    params: [
      { name: 'frame', type: 'number', required: true, description: 'Frame index to seek to (0-based)' },
    ],
    returns: '{ frame, totalFrames }',
    notes: [
      'Isolation guarantee: does NOT touch the live world.',
    ],
    example: { args: { frame: 0 }, gist: 'seek replay to frame 0 (start)' },
    hint: 'Req: frame (number, 0-based). Example: seek_replay({frame:0})',
    manualVerify: false,
  },

  {
    name: 'step_replay',
    group: 'I — Recording / replay',
    summary: 'Step one frame forward or backward in the ISOLATED sandbox. Does NOT touch the live world.',
    http: { method: 'POST', path: '/replay/step' },
    params: [
      {
        name: 'dir',
        type: 'string',
        required: false,
        description: 'Step direction: "forward" or "back". Default: "forward"',
        enum: ['forward', 'back'],
        default: 'forward',
      },
    ],
    returns: '{ stepped:bool, frame, totalFrames }',
    notes: [
      'Isolation guarantee: does NOT touch the live world.',
    ],
    example: { args: { dir: 'forward' }, gist: 'step one frame forward in the replay' },
    hint: 'No required params. Optional: dir ("forward"|"back", def "forward"). Example: step_replay({dir:"forward"})',
    manualVerify: false,
  },

  {
    name: 'get_replay_status',
    group: 'I — Recording / replay',
    summary: 'Replay sandbox status.',
    http: { method: 'GET', path: '/replay/status' },
    params: [],
    returns: '{ replayActive, currentFrame, totalFrames }',
    notes: [],
    example: { args: {}, gist: 'check if replay is active and current frame' },
    hint: 'No params. Example: get_replay_status({})',
    manualVerify: false,
  },

  {
    name: 'list_replay_entities',
    group: 'I — Recording / replay',
    summary: 'List entities from the ISOLATED replay sandbox at the current frame.',
    http: { method: 'GET', path: '/replay/entities' },
    params: [],
    returns: 'Same schema as list_entities but from the sandbox repo, NOT the live world.',
    notes: [
      'Requires an active replay (call load_replay first).',
      'Does not touch or affect the live world.',
    ],
    example: { args: {}, gist: 'inspect entities at current replay frame' },
    hint: 'No params. Requires load_replay first. Example: list_replay_entities({})',
    manualVerify: false,
  },

  {
    name: 'unload_replay',
    group: 'I — Recording / replay',
    summary: 'Dispose the replay sandbox and return to live world queries.',
    http: { method: 'POST', path: '/replay/unload' },
    params: [],
    returns: 'ok:true envelope.',
    notes: [],
    example: { args: {}, gist: 'unload replay sandbox when done inspecting' },
    hint: 'No params. Example: unload_replay({})',
    manualVerify: false,
  },

  // ── Group J — Logs ───────────────────────────────────────────────────────────

  {
    name: 'get_logs',
    group: 'J — Logs',
    summary: 'Query the in-process log sinks. Returns [{timestamp, level, logger, message}] sorted newest-first.',
    http: { method: 'GET', path: '/logs' },
    params: [
      {
        name: 'level',
        type: 'string',
        required: false,
        description: 'Minimum severity level (inclusive). Omit to return all levels.',
        enum: ['Trace', 'Debug', 'Info', 'Warning', 'Error', 'Critical'],
      },
      { name: 'logger', type: 'string', required: false, description: 'Filter by logger name substring (case-insensitive). Omit to return all loggers.' },
      { name: 'since', type: 'string', required: false, description: 'ISO-8601 timestamp. Only entries with timestamp >= since are returned.' },
      { name: 'max', type: 'number', required: false, description: 'Maximum number of entries to return (default 200).', default: 200 },
    ],
    returns: '[{timestamp, level, logger, message}] sorted newest-first.',
    notes: [
      'level = minimum severity (inclusive): Trace, Debug, Info, Warning, Error, Critical.',
      'logger = case-insensitive substring match on logger name.',
      'since = ISO-8601 timestamp; entries with timestamp >= since are included.',
      'Read off-thread — no main-thread marshal required.',
    ],
    example: { args: { level: 'Warning', max: 50 }, gist: 'get last 50 Warning-or-higher log entries' },
    hint: 'No required params. Optional: level (Trace|Debug|Info|Warning|Error|Critical), logger (string), since (ISO-8601), max (number). Example: get_logs({level:"Warning"})',
    manualVerify: false,
  },

  // ── Group K — AI behavior traces ─────────────────────────────────────────────

  {
    name: 'observe_trace',
    group: 'K — AI behavior traces',
    summary: 'Arm or disarm AI behavior trace buffer allocation for an entity.',
    http: { method: 'POST', path: '/trace/observe' },
    params: [
      { name: 'networkId', type: 'number', required: true, description: 'Network entity ID (long)' },
      { name: 'on', type: 'boolean', required: true, description: 'true to arm tracing, false to disarm' },
    ],
    returns: '{ armed, networkId }',
    notes: [
      'Must arm before get_entity_trace will return populated trace data.',
      'Without arming, get_entity_trace returns empty trace.',
    ],
    example: { args: { networkId: 1000, on: true }, gist: 'arm AI behavior tracing for entity 1000' },
    hint: 'Req: networkId (number), on (bool). Example: observe_trace({networkId:1000,on:true})',
    manualVerify: false,
  },

  {
    name: 'get_entity_trace',
    group: 'K — AI behavior traces',
    summary: 'Extract AI behavior trace for an entity.',
    http: { method: 'GET', path: '/entities/{networkId}/trace' },
    params: [
      { name: 'networkId', type: 'number', required: true, description: 'Network entity ID (long)' },
    ],
    returns: 'BTree active node path + history, HSM active leaves, or blueprint live state. Includes traceArmed flag.',
    notes: [
      'Arm the entity with observe_trace first to populate trace data.',
      'Returns tier field indicating the AI tier type (BTree/HSM/blueprint).',
    ],
    example: { args: { networkId: 1000 }, gist: 'read AI behavior trace for entity 1000 after arming' },
    hint: 'Req: networkId (number). Must call observe_trace({networkId,on:true}) first. Example: get_entity_trace({networkId:1000})',
    manualVerify: false,
  },

  // ── Group L — Mutation / fault injection ────────────────────────────────────

  {
    name: 'get_attributes_schema',
    group: 'L — Mutation / fault injection',
    summary: 'Return all patchable attribute paths and their JSON Schema.',
    http: { method: 'GET', path: '/attributes/schema' },
    params: [],
    returns: '{ registeredPaths, schema } — the discoverable, authority-aware patch paths (Name, Affiliation, GeoPosition.*, Heading, …).',
    notes: [
      'Use patch_attribute to apply a patch using these paths.',
      'Paths not in registeredPaths are silently ignored by patch_attribute.',
    ],
    example: { args: {}, gist: 'discover patchable attribute paths before calling patch_attribute' },
    hint: 'No params. Example: get_attributes_schema({})',
    manualVerify: false,
  },

  {
    name: 'patch_attribute',
    group: 'L — Mutation / fault injection',
    summary: 'Apply a JSON attribute patch to an entity.',
    http: { method: 'POST', path: '/entities/{networkId}/attribute' },
    params: [
      { name: 'networkId', type: 'number', required: true, description: 'Network entity ID (long)' },
      {
        name: 'patchJson',
        type: undefined,
        required: true,
        description: 'Patch as a JSON object {"Name":"Alpha"} or as a JSON string',
      },
    ],
    returns: 'Updated entity dump on success.',
    notes: [
      'Authority-aware; unregistered keys are silently ignored (no error).',
      'patchJson may be a nested JSON object like {"Name":"Alpha"} or a JSON string.',
    ],
    example: { args: { networkId: 1000, patchJson: { Name: 'Alpha' } }, gist: 'rename entity 1000 to Alpha' },
    hint: 'Req: networkId (number), patchJson (object {"Name":"Alpha"} or JSON string). Example: patch_attribute({networkId:1000,patchJson:{Name:"Alpha"}})',
    manualVerify: false,
  },

  {
    name: 'edit_component',
    group: 'L — Mutation / fault injection',
    summary: 'StructEdit escape hatch for arbitrary component fields.',
    http: { method: 'POST', path: '/entities/{networkId}/component' },
    params: [
      { name: 'networkId', type: 'number', required: true, description: 'Network entity ID (long)' },
      { name: 'componentType', type: 'string', required: true, description: 'ECS component type name (e.g. "EntityInfo", "SimTransform")' },
      { name: 'patch', type: 'object', required: true, description: 'JSON object with field names and new values to apply to the component' },
    ],
    returns: 'Updated entity component state. Invalid values → 400, component unchanged.',
    notes: [
      'Opens a StructEdit session, applies the patch fields, validates via IComponentValidator, and writes the result back to ECS.',
      'Invalid values → 400, component unchanged.',
      'For fields registered in the attribute schema, prefer patch_attribute.',
    ],
    example: { args: { networkId: 1000, componentType: 'SimTransform', patch: { Position: { X: 999.0, Y: 0.0, Z: 0.0 } } }, gist: 'set SimTransform Position.X to 999 for entity 1000' },
    hint: 'Req: networkId (number), componentType (string from list_component_types), patch (object). Example: edit_component({networkId:1000,componentType:"SimTransform",patch:{...}})',
    manualVerify: false,
  },

  // ── Group M (TKB) — Entity-type catalog ─────────────────────────────────────

  {
    name: 'list_entity_types',
    group: 'M (TKB) — Entity-type catalog',
    summary: 'List entity types (TKB templates) with id, name, category, disType.',
    http: { method: 'GET', path: '/tkb/types' },
    params: [
      { name: 'category', type: 'string', required: false, description: 'Filter by category path' },
    ],
    returns: '[{tkbType, name, categoryPath, disType}]',
    notes: [],
    example: { args: { category: 'Vehicle' }, gist: 'list all TKB types in the Vehicle category' },
    hint: 'No required params. Optional: category (string). Example: list_entity_types({})',
    manualVerify: false,
  },

  {
    name: 'get_entity_type',
    group: 'M (TKB) — Entity-type catalog',
    summary: 'Full TKB descriptor: mandatory components, child blueprints, DIS type, and descriptor DTOs.',
    http: { method: 'GET', path: '/tkb/types/{tkbType}' },
    params: [
      { name: 'tkbType', type: 'number', required: true, description: 'TKB type ID (long)' },
    ],
    returns: 'Full TKB descriptor including mandatory components, child blueprints, descriptors. No spawn.',
    notes: [],
    example: { args: { tkbType: 1001 }, gist: 'inspect TKB descriptor for type 1001' },
    hint: 'Req: tkbType (number/long from list_entity_types). Example: get_entity_type({tkbType:1001})',
    manualVerify: false,
  },

  // ── Group M (Focus/Annotations) — Focus / annotations ───────────────────────

  {
    name: 'focus_entity',
    group: 'O — Manual-assist (focus / annotations)',
    summary: 'Pan and zoom the map canvas to an entity. MANUAL-VERIFY: camera move requires windowed session.',
    http: { method: 'POST', path: '/entities/{networkId}/focus' },
    params: [
      { name: 'networkId', type: 'number', required: true, description: 'Network entity ID to center the view on' },
    ],
    returns: '{ focused: true } on success.',
    notes: [
      'Publishes CenterOnEntityCommand (headless-verifiable via event history).',
      'The actual camera move only occurs in a windowed session (MANUAL-VERIFY).',
    ],
    example: { args: { networkId: 1000 }, gist: 'center editor camera on entity 1000' },
    hint: 'Req: networkId (number). Example: focus_entity({networkId:1000})',
    manualVerify: true,
  },

  {
    name: 'add_annotation',
    group: 'O — Manual-assist (focus / annotations)',
    summary: 'Draw a debug primitive (sphere, anchor, or line) in the gizmo buffer. MANUAL-VERIFY: gizmo render requires windowed session.',
    http: { method: 'POST', path: '/annotations' },
    params: [
      { name: 'type', type: 'string', required: true, description: 'Annotation type', enum: ['sphere', 'anchor', 'line'] },
      { name: 'networkId', type: 'number', required: false, description: 'Entity network ID (anchor only)' },
      { name: 'x', type: 'number', required: false, description: 'World X coordinate' },
      { name: 'y', type: 'number', required: false, description: 'World Y coordinate' },
      { name: 'z', type: 'number', required: false, description: 'World Z coordinate' },
      { name: 'radius', type: 'number', required: false, description: 'Sphere radius in metres' },
      { name: 'heading', type: 'number', required: false, description: 'Heading in degrees (anchor)' },
      { name: 'color', type: 'string', required: false, description: 'Hex color string e.g. "#FF0000"' },
      {
        name: 'from',
        type: 'object',
        required: false,
        description: 'Line start point {x,y,z}',
        properties: { x: { type: 'number' }, y: { type: 'number' }, z: { type: 'number' } },
      },
      {
        name: 'to',
        type: 'object',
        required: false,
        description: 'Line end point {x,y,z}',
        properties: { x: { type: 'number' }, y: { type: 'number' }, z: { type: 'number' } },
      },
    ],
    returns: '{ added: true, primitiveIndex, bufferCount } on success.',
    notes: [
      '"sphere" — x, y, z, radius (float), optional color (hex "#RRGGBB").',
      '"anchor" — networkId, x, y, z, optional heading (float).',
      '"line" — from:{x,y,z}, to:{x,y,z}, optional color.',
      'The buffer write is headless-verifiable; the actual gizmo render requires a windowed session (MANUAL-VERIFY).',
    ],
    example: { args: { type: 'sphere', x: 100, y: 0, z: 50, radius: 10, color: '#FF4400' }, gist: 'draw a red sphere at (100,0,50) with radius 10' },
    hint: 'Req: type ("sphere"|"anchor"|"line"). For sphere: x,y,z,radius. For line: from:{x,y,z},to:{x,y,z}. Example: add_annotation({type:"sphere",x:0,y:0,z:0,radius:5})',
    manualVerify: true,
  },

  // ── Group N — World / coordinates ───────────────────────────────────────────

  {
    name: 'get_world_info',
    group: 'N — World / coordinates',
    summary: 'World metadata: geo origin, spatial grid extent. terrain and navmesh are null in editor mode.',
    http: { method: 'GET', path: '/world/info' },
    params: [],
    returns: '{ geo:{origin:{lat,lon,alt}}, spatialGrid:{...extent}, terrain:null, navmesh:null }',
    notes: [
      'terrain and navmesh are null in editor mode.',
    ],
    example: { args: {}, gist: 'get world geo origin and spatial grid extent' },
    hint: 'No params. Example: get_world_info({})',
    manualVerify: false,
  },

  {
    name: 'geo_to_local',
    group: 'N — World / coordinates',
    summary: 'Convert geographic coordinates to local ENU {x,y,z}.',
    http: { method: 'POST', path: '/world/geo-to-local' },
    params: [
      { name: 'lat', type: 'number', required: true, description: 'Latitude (degrees)' },
      { name: 'lon', type: 'number', required: true, description: 'Longitude (degrees)' },
      { name: 'alt', type: 'number', required: true, description: 'Altitude (meters)' },
      { name: 'headingDeg', type: 'number', required: false, description: 'Optional heading (degrees CW from North) → rotation quaternion' },
    ],
    returns: '{ x, y, z, rotation? } — optional rotation if headingDeg was provided.',
    notes: [
      'Optional headingDeg → adds rotation quaternion to response.',
    ],
    example: { args: { lat: 50.0755, lon: 14.4378, alt: 200 }, gist: 'convert Prague geo coords to local ECS metres' },
    hint: 'Req: lat, lon, alt (all numbers). Optional: headingDeg (number). Example: geo_to_local({lat:50.0,lon:14.0,alt:200})',
    manualVerify: false,
  },

  {
    name: 'local_to_geo',
    group: 'N — World / coordinates',
    summary: 'Convert local ENU {x,y,z} to geographic coordinates.',
    http: { method: 'POST', path: '/world/local-to-geo' },
    params: [
      { name: 'x', type: 'number', required: true, description: 'Local X (meters East)' },
      { name: 'y', type: 'number', required: true, description: 'Local Y (meters Up)' },
      { name: 'z', type: 'number', required: true, description: 'Local Z (meters North)' },
      {
        name: 'rotation',
        type: 'object',
        required: false,
        description: 'Optional quaternion {x,y,z,w} → headingDeg in response',
        properties: {
          x: { type: 'number' }, y: { type: 'number' },
          z: { type: 'number' }, w: { type: 'number' },
        },
      },
    ],
    returns: '{ lat, lon, alt, headingDeg? } — Heading: North=0°, East=90°.',
    notes: [
      'Optional rotation quaternion {x,y,z,w} → adds headingDeg to response.',
      'Heading convention: North=0°, East=90°.',
    ],
    example: { args: { x: 100, y: 0, z: 50 }, gist: 'convert local ECS position (100,0,50) to geographic coords' },
    hint: 'Req: x, y, z (all numbers). Optional: rotation ({x,y,z,w}). Example: local_to_geo({x:100,y:0,z:50})',
    manualVerify: false,
  },
];
