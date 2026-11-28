"""hrot_dps_probe.py — CE-4651 (U-18a): the DDS PLUMBING proof, run in the Unreal editor COMMANDLET.

    UnrealEditor-Cmd <project> -run=pythonscript -script=<this> -nullrhi      HROT_DPS_JOB = { "domain", "partition", "seconds", "out" }

Joins the DPS station, subscribes every topic the gateway writes (UHrotDpsSubsystem.ProbeSubscribeAll — the generated reader per topic with
the gateway's QoS), polls for `seconds` (a commandlet has no frames, so the probe polls itself) and writes the per-topic sample counts and
the last sample of Session / Entity / EntityState / Fire DECODED through the generated C types — the bytes arrive as the right fields.
"""
import json, os, time, traceback
import unreal

JOB = json.load(open(os.environ["HROT_DPS_JOB"], encoding="utf-8-sig"))
R = {"history": []}
try:
    dps = unreal.get_engine_subsystem(unreal.HrotDpsSubsystem)
    R["contract"] = dps.contract()
    R["topics"] = len(dps.topics())
    R["started"] = dps.start(int(JOB["domain"]), JOB["partition"])
    R["subscribed"] = dps.probe_subscribe_all()
    if JOB.get("ready"): open(JOB["ready"], "w").write("ready")   # the test's publisher starts now (Fire is VOLATILE: only what is sent after)
    t0 = time.time()
    while time.time() - t0 < float(JOB["seconds"]):
        n = dps.poll()
        if n: R["history"].append([round(time.time() - t0, 2), n, dict(dps.probe_last())])
        time.sleep(0.05)
    R["counts"] = {k: v for k, v in dps.counts().items() if v}
    R["last"] = dict(dps.probe_last())
    dps.stop()
    R["ok"] = True
except Exception:
    R["ok"] = False; R["why"] = traceback.format_exc()
R["history"] = R["history"][:3] + R["history"][-3:]
json.dump(R, open(JOB["out"], "w", encoding="utf-8"), indent=1)
