from __future__ import annotations

import os
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


source = Path(os.environ["RENDER_DIR"])
output = Path(os.environ["CONTACT_DIR"])
output.mkdir(parents=True, exist_ok=True)

pages = sorted(source.glob("page-*.png"), key=lambda p: int(p.stem.split("-")[1]))
thumb_width = 330
label_height = 34
columns = 4
rows = 3
per_sheet = columns * rows

for sheet_index in range(0, len(pages), per_sheet):
    batch = pages[sheet_index : sheet_index + per_sheet]
    thumbs = []
    max_height = 0
    for page in batch:
        image = Image.open(page).convert("RGB")
        height = round(image.height * thumb_width / image.width)
        image = image.resize((thumb_width, height), Image.Resampling.LANCZOS)
        thumbs.append((page, image))
        max_height = max(max_height, height)

    canvas = Image.new("RGB", (columns * thumb_width, rows * (max_height + label_height)), "#303030")
    draw = ImageDraw.Draw(canvas)
    for index, (page, image) in enumerate(thumbs):
        x = (index % columns) * thumb_width
        y = (index // columns) * (max_height + label_height)
        canvas.paste(image, (x, y + label_height))
        number = page.stem.split("-")[1]
        draw.text((x + 10, y + 8), f"Page {number}", fill="white")

    target = output / f"contact-{sheet_index // per_sheet + 1}.jpg"
    canvas.save(target, quality=88)
    print(target)
