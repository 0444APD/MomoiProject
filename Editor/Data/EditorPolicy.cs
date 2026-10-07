using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GBible.Data;
using UnityEditor;
using UnityEngine;

namespace GBible.EditorTools
{
    // 에디터에서 무엇을 바꿀 수 있나(미도리 — GBible = 개발자의 읽기·밸런싱·테스트 도구).
    //   로직·DB 항목은 AI가 코드와 DB 반영 스크립트로 만든다. 개발자는 값(숫자·고르는 값·참조·데이터 목록의 줄)을 바꾼다.
    //   잠그는 것(구조): Block(트리거·조건·결과·효과·대상) 더하기·빼기·바꾸기·순서, 조건식 조립, 규칙 줄(TriggerRule) 더하기·빼기,
    //     기능 필드 붙이기·떼기, 로직 쪽 DB 항목 추가·삭제·ID 바꾸기, 키워드 만들기·지우기, 코드를 고치는 연결([Hide]·키워드 그룹).
    //   열린 것: 위 밖의 모든 값. 개발자가 만드는 종류([Database(DeveloperAuthored = true)] — 다이얼로그·스프라이트 규격 …)는 추가·삭제도.
    // 라이브러리를 고치거나 급할 때: 메뉴 'MomoiProject › 개발 › 구조 편집 열기'(이 컴퓨터에만, EditorPrefs).
    public static class EditorPolicy
    {
        const string AuthoringKey = "GBible.EditorPolicy.Authoring";
        const string MenuPath = "MomoiProject/개발/구조 편집 열기(라이브러리 개발용)";

        public const string LockedTip = "구조(블록·항목 추가·삭제)는 AI에게 요청하세요. 여기서는 값을 바꿉니다.";

        public static event Action Changed;

        public static bool Authoring
        {
            get => EditorPrefs.GetBool(AuthoringKey, false);
            set
            {
                if (value == Authoring) return;
                EditorPrefs.SetBool(AuthoringKey, value);
                Changed?.Invoke();
            }
        }

        // Block·규칙 줄·기능 필드의 모양을 바꿀 수 있나.
        public static bool CanEditStructure => Authoring;

        // 이 에셋 안의 구조를 바꿀 수 있나: 개발자가 만드는 종류([Database(DeveloperAuthored = true)] — 다이얼로그 그래프의 선택지·조건 등)는
        // 안의 Block·목록 줄도 개발자가 짠다. 그 밖은 CanEditStructure와 같다.
        public static bool CanEditStructureOf(UnityEngine.Object owner) =>
            Authoring || (owner is GameData data && (DataTypes.Find(data.GetType())?.DeveloperAuthored ?? false));

        public static bool CanEditStructureOf(SerializedProperty property) => CanEditStructureOf(property?.serializedObject?.targetObject);

        // 이 DB 종류의 항목을 추가·삭제하고 ID를 바꿀 수 있나.
        public static bool CanAddRemove(Type dataType) => Authoring || (dataType != null && (DataTypes.Find(dataType)?.DeveloperAuthored ?? false));
        public static bool CanAddRemove(GameData item) => item != null && CanAddRemove(item.GetType());

        static readonly Dictionary<Type, bool> StructuralCache = new Dictionary<Type, bool>();

        // 목록 요소가 구조(로직)인가: Block, 규칙 줄, Block이 든 형식. 아니면 값(데이터 목록의 줄은 개발자가 더하고 뺀다).
        public static bool IsStructural(Type type)
        {
            if (type == null) return false;
            if (StructuralCache.TryGetValue(type, out var known)) return known;
            var result = typeof(Trigger).IsAssignableFrom(type) || typeof(Condition).IsAssignableFrom(type) || typeof(Outcome).IsAssignableFrom(type)
                || typeof(Effect).IsAssignableFrom(type) || typeof(TargetSelector).IsAssignableFrom(type) || typeof(ValueFilter).IsAssignableFrom(type)
                || typeof(TriggerRule).IsAssignableFrom(type) || typeof(StatRule).IsAssignableFrom(type)
                || type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any(f => f.IsDefined(typeof(SerializeReference), true));
            StructuralCache[type] = result;
            return result;
        }

        [MenuItem(MenuPath, priority = 900)]
        static void Toggle()
        {
            Authoring = !Authoring;
            Debug.Log(Authoring
                ? "[MomoiProject] 구조 편집을 열었습니다(이 컴퓨터만). Block·항목 추가·삭제가 보입니다. 게임 프로젝트에서는 AI에게 맡기는 것이 기본입니다."
                : "[MomoiProject] 구조 편집을 닫았습니다. 값만 바꿀 수 있습니다.");
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>()) window.Repaint();
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleCheck()
        {
            Menu.SetChecked(MenuPath, Authoring);
            return true;
        }
    }
}
