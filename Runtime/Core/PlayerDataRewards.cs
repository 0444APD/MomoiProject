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

    public static class PlayerDataGrants
    {
        // 필드 형식 → 주는 방법. 리스트·배열·그 밖의 컬렉션은 모두 요소 넣기(배열은 늘린 새 배열로 바꾼다). 딕셔너리는 키 + 값 넣기.
        public static GrantKind KindOf(Type type)
        {
            if (type == null) return GrantKind.None;
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(bool) || type == typeof(string) || type.IsEnum) return GrantKind.Value;
            if (TypeResolver.IsNumeric(type)) return GrantKind.Number;
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
                var t = text.Trim();
                if (t == "true" || t == "True" || t == "참" || t == "1") value = true;
                else if (t == "false" || t == "False" || t == "거짓" || t == "0") value = false;
                else
                {
                    error = t.Length == 0 ? "참/거짓이 선택되지 않았습니다." : $"'{text}'는 참/거짓이 아닙니다.";
                    return false;
                }
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
                value = ToNumber(number, type);
                return true;
            }
            error = $"{DataModel.FriendlyName(type)}은(는) 글자로 적을 수 없습니다.";
            return false;
        }

        // 숫자를 형식에 맞춘다(정수형이면 반올림).
        public static object ToNumber(double number, Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            var whole = type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
                        type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte);
            return Convert.ChangeType(whole ? Math.Round(number) : number, type, CultureInfo.InvariantCulture);
        }

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

            var cut = path.LastIndexOf('.');
            var parentPath = cut < 0 ? "" : path.Substring(0, cut);
            var member = cut < 0 ? path : path.Substring(cut + 1);
            store.Modify<object>(key, root =>
            {
                foreach (var parent in FieldPaths.Resolve(root, parentPath).ToList())
                {
                    if (parent == null) continue;
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
            if (collection == null) return null;
            foreach (var item in made)
            {
                if (collection is IList list) list.Add(item);
                else
                {
                    var add = collection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .FirstOrDefault(m => m.Name == "Add" && m.GetParameters().Length == 1);
                    add?.Invoke(collection, new[] { item });
                }
            }
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
                    var before = map.Contains(mapKey) && map[mapKey] != null ? Convert.ToDouble(map[mapKey], CultureInfo.InvariantCulture) : 0;
                    next = PlayerDataGrants.ToNumber(before + delta, valueType);
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
                    if (amount == 0) errors.Add("증감이 0입니다.");
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
