using UnityEditor;

namespace GBible.EditorTools
{
    // 옛 이름(GameSystems) 때 에디터 세션에 맡겨 둔 값을 새 이름(GBible)의 키로 옮긴다.
    // 이름을 바꾼 직후, 저장하지 않은 도감 변경이 남아 있던 세션에서만 할 일이 있다.
    public static class LegacyNames
    {
        const string NewPrefix = "GBible.";
        const string OldPrefix = "GameSystems.";

        // 세션 키를 돌려준다. 옛 키에 값이 있고 새 키가 비어 있으면 옮겨 둔다.
        public static string Session(string key)
        {
            if (!key.StartsWith(NewPrefix)) return key;
            var old = OldPrefix + key.Substring(NewPrefix.Length);
            var value = SessionState.GetString(old, "");
            if (value.Length == 0) return key;
            if (SessionState.GetString(key, "").Length == 0) SessionState.SetString(key, value);
            SessionState.EraseString(old);
            return key;
        }
    }
}
