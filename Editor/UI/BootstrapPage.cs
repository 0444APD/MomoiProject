using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GBible.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools.UI
{
    // DB 사이드바 맨 위 '시작 파일' 그룹: 프로젝트마다 하나씩 있는 시작 코드로 바로 가는 곳.
    //   Bootstrap — 이 화면. 세션 옵션(Undo 방식·보관 수·시드)·판 저장·고급 설정을 고치고, Bootstrap.cs를 연다.
    //   RunState·Profile — 탭이 아니라 누르면 바로 코드 파일을 연다(DatabaseWindow가 이 그룹 아래에 붙인다).
    // 세션 옵션은 GameDatabase 에셋에 바로 저장된다(작업본·Ctrl+S를 거치지 않는다). 게임이 켜질 때 SessionOptions에 먼저 들어가고,
    // Bootstrap 코드의 Configure가 바꾸면 그 값이 우선한다(바꾼 것은 모니터 타임라인에 남는다).
    public sealed class BootstrapPage : CodexPage
    {
        public const string PageKey = "bootstrap";
        public const string Group = "시작 파일";

        public override string Key => PageKey;
        public override string TabLabel => "Bootstrap";
        public override string TabGroup => Group;
        public override string CreateLabel => null;

        public override string Caption(CodexPageContext context) =>
            "게임이 켜질 때의 세션 옵션. 고치면 바로 저장된다. 코드(Bootstrap의 Configure)가 같은 값을 바꾸면 코드가 이긴다.";

        public override void Create(CodexPageContext context)
        {
        }

        static readonly string[] HistoryNames = { "행동 단위(턴제)", "스냅샷(실시간)", "쓰지 않음" };

        public override void Build(CodexPageContext context)
        {
            var root = Gs.Col();
            root.style.paddingLeft = 4;
            root.style.paddingRight = 4;
            root.style.maxWidth = 640;
            context.Content.Add(root);

            var file = StartFiles.BootstrapFile();
            var tools = Gs.Row("gs-wrap");
            tools.style.marginBottom = 12;
            if (file != null)
            {
                var open = new GsButton("cs 열기", () => StartFiles.Open(file), GsButtonKind.Primary);
                open.tooltip = file;
                tools.Add(open);
                tools.Add(Gs.HSpace(8));
                tools.Add(Gs.Label(file, "gs-caption"));
            }
            else
            {
                tools.Add(Gs.Label("Bootstrap.cs가 없습니다. 메뉴 MomoiProject › 시작 설정에서 '적용'하면 Assets/MomoiProject/Code/Client/Bootstrap.cs가 만들어집니다.", "gs-caption"));
            }
            root.Add(tools);

            var database = AssetDatabase.LoadAssetAtPath<GameDatabase>(EditorDatabase.DatabaseAssetPath);
            if (database == null)
            {
                root.Add(Gs.Label("GameDatabase가 아직 없습니다. 시작 설정을 적용하거나 DB에 항목을 하나 만들면 생깁니다.", "gs-caption"));
                return;
            }

            var events = TypeCache.GetTypesWithAttribute<GameEventAttribute>()
                .Where(t => !t.IsAbstract && ClassCatalog.IsDeveloperClass(t))
                .OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
            var undoBoundary = database.UndoBoundaryEvent ?? "";
            var continueAtStart = database.ContinueOnStart;
            var newRun = database.NewRunAction ?? "";
            void SaveFlow()
            {
                Undo.RecordObject(database, "시작 흐름 설정 변경");
                database.EditorSetFlowOptions(undoBoundary, continueAtStart, newRun);
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            }

            var history = database.History;
            var historyLimit = database.HistoryLimit;
            var interval = database.SnapshotInterval;
            var snapshots = database.SnapshotLimit;
            var fixSeed = database.FixSeedInDevelopment;
            var seed = database.DevelopmentSeed;

            void Save()
            {
                Undo.RecordObject(database, "세션 옵션 변경");
                database.EditorSetSessionOptions(history, historyLimit, interval, snapshots, fixSeed, seed);
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            }

            Heading(root, "Undo 방식", "턴제·카드·퍼즐은 행동 단위, 실시간 액션은 스냅샷. 게임 코드는 어느 방식이든 session.Undo() 하나로 되돌린다.");
            var mode = new DropdownField(HistoryNames.ToList(), (int)history);
            root.Add(mode);

            var actionsBox = Gs.Col();
            var limitField = new IntegerField("Undo 보관 수") { value = historyLimit };
            limitField.tooltip = "되돌릴 수 있는 최대 행동 수. 0이면 제한 없음(행동마다 바뀐 값의 이전 모습을 보관하므로 오래 플레이하면 메모리가 는다).";
            limitField.RegisterValueChangedCallback(e => { historyLimit = Math.Max(0, e.newValue); Save(); });
            actionsBox.Add(limitField);
            root.Add(actionsBox);

            var snapshotBox = Gs.Col();
            var intervalField = new DoubleField("스냅샷 간격(초)") { value = interval };
            intervalField.tooltip = "게임 시간 몇 초마다 스냅샷을 찍을지.";
            intervalField.RegisterValueChangedCallback(e => { interval = e.newValue > 0 ? e.newValue : 1.0; Save(); });
            snapshotBox.Add(intervalField);
            var snapshotField = new IntegerField("스냅샷 보관 수") { value = snapshots };
            snapshotField.tooltip = "되감을 수 있는 스냅샷 개수. 간격 × 개수 = 되감을 수 있는 시간.";
            snapshotField.RegisterValueChangedCallback(e => { snapshots = Math.Max(1, e.newValue); Save(); });
            snapshotBox.Add(snapshotField);
            root.Add(snapshotBox);

            void ShowModeFields()
            {
                actionsBox.style.display = history == HistoryMode.Actions ? DisplayStyle.Flex : DisplayStyle.None;
                snapshotBox.style.display = history == HistoryMode.Snapshots ? DisplayStyle.Flex : DisplayStyle.None;
            }
            mode.RegisterValueChangedCallback(_ => { history = (HistoryMode)mode.index; ShowModeFields(); Save(); });
            ShowModeFields();
            var boundaryField = TypePicker("Undo 끊는 이벤트(선택)", events, t => $"{EventBus.DisplayNameOf(t)} ({t.Name})", undoBoundary, value => { undoBoundary = value; SaveFlow(); });
            boundaryField.tooltip = "이 이벤트를 쏜 행동이 끝나면 Undo 기록을 비운다. 예: 턴 끝 이벤트 → 이번 턴 안에서만 되돌릴 수 있다. 비우면 끝없이(보관 수까지) 되돌린다.";
            root.Add(boundaryField);

            Heading(root, "시드", "같은 시드에 같은 행동이면 Game.Random의 결과가 같아 버그를 같은 순서로 재현할 수 있다. 에디터 플레이는 개발 빌드로 친다. 출시 빌드는 늘 지금 시각을 시드로 쓴다.");
            var fixToggle = new Toggle("개발 빌드에서 시드 고정") { value = fixSeed };
            root.Add(fixToggle);
            var seedField = new LongField("시드") { value = seed };
            seedField.RegisterValueChangedCallback(e => { seed = Math.Max(0, e.newValue); Save(); });
            seedField.SetEnabled(fixSeed);
            root.Add(seedField);
            fixToggle.RegisterValueChangedCallback(e => { fixSeed = e.newValue; seedField.SetEnabled(fixSeed); Save(); });

            // ── 판 저장 ──
            var runSave = database.RunSave;
            var endEvent = database.RunEndEvent ?? "";
            var profileEach = database.SaveProfileEachAction;
            void SaveRunOptions()
            {
                Undo.RecordObject(database, "판 저장 설정 변경");
                database.EditorSetSaveOptions(runSave, endEvent, profileEach);
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            }

            Heading(root, "판 저장", "진행 중인 판(이어 하기)을 언제 저장할지. 프로필은 이 설정과 상관없이 끌 때 늘 저장된다.");
            var saveMode = new DropdownField(RunSaveNames.ToList(), (int)runSave);
            root.Add(saveMode);
            var saveInterval = database.RunSaveInterval;
            var saveEvent = database.RunSaveEvent ?? "";
            void SaveTiming()
            {
                Undo.RecordObject(database, "판 저장 설정 변경");
                database.EditorSetSaveTiming(saveInterval, saveEvent);
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            }
            var saveIntervalField = new FloatField("저장 간격(초)") { value = saveInterval, tooltip = "판 데이터가 바뀐 뒤 이만큼 지나면 저장한다(실제 시간). 1 이상." };
            saveIntervalField.RegisterValueChangedCallback(e => { saveInterval = Math.Max(1f, e.newValue); SaveTiming(); });
            root.Add(saveIntervalField);
            var saveEventField = TypePicker("판 저장 이벤트", events, t => $"{EventBus.DisplayNameOf(t)} ({t.Name})", saveEvent, value => { saveEvent = value; SaveTiming(); });
            saveEventField.tooltip = "이 이벤트를 쏜 행동이 끝나면 판을 저장한다(예: 하루 끝, 체크포인트 도착, 방 클리어).";
            root.Add(saveEventField);
            void ShowTiming()
            {
                saveIntervalField.style.display = runSave == RunSaveMode.Interval ? DisplayStyle.Flex : DisplayStyle.None;
                saveEventField.style.display = runSave == RunSaveMode.OnEvent ? DisplayStyle.Flex : DisplayStyle.None;
            }
            ShowTiming();
            saveMode.RegisterValueChangedCallback(_ => { runSave = (RunSaveMode)saveMode.index; SaveRunOptions(); ShowTiming(); });

            var endField = TypePicker("판 끝 이벤트", events, t => $"{EventBus.DisplayNameOf(t)} ({t.Name})", endEvent, value => { endEvent = value; SaveRunOptions(); });
            endField.tooltip = "이 이벤트를 받으면 그 행동이 끝난 뒤 판 저장을 지우고 프로필을 바로 저장한다. 그 뒤 다른 행동이 판 데이터를 바꾸면(새 판) 다시 저장한다.";
            root.Add(endField);
            var profileField = new Toggle("프로필도 행동마다 저장") { value = profileEach };
            profileField.tooltip = "끄면 프로필은 끌 때와 판 끝 이벤트 때만 저장된다. 진행이 거의 프로필에 있는 게임(수집형·방치형)은 켠다.";
            profileField.RegisterValueChangedCallback(e => { profileEach = e.newValue; SaveRunOptions(); });
            root.Add(profileField);

            // ── 시작할 때 이어 하기(선택) ──
            Heading(root, "시작할 때 이어 하기(선택)", "타이틀 화면 없이 바로 판으로 들어가는 게임용. 게임이 켜질 때 저장된 판이 있으면 이어 하고, 없으면 새 판 행동을 실행한다. 타이틀 화면이 있는 게임은 끄고, 이어 하기 버튼에서 직접 부른다.");
            var actions = TypeCache.GetTypesDerivedFrom<GameAction>()
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && t.GetConstructor(Type.EmptyTypes) != null && ClassCatalog.IsDeveloperClass(t))
                .OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
            var continueField = new Toggle("시작할 때 이어 하기") { value = continueAtStart };
            root.Add(continueField);
            var newRunField = TypePicker("새 판 행동", actions, t => t.Name, newRun, value => { newRun = value; SaveFlow(); });
            newRunField.tooltip = "저장된 판이 없을 때 실행할 행동(매개변수 없는 생성자가 있는 행동 클래스). 판 끝 이벤트도 정해 두면 끝난 판은 지워져 있으므로 이어 하지 않는다.";
            newRunField.SetEnabled(continueAtStart);
            root.Add(newRunField);
            continueField.RegisterValueChangedCallback(e => { continueAtStart = e.newValue; newRunField.SetEnabled(continueAtStart); SaveFlow(); });

            // ── 시간(실시간) ──
            Heading(root, "고정 틱(실시간)", "초당 틱 수. 0이면 프레임마다(턴제·카드). 켜면 게임 시간이 일정한 간격으로 흐르고 예약·상태 효과·시간 조건·session.Ticked가 틱마다 돌아 프레임 속도와 상관없이 같은 결과가 난다.");
            var tickField = new IntegerField("초당 틱 수") { value = database.TickRate };
            tickField.tooltip = "예: 30이면 1/30초마다 한 틱. 0이면 끈다.";
            tickField.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(database, "고정 틱 변경");
                database.EditorSetTickRate(Math.Max(0, e.newValue));
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            });
            root.Add(tickField);

            // ── 씬 흐름(선택) ──
            var sceneKey = database.SceneKey ?? "";
            var scenePath = database.ScenePath ?? "";
            var emptyScene = database.EmptyScene ?? "";
            void SaveScenes()
            {
                Undo.RecordObject(database, "씬 흐름 변경");
                database.EditorSetSceneFlow(sceneKey, scenePath, emptyScene);
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            }
            Heading(root, "씬 흐름(선택)", "판 데이터의 필드(예: run › scene)가 가리키는 씬을 화면이 따라 연다. 규칙은 행동 안에서 그 필드만 바꾼다. 이어 하기로 판을 불러오면 그 씬이, 값이 비면 '빈 값일 때 씬'이 열린다.");
            var keys = new List<string> { "" };
            keys.AddRange(EditorDatabase.Instance.OfType<PlayerDataEntry>().Select(k => k.Id).OrderBy(k => k, StringComparer.Ordinal));
            var keyField = new DropdownField("플레이어 데이터", keys, Math.Max(0, keys.IndexOf(sceneKey)), k => string.IsNullOrEmpty(k) ? "(쓰지 않음)" : k, k => string.IsNullOrEmpty(k) ? "(쓰지 않음)" : k);
            keyField.RegisterValueChangedCallback(e => { sceneKey = e.newValue ?? ""; SaveScenes(); });
            root.Add(keyField);
            // isDelayed: 글자마다가 아니라 Enter·포커스를 잃을 때 한 번 저장한다.
            var pathField = new TextField("필드(씬 이름이 든 글자)") { value = scenePath, isDelayed = true };
            pathField.tooltip = "예: scene 또는 map.scene. 비우면 값 전체(글자 플레이어 데이터).";
            pathField.RegisterValueChangedCallback(e =>
            {
                var next = e.newValue?.Trim() ?? "";
                if (next != e.newValue) pathField.SetValueWithoutNotify(next);
                if (next == scenePath) return;
                scenePath = next;
                SaveScenes();
            });
            root.Add(pathField);
            var scenes = new List<string> { "" };
            scenes.AddRange(EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)));
            if (!scenes.Contains(emptyScene)) scenes.Add(emptyScene);
            var emptyField = new DropdownField("빈 값일 때 씬", scenes, Math.Max(0, scenes.IndexOf(emptyScene)), s => string.IsNullOrEmpty(s) ? "(그대로)" : s, s => string.IsNullOrEmpty(s) ? "(그대로)" : s);
            emptyField.tooltip = "새 판·판 끝으로 값이 비었을 때 열 씬(보통 타이틀). Build Settings의 씬 목록에서 고른다.";
            emptyField.RegisterValueChangedCallback(e => { emptyScene = e.newValue ?? ""; SaveScenes(); });
            root.Add(emptyField);

            // ── 고급(보통 그대로) ──
            var advanced = new Foldout { text = "고급", value = false };
            advanced.style.marginTop = 14;
            var journal = database.JournalCapacity;
            var detect = database.DetectUntrackedChanges;
            void SaveAdvanced()
            {
                Undo.RecordObject(database, "고급 설정 변경");
                database.EditorSetAdvanced(journal, detect);
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            }
            var journalField = new IntegerField("기록 보관 줄 수") { value = journal };
            journalField.tooltip = "모니터 기록(Journal)에 남기는 최대 줄 수. 넘치면 오래된 줄부터 지운다.";
            journalField.RegisterValueChangedCallback(e => { journal = Math.Max(64, e.newValue); SaveAdvanced(); });
            advanced.Add(journalField);
            var detectField = new Toggle("개발 빌드에서 Modify 누락 검사") { value = detect };
            detectField.tooltip = "예전 방식(Mirror를 쓰지 않는) 데이터를 행동 밖에서 고친 것을 찾는다. 값 전체를 비교하므로 출시 빌드에서는 늘 꺼진다.";
            detectField.RegisterValueChangedCallback(e => { detect = e.newValue; SaveAdvanced(); });
            advanced.Add(detectField);
            root.Add(advanced);

            Heading(root, "코드로 정하는 것", null);
            var note = Gs.Label("출시 뒤 저장 구조를 바꿨을 때의 변환(options.Migrations)만 코드로 등록한다. 저장 버전은 등록한 변환을 따라 저절로 올라간다. " +
                                "DB 항목 ID를 바꾼 것은 저장을 불러올 때 저절로 이어받는다([DataRef] 필드). " +
                                "위 값을 조건에 따라 바꿔야 하면 Bootstrap의 Configure에서 options를 고친다. 그때 바뀐 값은 플레이 중 모니터 타임라인에 \"코드(Configuring)가 … 바꿨습니다\"로 남는다.", "gs-text-2");
            note.style.whiteSpace = WhiteSpace.Normal;
            root.Add(note);
        }

        // 형식 고르기: "(없음)" + 목록. 저장된 이름이 목록에 없으면 "찾을 수 없음: …"을 보인다.
        static DropdownField TypePicker(string label, System.Collections.Generic.List<Type> types, Func<Type, string> name, string current, Action<string> set)
        {
            var choices = new[] { "(없음)" }.Concat(types.Select(name)).ToList();
            var index = types.FindIndex(t => t.FullName == current);
            if (index < 0 && !string.IsNullOrEmpty(current))
            {
                choices.Add($"찾을 수 없음: {current}");
                index = choices.Count - 2;
            }
            var field = new DropdownField(label, choices, index + 1);
            field.RegisterValueChangedCallback(_ =>
            {
                var picked = field.index - 1;
                if (picked >= types.Count) return;
                set(picked < 0 ? "" : types[picked].FullName);
            });
            return field;
        }

        static readonly string[] RunSaveNames = { "끌 때 저장", "행동마다 저장(턴제)", "저장하지 않음(이어 하기 없음)", "N초마다 저장(실시간·방치형)", "이벤트를 받을 때 저장(하루 끝·체크포인트)" };

        static void Heading(VisualElement root, string title, string caption)
        {
            root.Add(Gs.Space(14));
            root.Add(Gs.Label(title, "gs-heading"));
            if (string.IsNullOrEmpty(caption)) return;
            var label = Gs.Label(caption, "gs-caption");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginTop = 2;
            label.style.marginBottom = 6;
            root.Add(label);
        }
    }

    // 시작 파일(Bootstrap.cs, 판·프로필 데이터 클래스) 찾기와 열기.
    public static class StartFiles
    {
        // 판·프로필 바로가기: (보일 이름, 플레이어 데이터 ID, 시작 설정이 만든 클래스 이름).
        public static readonly (string label, string id, string className)[] DataShortcuts =
        {
            ("RunState", "run", "RunState"),
            ("Profile", "profile", "Profile"),
        };

        public static string BootstrapFile()
        {
            var made = StartSetup.FolderOf("Client") + "/Bootstrap.cs";
            if (File.Exists(made)) return made;
            foreach (var guid in AssetDatabase.FindAssets("Bootstrap t:MonoScript", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == "Bootstrap") return path;
            }
            return null;
        }

        // 플레이어 데이터 ID의 값 형식 → 시작 설정의 네임스페이스에서 같은 이름 → 시작 설정이 만든 파일 순서로 찾는다.
        // 클래스 이름을 바꿨어도 DB의 run·profile 항목이 그 클래스를 가리키면 따라간다.
        public static (string file, string typeName) DataFile(string id, string className)
        {
            var entry = EditorDatabase.Instance.All.OfType<PlayerDataEntry>().FirstOrDefault(e => e.Id == id);
            var type = entry?.ValueType;
            if (type != null && ClassCatalog.IsDeveloperClass(type) && ClassCatalog.FileOf(type.FullName) is string declared) return (declared, type.Name);
            var named = TypeResolver.Find(StartSetup.Prefix + "." + className);
            if (named != null && ClassCatalog.FileOf(named.FullName) is string file) return (file, named.Name);
            var made = StartSetup.FolderOf("Rules") + "/PlayerData.cs";
            return File.Exists(made) ? (made, className) : (null, className);
        }

        public static void Open(string path)
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null) AssetDatabase.OpenAsset(script);
            else Debug.LogWarning($"[MomoiProject] 스크립트를 열 수 없습니다: {path}");
        }
    }
}
