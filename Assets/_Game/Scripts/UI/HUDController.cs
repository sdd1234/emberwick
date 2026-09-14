using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Capstone.UI
{
    /// <summary>화면 고정 HUD - 체력, 불씨, 석궁 차징/재장전, 상호작용 프롬프트 (기획서 6.6).</summary>
    public class HUDController : MonoBehaviour
    {
        [Header("대상")]
        [SerializeField] private Combat.Health health;
        [SerializeField] private Player.TorchFuel torch;
        [SerializeField] private Combat.Crossbow crossbow;
        [SerializeField] private Combat.PlayerStealth stealth;
        [SerializeField] private Items.Interactor interactor;
        [SerializeField] private Items.PlayerInventory inventory;

        [Header("체력")]
        [SerializeField] private Image healthFill;
        [SerializeField] private TMP_Text healthText;

        [Header("불씨 (기획서 6.5)")]
        [SerializeField] private Image fuelFill;
        [SerializeField] private TMP_Text fuelText;
        [Tooltip("점등 중일 때 켜지는 표시")]
        [SerializeField] private GameObject litIndicator;

        [Header("석궁")]
        [Tooltip("차징 게이지. Filled 로 설정")]
        [SerializeField] private Image chargeFill;
        [SerializeField] private GameObject chargeRoot;
        [SerializeField] private TMP_Text chargeLevelText;
        [SerializeField] private Image reloadFill;
        [SerializeField] private GameObject reloadRoot;

        [Header("상태")]
        [SerializeField] private GameObject stealthIndicator;
        [SerializeField] private TMP_Text promptText;
        [SerializeField] private TMP_Text weightText;

        [Header("색")]
        [SerializeField] private Color[] chargeLevelColors =
        {
            new(0.75f, 0.75f, 0.75f),
            new(0.95f, 0.80f, 0.35f),
            new(0.95f, 0.40f, 0.25f),
        };

        private void Update()
        {
            UpdateHealth();
            UpdateFuel();
            UpdateCrossbow();
            UpdateStatus();
        }

        private void UpdateHealth()
        {
            if (health == null) return;
            if (healthFill) healthFill.fillAmount = health.Normalized;
            if (healthText) healthText.text = $"{health.Current:0} / {health.Max:0}";
        }

        private void UpdateFuel()
        {
            if (torch == null) return;
            if (fuelFill)
            {
                fuelFill.fillAmount = torch.Normalized;
                // 기획서 6.5 - 100 / 50 / 30 단계로 색을 바꿔 잔량을 알린다
                fuelFill.color = torch.IconStage switch
                {
                    0 => new Color(0.95f, 0.78f, 0.40f),
                    1 => new Color(0.90f, 0.55f, 0.25f),
                    _ => new Color(0.80f, 0.25f, 0.20f),
                };
            }
            if (fuelText) fuelText.text = $"{torch.Fuel:0}%";
            if (litIndicator) litIndicator.SetActive(torch.IsLit);
        }

        private void UpdateCrossbow()
        {
            if (crossbow == null) return;

            if (chargeRoot) chargeRoot.SetActive(crossbow.IsCharging);
            if (crossbow.IsCharging)
            {
                if (chargeFill)
                {
                    chargeFill.fillAmount = crossbow.ChargeProgress;
                    int idx = Mathf.Clamp(crossbow.CurrentLevel, 0, chargeLevelColors.Length - 1);
                    chargeFill.color = chargeLevelColors[idx];
                }
                if (chargeLevelText) chargeLevelText.text = $"{crossbow.CurrentLevel}단계";
            }

            if (reloadRoot) reloadRoot.SetActive(crossbow.IsReloading);
            if (crossbow.IsReloading && reloadFill) reloadFill.fillAmount = crossbow.ReloadProgress;
        }

        private void UpdateStatus()
        {
            if (stealthIndicator && stealth != null) stealthIndicator.SetActive(stealth.IsStealthed);
            if (promptText && interactor != null) promptText.text = interactor.CurrentPrompt;
            if (weightText && inventory != null)
                weightText.text = $"{inventory.CurrentWeight:0.0} / {inventory.MaxWeight:0} kg";
        }
    }
}
