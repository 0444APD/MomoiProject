using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace GBible
{
    // 테스트 상황: 지금 판(세션 구역·난수·게임 시각)과 프로필(메타 구역)을 이름 붙여 저장한 파일. 개발용.
    //   에디터: Assets/MomoiProjectData/Tests/<이름>.json(프로젝트와 함께 커밋) · 개발 빌드: <persistentDataPath>/MomoiProject/Tests/
    //   TestScenarios.Save(session, "보스 직전");  TestScenarios.Load(session, "보스 직전", seed: 7);
    // 테스트 씬(TestScene)이 시작할 때 불러오고, 테스트 패널·모니터에서 저장·불러오기를 한다. 행동 밖에서 부른다.
    public static class TestScenarios
    {
        public const string Kind = "testScenario";

        public static string Folder => Application.isEditor
            ? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Assets", "MomoiProjectData", "Tests")
            : Path.Combine(Application.persistentDataPath, "MomoiProject", "Tests");

        public static string PathOf(string name) => Path.Combine(Folder, Clean(name) + ".json");
        public static bool Exists(string name) => !string.IsNullOrEmpty(name) && File.Exists(PathOf(name));

        // 저장된 상황 이름(가나다순).
        public static List<string> Names()
        {
            if (!Directory.Exists(Folder)) return new List<string>();
            return Directory.GetFiles(Folder, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        public static void Save(GameSession session, string name)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("[MomoiProject] 테스트 상황 이름을 적으세요.");
            var root = new Dictionary<string, object>
            {
                ["kind"] = Kind,
                ["name"] = name.Trim(),
                ["savedAt"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["run"] = session.ExportRun(),
                ["meta"] = session.ExportMeta(),
            };
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathOf(name), MiniJson.Serialize(root));
            session.Journal.Append(JournalKind.System, "Test", $"테스트 상황 저장: {name}");
            RefreshAssets();
        }

        // 상황으로 판을 바꾼다(프로필 → 판 순서, Undo 이력은 비워진다). seed를 주면 불러온 뒤 난수를 그 시드로(같은 상황을 다른 운으로).
        public static bool Load(GameSession session, string name, ulong? seed = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (!Exists(name))
            {
                session.Journal.Append(JournalKind.Warning, "Test", $"테스트 상황 '{name}'이 없습니다({PathOf(name ?? "")}).");
                return false;
            }
            var root = MiniJson.Deserialize(File.ReadAllText(PathOf(name))) as Dictionary<string, object>;
            if (root == null || !(root.TryGetValue("run", out var run) && run is string runJson))
            {
                session.Journal.Append(JournalKind.Error, "Test", $"테스트 상황 '{name}'의 형식이 올바르지 않습니다.");
                return false;
            }
            if (root.TryGetValue("meta", out var meta) && meta is string metaJson) session.ImportMeta(metaJson);
            session.ImportRun(runJson);
            if (seed.HasValue) session.Random.Reseed(seed.Value);
            session.Journal.Append(JournalKind.System, "Test", $"테스트 상황 불러옴: {name}" + (seed.HasValue ? $" (시드 {seed.Value})" : ""));
            GBibleRuntime.NotifyRunLoaded(session);
            return true;
        }

        public static bool Delete(string name)
        {
            if (!Exists(name)) return false;
            File.Delete(PathOf(name));
            var meta = PathOf(name) + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
            RefreshAssets();
            return true;
        }

        // ── 자동 시뮬레이션(밸런싱) ───────────────────────────────
        // 같은 상황을 시드만 바꿔 runs번 돌린다. 한 번마다: 상황을 불러오고(seed = firstSeed + i) → step이 거짓을 돌려주거나 maxSteps가 될 때까지
        // step(session)을 부르고(한 번 = 플레이어의 한 수: 프로젝트가 행동을 골라 Game.Do) → measure(session)으로 숫자를 잰다.
        // 끝나면 상황을 처음 시드로 다시 불러 둔다. 턴제에 맞다(실시간은 step이 화면 판정을 근사해야 한다).
        //   var report = TestScenarios.Simulate(session, "보스 직전", 200, s => AutoPlay.Step(s), s => new Dictionary<string, double> { ["win"] = Won(s) ? 1 : 0, ["turns"] = Turns(s) });
        //   Debug.Log(report.ToText());  TestScenarios.SaveReport(report);
        public static TestReport Simulate(GameSession session, string scenario, int runs, Func<GameSession, bool> step,
            Func<GameSession, IReadOnlyDictionary<string, double>> measure, int maxSteps = 1000, ulong firstSeed = 1)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (measure == null) throw new ArgumentNullException(nameof(measure));
            var report = new TestReport { Scenario = scenario };
            for (int i = 0; i < Math.Max(1, runs); i++)
            {
                var seed = firstSeed + (ulong)i;
                if (!Load(session, scenario, seed)) break;
                var steps = 0;
                try
                {
                    while (steps < maxSteps && step(session)) steps++;
                }
                catch (Exception e)
                {
                    session.Journal.Append(JournalKind.Error, "Test", $"시뮬레이션 {i + 1}번째(시드 {seed})가 {steps}수에서 멈췄습니다: {e.Message}");
                    report.Failures++;
                    continue;
                }
                var row = new Dictionary<string, double>(StringComparer.Ordinal) { ["seed"] = seed, ["steps"] = steps };
                foreach (var pair in measure(session) ?? new Dictionary<string, double>()) row[pair.Key] = pair.Value;
                report.Runs.Add(row);
                if (session == GBibleRuntime.Session) GameView.ClearPresentation();
            }
            Load(session, scenario, firstSeed);
            return report;
        }

        public static string SaveReport(TestReport report, string name = null)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var folder = Path.Combine(Folder, "Reports");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, Clean(name ?? report.Scenario ?? "report") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");
            File.WriteAllText(path, report.ToCsv(), new UTF8Encoding(true));
            RefreshAssets();
            return path;
        }

        static string Clean(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string((name ?? "").Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }

        static void RefreshAssets()
        {
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }
    }

    // 자동 시뮬레이션 결과: 한 번마다 숫자 한 줄(seed·steps + measure가 잰 것), 그리고 평균·최소·최대.
    public sealed class TestReport
    {
        public string Scenario;
        public readonly List<Dictionary<string, double>> Runs = new List<Dictionary<string, double>>();
        public int Failures;

        public List<string> Columns => Runs.SelectMany(r => r.Keys).Distinct().ToList();
        public double Average(string column) => Values(column).DefaultIfEmpty(0).Average();
        public double Min(string column) => Values(column).DefaultIfEmpty(0).Min();
        public double Max(string column) => Values(column).DefaultIfEmpty(0).Max();

        IEnumerable<double> Values(string column) => Runs.Where(r => r.ContainsKey(column)).Select(r => r[column]);

        public string ToText()
        {
            var text = new StringBuilder();
            text.AppendLine($"시뮬레이션 '{Scenario}': {Runs.Count}번" + (Failures > 0 ? $"(멈춤 {Failures})" : ""));
            foreach (var column in Columns.Where(c => c != "seed"))
                text.AppendLine($"  {column}: 평균 {Average(column):0.###} · 최소 {Min(column):0.###} · 최대 {Max(column):0.###}");
            return text.ToString();
        }

        public string ToCsv()
        {
            var columns = Columns;
            var text = new StringBuilder();
            text.AppendLine(string.Join(",", columns));
            foreach (var row in Runs)
                text.AppendLine(string.Join(",", columns.Select(c => row.TryGetValue(c, out var v) ? v.ToString("R", CultureInfo.InvariantCulture) : "")));
            return text.ToString();
        }
    }
}
