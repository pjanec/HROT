#!/usr/bin/env python3
"""U-12 / CE-4273 (docs/DESIGN_Add_Entity_Picker.md §2j) — `scripts/thumbnails.py`'s hold-still: the subject of a type's still is pinned while it is shot, and the empty-view shot waits for it to be gone. Against a FAKE debug API (no editor, no Pillow).

  python3 scripts/test_thumbnails_hold_still.py
"""
import os, sys, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "thumbnails.py")


class HoldStillTests(unittest.TestCase):
    """U-12 / CE-4273 — `hold_still` / `wait_gone` against a FAKE debug API (no editor): the subject is pinned (order cancelled, velocity zeroed, pose back at the spawn point) and the empty-view shot waits for it to be really gone."""

    def setUp(self):
        import importlib.util
        spec = importlib.util.spec_from_file_location("thumbs_under_test", SCRIPT)
        self.t = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.t)
        self.calls = []
        self.live = [{"networkId": 7}]
        self.gone_after = 2          # GET /entities lists the entity for this many polls, then it is gone

        def fake(method, path, body=None, *a, **k):
            self.calls.append((method, path, body))
            class R:                  # what udc.call returns, as thumbnails.ok / body_of read it
                status = 200
            r = R()
            r.body = None
            if method == "GET" and path == "/entities":
                if self.gone_after <= 0: self.live = []
                self.gone_after -= 1
                r.body = self.live
            elif method == "GET" and path.startswith("/entities/"):
                r.body = {"Components": {"SimTransform": {"Position": {"X": 55.0, "Y": 66.0, "Z": 1.25}}}}
            return r
        self.t.udc.call = fake
        self.t.body_of = lambda r: r.body

    def test_hold_still_pins_the_subject_at_the_spawn_point(self):
        self.t.hold_still(7, [4000.0, 4000.0, 0.0])
        methods = [(m, p) for m, p, _ in self.calls]
        self.assertIn(("DELETE", "/entities/7/order"), methods)                     # any order cancelled
        comp = [b for m, p, b in self.calls if m == "POST" and p == "/entities/7/component"]
        vel = next(b for b in comp if b["componentType"] == "SimVelocity")
        self.assertEqual(vel["patch"]["Linear"], {"X": 0.0, "Y": 0.0, "Z": 0.0})   # velocity zero
        pose = next(b for b in comp if b["componentType"] == "SimTransform")
        self.assertEqual(pose["patch"]["Position"], {"X": 4000.0, "Y": 4000.0, "Z": 1.25})   # back at the spawn x, y; its own settled z

    def test_wait_gone_steps_until_the_entity_is_really_gone(self):
        self.assertTrue(self.t.wait_gone([7]))
        steps = [c for c in self.calls if c[:2] == ("POST", "/sim/step")]
        self.assertEqual(len(steps), 3)                                              # listed twice, gone on the third poll: three steps, not a fixed one
        self.gone_after = 10 ** 6
        self.live = [{"networkId": 7}]
        self.assertFalse(self.t.wait_gone([7], tries=2))                             # never gone: it says so


if __name__ == "__main__":
    unittest.main()
