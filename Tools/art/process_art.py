"""
카톡으로 받은 원화(2026-10-10)를 게임에 넣을 수 있게 가공한다.
  uv run --with pillow --with numpy --with "imageio[pyav]" process_art.py <원본 폴더> <capstone 루트>

원본 폴더 구조 (압축을 푼 그대로):
  rogue/  도적 대기 · 걷기 · 걷기2 · 공격
  cult/   감염자 대기1·2 · 감염자걸음1·2 · 공격1·2 · 이교도 대기 · 걷기1·2 · 사격 · 장전 · 사제 모션 대기
  work/   가문비 · 덤불 · 가지 · 바닥 타일
  v1.mp4  주인공 대기 (채색)
  v2.mp4  주인공 걷기 (선화)

하는 일
  1) 캐릭터 프레임은 한 세트(같은 캐릭터)의 합집합 영역으로 잘라 반으로 줄인다. 원본 캔버스 아래 가운데가
     발밑이 되도록 잘라야 프레임끼리 자리가 안 흔들린다 → Unity 피벗은 (발 x, 0)
  2) 흰 배경(사제 이미지 · 영상)은 테두리에서 이어진 흰색만 지운다. 옷 안쪽 흰색은 남는다
  3) 걷기 영상은 선화뿐이라, 대기 영상의 색을 같은 자리에서 가져와 닫힌 영역마다 칠한다
"""
import os, sys, json
import numpy as np
from PIL import Image
from collections import deque

SRC, DST = sys.argv[1], sys.argv[2]
ART = os.path.join(DST, "Assets", "_Game", "Art")
meta = {}

def save(img, rel, pivot):
    p = os.path.join(ART, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    img.save(p)
    meta[rel.replace("\\", "/")] = {"pivot": pivot, "size": img.size}

def remove_white_bg(rgba, thr=232, min_hole=150):
    """테두리에서 이어진 밝은 픽셀만 투명하게. 안쪽 흰색은 건드리지 않는다."""
    a = np.array(rgba).copy()
    h, w = a.shape[:2]
    bright = a[..., :3].min(axis=2) >= thr
    bg = np.zeros((h, w), bool)
    q = deque()
    for x in range(w):
        for y in (0, h - 1):
            if bright[y, x] and not bg[y, x]: bg[y, x] = True; q.append((y, x))
    for y in range(h):
        for x in (0, w - 1):
            if bright[y, x] and not bg[y, x]: bg[y, x] = True; q.append((y, x))
    while q:
        y, x = q.popleft()
        for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
            ny, nx = y+dy, x+dx
            if 0 <= ny < h and 0 <= nx < w and bright[ny, nx] and not bg[ny, nx]:
                bg[ny, nx] = True; q.append((ny, nx))
    # 팔과 몸 사이처럼 테두리와 안 이어진 배경도 있다. 거의 순백이고 일정 크기 이상인 덩어리는 배경으로 본다
    # (눈 흰자처럼 작은 흰색은 남긴다)
    white = (a[..., :3].min(axis=2) >= 238) & ~bg
    seen = np.zeros((h, w), bool)
    for sy, sx in zip(*np.where(white)):
        if seen[sy, sx]: continue
        comp = []; q2 = deque([(sy, sx)]); seen[sy, sx] = True
        while q2:
            y, x = q2.popleft(); comp.append((y, x))
            for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
                ny, nx = y+dy, x+dx
                if 0 <= ny < h and 0 <= nx < w and white[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = True; q2.append((ny, nx))
        if len(comp) >= min_hole:
            ys, xs = zip(*comp); bg[ys, xs] = True
    a[bg, 3] = 0
    # 외곽선 바로 바깥의 옅은 회색 테두리(압축 번짐)를 반투명으로 풀어 흰 테가 안 보이게
    edge = (~bg) & np.pad(bg, 1)[2:, 1:-1] | (~bg) & np.pad(bg, 1)[:-2, 1:-1] | (~bg) & np.pad(bg, 1)[1:-1, 2:] | (~bg) & np.pad(bg, 1)[1:-1, :-2]
    lum = a[..., :3].mean(axis=2)
    soft = edge & (lum > 150)
    a[soft, 3] = np.clip((255 - lum[soft]) * 2.2, 0, 255).astype(np.uint8)
    return Image.fromarray(a)

def char_set(name, files, out_dir, scale=0.5, canvas_foot=None):
    """같은 캐릭터 프레임들을 합집합 영역으로 잘라 저장. 발밑 = 캔버스 아래 가운데."""
    ims = [f if isinstance(f[1], Image.Image) else (f[0], Image.open(f[1]).convert("RGBA")) for f in files]
    W, H = ims[0][1].size
    boxes = [im.getchannel("A").getbbox() for _, im in ims]
    x0 = min(b[0] for b in boxes); x1 = max(b[2] for b in boxes); y0 = min(b[1] for b in boxes)
    y1 = H if canvas_foot is None else canvas_foot
    y1 = max(y1, max(b[3] for b in boxes))
    foot_x = W / 2
    for key, im in ims:
        c = im.crop((x0, y0, x1, y1))
        c = c.resize((max(1, int(c.width * scale)), max(1, int(c.height * scale))), Image.LANCZOS)
        piv = ((foot_x - x0) / (x1 - x0), (y1 - (canvas_foot or H)) / (y1 - y0))
        save(c, f"{out_dir}/{key}.png", [round(piv[0], 4), round(piv[1], 4)])
    print(name, "crop", (x0, y0, x1, y1), "→", c.size)

R = lambda *p: os.path.join(SRC, *p)

# ---------- 적 ----------
char_set("rogue", [("rogue_idle", R("rogue", "도적 대기.png")), ("rogue_walk_0", R("rogue", "도적 걷기.png")),
                   ("rogue_walk_1", R("rogue", "도적 걷기2.png")), ("rogue_attack", R("rogue", "도적 공격.png"))],
         "Enemies/Rogue")
char_set("infected", [("infected_idle_0", R("cult", "감염자 대기1.png")), ("infected_idle_1", R("cult", "감염자 대기2.png")),
                      ("infected_walk_0", R("cult", "감염자걸음1.png")), ("infected_walk_1", R("cult", "감염자걸음2.png")),
                      ("infected_attack_0", R("cult", "공격1.png")), ("infected_attack_1", R("cult", "공격2.png"))],
         "Enemies/Infected")
priest = remove_white_bg(Image.open(R("cult", "사제 모션 대기.png")).convert("RGBA"))
char_set("cultist", [("cultist_idle", R("cult", "이교도 대기.png")), ("cultist_walk_0", R("cult", "이교도 걷기1.png")),
                     ("cultist_walk_1", R("cult", "이교도 걷기2.png")), ("cultist_shoot", R("cult", "이교도 사격.png")),
                     ("cultist_reload", R("cult", "이교도 장전.png")), ("priest_idle", priest)],
         "Enemies/Cultist")

# ---------- 주인공 (영상) ----------
import imageio.v3 as iio
def frames(v): return [np.array(f) for f in iio.imiter(R(v), plugin="pyav")]
v1, v2 = frames("v1.mp4"), frames("v2.mp4")
idle = [remove_white_bg(Image.fromarray(v1[i]).convert("RGBA")) for i in (0, 6)]
# 선화는 안쪽이 전부 흰색(칠할 자리)이라 안쪽 흰 덩어리 제거를 끈다
walk_line = [remove_white_bg(Image.fromarray(v2[i]).convert("RGBA"), min_hole=10**9) for i in (6, 9, 12, 15)]

def colorize(line_img, ref_img):
    """선화의 닫힌 영역마다, 대기 원화에서 같은 자리 색의 중앙값을 칠한다.
    상체(머리 · 망토 · 몸통 · 벨트)는 두 포즈가 거의 같은 자리에 있어 색이 맞는다.
    다리처럼 자리가 다른 곳은 대기 원화의 다리 색(바지)으로 떨어진다."""
    L = np.array(line_img).astype(int); Rf = np.array(ref_img).astype(int)
    lb = Image.fromarray(L.astype(np.uint8)).getchannel("A").getbbox(); rb = ref_img.getchannel("A").getbbox()
    # 머리 · 어깨의 선이 가장 많이 겹치는 위치를 찾아 맞춘다. 걸을 때 머리가 위아래로 출렁이고
    # 다리를 벌리면 전체 영역이 넓어져서, 영역 기준으로 맞추면 머리카락 자리에 얼굴색이 들어간다
    def lines(arr, bb):
        top = arr[bb[1]:bb[1] + int((bb[3] - bb[1]) * 0.35)]
        return (top[..., 3] > 0) & (top[..., :3].max(axis=2) < 110), bb[1]
    lm, ly = lines(L, lb); rm, ry = lines(Rf, rb)
    best = (-1, 0, 0)
    for oy in range(-40, 41, 2):
        for ox in range(-60, 61, 2):
            ys, xs = np.where(lm[::2, ::2]); ys = ys * 2 + ly + oy - ry; xs = xs * 2 + ox
            ok = (ys >= 0) & (ys < rm.shape[0]) & (xs >= 0) & (xs < rm.shape[1])
            score = rm[ys[ok], xs[ok]].sum()
            if score > best[0]: best = (score, ox, oy)
    dx, dy = best[1], best[2]
    h, w = L.shape[:2]
    opaque = L[..., 3] > 0
    dark = L[..., :3].max(axis=2) < 110          # 선 · 검은 칠
    fill = opaque & ~dark
    lab = -np.ones((h, w), int); regions = []
    for y in range(h):
        for x in range(w):
            if fill[y, x] and lab[y, x] < 0:
                rid = len(regions); q = deque([(y, x)]); lab[y, x] = rid; px = []
                while q:
                    cy, cx = q.popleft(); px.append((cy, cx))
                    for ny, nx in ((cy+1,cx),(cy-1,cx),(cy,cx+1),(cy,cx-1)):
                        if 0 <= ny < h and 0 <= nx < w and fill[ny, nx] and lab[ny, nx] < 0:
                            lab[ny, nx] = rid; q.append((ny, nx))
                regions.append(px)
    pants = np.median(Rf[int(rb[1] + (rb[3]-rb[1]) * 0.75), rb[0]:rb[2]][Rf[int(rb[1] + (rb[3]-rb[1]) * 0.75), rb[0]:rb[2], 3] > 0][:, :3], axis=0)
    out = L.copy()
    for px in regions:
        cols = []
        for (y, x) in px[::3]:
            ry, rx = y + dy, x + dx
            if 0 <= ry < Rf.shape[0] and 0 <= rx < Rf.shape[1] and Rf[ry, rx, 3] > 200 and Rf[ry, rx, :3].max() >= 60:
                cols.append(Rf[ry, rx, :3])
        c = np.median(np.array(cols), axis=0) if len(cols) >= max(3, len(px) // 30) else pants
        ys, xs = zip(*px)
        out[ys, xs, :3] = c
    return Image.fromarray(out.astype(np.uint8))

walk = [colorize(f, idle[0]) for f in walk_line]
H = idle[0].size[1]
char_set("player", [("player_idle_0", idle[0]), ("player_idle_1", idle[1])] +
                   [(f"player_walk_{i}", f) for i, f in enumerate(walk)] +
                   [(f"player_walkline_{i}", f) for i, f in enumerate(walk_line)],
         "Characters/Hero", scale=1.0, canvas_foot=None)

# ---------- 배경 ----------
def env(src, rel, pivot):
    im = Image.open(R("work", src)).convert("RGBA")
    bb = im.getchannel("A").getbbox(); im = im.crop(bb)
    save(im, rel, pivot)
env("큰 가문비.PNG", "Env/Forest/spruce_big.png", [0.5, 0.0])
env("큰 가문비 2.PNG", "Env/Forest/spruce_big_crown.png", [0.5, 0.0])
env("작은 가문비.PNG", "Env/Forest/spruce_small.png", [0.5, 0.0])
env("작은 가문비 2.PNG", "Env/Forest/spruce_small_crown.png", [0.5, 0.0])
env("일러스트 20261009.PNG", "Env/Forest/bush_fern.png", [0.5, 0.15])
env("일러스트 20261009 2.PNG", "Env/Forest/bush_leaf.png", [0.5, 0.15])
env("일러스트 20261009 3.PNG", "Env/Forest/bush_leaf_2.png", [0.5, 0.15])
env("일러스트 20261009 4.PNG", "Env/Forest/branches_loose.png", [0.5, 0.5])
env("일러스트 20261009 5.PNG", "Env/Forest/branches_dense.png", [0.5, 0.5])
for i, n in enumerate(["일러스트 20261001.PNG", "일러스트 20261001 2.PNG", "일러스트 20261001 3.PNG", "일러스트 20261001 4.PNG"]):
    env(n, f"Env/Forest/ground_tile_{i}.png", [0.5, 0.5])

# ---------- 바닥 (타일 4장에서 뽑기) ----------
# 4장은 흙이 한 귀퉁이(왼위 · 오위 · 왼아래 · 오아래)에만 있는 거울상 세트다.
# 대각선으로 굽은 길을 칸 단위로 깔면 계단이 되므로, 조각을 잘라 쓴다.
#   ground_fern - 고사리 사분면을 거울로 2x2 → 이음매 없이 반복되는 바닥
#   ground_dirt - 흙 사분면을 같은 방법으로 → 길 바닥
#   road_edge   - "위는 흙, 아래는 고사리" 경계 띠. 길 양옆에 돌려 붙여 고사리가 길로 늘어지게 한다
#   dirt_disc   - 길이 꺾이는 곳을 메우는 둥근 흙 (가장자리를 흐리게)
def mirror_tile(q):
    w, h = q.size
    t = Image.new("RGBA", (w * 2, h * 2))
    t.paste(q, (0, 0)); t.paste(q.transpose(Image.FLIP_LEFT_RIGHT), (w, 0))
    t.paste(q.transpose(Image.FLIP_TOP_BOTTOM), (0, h)); t.paste(q.transpose(Image.ROTATE_180), (w, h))
    return t
t0 = Image.open(R("work", "일러스트 20261001.PNG")).convert("RGBA")   # 흙이 왼쪽 위
Q = t0.width // 2
# 원화 타일 가장자리에는 어두운 테두리 선이 있다. 그대로 거울 반복하면 1.5m 마다 격자선이 생기므로
# 테두리에서 M 픽셀 안쪽만 쓴다
M = 20
# 고사리 바닥은 거울 반복하면 만화경 같은 체크무늬가 생긴다. 대신 고사리 조각을 무작위로 돌리고 뒤집어
# 가장자리를 흐린 채 수백 장 겹쳐 붙여, 격자가 없는 1024px(8m) 바닥을 새로 짠다. 끝은 감아 붙여 이음매가 없다
def scatter_tile(patch, size=1024, count=420, seed=7):
    rng = np.random.default_rng(seed)
    pa = np.array(patch).astype(np.float32)
    ph, pw = pa.shape[:2]
    yy, xx = np.mgrid[0:ph, 0:pw]
    r = np.hypot((xx - pw / 2) / (pw / 2), (yy - ph / 2) / (ph / 2))
    feather = np.clip((1.0 - r) / 0.45, 0, 1) ** 1.5           # 가운데는 진하게, 가장자리는 흐리게
    base = pa[..., :3].reshape(-1, 3).mean(axis=0)
    acc = np.zeros((size, size, 3), np.float32); acc[:] = base
    for _ in range(count):
        q = pa.copy()
        k = rng.integers(4); q = np.rot90(q, k)
        if rng.random() < 0.5: q = q[:, ::-1]
        f = np.rot90(feather, k)
        f = f[:, ::-1] if q.shape == f.shape and rng.random() < 0 else f
        x0, y0 = rng.integers(size), rng.integers(size)
        ys = (np.arange(q.shape[0]) + y0) % size; xs = (np.arange(q.shape[1]) + x0) % size
        a = f[..., None]
        acc[np.ix_(ys, xs)] = acc[np.ix_(ys, xs)] * (1 - a) + q[..., :3] * a
    out = np.dstack([acc, np.full((size, size), 255, np.float32)]).clip(0, 255).astype(np.uint8)
    return Image.fromarray(out)
fern_patch = t0.crop((Q + 8, Q + 8, 2 * Q - M, 2 * Q - M))
save(scatter_tile(fern_patch), "Env/Forest/ground_fern.png", [0.5, 0.5])
# 흙도 거울 반복하면 이음매에 + 모양 무늬가 반복돼 보였다 → 고사리와 같은 방법으로 흩뿌려 짠다
dirt = scatter_tile(t0.crop((M, M, Q - 8, Q - 8)), seed=11)
save(dirt, "Env/Forest/ground_dirt.png", [0.5, 0.5])
band = t0.crop((0, Q - Q // 3, Q, 2 * Q))                              # 흙 1/3 + 고사리
edge = Image.new("RGBA", (band.width * 2, band.height))
edge.paste(band, (0, 0)); edge.paste(band.transpose(Image.FLIP_LEFT_RIGHT), (band.width, 0))
ea = np.array(edge); hh = ea.shape[0]
fade = np.ones(hh); top = hh // 8; bot = hh // 4
fade[:top] = np.linspace(0, 1, top); fade[-bot:] = np.linspace(1, 0, bot)     # 위아래를 흐려 바닥과 섞는다
ea[..., 3] = (ea[..., 3] * fade[:, None]).astype(np.uint8)
save(Image.fromarray(ea), "Env/Forest/road_edge.png", [0.5, 0.5])
da = np.array(dirt.resize((384, 384), Image.LANCZOS)).copy(); n = da.shape[0]
yy, xx = np.mgrid[0:n, 0:n]; r = np.hypot(xx - n / 2, yy - n / 2) / (n / 2)
da[..., 3] = (np.clip((1 - r) / 0.25, 0, 1) * 255).astype(np.uint8)
save(Image.fromarray(da), "Env/Forest/dirt_disc.png", [0.5, 0.5])

with open(os.path.join(ART, "art_import_meta.json"), "w", encoding="utf-8") as f:
    json.dump(meta, f, ensure_ascii=False, indent=1)
print("done", len(meta))

# ---------- 주인공 조준 (1프레임, 합성) ----------
# 조준 원화가 없어서 만든다: 대기 원화의 화면 왼쪽 팔을 잘라 어깨를 축으로 앞(오른쪽)으로 들고,
# 이교도 사격 원화의 석궁을 오려 좌우를 뒤집어 그 손에 쥐어 준다. 같은 작가의 그림이라 선 · 칠이 맞는다.
def make_aim():
    from PIL import ImageDraw, ImageFilter
    hero = Image.open(os.path.join(ART, "Characters/Hero/player_idle_0.png")).convert("RGBA")
    w, h = hero.size
    # 화면 왼쪽 팔(소매 + 장갑) 윤곽 - player_idle_0 (349x672) 기준 좌표. 몸통 왼쪽 선은 x≈112
    poly = [(22, 286), (113, 286), (113, 396), (130, 398), (130, 468), (56, 468), (50, 400), (22, 398)]
    mask = Image.new("L", (w, h), 0); ImageDraw.Draw(mask).polygon(poly, fill=255)
    piece = Image.new("RGBA", (w, h)); piece.paste(hero, (0, 0), mask)
    body = hero.copy(); bA = np.array(body); bA[np.array(mask) > 0, 3] = 0
    # 장갑이 덮고 있던 상의 자락(x 100~130, 장갑 높이)은 그림이 없다. 같은 줄 바로 오른쪽 색으로 메운다
    for y in range(396, 470):
        ref = bA[y, 133].copy()
        if ref[3] < 40: continue
        for x in range(100, 131):
            if bA[y, x, 3] < 40: bA[y, x] = ref
    body = Image.fromarray(bA)
    # 팔이 빠진 자리 - 몸통 왼쪽 외곽선을 다시 긋는다 (팔과 공유하던 선이라 같이 잘려 나갔다)
    d = ImageDraw.Draw(body)
    bAlpha = np.array(body)[..., 3]
    for y in range(286, 470):
        xs = np.where(bAlpha[y, :w // 2] > 40)[0]
        if len(xs): d.line([(xs[0], y), (xs[0] + 2, y)], fill=(25, 20, 22, 255))

    pad = 220                                    # 석궁이 앞으로 나가므로 양옆을 넓힌다 (발밑 가운데 유지)
    W2 = w + pad * 2
    canvas = Image.new("RGBA", (W2, h)); canvas.alpha_composite(body, (pad, 0))

    cult = Image.open(R("cult", "이교도 사격.png")).convert("RGBA")
    C = np.array(cult).astype(int)
    box = C[430:565, 105:610].copy()             # 석궁 (이교도 장갑은 색으로 지운다)
    navy = (box[..., 2] > box[..., 0] + 15) & (box[..., 2] > 70)
    box[navy, 3] = 0
    bow = Image.fromarray(box.astype(np.uint8)).transpose(Image.FLIP_LEFT_RIGHT)
    k = 0.47                                     # 이교도 키(1356px) 대비 주인공 키(≈660px)
    bow = bow.resize((int(bow.width * k), int(bow.height * k)), Image.LANCZOS)

    sh = (68, 292)                               # 어깨 (망토 아래)
    ang = 82                                     # 늘어진 팔을 앞으로 들어 올린다 (반시계)
    big = Image.new("RGBA", (W2, h)); big.alpha_composite(piece, (pad, 0))
    rot = big.rotate(ang, center=(sh[0] + pad, sh[1]), resample=Image.BICUBIC)
    L = 468 - sh[1] - 30                         # 어깨 → 장갑 가운데
    hand = (sh[0] + pad + L * np.sin(np.radians(ang)), sh[1] + L * np.cos(np.radians(ang)))
    # 석궁 개머리가 장갑 밑에 오도록
    canvas.alpha_composite(bow, (int(hand[0] - bow.width * 0.22), int(hand[1] - bow.height * 0.45)))
    canvas.alpha_composite(rot)
    save(canvas, "Characters/Hero/player_aim.png", [0.5, 0.0])
    print("aim hand", hand)
make_aim()
with open(os.path.join(ART, "art_import_meta.json"), "w", encoding="utf-8") as f:
    json.dump(meta, f, ensure_ascii=False, indent=1)
