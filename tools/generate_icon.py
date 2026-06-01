"""Generate icon.ico and icon_256.png from SVG-style design using Pillow.

Usage: python tools/generate_icon.py

Outputs:
  assets/icon.ico - Multi-size ICO (16,32,48,64,128,256)
  assets/icon_256.png - 256x256 PNG reference
"""
import math
from PIL import Image, ImageDraw, ImageFont

def create_icon_simple(size=256):
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    
    scale = size / 512.0
    cx, cy = size / 2, size / 2
    
    outer_r = int(220 * scale)
    inner_r = int(132 * scale)
    
    for i in range(8):
        angle = i * 45
        rad = math.radians(angle)
        half_w = int(28 * scale)
        base_r = int(176 * scale)
        tip_r = int(248 * scale)
        
        cos_a = math.cos(rad)
        sin_a = math.sin(rad)
        
        def rotate(x, y):
            rx = x * cos_a - y * sin_a
            ry = x * sin_a + y * cos_a
            return (cx + rx, cy + ry)
        
        tl = rotate(-half_w, -base_r)
        tr = rotate(half_w, -base_r)
        br = rotate(half_w, -tip_r)
        bl = rotate(-half_w, -tip_r)
        
        draw.polygon([tl, tr, br, bl], fill=(30, 136, 229))
    
    draw.ellipse([cx - outer_r, cy - outer_r, cx + outer_r, cy + outer_r],
                 fill=(0, 86, 155), outline=(0, 58, 110), width=max(1, int(2 * scale)))
    
    draw.ellipse([cx - outer_r + 4*scale, cy - outer_r + 4*scale, cx, cy],
                 fill=(30, 136, 229, 60))
    
    draw.ellipse([cx - inner_r, cy - inner_r, cx + inner_r, cy + inner_r],
                 fill=(255, 255, 255), outline=(255, 102, 0), width=max(1, int(4 * scale)))
    
    bar_x = int(cx - 108 * scale)
    bar_y1 = int(cy - 56 * scale)
    bar_y2 = int(cy + 56 * scale)
    bar_w = int(5 * scale)
    draw.rounded_rectangle([bar_x, bar_y1, bar_x + bar_w, bar_y2],
                           radius=max(1, int(2 * scale)),
                           fill=(255, 102, 0, 50))
    
    font_size = int(110 * scale)
    font = None
    for font_path in [
        'C:/Windows/Fonts/consola.ttf',
        'C:/Windows/Fonts/cour.ttf',
    ]:
        try:
            font = ImageFont.truetype(font_path, font_size)
            break
        except OSError:
            continue
    
    if font is None:
        font = ImageFont.load_default(size=font_size)
    
    s_text = "S"
    s_bbox = draw.textbbox((0, 0), s_text, font=font)
    s_w = s_bbox[2] - s_bbox[0]
    s_h = s_bbox[3] - s_bbox[1]
    
    t_text = "T"
    t_bbox = draw.textbbox((0, 0), t_text, font=font)
    t_w = t_bbox[2] - t_bbox[0]
    
    total_w = s_w + t_w
    x_start = cx - total_w / 2
    y_center = cy
    
    draw.text((x_start, y_center - s_h / 2), s_text, fill=(0, 86, 155), font=font)
    draw.text((x_start + s_w, y_center - s_h / 2), t_text, fill=(255, 102, 0), font=font)
    
    return img

if __name__ == '__main__':
    import os
    
    script_dir = os.path.dirname(os.path.abspath(__file__))
    project_dir = os.path.dirname(script_dir)
    assets_dir = os.path.join(project_dir, 'assets')
    os.makedirs(assets_dir, exist_ok=True)
    
    base_img = create_icon_simple(256)
    
    sizes = [16, 32, 48, 64, 128, 256]
    images = []
    for s in sizes:
        if s == 256:
            images.append(base_img)
        else:
            images.append(base_img.resize((s, s), Image.LANCZOS))
    
    ico_path = os.path.join(assets_dir, 'icon.ico')
    images[-1].save(ico_path, format='ICO', append_images=images[:-1])
    
    png_path = os.path.join(assets_dir, 'icon_256.png')
    base_img.save(png_path, format='PNG')
    
    print(f"Generated {ico_path}")
    print(f"Generated {png_path}")