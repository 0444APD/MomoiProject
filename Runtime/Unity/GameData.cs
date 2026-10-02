using System;
using System.Collections.Generic;
using UnityEngine;

namespace GBible.Data
{
    // 데이터베이스 항목의 공용 베이스. 이 클래스를 상속하면 코드 추가 없이 데이터베이스 창에 나타난다.
    // 제공 계약: ID(도감에서 짓고, 바꾸면 모든 참조·저장·생성 코드가 따라간다), 검증 훅. 분류가 필요하면 종류마다 필드를 두고 키워드 그룹을 연결한다.
    // 항목은 ID로만 식별한다(따로 표시 이름을 두지 않는다. 플레이어에게 보일 글자는 텍스트 시트·번역에서 ID로 찾는다).
    public abstract class GameData : ScriptableObject
    {
        [SerializeField, HideInInspector] string id;
        // 도감 목록에서 넣어 둔 폴더(예: "전투/보스"). 정리용일 뿐 게임 동작에는 쓰지 않는다. 비어 있으면 맨 위.
        [SerializeField, HideInInspector] string folder = "";

        public string Id => id;
        public string Folder => folder ?? "";
        // 화면·로그에 쓰는 이름 = ID(ID가 아직 없으면 에셋 이름).
        public string DisplayName => string.IsNullOrEmpty(id) ? name : id;

        // 항목 고유의 검증. 데이터베이스 검증기가 저장·빌드 시점에 호출한다.
        public virtual void ValidateData(IValidationContext context)
        {
        }

#if UNITY_EDITOR
        // ID는 에디터 도구만 부여·변경한다(변경 시 참조 갱신·저장 호환 별칭은 EditorDatabase.Rename이 처리).
        public void EditorAssignId(string newId) => id = newId;
        public void EditorAssignFolder(string path) => folder = path ?? "";
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
        // 사이드바에 따로 나오지 않는다(다른 종류의 전용 화면에서 함께 다룬다. 예: 키워드 그룹은 키워드 탭에서).
        public bool Hidden { get; set; }
        // 시트·카드 대신 쓸 전용 화면의 키(에디터가 같은 키의 화면을 찾는다). 예: "keywords"
        public string Page { get; set; }

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
    // InstanceField: 형식을 고르면 그 형식의 새 인스턴스를 넣을 [SerializeReference] 필드 이름(인스턴스 등).
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

namespace GBible.Data
{
    // 도감에서 순서를 끌어 바꾸는 항목(예: 키워드 그룹, 스프라이트 규격 그룹).
    public interface IOrderedData
    {
        int Order { get; set; }
    }

    // 그룹에 속하는 항목(예: 키워드 → 키워드 그룹, 스프라이트 규격 → 규격 그룹). 도감의 그룹 탭이 끌어 옮긴다.
    public interface IGroupedData : IOrderedData
    {
        string Group { get; set; }
    }

    // 같은 종류 안에서 부모를 두는 그룹(예: 키워드 그룹 안의 하위 그룹). Parent는 부모 그룹의 ID(비어 있으면 맨 위).
    public interface INestedData
    {
        string Parent { get; set; }
    }
}

namespace GBible.Data
{
    // 예전 저장 형식을 지금 형식으로 옮기는 항목(도감이 열릴 때 한 번, 바뀌었으면 에셋을 저장한다).
    public interface ILegacyData
    {
        bool MigrateLegacy();
    }

    // Game.Use(ID, 사용자, 대상들)로 쓸 수 있는 항목(능력, 사용 기능이 붙은 아이템 …). 런타임이 세션에 등록한다.
    public interface IUsableData
    {
        // 쓸 수 없는 항목이면 null.
        AbilityDefinition ToUsable();
    }
}
