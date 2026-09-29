using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameSystems.Data;
using UnityEditor;
using UnityEngine;

namespace GameSystems.EditorTools
{
    public enum SetupState
    {
        // 아직 할 일이 있다.
        Todo,
        // 자동 판정 또는 직접 표시로 끝났다.
        Done,
        // 할 필요가 없다고 표시했다.
        Skipped,
        // 해 두었지만 고칠 곳이 있다(검증 오류 등).
        Warning,
    }

    public sealed class SetupAction
    {
        public string Label;
        public Action Run;
        public bool Primary;

        public SetupAction(string label, Action run, bool primary = false)
        {
            Label = label;
            Run = run;
            Primary = primary;
        }
    }

    // 시작하기의 한 단계. 설명은 도움말과 같은 형식(빈 줄 = 문단, "## " 소제목, "- " 목록).
    public sealed class SetupStep
    {
        public string Id;
        public int Order;
        public string Title;
        public string Summary;
        public string Body;
        // 이 단계와 관련된 도움말 항목.
        public string HelpTopic;
        // 선택 단계(게임에 따라 필요 없을 수 있다).
        public bool Optional;
        // 현재 상태를 한 줄로. done이 true면 자동으로 완료 판정. null이면 자동 판정 없음(직접 표시).
        public Func<(bool done, string text)> Check;
        public List<SetupAction> Actions = new List<SetupAction>();
    }

    // 설정 단계 제공자. 새 기능이 설정을 요구하면 단계를 추가한다(TypeCache로 자동 수집).
    public interface ISetupProvider
    {
        IEnumerable<SetupStep> Steps();
    }

    public static class SetupRegistry
    {
        const string ProgressPath = "ProjectSettings/GameSystems/Setup.json";

        [Serializable]
        sealed class Progress
        {
            public List<string> done = new List<string>();
            public List<string> skipped = new List<string>();
        }

        static Progress _progress;

        public static IReadOnlyList<SetupStep> All
        {
            get
            {
                var steps = new List<SetupStep>();
                foreach (var type in TypeCache.GetTypesDerivedFrom<ISetupProvider>())
                {
                    if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null) continue;
                    try
                    {
                        steps.AddRange(((ISetupProvider)Activator.CreateInstance(type)).Steps().Where(s => s != null && !string.IsNullOrEmpty(s.Id)));
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
                return steps.GroupBy(s => s.Id).Select(g => g.First()).OrderBy(s => s.Order).ToList();
            }
        }

        public static (SetupState state, string text) StateOf(SetupStep step)
        {
            var progress = Load();
            string text = null;
            var autoDone = false;
            if (step.Check != null)
            {
                try
                {
                    (autoDone, text) = step.Check();
                }
                catch (Exception e)
                {
                    return (SetupState.Warning, "상태 확인 실패: " + e.Message);
                }
            }
            if (progress.skipped.Contains(step.Id)) return (SetupState.Skipped, text ?? "건너뜀");
            if (autoDone || progress.done.Contains(step.Id)) return (SetupState.Done, text ?? "완료로 표시함");
            return (SetupState.Todo, text ?? "직접 확인한 뒤 '완료로 표시'를 누르세요.");
        }

        public static bool IsMarkedDone(string id) => Load().done.Contains(id);
        public static bool IsSkipped(string id) => Load().skipped.Contains(id);

        public static void Mark(string id, SetupState state)
        {
            var progress = Load();
            progress.done.Remove(id);
            progress.skipped.Remove(id);
            if (state == SetupState.Done) progress.done.Add(id);
            if (state == SetupState.Skipped) progress.skipped.Add(id);
            Directory.CreateDirectory(Path.GetDirectoryName(ProgressPath));
            File.WriteAllText(ProgressPath, JsonUtility.ToJson(progress, true));
        }

        static Progress Load()
        {
            if (_progress != null) return _progress;
            try
            {
                _progress = File.Exists(ProgressPath) ? JsonUtility.FromJson<Progress>(File.ReadAllText(ProgressPath)) : null;
            }
            catch (Exception)
            {
                _progress = null;
            }
            return _progress ??= new Progress();
        }
    }

    // 기본 설정 단계. 기능을 추가하면 필요한 단계를 여기(또는 그 기능의 ISetupProvider)에 넣는다.
    sealed class BuiltInSetup : ISetupProvider
    {
        static int Count<T>() where T : GameData => EditorDatabase.Instance.OfType<T>().Count();

        static SetupAction Open<T>(string label = null) where T : GameData =>
            new SetupAction(label ?? $"{DataTypes.Find(typeof(T))?.DisplayName} 열기", () => DatabaseWindow.ShowType(typeof(T)));

        static SetupAction Create<T>() where T : GameData =>
            new SetupAction($"+ {DataTypes.Find(typeof(T))?.DisplayName} 만들기", () => DatabaseWindow.ShowType(typeof(T), create: true), true);

        public IEnumerable<SetupStep> Steps()
        {
            yield return new SetupStep
            {
                Id = "project-state", Order = 0, Title = "프로젝트 상태 확인",
                Summary = "지금이 개발 중인지 출시 후인지 정합니다. 저장 호환 방식이 달라집니다.",
                Body = "개발 중에는 ID나 데이터 구조가 바뀌어 기존 저장과 맞지 않으면 저장을 지우고 새 프로필로 시작합니다. 변환 코드를 작성할 필요가 없습니다.\n\n" +
                       "출시 후에는 ID 변경 이력을 기록해 예전 저장을 이어받습니다. 구조 변경에는 변환 단계(SessionOptions.Migrations)가 필요합니다.\n\n" +
                       "처음에는 '개발 중' 그대로 두면 됩니다. 출시 직전에 메뉴 Game Systems › 출시됨을 켭니다.",
                HelpTopic = "guide:saves",
                Check = () => (true, EditorDatabase.IsReleased ? "출시됨: 저장을 이어받습니다" : "개발 중: 호환되지 않는 저장은 지웁니다"),
                Actions = { new SetupAction("출시 상태 바꾸기", () => EditorApplication.ExecuteMenuItem(EditorDatabase.ReleasedMenu)) },
            };
            yield return new SetupStep
            {
                Id = "tags", Order = 10, Title = "태그 만들기",
                Summary = "데이터를 분류할 이름(보스·원거리·전설 등)을 만듭니다.",
                Body = "태그는 여러 데이터에 공통으로 붙이는 분류입니다. 조건식(예: 태그 boss 포함)과 도감 검색(`tag:이름`)에 쓰입니다.\n\n" +
                       "태그 이름은 여기서만 짓고, 다른 곳에서는 목록에서 고릅니다. 지금 떠오르는 것만 만들고 나중에 추가해도 됩니다.",
                HelpTopic = HelpIds.Data(typeof(TagData)),
                Optional = true,
                Check = () => (Count<TagData>() > 0, $"태그 {Count<TagData>()}개"),
                Actions = { Create<TagData>(), Open<TagData>() },
            };
            yield return new SetupStep
            {
                Id = "choices", Order = 20, Title = "선택지 목록 만들기",
                Summary = "장비 부위·등급·속성처럼 정해진 값 중에서 고르는 칸의 목록을 만듭니다.",
                Body = "선택지 목록을 만들고, 상세 화면의 '선택지' 블록에서 값을 추가합니다.\n\n" +
                       "코드의 클래스에는 빈 string 필드만 두고, 다음 단계의 필드 규칙으로 목록과 연결합니다. enum과 달리 값을 추가할 때 코드를 고치지 않아도 됩니다.",
                HelpTopic = HelpIds.Data(typeof(ChoiceListData)),
                Optional = true,
                Check = () =>
                {
                    var lists = Count<ChoiceListData>();
                    var empty = EditorDatabase.Instance.OfType<ChoiceListData>().Count(l => !EditorDatabase.Instance.OfType<ChoiceData>().Any(c => c.list == l.Id));
                    return (lists > 0 && empty == 0, lists == 0 ? "선택지 목록 없음" : empty > 0 ? $"목록 {lists}개 · 선택지가 없는 목록 {empty}개" : $"목록 {lists}개 · 선택지 {Count<ChoiceData>()}개");
                },
                Actions = { Create<ChoiceListData>(), Open<ChoiceListData>() },
            };
            yield return new SetupStep
            {
                Id = "field-rules", Order = 30, Title = "필드 규칙 연결",
                Summary = "클래스의 string·Sprite 필드에 값의 출처(태그·선택지·도감 항목·상태 키·스프라이트 규격)를 연결합니다.",
                Body = "규칙이 걸린 칸은 모든 편집 화면에서 드롭다운이 되고, ID를 바꾸면 값이 함께 바뀌며, 검증도 이 규칙을 따릅니다.\n\n" +
                       "## 방법\n" +
                       "- 필드 규칙을 만들고 클래스와 필드를 고릅니다.\n" +
                       "- 값 출처를 고르고, 선택지 목록이나 도감 종류를 지정합니다.\n" +
                       "- 비면 안 되는 값이면 '필수'를 켭니다.\n\n" +
                       "아래 상태 줄의 '규칙 없는 string 필드'는 데이터 클래스·이벤트 중 아직 규칙이 없는 식별용 필드 후보입니다. 자유 입력이 맞는 필드라면 그대로 둡니다.",
                HelpTopic = HelpIds.Data(typeof(FieldRuleData)),
                Check = () =>
                {
                    var rules = Count<FieldRuleData>();
                    var candidates = RuleCandidates().Count;
                    return (rules > 0 && candidates == 0, $"규칙 {rules}개" + (candidates > 0 ? $" · 규칙 없는 string 필드 {candidates}개" : ""));
                },
                Actions = { Create<FieldRuleData>(), Open<FieldRuleData>(), new SetupAction("후보 필드 보기", ShowCandidates) },
            };
            yield return new SetupStep
            {
                Id = "state-keys", Order = 40, Title = "상태 키 정의",
                Summary = "게임 중 바뀌는 값(골드, 인벤토리, 진행도 등)의 이름과 형식을 정합니다.",
                Body = "상태 키는 저장소에 담을 값 하나입니다. 여기서 만든 키는 게임이 시작될 때 자동으로 등록되고 저장·Undo·모니터에 자동으로 포함됩니다.\n\n" +
                       "## 정할 것\n" +
                       "- ID: 코드에서 GameIds.State.XXX로 쓰입니다.\n" +
                       "- 구역: 이번 판(새 판에서 초기화) 또는 프로필(계속 유지).\n" +
                       "- 값 형식: 숫자·글자 같은 단순 값이나 프로젝트의 클래스·리스트·딕셔너리.",
                HelpTopic = HelpIds.Data(typeof(StateKeyData)),
                Check = () => (Count<StateKeyData>() > 0, $"상태 키 {Count<StateKeyData>()}개"),
                Actions = { Create<StateKeyData>(), Open<StateKeyData>() },
            };
            yield return new SetupStep
            {
                Id = "sprites", Order = 50, Title = "스프라이트 규격",
                Summary = "원본 일러에서 잘라 낼 크기(스탠딩·아이콘 등)를 프로젝트 단위로 정합니다.",
                Body = "규격 묶음을 만들고, 묶음 안에 규격(가로·세로 픽셀)을 추가합니다. 그다음 필드 규칙에서 스프라이트 필드에 묶음과 미리보기 규격을 연결합니다.\n\n" +
                       "이후 그 필드에 원본을 넣으면 맞춤 창이 열리고, 중심과 배율을 한 번 정하면 묶음의 모든 규격이 만들어집니다. 규격 크기를 바꾸면 자동으로 다시 만들어집니다.\n\n" +
                       "이미지 크기 관리가 필요 없는 프로젝트라면 건너뛰어도 됩니다.",
                HelpTopic = HelpIds.Data(typeof(SpritePresetGroupData)),
                Optional = true,
                Check = () =>
                {
                    var groups = Count<SpritePresetGroupData>();
                    var presets = Count<SpritePresetData>();
                    var linked = EditorDatabase.Instance.OfType<FieldRuleData>().Count(r => !string.IsNullOrEmpty(r.spriteGroup));
                    return (groups > 0 && presets > 0 && linked > 0, $"묶음 {groups}개 · 규격 {presets}개 · 연결된 필드 {linked}개");
                },
                Actions = { Create<SpritePresetGroupData>(), Open<SpritePresetData>("규격 열기") },
            };
            yield return new SetupStep
            {
                Id = "stats", Order = 60, Title = "수치 정의",
                Summary = "처치 수·획득 골드처럼 이벤트에서 자동으로 집계할 값을 만듭니다.",
                Body = "수치는 반영 규칙에 따라 이벤트가 발생할 때 자동으로 올라갑니다. 게임 코드는 이벤트만 발생시키면 됩니다.\n\n" +
                       "## 정할 것\n" +
                       "- 범위: 이번 판 또는 누적.\n" +
                       "- 집계 방식: 합계·최고치·최저치·마지막 값.\n" +
                       "- 반영 규칙: 이벤트, 반영할 값(비우면 횟수), 배수, 조건.",
                HelpTopic = HelpIds.Data(typeof(StatData)),
                Optional = true,
                Check = () => (Count<StatData>() > 0, $"수치 {Count<StatData>()}개"),
                Actions = { Create<StatData>(), Open<StatData>() },
            };
            yield return new SetupStep
            {
                Id = "achievements", Order = 70, Title = "도전과제 정의",
                Summary = "조건과 보상을 조립합니다. 조건을 만족하는 순간 달성됩니다.",
                Body = "조건은 블록으로 조립합니다. AND·OR·NOT 블록 안에 다른 블록을 넣어 원하는 식을 만듭니다. 수치 비교, 상태 값 검사, 다른 도전과제 달성 여부를 조건으로 쓸 수 있습니다.\n\n" +
                       "달성과 보상은 즉시 적용되며 Undo로 되돌리지 않습니다.",
                HelpTopic = HelpIds.Data(typeof(AchievementData)),
                Optional = true,
                Check = () => (Count<AchievementData>() > 0, $"도전과제 {Count<AchievementData>()}개"),
                Actions = { Create<AchievementData>(), Open<AchievementData>() },
            };
            yield return new SetupStep
            {
                Id = "codex", Order = 80, Title = "템플릿·프리팹 등록",
                Summary = "테스트용 인스턴스(템플릿)와 조립에 쓸 프리팹을 도감에 등록합니다.",
                Body = "템플릿은 프로젝트 클래스에 값을 미리 채운 인스턴스입니다. 더미 아이템이나 테스트 몬스터 설정을 만들어 두고 코드에서 사본을 받아 씁니다.\n\n" +
                       "프리팹을 등록하면 도감에서 컴포넌트 필드를 탐색기로 보고 고칠 수 있습니다. 프리팹 항목은 도감에서 지우지 않고, 프로젝트 창에서 에셋을 지우면 자동으로 정리됩니다.",
                HelpTopic = HelpIds.Data(typeof(TemplateData)),
                Optional = true,
                Check = () => (Count<TemplateData>() + Count<PrefabData>() > 0, $"템플릿 {Count<TemplateData>()}개 · 프리팹 {Count<PrefabData>()}개"),
                Actions = { Create<TemplateData>(), Open<PrefabData>("프리팹 열기") },
            };
            yield return new SetupStep
            {
                Id = "game-ids", Order = 90, Title = "식별자 코드 확인",
                Summary = "도감의 ID가 코드에서 쓸 수 있게 GameIds.cs로 만들어졌는지 확인합니다.",
                Body = "GameIds.cs는 도감이 바뀔 때마다 자동으로 다시 만들어집니다. 코드에서는 문자열 대신 `GameIds.State.Gold`처럼 씁니다.\n\n" +
                       "파일이 없거나 오래됐다면 '다시 만들기'를 누릅니다.",
                HelpTopic = "guide:ids",
                Check = () =>
                {
                    var found = AssetDatabase.FindAssets("GameIds t:MonoScript").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileName(p) == GameIdsGenerator.FileName);
                    return (found != null, found != null ? found : "아직 만들어지지 않았습니다");
                },
                Actions = { new SetupAction("다시 만들기", () => EditorApplication.ExecuteMenuItem("Game Systems/식별자 코드 다시 만들기"), true) },
            };
            yield return new SetupStep
            {
                Id = "validation", Order = 100, Title = "검증 오류 해결",
                Summary = "도감 전체의 오류를 없앱니다. 오류가 있으면 빌드가 막힙니다.",
                Body = "끊긴 참조, 빠진 필수 값, ID 형식 등을 검사합니다. 도감 검색 칸에 `invalid`를 입력하면 오류가 있는 항목만 보입니다.",
                HelpTopic = "guide:validation",
                Check = () =>
                {
                    var issues = DataValidator.ValidateAll();
                    var errors = issues.Count(i => i.Severity == ValidationSeverity.Error);
                    var warnings = issues.Count(i => i.Severity == ValidationSeverity.Warning);
                    return (errors == 0, errors > 0 ? $"오류 {errors}건" : warnings > 0 ? $"오류 없음 · 경고 {warnings}건" : "검증 통과");
                },
                Actions = { new SetupAction("오류 항목 보기", () => DatabaseWindow.ShowInvalid()) },
            };
            yield return new SetupStep
            {
                Id = "monitor", Order = 110, Title = "플레이하며 확인",
                Summary = "플레이 모드에서 모니터 창으로 값과 기록이 제대로 쌓이는지 확인합니다.",
                Body = "플레이를 시작하고 모니터 창을 엽니다.\n\n" +
                       "- 데이터 탭에서 상태 키가 모두 보이는지, '빈 필수 값'이 없는지 봅니다.\n" +
                       "- 게임을 조작하면서 타임라인에 행동과 값 변경이 쌓이는지 봅니다.\n" +
                       "- 이벤트 탭에서 이벤트를 발생시켜 수치와 도전과제가 반응하는지 봅니다.\n\n" +
                       "확인했으면 '완료로 표시'를 누릅니다.",
                HelpTopic = "window:monitor",
                Actions = { new SetupAction("모니터 열기", MonitorWindow.Open, true) },
            };
        }

        // 규칙이 없는 식별용 string 필드 후보(데이터 클래스·이벤트만. 도감 종류의 id·이름 등은 제외).
        internal static List<(Type type, System.Reflection.FieldInfo field)> RuleCandidates()
        {
            var result = new List<(Type, System.Reflection.FieldInfo)>();
            foreach (var type in ClassTypes.All())
            {
                if (typeof(GameData).IsAssignableFrom(type)) continue;
                foreach (var field in ClassTypes.FieldsOf(type))
                    if (field.FieldType == typeof(string) && EditorDatabase.RuleFor(field) == null)
                        result.Add((type, field));
            }
            return result;
        }

        static void ShowCandidates()
        {
            var list = RuleCandidates();
            var text = list.Count == 0
                ? "규칙이 없는 string 필드가 없습니다."
                : string.Join("\n", list.Take(40).Select(c => $"{ClassTypes.Label(c.type)} › {c.field.Name}")) + (list.Count > 40 ? $"\n… 외 {list.Count - 40}개" : "");
            EditorUtility.DisplayDialog("규칙 없는 string 필드", text, "확인");
        }
    }
}
