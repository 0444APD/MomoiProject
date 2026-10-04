using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GBible.Data;
using UnityEngine;

namespace GBible.Samples
{
    // 확인용 패널(선택 기능 '확인용 패널·샘플', GBIBLE_SAMPLES): 게임 코드 없이 새 부품을 플레이 중에 눌러 보는 창.
    // 상태 효과·가로채기·비용·예약·미리보기·선택 대기·가중치 표·밸런스 값·연출 대기열·슬롯·설정·월드 상태.
    // 데이터는 코드로 정의한다(플레이어 데이터 'checkRun'). DB 추가 메뉴(MomoiProject › DB 추가 › 확인용 항목)로 같은 ID의 DB 항목을 만들면
    // 그 DB 항목이 쓰인다(DB에서 값을 바꿔 가며 확인). 기능을 끄면 이 코드는 빌드에서 빠진다.

    public static class CheckIds
    {
        public const string Run = "checkRun";
        public const string Poison = "checkPoison";
        public const string Guard = "checkGuard";
        public const string EnemyHp = "checkEnemyHp";
        public const string Loot = "checkLoot";
        public const string Chest = "check.chest";
    }

    [Serializable]
    public sealed class CheckUnit
    {
        public string id;
        public string name;
        public double attack = 10;
        public int hp = 30;
        public int maxHp = 30;
    }

    [Serializable]
    public sealed class CheckRun
    {
        public List<CheckUnit> units = new List<CheckUnit>
        {
            new CheckUnit { id = "checkHero", name = "영웅" },
            new CheckUnit { id = "checkFoe", name = "적" },
        };
        public int turn = 1;
        public int energy = 3;
        public List<string> loot = new List<string>();
        public string picked = "";

        public CheckUnit Hero => units.FirstOrDefault(u => u.id == "checkHero");
        public CheckUnit Foe => units.FirstOrDefault(u => u.id == "checkFoe");
    }

    // 전리품 원본(DB › 인스턴스, DB 추가 메뉴가 checkSword·checkShield·checkPotion·checkGem을 만든다).
    [Serializable]
    public sealed class CheckItem
    {
        public string name = "";
    }

    [Serializable, GameEvent("확인용 턴 끝")]
    public sealed class CheckTurnEnded
    {
        public int turn;
    }

    [Serializable, Intent("확인용 피해")]
    public sealed class CheckDamage
    {
        public string targetId;
        public int amount;
        public bool cancelled;
    }

    public static class CheckRules
    {
        public static CheckRun Run(GameApi game) => game.Get<CheckRun>(CheckIds.Run);

        // 피해: 가로채기 자리(보호막 상태 효과가 반으로 줄인다) → 체력.
        public static void Damage(GameApi game, CheckUnit target, int amount)
        {
            if (target == null) return;
            var hit = game.Intercept(new CheckDamage { targetId = target.id, amount = amount });
            if (hit.cancelled || hit.amount <= 0) return;
            target.hp = Math.Max(0, target.hp - hit.amount);
        }
    }

    // ── 행동 ───────────────────────────────────────────────

    [Serializable, BlockInfo("확인용: 턴 끝")]
    public sealed class CheckEndTurn : GameAction
    {
        protected override void Run()
        {
            var run = CheckRules.Run(Game);
            Game.Events.Publish(new CheckTurnEnded { turn = run.turn });
            run.turn++;
            run.energy = 3;
        }
    }

    [Serializable, BlockInfo("확인용: 때리기")]
    public sealed class CheckHit : GameAction
    {
        public CheckUnit target;
        public int amount = 6;
        protected override void Run() => CheckRules.Damage(Game, target, amount);
    }

    [Serializable, BlockInfo("확인용: 에너지 2 내고 때리기")]
    public sealed class CheckPayHit : GameAction
    {
        public CheckUnit target;
        protected override void Run()
        {
            Game.Pay(CheckIds.Run, "energy", 2, what: "에너지");
            CheckRules.Damage(Game, target, 8);
        }
    }

    [Serializable, BlockInfo("확인용: 상태 효과 걸기")]
    public sealed class CheckApply : GameAction
    {
        public CheckUnit target;
        public string status;
        protected override void Run() => Game.ApplyStatus(target, status);
    }

    [Serializable, BlockInfo("확인용: 2초 뒤 때리기 예약")]
    public sealed class CheckSchedule : GameAction
    {
        public CheckUnit target;
        protected override void Run() => Game.ScheduleAfter(2, new CheckHit { target = target, amount = 5 }, source: "check");
    }

    [Serializable, BlockInfo("확인용: 고르기 요구")]
    public sealed class CheckAsk : GameAction
    {
        protected override void Run() =>
            Game.Ask("하나 고르세요", new[] { new ChoiceCandidate("sword", "검"), new ChoiceCandidate("shield", "방패"), new ChoiceCandidate("potion", "물약") }, new CheckPicked());
    }

    [Serializable, BlockInfo("확인용: 고른 것 받기")]
    public sealed class CheckPicked : GameAction
    {
        [Chosen] public string pick;
        protected override void Run() => CheckRules.Run(Game).picked = pick ?? "";
    }

    [Serializable, BlockInfo("확인용: 전리품 3개 뽑기")]
    public sealed class CheckRollLoot : GameAction
    {
        protected override void Run()
        {
            var run = CheckRules.Run(Game);
            run.loot.Clear();
            run.loot.AddRange(Game.Roll(CheckIds.Loot, 3, unique: true));
        }
    }

    [Serializable, BlockInfo("확인용: 상자 열기/닫기")]
    public sealed class CheckToggleChest : GameAction
    {
        protected override void Run() => Game.SetWorld(CheckIds.Chest, !Game.WorldFlag(CheckIds.Chest));
    }

    [Serializable, BlockInfo("확인용: 처음으로")]
    public sealed class CheckReset : GameAction
    {
        protected override void Run() => Game.Set(CheckIds.Run, new CheckRun());
    }

    // ── Block(DB에서 조립할 때) ─────────────────────────────

    [Serializable, BlockInfo("확인용 유닛 모두", Category = "대상", Description = "확인용 패널의 영웅과 적.")]
    public sealed class CheckAllUnits : TargetSelector
    {
        protected override IEnumerable<object> Select(IEnumerable<object> targets) => CheckRules.Run(Game)?.units ?? new List<CheckUnit>();
    }

    [Serializable, BlockInfo("확인용 피해", Category = "확인용", Description = "대상의 체력을 깎는다(가로채기 '확인용 피해'를 거친다).")]
    public sealed class CheckHurt : Effect<CheckUnit>
    {
        public int amount = 2;
        protected override void Apply(CheckUnit target) => CheckRules.Damage(Game, target, amount * Math.Max(1, Game.StatusStacks(target, Use?.Context?.Owner ?? "")));
        public override string Describe() => $"확인용 피해 {amount}";
    }

    // DB에 확인용 항목이 없을 때 쓰는 정의(DB 추가 메뉴가 만드는 것과 같다).
    public static class CheckDefaults
    {
        public static StatusDefinition Poison()
        {
            var poison = new StatusDefinition
            {
                Id = CheckIds.Poison, DisplayName = "확인용 독", MaxStacks = 5, Reapply = StatusReapply.AddStacksAndRefresh,
                Duration = StatusDuration.Events, Length = 3, TickEvent = typeof(CheckTurnEnded).FullName,
                When = new EventTrigger { eventType = typeof(CheckTurnEnded).FullName },
            };
            var hurt = new TargetedOutcome();
            hurt.targets.Add(new SelfTarget());
            hurt.effects.Add(new CheckHurt { amount = 2 });
            poison.Outcomes.Add(hurt);
            return poison;
        }

        public static StatusDefinition Guard()
        {
            var guard = new StatusDefinition
            {
                Id = CheckIds.Guard, DisplayName = "확인용 보호막", MaxStacks = 1, Reapply = StatusReapply.Refresh,
                Duration = StatusDuration.Events, Length = 2, TickEvent = typeof(CheckTurnEnded).FullName,
                When = new InterceptTrigger { intentType = typeof(CheckDamage).FullName }, HolderField = "targetId",
            };
            guard.Modifiers.Add(new StatusModifier { attribute = nameof(CheckUnit.attack), kind = ModifierKind.Percent, valuePerStack = 0.2 });
            guard.Outcomes.Add(new ModifyIntentOutcome { path = "amount", op = IntentOp.Multiply, value = "0.5" });
            return guard;
        }

        public static WeightTableDefinition Loot()
        {
            var table = new WeightTableDefinition { Id = CheckIds.Loot, EmptyWeight = 1 };
            table.Entries.Add(new WeightEntry { item = "checkSword", weight = 3 });
            table.Entries.Add(new WeightEntry { item = "checkShield", weight = 2 });
            table.Entries.Add(new WeightEntry { item = "checkPotion", weight = 2, minCount = 1, maxCount = 2 });
            table.Entries.Add(new WeightEntry { item = "checkGem", weight = 1 });
            return table;
        }

        public static Curve EnemyHp() => new Curve { kind = CurveKind.Linear, step = 5 };
    }

    // ── 패널 ───────────────────────────────────────────────

    [AddComponentMenu("")]
    public sealed class CheckPanel : MonoBehaviour
    {
        static readonly string[] Tabs = { "규칙", "선택·뽑기", "연출", "슬롯·설정", "월드" };

        bool _open = true;
        int _tab;
        Rect _rect = new Rect(16, 16, 470, 600);
        Vector2 _scroll;
        string _message = "";
        int _presented;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Hook()
        {
            GBibleRuntime.Started -= OnStarted;
            GBibleRuntime.Started += OnStarted;
        }

        static void OnStarted(GameSession session)
        {
            if (!session.Data.IsDefined(CheckIds.Run)) session.Data.Define(CheckIds.Run, DataZone.Session, new CheckRun(), "확인용 판");
            if (session.Statuses.Find(CheckIds.Poison) == null) session.Statuses.Define(CheckDefaults.Poison());
            if (session.Statuses.Find(CheckIds.Guard) == null) session.Statuses.Define(CheckDefaults.Guard());
            if (!session.Balance.Has(CheckIds.EnemyHp)) session.Balance.Define(CheckIds.EnemyHp, 20, CheckDefaults.EnemyHp());
            if (!session.Tables.Has(CheckIds.Loot)) session.Tables.Define(CheckDefaults.Loot());
            if (FindAnyObjectByType<CheckPanel>() != null) return;
            var host = new GameObject("[MomoiProject 확인용 패널]");
            DontDestroyOnLoad(host);
            host.AddComponent<CheckPanel>();
        }

        static GameApi Game => GBibleRuntime.Game;

        void Do(GameAction action)
        {
            try
            {
                Game.Do(action);
                _message = "";
            }
            catch (Exception e)
            {
                _message = (e is GameFailure ? "플레이어용 실패: " : "실패: ") + e.Message;
            }
        }

        void OnGUI()
        {
            if (Game == null) return;
            if (!_open)
            {
                if (GUI.Button(new Rect(16, 16, 140, 28), "GBible 확인용 열기")) _open = true;
                return;
            }
            _rect = GUI.Window(73120, _rect, DrawWindow, "GBible 확인용 패널");
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            _tab = GUILayout.Toolbar(_tab, Tabs);
            if (GUILayout.Button("닫기", GUILayout.Width(48))) _open = false;
            GUILayout.EndHorizontal();
            GUILayout.Label($"연출 중: {(GameView.IsPresenting ? "예" : "아니오")} · 입력 막힘(Busy): {(GameView.Busy ? "예" : "아니오")} · Undo 가능 {GBibleRuntime.Session.UndoCount}");
            _scroll = GUILayout.BeginScrollView(_scroll);
            try
            {
                switch (_tab)
                {
                    case 0: DrawRules(); break;
                    case 1: DrawChoice(); break;
                    case 2: DrawPresentation(); break;
                    case 3: DrawSlotsAndSettings(); break;
                    default: DrawWorld(); break;
                }
            }
            catch (Exception e)
            {
                GUILayout.Label("그리기 오류: " + e.Message);
            }
            GUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Undo")) GBibleRuntime.Session.Undo();
            if (GUILayout.Button("처음으로")) Do(new CheckReset());
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        // ── 규칙: 상태 효과·가로채기·비용·예약·미리보기·능력치 ──
        void DrawRules()
        {
            var run = CheckRules.Run(Game);
            if (run == null) return;
            GUILayout.Label($"턴 {run.turn} · 에너지 {run.energy}");
            foreach (var unit in run.units)
            {
                var statuses = string.Join(", ", Game.Statuses(unit).Select(s => $"{s.status}×{s.stacks}(남음 {s.remaining:0.#})"));
                GUILayout.Label($"{unit.name}: 체력 {unit.hp}/{unit.maxHp} · 공격력 {Game.Attribute(unit, nameof(CheckUnit.attack)):0.##}(기본 {unit.attack}) · {(statuses.Length == 0 ? "상태 없음" : statuses)}");
            }
            var hero = run.Hero;
            var foe = run.Foe;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("턴 끝")) Do(new CheckEndTurn());
            if (GUILayout.Button("영웅이 적 때리기(6)")) Do(new CheckHit { target = foe, amount = 6 });
            if (GUILayout.Button("적이 영웅 때리기(6)")) Do(new CheckHit { target = hero, amount = 6 });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("적에게 독")) Do(new CheckApply { target = foe, status = CheckIds.Poison });
            if (GUILayout.Button("영웅에게 보호막")) Do(new CheckApply { target = hero, status = CheckIds.Guard });
            if (GUILayout.Button("2초 뒤 적 때리기(예약)")) Do(new CheckSchedule { target = foe });
            GUILayout.EndHorizontal();

            // 미리보기·할 수 있는지: 아무것도 남기지 않는다(타임라인·Undo에 안 생김).
            var after = Game.Preview(new CheckPayHit { target = foe }, g => CheckRules.Run(g).Foe?.hp ?? 0);
            var can = Game.CanDo(new CheckPayHit { target = foe }, out var why);
            GUILayout.Label($"미리보기: '에너지 2 내고 때리기'를 하면 적 체력 {foe?.hp} → {after} · 할 수 있음: {(can ? "예" : "아니오 — " + why)}");
            if (GUILayout.Button("에너지 2 내고 적 때리기(8)")) Do(new CheckPayHit { target = foe });
            GUILayout.Label("확인할 것: 독은 턴 끝마다 중첩×2 피해, 3번 뒤 사라짐 · 보호막은 영웅이 받는 피해 반, 공격력 +20%, 2턴 · 에너지가 모자라면 플레이어용 실패 글 · 예약은 2초 뒤 타임라인에 '예약 실행'");
        }

        // ── 선택 대기·가중치 표·밸런스 값 ──
        void DrawChoice()
        {
            var run = CheckRules.Run(Game);
            if (run == null) return;
            if (GUILayout.Button("고르기 요구(검·방패·물약 중 하나)")) Do(new CheckAsk());
            var choice = Game.Choice;
            if (choice != null)
            {
                GUILayout.Label($"기다리는 선택: {choice.prompt} ({choice.min}~{choice.max}개) — 모니터 › 명령 탭에서도 답할 수 있다");
                GUILayout.BeginHorizontal();
                foreach (var option in choice.options)
                    if (GUILayout.Button(option.label ?? option.id))
                    {
                        try
                        {
                            Game.Answer(choice.id, option.id);
                            _message = "";
                        }
                        catch (Exception e) { _message = e.Message; }
                    }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label($"고른 것: {(run.picked.Length == 0 ? "없음" : run.picked)}");
            GUILayout.Space(8);
            if (GUILayout.Button("전리품 3개 뽑기(서로 다른 것)")) Do(new CheckRollLoot());
            GUILayout.Label($"전리품: {(run.loot.Count == 0 ? "없음" : string.Join(", ", run.loot))}");
            var chances = GBibleRuntime.Session.Tables.Chances(CheckIds.Loot);
            GUILayout.Label("확률: " + string.Join(" · ", chances.Select(c => $"{c.item} {c.chance * 100:0.#}%")) + " (나머지는 꽝)");
            GUILayout.Space(8);
            GUILayout.Label("밸런스 값 checkEnemyHp(층별): " + string.Join(" · ", Enumerable.Range(1, 5).Select(x => $"{x}층 {Game.Balance(CheckIds.EnemyHp, x):0.##}")));
            GUILayout.Label("확인할 것: DB › 밸런스 값 checkEnemyHp를 플레이 중에 고치면 0.5초 안에 이 숫자가 바뀐다(저장 전이라도).");
        }

        // ── 연출 대기열 ──
        void DrawPresentation()
        {
            GUILayout.Label($"끝난 연출 수: {_presented} · 속도 {GameView.PresentationSpeed:0.#}배");
            if (GUILayout.Button("연출 3개 넣기(각 0.8초)"))
                for (int i = 1; i <= 3; i++) GameView.Present(Step(i));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("빨리 넘기기(Skip)")) GameView.SkipPresentation();
            if (GUILayout.Button("버리기(Clear)")) GameView.ClearPresentation();
            GUILayout.EndHorizontal();
            GameView.PresentationSpeed = GUILayout.HorizontalSlider(GameView.PresentationSpeed, 0.25f, 4f);
            GUILayout.Label("확인할 것: 넣으면 '연출 중·Busy'가 예가 되고 하나씩 끝난다 · Skip은 빨라진다 · 완료 콜백 연출은 done을 부르지 않으면 30초 뒤 경고하고 넘어간다.");
            if (GUILayout.Button("done을 부르지 않는 연출 넣기(30초 뒤 경고)")) GameView.Present(_ => { }, 30f);
        }

        IEnumerator Step(int index)
        {
            _message = $"연출 {index} 재생 중";
            yield return GameView.Wait(0.8f);
            _presented++;
            _message = $"연출 {index} 끝";
        }

        // ── 슬롯·설정 ──
        void DrawSlotsAndSettings()
        {
            GUILayout.Label($"지금 슬롯: '{(GBibleRuntime.Slot.Length == 0 ? "기본" : GBibleRuntime.Slot)}' · 플레이 시간 {GBibleRuntime.PlayTime:0}초");
            foreach (var name in new[] { "", "1", "2" })
            {
                var info = GBibleRuntime.SlotInfoOf(name);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{(name.Length == 0 ? "기본" : name)}: {(info.IsEmpty ? "비어 있음" : $"저장 {info.SavedAt:MM-dd HH:mm} · {info.PlayTime:0}초 · 판 {(info.HasRun ? "있음" : "없음")}")}", GUILayout.Width(300));
                if (GUILayout.Button("쓰기")) GBibleRuntime.UseSlot(name);
                if (GUILayout.Button("지우기")) GBibleRuntime.DeleteSlot(name);
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("지금 슬롯을 슬롯 2로 복사"))
            {
                try { GBibleRuntime.CopySlot(GBibleRuntime.Slot, "2"); _message = "복사함"; }
                catch (Exception e) { _message = e.Message; }
            }
            GUILayout.Label("확인할 것: 슬롯을 바꾸면 세션이 새로 시작(이 패널의 값이 그 슬롯 것으로) · 폴더 persistentDataPath/MomoiProject/slots/<이름>/");
            GUILayout.Space(8);
            GUILayout.Label("설정(DB › 설정, 기기마다 하나 — settings.json):");
            foreach (var setting in GameSettings.All)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{setting.Id}({setting.category}, {setting.target})", GUILayout.Width(220));
                switch (setting.spec.kind)
                {
                    case SettingKind.Toggle:
                        var on = GameSettings.Toggle(setting.Id);
                        if (GUILayout.Button(on ? "켜짐" : "꺼짐")) GameSettings.Set(setting.Id, !on);
                        break;
                    case SettingKind.Slider:
                        var value = (float)GameSettings.Number(setting.Id);
                        var next = GUILayout.HorizontalSlider(value, (float)setting.spec.min, (float)setting.spec.max, GUILayout.Width(140));
                        GUILayout.Label(value.ToString("0.##", CultureInfo.InvariantCulture), GUILayout.Width(40));
                        if (Math.Abs(next - value) > 1e-4) GameSettings.Set(setting.Id, next);
                        break;
                    case SettingKind.Choice:
                        var choices = GameSettings.ChoicesOf(setting.Id);
                        var current = GameSettings.Choice(setting.Id);
                        var index = choices.ToList().FindIndex(c => c.value == current);
                        if (GUILayout.Button(index >= 0 ? choices[index].label : current) && choices.Count > 0)
                            GameSettings.Set(setting.Id, choices[(index + 1 + choices.Count) % choices.Count].value);
                        break;
                    default:
                        GUILayout.Label(GameSettings.Text(setting.Id));
                        break;
                }
                if (GUILayout.Button("기본값", GUILayout.Width(56))) GameSettings.Reset(setting.Id);
                GUILayout.EndHorizontal();
            }
            if (GameSettings.All.Count == 0) GUILayout.Label("DB › 설정에 항목이 없다(DB 추가 메뉴 '확인용 항목'이 만든다).");
        }

        // ── 월드 상태 ──
        void DrawWorld()
        {
            GUILayout.Label($"월드 상태 '{CheckIds.Chest}': {(Game.World(CheckIds.Chest) ?? "(없음)")} · 열림: {(Game.WorldFlag(CheckIds.Chest) ? "예" : "아니오")}");
            if (GUILayout.Button("상자 열기/닫기")) Do(new CheckToggleChest());
            GUILayout.Label("확인할 것: Undo로 돌아간다 · 판 저장(모니터 › 명령 › 저장 › 판 저장 → 판 불러오기)으로 이어진다 · 씬 오브젝트에 컴포넌트 '월드 상태 연결'(이름 check.chest)을 붙이면 켜지고 꺼진다.");
        }
    }
}
