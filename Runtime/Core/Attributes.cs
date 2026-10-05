using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // 능력치(Attribute)와 Modifier: 캐릭터의 공격력·방어력 같은 숫자 필드에 붙는 보정.
    //
    // 능력치는 따로 선언하지 않는다. id가 있는 객체(유닛·카드 …)의 숫자 필드가 곧 기본값이다(public double attack;).
    // Modifier는 (대상 id, 필드 이름, 칸, 값, 출처) 한 줄이고, 플레이어 데이터에 저장된다(Undo·저장·멀티 그대로).
    // 최종값 = (기본값 + 플랫 합) × (1 + 퍼센트 합) × Π(1 + 독립 곱 하나하나)
    //   같은 칸은 더하고(합연산) 칸끼리는 곱한다 — 스탯을 10% 올리면 결과도 10% 오르고(가치 보존), 퍼센트를 많이 쌓아도 폭주하지 않는다.
    //   독립 곱(Multiply)은 드물게 쓰는 강한 효과용(하나하나 따로 곱해진다).
    // 출처(source)로 한꺼번에 뗀다: 장비를 벗으면 그 장비 ID 출처의 Modifier를 모두 뗀다.
    //   permanent면 메타 구역(프로필)에 저장한다(판이 끝나도 남는 영구 강화). 아니면 세션 구역(이번 판).
    public enum ModifierKind
    {
        // 기본값에 더한다(+30).
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("플랫(+값)")]
#endif
        Flat,
        // 같은 칸끼리 더한 뒤 한 번 곱한다(0.2 = +20%).
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("퍼센트(0.2 = +20%)")]
#endif
        Percent,
        // 하나하나 따로 곱한다(0.5 = ×1.5). 드물게 쓰는 강한 효과용.
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.InspectorName("독립 곱(0.5 = ×1.5)")]
#endif
        Multiply,
    }

    [Serializable]
    public sealed class Modifier
    {
        public string id;
        public string target;      // 대상 객체의 id
        public string attribute;   // 숫자 필드 이름(nameof(Fighter.attack))
        public ModifierKind kind;
        public double value;
        public string source;      // 붙인 쪽(장비·상태 효과·유물 ID …). 같은 출처를 한꺼번에 뗄 때 쓴다
    }

    [Serializable]
    public sealed class ModifierList
    {
        public List<Modifier> items = new List<Modifier>();
    }

    public sealed class AttributeSystem
    {
        public const string SessionKey = "$modifiers";
        public const string MetaKey = "$modifiers.meta";

        readonly GameSession _session;
        // (대상 id, 필드) → Modifier들. 저장소의 Modifier 목록이 바뀌면 다시 만든다.
        Dictionary<(string, string), List<Modifier>> _index;

        internal AttributeSystem(GameSession session)
        {
            _session = session;
            // 행동이 중단·미리보기로 되돌아가면 색인을 다시 만든다(미리보기는 변경 알림을 보내지 않는다).
            session.Actions.TransactionAborted += _ => _index = null;
            session.Data.Changed += key =>
            {
                if (key == SessionKey || key == MetaKey) _index = null;
            };
        }

        // 최종값. entity는 객체 또는 id. 숫자 필드가 없으면 0.
        // 패시브의 '가진 동안 능력치 보정'도 더한다(저장하지 않고 읽을 때 계산 — 얻고 잃는 순간을 따로 다루지 않는다).
        public double Get(object entity, string attribute)
        {
            var target = Resolve(entity, out var id);
            var value = BaseOf(target, attribute);
            var stored = StoredOf(id, attribute);
            var passives = _session.Passives;
            if (passives == null || !passives.HasModifiers(attribute)) return Apply(value, stored);
            return Apply(value, stored.Concat(passives.ModifiersFor(target, id, attribute)));
        }

        // 기본값에 Modifier들을 적용한 값(같은 칸 합, 칸끼리 곱).
        public static double Apply(double baseValue, IEnumerable<Modifier> modifiers)
        {
            double flat = 0, percent = 0, multiply = 1;
            foreach (var m in modifiers)
            {
                switch (m.kind)
                {
                    case ModifierKind.Flat: flat += m.value; break;
                    case ModifierKind.Percent: percent += m.value; break;
                    case ModifierKind.Multiply: multiply *= 1 + m.value; break;
                }
            }
            return (baseValue + flat) * (1 + percent) * multiply;
        }

        // Modifier를 붙이고 그 id를 돌려준다. 행동 안에서 부른다(Undo·저장·멀티에 기록된다).
        public string Add(object entity, string attribute, ModifierKind kind, double value, string source, bool permanent = false)
        {
            if (string.IsNullOrEmpty(attribute)) throw new ArgumentException("[MomoiProject] 능력치 이름이 비어 있습니다.", nameof(attribute));
            var target = Resolve(entity, out var id);
            if (target != null && !IsNumericField(target.GetType(), attribute))
                throw new InvalidOperationException($"[MomoiProject] {target.GetType().Name}에 숫자 필드 '{attribute}'가 없어 Modifier를 붙일 수 없습니다.");
            var modifier = new Modifier { id = _session.Game.NewId("$mod"), target = id, attribute = attribute, kind = kind, value = value, source = source ?? "" };
            var key = Ensure(permanent);
            _session.Data.Modify<ModifierList>(key, list => list.items.Add(modifier));
            _index = null;   // 같은 행동 안에서 바로 읽어도 보이게(저장소의 Changed는 Sync 때 온다)
            return modifier.id;
        }

        // 그 출처의 Modifier를 뗀다(entity를 주면 그 대상 것만). 뗀 개수.
        public int RemoveBySource(string source, object entity = null)
        {
            string id = null;
            if (entity != null) Resolve(entity, out id);
            return RemoveWhere(m => m.source == source && (id == null || m.target == id));
        }

        public bool Remove(string modifierId) => RemoveWhere(m => m.id == modifierId) > 0;

        // 대상의 Modifier들(필드를 주면 그 필드 것만). 패시브의 '가진 동안 능력치 보정'도 같은 모양으로 들어 있다(source = 패시브 ID).
        public IReadOnlyList<Modifier> Of(object entity, string attribute = null)
        {
            string id;
            object target = entity;
            if (entity is string text) id = text;
            else Resolve(entity, out id);
            var stored = StoredOf(id, attribute);
            var passives = _session.Passives;
            if (passives == null || !passives.HasModifiers(attribute) || string.IsNullOrEmpty(id)) return stored;
            if (entity is string) target = _session.Game.Find(id);
            var extra = passives.ModifiersFor(target, id, attribute).ToList();
            return extra.Count == 0 ? stored : stored.Concat(extra).ToList();
        }

        IReadOnlyList<Modifier> StoredOf(string id, string attribute)
        {
            if (string.IsNullOrEmpty(id)) return Array.Empty<Modifier>();
            BuildIndex();
            if (attribute != null) return _index.TryGetValue((id, attribute), out var list) ? list : (IReadOnlyList<Modifier>)Array.Empty<Modifier>();
            return _index.Where(p => p.Key.Item1 == id).SelectMany(p => p.Value).ToList();
        }

        internal bool HasSessionModifiers => _session.Data.IsDefined(SessionKey) && (_session.Data.Get<ModifierList>(SessionKey)?.items.Count ?? 0) > 0;

        // 대상이 데이터에서 사라진 이번 판 Modifier를 지운다(바깥 행동이 끝나기 직전). 영구 Modifier는 두지 않는다(대상이 다음 판에 다시 생긴다).
        internal void PruneMissing()
        {
            var list = _session.Data.IsDefined(SessionKey) ? _session.Data.Get<ModifierList>(SessionKey)?.items : null;
            if (list == null || list.Count == 0) return;
            var gone = new HashSet<string>(list.Select(m => m.target).Distinct().Where(id => !string.IsNullOrEmpty(id) && _session.Game.Find(id) == null), StringComparer.Ordinal);
            if (gone.Count == 0) return;
            _session.Data.Modify<ModifierList>(SessionKey, l => l.items.RemoveAll(m => gone.Contains(m.target)));
            _index = null;
        }

        int RemoveWhere(Func<Modifier, bool> match)
        {
            var removed = 0;
            foreach (var key in new[] { SessionKey, MetaKey })
            {
                if (!Exists(key)) continue;
                var list = _session.Data.Get<ModifierList>(key);
                if (list == null || !list.items.Any(match)) continue;
                _session.Data.Modify<ModifierList>(key, l => removed += l.items.RemoveAll(m => match(m)));
                _index = null;
            }
            return removed;
        }

        // 등록돼 있거나, 불러온 저장의 값이 기다리고 있으면(지금 등록한다) 참.
        bool Exists(string key)
        {
            if (_session.Data.IsDefined(key)) return true;
            if (!_session.Data.IsPending(key)) return false;
            Ensure(key == MetaKey);
            return true;
        }

        string Ensure(bool permanent)
        {
            var key = permanent ? MetaKey : SessionKey;
            if (!_session.Data.IsDefined(key))
                _session.Data.Define(key, permanent ? DataZone.Meta : DataZone.Session, new ModifierList(), permanent ? "영구 Modifier" : "Modifier");
            return key;
        }

        void BuildIndex()
        {
            if (_index != null) return;
            var index = new Dictionary<(string, string), List<Modifier>>();
            foreach (var key in new[] { SessionKey, MetaKey })
            {
                if (!Exists(key)) continue;
                var list = _session.Data.Get<ModifierList>(key);
                if (list == null) continue;
                foreach (var m in list.items)
                {
                    if (m == null || string.IsNullOrEmpty(m.target)) continue;
                    if (!index.TryGetValue((m.target, m.attribute), out var bucket)) index[(m.target, m.attribute)] = bucket = new List<Modifier>();
                    bucket.Add(m);
                }
            }
            _index = index;
        }

        object Resolve(object entity, out string id)
        {
            if (entity is string text)
            {
                id = text;
                return _session.Game.Find(text);
            }
            id = entity == null ? null : GameApi.IdOf(entity);
            return entity;
        }

        static double BaseOf(object target, string attribute)
        {
            if (target == null || string.IsNullOrEmpty(attribute)) return 0;
            var value = FieldAccess.Read(target, attribute);
            return value == null ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        static bool IsNumericField(Type type, string attribute)
        {
            var member = FieldAccess.Find(type, attribute);
            if (member == null) return false;
            var info = DataModel.Of(type).Fields.FirstOrDefault(f => f.Name == attribute);
            return info != null && TypeResolver.IsNumeric(Nullable.GetUnderlyingType(info.Type) ?? info.Type);
        }
    }
}
