"""CE-4361 — the ONE python answer to "where is the committed scenario <name>?" (scenarios are filed in category sub-folders, Q-S1/Q-S2;
a scenario is addressed by its LEAF name, unique across the tree). Mirrors ContentLayout.ResolveScenarioDirectory: scenarios/<name> when it
exists, else the one folder named <name> under a sub-folder, else scenarios/<name> (where a NEW scenario is created)."""
import os


def scenarios_root(repo):
    return os.path.join(repo, "scenarios")


def find(repo, name):
    """The existing folder of scenario <name>, or None."""
    root = scenarios_root(repo)
    direct = os.path.join(root, name)
    if os.path.isfile(os.path.join(direct, "scenario.json")):
        return direct
    for dp, dns, fns in os.walk(root):
        dns[:] = [d for d in dns if not d.startswith(".")]
        if os.path.basename(dp) == name and "scenario.json" in fns:
            return dp
    return None


def folder(repo, name):
    """The folder of scenario <name>: the existing one, else scenarios/<name> (a new scenario is written at the top level)."""
    return find(repo, name) or os.path.join(scenarios_root(repo), name)


def all_scenarios(repo):
    """{leaf name: folder} of every scenario under scenarios/ at any depth."""
    out = {}
    for dp, dns, fns in os.walk(scenarios_root(repo)):
        dns[:] = [d for d in dns if not d.startswith(".") and d != "takes"]
        if "scenario.json" in fns:
            out[os.path.basename(dp)] = dp
    return out
