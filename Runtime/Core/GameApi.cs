using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GBible
{
    // 게임 코드가 쓰는 창구. 세션마다 하나(session.Game). 행동·효과·조건 Block 안에서는 Game 속성으로 바로 쓴다.
    //   Game.All<Unit>()        플레이어 데이터 안의 Unit 전부(리스트·클래스 안쪽까지)
    //   Game.Find<Unit>(id)     id 필드가 같은 Unit
    //   Game.Random("damage")   결정론적 난수
    //   Game.Do(new MoveUnit{…}) 행동 실행(Undo 한 단위)   Game.Use("ability.fireball", 사용자, 대상들)
    // 행동·효과 안에서 꺼낸 객체는 필드를 그냥 고치면 된다(시스템이 바뀐 곳을 찾아 기록한다: Undo·Journal·저장).
    // 오래 들고 있을 것은 객체가 아니라 id로(Undo·불러오기 뒤에는 다른 인스턴스일 수 있다).
    public sealed class GameApi
    {
        public GameSession Session { get; }

        public GameApi(GameSession session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public EventBus Events => Session.Events;

        public RandomStream Random(string name) => Session.Random.Get(name);

        public ActionRecord Do(GameAction action) => Session.Do(action);

        // 능력(DB › 능력)을 쓴다. 사용자·대상은 id 필드가 있는 객체(또는 id 문자열).
        public ActionRecord Use(string ability, object user, params object[] targets) =>
            Session.Do(new UseAbility
            {
                ability = ability,
                user = user as string ?? IdOf(user),
                targets = (targets ?? Array.Empty<object>()).Select(t => t as string ?? IdOf(t)).ToList(),
            });

        // 지금 쓸 수 있는지(능력·사용 아이템의 트리거 = 사용 조건만 본다. 아무것도 바꾸지 않는다). 버튼을 미리 흐리게 할 때.
        //   if (!Game.CanUse("ability.heal", hero, out var why)) button.tooltip = why;
        public bool CanUse(string ability, object user, params object[] targets) => CanUse(ability, user, targets, out _);

        public bool CanUse(string ability, object user, out string reason) => CanUse(ability, user, Array.Empty<object>(), out reason);

        public bool CanUse(string ability, object user, object[] targets, out string reason)
        {
            reason = null;
            var definition = Session.Abilities.Get(ability);
            if (definition == null)
            {
                reason = $"'{ability}'은(는) 쓸 수 있는 능력·아이템이 아닙니다.";
                return false;
            }
            var who = user is string userId ? Find(userId) : user;
            var picked = (targets ?? Array.Empty<object>()).Select(t => t is string id ? Find(id) : t).Where(t => t != null).ToList();
            if (Session.Triggers.CanUse(ability, definition.When, who, picked, out _)) return true;
            reason = "사용 조건을 만족하지 않습니다: " + definition.When.Natural(Session);
            return false;
        }

        // ── 찾기 ───────────────────────────────────────────────

        // 플레이어 데이터 전체에서 T를 모은다(키 이름순 → 들어 있는 순서. 순서가 늘 같다).
        public List<T> All<T>() where T : class
        {
            var result = new List<T>();
            foreach (var key in DataKeys())
                if (CanContain(Session.Data.TypeOf(key), typeof(T)))
                    CollectFrom(key, result);
            return result;
        }

        // 플레이어 데이터 하나(key) 안의 T만.
        public List<T> All<T>(string key) where T : class
        {
            var result = new List<T>();
            CollectFrom(key, result);
            return result;
        }

        // Mirror 데이터는 사본 나무에서(리플렉션 없음), 예전 방식 데이터는 필드를 훑어서.
        void CollectFrom<T>(string key, List<T> into) where T : class
        {
            var mirror = Session.Data.SyncedMirror(key);
            Session.Data.Get(key);
            if (mirror != null) Mirror.Collect(mirror, into);
            else Collect(Session.Data.Get(key), into, 0);
        }

        public T Find<T>(string id) where T : class => (T)Find(typeof(T), id);

        // 형식을 런타임에 정할 때(행동의 참조 필드 등).
        public object Find(Type type, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            RequireId(type);
            foreach (var key in DataKeys())
            {
                if (!CanContain(Session.Data.TypeOf(key), type)) continue;
                var mirror = Session.Data.SyncedMirror(key);
                Session.Data.Get(key);
                if (mirror != null)
                {
                    if (Mirror.FindById(mirror, type, id) is { } hit) return hit;
                    continue;
                }
                var all = new List<object>();
                Collect(Session.Data.Get(key), all, 0);
                var found = all.FirstOrDefault(item => type.IsInstanceOfType(item) && IdOf(item) == id);
                if (found != null) return found;
            }
            return null;
        }

        // id 필드에 값을 넣는다(id만 든 객체를 만들 때).
        internal static void SetId(object item, string id)
        {
            var member = item == null ? null : IdMember(item.GetType());
            if (member != null) FieldAccess.Find(item.GetType(), member.Name)?.Set(item, id);
        }

        // 이 형식에 string id(필드·속성)가 있는가(찾거나 가리킬 수 있는 객체인가).
        public static bool HasId(Type type) => type != null && IdMember(type) != null;

        // 형식을 모를 때: id 필드가 있는 모든 객체에서 찾는다.
        public object Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var key in DataKeys())
            {
                // Mirror 데이터는 사본 나무에서(리플렉션 없음), 예전 방식 데이터는 필드를 훑어서.
                var mirror = Session.Data.SyncedMirror(key);
                if (mirror != null)
                {
                    if (Mirror.FindById<object>(mirror, id) is { } hit) return hit;
                    continue;
                }
                var all = new List<object>();
                Collect(Session.Data.Get(key), all, 0);
                var found = all.FirstOrDefault(item => IdMember(item.GetType()) != null && IdOf(item) == id);
                if (found != null) return found;
            }
            return null;
        }

        // 단순 값(숫자·글자 등) 플레이어 데이터.
        public T Get<T>(string key) => Session.Data.Get<T>(key);
        public void Set(string key, object value) => Session.Data.Set(key, value);

        // ── 능력치·Modifier ─────────────────────────────────────
        // 능력치 = id가 있는 객체의 숫자 필드. 최종값 = (기본 + 플랫 합) × (1 + 퍼센트 합) × Π(1 + 독립 곱).
        //   var attack = Game.Attribute(hero, nameof(Hero.attack));
        public double Attribute(object entity, string attribute) => Session.Attributes.Get(entity, attribute);

        // Modifier를 붙인다(행동 안에서). source = 붙인 쪽 ID(장비·상태 효과·유물). permanent면 프로필에 남는다. 돌려주는 값은 Modifier id.
        public string AddModifier(object entity, string attribute, ModifierKind kind, double value, string source, bool permanent = false) =>
            Session.Attributes.Add(entity, attribute, kind, value, source, permanent);

        // 그 출처의 Modifier를 뗀다(entity를 주면 그 대상 것만). 뗀 개수.
        public int RemoveModifiers(string source, object entity = null) => Session.Attributes.RemoveBySource(source, entity);

        public IReadOnlyList<Modifier> Modifiers(object entity, string attribute = null) => Session.Attributes.Of(entity, attribute);

        // ── 가로채기 ───────────────────────────────────────────
        // 의도를 가로채기 트리거들에 통과시키고 (고쳐진) 의도를 돌려준다. 규칙 함수가 일을 하기 직전에 부른다.
        //   var hit = Game.Intercept(new DamageIntent { … });  if (hit.cancelled) return;
        public T Intercept<T>(T intent) where T : class => Session.Triggers.Intercept(intent);

        // ── 상태 효과 ───────────────────────────────────────────
        // 상태 효과(DB › 상태 효과)를 건다·푼다(행동 안에서). target = 객체 또는 id. 다시 걸 때는 정의의 규칙(중첩·갱신)을 따른다.
        //   Game.ApplyStatus(enemy, GameIds.Status.Poison, stacks: 3);
        public ActiveStatus ApplyStatus(object target, string statusId, int stacks = 1, string source = null) => Session.Statuses.Apply(target, statusId, stacks, source);

        // stacks를 주면 그만큼 줄이고 0이 되면 푼다. 0이면 통째로 푼다. 푼 것은 '끝날 때 결과'를 돌리지 않는다.
        public bool RemoveStatus(object target, string statusId, int stacks = 0) => Session.Statuses.Remove(target, statusId, stacks);
        public int StatusStacks(object target, string statusId) => Session.Statuses.Stacks(target, statusId);
        public bool HasStatus(object target, string statusId) => Session.Statuses.Stacks(target, statusId) > 0;
        public IReadOnlyList<ActiveStatus> Statuses(object target) => Session.Statuses.Of(target);

        // ── 예약 ───────────────────────────────────────────────
        // 행동을 나중에 실행한다(저장·Undo 그대로). 돌려주는 값은 예약 id. 넘긴 행동의 대상 필드는 실행할 때 다시 찾는다.
        //   Game.ScheduleAfter(2.5, new Explode { target = enemy });     게임 시간 2.5초 뒤
        //   Game.ScheduleOn<TurnEnded>(3, new Explode { target = enemy }); TurnEnded를 3번 받은 순간
        public string ScheduleAfter(double seconds, GameAction action, string source = null, bool permanent = false) =>
            Session.Schedules.After(seconds, action, source, permanent);

        public string ScheduleOn<TEvent>(int count, GameAction action, string source = null, bool permanent = false) =>
            Session.Schedules.On(typeof(TEvent), count, action, source, permanent);

        public bool CancelSchedule(string id) => Session.Schedules.Cancel(id);
        public int CancelSchedules(string source) => Session.Schedules.CancelBySource(source);

        // ── 실패(플레이어에게 보일 글) ──────────────────────────
        // 행동 안에서 던지면 행동 전체가 취소되고 Message는 지금 언어의 글이다. 텍스트 표에 키가 없으면 fallback 문장(같은 {값} 틀).
        //   if (target.frozen) throw Game.Fail("fail.frozen", "얼어 있어 움직일 수 없습니다.");
        //   throw Game.Fail("fail.tooFar", "{range}칸 안의 대상만 고를 수 있습니다.", ("range", 3));
        public GameFailure Fail(string key, params (string name, object value)[] args) => new GameFailure(key, TextArgs.From(args));
        public GameFailure Fail(string key, string fallback, params (string name, object value)[] args) => new GameFailure(key, TextArgs.From(args), fallback);

        // ── 밸런스 값·가중치 표 ─────────────────────────────────
        // 이름 붙은 숫자(DB › 밸런스 값). x를 주면 곡선으로 그 단계의 값.
        //   var energy = Game.Balance(GameIds.Balance.StartEnergy);     var hp = Game.Balance(GameIds.Balance.EnemyHp, floor);
        public double Balance(string id) => Session.Balance.Get(id);
        public double Balance(string id, double x) => Session.Balance.Get(id, x);

        // 가중치 표(DB › 가중치 표)에서 뽑는다(행동 안에서). 하나: 항목 ID(꽝이면 null). 여럿: unique면 서로 다른 것.
        public string Roll(string tableId) => Session.Tables.Roll(tableId).FirstOrDefault();
        public List<string> Roll(string tableId, int count, bool unique = false) => Session.Tables.Roll(tableId, count, unique);

        // ── 미리보기 ───────────────────────────────────────────
        // 행동을 해 보고 결과만 읽는다(아무것도 남지 않음). 화면의 예상 피해·버튼을 막는 이유에 쓴다. 행동 밖에서 부른다.
        //   var hpAfter = Game.Preview(new PlayCard { card = c, target = enemy }, g => g.Find<Fighter>(enemy.id).hp);
        //   if (!Game.CanDo(new PlayCard { card = c, target = enemy }, out var why)) button.Disable(why);
        public T Preview<T>(GameAction action, Func<GameApi, T> read) => Session.Actions.Preview(action, () => read(this));

        // 지금 하면 성공하는가(해 보고 되돌린다). 실패하면 reason = 실패 글(GameFailure면 지금 언어).
        public bool CanDo(GameAction action, out string reason)
        {
            try
            {
                Session.Actions.Preview<object>(action, null);
                reason = null;
                return true;
            }
            catch (Exception e)
            {
                reason = e.Message;
                return false;
            }
        }

        public bool IsPreviewing => Session.Actions.IsPreviewing;

        // ── 월드 상태 ───────────────────────────────────────────
        // 씬에 놓인 것들의 상태를 이름으로 기억한다(행동 안에서 바꾼다). 판 값이 프로필 값보다 앞선다.
        //   Game.SetWorld("forest.chest3", "opened");   Game.WorldFlag("forest.boss")
        public string World(string id) => Session.World.Get(id);
        public bool WorldFlag(string id) => WorldStateSystem.IsTrue(Session.World.Get(id));
        public void SetWorld(string id, string value, bool permanent = false) => Session.World.Set(id, value, permanent);
        public void SetWorld(string id, bool flag, bool permanent = false) => Session.World.Set(id, flag ? "true" : null, permanent);
        public void ClearWorld(string id) => Session.World.Clear(id);

        // ── 선택 대기 ───────────────────────────────────────────
        // 플레이어에게 고르기를 요구한다(행동 안에서). 고르면 then의 [Chosen] 필드에 고른 것이 들어가 실행된다. 돌려주는 값은 선택 id.
        //   Game.Ask("버릴 카드", run.hand, new DiscardChosen(), min: 1, max: 1);
        // 후보는 id가 있는 객체 또는 id 글자. label은 모니터 등에 보일 이름(비우면 텍스트 표의 'id.name', 없으면 id).
        // 고른 뒤 할 일은 모두 then에 쓴다(Ask 다음 줄은 바로 실행된다).
        public string Ask(string prompt, IEnumerable<object> candidates, GameAction then, int min = 1, int max = 1, string source = null, string player = null, Func<object, string> label = null) =>
            Session.Choices.Ask(prompt, (candidates ?? Enumerable.Empty<object>()).Where(c => c != null)
                .Select(c =>
                {
                    var id = c as string ?? IdOf(c);
                    return new ChoiceCandidate(id, label?.Invoke(c) ?? Texts.NameOf(id));
                }), then, min, max, source, player);

        // 글 선택지: Game.Ask("자세", new[] { new ChoiceCandidate("attack", "공격"), new ChoiceCandidate("guard", "방어") }, new ChooseStance());
        public string Ask(string prompt, IEnumerable<ChoiceCandidate> options, GameAction then, int min = 1, int max = 1, string source = null, string player = null) =>
            Session.Choices.Ask(prompt, options, then, min, max, source, player);

        // 지금 답을 기다리는 선택(먼저 요구한 것). 없으면 null. 화면은 이것을 보여 주고, 있는 동안 다른 입력을 막는다.
        public PendingChoice Choice => Session.Choices.Current;
        public IReadOnlyList<PendingChoice> Choices => Session.Choices.All;
        public bool IsWaiting => Session.Choices.Current != null;

        // 답하기(행동 밖, 플레이어 입력에서). 고른 것은 객체 또는 id. 개수·후보가 맞지 않으면 실패하고 아무것도 바뀌지 않는다.
        public ActionRecord Answer(string choiceId, IEnumerable<object> picked) =>
            Do(new ChoiceAnswer { choice = choiceId, picked = (picked ?? Enumerable.Empty<object>()).Where(p => p != null).Select(p => p as string ?? IdOf(p)).ToList() });

        public ActionRecord Answer(string choiceId, params object[] picked) => Answer(choiceId, (IEnumerable<object>)picked);

        public bool CancelChoice(string choiceId) => Session.Choices.Cancel(choiceId);

        // ── 비용 ───────────────────────────────────────────────
        // 비용 내기: 가로채기(CostIntent)를 거친 뒤 모자라면 GameFailure(FailureKeys.NotEnough) → 행동 실패, 바꾼 것 모두 취소. 행동 안에서 부른다.
        //   Game.Pay(GameIds.PlayerData.Run.Id, "battle.energy", 2, what: "energy.name");
        // what: 실패 글의 {what}에 들어갈 이름(텍스트 키면 번역된 글, 비우면 플레이어 데이터 › 필드). 없는 필드·숫자가 아닌 필드는 프로그래머 실수로 예외.
        public void Pay(string key, string path, double amount, string what = null)
        {
            var cost = Intercept(new CostIntent { key = key, path = path ?? "", amount = amount });
            if (cost.cancelled || cost.amount <= 0) return;
            var have = ReadNumber(cost.key, cost.path);
            if (have < cost.amount)
            {
                var name = string.IsNullOrEmpty(what) ? PathText(cost.key, cost.path) : Texts.Has(what) ? Texts.Get(what) : what;
                throw new GameFailure(FailureKeys.NotEnough,
                    TextArgs.From(("what", name), ("have", Format(have)), ("need", Format(cost.amount))),
                    "{what}이(가) 모자랍니다({have} / {need}).");
            }
            WriteNumber(cost.key, cost.path, have - cost.amount);
        }

        // 낼 수 있는지(지금 값 기준, 가로채기 전 — 화면 미리보기용). 실제 지불은 Pay가 가로채기까지 거쳐 다시 판정한다.
        public bool CanPay(string key, string path, double amount) => ReadNumber(key, path ?? "") >= amount;

        double ReadNumber(string key, string path)
        {
            if (string.IsNullOrEmpty(key) || !Session.Data.IsDefined(key)) throw new InvalidOperationException($"[MomoiProject] 비용: 없는 플레이어 데이터 '{key}'입니다.");
            if (string.IsNullOrEmpty(path)) return Session.Data.GetNumber(key);
            var type = FieldPaths.TypeAt(Session.Data.TypeOf(key), path, out var error);
            if (type == null) throw new InvalidOperationException($"[MomoiProject] 비용: '{key}'에 필드 '{path}'가 없습니다({error}).");
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (!(underlying.IsPrimitive && underlying != typeof(bool) && underlying != typeof(char)) && underlying != typeof(decimal))
                throw new InvalidOperationException($"[MomoiProject] 비용: '{key} › {path}'는 숫자 필드가 아닙니다({type.Name}).");
            var value = FieldPaths.Resolve(Session.Data.Get(key), path).FirstOrDefault();
            return value == null ? 0 : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        void WriteNumber(string key, string path, double value)
        {
            if (string.IsNullOrEmpty(path))
            {
                Session.Data.Set(key, value);
                return;
            }
            var cut = path.LastIndexOf('.');
            var parentPath = cut < 0 ? "" : path.Substring(0, cut);
            var member = cut < 0 ? path : path.Substring(cut + 1);
            Session.Data.Modify<object>(key, root =>
            {
                foreach (var parent in FieldPaths.Resolve(root, parentPath).ToList())
                {
                    if (parent == null) continue;
                    var access = FieldAccess.Find(parent.GetType(), member);
                    var type = FieldPaths.TypeAt(parent.GetType(), member, out _);
                    if (access == null || type == null) continue;
                    var underlying = Nullable.GetUnderlyingType(type) ?? type;
                    access.Set(parent, Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture));
                }
            });
        }

        string PathText(string key, string path) => string.IsNullOrEmpty(path) ? Session.Data.LabelOf(key) : $"{Session.Data.LabelOf(key)} › {path}";

        static string Format(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        // 새 개체의 id(소환 등). 접두어별 번호를 플레이어 데이터 저장소에 두므로 Undo·저장·멀티플레이에서도 같다.
        public string NewId(string prefix)
        {
            var key = "$id." + prefix;
            if (!Session.Data.IsDefined(key)) Session.Data.Define(key, DataZone.Meta, 0.0, $"{prefix} 번호");
            var next = Session.Data.GetNumber(key) + 1;
            Session.Data.Set(key, next);
            return prefix + (long)next;
        }

        // 데이터 키 목록(이름순). 정의가 늘 때만 다시 만든다.
        List<string> _dataKeys;
        int _dataKeyCount = -1;

        List<string> DataKeys()
        {
            var count = Session.Data.KeyCount;
            if (_dataKeys == null || count != _dataKeyCount)
            {
                _dataKeys = Session.Data.Keys.Where(Session.Data.IsData).OrderBy(k => k, StringComparer.Ordinal).ToList();
                _dataKeyCount = count;
            }
            return _dataKeys;
        }

        // ── id ────────────────────────────────────────────────
        // 개체로 찾으려면 클래스에 string id 필드(또는 속성)가 있어야 한다.

        static readonly Dictionary<Type, MemberInfo> IdMembers = new Dictionary<Type, MemberInfo>();

        static MemberInfo IdMember(Type type)
        {
            lock (IdMembers)
            {
                if (IdMembers.TryGetValue(type, out var member)) return member;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                member = (MemberInfo)type.GetField("id", flags) ?? type.GetField("Id", flags) ?? (MemberInfo)type.GetProperty("Id", flags) ?? type.GetProperty("id", flags);
                if (member is FieldInfo f && f.FieldType != typeof(string)) member = null;
                if (member is PropertyInfo p && p.PropertyType != typeof(string)) member = null;
                IdMembers[type] = member;
                return member;
            }
        }

        public static string IdOf(object item)
        {
            if (item == null) return null;
            var type = item.GetType();
            var member = IdMember(type);
            if (member != null) return FieldAccess.Find(type, member.Name)?.Get(item) as string;
            throw new InvalidOperationException($"[MomoiProject] {item.GetType().Name}에 string id 필드가 없어 개체로 찾거나 가리킬 수 없습니다. 'public string id;'를 더하세요.");
        }

        static void RequireId(Type type)
        {
            if (!type.IsAbstract && !type.IsInterface && IdMember(type) == null)
                throw new InvalidOperationException($"[MomoiProject] {type.Name}에 string id 필드가 없어 Find로 찾을 수 없습니다. 'public string id;'를 더하세요.");
        }

        // ── 모으기 ─────────────────────────────────────────────

        static readonly Dictionary<(Type, Type), bool> ContainCache = new Dictionary<(Type, Type), bool>();

        // holder 형식의 값 안에 target이 들어 있을 수 있는지(형식만 보고 판단, 결과는 기억).
        static bool CanContain(Type holder, Type target)
        {
            if (holder == null) return false;
            lock (ContainCache)
            {
                if (ContainCache.TryGetValue((holder, target), out var known)) return known;
                var result = CanContain(holder, target, new HashSet<Type>());
                ContainCache[(holder, target)] = result;
                return result;
            }
        }

        static bool CanContain(Type holder, Type target, HashSet<Type> seen)
        {
            if (holder == null || !seen.Add(holder)) return false;
            if (target.IsAssignableFrom(holder) || holder.IsAssignableFrom(target)) return true;
            var info = DataModel.Of(holder);
            switch (info.Kind)
            {
                case DataKind.List:
                case DataKind.Array:
                case DataKind.Collection:
                case DataKind.Dictionary:
                    return CanContain(info.ElementType, target, seen);
                case DataKind.Object:
                    return info.Fields.Any(f => CanContain(f.Type, target, seen));
            }
            return false;
        }

        static void Collect<T>(object value, List<T> into, int depth) where T : class
        {
            if (value == null || depth > 24 || value is string) return;
            if (value is T found) into.Add(found);
            var info = DataModel.Of(value.GetType());
            switch (info.Kind)
            {
                case DataKind.List:
                case DataKind.Array:
                case DataKind.Collection:
                case DataKind.Dictionary:
                    foreach (var item in FieldPaths.Items(value)) Collect(item, into, depth + 1);
                    return;
                case DataKind.Object:
                    foreach (var field in info.Fields) Collect(FieldAccess.Find(value.GetType(), field.Name)?.Get(value), into, depth + 1);
                    return;
            }
        }
    }


    // ── 조건: Check만 쓴다. 무엇을 읽는지는 시스템이 기록한다(CollectDependencies 불필요) ──
    [Serializable]
    public abstract class SimpleCondition : Condition
    {
        [NonSerialized] GameApi _game;
        protected GameApi Game => _game;
        // 트리거 안에서 검사 중이면 그 순간(이벤트·사용자·대상). 아니면 null. 예: Context?.User as Unit
        protected TriggerContext Context => _game?.Session.Triggers.Current;

        protected abstract bool Check();

        public override bool Evaluate(GameSession session)
        {
            _game = session.Game;
            try { return Check(); }
            finally { _game = null; }
        }

        public override void CollectDependencies(ICollection<string> stateKeys)
        {
        }

        public override bool AutoDependencies => true;

        public override string Describe(INameResolver names) => BlockNames.Of(GetType());
    }


    static class BlockNames
    {
        static readonly Dictionary<Type, string> Names = new Dictionary<Type, string>();

        // [BlockInfo]의 이름(형식마다 처음 한 번 찾아 둔다).
        public static string Of(Type type)
        {
            lock (Names)
            {
                if (Names.TryGetValue(type, out var name)) return name;
                name = type.GetCustomAttribute<BlockInfoAttribute>()?.DisplayName ?? type.Name;
                Names[type] = name;
                return name;
            }
        }
    }
}
