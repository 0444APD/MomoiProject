using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GBible
{
    // 패시브: "가지고 있으면 발동"(유물·특성·패시브 스킬·장비 효과·축복). 정의(DB › 패시브)는 늘 등록되어 있고,
    // 가진 목록(플레이어 데이터의 ID 목록·딕셔너리)에 그 ID가 있는 쪽마다 트리거를 검사해 결과를 낸다.
    //   가진 목록 run › relics           → 가진 쪽 = run(판 하나에 하나)
    //   가진 목록 run › hero.perks       → 가진 쪽 = hero(사용자 자신 = 주인공)
    //   가진 목록 run › party[].traits   → 가진 쪽 = 그 목록을 가진 동료마다
    // 몇 개 가졌나(레벨): 목록이면 그 ID가 든 횟수, 딕셔너리면 그 키의 숫자(없으면 0) → Game.Owned(ID, 가진 쪽).
    // 결과의 컨텍스트: User = 가진 쪽, Owner = 패시브 ID. 가진 쪽 필드를 고르면 이벤트·의도의 그 필드가 가진 쪽 id일 때만(가시 갑옷: 내가 맞을 때만).
    // 상태 효과와 다른 점: 지속·중첩·만료가 없고, 가진 것은 게임의 데이터(유물 목록)에 그대로 있다.
    public sealed class PassiveDefinition
    {
        public string Id;
        public string DisplayName;
        public string OwnerKey;          // 가진 목록이 든 플레이어 데이터
        public string OwnerPath;         // 그 안의 목록·딕셔너리 경로(앞마디에 []를 쓰면 가진 쪽이 여럿)
        public Trigger When;
        public string HolderField;       // 이벤트·의도의 이 필드가 가진 쪽 id일 때만
        public readonly List<Outcome> Outcomes = new List<Outcome>();
    }

    public sealed class PassiveSystem
    {
        readonly GameSession _session;
        readonly Dictionary<string, PassiveDefinition> _definitions = new Dictionary<string, PassiveDefinition>(StringComparer.Ordinal);

        internal PassiveSystem(GameSession session)
        {
            _session = session;
        }

        public IEnumerable<PassiveDefinition> Definitions => _definitions.Values;
        public PassiveDefinition Find(string id) => id != null && _definitions.TryGetValue(id, out var d) ? d : null;

        public void Define(PassiveDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Id)) return;
            _definitions[definition.Id] = definition;
            var owner = "passive:" + definition.Id;
            _session.Triggers.Unregister(owner);
            if (definition.When != null)
                _session.Triggers.Register(owner, definition.When, repeat: true, fire: moment => Fire(definition, moment), holders: moment => HoldersFor(definition, moment));
        }

        // 몇 개 가졌나. holder를 비우면 모든 가진 쪽의 합.
        public int Owned(string id, object holder = null)
        {
            var definition = Find(id);
            if (definition == null) return 0;
            var total = 0;
            foreach (var (parent, collection) in Collections(definition))
                if (holder == null || ReferenceEquals(parent, holder) || SameId(parent, holder)) total += CountIn(collection, id);
            return total;
        }

        IEnumerable<object> HoldersFor(PassiveDefinition definition, TriggerContext moment)
        {
            foreach (var (parent, collection) in Collections(definition).ToList())
            {
                if (CountIn(collection, definition.Id) <= 0) continue;
                if (!string.IsNullOrEmpty(definition.HolderField))
                {
                    var holderId = parent != null && GameApi.HasId(parent.GetType()) ? GameApi.IdOf(parent) : null;
                    if (holderId == null || FieldText(moment.Event, definition.HolderField) != holderId) continue;
                }
                yield return parent;
            }
        }

        void Fire(PassiveDefinition definition, TriggerContext moment)
        {
            if (definition.Outcomes.Count == 0) return;
            var holder = moment.User;
            var context = new TriggerContext
            {
                Session = _session, Owner = definition.Id, Event = moment.Event, IsIntercept = moment.IsIntercept,
                User = holder, Picked = holder == null ? Array.Empty<object>() : new[] { holder },
            };
            _session.Triggers.RunOutcomes(definition.Outcomes, context, "Passive");
        }

        // 가진 쪽(목록을 품은 객체)과 그 목록.
        IEnumerable<(object parent, object collection)> Collections(PassiveDefinition definition)
        {
            if (string.IsNullOrEmpty(definition.OwnerKey) || !_session.Data.IsDefined(definition.OwnerKey)) yield break;
            var root = _session.Data.Get(definition.OwnerKey);
            FieldPaths.SplitLast(definition.OwnerPath ?? "", out var parentPath, out var last);
            if (string.IsNullOrEmpty(last))
            {
                yield return (root, root);
                yield break;
            }
            foreach (var parent in FieldPaths.Resolve(root, parentPath).ToList())
                if (parent != null) yield return (parent, FieldPaths.Resolve(parent, last).FirstOrDefault());
        }

        static int CountIn(object collection, string id)
        {
            switch (collection)
            {
                case null: return 0;
                case string text: return text == id ? 1 : 0;
                case IDictionary map:
                    foreach (DictionaryEntry entry in map)
                    {
                        if (!string.Equals(Convert.ToString(entry.Key, CultureInfo.InvariantCulture), id, StringComparison.Ordinal)) continue;
                        return entry.Value is IConvertible && !(entry.Value is string) ? (int)Math.Round(Convert.ToDouble(entry.Value, CultureInfo.InvariantCulture)) : 1;
                    }
                    return 0;
                case IEnumerable list:
                    var count = 0;
                    foreach (var item in list)
                        if (item is string s ? s == id : item != null && GameApi.HasId(item.GetType()) && GameApi.IdOf(item) == id) count++;
                    return count;
                default: return 0;
            }
        }

        static bool SameId(object a, object b) =>
            a != null && b != null && GameApi.HasId(a.GetType()) && (b is string text ? GameApi.IdOf(a) == text : GameApi.HasId(b.GetType()) && GameApi.IdOf(a) == GameApi.IdOf(b));

        static string FieldText(object evt, string field)
        {
            if (evt == null || string.IsNullOrEmpty(field)) return null;
            var value = FieldAccess.Read(evt, field);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
