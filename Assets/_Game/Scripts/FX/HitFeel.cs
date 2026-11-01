using System.Collections;
using UnityEngine;

namespace Capstone.FX
{
    /// <summary>
    /// 맞았을 때의 손맛 (피드백 - FEEL 에셋이 하는 일을 우선 직접 한다).
    ///   - 번쩍임: 맞은 순간 스프라이트가 붉게 하얘졌다가 돌아온다
    ///   - 밀림: 맞은 방향으로 살짝 밀린다
    ///   - 정지: 플레이어가 때렸을 때 · 무거운 한 방을 맞았을 때 아주 짧게 시간이 멎는다 (ScreenFX.HitStop)
    ///   - 킥: 무작위 흔들림 대신 맞은 · 때린 방향으로 카메라가 도트 몇 칸 밀렸다 돌아온다
    ///   - 파편: 피 · 쇳조각(저항) · 금빛(약점)
    ///   - 글자: 저항 / 관통! / 약점! - 감염자가 왜 안 죽는지, 무엇이 통하는지 화면에서 바로 읽힌다
    /// </summary>
    [RequireComponent(typeof(Combat.Health))]
    public class HitFeel : MonoBehaviour
    {
        [SerializeField] private Color bloodA = new(0.55f, 0.04f, 0.05f);
        [SerializeField] private Color bloodB = new(0.85f, 0.12f, 0.10f);
        [SerializeField] private float knockback = 2.2f;
        [SerializeField] private float hitStop = 0.05f;

        private Combat.Health _health;
        private SpriteRenderer _sr;
        private Rigidbody2D _rb;
        private Color _base = Color.white;
        private Coroutine _flash;

        private void Awake()
        {
            _health = GetComponent<Combat.Health>();
            _sr = GetComponentInChildren<SpriteRenderer>();
            _rb = GetComponent<Rigidbody2D>();
            if (_sr) _base = _sr.color;
            _health.OnHit += OnHit;
            _health.OnDeath += () =>
            {
                SfxBank.Play("death", transform.position, 0.9f);
                var fm = GetComponent<Core.FactionMember>();
                Vector2 c = (Vector2)transform.position + Vector2.up * 0.9f;
                if (fm != null && fm.Faction == Core.Faction.Infected) Vfx.Play("vfx_smoke", c + Vector2.up * 0.4f, false, 46, 0.8f);   // 감염자는 역병 연기로 흩어진다
                else Vfx.Play("vfx_blood", c, _sr != null && _sr.flipX, 44);
                Vfx.Play("vfx_dust", (Vector2)transform.position + Vector2.up * 0.25f, false, 20);
            };
        }

        private void OnDestroy() { if (_health != null) _health.OnHit -= OnHit; }

        /// <summary>마지막으로 맞은 시각 - 애니메이터가 움찔 그림을 잠깐 보여 준다.</summary>
        public float LastHitAt { get; private set; } = -9f;
        public Vector2 LastHitDir { get; private set; }

        private void OnHit(float amount, Vector2 dir, Combat.DamageType type, Combat.HitResult result, GameObject source)
        {
            if (result != Combat.HitResult.Resisted) { LastHitAt = Time.time; LastHitDir = dir; }
            Vector2 chest = (Vector2)transform.position + Vector2.up * 1.1f;
            bool byPlayer = source != null && source.GetComponent<Player.PlayerController>() != null;

            SfxBank.Play(result == Combat.HitResult.Resisted ? "hit_resist" : result == Combat.HitResult.Weakness ? "hit_weak" : "hit_flesh", chest, result == Combat.HitResult.Pierced ? 1f : 0.8f);
            bool left = dir.x < 0f;
            switch (result)
            {
                case Combat.HitResult.Resisted: Vfx.Play("vfx_resist", chest, left); break;
                case Combat.HitResult.Weakness: Vfx.Play("vfx_impact", chest, left); break;
                default:
                    Vfx.Play("vfx_impact", chest, left);
                    Vfx.Play("vfx_blood", chest + new Vector2(left ? -0.4f : 0.4f, 0f), left, 44);
                    break;
            }
            switch (result)
            {
                case Combat.HitResult.Resisted:
                    PixelFX.Burst(chest, -dir, new Color(0.75f, 0.78f, 0.8f), Color.white, 7, 5f, 60f, true, 0.3f);
                    // PixelFX.Text(chest + Vector2.up * 0.6f, "저항", new Color(0.72f, 0.76f, 0.82f), 2.6f);   // 공포 쪽 연출 - 떠오르는 글자 대신 소리 · 파편 색으로만 (2026-11-01)
                    break;
                case Combat.HitResult.Pierced:
                    PixelFX.Burst(chest, dir, bloodA, bloodB, 16, 6f, 45f);
                    // PixelFX.Text(chest + Vector2.up * 0.6f, "관통!", new Color(1f, 0.55f, 0.35f), 3.2f);   // 공포 쪽 연출 - 떠오르는 글자 대신 소리 · 파편 색으로만 (2026-11-01)
                    break;
                case Combat.HitResult.Weakness:
                    PixelFX.Burst(chest, Vector2.up, new Color(1f, 0.85f, 0.4f), Color.white, 18, 4.5f, 180f, false, 0.6f);
                    // PixelFX.Text(chest + Vector2.up * 0.6f, "약점!", new Color(1f, 0.86f, 0.42f), 3.2f);   // 공포 쪽 연출 - 떠오르는 글자 대신 소리 · 파편 색으로만 (2026-11-01)
                    break;
                default:
                    PixelFX.Burst(chest, dir, bloodA, bloodB, 10, 5f, 50f);
                    break;
            }

            if (_sr != null)
            {
                if (_flash != null) StopCoroutine(_flash);
                _flash = StartCoroutine(Flash(result == Combat.HitResult.Resisted ? new Color(0.8f, 0.85f, 1f) : new Color(1f, 0.45f, 0.42f)));
            }
            if (_rb != null && _rb.bodyType == RigidbodyType2D.Dynamic && dir.sqrMagnitude > 0.001f && result != Combat.HitResult.Resisted)
                _rb.position += dir.normalized * knockback * 0.06f;

            bool lethal = _health.IsDead || _health.Current <= 0f;
            if (byPlayer)
            {
                // 플레이어가 때렸다 - 막타는 더 길게 멈춘다 (가장 센 반응은 가장 중요한 순간에)
                if (result != Combat.HitResult.Resisted) ScreenFX.HitStop(lethal ? hitStop * 2.4f : result == Combat.HitResult.Normal ? hitStop : hitStop * 1.6f);
                Player.CameraFollow.KickMain(dir, result == Combat.HitResult.Resisted ? 0.06f : lethal ? 0.22f : 0.12f);
            }
            if (GetComponent<Player.PlayerController>() != null)
            {
                // 플레이어가 맞았다 - 맞은 방향으로 밀린다. 감염자의 한 방(20 이상)은 짧게 멈춰 무게를 싣는다
                Player.CameraFollow.KickMain(dir, Mathf.Clamp(amount * 0.012f, 0.08f, 0.32f));
                ScreenFX.PlayerHurt(amount, dir);
                if (amount >= 20f) ScreenFX.HitStop(0.07f);
            }
        }

        private IEnumerator Flash(Color c)
        {
            _sr.color = c;
            yield return new WaitForSecondsRealtime(0.07f);
            if (_sr) _sr.color = Color.Lerp(c, _base, 0.5f);
            yield return new WaitForSecondsRealtime(0.05f);
            if (_sr && !(_health != null && _health.IsDead)) _sr.color = _base;
            _flash = null;
        }

    }
}
