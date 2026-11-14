#!/usr/bin/env python3
"""CE-4271 (docs/DESIGN_Add_Entity_Picker.md §2j) — the entity atlas packer's rails: `scripts/thumbnails.py --add-image / --atlas-list / --atlas-remove` against a temp atlas directory.

  python3 scripts/test_thumbnails_atlas.py          (needs Pillow; on Windows: py -3 scripts\\test_thumbnails_atlas.py)

The index it checks is the one the C# `AtlasIndex` parses (`AtlasIndexTests` embeds a literal of the same shape): version 1, cell [320,180], page [2048,2048], sorted keys.
"""
import json, os, subprocess, sys, tempfile, time, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SCRIPT = os.path.join(HERE, "thumbnails.py")
try:
    from PIL import Image
except ImportError:          # a sandbox without Pillow: say so, do not fail the repository's other checks
    print("SKIP: Pillow is not installed")
    sys.exit(0)

W, H = 320, 180


def run(*args):
    return subprocess.run([sys.executable, SCRIPT, *args], capture_output=True, text=True)


def picture(path, colour, size=(512, 288)):
    Image.new("RGB", size, colour).save(path)


def cell_mean(page_path, col, row):
    with Image.open(page_path) as page:
        px = page.convert("RGB").crop((col * W, row * H, (col + 1) * W, (row + 1) * H)).resize((1, 1), Image.BOX).getpixel((0, 0))
    return px


class AtlasPackerTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = os.path.join(self.tmp.name, "atlas")
        self.pics = os.path.join(self.tmp.name, "pics")
        os.makedirs(self.pics)

    def tearDown(self):
        self.tmp.cleanup()

    def add(self, name, colour, size=(512, 288)):
        f = os.path.join(self.pics, name.replace("/", "_") + ".png")
        picture(f, colour, size)
        r = run("--add-image", f, "--name", name, "--atlas-dir", self.dir)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)

    def index(self):
        with open(os.path.join(self.dir, "entities.atlas.json"), encoding="utf-8") as fh:
            return json.load(fh)

    def test_AddImage_RoundTrip_TheIndexNamesTheStem_AndThePageHoldsTheCell(self):
        self.add("Rifleman", (200, 30, 30))
        self.add("T-72", (30, 30, 200))
        idx = self.index()
        self.assertEqual((idx["version"], idx["cell"], idx["page"]), (1, [W, H], [2048, 2048]))
        self.assertEqual(idx["pages"], ["entities-0.jpg"])
        self.assertEqual(idx["items"], {"Rifleman": [0, 0, 0], "T-72": [0, 1, 0]})
        page = os.path.join(self.dir, "entities-0.jpg")
        r, g, b = cell_mean(page, 1, 0)                       # T-72's cell is blue
        self.assertTrue(b > 150 and r < 80, (r, g, b))
        r, g, b = cell_mean(page, 0, 0)                       # Rifleman's is red
        self.assertTrue(r > 150 and b < 80, (r, g, b))
        self.assertEqual(sum(cell_mean(page, 5, 10)), 0)      # an empty cell is black

    def test_AName_KeepsItsSlot_AndOnlyAChangedPageIsReEncoded(self):
        self.add("A", (200, 30, 30))
        self.add("B", (30, 200, 30))
        page = os.path.join(self.dir, "entities-0.jpg")
        before = (os.stat(page).st_mtime_ns, open(page, "rb").read())
        time.sleep(0.05)
        self.add("B", (30, 200, 30))                          # the same picture again: nothing to write
        self.assertEqual((os.stat(page).st_mtime_ns, open(page, "rb").read()), before)
        self.add("B", (240, 240, 20))                         # a changed picture: replaced IN ITS slot
        self.assertEqual(self.index()["items"]["B"], [0, 1, 0])
        r, g, b = cell_mean(page, 1, 0)
        self.assertTrue(r > 180 and g > 180 and b < 80, (r, g, b))

    def test_TheIndexKeysAreSorted_AndARemovedSlotIsReused(self):
        for name, c in (("Zulu", (10, 10, 10)), ("Alpha", (250, 10, 10)), ("Mike", (10, 250, 10))):
            self.add(name, c)
        text = open(os.path.join(self.dir, "entities.atlas.json"), encoding="utf-8").read()
        self.assertEqual([k for k in self.index()["items"]], ["Alpha", "Mike", "Zulu"])
        self.assertLess(text.index('"Alpha"'), text.index('"Zulu"'))
        slot_of_alpha = self.index()["items"]["Alpha"]
        r = run("--atlas-remove", "Alpha", "--atlas-dir", self.dir)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertNotIn("Alpha", self.index()["items"])
        self.assertEqual(sum(cell_mean(os.path.join(self.dir, "entities-0.jpg"), slot_of_alpha[1], slot_of_alpha[2])), 0)   # blacked
        self.add("Echo", (10, 10, 250))
        self.assertEqual(self.index()["items"]["Echo"], slot_of_alpha)                                                         # the freed slot is reused
        listed = run("--atlas-list", "--atlas-dir", self.dir).stdout
        self.assertIn("Echo", listed)
        self.assertNotIn("Alpha", listed)

    def test_AFullPageOpensTheNext_AndANameWithDotDotIsRefused(self):
        f = os.path.join(self.pics, "p.png")
        picture(f, (90, 90, 90), (64, 36))
        for i in range(67):                                   # 6 x 11 = 66 cells a page
            r = run("--add-image", f, "--name", f"T{i:02d}", "--atlas-dir", self.dir)
            self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        idx = self.index()
        self.assertEqual(idx["pages"], ["entities-0.jpg", "entities-1.jpg"])
        self.assertEqual(idx["items"]["T66"], [1, 0, 0])
        self.assertEqual(run("--add-image", f, "--name", "../evil", "--atlas-dir", self.dir).returncode, 1)
        self.assertNotIn("../evil", self.index()["items"])


if __name__ == "__main__":
    unittest.main()
