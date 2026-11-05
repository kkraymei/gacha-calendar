"""Draws the app icon (calendar sheet with timeline bars) as PNG and ICO.

    python script/make_icon.py
"""
from pathlib import Path
from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "src" / "GachaCalendar.App" / "Assets"
S = 1024


def draw() -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = 72
    # Sheet with a coloured header band.
    d.rounded_rectangle([pad, pad + 40, S - pad, S - pad], radius=180, fill=(250, 251, 255, 255))
    d.rounded_rectangle([pad, pad + 40, S - pad, pad + 300], radius=180, fill=(91, 108, 240, 255))
    d.rectangle([pad, pad + 200, S - pad, pad + 300], fill=(91, 108, 240, 255))
    # Binder rings.
    for x in (330, S - 330):
        d.rounded_rectangle([x - 34, pad - 10, x + 34, pad + 150], radius=34, fill=(52, 61, 140, 255))
    # Timeline bars in kind colours.
    bars = [(200, 420, 640, (232, 87, 125)), (330, 540, 830, (59, 130, 246)), (250, 660, 560, (245, 158, 11)), (420, 780, 820, (20, 184, 166))]
    for x0, y, x1, color in bars:
        d.rounded_rectangle([x0, y, x1, y + 78], radius=39, fill=color + (255,))
    # Today marker.
    d.rounded_rectangle([600, 380, 624, 890], radius=12, fill=(30, 35, 60, 230))
    return img


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    icon = draw()
    icon.save(OUT / "icon.png")
    icon.save(OUT / "app.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])


if __name__ == "__main__":
    main()
