using System.IO;
using UnityEditor;
using UnityEngine;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 전투 · 빛 효과음을 코드로 합성한다 (메뉴 Capstone/사운드 굽기/전투 · 빛, 2026-10-29).
    /// 팀 사운드(Resources/Sounds)에 아직 없는 소리만 남겼다 (2026-11-03) - 다시 구우면 같은 소리가 나온다.
    /// 소리마다 "무슨 재료로 만든 소리인가"를 주석으로 남긴다.
    /// </summary>
    public static class CombatSfxBaker
    {
        public const string Folder = "Assets/_Game/Audio/Sfx";
        private const int Rate = 44100;
        private static System.Random _rng;

        public static readonly string[] Names =
        {
            "hit_flesh", "hit_resist", "hit_weak", "dagger", "slam", "enemy_shot", "lamp_harvest", "lamp_place", "suspect", "death",
        };

        [MenuItem("Capstone/사운드 굽기/전투 · 빛")]
        public static void BakeAll()
        {
            Directory.CreateDirectory(Folder);
            _rng = new System.Random(291029);
            // 살에 박힘 - 둔탁한 저음 펀치 + 젖은 잡음
            Write("hit_flesh", 0.22f, (t, i) => Sine(t, Mathf.Lerp(140f, 60f, t / 0.12f)) * Env(t, 0.001f, 0.1f) + Noise() * Env(t, 0.001f, 0.05f) * 0.5f, lowpass: 2200f);
            // 막힘(감염자 물리) - 쇠 긁힘 같은 높은 배음 여러 개가 짧게 울린다
            Write("hit_resist", 0.35f, (t, i) => (Sine(t, 1240f) + Sine(t, 1873f) * 0.6f + Sine(t, 2710f) * 0.4f) * Env(t, 0.001f, 0.18f) * 0.5f + Noise() * Env(t, 0.001f, 0.02f) * 0.4f);
            // 약점(빛 · 불) - 지글거리며 타는 소리 + 반짝 위로 오르는 음
            Write("hit_weak", 0.45f, (t, i) => Noise() * Env(t, 0.005f, 0.3f) * 0.45f + Sine(t, Mathf.Lerp(600f, 1400f, t / 0.3f)) * Env(t, 0.01f, 0.25f) * 0.4f, highpass: 300f);
            // 단도 - 빠른 휙 (대역 잡음이 높은 쪽으로 쓸려 간다)
            Write("dagger", 0.16f, (t, i) => Noise() * Env(t, 0.01f, 0.1f), band: t => Mathf.Lerp(1500f, 5000f, t / 0.16f));
            // 감염자 내려치기 - 무거운 쿵 + 땅 울림
            Write("slam", 0.6f, (t, i) => Sine(t, Mathf.Lerp(90f, 38f, t / 0.3f)) * Env(t, 0.002f, 0.4f) * 1.2f + Noise() * Env(t, 0.001f, 0.12f) * 0.6f, lowpass: 900f, reverb: 0.25f);
            // 이교도 석궁 - 우리 석궁보다 가볍고 높다
            Write("enemy_shot", 0.25f, (t, i) => Pluck(t, 180f, 14f) * 0.7f + Noise() * Env(t, 0.002f, 0.05f) * 0.3f);
            // 빛 거두기 - 위로 오르는 맑은 음 셋 (거둬 온다)
            Write("lamp_harvest", 1.1f, (t, i) => Arp(t, new[] { 523.25f, 659.25f, 987.77f }, 0.12f), reverb: 0.45f);
            // 빛 걸기 - 내려가는 음 셋 (내어 준다)
            Write("lamp_place", 1.1f, (t, i) => Arp(t, new[] { 987.77f, 783.99f, 523.25f }, 0.12f), reverb: 0.45f);
            // 의심 - 낮게 갸웃하는 음 (올라가다 멈춘다)
            Write("suspect", 0.3f, (t, i) => Triangle(t, Mathf.Lerp(330f, 440f, Mathf.Clamp01(t / 0.15f))) * Env(t, 0.01f, 0.25f) * 0.4f);
            // 쓰러짐 - 둔탁한 털썩
            Write("death", 0.5f, (t, i) => Sine(t, Mathf.Lerp(110f, 45f, t / 0.25f)) * Env(t, 0.005f, 0.3f) + Noise() * Env(t, 0.002f, 0.15f) * 0.4f, lowpass: 1200f, reverb: 0.15f);
            AssetDatabase.Refresh();
            Debug.Log("[CombatSfxBaker] 전투 · 빛 효과음을 구웠다 - " + Folder);
        }

        // ---------- 재료 ----------
        private static float Noise() => (float)(_rng.NextDouble() * 2.0 - 1.0);
        private static float Sine(float t, float f) => Mathf.Sin(2f * Mathf.PI * f * t);
        private static float Square(float t, float f) => Mathf.Sign(Sine(t, f));
        private static float Triangle(float t, float f) => 2f * Mathf.Abs(2f * (t * f - Mathf.Floor(t * f + 0.5f))) - 1f;
        /// <summary>올라가는 시간 a, 잦아드는 시간 d 의 지수 엔벨로프.</summary>
        private static float Env(float t, float a, float d) => t < 0f ? 0f : t < a ? t / a : Mathf.Exp(-(t - a) / Mathf.Max(0.0001f, d) * 3f);
        /// <summary>뜯은 현 - 배음이 높은 것부터 빨리 죽는다.</summary>
        private static float Pluck(float t, float f, float decay)
        {
            float s = 0f;
            for (int h = 1; h <= 6; h++) s += Sine(t, f * h) * Mathf.Exp(-t * decay * h) / h;
            return s;
        }
        private static float Glass(float t)
        {
            float s = 0f;
            float[] f = { 2840f, 3610f, 4470f, 5230f, 6180f };
            float[] at = { 0f, 0.015f, 0.04f, 0.07f, 0.11f };
            for (int k = 0; k < f.Length; k++) s += Sine(t - at[k], f[k]) * Env(t - at[k], 0.0005f, 0.12f) * 0.3f;
            return s + Noise() * Env(t, 0.0005f, 0.04f) * 0.5f;
        }
        private static float Chord(float t, float[] fs, float a, float d)
        {
            float s = 0f;
            foreach (var f in fs) s += (Sine(t, f) + Sine(t, f * 2f) * 0.25f) * Env(t, a, d);
            return s / fs.Length;
        }
        private static float Arp(float t, float[] fs, float step)
        {
            float s = 0f;
            for (int k = 0; k < fs.Length; k++)
            {
                float u = t - k * step;
                s += (Sine(u, fs[k]) + Sine(u, fs[k] * 2.01f) * 0.3f + Sine(u, fs[k] * 3f) * 0.1f) * Env(u, 0.004f, 0.35f);
            }
            return s * 0.4f;
        }

        // ---------- 굽기 ----------
        private static void Write(string name, float seconds, System.Func<float, int, float> gen,
                                  float lowpass = 0f, float highpass = 0f, System.Func<float, float> band = null, float reverb = 0f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var mono = new float[n];
            Biquad lp = lowpass > 0f ? Biquad.LowPass(lowpass, 0.707f) : null;
            Biquad hp = highpass > 0f ? Biquad.HighPass(highpass, 0.707f) : null;
            Biquad bp = null; float lastBand = -1f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = gen(t, i);
                if (band != null)
                {
                    float f = band(t);
                    if (bp == null || Mathf.Abs(f - lastBand) > 40f) { var nb = Biquad.BandPass(f, 1.2f); if (bp != null) nb.CopyState(bp); bp = nb; lastBand = f; }
                    x = bp.Process(x) * 2f;
                }
                if (lp != null) x = lp.Process(x);
                if (hp != null) x = hp.Process(x);
                mono[i] = x;
            }
            var l = (float[])mono.Clone(); var r = (float[])mono.Clone();
            if (reverb > 0f) Reverb(l, r, reverb);
            Normalize(l, r, 0.85f);
            WriteWav($"{Folder}/{name}.wav", l, r);
        }

        private static void Reverb(float[] l, float[] r, float wet)
        {
            int[] d = { 1557, 1617, 1491, 1422 };
            for (int ch = 0; ch < 2; ch++)
            {
                var x = ch == 0 ? l : r; var y = new float[x.Length];
                foreach (int baseD in d)
                {
                    int dd = baseD + ch * 23; var buf = new float[dd]; int idx = 0; float lpv = 0f;
                    for (int i = 0; i < x.Length; i++) { float o = buf[idx]; lpv = o * 0.7f + lpv * 0.3f; buf[idx] = x[i] + lpv * 0.78f; idx = (idx + 1) % dd; y[i] += o * 0.25f; }
                }
                for (int i = 0; i < x.Length; i++) x[i] = x[i] * (1f - wet * 0.5f) + y[i] * wet;
            }
        }

        private static void Normalize(float[] l, float[] r, float peak)
        {
            float max = 1e-6f;
            for (int i = 0; i < l.Length; i++) max = Mathf.Max(max, Mathf.Abs(l[i]), Mathf.Abs(r[i]));
            float k = peak / max;
            for (int i = 0; i < l.Length; i++) { l[i] *= k; r[i] *= k; }
            int fade = Mathf.Min(l.Length, Rate / 100);
            for (int i = 0; i < fade; i++) { float g = i / (float)fade; l[l.Length - 1 - i] *= g; r[r.Length - 1 - i] *= g; }
        }

        private static void WriteWav(string path, float[] l, float[] r)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            int dataBytes = l.Length * 4;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataBytes); w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataBytes);
            for (int i = 0; i < l.Length; i++) { w.Write((short)(Mathf.Clamp(l[i], -1f, 1f) * 32767f)); w.Write((short)(Mathf.Clamp(r[i], -1f, 1f) * 32767f)); }
        }

        private class Biquad
        {
            private float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;
            public float Process(float x) { float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2; _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y; return y; }
            public void CopyState(Biquad o) { _x1 = o._x1; _x2 = o._x2; _y1 = o._y1; _y2 = o._y2; }
            private static Biquad Make(float b0, float b1, float b2, float a0, float a1, float a2) => new() { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0, _a1 = a1 / a0, _a2 = a2 / a0 };
            public static Biquad LowPass(float f, float q) { float w = 2f * Mathf.PI * f / Rate, c = Mathf.Cos(w), a = Mathf.Sin(w) / (2f * q); return Make((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a); }
            public static Biquad HighPass(float f, float q) { float w = 2f * Mathf.PI * f / Rate, c = Mathf.Cos(w), a = Mathf.Sin(w) / (2f * q); return Make((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a); }
            public static Biquad BandPass(float f, float q) { float w = 2f * Mathf.PI * Mathf.Min(f, Rate * 0.45f) / Rate, c = Mathf.Cos(w), a = Mathf.Sin(w) / (2f * q); return Make(a, 0, -a, 1 + a, -2 * c, 1 - a); }
        }
    }
}
