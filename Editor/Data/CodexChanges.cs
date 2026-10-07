using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace GBible.EditorTools
{
    // DB에서 한 모든 변경의 한 창구. 변경은 바로 쓰지 않고 모아 두었다가 한 번에 저장한다:
    //   · DB 항목(추가·수정·삭제·ID·순서), 필드 연결(키워드 그룹·스프라이트 규격 그룹·필수) → CodexStaging
    //   · 클래스 파일 위치 기억 → ClassRegistry (클래스·키워드 코드는 DB가 쓰지 않는다 — AI가 코드에서)
    //   · 다른 탭의 폴더(정리용) → CodexFolders, 항목이 든 폴더는 항목 자신(GameData.Folder) → CodexStaging
    // 저장하는 때: Ctrl+S(DB·상세 창에서), 사이드바의 저장 버튼, 그리고 창을 닫거나 플레이·종료하기 전의 확인.
    // 탭을 옮기는 것으로는 저장하지 않는다. 저장 전까지 에셋은 바뀌지 않는다.
    // 예외(누르는 즉시): 스프라이트 베이크, 프리팹 컴포넌트 수정, 선택 기능 켜고 끄기.
    public static class CodexChanges
    {
        public static bool HasChanges => EditorDatabase.Staging.HasChanges || ClassRegistry.IsDirty || CodexFolders.IsDirty;

        public static int Count => Rows().Count;

        // 저장 안 된 변경을 한 줄씩(확인 창·사이드바용).
        public static List<FieldChange> Rows()
        {
            var rows = new List<FieldChange>();
            foreach (var change in EditorDatabase.Staging.Changes())
            {
                var kind = CodexStaging.KindText(change.Kind);
                if (change.Kind == PendingKind.Modified)
                    rows.AddRange(change.Fields.Select(f => new FieldChange
                    {
                        Path = f.Path, Label = $"{kind} · {change.Label} › {f.Label}", Old = f.Old, New = f.New, OldObject = f.OldObject, NewObject = f.NewObject,
                    }));
                else
                    rows.Add(new FieldChange
                    {
                        Label = $"{kind} · {change.Label}",
                        Old = change.Kind == PendingKind.Added ? "(없음)" : change.Item.DisplayName,
                        New = change.Kind == PendingKind.Added ? change.Item.DisplayName : "(삭제)",
                    });
            }
            if (ClassRegistry.IsDirty) rows.Add(new FieldChange { Label = "클래스 설정", Old = "", New = "클래스 파일 위치 기억이 바뀜" });
            if (CodexFolders.IsDirty) rows.Add(new FieldChange { Label = "DB 폴더", Old = "", New = "폴더 만들기·옮기기·지우기" });
            return rows;
        }

        // 모아 둔 변경을 모두 저장한다. 돌려주는 값: 저장한 변경 수.
        public static int Save()
        {
            if (!HasChanges)
            {
                // 바뀐 것은 없지만 원본과 같은 작업본만 남았으면(같은 값으로 Work만 한 DB 반영 스크립트 등) 치우고 '적용했다'로 친다 — 돌려 둔 스크립트가 기록된다.
                if (!EditorDatabase.Staging.IsEmpty) EditorDatabase.Staging.Commit();
                return 0;
            }
            var items = EditorDatabase.Staging.Commit();
            var registry = ClassRegistry.IsDirty;
            if (ClassRegistry.IsDirty) ClassRegistry.Commit();
            var folders = CodexFolders.IsDirty;
            CodexFolders.Commit();
            var parts = new List<string>();
            if (items > 0) parts.Add($"DB 항목 {items}건");
            if (registry) parts.Add("클래스 설정");
            if (folders) parts.Add("DB 폴더");
            // 적용이 막혔거나(겹치는 ID 등 — 까닭은 바로 앞 오류) 일부가 실패하면 변경이 남는다: '변경 없음'으로 적지 않는다.
            var left = EditorDatabase.Staging.HasChanges;
            if (left) Debug.LogWarning("[MomoiProject] 저장하지 못한 DB 변경이 남아 있습니다" + (parts.Count > 0 ? " (저장한 것: " + string.Join(", ", parts) + ")" : "") + " — Console의 오류를 보고 고친 뒤 다시 저장(Ctrl+S)하세요.");
            else Debug.Log("[MomoiProject] 저장했습니다: " + (parts.Count > 0 ? string.Join(", ", parts) : "변경 없음"));
            return items + (registry ? 1 : 0) + (folders ? 1 : 0);
        }

        public static void Discard()
        {
            EditorDatabase.Staging.Discard();
            ClassRegistry.Discard();
            CodexFolders.Discard();
        }

        // Ctrl+S: DB·상세 창에 포커스가 있을 때만 이 저장이 된다(다른 창에서는 Unity의 저장 그대로).
        [Shortcut("MomoiProject/DB 변경 저장 (DB 창)", typeof(DatabaseWindow), KeyCode.S, ShortcutModifiers.Action)]
        static void SaveFromCodex(ShortcutArguments arguments) => Save();

        [Shortcut("MomoiProject/DB 변경 저장 (상세 창)", typeof(DetailWindow), KeyCode.S, ShortcutModifiers.Action)]
        static void SaveFromDetail(ShortcutArguments arguments) => Save();

        [Shortcut("MomoiProject/DB 변경 저장 (그룹 창)", typeof(CodexGroupWindow), KeyCode.S, ShortcutModifiers.Action)]
        static void SaveFromGroup(ShortcutArguments arguments) => Save();
    }
}
