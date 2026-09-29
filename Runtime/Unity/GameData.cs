using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameSystems.Data
{
    // 데이터베이스 항목의 공용 베이스. 이 클래스를 상속하면 코드 추가 없이 데이터베이스 창에 나타난다.
    // 제공 계약: ID(데이터베이스 창에서 짓고, 바꾸면 모든 참조·저장·생성 코드가 따라간다), 표시 이름, 태그, 검증 훅.
    public abstract class GameData : ScriptableObject
    {
        [SerializeField, HideInInspector] string id;
        [SerializeField, FieldLabel("이름")] string displayName;
        [SerializeField, FieldLabel("태그"), DataRef(typeof(TagData))] List<string> tags = new List<string>();

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public IReadOnlyList<string> Tags => tags;

        // 항목 고유의 검증. 데이터베이스 검증기가 저장·빌드 시점에 호출한다.
        public virtual void ValidateData(IValidationContext context)
        {
        }

#if UNITY_EDITOR
        // ID는 에디터 도구만 부여·변경한다(변경 시 참조 갱신·저장 호환 별칭은 EditorDatabase.Rename이 처리).
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

    // 문자열 필드가 어떤 데이터베이스 항목의 ID인지 표시한다. 에디터는 직접 입력 대신 그 타입 항목만 목록으로 보여 주고,
    // 검증기는 없는 ID를 찾아내며, ID 이름을 바꾸면 이 필드들의 값도 함께 바뀐다. List<string>에도 쓸 수 있다.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class DataRefAttribute : Attribute
    {
        public Type DataType { get; }

        public DataRefAttribute(Type dataType)
        {
            DataType = dataType;
        }
    }

    // 문자열 필드가 저장소에 담을 값 형식(타입 전체 이름)임을 표시한다. 에디터는 담을 수 있는 형식 목록을 띄운다.
    // InstanceField: 형식을 고르면 그 형식의 새 인스턴스를 넣을 [SerializeReference] 필드 이름(템플릿 등).
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class DataTypeRefAttribute : Attribute
    {
        public string InstanceField { get; set; }
    }

    // GameData를 상속하지만 데이터베이스 창에 보이지 않아야 할 때.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class HideInDatabaseAttribute : Attribute
    {
    }
}
