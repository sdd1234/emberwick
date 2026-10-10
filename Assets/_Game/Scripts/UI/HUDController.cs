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

        [Header("상처 · 탄약 · 알림 (피드백)")]
        [Tooltip("출혈 · 감염 표시. 비우면 이름(AilmentText)으로 찾는다")]
        [SerializeField] private TMP_Text ailmentText;
        [Tooltip("남은 볼트 수. 비우면 이름(AmmoText)으로 찾는다")]
        [SerializeField] private TMP_Text ammoText;
        [Tooltip("잠깐 뜨는 알림 (붕대를 썼다 · 볼트가 없다). 비우면 이름(ToastText)으로 찾는다")]
        [SerializeField] private TMP_Text toastText;
        [SerializeField] private float toastSeconds = 2f;

        private static HUDController _instance;
        private Combat.PlayerAilments _ailments;
        private float _toastUntil;
        private float _lastDry = -10f;

        /// <summary>화면 아래에 잠깐 알림을 띄운다.</summary>
        public static void Toast(string message)
        {
            if (_instance == null || _instance.toastText == null || string.IsNullOrEmpty(message)) return;
            _instance.toastText.text = message;
            _instance.toastText.gameObject.SetActive(true);
            _instance._toastUntil = Time.time + _instance.toastSeconds;
        }

        [Header("색")]
        [SerializeField] private Color[] chargeLevelColors =
        {
            new(0.75f, 0.75f, 0.75f),
            new(0.95f, 0.80f, 0.35f),
            new(0.95f, 0.40f, 0.25f),
        };

        private void Start()
        {
            _instance = this;
            if (ailmentText == null) ailmentText = FindText("AilmentText");
            if (ammoText == null) ammoText = FindText("AmmoText");
            if (toastText == null) toastText = FindText("ToastText");
            if (toastText) toastText.gameObject.SetActive(false);
            if (health != null) _ailments = health.GetComponent<Combat.PlayerAilments>();
        }

        private TMP_Text FindText(string name)
        {
            foreach (var t in GetComponentsInChildren<TMP_Text>(true)) if (t.name == name) return t;
            return null;
        }

        private void UpdateAilmentsAndAmmo()
        {
            if (ailmentText != null)
            {
                string line = "";
                if (_ailments != null && _ailments.IsBleeding) line += "<color=#E0473F>출혈</color>  [H] 붕대   ";
                if (_ailments != null && _ailments.IsInfected) line += $"<color=#8FCB5A>감염 {_ailments.Infection:0}%</color>";
                ailmentText.text = line;
            }
            if (ammoText != null && crossbow != null)
            {
                int n = crossbow.BoltsLeft;
                ammoText.text = n < 0 ? "" : $"볼트 {n}";
                ammoText.color = n == 0 ? new Color(0.9f, 0.35f, 0.3f) : new Color(0.86f, 0.84f, 0.78f);
                if (crossbow.LastDryFireAt > _lastDry) { _lastDry = crossbow.LastDryFireAt; Toast("볼트가 없다"); }
            }
            if (toastText != null && toastText.gameObject.activeSelf && Time.time > _toastUntil) toastText.gameObject.SetActive(false);
        }

        private void Update()
        {
            UpdateAilmentsAndAmmo();
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
