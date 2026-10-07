using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GBible
{
    // 식별자(ID) 규칙. ID는 DB에서 한 번 짓고, 나머지는 모두 목록에서 고른다.
    // 형식: lowerCamelCase(영문 소문자로 시작, 단어 사이는 대문자, 영문·숫자만), 점(.)으로 분류를 나눈다. 예: playerGold, player.inventory
    // 대소문자만 다른 ID는 같은 ID로 본다(Windows 파일 이름이 대소문자를 구분하지 않으므로).
    // 이 문자열이 저장 파일·네트워크·코드 생성(GameIds)에서 공통 식별자로 쓰인다.
    public static class IdRules
    {
        public const int MaxLength = 64;
        // lowerCamelCase: 영문 소문자로 시작, 단어 사이는 대문자. 분류는 점(.)으로. 예: playerGold, player.inventory
        // 끝은 \z($는 마지막 줄바꿈 앞에서도 맞아 "gold\n"을 통과시킨다 — GameIds 생성 코드가 깨진다).
        static readonly Regex Pattern = new Regex(@"^[a-z][a-zA-Z0-9]*(\.[a-z][a-zA-Z0-9]*)*\z", RegexOptions.CultureInvariant);

        public static bool IsValid(string id, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(id)) error = "ID가 비어 있습니다.";
            else if (id.Length > MaxLength) error = $"ID는 {MaxLength}자 이하여야 합니다.";
            else if (!Pattern.IsMatch(id))
                error = "ID는 영문 소문자로 시작하고 영문·숫자만 씁니다. 단어 사이는 대문자로 잇고(띄어쓰기·밑줄 없음), 분류는 점(.)으로 나눕니다. 예: playerGold, player.inventory";
            return error == null;
        }

        // 코드 이름·글자를 ID 조각(lowerCamelCase)으로: SampleEquipment → sampleEquipment, effect_tags → effectTags.
        public static string Normalize(string name)
        {
            var b = new System.Text.StringBuilder();
            var upperNext = false;
            foreach (var c in name ?? "")
            {
                if (c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9')
                {
                    if (b.Length == 0) b.Append(char.ToLowerInvariant(c));
                    else b.Append(upperNext ? char.ToUpperInvariant(c) : c);
                    upperNext = false;
                }
                else upperNext = b.Length > 0;
            }
            var result = b.ToString();
            if (result.Length == 0 || !(result[0] >= 'a' && result[0] <= 'z')) result = "x" + result;
            return result;
        }

        // 분류(첫 점 또는 콜론 앞). 모니터가 이 이름으로 묶어 보여 준다.
        public static string GroupOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return "기타";
            var index = key.IndexOfAny(new[] { '.', ':' });
            return index > 0 ? key.Substring(0, index) : "기타";
        }

        // 코드 생성용 C# 식별자. player.inventory → PlayerInventory
        public static string ToIdentifier(string id)
        {
            var builder = new StringBuilder();
            foreach (var part in (id ?? "").Split(new[] { '.', '_', ':', '-' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var clean = new string(part.Where(char.IsLetterOrDigit).ToArray());
                if (clean.Length == 0) continue;
                builder.Append(char.ToUpper(clean[0], CultureInfo.InvariantCulture)).Append(clean.Substring(1));
            }
            if (builder.Length == 0) builder.Append("Id");
            if (char.IsDigit(builder[0])) builder.Insert(0, '_');
            return builder.ToString();
        }
    }
}
