using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GBible.Tests
{
    // 다국어 설정·표 애셋이 카탈로그에 제대로 옮겨지는지(애셋 파일 없이 메모리에서).
    public sealed class UnityLocalizationTests
    {
        static TextTable Table(string code, params (string key, string text)[] lines)
        {
            var table = ScriptableObject.CreateInstance<TextTable>();
            table.language = code;
            foreach (var (key, text) in lines) table.entries.Add(new TextTable.Entry { key = key, text = text });
            return table;
        }

        [Test]
        public void SettingsFeedCatalog()
        {
            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            settings.sourceLanguage = "ko";
            settings.languages = new List<LocalizationSettings.Language>
            {
                new LocalizationSettings.Language { code = "ko", table = Table("ko", ("ui.start", "시작하기"), ("only.ko", "원문")) },
                new LocalizationSettings.Language { code = "en", table = Table("en", ("ui.start", "Start")) },
                new LocalizationSettings.Language { code = "zh-Hant", fallbacks = "zh-Hans", table = Table("zh-Hant") },
                new LocalizationSettings.Language { code = "zh-Hans", table = Table("zh-Hans", ("ui.start", "开始")) },
            };
            var catalog = new TextCatalog();
            settings.Apply(catalog);

            catalog.Language = "en";
            Assert.AreEqual("Start", catalog.Get("ui.start"));
            Assert.AreEqual("원문", catalog.Get("only.ko"));
            catalog.Language = "zh-Hant";
            Assert.AreEqual("开始", catalog.Get("ui.start"), "zh-Hant → zh-Hans");

            Assert.AreEqual("en", settings.Closest("en-US"));
            Assert.AreEqual("zh-Hant", settings.Closest("zh-Hant"));
            Assert.IsNull(settings.Closest("fr"));
            Assert.AreEqual("ko", GameLanguage.SystemCode(SystemLanguage.Korean));
        }

        [Test]
        public void TableSetAllKeepsOrder()
        {
            var table = Table("en", ("a", "1"), ("b", "2"));
            table.SetAll(new Dictionary<string, string> { ["b"] = "20", ["c"] = "30" }, removeOthers: false);
            Assert.AreEqual(3, table.entries.Count);
            Assert.AreEqual("a", table.entries[0].key);
            Assert.AreEqual("20", table.entries[1].text);
            Assert.AreEqual("c", table.entries[2].key);
            table.SetAll(new Dictionary<string, string> { ["c"] = "31" }, removeOthers: true);
            Assert.AreEqual(1, table.entries.Count);
        }
    }
}
