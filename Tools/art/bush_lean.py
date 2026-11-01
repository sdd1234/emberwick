"""숨는 덤불의 기운 그림 (2026-10-31).
밑동은 그대로, 위로 갈수록 몇 점씩 옆으로 민다 - 덤불을 통째로 미끄러뜨리거나
다른 모양 그림으로 바꾸지 않고 '그 덤불'이 쓸리는 것처럼 보이게 한다.
hide_bush_{a,b,c}.png -> hide_bush_{a,b,c}_lean{L2,L1,R1,R2}.png (+ pixel_meta.json 항목)
"""
import json, sys
from pathlib import Path
from PIL import Image

DIR = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[2] / "Assets/_Game/Art/Pixel"
PAD = 6                                   # 기운 만큼 캔버스를 좌우로 넓힌다 (발밑 가운데 피벗은 그대로)
LEANS = {"L2": -5, "L1": -2, "R1": 2, "R2": 5}   # 꼭대기가 밀리는 점 수
CURVE = 1.7                               # 클수록 밑동은 덜, 꼭대기만 휜다

meta_path = DIR / "pixel_meta.json"
meta = json.loads(meta_path.read_text(encoding="utf-8"))
for base in ("hide_bush_a", "hide_bush_b", "hide_bush_c"):
    src = Image.open(DIR / f"{base}.png").convert("RGBA")
    w, h = src.size
    bbox = src.getbbox()
    top, bottom = bbox[1], bbox[3]
    for tag, lean in LEANS.items():
        out = Image.new("RGBA", (w + PAD * 2, h), (0, 0, 0, 0))
        for y in range(h):
            k = max(0.0, (bottom - 1 - y) / max(1, bottom - 1 - top))   # 밑동 0 -> 꼭대기 1
            dx = round(lean * k ** CURVE)
            out.paste(src.crop((0, y, w, y + 1)), (PAD + dx, y))
        name = f"{base}_lean{tag}"
        out.save(DIR / f"{name}.png")
        e = dict(meta.get(base, {"ppu": 38.0, "pivot": [0.5, 0.0]}))
        e["w"], e["h"] = out.size
        meta[name] = e
meta_path.write_text(json.dumps(meta, ensure_ascii=False, indent=1), encoding="utf-8")
print("ok")
