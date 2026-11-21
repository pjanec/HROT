"""CE-4651 (U-18n) - cut a sound library's long takes into the IG's loops and one-shots.

Reads WAVs (PCM 16/24/32, float 32; mono, stereo, ambisonic AmbiX or surround), and per recipe writes 16-bit WAVs at 48 kHz
(or the source rate when it is not a multiple of 48 kHz):

  loop    - the steadiest window of the take (lowest spread of its 0.25 s loudness), made seamless by an equal-power crossfade of
            its tail into the audio just before it; beds stereo, point sources mono
  shots   - the N loudest separate onsets, each from 10 ms before its onset until it decays 50 dB (or MaxLen), faded out

Usage: python sound_cut.py <raw root> <out root> [--set gdc|gdc23|pro] [--only <name substring>]
The recipes are below; a recipe's `src` is a substring of the file's path under the raw root. Nothing here reads the network.
"""
import math, os, struct, sys
import numpy as np

# name, src (substring), mode, seconds (loop length or a shot's max length), channels (1 or 2), count (shots), gain target dBFS,
# [a shot's min length: it is not ended by the decay rule before that]
R = [
    # ---- ambience beds (stereo loops) -----------------------------------------------------------------------------------------
    ("amb/countryside_day", "Field Wheat Dry Sizzle Insects", "loop", 60, 2, 0, -24),
    ("amb/meadow_wind", "Meadow Open Plane Windy", "loop", 60, 2, 0, -24),
    ("amb/pasture_horses", "Ranch Horse Pasture", "loop", 60, 2, 0, -24),
    ("amb/forest_day", "Morning Small Birch Forest", "loop", 60, 2, 0, -24),
    ("amb/forest_wind", "WINDVege_Mixed Forest Wind", "loop", 60, 2, 0, -25),
    ("amb/trees_gusts", "Wind - Aspen - Gusts", "loop", 60, 2, 0, -25),
    ("amb/trees_calm", "Wind - Oak - Calm", "loop", 60, 2, 0, -27),
    ("amb/night_woods", "Nighttime-Woodlands", "loop", 60, 2, 0, -27),
    ("amb/town_night", "Suburban Residential Area Main Street", "loop", 60, 2, 0, -25),
    ("amb/town_rain_night", "Urban Side Street Empty Rain", "loop", 60, 2, 0, -25),
    ("amb/town_day", "Berlin Balcony Late Afternoon", "loop", 60, 2, 0, -24),
    ("amb/traffic_city", "HAUTEUR VILLE DOWNMIX", "loop", 60, 2, 0, -24),
    ("amb/traffic_motorway", "UK Motorway", "loop", 60, 2, 0, -25),
    ("amb/rain_heavy", "Rain, Heavy with Distant Thunders", "loop", 60, 2, 0, -22),
    ("amb/rain_light", "Rain at Night Slow Crescendo", "loop", 60, 2, 0, -25),
    ("amb/rain_gutter", "Rain, Water Gurgling and Spraying in Gutter", "loop", 60, 2, 0, -24),
    ("amb/creek", "Stefano Cremona - Rivers", "loop", 60, 2, 0, -25),
    ("amb/crowd_unrest", "SAPST01", "loop", 60, 2, 0, -24),
    ("amb/walla_inside", "INT WALLA_03", "loop", 60, 2, 0, -26),
    # ---- animals ---------------------------------------------------------------------------------------------------------------
    ("animal/horse_snort", "Snort_Uberduo_HORS_Stereo", "shots", 2.5, 1, 6, -3),
    ("animal/horse_snort_b", "Snort_Uberduo_HORS_Mono", "shots", 2.5, 1, 4, -3),
    ("animal/horse_trot", "Trot_Uberduo_HORS_Stereo", "loop", 4, 1, 0, -16),
    ("animal/pig", "Pig Squeal and Snort", "shots", 3, 1, 5, -3),
    ("animal/chicken", "Chick Squawks", "shots", 2, 1, 6, -3),
    ("animal/goose", "Goose Baby And Adult", "shots", 3, 1, 5, -3),
    ("animal/rooster", "Rooster Call", "shots", 4, 1, 3, -3),
    ("animal/raptor", "Red Kite Call", "shots", 3, 1, 5, -3),
    # ---- vehicles (mono loops, pitched by speed in the IG) ----------------------------------------------------------------------
    ("veh/tracked_run", "Bandkanon 1C - t6 - EXT - STEADY RPMS", "loop", 8, 1, 0, -16),
    ("veh/tracked_idle", "Bandkanon 1C - t1 - ONBRD - SLOW", "loop_quiet", 8, 1, 0, -18),
    ("veh/moto_idle", "Onboard, idle, Mix, Loop, Short", "loop", 3, 1, 0, -16),
    ("veh/moto_run", "Onboard, Cruising, City", "loop", 6, 1, 0, -16),
    ("veh/car_run", "Range Rover SDV8 - t3 - EXT - MEDIUM", "loop", 6, 1, 0, -16),
    ("veh/car_idle", "Range Rover SDV8 - t10 - ONBRD - CIRCLING", "loop_quiet", 6, 1, 0, -18),
    ("veh/quad_run", "Medium Steady Rpm Loop", "loop", 6, 1, 0, -16),
    ("veh/heli", "Helicopter - Military - FLY - By", "loop_loud", 6, 1, 0, -14),
    ("veh/plane_prop", "Airplane - FLY - By - Overhead", "loop_loud", 8, 1, 0, -15),
    ("veh/horn", "Man Lions Coach - VAR SFX - Horn", "shots", 2.5, 1, 4, -3),
    # ---- weapons ----------------------------------------------------------------------------------------------------------------
    ("wpn/sniper_far", "SOURCE Single Shots 30m Front", "shots", 3, 1, 6, -1, 1.5),
    ("wpn/shotgun", "Saiga 12 Cal.12 SOURCE Single Shots", "shots", 2.5, 1, 6, -1),
    ("wpn/heavy_rifle", "Lynx 50 - FIRING - Take 2 - 4m Left", "shots", 3, 1, 5, -1),
    ("wpn/pistol", "Desert Eagle - FIRING - Take 1", "shots", 2, 1, 6, -1),
    ("wpn/burst_hall", "AK5 7,62mm - FIRING - Large Hall", "shots", 3, 1, 4, -1),
    ("wpn/smg_hall", "MP5 9mm - FIRING - Large Hall", "shots", 2.5, 1, 4, -1),
    ("wpn/howitzer", "Haubits FH77 Howitzer", "shots", 6, 1, 4, -1, 3.5),
    ("wpn/rpg", "RPG - FIRING AND IMPACT", "shots", 3, 1, 2, -1, 1.5),
    ("expl/mine", "Tank Mine - EXPLOSION", "shots", 6, 1, 2, -1, 4),
    ("expl/medium", "Medium Realistic Explosion", "shots", 6, 1, 1, -1, 4),
    ("expl/car", "Designed Car Explosion", "shots", 6, 1, 1, -1, 4),
    ("expl/wood_crash", "Designed Wood Crash And Debris", "shots", 4, 1, 1, -1, 2.5),
    ("expl/debris_rattle", "Seismic_Activities", "shots", 4, 1, 6, -4),
    # ---- impacts ----------------------------------------------------------------------------------------------------------------
    ("hit/armor", "Hit Metal Armor Plate", "shots", 1.2, 1, 8, -2),
    ("hit/metal", "Metal Hit Sweeteners", "shots", 1.5, 1, 8, -2),
    ("hit/metal_debris", "Drop Fall Metal Rattle Scrap", "shots", 2.5, 1, 4, -3),
    ("hit/stone", "BluezoneCorp - Stone Impact", "shots", 1.2, 1, 8, -2),
    ("hit/wood", "InMotionAudio - Wood", "shots", 1.2, 1, 8, -2),
    ("hit/glass", "Glass Break Hit", "shots", 2.5, 1, 5, -2),
    ("hit/flesh", "Gore Splatter", "shots", 1.2, 1, 5, -4),
    ("hit/bone", "Bone Breaks Celery", "shots", 1.0, 1, 5, -4),
    # ---- the scene --------------------------------------------------------------------------------------------------------------
    ("door/slam", "Door Slams, Impacts", "shots", 2, 1, 5, -3),
    ("door/creak", "Wooden Door, Opening and Closing", "shots", 3, 1, 4, -4),
    ("scene/tree_fall", "Tree Crack and Fall", "shots", 8, 1, 1, -2, 5),
    ("scene/fire_crackle", "Fire Crackling In A Woodstove", "loop", 10, 1, 0, -18),
    ("scene/flame_roar", "Leaf Blower - ONBRD - WORK", "loop", 4, 1, 0, -14),
    ("scene/whoosh", "Distinct Whooshes", "shots", 2.5, 1, 4, -3),
    ("scene/parachute", "PARACHUTE DEPLOY", "shots", 3, 1, 2, -3),
]

# ---- the PURCHASED pack (Military Warfare SFX Pack, GfxSounds) — PRIVATE: its WAVs and cuts stay outside git, its assets go to
# /Game/Hrot/AudioPro (never published; shipped only cooked inside a packaged IG). These rows name its files, nothing more.
# `--set pro`. The same names as the free set where they mean the same thing: the IG loads AudioPro first, then Audio.
R_PRO = [
    # weapons: one shot per file (the pack's single shots), single ROUNDS cut out of the machine-gun bursts
    ("wpn/rifle", "assault rifle single shot", "shots", 1.5, 1, 12, -1),
    ("wpn/rifle_east", "AK 47 assault rifle single shot", "shots", 1.5, 1, 3, -1),
    ("wpn/rifle_suppressed", "assault rifle suppressed shot", "shots", 1.2, 1, 8, -1),
    ("wpn/pistol", "handgun single shot", "shots", 1.2, 1, 4, -1),
    ("wpn/pistol_b", "Beretta handgun shot", "shots", 1.2, 1, 4, -1),
    ("wpn/pistol_far", "Pistol distant shot", "shots", 2, 1, 6, -1),
    ("wpn/sniper", "Sniper rifle single shot", "shots", 3, 1, 8, -1),
    ("wpn/mg_round", "M240B machine gun automatic fire", "shots", 0.7, 1, 12, -1),
    ("wpn/mg_heavy_round", "Heavy machine gun automatic fire short", "shots", 0.8, 1, 12, -1),
    ("wpn/autocannon_round", "Slow firing machine gun short burst", "shots", 1.0, 1, 8, -1),
    ("wpn/mg_far", "Machine gun burst distant perspective", "shots", 3, 1, 3, -1),
    ("wpn/tank_gun", "Armored tank cannon fire", "shots", 6, 1, 5, -1, 3),
    ("wpn/howitzer", "Cannon fire single shot", "shots", 6, 1, 4, -1, 3),
    ("wpn/rocket_launch", "Rocket launcher single shot", "shots", 3, 1, 4, -1, 1.5),
    ("wpn/mortar", "Mortar strike launch and impact", "shots", 6, 1, 4, -1, 3),
    ("wpn/missile_flyby", "Missile flyby", "shots", 4, 1, 7, -2, 2),
    ("wpn/flame_burst", "Flamethrower burst", "shots", 4, 1, 5, -2, 1.5),
    ("scene/flame_roar", "Flamethrower burst", "loop", 2.5, 1, 0, -14),
    # rounds arriving
    ("hit/body", "Bullet body hit", "shots", 1, 1, 5, -3),
    ("hit/head", "Bullet headshot impact", "shots", 1, 1, 5, -3),
    ("hit/flesh", "Bullet body hit", "shots", 1, 1, 5, -3),
    ("hit/metal", "Bullet metal hit", "shots", 1, 1, 5, -2),
    ("hit/dirt", "Bullet hits on ground dirt", "shots", 1.2, 1, 6, -3),
    ("hit/armor", "Tank armor hit by enemy fire", "shots", 2.5, 1, 5, -1),
    ("crack/bullet", "BLLTBy_Bullet pass by", "shots", 1, 1, 6, -3),
    ("crack/sniper", "Sniper bullet pass by", "shots", 1.5, 1, 3, -3),
    # explosions by size
    ("expl/grenade", "Grenade explosion with debris", "shots", 5, 1, 2, -1, 2.5),
    ("expl/grenade_b", "Grenade blast with flying debris", "shots", 5, 1, 4, -1, 2.5),
    ("expl/medium", "Large grenade explosion close perspective", "shots", 6, 1, 3, -1, 3),
    ("expl/large", "Large grenade explosion with debris", "shots", 7, 1, 2, -1, 3.5),
    ("expl/mine", "Anti personnel mine explosion", "shots", 6, 1, 6, -1, 3),
    ("expl/c4", "C4 detonation explosion", "shots", 7, 1, 2, -1, 3.5),
    ("expl/wood_crash", "Small explosion wooden structure", "shots", 6, 1, 2, -1, 3),
    ("expl/car", "Large grenade explosion glass shattering", "shots", 7, 1, 1, -1, 3.5),
    ("expl/distant", "Distant bomb explosion", "shots", 7, 1, 5, -2, 3.5),
    ("expl/smoke", "Smoke grenade explosion, air hiss", "shots", 5, 1, 4, -3, 2),
    # vehicles (loops for engines, one-shots for the rest)
    ("veh/jeep_idle", "Military jeep engine idle loop", "loopfile", 0, 1, 0, -18),
    ("veh/jeep_run", "Military jeep on the road loop", "loopfile", 0, 1, 0, -16),
    ("veh/truck_idle", "Military truck engine steady idle loop", "loopfile", 0, 1, 0, -18),
    ("veh/truck_run", "Military truck engine steady running loop", "loopfile", 0, 1, 0, -16),
    ("veh/tracked_run", "Tank moving mechanical noise", "loop", 8, 1, 0, -15),
    ("veh/tracked_idle", "Tank moving interior perspective", "loop_quiet", 8, 1, 0, -18),
    ("veh/tank_turret", "Tank turret moving or turning", "shots", 3, 1, 6, -6, 1.5),
    ("veh/heli_idle", "Military helicopter idle engine loop", "loopfile", 0, 1, 0, -16),
    ("veh/heli", "Military helicopter flying forward loop", "loopfile", 0, 1, 0, -14),
    ("veh/jet_idle", "Military aircraft idle engine loop", "loopfile", 0, 1, 0, -16),
    ("veh/jet_pass", "Military aircraft fast pass by", "shots", 6, 1, 5, -1, 4),
    ("veh/truck_horn", "Military truck horn honk", "shots", 2.5, 1, 2, -3),
    # people
    ("foot/dirt", "single footstep on dirt", "shots", 0.6, 1, 4, -6),
    ("foot/mud", "single footstep on mud", "shots", 0.6, 1, 4, -6),
    ("foot/sand", "single footstep on sand", "shots", 0.6, 1, 4, -6),
    ("foot/street", "single footstep on rough street", "shots", 0.6, 1, 4, -6),
    ("foot/metal", "single footstep on metal platform", "shots", 0.6, 1, 4, -6),
    ("foot/glass", "single footstep on broken glass", "shots", 0.8, 1, 4, -6),
    ("body/fall_concrete", "Human body fall on concrete", "shots", 2, 1, 4, -4, 0.8),
    ("body/fall_dirt", "Human body fall on dirt grass", "shots", 2, 1, 3, -4, 0.8),
    ("body/fall_wood", "Human body fall on wood floor", "shots", 2, 1, 2, -4, 0.8),
    ("hit/bone", "Bone snap crack", "shots", 1, 1, 4, -4),
]

# ---- GDC 2023 additions (free): the animals the 2024 bundle lacks
R_GDC23 = [
    ("animal/goose_flock", "Excited gaggle vocals", "shots", 4, 1, 6, -3),
    ("animal/sheep", "Sheeps & Lamps", "shots", 3, 1, 8, -3),
    ("animal/horse_walk", "Draft Horse Walk On On Grass", "loop", 4, 1, 0, -16),
    ("animal/horse_trot", "Draft Horse Trot On Concrete", "loop", 4, 1, 0, -16),
    ("amb/crickets", "Crickets.wav", "loop", 30, 2, 0, -27),
]

OUT_RATE = 48000


# ---- reading -------------------------------------------------------------------------------------------------------------------
class Wav:
    def __init__(self, path):
        self.path = path
        with open(path, "rb") as f:
            head = f.read(12)
            if head[:4] not in (b"RIFF", b"RF64") or head[8:12] != b"WAVE": raise ValueError("not a WAV: " + path)
            fmt = None; data = None; size64 = None
            while True:
                h = f.read(8)
                if len(h) < 8: break
                cid, n = h[:4], struct.unpack("<I", h[4:])[0]
                pos = f.tell()
                if cid == b"ds64": size64 = struct.unpack("<QQQ", f.read(24))[1]
                elif cid == b"fmt ": fmt = f.read(n)
                elif cid == b"data":
                    data = (pos, size64 if (n == 0xFFFFFFFF and size64) else n)
                    break
                f.seek(pos + n + (n & 1))
        tag, ch, rate, _, align, bits = struct.unpack("<HHIIHH", fmt[:16])
        if tag == 0xFFFE: tag = struct.unpack("<H", fmt[24:26])[0]
        self.float = tag == 3
        self.ch, self.rate, self.bits, self.align = ch, rate, bits, align
        avail = os.path.getsize(path) - data[0]
        self.offset, self.frames = data[0], min(data[1], avail) // align

    def read(self, start, count):
        """frames [start, start+count) as float32 (frames, ch)"""
        count = max(0, min(count, self.frames - start))
        with open(self.path, "rb") as f:
            f.seek(self.offset + start * self.align)
            raw = f.read(count * self.align)
        b = self.bits // 8
        if self.float and b == 4: x = np.frombuffer(raw, "<f4").astype(np.float32)
        elif b == 2: x = np.frombuffer(raw, "<i2").astype(np.float32) / 32768.0
        elif b == 3:
            u = np.frombuffer(raw, np.uint8).reshape(-1, 3).astype(np.int32)
            v = u[:, 0] | (u[:, 1] << 8) | (u[:, 2] << 16)
            x = (np.where(v >= 1 << 23, v - (1 << 24), v)).astype(np.float32) / float(1 << 23)
        elif b == 4: x = np.frombuffer(raw, "<i4").astype(np.float32) / float(1 << 31)
        else: raise ValueError("bits %d" % self.bits)
        return x.reshape(-1, self.ch)


def to_channels(x, path, want):
    """downmix to `want` channels: AmbiX (W Y Z X) -> L/R = W +- Y; surround FL FR C LFE SL SR -> stereo"""
    ch = x.shape[1]
    if ch == 1: st = np.repeat(x, 2, axis=1)
    elif ch == 2: st = x
    elif "ambix" in path.lower() and ch >= 4:
        st = np.stack([x[:, 0] + 0.7 * x[:, 1], x[:, 0] - 0.7 * x[:, 1]], axis=1) * 0.7
    elif ch >= 6:
        st = np.stack([x[:, 0] + 0.7 * x[:, 2] + 0.7 * x[:, 4], x[:, 1] + 0.7 * x[:, 2] + 0.7 * x[:, 5]], axis=1) * 0.5
    else: st = x[:, :2]
    return st.mean(axis=1, keepdims=True) if want == 1 else st


def resample(x, rate):
    """integer decimation to 48 kHz (a windowed-sinc low-pass first); other rates are kept"""
    if rate == OUT_RATE or rate % OUT_RATE: return x, rate
    k = rate // OUT_RATE
    n = 64 * k + 1
    t = np.arange(n) - (n - 1) / 2
    h = np.sinc(t / k) / k * np.blackman(n)
    y = np.stack([np.convolve(x[:, c], h, mode="same")[::k] for c in range(x.shape[1])], axis=1)
    return y.astype(np.float32), OUT_RATE


def envelope(w, block):
    """0.25 s (or `block`) RMS of the mono mix over the whole file, read in chunks"""
    out = []; pos = 0; step = block * 400
    while pos < w.frames:
        x = w.read(pos, step).mean(axis=1)
        m = len(x) // block
        if m: out.append(np.sqrt(np.mean(x[: m * block].reshape(m, block) ** 2, axis=1) + 1e-12))
        pos += step
    return np.concatenate(out) if out else np.zeros(0)


def write(path, x, rate):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with open(path, "wb") as f:
        data = pcm.tobytes()
        ch = x.shape[1]
        f.write(b"RIFF" + struct.pack("<I", 36 + len(data)) + b"WAVE")
        f.write(b"fmt " + struct.pack("<IHHIIHH", 16, 1, ch, rate, rate * ch * 2, ch * 2, 16))
        f.write(b"data" + struct.pack("<I", len(data)) + data)


def db(v): return 20 * math.log10(max(v, 1e-9))


# ---- the cuts ------------------------------------------------------------------------------------------------------------------
def cut_loop(w, path, seconds, want, target, pick):
    block = w.rate // 4
    env = envelope(w, block)
    win = int(seconds * 4)
    fade_s = min(2.0, seconds * 0.15)
    pre = int(math.ceil(fade_s * 4)) + 1
    if len(env) < win + pre + 2: win = max(4, len(env) - pre - 2)
    ldb = 20 * np.log10(env + 1e-9)
    best, score = pre, None
    for s in range(pre, len(env) - win, max(1, win // 8)):
        seg = ldb[s:s + win]
        if pick == "loop_quiet": sc = np.mean(seg) + 0.5 * np.std(seg)
        elif pick == "loop_loud": sc = -np.mean(seg) + 0.5 * np.std(seg)
        else: sc = np.std(seg) + 0.15 * max(0.0, -55 - np.mean(seg)) + 0.25 * (np.max(seg) - np.mean(seg))
        if score is None or sc < score: best, score = s, sc
    start = best * block
    n = win * block
    fade = int(fade_s * w.rate)
    x = to_channels(w.read(start - fade, n + fade), path, want)
    head, body = x[:fade], x[fade:]
    # seamless: the body's last `fade` frames cross into what came just before the body's start
    t = np.linspace(0, 1, fade, dtype=np.float32)[:, None]
    body[-fade:] = body[-fade:] * np.cos(t * math.pi / 2) + head * np.sin(t * math.pi / 2)
    body, rate = resample(body, w.rate)
    rms = float(np.sqrt(np.mean(body ** 2)))
    return [body * (10 ** ((target - db(rms)) / 20))], rate, "window %.1f-%.1f s" % (start / w.rate, (start + n) / w.rate)


def cut_shots(w, path, seconds, want, count, target, min_s=0.0):
    block = max(1, w.rate // 200)                     # 5 ms
    env = envelope(w, block)
    if len(env) == 0: return [], w.rate, "empty"
    ldb = 20 * np.log10(env + 1e-9)
    floor = np.percentile(ldb, 20)
    rise = ldb[1:] - ldb[:-1]
    cand = np.where((rise > 9) & (ldb[1:] > floor + 20))[0] + 1
    gap = int(0.35 * 200)
    onsets = []
    for c in cand:
        if not onsets or c - onsets[-1] > gap: onsets.append(c)
    if not onsets: onsets = [int(np.argmax(ldb))]
    peaks = [(float(np.max(ldb[o:o + 40])), o) for o in onsets]
    peaks.sort(reverse=True)
    chosen = [o for _, o in peaks[:count]]                       # the loudest separate onsets
    outs = []
    maxb = int(seconds * 200)
    for o in sorted(chosen):
        pk = float(np.max(ldb[o:o + 40]))
        end = o + 8
        minb = max(20, int(min_s * 200))
        while end < min(len(ldb), o + maxb) and not (ldb[end] < pk - 50 and end > o + minb): end += 1
        s0 = max(0, (o - 2) * block)
        x = to_channels(w.read(s0, (end - o + 2) * block), path, want)
        f = min(len(x), int(0.05 * w.rate))
        x[-f:] *= np.linspace(1, 0, f, dtype=np.float32)[:, None]
        x, rate = resample(x, w.rate)
        peak = float(np.max(np.abs(x)) + 1e-9)
        outs.append(x * (10 ** ((target - db(peak)) / 20)))
    return outs, OUT_RATE if w.rate % OUT_RATE == 0 else w.rate, "%d onsets, kept %d" % (len(onsets), len(outs))


def main():
    raw, out = sys.argv[1], sys.argv[2]
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    files = []
    for d, _, fs in os.walk(raw):
        for f in fs:
            if f.lower().endswith(".wav"): files.append(os.path.join(d, f))
    total = 0
    recipes = {"gdc": R, "pro": R_PRO, "gdc23": R_GDC23}[sys.argv[sys.argv.index("--set") + 1] if "--set" in sys.argv else "gdc"]
    for rec in recipes:
        name, src, mode, seconds, want, count, target = rec[:7]
        min_s = rec[7] if len(rec) > 7 else 0.0
        if only and only not in name: continue
        hits = sorted(p for p in files if src.lower() in p.lower())
        if not hits: print("MISSING", name, "<-", src); continue
        path = hits[0] if mode != "shots" else None
        try:
            if mode == "loopfile":   # a finished loop: converted whole (channels, rate, level)
                w = Wav(hits[0])
                x = to_channels(w.read(0, w.frames), hits[0], want)
                x, rate = resample(x, w.rate)
                rms = float(np.sqrt(np.mean(x ** 2)))
                outs, why = [x * (10 ** ((target - db(rms)) / 20))], "whole file"
            elif mode.startswith("loop"):
                w = Wav(hits[0])
                outs, rate, why = cut_loop(w, hits[0], seconds, want, target, mode)
            else:
                outs = []; why = []
                per = max(1, -(-count // len(hits)))
                for p in hits:
                    w = Wav(p)
                    o, rate, y = cut_shots(w, p, seconds, want, per, target, min_s)
                    outs += o; why.append(y)
                    if len(outs) >= count: break
                outs = outs[:count]; why = "; ".join(why)
        except Exception as e:
            print("FAILED", name, e); continue
        for i, x in enumerate(outs):
            fn = os.path.join(out, *(name + ("_%02d" % (i + 1) if mode == "shots" else "") + ".wav").split("/"))
            write(fn, x, rate)
            total += os.path.getsize(fn)
        src_desc = os.path.basename(hits[0])[:60]
        print("%-24s %-5s %2d file(s) %5.1f s  %s  <- %s" % (name, mode, len(outs), sum(len(x) for x in outs) / rate, why, src_desc), flush=True)
    print("total %.0f MB" % (total / 1e6))


if __name__ == "__main__":
    main()
