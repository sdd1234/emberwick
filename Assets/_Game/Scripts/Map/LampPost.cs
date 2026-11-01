using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Capstone.Map
{
    /// <summary>
    /// 길가 등불 (피드백 2026-10-29 - 램프가 걸린 등대).
    /// 60% 확률로 빛이 조금 담겨 있고 주변을 밝힌다. 주인공은
    ///   - 빛을 거둬 자기 등불의 불씨로 쓸 수 있고 (F)
    ///   - 빈 등불에 불씨를 덜어 걸어 두면 길을 비추는 이정표가 된다 (F)
    /// 빛은 주인공 등불과 같은 따뜻한 노란빛이다. 켜지거나 꺼지는 순간은 빛 신호로 퍼져 도적이 보러 온다.
    /// 켜져 있는 동안은 고정 광원(FixedLightZone)이라 어둠 디버프가 풀린다.
    /// </summary>
    public class LampPost : MonoBehaviour, Items.IInteractable
    {
        [Header("그림")]
        [SerializeField] private SpriteRenderer body;
        [SerializeField] private Sprite litSprite;
        [SerializeField] private Sprite unlitSprite;
        [SerializeField] private Light2D lampLight;
        [Tooltip("등불 안 불꽃 위치 (로컬) - 빛 가루가 여기서 나가고 들어온다")]
        [SerializeField] private Vector2 flameOffset = new(0.55f, 3.1f);

        [Header("빛")]
        [Tooltip("판이 시작될 때 빛이 들어 있을 확률 (피드백 - 60%)")]
        [SerializeField, Range(0f, 1f)] private float startLitChance = 0.6f;
        [Tooltip("시작 지점 옆 등불 - 항상 켜져 있다 (첫 숨 고를 곳)")]
        [SerializeField] private bool alwaysLitAtStart;
        [SerializeField] private float litIntensity = 1.35f;
        [SerializeField] private float flicker = 0.08f;

        [Header("거두기 · 걸기")]
        [Tooltip("빛을 거두면 채워지는 불씨 (%)")]
        [SerializeField] private float harvestAmount = 18f;
        [Tooltip("빈 등불에 빛을 걸 때 드는 불씨 (%)")]
        [SerializeField] private float placeCost = 8f;
        [Tooltip("켜고 끌 때 퍼지는 빛 신호 반경 (m) - 이 안의 도적이 보러 온다")]
        [SerializeField] private float signalRadius = 16f;

        public bool IsLit { get; private set; }
        /// <summary>한 번이라도 직접 빛을 건 등불 (이정표).</summary>
        public bool PlacedByPlayer { get; private set; }

        private float _level;           // 0~1 - 지금 밝기 (켜고 끌 때 서서히)
        private float _target;
        private float _seed;
        private bool _busy;

        private Vector2 Flame => (Vector2)transform.position + flameOffset;

        private void Awake()
        {
            if (!body) body = GetComponent<SpriteRenderer>();
            if (!lampLight) lampLight = GetComponentInChildren<Light2D>(true);
            _seed = Random.value * 50f;
        }

        private void Start()
        {
            bool lit = alwaysLitAtStart || Random.value < startLitChance;
            SetLitImmediate(lit);
        }

        /// <summary>맵 빌더 · 시연용 - 처음 상태를 정해 둔다.</summary>
        public void SetLitImmediate(bool lit)
        {
            IsLit = lit;
            _level = _target = lit ? 1f : 0f;
            Apply();
        }

        private void Update()
        {
            _level = Mathf.MoveTowards(_level, _target, Time.deltaTime * 1.6f);
            Apply();
        }

        private void Apply()
        {
            if (body) body.sprite = _level > 0.35f ? litSprite : unlitSprite;
            if (!lampLight) return;
            float f = 1f + (Mathf.PerlinNoise(_seed, Time.time * 3.1f) - 0.5f) * 2f * flicker;
            lampLight.intensity = litIntensity * _level * f;
            // 꺼진 등불은 고정 광원 판정에서도 빠져야 한다 (FixedLightZone 은 enabled 를 본다)
            lampLight.enabled = _level > 0.02f;
        }

        // ---------- 상호작용 ----------
        public string GetPrompt(GameObject who)
        {
            var torch = who.GetComponent<Player.TorchFuel>();
            if (_busy) return "";
            if (IsLit) return $"[F] 등불의 빛을 거둔다  (불씨 +{harvestAmount:0}%)";
            if (torch != null && torch.Fuel < placeCost) return "불씨가 모자라 빛을 걸 수 없다";
            return $"[F] 빈 등불에 빛을 건다  (불씨 -{placeCost:0}%, 이정표)";
        }

        public bool CanInteract(GameObject who) => !_busy && who.GetComponent<Player.TorchFuel>() != null;

        public void Interact(GameObject who)
        {
            var torch = who.GetComponent<Player.TorchFuel>();
            if (torch == null || _busy) return;
            if (IsLit) StartCoroutine(Harvest(torch));
            else if (torch.TrySpend(placeCost)) StartCoroutine(Place(torch));
        }

        private IEnumerator Harvest(Player.TorchFuel torch)
        {
            _busy = true;
            IsLit = false;
            _target = 0f;
            Core.LightSignal.Emit(Flame, signalRadius, gameObject);
            FX.SfxBank.Play("lamp_harvest", Flame, 0.9f, 0f);
            FX.Vfx.Play("vfx_harvest", Flame, false, 50, 0.8f);
            yield return Wisp.Fly(Flame, torch.transform, 1.15f);
            if (torch) { torch.Refill(harvestAmount); FX.Vfx.Play("vfx_harvest", (Vector2)torch.transform.position + Vector2.up * 1.2f, false, 50); }
            UI.HUDController.Toast($"빛을 거뒀다  불씨 +{harvestAmount:0}%");
            _busy = false;
        }

        private IEnumerator Place(Player.TorchFuel torch)
        {
            _busy = true;
            FX.SfxBank.Play("lamp_place", Flame, 0.9f, 0f);
            yield return Wisp.Fly(torch.transform, Flame, 1.15f);
            IsLit = true;
            PlacedByPlayer = true;
            _target = 1f;
            Core.LightSignal.Emit(Flame, signalRadius, gameObject);
            FX.PixelFX.Burst(Flame, Vector2.up, new Color(1f, 0.85f, 0.45f), Color.white, 14, 2.6f, 180f, false, 0.6f);
            FX.Vfx.Play("vfx_harvest", Flame, false, 50, 0.8f);
            _busy = false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.8f);
            Gizmos.DrawWireSphere(Flame, 0.2f);
        }
    }

    /// <summary>등불과 주인공 사이를 오가는 빛 한 덩이. 작은 Light2D 를 달고 포물선을 그린다.</summary>
    public static class Wisp
    {
        public static Sprite Sprite;    // 빌더가 넣어 준다 (small_items 의 빛 덩이). 없으면 도트 사각형

        public static IEnumerator Fly(Vector2 from, Transform to, float seconds)
            => FlyCore(() => from, () => to != null ? (Vector2)to.position + Vector2.up * 1.2f : from, seconds);
        public static IEnumerator Fly(Transform from, Vector2 to, float seconds)
            => FlyCore(() => from != null ? (Vector2)from.position + Vector2.up * 1.2f : to, () => to, seconds);

        private static IEnumerator FlyCore(System.Func<Vector2> a, System.Func<Vector2> b, float seconds)
        {
            var go = new GameObject("LightWisp");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite != null ? Sprite : FX.PixelFX.Dot;
            sr.sharedMaterial = FX.PixelFX.UnlitMaterial;
            sr.sortingOrder = 50;
            if (Sprite == null) go.transform.localScale = Vector3.one * 3f;
            var l = go.AddComponent<Light2D>();
            FX.LightUtil.AllLayers(l);
            l.lightType = Light2D.LightType.Point;
            l.color = new Color(1f, 0.82f, 0.5f);
            l.pointLightOuterRadius = 2.4f;
            l.pointLightInnerRadius = 0.2f;
            l.intensity = 1.2f;
            Vector2 start = a();
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = t / seconds;
                float e = k * k * (3f - 2f * k);
                Vector2 p = Vector2.Lerp(start, b(), e) + Vector2.up * Mathf.Sin(k * Mathf.PI) * 1.4f;
                go.transform.position = p;
                if (Random.value < 0.35f) FX.PixelFX.Burst(p, Vector2.down, new Color(1f, 0.8f, 0.4f), new Color(1f, 0.95f, 0.7f), 1, 0.6f, 180f, false, 0.4f);
                yield return null;
            }
            Object.Destroy(go);
        }
    }
}
