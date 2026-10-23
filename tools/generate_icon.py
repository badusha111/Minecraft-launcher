from PIL import Image, ImageDraw
import math, os

OUT = os.environ.get("NEXCRAFT_ICON_DIR", "artifacts/icon")
os.makedirs(OUT, exist_ok=True)
S = 1024
im = Image.new("RGBA", (S, S), (10, 7, 20, 255))
px = im.load()
for y in range(S):
    for x in range(S):
        dx, dy = (x-S*.5)/(S*.7), (y-S*.48)/(S*.7)
        glow = max(0, 1-math.sqrt(dx*dx+dy*dy))**2
        px[x, y] = (int(10+18*glow), int(7+5*glow), int(20+35*glow), 255)

d = ImageDraw.Draw(im)
pad = 72
d.rounded_rectangle((pad, pad, S-pad, S-pad), radius=150, outline=(111,42,214,255), width=18)
# A clean, geometric N: flat facets, no artificial-looking gloss or tiny details.
d.polygon([(270,250),(390,250),(390,770),(270,770)], fill=(161,91,255,255))
d.polygon([(270,250),(330,250),(330,770),(270,770)], fill=(194,143,255,255))
d.polygon([(634,250),(754,250),(754,770),(634,770)], fill=(111,45,220,255))
d.polygon([(694,250),(754,250),(754,770),(694,770)], fill=(151,79,246,255))
d.polygon([(350,250),(455,250),(675,770),(570,770)], fill=(137,68,240,255))
d.polygon([(350,250),(402,250),(622,770),(570,770)], fill=(188,126,255,255))
d.polygon([(455,250),(675,770),(630,770),(410,250)], fill=(106,43,207,255))
png = os.path.join(OUT, "Nexcraft.png")
ico = os.path.join(OUT, "Nexcraft.ico")
im.save(png, optimize=True)
im.save(ico, format="ICO", sizes=[(256,256),(128,128),(64,64),(48,48),(32,32),(16,16)])
print(f"Created {png} and {ico}")
