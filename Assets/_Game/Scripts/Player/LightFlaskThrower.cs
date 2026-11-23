using UnityEngine;
using UnityEngine.InputSystem;

namespace Capstone.Player
{
    /// <summary>
    /// 빛 플라스크 - G (피드백 2026-10-29 - 빛으로 만든 연금술 도구).
    /// 등불의 불씨를 덜어 병에 담아 커서 쪽으로 던진다. 터지면 Light 피해를 주고 몇 초 동안 그 자리를 밝힌다.
    /// 물리 피해를 막는 감염자에게 통하는 수단이고, 동시에 그 자리를 소리와 빛으로 알리는 미끼이기도 하다.
    /// 등불에서 거둔 빛이 곧 탄약이 된다 - 빛을 어디에 쓸지가 선택이 된다.
    /// </summary>
    public class LightFlaskThrower : MonoBehaviour
    {
        [SerializeField] private float fuelCost = 15f;
        [SerializeField] private float damage = 42f;
        [SerializeField] private float radius = 2.6f;
        [SerializeField] private float maxRange = 9f;
        [SerializeField] private float cooldown = 1.2f;
        [SerializeField] private float lingerSeconds = 5f;
        [SerializeField] private Sprite flaskSprite;
        [Tooltip("터진 뒤 남는 빛 덩이")]
        [SerializeField] private Sprite wispSprite;
        [SerializeField] private Color glow = new(1f, 0.88f, 0.55f);

        private PlayerController _pc;
        private TorchFuel _torch;
        private float _readyAt;

        public float FuelCost => fuelCost;

        private void Awake()
        {
            _pc = GetComponent<PlayerController>();
            _torch = GetComponent<TorchFuel>();
            if (wispSprite != null) { Combat.ThrownFlask.WispSprite = wispSprite; Map.Wisp.Sprite = wispSprite; }
        }

        private void Update()
        {
            if (!Core.KeyBindings.Pressed(Core.GameAction.Flask) || UI.LootScreenUI.AnyScreenOpen) return;
            if (Time.time < _readyAt) return;
            if (_torch == null || !_torch.TrySpend(fuelCost)) { UI.HUDController.Toast($"불씨가 모자라다 (빛 플라스크 {fuelCost:0}%)"); return; }

            _readyAt = Time.time + cooldown;
            Vector2 from = (Vector2)transform.position + Vector2.up * 1.3f;
            Vector2 to = _pc.AimWorldPoint;
            Vector2 d = to - (Vector2)transform.position;
            if (d.magnitude > maxRange) to = (Vector2)transform.position + d.normalized * maxRange;

            Combat.ThrownFlask.Throw(new Combat.ThrownFlask.Spec
            {
                type = Combat.DamageType.Light, damage = damage, radius = radius, glow = glow,
                lingerSeconds = lingerSeconds, noise = 7f, sprite = flaskSprite,
            }, from, to, gameObject);
        }
    }
}
