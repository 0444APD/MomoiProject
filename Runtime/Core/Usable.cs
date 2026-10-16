using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 사용 기능: 클래스에 필드로 붙이면 그 클래스의 인스턴스를 Game.Use(인스턴스 ID, 사용자, 대상들)로 쓸 수 있다.
    // 액티브 효과·로그·도전과제와 같은 트리거·결과 칸(트리거 = 사용 + 원하면 AND로 사용 조건).
    //   [Serializable] public class Item
    //   {
    //       public string id;
    //       [SerializeReference] public Usable usable;   // null이면 쓸 수 없다(기능이 붙어 있지 않음)
    //   }
    //   if (item.usable is { } use) …
    // 개수 줄이기 같은 뒤처리는 게임 코드가 한다(예: inventory.TryRemove).
    [Serializable]
    [BlockInfo("사용", Category = "기능", Description = "쓰는 순간의 결과(대상에게 효과·플레이어 데이터에 주기 …). 게임 코드는 Game.Use(인스턴스 ID, 사용자, 대상들).")]
    public sealed class Usable
    {
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("트리거"), TriggerSlot(TriggerKinds.Use | TriggerKinds.Condition)] public Trigger when = new UseTrigger();
#if UNITY_2020_1_OR_NEWER
        [UnityEngine.SerializeReference]
#endif
        [FieldLabel("결과"), OutcomeSlot(OutcomeKinds.Targeted | OutcomeKinds.PlayerData | OutcomeKinds.Coded)]
        public List<Outcome> then = new List<Outcome>();
        [FieldLabel("쓸 수 없을 때 글(텍스트 키 또는 글, 비우면 기본 글)")] public string failText = "";

        // Game.Use로 쓰는 액티브 효과 정의(ID = 인스턴스 ID).
        public AbilityDefinition ToDefinition(string id, string displayName = null)
        {
            var definition = new AbilityDefinition { Id = id, DisplayName = displayName ?? id, When = when, FailText = failText };
            definition.Outcomes.AddRange(then.Where(o => o != null));
            return definition;
        }

        // 값(인스턴스 원본)에 붙은 사용 기능(형식의 Usable 필드 — 없거나 비었으면 null). 런타임은 게임이 켜질 때, 테스트는 DefineInstance에서 부른다
        // (형식마다 한 번 필드를 찾아 둔다 — 실행 중에는 부르지 않는다).
        public static Usable Of(object value)
        {
            if (value == null) return null;
            var type = value.GetType();
            System.Reflection.FieldInfo field;
            lock (Fields)
            {
                if (!Fields.TryGetValue(type, out field))
                {
                    for (var t = type; t != null && field == null; t = t.BaseType)
                        field = t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)
                            .FirstOrDefault(f => f.FieldType == typeof(Usable));
                    Fields[type] = field;
                }
            }
            return field?.GetValue(value) as Usable;
        }

        static readonly Dictionary<Type, System.Reflection.FieldInfo> Fields = new Dictionary<Type, System.Reflection.FieldInfo>();
    }
}
