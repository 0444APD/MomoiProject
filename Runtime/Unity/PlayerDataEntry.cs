using System;
using System.Globalization;
using UnityEngine;

namespace GBible.Data
{
    // 플레이어 데이터: 저장소에 담을 플레이어 데이터 하나. 데이터베이스 창에서 ID(=키)와 값 형식을 정하면
    // 런타임이 시작할 때 자동으로 등록하고, 코드에서는 생성된 GameIds.PlayerData.XXX로 타입까지 맞춰 쓴다.
    [Database("플레이어 데이터", Group = "선언", Order = 4, IdPrefix = "playerData")]
    [CreateAssetMenu(menuName = "MomoiProject/플레이어 데이터", fileName = "NewPlayerData")]
    public sealed class PlayerDataEntry : GameData
    {
        [FieldLabel("구역")] public DataZone zone = DataZone.Session;
        [FieldLabel("값 형식"), DataTypeRef] public string valueType = typeof(double).FullName;
        // 숫자·참거짓·글자·enum·날짜 같은 단순 값만. 클래스·컬렉션은 그 클래스의 초기값(필드 초기화·생성자)을 쓴다.
        [FieldLabel("기본값")] public string defaultValue = "";

        public Type ValueType => TypeResolver.Find(valueType);

        // 저장소에 등록한다(런타임 시작 시 자동 호출).
        public void DefineIn(PlayerDataStore store)
        {
            var type = ValueType ?? throw new InvalidOperationException($"[MomoiProject] 플레이어 데이터 '{Id}'의 값 형식 '{valueType}'을 찾을 수 없습니다.");
            if (!TryParseDefault(type, out var value, out var error)) throw new InvalidOperationException($"[MomoiProject] 플레이어 데이터 '{Id}': {error}");
            store.DefineOfType(Id, zone, type, value, DisplayName);
        }

        public bool TryParseDefault(Type type, out object value, out string error)
        {
            value = null;
            error = null;
            var text = defaultValue ?? "";
            var kind = DataModel.Of(type).Kind;
            if (kind != DataKind.Scalar && kind != DataKind.Enum) return true;
            if (text.Length == 0)
            {
                value = type == typeof(string) ? "" : type.IsValueType ? Activator.CreateInstance(type) : null;
                return true;
            }
            try
            {
                if (type == typeof(string)) value = text;
                else if (type == typeof(bool)) value = text == "true" || text == "예" || text == "1";
                else if (type.IsEnum) value = Enum.Parse(type, text);
                else if (TypeResolver.IsNumeric(type)) value = Convert.ChangeType(double.Parse(text, CultureInfo.InvariantCulture), type, CultureInfo.InvariantCulture);
                else value = DataModel.FromTree(text, type, null, null);
                return true;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is OverflowException || e is InvalidCastException)
            {
                error = $"기본값 '{text}'을(를) {DataModel.FriendlyName(type)}(으)로 읽을 수 없습니다.";
                return false;
            }
        }

        public override void ValidateData(IValidationContext context)
        {
            var type = ValueType;
            if (type == null)
            {
                context.Error(string.IsNullOrEmpty(valueType) ? "값 형식이 선택되지 않았습니다." : $"값 형식 '{valueType}'을 찾을 수 없습니다(클래스 이름이 바뀌었는지 확인).");
                return;
            }
            foreach (var problem in DataModel.Validate(type)) context.Error("담을 수 없는 필드: " + problem);
            if (!TryParseDefault(type, out _, out var error)) context.Error(error);
            var kind = DataModel.Of(type).Kind;
            if (kind != DataKind.Scalar && kind != DataKind.Enum && !string.IsNullOrEmpty(defaultValue))
                context.Warning("클래스·컬렉션 형식은 기본값 글자를 쓰지 않습니다(클래스의 초기값을 씁니다).");
            if (kind == DataKind.Object && !type.IsValueType && !Mirror.Supports(type, out var reason))
                context.Warning("비교 코드로 다룰 수 없어 예전 방식(리플렉션, 더 느림)으로 비교합니다: " + reason);
        }
    }
}
