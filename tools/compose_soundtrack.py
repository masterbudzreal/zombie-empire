"""Original 'zombies-style' soundtrack for Zombie Empire: eerie music box, drones, reverb.

Usage: py -3 -I compose.py <output_dir>
Writes menu.ogg, game1.ogg, game2.ogg (OGG Vorbis, stereo, 44.1 kHz).
"""
import os
import sys

import numpy as np
import soundfile as sf
from scipy.signal import butter, fftconvolve, sosfilt

SR = 44100
rng = np.random.default_rng(1013)


def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)


def buf(seconds):
    return np.zeros((int(seconds * SR), 2))


def add(dst, src, at, pan=0.0, gain=1.0):
    i = int(at * SR)
    if i >= len(dst):
        return
    src = src[: len(dst) - i]
    l = np.cos((pan + 1) * np.pi / 4) * gain
    r = np.sin((pan + 1) * np.pi / 4) * gain
    dst[i:i + len(src), 0] += src * l
    dst[i:i + len(src), 1] += src * r


def lowpass(x, hz, order=2):
    return sosfilt(butter(order, hz, "low", fs=SR, output="sos"), x, axis=0)


def highpass(x, hz, order=2):
    return sosfilt(butter(order, hz, "high", fs=SR, output="sos"), x, axis=0)


# ---------- instruments ----------

def music_box(f, dur=2.6, detune=0.0):
    t = np.arange(int(dur * SR)) / SR
    f = f * 2 ** (detune / 1200)
    wob = 1 + 0.0025 * np.sin(2 * np.pi * 5.2 * t)  # worn-out mechanism
    out = np.zeros_like(t)
    for ratio, amp, decay in ((1.0, 1.0, 2.2), (2.0, 0.35, 3.5), (2.76, 0.18, 6.0), (5.4, 0.08, 12.0)):
        out += amp * np.sin(2 * np.pi * f * ratio * t * wob) * np.exp(-decay * t)
    attack = np.minimum(t / 0.004, 1)
    click = rng.standard_normal(len(t)) * np.exp(-t * 400) * 0.15
    return (out * attack + click) * 0.5


def pad(freqs, dur, bright=900.0, voices=3):
    t = np.arange(int(dur * SR)) / SR
    out = np.zeros_like(t)
    for f in freqs:
        for v in range(voices):
            cents = (v - (voices - 1) / 2) * 9
            ff = f * 2 ** (cents / 1200)
            ph = rng.uniform(0, 2 * np.pi)
            vib = 1 + 0.003 * np.sin(2 * np.pi * (0.17 + 0.05 * v) * t + ph)
            for h in range(1, 7):  # soft saw
                out += np.sin(2 * np.pi * ff * h * t * vib + ph * h) / (h ** 1.4)
    out = lowpass(out, bright)
    env = np.minimum(t / (dur * 0.35), 1) * np.minimum((dur - t) / (dur * 0.35), 1)
    swell = 0.75 + 0.25 * np.sin(2 * np.pi * 0.07 * t)
    return out * env * swell / (len(freqs) * voices * 2.2)


def choir(freqs, dur):
    """Breathy 'ahh' made of filtered noise bands around each note's harmonics."""
    t = np.arange(int(dur * SR)) / SR
    out = np.zeros_like(t)
    for f in freqs:
        vib = 1 + 0.004 * np.sin(2 * np.pi * 4.8 * t + rng.uniform(0, 6))
        for h, a in ((1, 1.0), (2, 0.6), (3, 0.45), (4, 0.25), (5, 0.12)):
            out += a * np.sin(2 * np.pi * f * h * t * vib)
    out = lowpass(out, 1400) + lowpass(rng.standard_normal(len(t)), 900) * 0.04
    env = np.minimum(t / (dur * 0.4), 1) * np.minimum((dur - t) / (dur * 0.4), 1)
    return out * env / (len(freqs) * 3)


def boom(dur=4.0, f0=55.0):
    t = np.arange(int(dur * SR)) / SR
    f = f0 * (1 + 1.5 * np.exp(-t * 12))
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t * 1.1)
    hit = lowpass(rng.standard_normal(len(t)), 300) * np.exp(-t * 6) * 0.6
    return (body + hit) * 0.9


def metal(dur=5.0, f0=310.0):
    """Distant metallic clang / scrape (inharmonic partials)."""
    t = np.arange(int(dur * SR)) / SR
    out = np.zeros_like(t)
    for r, a in ((1, 1), (1.47, 0.7), (2.09, 0.5), (2.56, 0.4), (3.91, 0.25), (5.12, 0.15)):
        out += a * np.sin(2 * np.pi * f0 * r * t + rng.uniform(0, 6)) * np.exp(-t * (0.8 + r * 0.4))
    return out * 0.18


def heartbeat(dur=1.2):
    t = np.arange(int(dur * SR)) / SR
    one = np.sin(2 * np.pi * 48 * t) * np.exp(-t * 18)
    two = np.zeros_like(t)
    k = int(0.28 * SR)
    two[k:] = np.sin(2 * np.pi * 44 * t[: len(t) - k]) * np.exp(-t[: len(t) - k] * 22) * 0.7
    return (one + two) * 0.9


def crackle(seconds, density=18):
    n = int(seconds * SR)
    out = np.zeros((n, 2))
    idx = rng.integers(0, n, int(seconds * density))
    out[idx, 0] = rng.uniform(-1, 1, len(idx))
    out[idx, 1] = out[idx, 0] * rng.uniform(0.6, 1, len(idx))
    out = highpass(out, 1500)
    hiss = highpass(rng.standard_normal((n, 2)), 4000) * 0.004
    return out * 0.25 + hiss


def wind(seconds):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    noise = rng.standard_normal((n, 2))
    sweep = 0.5 + 0.5 * np.sin(2 * np.pi * 0.05 * t)[:, None]
    lo = lowpass(noise, 250)
    mid = lowpass(highpass(noise, 300), 800)
    return (lo * 0.6 + mid * sweep * 0.35) * 0.25


# ---------- mixing ----------

def reverb(x, seconds=3.4, wet=0.38):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    ir = np.stack([rng.standard_normal(n), rng.standard_normal(n)], 1) * np.exp(-t * 6.9 / seconds)[:, None]
    ir = lowpass(ir, 5000)
    ir[: int(0.02 * SR)] = 0  # pre-delay
    ir /= np.sqrt((ir ** 2).sum(0))
    out = np.stack([fftconvolve(x[:, c], ir[:, c])[: len(x)] for c in range(2)], 1)
    return x * (1 - wet) + out * wet * 1.6


def finish(x, peak_db=-1.5, fade=3.0):
    x = highpass(x, 28)
    x = np.tanh(x * 1.2) / 1.2  # gentle saturation, "old tape"
    n = int(fade * SR)
    ramp = np.linspace(0, 1, n)[:, None]
    x[:n] *= ramp
    x[-n:] *= ramp[::-1]
    x *= 10 ** (peak_db / 20) / np.max(np.abs(x))
    return x.astype(np.float32)


# ---------- tracks ----------

def track_menu():
    """Music-box waltz in D minor over a choir drone (menu theme)."""
    bpm = 76
    beat = 60 / bpm
    # (midi note, beats) - '0' = rest
    phrase_a = [(69, 1), (74, 1), (77, 1), (76, 2), (74, 1), (73, 1), (74, 1), (69, 1), (70, 3),
                (69, 1), (67, 1), (65, 1), (64, 2), (65, 1), (62, 3), (0, 3)]
    phrase_b = [(74, 1), (77, 1), (81, 1), (80, 2), (77, 1), (76, 1), (77, 1), (74, 1), (75, 3),
                (74, 1), (70, 1), (69, 1), (68, 2), (69, 1), (62, 3), (0, 3)]
    melody = phrase_a + phrase_b + phrase_a + [(n - 12 if n else 0, d) for n, d in phrase_b]
    total = sum(d for _, d in melody) * beat + 8
    mix = buf(total)

    # drone chords change every 6 beats: Dm, Bb, Gm, A (with a b9 for menace)
    chords = [[38, 45, 50, 53], [34, 41, 46, 50], [31, 43, 46, 50], [33, 45, 49, 58]]
    bar = 6 * beat
    k = 0
    while k * bar < total - 2:
        ch = chords[k % 4]
        add(mix, pad([midi(n) for n in ch], bar * 1.6, bright=650), k * bar, pan=0, gain=0.55)
        add(mix, choir([midi(n + 12) for n in ch[1:3]], bar * 1.6), k * bar, pan=(-0.3 if k % 2 else 0.3), gain=0.35)
        k += 1

    t = 2.0
    for i, (n, d) in enumerate(melody):
        if n:
            add(mix, music_box(midi(n), detune=rng.uniform(-14, 14)), t, pan=rng.uniform(-0.25, 0.25), gain=0.6)
            if i % 7 == 0:  # ghostly octave echo
                add(mix, music_box(midi(n + 12), detune=25), t + beat * 0.5, pan=0.6, gain=0.18)
        t += d * beat

    for at in (0.5, total / 2, total - 7):
        add(mix, boom(), at, gain=0.5)
    mix = reverb(mix, 3.8, 0.42)
    mix += crackle(total)
    return finish(mix)


def track_game(seed_shift, root):
    """Dark ambient: drones, sparse music box, heartbeat, distant metal (in-game)."""
    total = 150.0
    mix = buf(total)
    mix += wind(total)
    # slow drone moving between root, minor 2nd above and tritone - classic dread
    steps = [0, 1, 0, 6, 0, -2, 0, 1]
    seg = total / len(steps)
    for i, s in enumerate(steps):
        f = midi(root + s)
        add(mix, pad([f, f * 1.5, f * 2.0], seg * 1.7, bright=420, voices=4), i * seg - seg * 0.3 if i else 0, gain=0.8)
        if i % 2:
            add(mix, choir([f * 4, f * 4 * 2 ** (3 / 12)], seg * 1.5), i * seg, pan=rng.uniform(-0.5, 0.5), gain=0.3)

    # sparse, broken music-box fragments
    scale = [0, 2, 3, 5, 7, 8, 11]  # harmonic minor
    t = 6.0
    while t < total - 10:
        frag = rng.integers(2, 6)
        start = rng.integers(0, len(scale))
        for j in range(frag):
            deg = scale[(start + j * rng.choice([1, 2, -1])) % len(scale)]
            n = root + 36 + deg
            add(mix, music_box(midi(n), detune=rng.uniform(-30, 30)), t, pan=rng.uniform(-0.6, 0.6), gain=0.35)
            t += rng.choice([0.55, 0.8, 1.1])
        t += rng.uniform(6, 14)

    # heartbeat sections
    for start in (40.0 + seed_shift, 105.0 - seed_shift):
        for b in range(14):
            add(mix, heartbeat(), start + b * 0.95, gain=0.5 + b * 0.02)

    for at in rng.uniform(10, total - 10, 5):
        add(mix, metal(f0=rng.uniform(220, 420)), at, pan=rng.uniform(-0.9, 0.9), gain=0.45)
    for at in (0.3, 60.0, 120.0):
        add(mix, boom(f0=46), at + seed_shift, gain=0.55)

    mix = reverb(mix, 4.5, 0.48)
    mix += crackle(total, density=8) * 0.6
    return finish(mix, peak_db=-2.0)


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    tracks = {"menu": track_menu(), "game1": track_game(0.0, 38), "game2": track_game(7.0, 33)}
    for name, x in tracks.items():
        path = os.path.join(out, name + ".ogg")
        # libsndfile on Windows overflows the stack on big Vorbis writes: write in blocks
        with sf.SoundFile(path, "w", SR, 2, format="OGG", subtype="VORBIS") as f:
            for i in range(0, len(x), SR):
                f.write(x[i:i + SR])
        rms = 20 * np.log10(np.sqrt(np.mean(x ** 2)) + 1e-9)
        print(f"{name}: {len(x) / SR:.0f}s, rms {rms:.1f} dBFS, {os.path.getsize(path) / 1e6:.1f} MB")


if __name__ == "__main__":
    main()
