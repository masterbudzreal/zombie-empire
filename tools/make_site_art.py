"""Site art for Zombie Empire: pixel-font title, social share image, favicon.

Usage: py -3 make_art.py <icon_png> <out_assets_dir>
"""
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

# 5x7 pixel font, only the glyphs we need
GLYPHS = {
    "Z": ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
    "O": ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
    "M": ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
    "B": ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
    "I": ["11111", "00100", "00100", "00100", "00100", "00100", "11111"],
    "E": ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
    "P": ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
    "R": ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
    " ": ["000", "000", "000", "000", "000", "000", "000"],
}


def text_mask(text, px):
    cols = []
    for ch in text:
        g = GLYPHS[ch]
        w = len(g[0])
        for x in range(w):
            cols.append([int(g[y][x]) for y in range(7)])
        cols.append([0] * 7)  # letter spacing
    m = np.array(cols, np.uint8).T  # 7 x W
    big = np.kron(m, np.ones((px, px), np.uint8))
    bold = big.copy()  # faux-bold: thicken every stroke by half a block
    h = px // 2
    bold[:, h:] |= big[:, :-h]
    bold[h:, :] |= big[:-h, :]
    return bold


def title_image(text="ZOMBIE EMPIRE", px=12, seed=3):
    rng = np.random.default_rng(seed)
    mask = text_mask(text, px)
    h, w = mask.shape
    pad = px * 3
    H, W = h + pad * 2 + px * 4, w + pad * 2
    rgba = np.zeros((H, W, 4), np.float32)

    # blood drips under random letter columns
    drip = np.zeros((H, W), np.uint8)
    base = np.zeros((H, W), np.uint8)
    base[pad:pad + h, pad:pad + w] = mask
    for x0 in range(0, w, px):
        col = mask[-px:, x0:x0 + px]
        if col.any() and rng.random() < 0.28:
            length = int(rng.integers(1, 5)) * px
            drip[pad + h:pad + h + length, pad + x0:pad + x0 + px] = 1

    # vertical gradient red -> dark red, pixel noise per block
    y = np.linspace(0, 1, H)[:, None]
    red = np.stack([215 - 90 * y, 28 - 18 * y, 24 - 14 * y], -1) * np.ones((1, W, 1))
    noise = np.kron(rng.uniform(0.85, 1.1, (H // px + 1, W // px + 1)), np.ones((px, px)))[:H, :W]
    letters = (base | drip).astype(bool)
    rgba[..., :3] = red * noise[..., None]
    rgba[..., 3] = letters * 255

    # dark pixel outline (shadow offset)
    shadow = np.zeros((H, W), bool)
    s = px // 2
    shadow[s:, s:] = letters[:-s, :-s]
    shadow &= ~letters
    rgba[shadow] = [10, 6, 6, 255]

    # soft red glow behind
    glow = Image.fromarray((letters * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(px * 1.6))
    g = np.asarray(glow, np.float32) / 255 * 0.55
    under = rgba[..., 3] == 0
    rgba[under, 0] = 200
    rgba[under, 1] = 20
    rgba[under, 2] = 20
    rgba[under, 3] = g[under] * 255
    return Image.fromarray(rgba.clip(0, 255).astype(np.uint8), "RGBA")


def social_image(icon, title):
    W, H = 1200, 630
    yy, xx = np.mgrid[0:H, 0:W]
    d = np.sqrt(((xx - W * 0.5) / W) ** 2 + ((yy - H * 0.45) / H) ** 2)
    bg = np.stack([22 + 40 * np.exp(-d * 5), 26 + 50 * np.exp(-d * 5), 22 + 30 * np.exp(-d * 5)], -1)
    img = Image.fromarray(bg.astype(np.uint8), "RGB").convert("RGBA")
    ic = icon.resize((220, 220), Image.NEAREST)
    img.alpha_composite(ic, (W // 2 - 110, 70))
    t = title.copy()
    scale = min(1.0, 1080 / t.width)
    t = t.resize((int(t.width * scale), int(t.height * scale)), Image.NEAREST)
    img.alpha_composite(t, (W // 2 - t.width // 2, 330))
    return img.convert("RGB")


def main():
    icon = Image.open(sys.argv[1]).convert("RGBA")
    out = sys.argv[2]
    os.makedirs(out, exist_ok=True)
    title = title_image()
    title.save(os.path.join(out, "title.png"), optimize=True)
    icon.save(os.path.join(out, "logo.png"), optimize=True)
    social_image(icon, title).save(os.path.join(out, "og.png"), optimize=True)
    icon.save(os.path.join(out, "favicon.ico"), sizes=[(16, 16), (32, 32), (48, 48), (64, 64)])
    print("title", title.size)


if __name__ == "__main__":
    main()
