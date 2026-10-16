using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace GBible
{
    // 플레이어 데이터 필드 하나에 무엇을 주는지는 필드 형식이 정한다(보상처럼 '주는' 구문의 공통 규칙).
    public enum GrantKind
    {
        None,     // 줄 수 없는 형식(에셋 참조, 요소가 또 컬렉션인 컬렉션 등)
        Number,   // 숫자: 증감만(다시 정하기는 하지 않는다)
        Value,    // 글자·참거짓·enum: 값 정하기(키워드가 연결된 글자는 목록에서)
        Instance, // 클래스: 인스턴스 중 하나(사본이 들어간다). 여기서 새로 만들지 않는다.
        List,     // 리스트·배열·컬렉션: 요소 넣기. 요소마다 다시 숫자·값·인스턴스 규칙.
        Dictionary, // 딕셔너리: 키 + 값 넣기. 키는 글자·숫자·enum·참거짓, 값은 숫자(증감: 없던 키는 0에서)·값·인스턴스(있던 키는 덮어쓴다).
    }

    // 게임 숫자를 정수로 맞추는 한 가지 규칙: 반올림은 0에서 먼 쪽(2.5 → 3, −2.5 → −3). 정수 필드에 넣는 값(주기·의도 고치기·이벤트 쏘기·
    // 필드 바꾸기·비용)·이벤트 횟수 지속·중첩·레벨·곡선의 반올림이 모두 이것을 쓴다. Math.Round의 기본과 Convert.ChangeType(정수로)은
    // 짝수 쪽(2.5 → 2)이라, 곳마다 섞어 쓰면 같은 값이 곳마다 다르게 반올림된다.
    public static class GameNumbers
    {
        public static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

        public static int RoundToInt(double value) => (int)Round(value);

        // 정수 형식인가(부호 있는·없는 정수 모두, nullable이면 그 안쪽).
        public static bool IsWhole(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
                   type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte);
        }

        // 숫자를 그 숫자 형식의 값으로(정수 형식이면 Round로 맞춘 뒤).
        // 그 형식에 담기지 않으면(int 칸에 30억·NaN) 자르지 않고 던진다 — 조용히 자르면 계산 실수가 숨는다. 글은 무엇을 고칠지 알린다.
        public static object ToType(double number, Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            var value = IsWhole(type) ? Round(number) : number;
            try { return Convert.ChangeType(value, type, CultureInfo.InvariantCulture); }
            catch (OverflowException)
            {
                var shown = double.IsNaN(value) ? "숫자가 아닌 값(NaN)" : value.ToString("R", CultureInfo.InvariantCulture);
                throw new OverflowException($"[MomoiProject] {shown}은(는) {type.Name} 칸에 담기지 않습니다 — 더 큰 형식(long·double·BigNumber)의 필드를 쓰거나 계산을 고치세요.");
            }
        }
    }

    public static class PlayerDataGrants
    {
        // 필드 형식 → 주는 방법. 리스트·배열·그 밖의 컬렉션은 모두 요소 넣기(배열은 늘린 새 배열로 바꾼다). 딕셔너리는 키 + 값 넣기.
        public static GrantKind KindOf(Type type)
        {
            if (type == null) return GrantKind.None;
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(bool) || type == typeof(string) || type.IsEnum) return GrantKind.Value;
            if (TypeResolver.IsNumeric(type) || type == typeof(BigNumber)) return GrantKind.Number;
            var info = DataModel.Of(type);
            if (info.Kind == DataKind.List || info.Kind == DataKind.Collection || info.Kind == DataKind.Array)
            {
                var element = ElementKind(type);
                return element == GrantKind.None || element == GrantKind.List ? GrantKind.None : GrantKind.List;
            }
            if (info.Kind == DataKind.Dictionary)
            {
                var keyKind = KindOf(info.KeyType);
                var valueKind = ElementKind(type);
                var keyOk = keyKind == GrantKind.Value || keyKind == GrantKind.Number;
                return keyOk && valueKind != GrantKind.None && valueKind != GrantKind.List && valueKind != GrantKind.Dictionary ? GrantKind.Dictionary : GrantKind.None;
            }
            if (info.Kind == DataKind.Object && !type.IsValueType) return GrantKind.Instance;
            return GrantKind.None;
        }

        // 딕셔너리의 키 형식.
        public static Type KeyTypeOf(Type dictionary) => dictionary == null ? null : DataModel.Of(dictionary).KeyType;

        // 컬렉션 요소(딕셔너리는 값)의 주는 방법(리스트의 숫자 요소는 증감이 아니라 그 숫자를 넣는다).
        public static GrantKind ElementKind(Type collection) => KindOf(FieldPaths.ElementTypeOf(collection));

        // 값 글자를 형식에 맞게 읽는다(숫자는 소수점, 참거짓은 true/false·참/거짓, enum은 이름).
        public static bool TryParse(Type type, string text, out object value, out string error)
        {
            value = null;
            error = null;
            text = text ?? "";
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(string))
            {
                value = text;
                return true;
            }
            if (type == typeof(bool))
            {
                if (!TryParseBool(text, out var flag))
                {
                    error = text.Trim().Length == 0 ? "참/거짓이 선택되지 않았습니다." : $"'{text}'는 참/거짓이 아닙니다.";
                    return false;
                }
                value = flag;
                return true;
            }
            if (type.IsEnum)
            {
                if (!Enum.IsDefined(type, text))
                {
                    error = text.Length == 0 ? "값이 선택되지 않았습니다." : $"'{text}'는 {type.Name}에 없는 값입니다.";
                    return false;
                }
                value = Enum.Parse(type, text);
                return true;
            }
            if (TypeResolver.IsNumeric(type))
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    error = text.Length == 0 ? "숫자가 비어 있습니다." : $"'{text}'는 숫자가 아닙니다.";
                    return false;
                }
                // 형식에 담기지 않는 숫자(int에 30억)도 읽지 못한 값이다 — 던지지 않는다(DB 검증·보상 실행이 멈추지 않게).
                try { value = ToNumber(number, type); }
                catch (OverflowException)
                {
                    error = $"'{text}'는 {DataModel.FriendlyName(type)}에 담을 수 없는 크기입니다.";
                    return false;
                }
                return true;
            }
            error = $"{DataModel.FriendlyName(type)}은(는) 글자로 적을 수 없습니다.";
            return false;
        }

        // 참거짓 글자: true·false(대소문자 무관)·참·거짓·1·0, 앞뒤 빈칸은 무시. 그 밖의 글자는 읽지 못한다(거짓으로 보지 않는다).
        // 틀을 펼친 값(값 틀 {event:…}의 True)도 같은 규칙으로 읽는다 — 의도 고치기·이벤트 쏘기·플레이어 데이터에 주기 공통.
        public static bool TryParseBool(string text, out bool value)
        {
            var t = (text ?? "").Trim();
            value = string.Equals(t, "true", StringComparison.OrdinalIgnoreCase) || t == "참" || t == "1";
            return value || string.Equals(t, "false", StringComparison.OrdinalIgnoreCase) || t == "거짓" || t == "0";
        }

        // 숫자를 형식에 맞춘다(정수형이면 반올림 — GameNumbers의 규칙).
        public static object ToNumber(double number, Type type) => GameNumbers.ToType(number, type);

        // 보상으로 고를 수 있는 필드 목록: 값 자체(단순 값·컬렉션·클래스) + 클래스 안의 필드(클래스는 안쪽으로 들어가고, 컬렉션 안쪽은 들어가지 않는다).
        public static IEnumerable<string> Paths(Type root, int depth = 3)
        {
            if (root == null) yield break;
            if (KindOf(root) != GrantKind.None) yield return "";
            foreach (var path in Members(root, "", depth)) yield return path;
        }

        static IEnumerable<string> Members(Type type, string prefix, int depth)
        {
            if (depth <= 0 || DataModel.Of(type).Kind != DataKind.Object) yield break;
            foreach (var member in FieldPaths.MembersOf(type))
            {
                if (!(member.Info is FieldInfo)) continue;
                var path = prefix.Length == 0 ? member.Name : prefix + "." + member.Name;
                if (KindOf(member.Type) != GrantKind.None) yield return path;
                if (!member.IsCollection)
                    foreach (var inner in Members(member.Type, path, depth - 1)) yield return inner;
            }
        }

        public static string PathLabel(string key, string path) =>
            (string.IsNullOrEmpty(key) ? "(플레이어 데이터 없음)" : key) + (string.IsNullOrEmpty(path) ? "" : " › " + FieldFilter.PathLabel(path));
    }

    // 뽑은·고른 원본 ID들을 플레이어 데이터에 넣는 공통 규칙(가중치 표에서 주기·고르기): 목록이면 요소로, 딕셔너리면 그 키의 개수 +1, 숫자 하나면 개수만큼 +,
    // 값 하나(글자·enum·클래스)면 그 ID로(차례로 넣어 여럿이면 마지막 것, 클래스면 그 인스턴스의 사본).
    public static class GrantIds
    {
        public static void Into(GameSession session, string key, string path, IReadOnlyList<string> ids)
        {
            if (ids == null || ids.Count == 0 || string.IsNullOrEmpty(key)) return;
            var target = session.Data.IsDefined(key) ? FieldPaths.TypeAt(session.Data.TypeOf(key), path ?? "", out _) : null;
            var kind = PlayerDataGrants.KindOf(target);
            if (kind == GrantKind.Dictionary)
            {
                var counts = ids.GroupBy(id => id, StringComparer.Ordinal).ToList();
                new PlayerDataReward { key = key, path = path ?? "", keys = counts.Select(g => g.Key).ToList(), items = counts.Select(g => g.Count().ToString(CultureInfo.InvariantCulture)).ToList() }.Grant(session);
            }
            else if (kind == GrantKind.Number) new PlayerDataReward { key = key, path = path ?? "", amount = ids.Count }.Grant(session);
            // 값 하나는 요소 넣기(items)가 아니다 — 그대로 주면 빈 value로 정해져 글자 필드가 지워진다.
            else if (kind == GrantKind.Value || kind == GrantKind.Instance) new PlayerDataReward { key = key, path = path ?? "", value = ids[ids.Count - 1] }.Grant(session);
            else new PlayerDataReward { key = key, path = path ?? "", items = ids.ToList() }.Grant(session);
        }
    }

    // 보상: 플레이어 데이터 필드 하나에 준다. 필드 형식에 따라
    //   숫자 → amount만큼 증감 / 글자·참거짓·enum → value로 정하기 / 클래스 → value(인스턴스 ID)의 사본으로 정하기 /
    //   컬렉션 → items의 요소마다(숫자·값·인스턴스 ID) 넣기 / 딕셔너리 → keys[i]에 items[i](숫자면 증감, 아니면 값·인스턴스로 덮어쓰기).
    // 인스턴스는 DB › 인스턴스에서 만든 인스턴스만 고른다(보상 안에서 새로 만들거나 고치지 않는다). 런타임은 session.Instances로 찾는다.
    [Serializable]
    [BlockInfo("플레이어 데이터에 주기", Category = "플레이어 데이터",
        Description = "플레이어 데이터 필드 하나에 줍니다. 숫자는 증감, 글자·enum은 값, 클래스는 인스턴스, 리스트는 요소(숫자·값·인스턴스)를 넣습니다.")]
    public sealed class PlayerDataReward : Reward
    {
        [PlayerDataRef, FieldPathRoot(FieldPathRootKind.PlayerDataKey), FieldLabel("플레이어 데이터")] public string key;
        [FieldPath(EmptyLabel = "값 전체"), FieldLabel("필드(비우면 값 전체)")] public string path = "";
        [FieldLabel("증감")] public double amount;
        [FieldLabel("값(클래스면 인스턴스 ID)")] public string value = "";
        [FieldLabel("넣을 요소(숫자·값·인스턴스 ID)")] public List<string> items = new List<string>();
        // 딕셔너리: items[i]를 넣을 키(같은 순서).
        [FieldLabel("딕셔너리 키")] public List<string> keys = new List<string>();

        public override OutcomeKinds Kind => OutcomeKinds.PlayerData;
        public override RewardEffect Effect => RewardEffect.Reversible;

        public override void Grant(GameSession session)
        {
            var store = session.Data;
            if (string.IsNullOrEmpty(key) || !store.IsDefined(key))
            {
                Warn(session, $"없는 플레이어 데이터 '{key}'");
                return;
            }
            var target = FieldPaths.TypeAt(store.TypeOf(key), path, out var error);
            if (target == null)
            {
                Warn(session, error);
                return;
            }
            var kind = PlayerDataGrants.KindOf(target);
            if (string.IsNullOrEmpty(path))
            {
                switch (kind)
                {
                    case GrantKind.List:
                        if (target.IsArray) store.Set(key, AddItems(session, store.Get(key), target));
                        else store.Modify<object>(key, root => AddItems(session, root, target));
                        return;
                    case GrantKind.Dictionary:
                        store.Modify<object>(key, root => AddEntries(session, root, target));
                        return;
                    case GrantKind.Instance:
                        var made = Make(session, target, value, GrantKind.Instance);
                        if (made != null) store.Set(key, made);
                        return;
                    default:
                        var next = NewValue(session, store.Get(key), target, kind);
                        if (next != null) store.Set(key, next);
                        return;
                }
            }

            FieldPaths.SplitLast(path, out var parentPath, out var member);
            store.Modify<object>(key, root =>
            {
                foreach (var parent in FieldPaths.Resolve(root, parentPath).ToList())
                {
                    if (parent == null) continue;
                    // 딕셔너리의 키 하나(stock[potion]): 숫자는 증감, 값·인스턴스는 그 키에 넣는다.
                    if (FieldPaths.IsSelect(member))
                    {
                        if (kind == GrantKind.List || kind == GrantKind.Dictionary) Warn(session, $"'{path}': 키 하나에는 목록을 넣을 수 없습니다.");
                        else FieldPaths.Assign(parent, member, (current, type) => kind == GrantKind.Instance ? Make(session, type, value, GrantKind.Instance) : NewValue(session, current, type, kind));
                        continue;
                    }
                    if (parent.GetType().IsValueType)
                    {
                        Warn(session, $"'{path}'의 부모가 구조체라 바꿀 수 없습니다(클래스로 바꾸세요).");
                        return;
                    }
                    var current = FieldPaths.Read(parent, member);
                    if (kind == GrantKind.Dictionary)
                    {
                        if (current == null)
                        {
                            current = DataModel.Create(target);
                            Write(parent, member, current);
                        }
                        AddEntries(session, current, target);
                    }
                    else if (kind == GrantKind.List)
                    {
                        if (current == null) current = target.IsArray ? Array.CreateInstance(target.GetElementType(), 0) : DataModel.Create(target);
                        var grown = AddItems(session, current, target);
                        if (!ReferenceEquals(grown, FieldPaths.Read(parent, member))) Write(parent, member, grown);
                    }
                    else if (kind == GrantKind.Instance)
                    {
                        var made = Make(session, target, value, GrantKind.Instance);
                        if (made != null) Write(parent, member, made);
                    }
                    else
                    {
                        var next = NewValue(session, current, target, kind);
                        if (next != null) Write(parent, member, next);
                    }
                }
            });
        }

        // 숫자는 지금 값 + 증감, 글자·enum·참거짓은 value.
        object NewValue(GameSession session, object current, Type type, GrantKind kind)
        {
            if (kind == GrantKind.Number)
            {
                // 값을 적었으면 그 값으로 정한다(증감은 보지 않는다 — "천장 89로" · "스태미나 0으로"). 문장도 'X를 N(으)로'로 보인다.
                if (!string.IsNullOrWhiteSpace(value))
                {
                    if ((Nullable.GetUnderlyingType(type) ?? type) == typeof(BigNumber))
                    {
                        if (BigNumber.TryParse(value, out var bigSet)) return bigSet;
                    }
                    else if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var set)) return PlayerDataGrants.ToNumber(set, type);
                    WarnStatic(session, $"'{value}'는 숫자가 아니라 값으로 정하지 않았습니다");
                    return current;
                }
                // 큰 수 칸은 큰 수로 더한다(1e308을 넘어도).
                if ((Nullable.GetUnderlyingType(type) ?? type) == typeof(BigNumber)) return (current is BigNumber big ? big : BigNumber.Zero) + amount;
                var before = current == null ? 0 : Convert.ToDouble(current, CultureInfo.InvariantCulture);
                return PlayerDataGrants.ToNumber(before + amount, type);
            }
            return Make(session, type, value, kind);
        }

        // 넣을 값 하나: 숫자·값은 글자를 읽고, 인스턴스는 원본의 사본(여러 번 줘도 서로 다른 인스턴스).
        static object Make(GameSession session, Type type, string text, GrantKind kind)
        {
            if (kind == GrantKind.Instance)
            {
                var made = session.CopyInstance(text);
                if (made == null)
                {
                    WarnStatic(session, string.IsNullOrEmpty(text) ? "인스턴스가 선택되지 않았습니다" : $"인스턴스 '{text}'을(를) 찾을 수 없습니다");
                    return null;
                }
                if (!type.IsInstanceOfType(made))
                {
                    WarnStatic(session, $"인스턴스 '{text}'의 형식({made.GetType().Name})이 {DataModel.FriendlyName(type)}과(와) 다릅니다");
                    return null;
                }
                return made;
            }
            if (PlayerDataGrants.TryParse(type, text, out var value, out var error)) return value;
            WarnStatic(session, error);
            return null;
        }

        // 요소들을 넣은 컬렉션을 돌려준다. 리스트·컬렉션은 그 자리에 넣고, 배열은 크기가 고정이라 늘린 새 배열을 만든다.
        object AddItems(GameSession session, object collection, Type type)
        {
            var element = FieldPaths.ElementTypeOf(type);
            var kind = PlayerDataGrants.ElementKind(type);
            var made = items.Select(text => Make(session, element, text, kind == GrantKind.Number ? GrantKind.Value : kind))
                            .Where(item => item != null).ToList();
            if (type.IsArray)
            {
                var old = collection as Array ?? Array.CreateInstance(element, 0);
                var grown = Array.CreateInstance(element, old.Length + made.Count);
                Array.Copy(old, grown, old.Length);
                for (int i = 0; i < made.Count; i++) grown.SetValue(made[i], old.Length + i);
                return grown;
            }
            if (collection == null || made.Count == 0) return collection;
            if (collection is IList list)
            {
                foreach (var item in made) list.Add(item);
                return collection;
            }
            // 그 밖의 컬렉션(HashSet·Queue·Stack …): 데이터 모델이 형식마다 한 번 정해 둔 꺼내기·다시 채우기로 차례 끝에 붙인다
            // (넣을 때마다 Add를 찾지 않는다 — Queue·Stack에는 Add가 없어 아무것도 넣지 못했다).
            var info = DataModel.Of(collection.GetType());
            if (info.ReadItems == null || info.WriteItems == null) return collection;
            var all = info.ReadItems(collection);
            all.AddRange(made);
            info.WriteItems(collection, all);
            return collection;
        }

        // 딕셔너리에 키 + 값을 넣는다. 숫자 값은 있던 값에 더하고(없던 키는 0에서), 그 밖의 값·인스턴스는 덮어쓴다.
        void AddEntries(GameSession session, object collection, Type type)
        {
            if (!(collection is IDictionary map)) return;
            var keyType = PlayerDataGrants.KeyTypeOf(type);
            var valueType = FieldPaths.ElementTypeOf(type);
            var kind = PlayerDataGrants.ElementKind(type);
            for (int i = 0; i < keys.Count; i++)
            {
                if (!PlayerDataGrants.TryParse(keyType, keys[i], out var mapKey, out var keyError))
                {
                    WarnStatic(session, $"{i + 1}번째 키: {keyError}");
                    continue;
                }
                var text = i < items.Count ? items[i] : "";
                object next;
                if (kind == GrantKind.Number)
                {
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
                    {
                        WarnStatic(session, $"{i + 1}번째 값: '{text}'는 숫자가 아닙니다");
                        continue;
                    }
                    if ((Nullable.GetUnderlyingType(valueType) ?? valueType) == typeof(BigNumber))
                        next = (map.Contains(mapKey) && map[mapKey] is BigNumber big ? big : BigNumber.Zero) + delta;
                    else
                    {
                        var before = map.Contains(mapKey) && map[mapKey] != null ? Convert.ToDouble(map[mapKey], CultureInfo.InvariantCulture) : 0;
                        next = PlayerDataGrants.ToNumber(before + delta, valueType);
                    }
                }
                else next = Make(session, valueType, text, kind);
                if (next != null) map[mapKey] = next;
            }
        }

        // 생성 코드(FieldAccess)로 쓴다(리플렉션 없음).
        static void Write(object parent, string member, object value) => FieldAccess.Find(parent.GetType(), member)?.Set(parent, value);

        void Warn(GameSession session, string problem) =>
            session.Journal.Append(JournalKind.Warning, "Reward", $"플레이어 데이터에 주기({PlayerDataGrants.PathLabel(key, path)}): {problem}");

        static void WarnStatic(GameSession session, string problem) =>
            session.Journal.Append(JournalKind.Warning, "Reward", $"플레이어 데이터에 주기: {problem}");

        static string Amount(double amount) => amount.ToString("0.##", CultureInfo.InvariantCulture);

        static string Or(string text, string empty) => string.IsNullOrEmpty(text) ? empty : text;

        // 형식을 모르는 곳(로그·요약)에서는 채워진 칸으로 무엇을 주는지 짐작한다.
        public override string Describe(INameResolver names)
        {
            var target = Or(key, "(플레이어 데이터 없음)") + (string.IsNullOrEmpty(path) ? "" : "." + path);
            if (keys.Count > 0) return $"{target}[{string.Join(", ", keys)}] <- {string.Join(", ", items)}";
            if (items.Count > 0) return $"{target}.add({string.Join(", ", items)})";
            if (!string.IsNullOrEmpty(value)) return $"{target} = {value}";
            return amount < 0 ? $"{target} -= {Amount(-amount)}" : $"{target} += {Amount(amount)}";
        }

        public override string Natural(INameResolver names)
        {
            var label = PlayerDataGrants.PathLabel(key, path);
            if (keys.Count > 0) return $"{label}에 {string.Join(", ", keys.Select((k, i) => $"{Or(k, "(빈 키)")}: {(i < items.Count ? Or(items[i], "(빈칸)") : "(빈칸)")}"))} 넣기";
            if (items.Count > 0) return $"{label}에 {string.Join(", ", items.Select(i => Or(i, "(빈칸)")))} 넣기";
            if (!string.IsNullOrEmpty(value)) return $"{Josa.EulReul(label)} {value}(으)로";
            return amount < 0 ? $"{label} {Amount(-amount)} 감소" : $"{label} {Amount(amount)} 증가";
        }

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(key)) context.Error("보상을 줄 플레이어 데이터가 선택되지 않았습니다.");
        }

        // 플레이어 데이터의 값 형식을 알 때의 검사(DB 검증기가 부른다). 인스턴스가 있는지는 DB 검증기가 따로 본다.
        public void Validate(Type root, List<string> errors)
        {
            if (root == null) return;
            var target = FieldPaths.TypeAt(root, path, out var error);
            if (target == null)
            {
                errors.Add(error);
                return;
            }
            switch (PlayerDataGrants.KindOf(target))
            {
                case GrantKind.None:
                    errors.Add($"{DataModel.FriendlyName(target)}은(는) 보상으로 줄 수 없는 형식입니다.");
                    return;
                case GrantKind.Number:
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        // 값으로 정하기
                        var isBig = (Nullable.GetUnderlyingType(target) ?? target) == typeof(BigNumber);
                        if (isBig ? !BigNumber.TryParse(value, out _) : !double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                            errors.Add($"'{value}'는 숫자가 아닙니다(값으로 정하기 — 비우면 증감).");
                        return;
                    }
                    if (amount == 0) errors.Add("증감이 0입니다(값으로 정하려면 '값'에 숫자를).");
                    return;
                case GrantKind.Value:
                    if (!PlayerDataGrants.TryParse(target, value, out _, out var parseError)) errors.Add(parseError);
                    return;
                case GrantKind.Instance:
                    if (string.IsNullOrEmpty(value)) errors.Add("인스턴스가 선택되지 않았습니다.");
                    return;
                case GrantKind.List:
                    if (items.Count == 0) errors.Add("넣을 요소가 없습니다. '+'로 요소를 더하세요.");
                    var element = FieldPaths.ElementTypeOf(target);
                    var kind = PlayerDataGrants.ElementKind(target);
                    for (int i = 0; i < items.Count; i++)
                    {
                        if (kind == GrantKind.Instance)
                        {
                            if (string.IsNullOrEmpty(items[i])) errors.Add($"{i + 1}번째 요소의 인스턴스가 선택되지 않았습니다.");
                        }
                        else if (!PlayerDataGrants.TryParse(element, items[i], out _, out var itemError)) errors.Add($"{i + 1}번째 요소: {itemError}");
                    }
                    return;
                case GrantKind.Dictionary:
                    if (keys.Count == 0) errors.Add("넣을 키가 없습니다. '+'로 키와 값을 더하세요.");
                    var keyType = PlayerDataGrants.KeyTypeOf(target);
                    var valueType = FieldPaths.ElementTypeOf(target);
                    var valueKind = PlayerDataGrants.ElementKind(target);
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        if (!PlayerDataGrants.TryParse(keyType, keys[i], out _, out var keyError)) errors.Add($"{i + 1}번째 키: {keyError}");
                        else if (!seen.Add(keys[i])) errors.Add($"키 '{keys[i]}'가 두 번 들어 있습니다.");
                        var text = i < items.Count ? items[i] : "";
                        if (valueKind == GrantKind.Instance)
                        {
                            if (string.IsNullOrEmpty(text)) errors.Add($"{i + 1}번째 값의 인스턴스가 선택되지 않았습니다.");
                        }
                        else if (!PlayerDataGrants.TryParse(valueKind == GrantKind.Number ? typeof(double) : valueType, text, out _, out var valueError))
                            errors.Add($"{i + 1}번째 값: {valueError}");
                    }
                    return;
            }
        }

        // 인스턴스 ID를 쓰는 칸들(검증기가 인스턴스가 있는지·형식이 맞는지 본다).
        public IEnumerable<(string id, Type type)> InstanceRefs(Type root)
        {
            var target = root == null ? null : FieldPaths.TypeAt(root, path, out _);
            if (target == null) yield break;
            var kind = PlayerDataGrants.KindOf(target);
            if (kind == GrantKind.Instance && !string.IsNullOrEmpty(value)) yield return (value, target);
            if ((kind == GrantKind.List || kind == GrantKind.Dictionary) && PlayerDataGrants.ElementKind(target) == GrantKind.Instance)
                foreach (var id in items.Where(i => !string.IsNullOrEmpty(i))) yield return (id, FieldPaths.ElementTypeOf(target));
        }
    }
}
