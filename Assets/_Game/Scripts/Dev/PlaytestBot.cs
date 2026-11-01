#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace Capstone.Dev
{
    /// <summary>
    /// 자동 플레이 테스트 봇 (2026-10-31). 에디터 전용 - 메뉴 Capstone/플레이테스트 에서 켠다 (PlaytestMenu).
    ///
    /// 사람 대신 마을 → 레이드 → 탈출(또는 사망) → 마을 판매를 N 판 돈다.
    /// 이동 · 조준 · 쏘기 · 상호작용(F) · 등불(E) · 붕대(H) · 사망 후 귀환(R)은 진짜 키 입력을 넣어서
    /// 입력 처리까지 같이 시험한다. 격자 끌어 옮기기 · 판매 · 출격 확인 버튼만 API 로 대신한다.
    ///
    /// 재미는 못 잰다. 잡는 것: 에러 로그, 막혀서 못 가는 곳, 맞은 곳과 때린 적, 죽은 원인, 탈출까지 걸린 시간.
    /// 결과는 프로젝트/Logs/Playtest/날짜시각/ 에 events.jsonl · summary.md · 캡처(png).
    /// </summary>
    public class PlaytestBot : MonoBehaviour
    {
        // ---------- 설정 ----------
        private int _runsTotal = 3;
        private float _timeScale = 2f;
        private string _raidScene = "Raid_Map1";
        private const float LootMinutes = 7f;          // 이만큼 뒤졌으면 탈출로 돌아선다 (게임 시간)
        private const float RaidTimeoutMinutes = 19f;

        public void Configure(int runs, float timeScale, string raidScene)
        {
            _runsTotal = Mathf.Max(1, runs);
            _timeScale = Mathf.Clamp(timeScale, 1f, 4f);
            if (!string.IsNullOrEmpty(raidScene)) _raidScene = raidScene;
        }

        // ---------- 기록 ----------
        private string _dir;
        private StreamWriter _events;
        private readonly StringBuilder _summary = new();
        private int _run;                  // 지금 몇 번째 레이드 (1부터)
        private RunStats _stats;
        private int _shot;

        private class RunStats
        {
            public string scene; public float start; public float firstHit = -1f;
            public int containers, items, kills, hitsTaken, shots, stucks, errors, heals;
            public float damageTaken, walked;
            public string outcome = "?", deathBy = "";
            public readonly Dictionary<string, float> damageBy = new();
        }

        // ---------- 입력 ----------
        private readonly HashSet<Key> _held = new();
        private readonly HashSet<Key> _tap = new();
        private Vector2 _mouseScreen;
        private bool _mouseLeft;

        // ---------- 장면 ----------
        private enum Mode { Wait, Town, Raid, Done }
        private Mode _mode = Mode.Wait;
        private float _sceneTime;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            _dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "Playtest", System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(_dir);
            _events = new StreamWriter(Path.Combine(_dir, "events.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true };
            Application.logMessageReceived += OnLog;
            SceneManager.activeSceneChanged += OnSceneChanged;
            _summary.AppendLine("# 자동 플레이 테스트").AppendLine();
        }

        private void Start()
        {
            Time.timeScale = _timeScale;
            Event("start", $"\"runs\":{_runsTotal},\"timeScale\":{F(_timeScale)},\"raidScene\":\"{_raidScene}\"");
            OnSceneChanged(default, SceneManager.GetActiveScene());
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Time.timeScale = 1f;
            _events?.Dispose();
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            _sceneTime = 0f;
            _path.Clear(); _goal = null; _blacklist.Clear(); _looting = null;
            ReleaseAll();
            if (_mode == Mode.Done) return;
            // 레이드 씬은 Raid 로 시작하는 이름 (Raid, Raid_Map1)
            if (to.name.StartsWith("Raid")) { _mode = Mode.Raid; BeginRaid(to.name); }
            else if (to.name == "Town") { _mode = Mode.Town; _townStep = 0; }
            else _mode = Mode.Wait;
            Event("scene", $"\"name\":\"{to.name}\"");
        }

        // =====================================================================
        private void Update()
        {
            if (_mode == Mode.Done) return;
            // 씬 전환 · 역경직(ScreenFX)이 1 로 되돌려 놓는다 - 역경직 중(0.05)에는 건드리지 않는다
            if (Mathf.Approximately(Time.timeScale, 1f)) Time.timeScale = _timeScale;
            _sceneTime += Time.unscaledDeltaTime;
            if (_sceneTime < 1.5f) { FlushInput(); return; }   // 씬이 자리 잡을 때까지

            if (_mode == Mode.Raid) TickRaid();
            else if (_mode == Mode.Town) TickTown();
            FlushInput();
        }

        // ============================ 마을 ============================
        private int _townStep;
        private float _townStepAt;

        private void TickTown()
        {
            var pc = FindFirstObjectByType<Player.PlayerController>();
            if (pc == null) return;
            switch (_townStep)
            {
                case 0:
                    Capture("town_arrive");
                    Event("town", $"\"gold\":{Core.GameSession.Gold},\"stash\":{Core.GameSession.Stash.Items.Count}");
                    if (_run >= _runsTotal) { Finish(); return; }
                    _townStep = 1; _townStepAt = Time.time;
                    // 가게 창을 한 번 열어 겹침을 본다 (첫 방문만)
                    if (_run > 0) _townStep = 3;
                    break;
                case 1:   // 가게 앞으로
                {
                    var shop = FindFirstObjectByType<Town.TownShop>();
                    if (shop == null) { _townStep = 3; break; }
                    if (MoveTo(pc, shop.transform.position, 1.0f, wallMaskOnly: true) || Time.time - _townStepAt > 25f)
                    {
                        ReleaseMove(); _tap.Add(Key.F); _townStep = 2; _townStepAt = Time.time;
                    }
                    break;
                }
                case 2:   // 가게 창 캡처 후 닫기
                    if (Time.time - _townStepAt > 1.2f)
                    {
                        Capture("town_shop");
                        Event("shop", $"\"open\":{(UI.ShopUI.Current != null ? "true" : "false")}");
                        _tap.Add(Key.Escape);
                        _townStep = 3; _townStepAt = Time.time;
                    }
                    break;
                case 3:   // 판매 (격자 끌기는 API 로 대신)
                {
                    if (UI.ShopUI.Current != null) { _tap.Add(Key.Escape); break; }
                    int before = Core.GameSession.Gold;
                    int got = Core.GameSession.SellAll(Core.GameSession.Stash);
                    Event("sell", $"\"gold\":{got},\"total\":{Core.GameSession.Gold},\"before\":{before}");
                    _townStep = 4; _townStepAt = Time.time;
                    break;
                }
                case 4:   // 출구로 걸어가 출격
                {
                    var gate = FindFirstObjectByType<Town.RaidGate>();
                    if (gate == null) { Event("error", "\"msg\":\"RaidGate 없음\""); Finish(); return; }
                    if (gate.PopupShown)
                    {
                        ReleaseMove(); Capture("town_gate");
                        _townStep = 5; _townStepAt = Time.time;
                        break;
                    }
                    var zone = gate.GetComponent<Collider2D>();
                    MoveTo(pc, zone ? (Vector2)zone.bounds.center : (Vector2)gate.transform.position, 0.3f, wallMaskOnly: true);
                    if (Time.time - _townStepAt > 40f) { Event("stuck", "\"where\":\"town_gate\""); Finish(); }
                    break;
                }
                case 5:
                    if (Time.time - _townStepAt > 0.6f)
                    {
                        var gate = FindFirstObjectByType<Town.RaidGate>();
                        var tm = FindFirstObjectByType<Core.TownManager>();
                        string target = tm ? ReadField<string>(tm, "raidSceneName") : "?";
                        Event("depart", $"\"townTarget\":\"{target}\",\"botTarget\":\"{_raidScene}\"");
                        // 시험하려는 맵과 마을 출구가 다르면 출구 설정을 따르지 않고 시험할 맵으로 보낸다 - 다른 점은 기록에 남는다
                        if (target != _raidScene && tm) WriteField(tm, "raidSceneName", _raidScene);
                        gate.Accept();
                        _townStep = 6;
                    }
                    break;
            }
        }

        // ============================ 레이드 ============================
        private Player.PlayerController _pc;
        private Combat.Health _hp;
        private Items.PlayerInventory _inv;
        private Items.Interactor _interactor;
        private Player.TorchFuel _torch;
        private Combat.Crossbow _xbow;
        private Combat.PlayerAilments _ail;
        private Core.RaidManager _raid;
        private Map.ExtractionPoint _exit;
        private LayerMask _navMask; private float _navRadius = 0.42f; private LayerMask _wallMask;
        private bool _ended, _noMoreLoot; private float _endAt;
        private Vector2 _lastPos;
        private bool _hurtShot, _lowShot;
        private float _nextPeriodicShot;

        private void BeginRaid(string scene)
        {
            _run++;
            _stats = new RunStats { scene = scene, start = Time.time };
            _ended = false; _noMoreLoot = false; _hurtShot = false; _lowShot = false;
            _nextPeriodicShot = Time.time + 60f;
            _pc = null;
            _summary.AppendLine($"## {_run}판 - {scene}");
        }

        private bool BindRaid()
        {
            if (_pc != null) return true;
            _pc = FindFirstObjectByType<Player.PlayerController>();
            if (_pc == null) return false;
            _hp = _pc.GetComponent<Combat.Health>();
            _inv = _pc.GetComponent<Items.PlayerInventory>();
            _interactor = _pc.GetComponent<Items.Interactor>();
            _torch = _pc.GetComponent<Player.TorchFuel>();
            _xbow = _pc.GetComponentInChildren<Combat.Crossbow>();
            _ail = _pc.GetComponent<Combat.PlayerAilments>();
            _raid = FindFirstObjectByType<Core.RaidManager>();
            _exit = _raid ? ReadField<Map.ExtractionPoint>(_raid, "extraction") : null;
            if (_exit == null) _exit = FindFirstObjectByType<Map.ExtractionPoint>();
            _wallMask = LayerMask.GetMask(Core.GameLayers.Wall);
            // 길찾기 격자는 적과 같이 쓴다 - 같은 값으로 굽지 않으면 서로 번갈아 다시 굽는다
            _navMask = _wallMask;
            var anyEnemy = FindFirstObjectByType<Enemy.EnemyAI>();
            if (anyEnemy)
            {
                var m = ReadField<LayerMask>(anyEnemy, "obstacleMask");
                if (m.value == 0) m = ReadField<LayerMask>(anyEnemy, "sightBlockMask");
                if (m.value != 0) _navMask = m;
                _navRadius = ReadField<float>(anyEnemy, "agentRadius");
            }
            if (_hp) { _hp.OnDamaged += OnPlayerDamaged; }
            foreach (var e in FindObjectsByType<Enemy.EnemyAI>(FindObjectsSortMode.None))
            {
                var h = e.GetComponent<Combat.Health>();
                if (h) { var name = e.name; h.OnDeath += () => { if (_stats != null) { _stats.kills++; Event("kill", $"\"enemy\":\"{Clean(name)}\""); } }; }
            }
            _lastPos = _pc.transform.position;
            Event("raid_begin", $"\"run\":{_run},\"containers\":{FindObjectsByType<Items.LootContainer>(FindObjectsSortMode.None).Length},\"enemies\":{FindObjectsByType<Enemy.EnemyAI>(FindObjectsSortMode.None).Length},\"exit\":{V(_exit ? (Vector2)_exit.transform.position : Vector2.zero)}");
            Capture("raid_start");
            return true;
        }

        private void OnPlayerDamaged(float amount, Vector2 dir)
        {
            if (_stats == null || _hp == null) return;
            var by = _hp.LastAttacker ? Clean(_hp.LastAttacker.name) : "?";
            _stats.hitsTaken++; _stats.damageTaken += amount;
            _stats.damageBy[by] = (_stats.damageBy.TryGetValue(by, out var d) ? d : 0f) + amount;
            float since = Time.time - _stats.start;
            if (_stats.firstHit < 0f) _stats.firstHit = since;
            float dist = _hp.LastAttacker ? Vector2.Distance(_hp.LastAttacker.transform.position, _pc.transform.position) : -1f;
            Event("hit", $"\"dmg\":{F(amount)},\"by\":\"{by}\",\"dist\":{F(dist)},\"hp\":{F(_hp.Current)},\"pos\":{V(_pc.transform.position)}");
            if (!_hurtShot) { _hurtShot = true; Invoke(nameof(CaptureHurt), 0.08f); }
        }
        private void CaptureHurt() => Capture("hurt");

        private void TickRaid()
        {
            if (!BindRaid()) return;
            Vector2 pos = _pc.transform.position;
            _stats.walked += Vector2.Distance(pos, _lastPos); _lastPos = pos;

            // --- 끝 ---
            if (_raid && (_raid.IsDead || _raid.IsExtracted))
            {
                if (!_ended) EndRaid(_raid.IsDead ? "death" : "extract");
                ReleaseAll();
                if (_raid.IsDead && Time.time - _endAt > 1.5f * _timeScale) _tap.Add(Key.R);
                return;
            }
            float elapsed = Time.time - _stats.start;
            if (elapsed > RaidTimeoutMinutes * 60f) { EndRaid("timeout"); _raid?.LeaveRaid(); return; }

            if (Time.time > _nextPeriodicShot) { _nextPeriodicShot = Time.time + 60f; Capture("raid_t" + Mathf.RoundToInt(elapsed)); }
            if (!_lowShot && _hp && _hp.Normalized < 0.35f) { _lowShot = true; Capture("lowhp"); }

            // --- 등불은 켜 둔다 (어두우면 뒤질 수 없다) ---
            if (_torch && !_torch.IsLit && !_torch.IsEmpty && Every(ref _torchAt, 1f)) _tap.Add(Key.E);

            // --- 붕대 ---
            if (_ail && _hp && (_ail.IsBleeding || _hp.Normalized < 0.5f) && Every(ref _healAt, 6f))
            { _tap.Add(Key.H); _stats.heals++; Event("heal", $"\"hp\":{F(_hp.Current)},\"bleed\":{(_ail.IsBleeding ? "true" : "false")}"); }

            // --- 싸움 ---
            if (TickCombat(pos)) return;

            // --- 뒤지는 중 ---
            if (_looting != null) { TickLoot(); return; }

            // --- 목표 고르기 ---
            bool goHome = elapsed > LootMinutes * 60f || (_inv && _inv.MaxWeight > 0f && _inv.CurrentWeight > _inv.MaxWeight * 0.8f)
                       || (_hp && _hp.Normalized < 0.4f);
            if ((goHome || _noMoreLoot) && _exit)
            {
                if (_goal != (Object)_exit) { _goal = _exit; _path.Clear(); Event("goal", $"\"what\":\"extract\",\"why\":\"{(_noMoreLoot ? "no_more_loot" : elapsed > LootMinutes * 60f ? "time" : _hp.Normalized < 0.4f ? "hp" : "weight")}\""); }
                MoveTo(_pc, _exit.transform.position, 0.2f, false);
                return;
            }

            if (_goal == null || (_goal is Items.LootContainer lc && lc.IsGenerated && !lc.HasRemaining))
                PickContainer(pos);
            if (_goal == null) { _noMoreLoot = true; return; }

            var target = (Component)_goal;
            bool inReach = _interactor && ReferenceEquals(_interactor.Current, target);
            if (inReach || MoveTo(_pc, target.transform.position, 0.9f, false))
            {
                ReleaseMove();
                if (!inReach && _interactor && _interactor.Current != null && _interactor.CandidateCount > 1) { if (Every(ref _cycleAt, 0.3f)) _tap.Add(Key.Q); return; }
                if (_goal is Items.LootContainer box)
                {
                    _looting = box; _lootStart = Time.time; _tap.Add(Key.F);
                }
                else { _tap.Add(Key.F); _blacklist.Add(_goal); _goal = null; }
            }
        }

        // --- 상자 ---
        private Object _goal;
        private readonly HashSet<Object> _blacklist = new();
        private Items.LootContainer _looting;
        private float _lootStart, _torchAt, _healAt, _cycleAt, _shootAt;

        private void PickContainer(Vector2 pos)
        {
            _goal = null; float best = float.MaxValue;
            foreach (var c in FindObjectsByType<Items.LootContainer>(FindObjectsSortMode.None))
            {
                if (_blacklist.Contains(c) || (c.IsGenerated && !c.HasRemaining)) continue;
                float d = Vector2.Distance(pos, c.transform.position);
                if (d < best) { best = d; _goal = c; }
            }
            foreach (var w in FindObjectsByType<Items.WorldItem>(FindObjectsSortMode.None))
            {
                if (_blacklist.Contains(w)) continue;
                float d = Vector2.Distance(pos, w.transform.position);
                if (d < best) { best = d; _goal = w; }
            }
            _path.Clear();
            if (_goal) Event("goal", $"\"what\":\"{Clean(_goal.name)}\",\"dist\":{F(best)}");
        }

        private void TickLoot()
        {
            var box = _looting;
            // 감정이 끝난 것부터 가방으로 (사람은 끌어 옮긴다 - 봇은 API)
            if (Every(ref _cycleAt, 0.3f) && box.Grid != null && _inv)
            {
                var list = new List<Items.GridItem>(box.Grid.Items);
                foreach (var it in list)
                {
                    if (!it.Identified) continue;
                    if (_inv.Grid.WouldExceedWeight(it)) continue;
                    int x = it.X, y = it.Y; bool r = it.Rotated;
                    box.Grid.Remove(it);
                    if (_inv.Grid.TryAutoPlace(it)) _stats.items++;
                    else box.Grid.Place(it, x, y, r);
                }
            }
            bool allSeen = box.IsGenerated && box.Grid != null && System.Linq.Enumerable.All(box.Grid.Items, i => i.Identified);
            bool searching = _interactor && _interactor.Searching == box;
            float spent = Time.time - _lootStart;
            if (!searching && spent > 1.5f && !box.IsGenerated)
            {
                // F 가 안 먹었다 (불이 없거나 다른 대상) - 다시 누르거나 포기
                if (spent > 6f) { Event("loot_fail", $"\"box\":\"{Clean(box.name)}\",\"prompt\":\"{Esc(_interactor ? _interactor.CurrentPrompt : "")}\""); _blacklist.Add(box); _looting = null; _goal = null; return; }
                if (Every(ref _shootAt, 1.5f)) _tap.Add(Key.F);
                return;
            }
            if ((allSeen && !box.HasRemaining) || (allSeen && spent > 2f) || spent > 30f)
            {
                _stats.containers++;
                Event("looted", $"\"box\":\"{Clean(box.name)}\",\"left\":{(box.Grid != null ? box.Grid.Items.Count : 0)},\"sec\":{F(spent)},\"bag\":{F(_inv ? _inv.CurrentWeight : 0f)}");
                if (_stats.containers == 1) Capture("first_loot");
                if (searching) _tap.Add(Key.F);       // 닫기
                _blacklist.Add(box); _looting = null; _goal = null;
            }
        }

        // --- 싸움 ---
        private Enemy.EnemyAI _foe;
        private bool _charging; private float _chargeAt;

        private bool TickCombat(Vector2 pos)
        {
            Enemy.EnemyAI pick = null; float best = float.MaxValue;
            foreach (var e in FindObjectsByType<Enemy.EnemyAI>(FindObjectsSortMode.None))
            {
                var h = e.GetComponent<Combat.Health>();
                if (h && h.IsDead) continue;
                float d = Vector2.Distance(pos, e.transform.position);
                bool threat = (e.State == Enemy.AlertState.Alerted && d < 13f) || d < 4f;
                if (!threat || d >= best) continue;
                if (Physics2D.Linecast(pos + Vector2.up * 0.5f, (Vector2)e.transform.position + Vector2.up * 0.5f, _wallMask)) continue;
                best = d; pick = e;
            }
            if (pick == null || _xbow == null || (_xbow.BoltsLeft == 0))
            {
                if (_charging) { _mouseLeft = false; _charging = false; }
                _foe = null;
                return false;
            }
            if (_foe != pick) { _foe = pick; Event("engage", $"\"enemy\":\"{Clean(pick.name)}\",\"dist\":{F(best)},\"state\":\"{pick.State}\""); }
            if (_looting != null) { if (_interactor && _interactor.Searching) _tap.Add(Key.F); _looting = null; }

            ReleaseMove();
            Vector2 aim = (Vector2)pick.transform.position + Vector2.up * 0.6f;
            AimAt(aim);
            // 1단(0.5초) 차징 후 놓는다 - 사람이 가장 많이 쓰는 박자
            if (!_charging && _xbow.IsLoaded && !_xbow.IsReloading) { _mouseLeft = true; _charging = true; _chargeAt = Time.time; }
            else if (_charging && Time.time - _chargeAt > 0.6f) { _mouseLeft = false; _charging = false; _stats.shots++; }
            return true;
        }

        private void EndRaid(string outcome)
        {
            _ended = true; _endAt = Time.time;
            _stats.outcome = outcome;
            if (outcome == "death" && _hp && _hp.LastAttacker) _stats.deathBy = Clean(_hp.LastAttacker.name);
            float sec = Time.time - _stats.start;
            Event("raid_end", $"\"outcome\":\"{outcome}\",\"sec\":{F(sec)},\"by\":\"{_stats.deathBy}\"");
            Invoke(nameof(CaptureEnd), 0.6f);

            var s = _summary;
            s.AppendLine($"- 결과: **{(outcome == "death" ? "사망" : outcome == "extract" ? "탈출" : "시간 초과")}**{(_stats.deathBy != "" ? $" (마지막 공격: {_stats.deathBy})" : "")} · {sec / 60f:0.0}분(게임 시간)");
            s.AppendLine($"- 뒤진 상자 {_stats.containers} · 챙긴 물건 {_stats.items} · 처치 {_stats.kills} · 쏜 화살 {_stats.shots} · 붕대 {_stats.heals}");
            s.AppendLine($"- 맞은 횟수 {_stats.hitsTaken} · 받은 피해 {_stats.damageTaken:0} · 첫 피격 {(_stats.firstHit < 0 ? "없음" : $"{_stats.firstHit:0}초")}");
            if (_stats.damageBy.Count > 0)
            {
                s.Append("- 피해 출처: ");
                foreach (var kv in _stats.damageBy) s.Append($"{kv.Key} {kv.Value:0} · ");
                s.Length -= 3; s.AppendLine();
            }
            s.AppendLine($"- 걸은 거리 {_stats.walked:0}m · 막힘 {_stats.stucks}회 · 에러 로그 {_stats.errors}건").AppendLine();
        }
        private void CaptureEnd() => Capture(_stats != null ? "end_" + _stats.outcome : "end");

        // ============================ 이동 ============================
        private readonly List<Vector2> _path = new();
        private int _pathIndex;
        private Vector2 _pathGoal;
        private float _repathAt, _stuckCheckAt;
        private Vector2 _stuckFrom;
        private int _stuckStreak;

        /// <summary>목표까지 걷는다. 도착하면 true.</summary>
        private bool MoveTo(Player.PlayerController pc, Vector2 goal, float arrive, bool wallMaskOnly)
        {
            Vector2 pos = pc.transform.position;
            if (Vector2.Distance(pos, goal) <= arrive) { ReleaseMove(); return true; }

            if (_path.Count == 0 || (goal - _pathGoal).sqrMagnitude > 1f || Time.time > _repathAt)
            {
                if (wallMaskOnly) Map.NavGrid.EnsureBuilt(LayerMask.GetMask(Core.GameLayers.Wall), 0.42f);
                else Map.NavGrid.EnsureBuilt(_navMask, _navRadius);
                if (!Map.NavGrid.FindPath(pos, goal, _path, 40000)) { _path.Clear(); _path.Add(goal); }
                _pathIndex = 0; _pathGoal = goal; _repathAt = Time.time + 3f;
            }

            while (_pathIndex < _path.Count - 1 && Vector2.Distance(pos, _path[_pathIndex]) < 0.6f) _pathIndex++;
            Vector2 next = _pathIndex >= _path.Count - 1 ? goal : _path[_pathIndex];
            Vector2 dir = next - pos;
            SetMove(dir);
            AimAt(pos + dir.normalized * 3f + Vector2.up * 0.6f);

            // 막힘 - 1.5초에 0.3m 도 못 갔으면 다시 길을 잡고, 세 번째면 그 목표를 버린다
            if (Time.time > _stuckCheckAt)
            {
                if (Vector2.Distance(pos, _stuckFrom) < 0.3f)
                {
                    _stuckStreak++;
                    if (_stats != null) _stats.stucks++;
                    Event("stuck", $"\"pos\":{V(pos)},\"goal\":{V(goal)},\"streak\":{_stuckStreak}");
                    if (_stuckStreak == 1) Capture("stuck");
                    _path.Clear();
                    if (_stuckStreak >= 3 && _goal != null && _goal != (Object)_exit) { _blacklist.Add(_goal); _goal = null; _stuckStreak = 0; }
                }
                else _stuckStreak = 0;
                _stuckFrom = pos; _stuckCheckAt = Time.time + 1.5f;
            }
            return false;
        }

        private void SetMove(Vector2 dir)
        {
            ReleaseMove();
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            if (dir.x > 0.38f) _held.Add(Key.D); else if (dir.x < -0.38f) _held.Add(Key.A);
            if (dir.y > 0.38f) _held.Add(Key.W); else if (dir.y < -0.38f) _held.Add(Key.S);
        }

        private void ReleaseMove() { _held.Remove(Key.W); _held.Remove(Key.A); _held.Remove(Key.S); _held.Remove(Key.D); }
        private void ReleaseAll() { _held.Clear(); _mouseLeft = false; _charging = false; }

        private void AimAt(Vector2 world)
        {
            var cam = Camera.main;
            if (cam) _mouseScreen = cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
        }

        private void FlushInput()
        {
            if (Keyboard.current != null)
            {
                var keys = new List<Key>(_held); keys.AddRange(_tap);
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys.ToArray()));
                _tap.Clear();
            }
            if (Mouse.current != null)
            {
                var ms = new MouseState { position = _mouseScreen };
                if (_mouseLeft) ms = ms.WithButton(MouseButton.Left);
                InputSystem.QueueStateEvent(Mouse.current, ms);
            }
        }

        // ============================ 끝 ============================
        private void Finish()
        {
            _mode = Mode.Done;
            ReleaseAll(); FlushInput();
            Event("finish", $"\"gold\":{Core.GameSession.Gold}");
            _summary.AppendLine($"마지막 소지금 {Core.GameSession.Gold} G").AppendLine().AppendLine($"캡처 {_shot}장 · 자세한 기록은 events.jsonl");
            File.WriteAllText(Path.Combine(_dir, "summary.md"), _summary.ToString(), new UTF8Encoding(false));
            Debug.Log("[PlaytestBot] 끝 - " + _dir + "\n" + _summary);
            UnityEditor.EditorApplication.isPlaying = false;
        }

        // ============================ 도우미 ============================
        private void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (_stats != null) _stats.errors++;
            string first = stack != null ? stack.Split('\n')[0] : "";
            Event("log_error", $"\"type\":\"{type}\",\"msg\":\"{Esc(msg)}\",\"at\":\"{Esc(first)}\"");
        }

        private void Event(string ev, string body)
        {
            if (_events == null) return;
            float t = _stats != null ? Time.time - _stats.start : Time.time;
            _events.WriteLine($"{{\"run\":{_run},\"t\":{F(t)},\"scene\":\"{SceneManager.GetActiveScene().name}\",\"ev\":\"{ev}\"{(string.IsNullOrEmpty(body) ? "" : "," + body)}}}");
        }

        private void Capture(string tag)
        {
            _shot++;
            string file = Path.Combine(_dir, $"{_shot:000}_r{_run}_{tag}.png");
            ScreenCapture.CaptureScreenshot(file);
            Event("shot", $"\"file\":\"{Path.GetFileName(file)}\"");
        }

        private bool Every(ref float at, float interval)
        {
            if (Time.time < at) return false;
            at = Time.time + interval; return true;
        }

        private static T ReadField<T>(object o, string name)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (f != null && f.GetValue(o) is T v) return v;
            }
            return default;
        }

        private static void WriteField(object o, string name, object value)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (f != null) { f.SetValue(o, value); return; }
            }
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string V(Vector2 v) => $"[{F(v.x)},{F(v.y)}]";
        private static string Clean(string n) => n.Replace("(Clone)", "").Trim();
        private static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "'").Replace("\n", " ").Replace("\r", "");
    }
}
#endif
