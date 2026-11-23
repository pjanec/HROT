"""dps_gen.py — CE-4651 (U-18a): the DDS PLUMBING of the Unreal image generator, regenerated from the gateway's contract.

    python scripts/unreal/dps_gen.py [--gateway D:/Work/SimNetGateway] [--cyclone D:/Work/CycloneDds.NET/artifacts/native-install]

The DPS contract (SimNetGateway docs/clients/idl, DD-017/018/019) is still changing, so NOTHING contract-specific is written by hand:
  ① copies CycloneDDS C (headers, ddsc.lib, ddsc.dll) into the plugin's  Source/ThirdParty/CycloneDDS            (git-ignored)
  ② runs idlc -l c on the committed IDL into                              Source/HrotDps/Private/Generated         (git-ignored)
  ③ writes the topic table (name, type descriptor, QoS) from the gateway's [DdsTopic]/[DdsQos] attributes in DpsTopics.cs —
     the QoS is NOT in the IDL and a durability/reliability mismatch fails the DDS match silently (guide "QoS is not in the IDL").
Re-run after every contract change, then rebuild the plugin.
"""
import argparse, os, re, shutil, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
PLUGIN = os.path.join(ROOT, "tools", "unreal", "HrotTerrain", "Source")
THIRD = os.path.join(PLUGIN, "ThirdParty", "CycloneDDS")
GEN = os.path.join(PLUGIN, "HrotDps", "Private", "Generated")
PREFIX = "SimNetGateway_Protocol_DdsPlayer_"


def topics_of(cs_path):
    """[(topic name, struct, durability, depth, lease ms)] from DpsTopics.cs + DpsProtocol.cs."""
    proto = open(os.path.join(os.path.dirname(cs_path), "DpsProtocol.cs"), encoding="utf-8").read()
    names = dict(re.findall(r'public const string (\w+) = "([^"]+)";', proto))
    lease_default = int(re.search(r"DefaultLeaseMs = (\d+)", proto).group(1))
    src = open(cs_path, encoding="utf-8").read()
    out = []
    for m in re.finditer(r"\[DdsTopic\(DpsProtocol\.(\w+)\)\]\s*\[DdsQos\((.*?)\)\]\s*(?:\[[^\]]*\]\s*)*public\s+(?:partial\s+)?struct\s+(\w+)",
                         src, re.S):
        key, qos, struct = m.groups()
        dur = re.search(r"Durability = DdsDurability\.(\w+)", qos).group(1)
        rel = re.search(r"Reliability = DdsReliability\.(\w+)", qos).group(1)
        depth = int(re.search(r"HistoryDepth = (\d+)", qos).group(1))
        lease = 0
        lm = re.search(r"LivelinessLeaseDurationMs = ([\w.]+)", qos)
        if lm:
            lease = lease_default if "DefaultLeaseMs" in lm.group(1) else int(lm.group(1))
        out.append((names[key], struct, dur, rel, depth, lease))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gateway", default=os.path.join(os.path.dirname(ROOT), "SimNetGateway"))
    ap.add_argument("--cyclone", default=os.path.join(os.path.dirname(ROOT), "CycloneDds.NET", "artifacts", "native-install"))
    a = ap.parse_args()
    idl = os.path.join(a.gateway, "docs", "clients", "idl")
    cs = os.path.join(a.gateway, "src", "Protocols", "SimNetGateway.Protocol.DdsPlayer", "DpsTopics.cs")

    # ① the native library (headers + import lib + dll)
    if os.path.isdir(THIRD): shutil.rmtree(THIRD)
    shutil.copytree(os.path.join(a.cyclone, "include", "dds"), os.path.join(THIRD, "include", "dds"))
    os.makedirs(os.path.join(THIRD, "lib")); os.makedirs(os.path.join(THIRD, "bin"))
    shutil.copy2(os.path.join(a.cyclone, "lib", "ddsc.lib"), os.path.join(THIRD, "lib"))
    shutil.copy2(os.path.join(a.cyclone, "bin", "ddsc.dll"), os.path.join(THIRD, "bin"))

    # ② the C types and descriptors
    if os.path.isdir(GEN): shutil.rmtree(GEN)
    os.makedirs(GEN)
    env = dict(os.environ, PATH=os.path.join(a.cyclone, "bin") + os.pathsep + os.environ["PATH"])
    for f in ("DpsTypes", "DpsTopics"):
        subprocess.run([os.path.join(a.cyclone, "bin", "idlc.exe"), "-l", "c", "-I", idl, os.path.join(idl, f + ".idl")],
                       cwd=GEN, env=env, check=True)
    version = open(os.path.join(idl, "DpsTopics.idl"), encoding="utf-8").readline().strip().lstrip("/ ").split(",")[0]

    # ③ the topic table
    tops = topics_of(cs)
    want = set(re.findall(r'public const string \w+ = "(DPS_\w+)";',
                          open(os.path.join(os.path.dirname(cs), "DpsProtocol.cs"), encoding="utf-8").read()))
    missed = want - {t[0] for t in tops}
    if missed:   # a topic whose attributes the pattern did not match would silently vanish from the table
        sys.exit(f"topics named in DpsProtocol.cs but not parsed from DpsTopics.cs: {sorted(missed)}")
    header = open(os.path.join(GEN, "DpsTopics.h"), encoding="utf-8").read()
    rows = []
    for name, struct, dur, rel, depth, lease in tops:
        if f"{PREFIX}{struct}_desc" not in header:
            sys.exit(f"{name}: no {PREFIX}{struct}_desc in the idlc output — the IDL and DpsTopics.cs disagree")
        rows.append(f'\t{{ TEXT("{name}"), TEXT("{struct}"), &{PREFIX}{struct}_desc, sizeof({PREFIX}{struct}), '
                    f'{"true" if dur == "TransientLocal" else "false"}, {"true" if rel == "Reliable" else "false"}, {depth}, {lease} }},')
    with open(os.path.join(GEN, "DpsTopicTable.gen.inl"), "w", encoding="utf-8", newline="\n") as o:
        o.write(f"// generated by scripts/unreal/dps_gen.py from {version} - do not edit\n")
        o.write(f'#define HROT_DPS_CONTRACT TEXT("{version}")\n')
        o.write("static const FHrotDpsTopicDef GHrotDpsTopics[] =\n{\n" + "\n".join(rows) + "\n};\n")
    print(f"{version}: {len(tops)} topics -> {GEN}")


if __name__ == "__main__":
    main()
