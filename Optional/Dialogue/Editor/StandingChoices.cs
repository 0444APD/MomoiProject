using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameSystems.Data;
using GameSystems.Dialogue;
using GameSystems.EditorTools.UI;
using UnityEditor;

namespace GameSystems.EditorTools.Graph
{
    // '스탠딩 ID' 칸의 목록: 같은 노드의 프로필 칸이 정한 스탠딩 프로필의 스탠딩들(프로필이 비어 있으면 = 현재 화자, 모든 프로필을 묶어 보인다).
    sealed class StandingChoices : IInlineChoiceProvider
    {
        public string Placeholder => "스탠딩 검색";

        public bool Handles(FieldInfo field) => field.IsDefined(typeof(StandingRefAttribute), false);

        public IEnumerable<GsDropdownItem> Items(SerializedProperty property, FieldInfo field, Action<string> set)
        {
            var attribute = field.GetCustomAttribute<StandingRefAttribute>();
            var path = property.propertyPath;
            var dot = path.LastIndexOf('.');
            var sibling = property.serializedObject.FindProperty((dot < 0 ? "" : path.Substring(0, dot + 1)) + attribute.CharacterField);
            var profileId = sibling != null && sibling.propertyType == SerializedPropertyType.String ? sibling.stringValue : "";
            var profiles = EditorDatabase.Instance.OfType<StandingProfileData>()
                .Where(p => string.IsNullOrEmpty(profileId) || p.Id == profileId)
                .OrderBy(p => p.Id, StringComparer.Ordinal)
                .ToList();
            var grouped = profiles.Count > 1;
            var current = property.stringValue;
            yield return new GsDropdownItem("(비움)", () => set("")) { Detail = "바꾸지 않음 / 기본 스탠딩", Selected = string.IsNullOrEmpty(current) };
            var any = false;
            foreach (var profile in profiles)
            foreach (var standing in profile.expressions.Where(e => !string.IsNullOrEmpty(e.id)))
            {
                any = true;
                var id = standing.id;
                yield return new GsDropdownItem(id, () => set(id))
                {
                    Caption = standing.sprite != null ? standing.sprite.name : "스프라이트 없음",
                    Group = grouped ? profile.Id : null,
                    Selected = current == id,
                };
            }
            if (!any)
                yield return new GsDropdownItem(profiles.Count == 0 ? "스탠딩 프로필이 없습니다 — 도감에서 먼저 만드세요" : "스탠딩 목록이 비었습니다 — 스탠딩 프로필에서 추가하세요") { Disabled = true };
        }
    }
}
