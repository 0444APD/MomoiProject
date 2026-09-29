using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GameSystems
{
    // 식별자(ID·상태 키) 규칙. 이름은 데이터베이스 창 한 곳에서만 짓고, 나머지는 모두 목록에서 고른다.
    // 형식: 영문 소문자로 시작, 소문자·숫자·밑줄, 점(.)으로 분류를 나눈다. 예: player.inventory, kills_total
    // 이 문자열이 저장 파일·네트워크·코드 생성(GameIds)에서 공통 식별자로 쓰인다.
    public static class IdRules
    {
        public const int MaxLength = 64;
        static readonly Regex Pattern = new Regex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant);

        public static bool IsValid(string id, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(id)) error = "ID가 비어 있습니다.";
            else if (id.Length > MaxLength) error = $"ID는 {MaxLength}자 이하여야 합니다.";
            else if (!Pattern.IsMatch(id))
                error = "ID는 영문 소문자로 시작하고 소문자·숫자·밑줄(_)만 쓸 수 있습니다. 분류는 점(.)으로 나눕니다. 예: player.inventory";
            return error == null;
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
