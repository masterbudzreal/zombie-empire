"""Zombie Empire icon: 8x8 pixel-art zombie face with glowing white Herobrine eyes.

Usage: py -3 make_icon.py <output_dir>   -> zombieempire.png (256), zombieempire.ico (16..256)
"""
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

# palette
K = (24, 46, 22)     # dark shadow (nose / mouth)
D = (44, 84, 38)     # dark green
M = (62, 114, 52)    # mid green
L = (88, 150, 70)    # light green
T = (70, 128, 84)    # teal-ish green, zombie skin variation
W = (255, 255, 255)  # Herobrine eyes

FACE = [
    [D, D, M, D, M, M, D, D],
    [D, M, L, M, M, T, M, D],
    [M, L, M, T, L, M, L, M],
    [M, W, W, M, M, W, W, M],
    [T, M, L, D, D, L, M, T],
    [L, M, T, M, M, T, M, L],
    [M, T, K, K, K, K, M, M],
    [D, M, M, T, M, M, T, D],
]


def render(size=256, seed=7):
    rng = np.random.default_rng(seed)
    px = size // 8
    img = np.zeros((size, size, 3), np.float32)
    eyes = np.zeros((size, size), np.float32)
    for y, row in enumerate(FACE):
        for x, c in enumerate(row):
            block = np.array(c, np.float32)
            if c != W:
                # subtle per-pixel texture, like Minecraft skins
                block = block * rng.uniform(0.9, 1.08)
            img[y * px:(y + 1) * px, x * px:(x + 1) * px] = block
            if c == W:
                eyes[y * px:(y + 1) * px, x * px:(x + 1) * px] = 1

    # glow around the eyes: white bloom bleeding onto the skin
    glow = Image.fromarray((eyes * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(px * 0.55))
    g = np.asarray(glow, np.float32)[..., None] / 255
    img = img * (1 - g * 0.85) + 255 * g * 0.85
    img = np.where(eyes[..., None] > 0, 255, img)

    # thin dark frame so it reads well on any background
    edge = max(1, size // 64)
    img[:edge], img[-edge:], img[:, :edge], img[:, -edge:] = 18, 18, 18, 18
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), "RGB").convert("RGBA")


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    big = render(256)
    big.save(os.path.join(out, "zombieempire.png"))
    sizes = [256, 128, 64, 48, 32, 24, 16]
    # small sizes rendered natively (crisp pixels, no blurry downscale)
    frames = {s: render(s) for s in sizes}
    big.save(os.path.join(out, "zombieempire.ico"), sizes=[(s, s) for s in sizes],
             append_images=[frames[s] for s in sizes if s != 256])
    print("ok", sorted(frames))


if __name__ == "__main__":
    main()
