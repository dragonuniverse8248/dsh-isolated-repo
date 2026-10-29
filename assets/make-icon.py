#!/usr/bin/env python
"""DSH 隔离版图标：去掉四角黑底 -> 透明圆角 -> icon.png + 多尺寸 app.ico。"""
import io
import os
import struct
from collections import deque

import numpy as np
from PIL import Image

# 用户提供的头像原图（附件快照）；丢失时回退到同目录 icon.png
SRC = r"D:\DeepSeek_Harness\dsh-home\attachments\v1\objects\b7\b781fb265233f65e33c3698246a5183845a3dd3a9eab9252a2805c3edcc275c2"
HERE = os.path.dirname(os.path.abspath(__file__))
SIZES = [16, 24, 32, 48, 64, 128, 256]


def remove_black_corners(image):
    """把圆角卡片外侧的黑色背景抠成透明（只处理与四角相连的纯黑区域）。"""
    px = np.array(image)
    black = (px[:, :, 0] < 16) & (px[:, :, 1] < 16) & (px[:, :, 2] < 16)
    height, width = black.shape
    filled = np.zeros_like(black)
    queue = deque()
    for y, x in ((0, 0), (0, width - 1), (height - 1, 0), (height - 1, width - 1)):
        if black[y, x] and not filled[y, x]:
            filled[y, x] = True
            queue.append((y, x))
    while queue:
        y, x = queue.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < height and 0 <= nx < width and black[ny, nx] and not filled[ny, nx]:
                filled[ny, nx] = True
                queue.append((ny, nx))
    alpha = px[:, :, 3]
    alpha[filled] = 0
    return Image.fromarray(px, "RGBA")


def pack_ico(image, path):
    """按 16..256 手工打包 ICO（每帧内嵌优化过的 PNG）。"""
    entries = []
    blobs = b""
    offset = 6 + 16 * len(SIZES)
    for size in SIZES:
        frame = image.resize((size, size), Image.LANCZOS)
        buffer = io.BytesIO()
        frame.save(buffer, "PNG", optimize=True, compress_level=9)
        png = buffer.getvalue()
        side = 0 if size >= 256 else size
        entries.append(struct.pack("<BBBBHHII", side, side, 0, 0, 1, 32, len(png), offset))
        offset += len(png)
        blobs += png
    with open(path, "wb") as handle:
        handle.write(struct.pack("<HHH", 0, 1, len(SIZES)))
        for entry in entries:
            handle.write(entry)
        handle.write(blobs)


def main():
    source = SRC if os.path.exists(SRC) else os.path.join(HERE, "icon.png")
    image = remove_black_corners(Image.open(source).convert("RGBA"))
    image.resize((512, 512), Image.LANCZOS).save(os.path.join(HERE, "icon.png"), "PNG", optimize=True)
    pack_ico(image, os.path.join(HERE, "app.ico"))
    print("icon.png (512) + app.ico", SIZES)


if __name__ == "__main__":
    main()
