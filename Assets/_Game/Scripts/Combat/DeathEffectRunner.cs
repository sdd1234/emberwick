using System.Collections;
using UnityEngine;

namespace Capstone.Combat
{
    /// <summary>
    /// 사망 연출을 실제로 돌리는 쪽. 죽은 적에게서 떨어져 나와 혼자 돌다가 스스로 사라진다.
    /// (핏자국은 남기고 간다)
    /// 색과 크기는 프리팹에 들어 있는 값을 그대로 쓴다 - 인스펙터에서 보이는 게 결과여야 한다.
    /// </summary>
    public class DeathEffectRunner : MonoBehaviour
    {
        private struct Droplet
        {
            public Transform T;
            public SpriteRenderer R;
            public Vector2 Vel;
            public float Scale;
            public Color Tint;
        }

        public void Run(DeathEffect cfg, SpriteRenderer source, Vector2 hitDir)
        {
            StartCoroutine(Sequence(cfg, source, hitDir));
        }

        private IEnumerator Sequence(DeathEffect cfg, SpriteRenderer source, Vector2 hitDir)
        {
            SpawnStain(cfg);
            if (source != null && source.sprite != null) SpawnFlash(cfg, source);
            yield return Spray(cfg, hitDir);
            Destroy(gameObject);
        }

        // ---------- 바닥 핏자국 ----------
        private void SpawnStain(DeathEffect cfg)
        {
            var prefab = cfg.PickStain();
            if (prefab == null) return;

            var sr = Instantiate(prefab, transform.position, Quaternion.identity);
            // 탑다운이라 바닥에 깔린 것은 세로로 눌러야 누워 있는 것처럼 보인다
            sr.transform.localScale = new Vector3(1f, 0.62f, 1f);

            var c = sr.color; c.a = 0f; sr.color = c;
            StartCoroutine(GrowStain(sr, cfg, prefab.color.a));
        }

        private IEnumerator GrowStain(SpriteRenderer sr, DeathEffect cfg, float targetAlpha)
        {
            float dur = Mathf.Max(0.01f, cfg.StainGrow);
            Vector3 flat = sr.transform.localScale;
            float target = cfg.StainRadius * 2f;

            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                if (sr == null) yield break;
                float k = e / dur;
                float ease = 1f - (1f - k) * (1f - k);           // 빠르게 번지고 천천히 멎는다
                sr.transform.localScale = new Vector3(flat.x * target * ease, flat.y * target * ease, 1f);
                var c = sr.color; c.a = targetAlpha * ease; sr.color = c;
                yield return null;
            }
            if (sr == null) yield break;
            sr.transform.localScale = new Vector3(flat.x * target, flat.y * target, 1f);
            var fin = sr.color; fin.a = targetAlpha; sr.color = fin;
        }

        // ---------- 흰 실루엣 번쩍임 ----------
        private void SpawnFlash(DeathEffect cfg, SpriteRenderer source)
        {
            if (cfg.FlashPrefab == null) return;

            var sr = Instantiate(cfg.FlashPrefab, source.transform.position, source.transform.rotation);
            sr.transform.localScale = source.transform.lossyScale;
            sr.sprite = source.sprite;                 // 죽은 그 모습 그대로 번쩍인다
            sr.flipX = source.flipX;
            sr.color = cfg.FlashColor;

            StartCoroutine(FadeFlash(sr, cfg));
        }

        private IEnumerator FadeFlash(SpriteRenderer sr, DeathEffect cfg)
        {
            float dur = Mathf.Max(0.01f, cfg.FlashDuration);
            Vector3 from = sr.transform.localScale;
            Vector3 to = from * cfg.FlashGrow;

            for (float e = 0f; e < dur; e += Time.deltaTime)
            {
                if (sr == null) yield break;
                float k = e / dur;
                sr.transform.localScale = Vector3.Lerp(from, to, k);
                var c = sr.color; c.a = 1f - k; sr.color = c;
                yield return null;
            }
            if (sr != null) Destroy(sr.gameObject);
        }

        // ---------- 튀는 피 ----------
        private IEnumerator Spray(DeathEffect cfg, Vector2 hitDir)
        {
            if (cfg.DropletPrefab == null) yield break;

            int n = Mathf.Max(0, cfg.DropletCount);
            var drops = new Droplet[n];
            float baseAngle = Mathf.Atan2(hitDir.y, hitDir.x) * Mathf.Rad2Deg;

            for (int i = 0; i < n; i++)
            {
                var sr = Instantiate(cfg.DropletPrefab, transform);

                // 대부분은 맞은 방향으로 흩어지고, 몇 방울은 반대로도 튄다
                float spread = Random.Range(-cfg.SprayArc, cfg.SprayArc) * (Random.value < 0.15f ? 4f : 1f);
                float ang = (baseAngle + spread) * Mathf.Deg2Rad;
                float spd = Random.Range(cfg.DropletSpeed.x, cfg.DropletSpeed.y);

                float scale = Random.Range(cfg.DropletScale.x, cfg.DropletScale.y);
                sr.transform.localScale = Vector3.one * scale;
                sr.transform.localPosition = new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(0.1f, 0.7f), 0f);

                drops[i] = new Droplet
                {
                    T = sr.transform, R = sr, Scale = scale, Tint = sr.color,
                    Vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * spd,
                };
            }

            float life = Mathf.Max(0.05f, cfg.DropletLife);
            for (float e = 0f; e < life; e += Time.deltaTime)
            {
                float k = e / life;
                float dt = Time.deltaTime;
                for (int i = 0; i < n; i++)
                {
                    if (drops[i].T == null) continue;
                    drops[i].Vel *= 1f / (1f + cfg.DropletDrag * dt);        // 공기에 먹혀 금방 멎는다
                    drops[i].T.localPosition += (Vector3)(drops[i].Vel * dt);
                    drops[i].T.localScale = Vector3.one * drops[i].Scale * (1f - k * 0.45f);

                    var c = drops[i].Tint;
                    c.a *= 1f - k * k;                                       // 끝에서 빠르게 지운다
                    drops[i].R.color = c;
                }
                yield return null;
            }

            LeaveSpecks(cfg, drops);
        }

        /// <summary>튄 피가 멎은 자리에 작은 점을 남긴다. 싸우고 간 흔적이 바닥에 남는다.</summary>
        private void LeaveSpecks(DeathEffect cfg, Droplet[] drops)
        {
            if (cfg.SpeckPrefab == null) return;

            int left = Mathf.Max(0, cfg.MaxSpecks);
            for (int i = 0; i < drops.Length && left > 0; i++)
            {
                if (drops[i].T == null) continue;
                if (Random.value > 0.55f) continue;
                left--;

                var sr = Instantiate(cfg.SpeckPrefab, drops[i].T.position, Quaternion.identity);
                sr.transform.localScale = new Vector3(drops[i].Scale * 1.3f, drops[i].Scale * 0.85f, 1f);
            }
        }
    }
}
