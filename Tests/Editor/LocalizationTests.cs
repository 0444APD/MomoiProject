using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GBible.Tests
{
    public sealed class LocalizationTests
    {
        [Test]
        public void 문장_틀의_값_안_필드를_점으로_읽는다()
        {
            var catalog = new TextCatalog();
            var args = new Dictionary<string, object> { ["run"] = new GsParty { gold = 7 }, ["player.name"] = "모모이" };
            Assert.AreEqual("모모이의 골드 7", catalog.FormatTemplate("{player.name}의 골드 {run.gold}", args));
        }

        TextCatalog _texts;

        [SetUp]
        public void SetUp()
        {
            _texts = new TextCatalog { SourceLanguage = "ko", Language = "ko" };
            _texts.SetTable("ko", new Dictionary<string, string>
            {
                ["ui.start"] = "시작하기",
                ["dragonArmor.name"] = "용의 비늘 갑옷",
                ["potion.name"] = "포션",
                ["goblin.name"] = "고블린",
                ["orc.name"] = "오크",
                ["castle.name"] = "성",
                ["village.name"] = "마을",
                ["road.name"] = "길",
                ["arin.name"] = "아린",
                ["burn.name"] = "화상",
                ["gold.name"] = "골드",
                ["reward.gain"] = "{item:을} {count}개 얻었습니다.",
                ["enemy.appear"] = "{enemy:이} 나타났다!",
                ["move.to"] = "{place:으로} 이동합니다.",
                ["call"] = "{name:아}, 일어나.",
                ["status.burn"] = "[[burn:이]] 번졌다.",
                ["only.ko"] = "한국어만 있음",
            });
            _texts.SetTable("en", new Dictionary<string, string>
            {
                ["ui.start"] = "Start",
                ["dragonArmor.name"] = "{count, plural, one {Dragon Scale Armor} other {Dragon Scale Armors}}",
                ["potion.name"] = "{count, plural, one {potion} other {potions}}",
                ["burn.name"] = "burn",
                ["reward.gain"] = "You got {count} {item}.",
                ["inbox"] = "{n, plural, =0 {No messages} one {# message} other {# messages}}",
                ["gold.total"] = "{gold, number} gold",
                ["loot"] = "{count, plural, one {[[potion, count]]} other {# [[potion, count]]}}",
                ["term.cap"] = "[[burn:cap]] spreads.",
                ["gender"] = "{hero} lost {gender, select, male {his} female {her} other {their}} sword.",
            });
        }

        string Ko(string key, params (string, object)[] args)
        {
            _texts.Language = "ko";
            return _texts.Get(key, args);
        }

        [Test]
        public void PlainTextAndCodexNames()
        {
            Assert.AreEqual("시작하기", Ko("ui.start"));
            Assert.AreEqual("용의 비늘 갑옷을 3개 얻었습니다.", Ko("reward.gain", ("item", "dragonArmor"), ("count", 3)));
            _texts.Language = "en";
            Assert.AreEqual("Start", _texts.Get("ui.start"));
        }

        [Test]
        public void KoreanParticlesFollowFinalSound()
        {
            Assert.AreEqual("포션을 1개 얻었습니다.", Ko("reward.gain", ("item", "potion"), ("count", 1)));
            Assert.AreEqual("고블린이 나타났다!", Ko("enemy.appear", ("enemy", "goblin")));
            Assert.AreEqual("오크가 나타났다!", Ko("enemy.appear", ("enemy", "orc")));
            Assert.AreEqual("성으로 이동합니다.", Ko("move.to", ("place", "castle")));
            Assert.AreEqual("마을로 이동합니다.", Ko("move.to", ("place", "village")));
            Assert.AreEqual("길로 이동합니다.", Ko("move.to", ("place", "road")));
            Assert.AreEqual("지하 1층으로 이동합니다.", Ko("move.to", ("place", "지하 1층")));
            Assert.AreEqual("아린아, 일어나.", Ko("call", ("name", "arin")));
        }

        [Test]
        public void ParticlesForNumbersAndLatin()
        {
            Assert.AreEqual("을", KoreanParticles.Pick("3", "를"));
            Assert.AreEqual("를", KoreanParticles.Pick("2", "을"));
            Assert.AreEqual("을", KoreanParticles.Pick("10", "를"));
            Assert.AreEqual("로", KoreanParticles.Pick("1", "으로"));
            Assert.AreEqual("이", KoreanParticles.Pick("HTML", "가"));
            Assert.AreEqual("를", KoreanParticles.Pick("PC", "을"));
            Assert.AreEqual("을", KoreanParticles.Pick("<b>검</b>", "를"));
            Assert.AreEqual("을(를)", KoreanParticles.Pick("Kit", "을"));
            // 소리 나지 않는 e: 게임·룬은 받침, 테이블은 ㄹ 받침, 페이지는 받침 없음.
            Assert.AreEqual("을", KoreanParticles.Pick("Game", "를"));
            Assert.AreEqual("이", KoreanParticles.Pick("Rune", "가"));
            Assert.AreEqual("로", KoreanParticles.Pick("Table", "으로"));
            Assert.AreEqual("를", KoreanParticles.Pick("Page", "을"));
            KoreanParticles.Overrides["Kit"] = true;
            try
            {
                Assert.AreEqual("을", KoreanParticles.Pick("Kit", "을"));
                Assert.AreEqual("을", KoreanParticles.Pick("Starter Kit", "을"), "예외 등록은 마지막 단어로도");
            }
            finally
            {
                KoreanParticles.Overrides.Remove("Kit");
            }
        }

        [Test]
        public void PluralAndSelect()
        {
            _texts.Language = "en";
            Assert.AreEqual("No messages", _texts.Get("inbox", ("n", 0)));
            Assert.AreEqual("1 message", _texts.Get("inbox", ("n", 1)));
            Assert.AreEqual("5 messages", _texts.Get("inbox", ("n", 5)));
            Assert.AreEqual("1,200 messages", _texts.Get("inbox", ("n", 1200)));
            Assert.AreEqual("Mina lost her sword.", _texts.Get("gender", new { hero = "Mina", gender = "female" }));
            Assert.AreEqual("Kai lost their sword.", _texts.Get("gender", new { hero = "Kai", gender = "x" }));
            Assert.AreEqual("You got 3 Dragon Scale Armors.", _texts.Get("reward.gain", ("item", "dragonArmor"), ("count", 3)));
        }

        [Test]
        public void PluralWithBigNumber()
        {
            // 큰 수(BigNumber)도 숫자다: 복수 규칙은 그 값으로, #은 {n}처럼 큰 수의 형식(1.5K)으로(0으로 읽지 않는다).
            _texts.Language = "en";
            const string template = "{n, plural, one {# cookie} other {# cookies}}";
            Assert.AreEqual("1 cookie", _texts.FormatTemplate(template, TextArgs.From(("n", new BigNumber(1)))));
            Assert.AreEqual("1.5K cookies", _texts.FormatTemplate(template, TextArgs.From(("n", new BigNumber(1500)))));
            Assert.AreEqual("1.5K", _texts.FormatTemplate("{n}", TextArgs.From(("n", new BigNumber(1500)))), "#과 같은 모양");
            Assert.AreEqual("4 left", _texts.FormatTemplate("{n, plural, offset:1 =1 {none} other {# left}}", TextArgs.From(("n", new BigNumber(5)))));
            Assert.AreEqual("1.23e400 cookies", _texts.FormatTemplate(template, TextArgs.From(("n", new BigNumber(1.23, 400)))));
        }

        [Test]
        public void PluralCategoriesPerLanguage()
        {
            Assert.AreEqual("one", PluralRules.Category("en", 1));
            Assert.AreEqual("other", PluralRules.Category("en", 1.5));
            Assert.AreEqual("other", PluralRules.Category("ko", 1));
            Assert.AreEqual("few", PluralRules.Category("ru", 3));
            Assert.AreEqual("many", PluralRules.Category("ru", 11));
            Assert.AreEqual("one", PluralRules.Category("ru", 21));
            Assert.AreEqual("one", PluralRules.Category("fr", 0));
            Assert.AreEqual("one", PluralRules.Category("pt-BR", 1));
        }

        [Test]
        public void PortugalPortugueseIsOneOnlyForOne()
        {
            // CLDR: pt(브라질) one = 정수 부분 0·1, pt_PT one = 정수 1만.
            Assert.AreEqual("one", PluralRules.Category("pt", 0));
            Assert.AreEqual("one", PluralRules.Category("pt-BR", 1.5));
            Assert.AreEqual("other", PluralRules.Category("pt-PT", 0));
            Assert.AreEqual("one", PluralRules.Category("pt-PT", 1));
            Assert.AreEqual("other", PluralRules.Category("pt_pt", 1.5));
        }

        [Test]
        public void NumbersFollowLanguage()
        {
            _texts.SetTable("de", new Dictionary<string, string> { ["gold.total"] = "{gold, number} Gold" });
            _texts.Language = "de";
            Assert.AreEqual("1.234.567,5 Gold", _texts.Get("gold.total", ("gold", 1234567.5)));
            _texts.Language = "en";
            Assert.AreEqual("1,234,567.5 gold", _texts.Get("gold.total", ("gold", 1234567.5)));
            Assert.AreEqual("50%", _texts.FormatTemplate("{p, number, percent}", TextArgs.From(("p", 0.5))));
            Assert.AreEqual("3", _texts.FormatTemplate("{p, number, integer}", TextArgs.From(("p", 2.6))));
            // 모든 정수 형식이 숫자다(ushort·sbyte도 천 단위 표기).
            Assert.AreEqual("65,535", _texts.FormatTemplate("{p}", TextArgs.From(("p", ushort.MaxValue))));
            Assert.AreEqual("-128", _texts.FormatTemplate("{p, number, integer}", TextArgs.From(("p", sbyte.MinValue))));
        }

        [Test]
        public void NegativeRoundingToZeroShowsZero()
        {
            // 0으로 반올림되는 음수는 "0"(.NET은 "-0", Mono는 "0"으로 적는다 — 어디서나 같게).
            string F(string template, double n) => _texts.FormatTemplate(template, TextArgs.From(("n", n)));
            Assert.AreEqual("0", F("{n}", -0.0001));
            Assert.AreEqual("0", F("{n}", -0.0));
            Assert.AreEqual("0", F("{n, number, integer}", -0.3));
            Assert.AreEqual("0%", F("{n, number, percent}", -0.00001));
            Assert.AreEqual("0.0", F("{n, number, 0.0}", -0.01));
            Assert.AreEqual("-0.5", F("{n}", -0.5));
            Assert.AreEqual("-1", F("{n, number, integer}", -0.5));
            Assert.AreEqual("0 messages", _texts.FormatTemplate("{n, plural, one {# message} other {# messages}}", TextArgs.From(("n", -0.0001))));
        }

        [Test]
        public void TermsWithParticlesAndDecoration()
        {
            Assert.AreEqual("화상이 번졌다.", Ko("status.burn"));
            _texts.DecorateTerm = (id, text) => $"<link=\"term:{id}\"><b>{text}</b></link>";
            Assert.AreEqual("<link=\"term:burn\"><b>화상</b></link>이 번졌다.", Ko("status.burn"));
            _texts.DecorateTerm = null;
            _texts.Language = "en";
            Assert.AreEqual("Burn spreads.", _texts.Get("term.cap"));
            Assert.AreEqual("potion", _texts.Get("loot", ("count", 1)));
            Assert.AreEqual("4 potions", _texts.Get("loot", ("count", 4)));
        }

        [Test]
        public void FallbackAndMissing()
        {
            _texts.Language = "en-GB";
            Assert.AreEqual("Start", _texts.Get("ui.start"), "en-GB → en");
            Assert.AreEqual("한국어만 있음", _texts.Get("only.ko"), "→ 원문 언어");
            string missing = null;
            _texts.Missing += (lang, key) => missing = key;
            Assert.AreEqual("[nope]", _texts.Get("nope"));
            Assert.AreEqual("nope", missing);

            _texts.Fallbacks["pt-BR"] = new[] { "en" };
            _texts.Language = "pt-BR";
            Assert.AreEqual("Start", _texts.Get("ui.start"));
            CollectionAssert.AreEqual(new[] { "pt-BR", "en", "pt", "ko" }, _texts.Chain("pt-BR").ToArray());
        }

        [Test]
        public void LoaderAndLanguageChanged()
        {
            var changed = 0;
            _texts.LanguageChanged += () => changed++;
            _texts.Loader = lang => lang == "ja" ? new Dictionary<string, string> { ["ui.start"] = "スタート" } : null;
            _texts.Language = "ja";
            Assert.AreEqual("スタート", _texts.Get("ui.start"));
            Assert.AreEqual(1, changed);
            _texts.Language = "ja";
            Assert.AreEqual(1, changed, "같은 언어면 알리지 않는다");
        }

        [Test]
        public void MergeBeforeFirstUseKeepsLoadedTable()
        {
            // 아직 읽지 않은 언어에 Merge·Set을 해도 Loader의 표에 더한다(빈 표가 Loader 표를 가리지 않는다).
            var catalog = new TextCatalog { SourceLanguage = "ko", Language = "en" };
            catalog.Loader = lang => lang == "en" ? new Dictionary<string, string> { ["ui.start"] = "Start", ["ui.quit"] = "Quit" } : null;
            catalog.Set("en", "ui.extra", "Extra");
            catalog.Merge("en", new Dictionary<string, string> { ["ui.quit"] = "Leave" });
            Assert.AreEqual("Start", catalog.Get("ui.start"));
            Assert.AreEqual("Extra", catalog.Get("ui.extra"));
            Assert.AreEqual("Leave", catalog.Get("ui.quit"), "같은 키는 덮어쓴다");
        }

        [Test]
        public void QuotingAndEscapes()
        {
            Assert.AreEqual("It's {literal} [[x]]", _texts.FormatTemplate("It''s '{literal}' \\[[x]]"));
            Assert.AreEqual("<color=red>3</color>", _texts.FormatTemplate("<color=red>{n}</color>", TextArgs.From(("n", 3))));
            Assert.AreEqual("{missing}", _texts.FormatTemplate("{missing}"));
        }

        [Test]
        public void ParseErrorsKeepTemplate()
        {
            var bad = TextFormat.Parse("{count, plural, one {x}}");
            Assert.IsNotNull(bad.Error, "other 없음");
            Assert.IsNotNull(TextFormat.Parse("{open").Error);
            Assert.IsNotNull(TextFormat.Parse("[[x").Error);
            Assert.AreEqual("{open", _texts.FormatTemplate("{open"));

            var good = TextFormat.Parse("{a:을} {n, plural, one {# [[sword]]} other {# [[sword, n]]}} {b, select, x {{c}} other {}}");
            Assert.IsNull(good.Error);
            CollectionAssert.AreEqual(new[] { "a", "n", "b", "c" }.OrderBy(x => x).ToArray(), good.Arguments.OrderBy(x => x).ToArray());
            CollectionAssert.AreEqual(new[] { "sword" }, good.Terms.ToArray());
        }

        [Test]
        public void PseudoLocalization()
        {
            _texts.Language = "en";
            _texts.Pseudo = true;
            var text = _texts.Get("ui.start");
            Assert.IsTrue(text.StartsWith("[Šţåŕţ"), text);
            Assert.IsTrue(text.EndsWith("~]"), text);
        }

        [Test]
        public void CsvRoundTrip()
        {
            var rows = Csv.Read("﻿a,\"b,c\",\"say \"\"hi\"\"\"\r\n\r\nx,\"line1\nline2\",\n");
            Assert.AreEqual(2, rows.Count);
            CollectionAssert.AreEqual(new[] { "a", "b,c", "say \"hi\"" }, rows[0]);
            CollectionAssert.AreEqual(new[] { "x", "line1\nline2", "" }, rows[1]);
            var text = Csv.Write(rows.Select(r => (IEnumerable<string>)r));
            var again = Csv.Read(text);
            CollectionAssert.AreEqual(rows[0], again[0]);
            CollectionAssert.AreEqual(rows[1], again[1]);
        }

        [Test]
        public void CsvQuoteInsideUnquotedCellIsText()
        {
            // 칸 가운데의 따옴표는 글자다(따옴표 칸은 칸 맨 앞의 따옴표로만 시작한다). 손으로 쓴 5" 칼이 뒤 줄을 삼키지 않는다.
            var rows = Csv.Read("id,title\r\nsword,5\" blade\r\nbow,say \"hi\" now\r\nstaff, \"a, b\"\r\n");
            Assert.AreEqual(4, rows.Count);
            CollectionAssert.AreEqual(new[] { "sword", "5\" blade" }, rows[1]);
            CollectionAssert.AreEqual(new[] { "bow", "say \"hi\" now" }, rows[2]);
            CollectionAssert.AreEqual(new[] { "staff", " a, b" }, rows[3], "공백 뒤의 따옴표는 따옴표 칸");
            CollectionAssert.AreEqual(new[] { "ab\"c\"" }, Csv.Read("\"a\"b\"c\"")[0], "닫는 따옴표 뒤의 따옴표는 글자");
        }

        [Test]
        public void SheetExportImportByHeaderName()
        {
            var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["ko"] = new Dictionary<string, string> { ["ui.start"] = "시작하기", ["ui.quit"] = "끝내기, 정말?" },
                ["en"] = new Dictionary<string, string> { ["ui.start"] = "Start" },
            };
            var csv = TextSheet.Export(new[] { "ko", "en" }, tables);
            StringAssert.Contains("\"끝내기, 정말?\"", csv);

            // 번역가가 열 순서를 바꾸고 메모 열을 더해도 이름으로 읽는다.
            var edited = "#gbible-text,1\nmemo,EN,key,ko\nhi,Quit,ui.quit,바뀐 원문\n,,ui.start,\n";
            var result = TextSheet.Import(edited, new[] { "en" });
            Assert.IsEmpty(result.Warnings);
            Assert.AreEqual("Quit", result.Tables["en"]["ui.quit"]);
            Assert.IsFalse(result.Tables["en"].ContainsKey("ui.start"), "빈 칸은 가져오지 않는다");
            Assert.IsFalse(result.Tables.ContainsKey("ko"), "요청한 언어만");

            Assert.IsNotEmpty(TextSheet.Import("a,b\n1,2").Warnings, "key 열 없음");
        }

        [Test]
        public void SheetRowStartingWithHashInOtherColumnIsData()
        {
            // 머리줄 아래에서는 key 칸이 '#'일 때만 설명 줄이다. 앞으로 옮긴 메모·언어 열이 '#'으로 시작해도 그 줄을 읽는다.
            var edited = "#gbible-text,1\nmemo,key,en\n#확인 필요,ui.quit,Quit\n#1 판매,ui.best,#1 Seller\n# 상점,,\n,#note,무시\n";
            var result = TextSheet.Import(edited, new[] { "en" });
            Assert.AreEqual("Quit", result.Tables["en"]["ui.quit"]);
            Assert.AreEqual("#1 Seller", result.Tables["en"]["ui.best"]);
            Assert.AreEqual(2, result.Tables["en"].Count, "key가 '#'인 줄은 설명");
            Assert.IsEmpty(result.Warnings);
        }

        [Test]
        public void SheetContextColumnsAreReadOnly()
        {
            var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["ko"] = new Dictionary<string, string> { ["line.1"] = "왔구나" },
                ["en"] = new Dictionary<string, string>(),
            };
            var csv = TextSheet.Export(new[] { "ko", "en" }, tables, null, new Dictionary<string, string> { ["line.1"] = "놀란 말투" },
                new[] { "location", "speaker" }, (key, column) => column == "location" ? "성" : "아린");
            var rows = Csv.Read(csv);
            CollectionAssert.AreEqual(new[] { "key", "location", "speaker", "ko", "en", "note" }, rows[1]);
            CollectionAssert.AreEqual(new[] { "line.1", "성", "아린", "왔구나", "", "놀란 말투" }, rows[2]);

            var edited = csv.Replace("왔구나,,", "왔구나,You came,");
            var result = TextSheet.Import(edited, new[] { "en" });
            Assert.AreEqual("You came", result.Tables["en"]["line.1"]);
            Assert.AreEqual(1, result.Tables["en"].Count, "문맥 열은 가져오지 않는다");
        }

        [Test]
        public void TranslationCheck()
        {
            Assert.IsEmpty(TextSheet.Check("{item:을} {count}개", "{count} {item}"));
            Assert.IsNotEmpty(TextSheet.Check("{item:을} {count}개", "{count} items"));
            Assert.IsNotEmpty(TextSheet.Check("[[burn]] 피해", "damage"));
            Assert.IsNotEmpty(TextSheet.Check("<b>{n}</b>", "{n}"));
            Assert.IsNotEmpty(TextSheet.Check("{n}", "{n, plural, one {x}}"));
        }

        [Test]
        public void TranslationCheckReadsPluralCasesAndTermValues()
        {
            // 경우마다 같은 태그를 되풀이한 번역(영어 복수형)은 원문과 같은 태그다.
            Assert.IsEmpty(TextSheet.Check("<b>{n}</b>개의 [[potion]]", "{n, plural, one {<b>#</b> [[potion, n]]} other {<b>#</b> [[potion, n]]}}"));
            Assert.IsEmpty(TextSheet.Check("<color={c}>{n}</color>", "{n, plural, =0 {<color={c}>none</color>} other {<color={c}>#</color>}}"));
            Assert.IsNotEmpty(TextSheet.Check("<b>A</b> <b>B</b>", "<b>A</b> <b>B"), "닫는 태그가 빠짐");
            Assert.IsNotEmpty(TextSheet.Check("<b>{n}</b>", "{n, plural, one {#} other {#}}"), "모든 경우에서 태그가 빠짐");
            // 용어에 넘기는 값([[potion, count]])도 코드가 넘기는 값이다. 번역이 그 값을 보여도 된다.
            Assert.IsEmpty(TextSheet.Check("[[potion, count]]을 얻었다", "Got {count} [[potion, count]]"));
            Assert.IsNotEmpty(TextSheet.Check("{count}개의 [[potion]]", "[[potion, count]]"), "원문이 보여 주는 값을 번역이 보여 주지 않음");
            Assert.IsNotEmpty(TextSheet.Check("{count}개의 [[potion]]", "{count} [[potion, n]]"), "용어에 원문에 없는 값");
        }

        [Test]
        public void DictionaryArgsOfAnyValueType()
        {
            // Dictionary<string, object>가 아닌 딕셔너리도 값 묶음이다(속성 Count·Keys를 값으로 읽지 않는다).
            Assert.AreEqual("고블린이 나타났다!", _texts.Get("enemy.appear", new Dictionary<string, string> { ["enemy"] = "goblin" }));
            _texts.Language = "en";
            Assert.AreEqual("1,200 messages", _texts.Get("inbox", new Dictionary<string, int> { ["n"] = 1200 }));
        }

        [Test]
        public void TextKeyArgument()
        {
            _texts.Language = "en";
            Assert.AreEqual("You got 2 Start.", _texts.Get("reward.gain", ("item", new TextKey("ui.start")), ("count", 2)));
        }
    }
}
