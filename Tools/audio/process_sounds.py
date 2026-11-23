#!/usr/bin/env python3
"""
팀 사운드 납품본(sounds/*.mp3)을 게임용으로 가공한다 (2026-11-03).
    python3 -I Tools/audio/process_sounds.py <풀어 놓은 sounds 폴더> Assets/_Game/Resources/Sounds

  - 앞 무음을 잘라 누른 순간 소리가 난다 (원본은 최대 0.6초 늦게 시작한다). 루프는 자르지 않는다.
  - 같은 그룹(walk_dirt1~4 등)은 피크를 맞춘다. 원본은 25dB 까지 차이 난다. EQ · 압축은 안 한다 - 게인만.
  - BGM 은 건드리지 않고 복사한다. 나머지는 WAV (MP3 는 앞뒤 패딩 때문에 루프 이음매가 벌어진다).
재납품이 오면 같은 명령을 다시 돌리면 된다.
"""
import array, math, os, re, shutil, subprocess, sys, wave

RATE = 48000
LOOPS = {"darkforest_ambience", "hub_ambience", "witch_whips", "during_appraise", "dummy_find"}

# 소리 이름(끝 숫자 뺀 것) → 목표 피크 dBFS
TARGET = {
    "bandage_use": -8, "bleeding": -10, "bleeding_start": -4, "bolt_hit": -6, "bush": -8, "button_press": -8,
    "crossbow_1st_charging": -8, "crossbow_2nd_charging": -6, "crossbow_reload": -6, "crossbow_shot_strong": -1,
    "crossbow_shot_weak": -3, "crossbow_start_chaging": -12, "darkforest_ambience": -14, "dodge_roll": -8,
    "dummy_find": -14, "during_appraise": -20, "escape_succeed": -6, "exposure": -1.5, "glass_broken": -1,
    "hub_ambience": -14, "inventory_close": -10, "inventory_open": -10, "item_get_legendary": -5, "item_get_rare": -10,
    "item_get_unique": -8, "light_change": -8, "light_off": -12, "light_on": -10, "player_dead": -3, "player_hit": -3,
    "raid_enter": -4, "raid_popup": -8, "run": -8, "walk_dirt": -8, "walk_grass": -4, "witch_whips": -10,
}

# 임시: item_get_common 원본은 -50dB 잡음뿐이고 소리가 없다. 재납품 전까지 rare 를 낮춰 쓴다 - 새 파일이 오면 이 줄을 지운다
BORROW = {"item_get_common": ("item_get_rare", 0.8, -8)}   # 이름: (원본, 재생 속도, 피크)


def decode(path, speed=1.0):
    af = ["-af", f"asetrate={int(RATE * speed)},aresample={RATE}"] if speed != 1.0 else []
    out = subprocess.run(["ffmpeg", "-v", "error", "-i", path, *af, "-f", "f32le", "-ac", "2", "-ar", str(RATE), "-"],
                         capture_output=True, check=True).stdout
    a = array.array("f"); a.frombytes(out)
    return a


def db(x):
    return 20 * math.log10(x) if x > 1e-9 else -180


def trim(a, peak):
    """앞 무음 · 뒤 무음을 잘라낸다. 3ms 앞을 남기고, 끝은 30ms 페이드."""
    frames = len(a) // 2
    thr = peak * 0.006
    head = next((i for i in range(frames) if max(abs(a[i * 2]), abs(a[i * 2 + 1])) >= thr), 0)
    thr_tail = peak * 0.003
    tail = next((i for i in range(frames - 1, -1, -1) if max(abs(a[i * 2]), abs(a[i * 2 + 1])) >= thr_tail), frames - 1)
    start = max(0, head - int(RATE * 0.003))
    end = min(frames, tail + int(RATE * 0.03))
    cut = a[start * 2:end * 2]
    fade = min(int(RATE * 0.03), len(cut) // 2)
    n = len(cut) // 2
    for i in range(fade):
        k = 1 - (i + 1) / fade
        cut[(n - fade + i) * 2] *= k
        cut[(n - fade + i) * 2 + 1] *= k
    return cut, start / RATE


def write_wav(path, a, gain):
    pcm = array.array("h", (max(-32767, min(32767, int(s * gain * 32767))) for s in a))
    with wave.open(path, "wb") as w:
        w.setnchannels(2); w.setsampwidth(2); w.setframerate(RATE); w.writeframes(pcm.tobytes())


def main(src, dst):
    os.makedirs(dst, exist_ok=True)
    names = sorted(f[:-4] for f in os.listdir(src) if f.endswith(".mp3"))
    print("이름\t원본피크\t게인dB\t앞무음ms\t길이s")
    for name in names:
        if name.endswith("_bgm"):
            shutil.copyfile(os.path.join(src, name + ".mp3"), os.path.join(dst, name + ".mp3"))
            print(f"{name}\t-\t(원본 복사)")
            continue
        base = re.sub(r"\d+$", "", name)
        speed, target, from_name = 1.0, None, name
        if name in BORROW:
            from_name, speed, target = BORROW[name]
        else:
            target = TARGET[base]
        a = decode(os.path.join(src, from_name + ".mp3"), speed)
        peak = max(abs(s) for s in a)
        cut = 0.0
        if name not in LOOPS:
            a, cut = trim(a, peak)
        gain = 10 ** (target / 20) / peak
        write_wav(os.path.join(dst, name + ".wav"), a, gain)
        print(f"{name}\t{db(peak):.1f}\t{db(gain):+.1f}\t{cut * 1000:.0f}\t{len(a) / 2 / RATE:.2f}" + ("\t(임시 대체)" if name in BORROW else ""))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
