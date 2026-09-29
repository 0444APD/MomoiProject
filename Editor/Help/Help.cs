using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using GameSystems.EditorTools.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace GameSystems.EditorTools
{
    // 도움말 항목 하나. Body는 빈 줄로 문단을 나누고, "## "로 시작하는 줄은 소제목, "- "로 시작하는 줄은 목록이 된다.
    public sealed class HelpTopic
    {
        public string Id;
        public string Title;
        public string Category;
        // 한두 문장 요약(툴팁·목록에 쓰인다).
        public string Summary;
        public string Body;
        // 함께 읽을 항목 id.
        public string[] Related = Array.Empty<string>();
        // 목록 정렬(작을수록 위).
        public int Order = 100;
        // 설명 문구(Description)에서 자동으로 만든 항목인지. 직접 쓴 항목이 필요한 곳은 계약 테스트가 이것으로 구분한다.
        public bool Generated;
    }

    // 도움말 제공자. 구현 클래스를 만들기만 하면 도움말 창과 "?" 버튼이 자동으로 찾는다(TypeCache).
    // 새 기능을 추가할 때는 그 기능의 항목을 여기로 함께 제공한다. 계약 테스트(HelpCoverageTests)가 누락을 막는다.
    public interface IHelpProvider
    {
        IEnumerable<HelpTopic> Topics();
    }

    public static class HelpIds
    {
        public const string Setup = "guide:setup";
        public static string Data(Type type) => "data:" + type.FullName;
        public static string Layout(string blockId) => "layout:" + blockId;
        public static string Monitor(string tab) => "monitor:" + tab;
        public static string Block(Type type) => "block:" + type.FullName;
    }

    public static class HelpRegistry
    {
        static List<HelpTopic> _topics;
        static Dictionary<string, HelpTopic> _byId;

        public static IReadOnlyList<HelpTopic> All
        {
            get
            {
                Load();
                return _topics;
            }
        }

        public static HelpTopic Find(string id)
        {
            Load();
            return id != null && _byId.TryGetValue(id, out var topic) ? topic : null;
        }

        public static void ClearCache()
        {
            _topics = null;
            _byId = null;
        }

        static void Load()
        {
            if (_topics != null) return;
            var topics = new List<HelpTopic>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IHelpProvider>())
            {
                if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null) continue;
                try
                {
                    topics.AddRange(((IHelpProvider)Activator.CreateInstance(type)).Topics().Where(t => t != null && !string.IsNullOrEmpty(t.Id)));
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }
            }
            topics.AddRange(Generated(topics));
            _byId = new Dictionary<string, HelpTopic>(StringComparer.Ordinal);
            foreach (var topic in topics)
                if (!_byId.ContainsKey(topic.Id)) _byId[topic.Id] = topic;
            _topics = _byId.Values.OrderBy(t => t.Category ?? "").ThenBy(t => t.Order).ThenBy(t => t.Title).ToList();
        }

        // 설명 문구를 이미 갖고 있는 기능(조건·보상·필터 블록, 배치 블록, 프로젝트에서 만든 도감 종류)은 항목을 자동으로 만든다.
        static IEnumerable<HelpTopic> Generated(List<HelpTopic> written)
        {
            var ids = new HashSet<string>(written.Select(t => t.Id));
            foreach (var baseType in new[] { typeof(Condition), typeof(Reward), typeof(ValueFilter) })
                foreach (var type in BlockTypes.ConcreteTypes(baseType))
                {
                    var id = HelpIds.Block(type);
                    if (ids.Contains(id)) continue;
                    var sentence = BlockTypes.Sentence(type) ?? BlockTypes.Keyword(type);
                    yield return new HelpTopic
                    {
                        Id = id,
                        Title = BlockTypes.DisplayName(type),
                        Category = baseType == typeof(Reward) ? "보상 블록" : baseType == typeof(Condition) ? "조건 블록" : "필드 조건 블록",
                        Summary = BlockTypes.Description(type) ?? "",
                        Body = (BlockTypes.Description(type) ?? "") +
                               (sentence != null ? $"\n\n## 표시 형식\n편집 칸에는 `{sentence}` 형태로 보입니다. 각 칸을 클릭해 값을 고릅니다." : "") +
                               "\n\n## 공통\n블록 제목 줄에서 접기·위아래 이동·복제·삭제를 할 수 있습니다. 문장에 마우스를 올리면 자연어 설명이 나옵니다.",
                        Related = new[] { "guide:conditions" },
                        Generated = true,
                    };
                }
            foreach (var entry in GsLayoutBlock.Registry.Values)
            {
                var id = HelpIds.Layout(entry.info.Id);
                if (ids.Contains(id)) continue;
                yield return new HelpTopic
                {
                    Id = id,
                    Title = entry.info.DisplayName,
                    Category = "화면 배치 블록",
                    Summary = entry.info.Description ?? "",
                    Body = (entry.info.Description ?? "") + "\n\n상세 화면의 '배치 편집'에서 추가하고, 12칸 격자 위의 위치와 폭을 정합니다.",
                    Related = new[] { "guide:layout" },
                    Generated = true,
                };
            }
            foreach (var info in DataTypes.All)
            {
                var id = HelpIds.Data(info.Type);
                if (ids.Contains(id)) continue;
                yield return new HelpTopic
                {
                    Id = id,
                    Title = info.DisplayName,
                    Category = "도감 종류",
                    Order = 500,
                    Summary = $"프로젝트에서 만든 도감 종류입니다({info.Type.Name}).",
                    Body = $"'{info.DisplayName}'은(는) 프로젝트 코드에서 GameData를 상속해 만든 종류입니다. 이 종류 전용 설명은 아직 없습니다.\n\n" +
                           "설명을 추가하려면 IHelpProvider를 구현한 클래스에서 id가 \"" + id + "\"인 항목을 제공하세요.",
                    Related = new[] { "guide:codex" },
                    Generated = true,
                };
            }
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        static void OnReload() => ClearCache();
    }

    public static class HelpUI
    {
        // 작은 "?" 버튼: 마우스를 올리면 요약, 누르면 도움말 창에서 그 항목을 연다.
        public static GsButton Button(string topicId)
        {
            var topic = HelpRegistry.Find(topicId);
            var button = new GsButton("?", () => HelpWindow.Show(topicId), GsButtonKind.Ghost);
            button.AddToClassList("gs-help-button");
            button.tooltip = topic != null ? $"{topic.Title}\n{topic.Summary}\n\n눌러서 자세히 보기" : "도움말 보기";
            return button;
        }

        // 도움말 본문을 문단·소제목·목록으로 그린다.
        public static VisualElement Body(string body)
        {
            var box = Gs.Col("gs-help__body");
            if (string.IsNullOrEmpty(body)) return box;
            foreach (var block in body.Replace("\r", "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var line in block.Split('\n'))
                {
                    if (line.StartsWith("## "))
                        box.Add(Gs.Label(line.Substring(3), "gs-help__heading"));
                    else if (line.StartsWith("- "))
                    {
                        var row = Gs.Row("gs-help__item");
                        row.Add(Gs.Label("·", "gs-help__bullet"));
                        row.Add(Gs.Label(line.Substring(2), "gs-help__text", "gs-grow"));
                        box.Add(row);
                    }
                    else box.Add(Gs.Label(line, "gs-help__text"));
                }
                box.Add(Gs.Space(10));
            }
            return box;
        }
    }
}
