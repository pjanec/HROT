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
                r.body = {"Components": {"SimTransform": {"Position": [55.0, 66.0, 1.25]}}}   # the GET writes a vector as [x, y, z] (measured against a live editor, 2026-11-09)
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


class CropWithinSubjectTests(unittest.TestCase):
    """⭐ CE-4273 — a TERRAIN obstacle (the Car, 8806) changes the ground round it, so the shot with it and the shot without it differ in a band
    across the frame: for a static obstacle the crop counts only its own projected box. Any other subject (no box) is cropped as before."""

    def setUp(self):
        try:
            from PIL import Image  # noqa: F401
        except ImportError:
            self.skipTest("needs Pillow")
        import importlib.util, tempfile
        spec = importlib.util.spec_from_file_location("thumbs_crop_under_test", SCRIPT)
        self.t = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.t)
        self.dir = tempfile.mkdtemp()

    def shots(self, ground_changes):
        from PIL import Image, ImageDraw
        w, h = 1024, 576
        bg = Image.new("RGB", (w, h), (120, 160, 220))                       # sky only: no ground drawn without the obstacle
        big = Image.new("RGB", (w, h), (120, 160, 220))
        d = ImageDraw.Draw(big)
        if ground_changes:
            d.rectangle((0, 200, w, h), fill=(60, 90, 40))                   # the ground the obstacle's bake brings
        d.rectangle((480, 260, 560, 300), fill=(70, 70, 75))                 # the car itself
        a, b = os.path.join(self.dir, "a.png"), os.path.join(self.dir, "b.png")
        big.save(a); bg.save(b)
        return a, b, os.path.join(self.dir, "out.png")

    def test_a_scene_wide_difference_is_cropped_to_the_subject(self):
        a, b, out = self.shots(ground_changes=True)
        verdict = self.t.crop_to_drawn(a, b, out, within=(480, 260, 560, 300))
        self.assertEqual(verdict[0], "ok")                                    # not "clipped" by ground it does not own
        from PIL import Image
        self.assertEqual(Image.open(out).size, (self.t.W, self.t.H))

    def test_without_the_subject_box_the_same_shots_read_as_clipped(self):
        a, b, out = self.shots(ground_changes=True)
        self.assertEqual(self.t.crop_to_drawn(a, b, out)[0], "clipped")      # the failure the box fixes

    def test_without_a_box_a_local_difference_is_cropped_as_before(self):
        a, b, out = self.shots(ground_changes=False)
        self.assertEqual(self.t.crop_to_drawn(a, b, out)[0], "small")        # no box (not an obstacle): the ordinary zoom-in path

    def test_the_box_projects_onto_the_view_centre(self):
        x0, y0, x1, y1 = self.t.project_box_px(self.t.box_corners((0.0, 0.0, 0.0), 1.0, 2.0, 2.0), [20.0, 0.0, 0.5], [0.0, 0.0, 0.5], 2048, 1152)
        self.assertLess(abs((x0 + x1) / 2 - 1024), 2); self.assertLess(abs((y0 + y1) / 2 - 576), 2)


if __name__ == "__main__":
    unittest.main()
