using UnityEngine;

namespace GBible.Data
{
    // DB › 저장 변환: 출시 뒤 저장 구조를 바꿨을 때, 예전 저장 파일을 지금 구조로 옮기는 한 단계(코드 options.Migrations와 같다).
    // 버전·키·경로만 받는 변환(필드 옮김·키 이름 바꿈·키 삭제)을 표로 적는다. 값을 계산해 바꾸는 변환(Migrations.Add)만 코드로 쓴다.
    // 게임이 켜질 때 버전 순서대로 등록되고(같은 버전이면 DB 목록 순서), 그다음 코드의 Configure가 더 등록할 수 있다.
    // 저장 버전은 등록된 변환의 가장 높은 버전을 저절로 따라간다.
    [Database("저장 변환", Group = "개발", Order = 20, IdPrefix = "migration")]
    public sealed class SaveMigrationData : GameData
    {
        [FieldLabel("버전"), Tooltip("이 버전으로 올릴 때 적용한다(버전 - 1 → 버전). 2부터. 처음 출시는 버전 1이다.")]
        public int version = 2;
        [FieldLabel("종류")] public SaveMigrationKind kind = SaveMigrationKind.MoveField;
        [FieldLabel("키"), Tooltip("저장 파일 안의 플레이어 데이터 키(예전 이름). 예: run")]
        public string key = "";
        [FieldLabel("예전 경로"), ShowIf(nameof(kind), SaveMigrationKind.MoveField), Tooltip("값 안의 필드 경로. 리스트의 모든 요소는 items[].power처럼 [].")]
        public string fromPath = "";
        [FieldLabel("새 경로"), ShowIf(nameof(kind), SaveMigrationKind.MoveField)]
        public string toPath = "";
        [FieldLabel("새 키"), ShowIf(nameof(kind), SaveMigrationKind.RenameKey)]
        public string newKey = "";

        public void Register(SaveMigrations migrations)
        {
            switch (kind)
            {
                case SaveMigrationKind.MoveField: migrations.MoveField(version, key, fromPath, toPath); break;
                case SaveMigrationKind.RenameKey: migrations.RenameKey(version, key, newKey); break;
                case SaveMigrationKind.RemoveKey: migrations.RemoveKey(version, key); break;
            }
        }

        public override void ValidateData(IValidationContext context)
        {
            if (version < 2) context.Error("버전은 2부터입니다(버전 1이 처음 출시).");
            if (string.IsNullOrWhiteSpace(key)) context.Error("키를 적으세요.");
            if (kind == SaveMigrationKind.MoveField && (string.IsNullOrWhiteSpace(fromPath) || string.IsNullOrWhiteSpace(toPath))) context.Error("예전 경로와 새 경로를 적으세요.");
            if (kind == SaveMigrationKind.RenameKey && string.IsNullOrWhiteSpace(newKey)) context.Error("새 키를 적으세요.");
        }
    }

    public enum SaveMigrationKind
    {
        [InspectorName("필드 옮김")] MoveField,
        [InspectorName("키 이름 바꿈")] RenameKey,
        [InspectorName("키 삭제")] RemoveKey,
    }
}
