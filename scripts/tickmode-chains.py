#!/usr/bin/env python3
"""CE-3293 (R-3036) — the event-chain gate of docs/blueprints/DESIGN_Paused_And_Stepped_Execution.md §10 F2.

For every event type, find the production files that PUBLISH it and the ones that READ it, and tag each file with the [TickMode]
of the system(s) it declares — or, for code that declares no system, with its role (editor/UI, network, simulation helper, other).

A chain where a step-only (WhenTimeAdvances) system reads a type that something NOT step-only publishes is legal — the step window
delivers it at the next step — but it is exactly the shape that broke the load-time AI (F2: a unit loaded while paused had no
behaviour until the first step). So every such type must be REVIEWED: listed in ALLOW below with why the delay is right.
A new, unreviewed one fails the gate.

    python3 scripts/tickmode-chains.py            # report
    python3 scripts/tickmode-chains.py --check    # exit 1 on an unreviewed chain (or a stale allow-list entry)
"""
import os, re, subprocess, sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# ⭐ Reviewed 2026-10-21 (design §10 F2). Each: why the step-only reader may take this event at the next step.
ALLOW = {
    # network ingress: simulation events from other nodes — the step window delivers them to the next step
    'DangerAreaResultEvent':      'network ingress of solver answers',
    'DetonationNotification':     'network ingress of remote bursts',
    'EqsResultUpdateEvent':       'network ingress of EQS answers',
    'MissileWarningEvent':        'network ingress of remote launches',
    'RaycastResultEvent':         'network ingress of raycast answers',
    'RoundImpactNotification':    'network ingress of remote impacts',
    'SensorTrackStateEvent':      'network ingress of sensor tracks',
    'SoundContactEvent':          'network ingress of heard sounds',
    'SquadReportNotification':    'network ingress of squad reports',
    'WeaponFireNotification':     'network ingress of remote shots',
    'WorldSurveyResultEvent':     'network ingress of survey answers',
    'DamageAssessedEvent':        'network ingress + EmbarkedStateSystem (EVERY) passengers hurt with a vehicle: damage is simulation, applied at the next step',
    'WeaponFireIntent':           'network fire requests + behaviour helpers: firing is simulation',
    'PathfindingRequestEvent':    'network + behaviour helpers: path planning is simulation',
    'SensorChangedEvent':         'sensor helpers called from step-only systems',
    'StrikeNotification':         'melee helper called from a step-only system',
    'PickUpRoundEvent':           'behaviour helper (RoundPickUp) called from step-only code',
    'DoorCommandEvent':           'behaviour helpers / network: doors are simulation',
    'EjectRequestEvent':          'behaviour helper: ejection is simulation',
    'RaycastRequestEvent':        'behaviour helpers: raycasts serve simulation',
    'SetReportingEvent':          'order helpers: reporting requirements apply at the next step',
    'CmdCreateFormation':         'VehicleAPI from behaviour executors',
    'CmdJoinFormation':           'VehicleAPI from behaviour executors',
    'CmdLeaveFormation':          'VehicleAPI from behaviour executors',
    'CmdSpawnVehicle':            'VehicleAPI from behaviour executors',
    'CmdAssignSubordinateRejected': 'UnitHierarchySystem (EVERY) answers VehicleCommandSystem at its next step',
    'NodeDeparted':               'ClusterCapabilityIngestSystem (EVERY): a departed node releases possession at the next step',
    'PossessRequestEvent':        'operator possess click while paused takes effect at the next step (approved lean, design §8)',
    'ReleasePossessionEvent':     'operator release click while paused takes effect at the next step (approved lean, design §8)',
    'CustodyEvent':               'Custody.Ask from interactions (network ingress): custody runs on sim timers',
    'DetonateChargeEvent':        'Charges.Ask from behaviours (DetonateChargeBehavior): detonation is simulation',
    'RestationEvent':             'CarryStations from behaviours (StationWeaponBehavior): restationing is simulation',
    'ClearBehaviorEvent':         'the brain drops its tree at the next step; the ingress (EVERY) applies it at once',
    # editor code publishes these only while the sim RUNS; paused edits apply directly (EntityAiDetailsView.cs:170-188,
    # DebugApiService.Reuse.cs:86)
    'AttachInstanceBlueprintEvent':  'editor publishes only while running; paused attach applies directly',
    'RemoveInstanceBlueprintEvent':  'editor publishes only while running; paused detach applies directly',
    'ReplaceInstanceBlueprintEvent': 'blueprint actions (simulation) + editor only while running',
}

EXCL = re.compile(r'Tests?/|Tests?\.cs|/Examples/|Benchmark|/obj/|/bin/|ExtDeps/|TestShared/')
PUB = re.compile(r'\b(?:Publish|PublishManaged|PublishEvent|PublishManagedEvent)\s*(?:<\s*([\w.]+)\s*>\s*\(|\(\s*new\s+([\w.]+)\s*[({])')
READ = re.compile(r'\b(?:Read|ReadManaged|ReadEvents|ReadManagedEvents|HasEvent|HasManagedEvent|ConsumeEvents|ConsumeManagedEvents)\s*<\s*([\w.]+)\s*>')
MARK = re.compile(r'\[(?:Fdp\.ModuleHost\.Abstractions\.)?TickMode\(\s*(?:Fdp\.ModuleHost\.Abstractions\.)?TickPolicy\.(\w+)\s*\)\]')


CLASS = re.compile(r'\b(?:class|record)\s+(\w+)[^{;]*\{')


def class_spans(src):
    """(start, end, mode or None) for every class body, mode from a [TickMode] in the attribute lines just above it."""
    spans = []
    for m in CLASS.finditer(src):
        open_i = m.end() - 1
        depth, j = 0, open_i
        while j < len(src):
            c = src[j]
            if c == '{':
                depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    break
            j += 1
        head = src[max(0, src.rfind('\n', 0, max(0, m.start() - 600))):m.start()]
        attrs = head.split('\n')[-8:]
        mode = None
        for line in attrs:
            mm = MARK.search(line)
            if mm:
                mode = mm.group(1)
        spans.append((open_i, j, mode))
    return spans


def path_role(p):
    p = p.lower()
    if 'translator' in p or '/network/' in p or 'replication' in p:
        return 'NET-CODE'
    if 'editor' in p or 'presentation' in p or '/ui/' in p or 'debugapi' in p or 'gizmo' in p or 'map' in p:
        return 'EDITOR/UI-CODE'
    if 'executor' in p or 'node' in p or 'behavior' in p or 'runner' in p:
        return 'SIM-HELPER'
    return 'OTHER-CODE'


def role_at(path, spans, pos):
    """The tick mode of the innermost class around pos that declares one; the path's role when none does."""
    best = None
    for s, e, mode in spans:
        if s <= pos <= e and mode and (best is None or s > best[0]):
            best = (s, mode)
    if best:
        return 'STEP' if best[1] == 'WhenTimeAdvances' else 'EVERY'
    return path_role(path)


def main():
    files = subprocess.run(['git', 'ls-files', '*.cs'], cwd=ROOT, capture_output=True, text=True).stdout.split()
    pubs, reads = defaultdict(set), defaultdict(set)
    for f in files:
        if EXCL.search(f):
            continue
        try:
            src = open(os.path.join(ROOT, f), encoding='utf-8', errors='replace').read()
        except OSError:
            continue
        spans = class_spans(src)
        for m in PUB.finditer(src):
            t = (m.group(1) or m.group(2)).split('.')[-1]
            if t[:1].isupper():
                pubs[t].add((f, role_at(f, spans, m.start())))
        for m in READ.finditer(src):
            t = m.group(1).split('.')[-1]
            if t[:1].isupper() and t != 'T':
                reads[t].add((f, role_at(f, spans, m.start())))
    chains = {}
    for t, rr in reads.items():
        step_readers = sorted(f for f, r in rr if r == 'STEP')
        if not step_readers:
            continue
        other_pubs = sorted(f'{f} ({r})' for f, r in pubs.get(t, ()) if r != 'STEP')
        if other_pubs:
            chains[t] = (step_readers, other_pubs)
    unreviewed = sorted(t for t in chains if t not in ALLOW)
    stale = sorted(t for t in ALLOW if t not in chains)
    for t in sorted(chains):
        sr, op = chains[t]
        tag = 'reviewed' if t in ALLOW else 'UNREVIEWED'
        print(f'{tag:10s} {t}\n           step-only readers: {"; ".join(sr)}\n           other publishers : {"; ".join(op)}')
    print(f'\n{len(chains)} chains; {len(unreviewed)} unreviewed; {len(stale)} allow-list entries no longer seen')
    if stale:
        print('stale allow-list entries: ' + ', '.join(stale))
    if '--check' in sys.argv and (unreviewed or stale):
        print('FAIL: review each UNREVIEWED chain (is the delay to the next step right? if not, the reader must run every frame) '
              'and add it to ALLOW with the reason; remove stale entries.')
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
