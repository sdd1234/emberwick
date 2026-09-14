using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Capstone.Items;

namespace Capstone.Enemy
{
    /// <summary>
    /// 죽은 적을 시체로 남긴다 (기획서 3 - 전리품 확보).
    /// 사라지지 않고 쓰러진 채 남아서 F 로 털 수 있다.
    /// 시체를 뒤지려면 불을 켜야 하므로, 처치한 자리에서 머무는 것 자체가 위험을 감수하는 선택이 된다.
    /// </summary>
    [RequireComponent(typeof(Combat.Health))]
    public class EnemyCorpse : MonoBehaviour
    {
        [Header("전리품")]
        [SerializeField] private List<ItemDefinition> dropPool = new();
        [SerializeField] private int minDrops = 1;
        [SerializeField] private int maxDrops = 3;
        [Tooltip("시체를 다 뒤지는 데 걸리는 시간(초). 상자보다 짧게 둔다")]
        [SerializeField] private float searchTime = 3.5f;
        [SerializeField] private string corpseLabel = "시체";

        [Header("연출")]
        [Tooltip("쓰러질 때 눕히는 각도")]
        [SerializeField] private float fallAngle = 82f;
        [Tooltip("쓰러지는 데 걸리는 시간(초). 0 이면 즉시 눕는다")]
        [SerializeField] private float fallDuration = 0.28f;
        [SerializeField] private Color corpseTint = new(0.55f, 0.52f, 0.52f, 1f);
        [Tooltip("시체가 남아 있는 시간(초). 0 이면 영구")]
        [SerializeField] private float corpseLifetime = 0f;

        private Combat.Health _health;

        private void Awake()
        {
            _health = GetComponent<Combat.Health>();
            _health.OnDeath += HandleDeath;
        }

        private void OnDestroy()
        {
            if (_health != null) _health.OnDeath -= HandleDeath;
        }

        private void HandleDeath()
        {
            // --- AI 와 물리를 멈춘다
            var ai = GetComponent<EnemyAI>();
            if (ai) ai.enabled = false;

            var telegraph = GetComponentInChildren<EnemyTelegraph>();
            if (telegraph) telegraph.gameObject.SetActive(false);

            var icon = GetComponentInChildren<EnemyAlertIcon>();
            if (icon) icon.gameObject.SetActive(false);

            var body = GetComponent<Rigidbody2D>();
            if (body)
            {
                body.linearVelocity = Vector2.zero;
                // simulated = false 로 끄면 안 된다.
                // 그러면 이 오브젝트의 콜라이더가 물리 쿼리에서 통째로 빠져서
                // Interactor 의 OverlapCircle 이 시체를 못 찾는다 - F 가 아예 안 먹는다.
                body.bodyType = RigidbodyType2D.Static;
            }

            // 본체 콜라이더는 통과할 수 있게 트리거로 바꾼다 (시체가 길을 막으면 답답하다)
            foreach (var col in GetComponents<Collider2D>()) col.isTrigger = true;

            // --- 쓰러진 모습
            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr)
            {
                sr.sortingOrder -= 5;                       // 살아있는 것들 아래로
                // 바라보던 쪽으로 넘어가야 자연스럽다
                float target = sr.flipX ? -fallAngle : fallAngle;
                if (fallDuration > 0f && isActiveAndEnabled) StartCoroutine(FallOver(sr, target));
                else
                {
                    sr.color = corpseTint;
                    sr.transform.rotation = Quaternion.Euler(0f, 0f, target);
                }
            }

            // --- 털 수 있게 만든다
            gameObject.layer = LayerMask.NameToLayer(Core.GameLayers.Interact);

            var lootCollider = gameObject.AddComponent<CircleCollider2D>();
            lootCollider.isTrigger = true;
            lootCollider.radius = 0.5f;
            lootCollider.offset = new Vector2(0f, 0.3f);

            var container = gameObject.AddComponent<LootContainer>();
            container.SetupAsCorpse(corpseLabel, RollDrops(), searchTime);

            gameObject.name = $"Corpse_{gameObject.name}";
            if (corpseLifetime > 0f) Destroy(gameObject, corpseLifetime);
        }

        /// <summary>
        /// 즉시 눕히면 "죽었다"가 아니라 "스프라이트가 회전했다"로 읽힌다.
        /// 넘어가다 살짝 지나쳐서 되돌아오게 하면 무게가 실린 것처럼 보인다.
        /// </summary>
        private IEnumerator FallOver(SpriteRenderer sr, float target)
        {
            Quaternion from = sr.transform.rotation;
            Color tintFrom = sr.color;
            float over = target * 1.08f;                    // 살짝 지나쳤다가

            for (float e = 0f; e < fallDuration; e += Time.deltaTime)
            {
                if (sr == null) yield break;
                float k = e / fallDuration;
                float ease = 1f - Mathf.Pow(1f - k, 3f);    // 처음엔 빠르고 끝에서 잦아든다
                float ang = Mathf.LerpAngle(from.eulerAngles.z, over, ease);
                sr.transform.rotation = Quaternion.Euler(0f, 0f, ang);
                sr.color = Color.Lerp(tintFrom, corpseTint, ease);
                yield return null;
            }

            // 되돌아와 멎는다
            for (float e = 0f; e < 0.08f; e += Time.deltaTime)
            {
                if (sr == null) yield break;
                float ang = Mathf.LerpAngle(over, target, e / 0.08f);
                sr.transform.rotation = Quaternion.Euler(0f, 0f, ang);
                yield return null;
            }

            if (sr == null) yield break;
            sr.transform.rotation = Quaternion.Euler(0f, 0f, target);
            sr.color = corpseTint;
        }

        private List<ItemDefinition> RollDrops()
        {
            var result = new List<ItemDefinition>();
            if (dropPool.Count == 0) return result;

            int n = Random.Range(minDrops, maxDrops + 1);
            for (int i = 0; i < n; i++)
                result.Add(dropPool[Random.Range(0, dropPool.Count)]);
            return result;
        }
    }
}
