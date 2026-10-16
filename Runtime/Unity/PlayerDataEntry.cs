using System;
using System.Globalization;
using UnityEngine;

namespace GBible.Data
{
    // 플레이어 데이터: 저장소에 담을 플레이어 데이터 하나. DB 창에서 ID(=키)와 값 형식을 정하면
    // 런타임이 시작할 때 자동으로 등록하고, 코드에서는 생성된 GameIds.PlayerData.XXX로 타입까지 맞춰 쓴다.
    [Database("플레이어 데이터", Group = "요소", Order = 4, IdPrefix = "playerData")]
    [CreateAssetMenu(menuName = "MomoiProject/플레이어 데이터", fileName = "NewPlayerData")]
    public sealed class PlayerDataEntry : GameData
    {
        [FieldLabel("구역")] public DataZone zone = DataZone.Session;
        [FieldLabel("값 형식"), DataTypeRef] public string valueType = typeof(double).FullName;
        // 숫자·참거짓·글자·enum·날짜 같은 단순 값만. 클래스·컬렉션은 그 클래스의 초기값(필드 초기화·생성자)을 쓴다.
        [FieldLabel("기본값")] public string defaultValue = "";
        [FieldLabel("플레이어마다"), Tooltip("멀티: 플레이어마다 따로 둔다(실제 키 'ID:번호'). 게임 코드 · DB Block이 이 ID를 읽고 쓰면 지금 행동을 한 플레이어의 것. 싱글은 이 화면의 플레이어 하나")] public bool perPlayer;
        [FieldLabel("그 사람에게만 보임"), ShowIf(nameof(perPlayer)), Tooltip("켜면 그 플레이어에게만 보낸다(손패 · 숨긴 편성). 끄면 모두에게")] public bool ownerOnly;
        [FieldLabel("서버만(그 사람에게도 숨김)"), ShowIf(nameof(perPlayer)), Tooltip("켜면 아무에게도 보내지 않는다 — 서버 규칙만 읽는다(덱 순서 · 뽑을 카드). '그 사람에게만 보임'보다 먼저")] public bool serverOnly;
        [FieldLabel("방이 끝나도 서버에 남김"), ShowIf(nameof(perPlayer)), Tooltip("멀티의 사람마다 남는 값(재화 · 해금 · 로스터 · 컬렉션 — 멀티의 데이터는 서버 것): 서버의 그 사람 계정에 둔다. 매칭큐는 방이 열릴 때 읽고 매치가 끝나면 쓴다(취소된 매치는 쓰지 않음, 계정 방은 행동마다), 사설 서버(방장 · 화면 없는 서버)는 들어올 때 읽고 나갈 때 · 몇 초마다 · 닫을 때 쓴다. 판 저장 · 새 판에는 들지 않는다. 클라이언트가 고칠 수 없다")] public bool onAccount;

        public Type ValueType => TypeResolver.Find(valueType);

        // 저장소에 등록한다(런타임 시작 시 자동 호출).
        public void DefineIn(PlayerDataStore store)
        {
            var type = ValueType ?? throw new InvalidOperationException($"[MomoiProject] 플레이어 데이터 '{Id}'의 값 형식 '{valueType}'을 찾을 수 없습니다.");
            if (!TryParseDefault(type, out var value, out var error)) throw new InvalidOperationException($"[MomoiProject] 플레이어 데이터 '{Id}': {error}");
            if (perPlayer)
            {
                store.DefinePerPlayer(Id, zone, type, value ?? (DataModel.Of(type).Kind == DataKind.Scalar || DataModel.Of(type).Kind == DataKind.Enum ? null : DataModel.Create(type)), DisplayName, ownerOnly, onAccount, serverOnly);
                return;
            }
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
                else if (TypeResolver.IsNumeric(type)) value = GameNumbers.ToType(double.Parse(text, CultureInfo.InvariantCulture), type);
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
            if (perPlayer && zone != DataZone.Session)
                context.Error("'플레이어마다'는 판(Session) 구역만 됩니다 — 사람마다 남는 값은 '방이 끝나도 서버에 남김'으로(멀티의 데이터는 서버 것, 기기에는 설정만).");
            var kind = DataModel.Of(type).Kind;
            if (kind != DataKind.Scalar && kind != DataKind.Enum && !string.IsNullOrEmpty(defaultValue))
                context.Warning("클래스·컬렉션 형식은 기본값 글자를 쓰지 않습니다(클래스의 초기값을 씁니다).");
            if (kind == DataKind.Object && !type.IsValueType && !Mirror.Supports(type, out var reason))
                context.Warning("비교 코드로 다룰 수 없어 예전 방식(리플렉션, 더 느림)으로 비교합니다: " + reason);
        }
    }
}
