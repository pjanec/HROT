"""The small scenario-writing kit the demo generators share (gen-rf-scenarios.py, gen-ts-scenarios.py)."""
import json, math, os

ROOT = "/home/user/HROT"


def rot(deg):  # compass heading (0 = north, clockwise) → quaternion about Z (forward = +X)
    yaw = math.radians(90 - deg)
    return [0, 0, round(math.sin(yaw / 2), 7), round(math.cos(yaw / 2), 7)]


class Scn:
    def __init__(self, sid, prefix):
        self.ents, self.n, self.prefix, self.nid = {}, 0, prefix, None
        self.root = ROOT

    def guid(self):
        self.n += 1
        return f"{self.prefix}-0000-4000-8000-{self.n:012d}"

    def man(self, name, force, at, facing, tkb=2004, health=100000, behavior=None, sop=None, commander=None, designation=None, nid=0):
        g = self.guid()
        e = {"SimTransform": {"Position": list(at), "Rotation": rot(facing)},
             "Health": {"Current": health, "Max": health},
             "NetworkIdentity": {"Value": nid},
             "TkbIdentity": {"TkbType": tkb},
             "EntityInfo": {"Name": name, "ForceId": force},
             "TargetMemory": {"Entries": []}}
        if behavior: e["Behavior"] = behavior
        if sop: e["Sop"] = sop
        if commander: e["UnitSubordinate"] = {"CommanderGuid": commander, "Designation": designation or "Wingman"}
        self.ents[g] = e
        return g

    def squad(self, name, force, at, nid, sop=None):
        g = self.guid()
        e = {"SimTransform": {"Position": list(at), "Rotation": [0, 0, 0, 1]},
             "NetworkIdentity": {"Value": nid}, "TkbIdentity": {"TkbType": 302},
             "EntityInfo": {"Name": name, "ForceId": force}}
        if sop: e["Sop"] = sop
        self.ents[g] = e
        return g

    def car(self, name, at, heading=90, nid=0):
        g = self.guid()
        self.ents[g] = {"SimTransform": {"Position": list(at), "Rotation": rot(heading)},
                        "NetworkIdentity": {"Value": nid}, "TkbIdentity": {"TkbType": 8806},
                        "EntityInfo": {"Name": name},
                        "ObstacleShape": {"Length": 4.5, "Width": 1.8, "Height": 1.5}}
        return g

    def write(self, sid, terrain, description):
        doc = {"$meta": {"docType": "Hrot.Scenario", "schemaVersion": 1},
               "header": {"subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": terrain,
                          "description": description},
               "entities": self.ents}
        d = os.path.join(self.root, "scenarios", sid)
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, "scenario.json"), "w") as f:
            json.dump(doc, f, indent=2)
            f.write("\n")
        print("wrote", sid, len(self.ents), "entities")


