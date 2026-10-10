using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Capstone.EditorTools
{
    /// <summary>
    /// 효과음을 코드로 합성해 WAV 로 굽는다 (메뉴 Capstone/사운드 굽기).
    ///
    /// 스프라이트 생성기와 같은 원칙 - 다시 구우면 같은 소리가 나오고, 왜 그런 소리인지가 코드에 남는다.
    /// 굽는 것:
    ///   - 감정 소리 4종 (일반 · 레어 · 유니크 · 전설). 등급이 높을수록 음이 높고 풍성하다
    ///   - 발소리 걷기 4종 · 뛰기 4종
    ///   - 마녀의 속삭임 (어둠 패널티, 끊김 없이 반복되는 루프)
    /// </summary>
    public static class SoundBaker
    {
        public const string Folder = "Assets/_Game/Audio";
        private const int Rate = 44100;

        [MenuItem("Capstone/사운드 굽기")]
        public static void BakeAll()
        {
            Directory.CreateDirectory(Folder);
            var rng = new System.Random(1717);

            BakeReveal("reveal_common",    0, rng);
            BakeReveal("reveal_rare",      1, rng);
            BakeReveal("reveal_unique",    2, rng);
            BakeReveal("reveal_legendary", 3, rng);

            for (int i = 0; i < 4; i++) BakeStep($"step_walk_{i + 1}", false, rng);
            for (int i = 0; i < 4; i++) BakeStep($"step_run_{i + 1}", true, rng);

            BakeWhisper("witch_whisper", rng);

            AssetDatabase.Refresh();
            var whisper = AssetImporter.GetAtPath($"{Folder}/witch_whisper.wav") as AudioImporter;
            if (whisper != null)
            {
                var s = whisper.defaultSampleSettings;
                s.loadType = AudioClipLoadType.CompressedInMemory;
                whisper.defaultSampleSettings = s;
                whisper.SaveAndReimport();
            }
            Debug.Log("[SoundBaker] 효과음을 구웠다 - " + Folder);
        }

        // ================= 감정 소리 =================
        /// <summary>
        /// 등급이 오를수록 (1) 음이 높아지고 (2) 겹치는 음이 늘고 (3) 배음과 잔향이 풍성해진다.
        /// 일반은 낮은 단음 하나, 전설은 높은 장화음 아르페지오 + 깔리는 화음 + 반짝임 + 긴 잔향.
        /// </summary>
        private static void BakeReveal(string name, int grade, System.Random rng)
        {
            float[][] notes =
            {
                new[] { 392.00f },                                   // 일반 - G4 하나
                new[] { 523.25f, 659.25f },                          // 레어 - C5 E5
                new[] { 659.25f, 830.61f, 987.77f, 1318.51f },       // 유니크 - E 장화음 + 옥타브
                new[] { 880.00f, 1108.73f, 1318.51f, 1760.00f, 2217.46f }, // 전설 - A 장화음을 높이 쌓는다
            };
            float[] lengths  = { 0.45f, 0.8f, 1.2f, 2.1f };
            float[] stagger  = { 0f, 0.075f, 0.065f, 0.06f };
            int[]   partials = { 2, 3, 5, 6 };
            float[] reverb   = { 0.12f, 0.2f, 0.3f, 0.42f };
            float[] peak     = { 0.55f, 0.65f, 0.75f, 0.85f };

            int n = (int)(lengths[grade] * Rate);
            var l = new float[n];
            var r = new float[n];

            var chord = notes[grade];
            for (int i = 0; i < chord.Length; i++)
            {
                float start = i * stagger[grade];
                float pan = chord.Length == 1 ? 0f : Mathf.Lerp(-0.35f, 0.35f, i / (float)(chord.Length - 1));
                float decay = Mathf.Lerp(7f, 2.2f, grade / 3f);
                Bell(l, r, chord[i], start, lengths[grade] - start, partials[grade], decay, pan, grade >= 2);
            }

            // 전설 - 아르페지오 밑에 화음을 부풀려 깔고, 높은 반짝임을 흩뿌린다
            if (grade == 3)
            {
                foreach (var f in new[] { 440f, 554.37f, 659.25f })
                    Pad(l, r, f, 0.05f, lengths[grade] - 0.1f, 0.16f);
                for (int i = 0; i < 14; i++)
                {
                    float t = 0.25f + (float)rng.NextDouble() * 1.3f;
                    float f = 3000f + (float)rng.NextDouble() * 3500f;
                    Bell(l, r, f, t, 0.35f, 1, 14f, (float)rng.NextDouble() * 1.4f - 0.7f, false, 0.10f);
                }
            }
            // 유니크 - 옥타브 위를 살짝 덧대 반짝이게
            if (grade == 2)
                Bell(l, r, 1975.53f, 0.2f, 0.9f, 2, 5f, 0.2f, false, 0.18f);

            Reverb(l, r, reverb[grade]);
            Normalize(l, r, peak[grade]);
            WriteWav($"{Folder}/{name}.wav", l, r);
        }

        /// <summary>종소리 한 음. 배음을 살짝 어긋나게 쌓아(2, 3, 4.2...) 금속성 울림을 낸다.</summary>
        private static void Bell(float[] l, float[] r, float freq, float start, float dur, int partials,
                                 float decay, float pan, bool chorus, float gain = 0.35f)
        {
            float[] ratios = { 1f, 2.0f, 3.01f, 4.2f, 5.43f, 6.8f };
            float[] amps   = { 1f, 0.45f, 0.25f, 0.16f, 0.1f, 0.07f };
            int s0 = (int)(start * Rate);
            int len = Mathf.Min((int)(dur * Rate), l.Length - s0);
            float gl = gain * Mathf.Sqrt(0.5f * (1f - pan)), gr = gain * Mathf.Sqrt(0.5f * (1f + pan));

            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float attack = Mathf.Clamp01(t / 0.004f);
                float v = 0f;
                for (int p = 0; p < partials && p < ratios.Length; p++)
                {
                    float env = Mathf.Exp(-t * decay * (1f + p * 0.6f));   // 높은 배음이 먼저 사라진다
                    float f = freq * ratios[p];
                    v += Mathf.Sin(2f * Mathf.PI * f * t) * amps[p] * env;
                    if (chorus) v += Mathf.Sin(2f * Mathf.PI * f * 1.0035f * t) * amps[p] * env * 0.5f;
                }
                v *= attack;
                l[s0 + i] += v * gl;
                r[s0 + i] += v * gr;
            }
        }

        /// <summary>천천히 부풀었다 가라앉는 화음 한 음.</summary>
        private static void Pad(float[] l, float[] r, float freq, float start, float dur, float gain)
        {
            int s0 = (int)(start * Rate);
            int len = Mathf.Min((int)(dur * Rate), l.Length - s0);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float k = t / dur;
                float env = Mathf.Sin(Mathf.PI * Mathf.Pow(k, 0.6f));
                float v = (Mathf.Sin(2f * Mathf.PI * freq * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * freq * 2.003f * t)
                         + 0.6f * Mathf.Sin(2f * Mathf.PI * freq * 0.997f * t)) * env * gain;
                l[s0 + i] += v;
                r[s0 + i] += v * 0.92f;
            }
        }

        // ================= 발소리 =================
        /// <summary>
        /// 흙바닥 발소리. 낮은 쿵(뒤꿈치) + 걸러진 잡음(바닥 긁힘) + 30ms 뒤 앞꿈치 긁힘.
        /// 뛰기는 더 크고 밝고 짧게 끊어 친다.
        /// </summary>
        private static void BakeStep(string name, bool run, System.Random rng)
        {
            int n = (int)(0.28f * Rate);
            var l = new float[n];
            var r = new float[n];

            float cutoff = run ? 1700f : 950f;
            float thumpF = (run ? 95f : 78f) * (0.9f + (float)rng.NextDouble() * 0.2f);
            float tau = run ? 0.05f : 0.04f;

            var lp = Biquad.LowPass(cutoff * (0.85f + (float)rng.NextDouble() * 0.3f), 0.8f);
            var lp2 = Biquad.LowPass(cutoff * 1.6f, 0.7f);
            float heel = 0.028f + (float)rng.NextDouble() * 0.015f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);

                float body = lp.Process(noise) * Mathf.Exp(-t / tau) * Mathf.Clamp01(t / 0.003f);
                float thump = Mathf.Sin(2f * Mathf.PI * thumpF * t) * Mathf.Exp(-t / 0.035f) * 0.7f;
                float t2 = t - heel;
                float scuff = t2 > 0f ? lp2.Process(noise) * Mathf.Exp(-t2 / (tau * 0.7f)) * 0.45f : 0f;

                float v = body * 1.6f + thump + scuff;
                l[i] = v; r[i] = v;
            }
            Normalize(l, r, run ? 0.9f : 0.75f);
            WriteWav($"{Folder}/{name}.wav", l, r);
        }

        // ================= 속삭임 =================
        /// <summary>
        /// 마녀의 속삭임 - 성대를 울리지 않는 목소리는 숨(잡음)이 입 모양(포먼트)을 지나며 생긴다.
        /// 그래서 잡음을 모음별 포먼트 두 개로 거르고, 음절 단위로 끊어, 'ㅅ' 같은 치찰음을 섞는다.
        /// 목소리 셋을 좌우로 흩어 머리 주위를 맴도는 것처럼 들리게 하고, 끝과 처음을 겹쳐 끊김 없이 돈다.
        /// </summary>
        private static void BakeWhisper(string name, System.Random rng)
        {
            const float length = 9f;
            const float tail = 1.2f;
            int n = (int)((length + tail) * Rate);
            var l = new float[n];
            var r = new float[n];

            // (F1, F2) - 아 / 에 / 이 / 오 / 우
            Vector2[] vowels =
            {
                new(750f, 1250f), new(530f, 1850f), new(300f, 2250f), new(480f, 900f), new(330f, 850f),
            };

            int voices = 3;
            for (int v = 0; v < voices; v++)
            {
                float shift = 1f + v * 0.09f;                     // 목소리마다 입 크기가 다르다
                float panCenter = v == 0 ? -0.55f : v == 1 ? 0.6f : 0f;
                float level = v == 2 ? 0.55f : 0.85f;
                float t = (float)rng.NextDouble() * 0.8f + v * 0.35f;

                while (t < length)
                {
                    int syllables = 2 + rng.Next(4);
                    for (int s = 0; s < syllables && t < length; s++)
                    {
                        float dur = 0.11f + (float)rng.NextDouble() * 0.2f;
                        // 단어 속에서 소리가 머리 주위를 돈다
                        float pan = Mathf.Clamp(panCenter + Mathf.Sin(t * 0.9f + v) * 0.35f, -1f, 1f);

                        if (rng.NextDouble() < 0.35)
                            Fricative(l, r, t, dur * 0.8f, pan, level * 0.55f, rng);   // ㅅ / ㅎ
                        var vw = vowels[rng.Next(vowels.Length)] * shift;
                        Syllable(l, r, t, dur, vw, pan, level, rng);
                        t += dur + 0.015f + (float)rng.NextDouble() * 0.05f;
                    }
                    t += 0.25f + (float)rng.NextDouble() * 0.9f;   // 단어 사이 숨
                }
            }

            // 바닥에 깔리는 숨소리 - 완전한 무음이 없어야 반복이 티 나지 않는다
            var breath = Biquad.BandPass(600f, 0.7f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float swell = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t / 4.5f);
                float b = breath.Process((float)(rng.NextDouble() * 2.0 - 1.0)) * 0.05f * swell;
                l[i] += b; r[i] += b * 0.8f;
            }

            Reverb(l, r, 0.45f);

            // 꼬리를 앞머리에 겹쳐 이어 붙인다 (루프 이음매 제거)
            int loopLen = (int)(length * Rate);
            int fade = (int)(tail * Rate);
            var ol = new float[loopLen];
            var or = new float[loopLen];
            Array.Copy(l, ol, loopLen);
            Array.Copy(r, or, loopLen);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                ol[i] = ol[i] * k + l[loopLen + i] * (1f - k);
                or[i] = or[i] * k + r[loopLen + i] * (1f - k);
            }
            Normalize(ol, or, 0.8f);
            WriteWav($"{Folder}/{name}.wav", ol, or);
        }

        private static void Syllable(float[] l, float[] r, float start, float dur, Vector2 formant,
                                     float pan, float level, System.Random rng)
        {
            var f1 = Biquad.BandPass(formant.x, 5f);
            var f2 = Biquad.BandPass(formant.y, 7f);
            var f3 = Biquad.BandPass(2700f, 6f);
            int s0 = (int)(start * Rate);
            int len = Mathf.Min((int)(dur * Rate), l.Length - s0);
            float gl = Mathf.Sqrt(0.5f * (1f - pan)), gr = Mathf.Sqrt(0.5f * (1f + pan));

            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.035f) * Mathf.Clamp01((dur - t) / 0.07f);
                float x = (float)(rng.NextDouble() * 2.0 - 1.0);
                float v = (f1.Process(x) * 1.0f + f2.Process(x) * 0.7f + f3.Process(x) * 0.25f) * env * level;
                l[s0 + i] += v * gl;
                r[s0 + i] += v * gr;
            }
        }

        private static void Fricative(float[] l, float[] r, float start, float dur, float pan, float level, System.Random rng)
        {
            var hp = Biquad.HighPass(3800f + (float)rng.NextDouble() * 2500f, 0.9f);
            int s0 = (int)(start * Rate);
            int len = Mathf.Min((int)(dur * Rate), l.Length - s0);
            float gl = Mathf.Sqrt(0.5f * (1f - pan)), gr = Mathf.Sqrt(0.5f * (1f + pan));
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Sin(Mathf.PI * t / dur);
                float v = hp.Process((float)(rng.NextDouble() * 2.0 - 1.0)) * env * level;
                l[s0 + i] += v * gl;
                r[s0 + i] += v * gr;
            }
        }

        // ================= 공용 =================
        /// <summary>슈뢰더 잔향 - 콤 필터 넷 + 올패스 둘. 좌우 지연을 어긋나게 해 넓게 퍼지게 한다.</summary>
        private static void Reverb(float[] l, float[] r, float wet)
        {
            if (wet <= 0f) return;
            var dl = SchroederChannel(l, new[] { 1557, 1617, 1491, 1422 }, new[] { 225, 556 });
            var dr = SchroederChannel(r, new[] { 1580, 1640, 1514, 1445 }, new[] { 248, 579 });
            for (int i = 0; i < l.Length; i++)
            {
                l[i] = l[i] * (1f - wet * 0.5f) + dl[i] * wet;
                r[i] = r[i] * (1f - wet * 0.5f) + dr[i] * wet;
            }
        }

        private static float[] SchroederChannel(float[] input, int[] combs, int[] allpasses)
        {
            var output = new float[input.Length];
            foreach (int d in combs)
            {
                var buf = new float[d];
                int idx = 0;
                float lowpass = 0f;
                for (int i = 0; i < input.Length; i++)
                {
                    float y = buf[idx];
                    lowpass = y * 0.7f + lowpass * 0.3f;          // 높은 음이 먼저 죽는다
                    buf[idx] = input[i] + lowpass * 0.8f;
                    idx = (idx + 1) % d;
                    output[i] += y * 0.25f;
                }
            }
            foreach (int d in allpasses)
            {
                var buf = new float[d];
                int idx = 0;
                for (int i = 0; i < output.Length; i++)
                {
                    float b = buf[idx];
                    float x = output[i];
                    float y = -x + b;
                    buf[idx] = x + b * 0.5f;
                    idx = (idx + 1) % d;
                    output[i] = y;
                }
            }
            return output;
        }

        private static void Normalize(float[] l, float[] r, float peak)
        {
            float max = 1e-6f;
            for (int i = 0; i < l.Length; i++) max = Mathf.Max(max, Mathf.Abs(l[i]), Mathf.Abs(r[i]));
            float k = peak / max;
            for (int i = 0; i < l.Length; i++) { l[i] *= k; r[i] *= k; }

            // 끝 10ms 를 잘라 딸깍 소리를 없앤다
            int fade = Mathf.Min(l.Length, Rate / 100);
            for (int i = 0; i < fade; i++)
            {
                float g = i / (float)fade;
                l[l.Length - 1 - i] *= g;
                r[r.Length - 1 - i] *= g;
            }
        }

        private static void WriteWav(string path, float[] l, float[] r)
        {
            int n = l.Length;
            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            int dataBytes = n * 2 * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);          // PCM
            w.Write((short)2);          // 스테레오
            w.Write(Rate);
            w.Write(Rate * 4);
            w.Write((short)4);
            w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            for (int i = 0; i < n; i++)
            {
                w.Write((short)(Mathf.Clamp(l[i], -1f, 1f) * 32767f));
                w.Write((short)(Mathf.Clamp(r[i], -1f, 1f) * 32767f));
            }
        }

        /// <summary>RBJ 쿡북 2차 필터.</summary>
        private class Biquad
        {
            private float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

            public float Process(float x)
            {
                float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
                return y;
            }

            private static Biquad Make(float b0, float b1, float b2, float a0, float a1, float a2)
                => new() { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0, _a1 = a1 / a0, _a2 = a2 / a0 };

            public static Biquad LowPass(float f, float q)
            {
                float w = 2f * Mathf.PI * f / Rate, c = Mathf.Cos(w), a = Mathf.Sin(w) / (2f * q);
                return Make((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + a, -2 * c, 1 - a);
            }

            public static Biquad HighPass(float f, float q)
            {
                float w = 2f * Mathf.PI * f / Rate, c = Mathf.Cos(w), a = Mathf.Sin(w) / (2f * q);
                return Make((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + a, -2 * c, 1 - a);
            }

            public static Biquad BandPass(float f, float q)
            {
                float w = 2f * Mathf.PI * Mathf.Min(f, Rate * 0.45f) / Rate, c = Mathf.Cos(w), a = Mathf.Sin(w) / (2f * q);
                return Make(a, 0, -a, 1 + a, -2 * c, 1 - a);
            }
        }
    }
}
