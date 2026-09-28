using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameSystems.Data
{
    // 데이터베이스 항목의 공용 베이스. 이 클래스를 상속하면 코드 추가 없이 데이터베이스 창에 나타난다.
    // 제공 계약: 자동 ID(생성 후 변경 불가), 표시 이름, 태그, 검증 훅.
    public abstract class GameData : ScriptableObject
    {
        [SerializeField, HideInInspector] string id;
        [SerializeField, FieldLabel("이름")] string displayName;
        [SerializeField, FieldLabel("태그")] List<string> tags = new List<string>();

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public IReadOnlyList<string> Tags => tags;

        // 항목 고유의 검증. 데이터베이스 검증기가 저장·빌드 시점에 호출한다.
        public virtual void ValidateData(IValidationContext context)
        {
        }

#if UNITY_EDITOR
        // ID는 에디터 도구만 부여한다(세이브 호환을 위해 이후 변경하지 않는다).
        public void EditorAssignId(string newId) => id = newId;
        public void EditorSetDisplayName(string value) => displayName = value;
#endif
    }

    // 데이터베이스 창의 표시 정보(선택). 없으면 클래스 이름을 쓴다.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DatabaseAttribute : Attribute
    {
        public string DisplayName { get; }
        public string Group { get; set; }
        public int Order { get; set; }
        // ID 앞부분. 없으면 클래스 이름에서 만든다(예: ItemData → item).
        public string IdPrefix { get; set; }

        public DatabaseAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }

    // GameData를 상속하지만 데이터베이스 창에 보이지 않아야 할 때.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class HideInDatabaseAttribute : Attribute
    {
    }
}
