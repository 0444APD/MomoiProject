using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameSystems
{
    // 한 언어의 텍스트 표(키 → 문장 틀). 시트(CSV)에서 가져오거나 직접 고친다.
    [CreateAssetMenu(menuName = "Game Systems/다국어/텍스트 표", fileName = "Texts_ko")]
    public sealed class TextTable : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            [TextArea(1, 10)] public string text;
        }

        [Tooltip("BCP-47 언어 코드(ko, en, ja, zh-Hans, pt-BR …)")]
        public string language = "ko";
        public List<Entry> entries = new List<Entry>();

        public Dictionary<string, string> ToDictionary()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in entries)
                if (!string.IsNullOrEmpty(entry.key)) map[entry.key] = entry.text ?? "";
            return map;
        }

        // 표 내용을 바꾼다(키 순서는 기존 순서 유지, 새 키는 뒤에).
        public void SetAll(IReadOnlyDictionary<string, string> values, bool removeOthers)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (values.TryGetValue(entry.key ?? "", out var text))
                {
                    entry.text = text;
                    entries[i] = entry;
                    seen.Add(entry.key);
                }
                else if (removeOthers) entries.RemoveAt(i);
            }
            foreach (var pair in values)
                if (!seen.Contains(pair.Key)) entries.Add(new Entry { key = pair.Key, text = pair.Value });
        }
    }
}
