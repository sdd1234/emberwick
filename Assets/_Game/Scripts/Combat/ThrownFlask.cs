using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Capstone.Combat
{
    /// <summary>
    /// 던지는 연금술 병 (피드백 - 감염자는 빛으로 만든 연금술 도구나 속성 피해로 공략한다).
    ///   주인공 - 빛 플라스크: 불씨를 덜어 만든 빛. 터지면 눈부시게 번쩍이고 잠시 주변을 밝힌다 (Light 피해)
    ///   이교도 - 화염병: 횃불을 든 이교도만 가진 대응수단 (Fire 피해)
    /// 포물선은 높이 오프셋으로만 그린다 - 병을 돌리면 도트 격자가 깨진다.
    /// </summary>
    public class ThrownFlask : MonoBehaviour
    {
        public struct Spec
        {
            public DamageType type;
            public float damage, radius;
            public Color glow;
            public float lingerSeconds;   // 터진 뒤 남는 빛
            public float noise;           // 깨지는 소리 반경
            public Sprite sprite;
        }

        /// <summary>남는 빛 덩이 그림 (small_items 의 빛 덩이). 맵 빌더 · 플레이어가 넣는다.</summary>
        public static Sprite WispSprite;

        private Spec _spec;
        private Vector2 _from, _to;
        private float _duration;
        private GameObject _owner;
        private Core.FactionMember _ownerFaction;

        public static void Throw(Spec spec, Vector2 from, Vector2 to, GameObject owner)
        {
            var go = new GameObject(spec.type == DamageType.Light ? "LightFlask" : "FireFlask");
            go.transform.position = from;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spec.sprite != null ? spec.sprite : FX.PixelFX.Dot;
            if (spec.sprite == null) go.transform.localScale = Vector3.one * 3f;
            sr.sortingOrder = 30;
            var l = go.AddComponent<Light2D>();
            FX.LightUtil.AllLayers(l);
            l.lightType = Light2D.LightType.Point;
            l.color = spec.glow;
            l.intensity = 0.7f;
            l.pointLightOuterRadius = 1.6f;
            var f = go.AddComponent<ThrownFlask>();
            f._spec = spec;
            f._from = from;
            f._to = to;
            f._owner = owner;
            f._ownerFaction = owner ? owner.GetComponent<Core.FactionMember>() : null;
            f._duration = Mathf.Clamp(Vector2.Distance(from, to) / 11f, 0.25f, 0.9f);
        }

        private IEnumerator Start()
        {
            for (float t = 0f; t < _duration; t += Time.deltaTime)
            {
                float k = t / _duration;
                Vector2 p = Vector2.Lerp(_from, _to, k) + Vector2.up * (Mathf.Sin(k * Mathf.PI) * Mathf.Min(2.2f, _duration * 3.2f));
                transform.position = new Vector3(Mathf.Round(p.x * 38f) / 38f, Mathf.Round(p.y * 38f) / 38f, 0f);
                yield return null;
            }
            Burst();
        }

        private void Burst()
        {
            Vector2 at = _to;
            // 깨진 자리를 중심으로 피해 - 던진 쪽과 같은 편은 다치지 않는다
            var hits = Physics2D.OverlapCircleAll(at + Vector2.up * 0.6f, _spec.radius);
            var done = new System.Collections.Generic.HashSet<Health>();
            foreach (var h in hits)
            {
                var hp = h.GetComponentInParent<Health>();
                if (hp == null || hp.IsDead || done.Contains(hp)) continue;
                if (_owner != null && hp.gameObject == _owner) continue;
                var fm = hp.GetComponent<Core.FactionMember>();
                if (fm != null && _ownerFaction != null && fm.Faction == _ownerFaction.Faction) continue;
                if (fm == null && _ownerFaction == null) continue;          // 플레이어가 던진 병은 플레이어를 안 다치게
                done.Add(hp);
                Vector2 dir = ((Vector2)hp.transform.position - at).normalized;
                hp.TakeDamage(_spec.damage, dir, _spec.type, _owner);
            }

            Core.NoiseSystem.Emit(at, _spec.noise, _owner);
            if (_owner != null && _owner.GetComponent<Player.PlayerController>() != null) FX.SfxBank.PlayOwn("glass_broken", at, 1f, 0.03f);
            else FX.SfxBank.Play("glass_broken", at, 1f, 0.03f);
            FX.Vfx.Play(_spec.type == DamageType.Light ? "vfx_light_burst" : "vfx_fire_burst", at + Vector2.up * 0.7f, false, 46, 0.75f);
            // 시야 안에서 터졌을 때만 화면이 번쩍인다
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(at);
                if (vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f)
                {
                    bool lightF = _spec.type == DamageType.Light;
                    FX.ScreenFX.Flash(lightF ? new Color(1f, 0.9f, 0.6f) : new Color(1f, 0.5f, 0.2f), lightF ? 0.28f : 0.18f, lightF ? 0.22f : 0.16f);
                    Player.CameraFollow.KickMain(((Vector2)cam.transform.position - at), lightF ? 0.12f : 0.1f);
                }
            }
            Core.LightSignal.Emit(at, 18f, _owner);

            bool light = _spec.type == DamageType.Light;
            FX.PixelFX.Burst(at, Vector2.up, _spec.glow, Color.white, light ? 30 : 22, light ? 6.5f : 5f, 180f, false, light ? 0.7f : 0.55f);
            FX.PixelFX.Burst(at, Vector2.up, new Color(0.75f, 0.85f, 0.9f), Color.white, 8, 3.5f, 80f, true, 0.4f);   // 유리 조각

            var go = new GameObject(light ? "LightBurst" : "FireBurst");
            go.transform.position = at + Vector2.up * 0.5f;
            var l = go.AddComponent<Light2D>();
            FX.LightUtil.AllLayers(l);
            l.lightType = Light2D.LightType.Point;
            l.color = _spec.glow;
            l.pointLightOuterRadius = _spec.radius * 2.2f;
            l.pointLightInnerRadius = 0.3f;
            l.falloffIntensity = 0.6f;
            l.shadowsEnabled = true;
            l.shadowIntensity = 0.8f;
            go.AddComponent<BurstLight>().Init(l, light ? 4.2f : 2.2f, _spec.lingerSeconds, _spec.glow, !light, light ? WispSprite : null);
            if (light) go.AddComponent<Map.FixedLightZone>();               // 빛이 남아 있는 동안은 어둠 디버프도 풀린다
            Destroy(gameObject);
        }
    }

    /// <summary>번쩍 터졌다가 잔광으로 남는 빛.</summary>
    public class BurstLight : MonoBehaviour
    {
        private Light2D _l;
        private float _peak, _linger, _age;
        private Color _c;
        private bool _flames;

        private SpriteRenderer _orb;

        public void Init(Light2D l, float peak, float linger, Color c, bool flames, Sprite orb = null)
        {
            _l = l; _peak = peak; _linger = linger; _c = c; _flames = flames;
            if (orb != null)
            {
                var go = new GameObject("Orb");
                go.transform.SetParent(transform, false);
                _orb = go.AddComponent<SpriteRenderer>();
                _orb.sprite = orb;
                _orb.sharedMaterial = FX.PixelFX.UnlitMaterial;
                _orb.sortingOrder = 30;
            }
        }

        private void Update()
        {
            if (_l == null) return;
            _age += Time.deltaTime;
            float flash = _age < 0.08f ? _age / 0.08f : Mathf.Exp(-(_age - 0.08f) * 6f);
            // 잔광 - 달빛(0.3)보다 확실히 밝아야 "여기가 밝혀졌다"가 읽힌다
            float rest = _age < _linger ? 1.25f * (1f - Mathf.SmoothStep(0f, 1f, (_age - _linger * 0.6f) / (_linger * 0.4f))) : 0f;
            _l.intensity = Mathf.Max(_peak * flash, rest * (1f + Mathf.Sin(_age * 23f) * 0.06f));
            if (_orb != null)
            {
                bool on = _age < _linger;
                _orb.enabled = on && (_age < _linger * 0.7f || Mathf.Repeat(_age * 8f, 1f) > 0.4f);   // 꺼질 때쯤 깜빡인다
                if (!_flames && on && Random.value < 0.25f)
                    FX.PixelFX.Burst((Vector2)transform.position + Random.insideUnitCircle * 0.5f, Vector2.up, new Color(1f, 0.85f, 0.45f), Color.white, 1, 0.8f, 40f, false, 0.7f);
            }
            if (_flames && _age < _linger && Random.value < 0.5f)
                FX.PixelFX.Burst((Vector2)transform.position + Random.insideUnitCircle * 0.7f + Vector2.down * 0.4f, Vector2.up,
                    new Color(1f, 0.45f, 0.12f), new Color(1f, 0.8f, 0.3f), 1, 1.6f, 25f, false, 0.45f);
            if (_age > _linger + 0.5f) Destroy(gameObject);
        }
    }
}
