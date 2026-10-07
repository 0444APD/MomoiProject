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
    //   에디터: Assets/MomoiProject/Data/Tests/<이름>.json(프로젝트와 함께 커밋)
    //   개발 빌드: 저장·지우기는 <persistentDataPath>/MomoiProject/Tests/. 불러오기는 거기와, 빌드에 같이 넣은 상황(StreamingAssets/MomoiProject/Tests, 읽기 전용)에서
    //             — 개발 빌드를 만들 때 에디터(TestScenarioBuild)가 Assets/MomoiProject/Data/Tests의 상황을 넣는다. 출시 빌드에는 넣지 않는다. 같은 이름이면 기기에 저장한 것.
    //   TestScenarios.Save(session, "보스 직전");  TestScenarios.Load(session, "보스 직전", seed: 7);
    // 테스트 씬(TestScene)이 시작할 때 불러오고, 테스트 패널·모니터에서 저장·불러오기를 한다. 행동 밖에서 부른다.
    public static class TestScenarios
    {
        public const string Kind = "testScenario";

        // 빌드에 같이 넣는 상황의 자리(StreamingAssets 아래, '/'로 나눔). 에디터의 빌드 처리기가 개발 빌드에만 여기로 넣는다.
        public const string BundledPath = "MomoiProject/Tests";

        // 저장하는 곳(Save·Delete·SaveReport).
        public static string Folder => Application.isEditor
            ? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Assets", "MomoiProject", "Data", "Tests")
            : Path.Combine(Application.persistentDataPath, "MomoiProject", "Tests");

        // 개발 빌드에 같이 넣은 상황(읽기 전용). 에디터·출시 빌드는 null.
        // StreamingAssets가 파일로 열리지 않는 플랫폼(Android·WebGL)에서는 찾지 못한다(그곳은 기기에 저장한 상황만).
        public static string BundledFolder => Application.isEditor || !Debug.isDebugBuild
            ? null
            : Path.Combine(Application.streamingAssetsPath, BundledPath.Replace('/', Path.DirectorySeparatorChar));

        // 저장할 파일(Folder 안).
        public static string PathOf(string name) => Path.Combine(Folder, Clean(name) + ".json");
        public static bool Exists(string name) => ReadPathOf(name) != null;

        // 불러올 파일: 저장한 것이 먼저, 없으면 빌드에 같이 넣은 것. 둘 다 없으면 null.
        static string ReadPathOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var saved = PathOf(name);
            if (File.Exists(saved)) return saved;
            var bundled = BundledFolder;
            if (bundled == null) return null;
            var path = Path.Combine(bundled, Clean(name) + ".json");
            return File.Exists(path) ? path : null;
        }

        // 저장된 상황 이름(가나다순, 빌드에 같이 넣은 것 포함 — 같은 이름은 하나).
        public static List<string> Names()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var folder in new[] { Folder, BundledFolder })
            {
                if (folder == null || !Directory.Exists(folder)) continue;
                try
                {
                    foreach (var file in Directory.GetFiles(folder, "*.json")) names.Add(Path.GetFileNameWithoutExtension(file));
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // 읽지 못하는 폴더는 건너뛴다(목록 전체가 실패하지 않게).
                }
            }
            return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
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
        // 없거나 읽지 못하면(형식이 틀림·저장한 뒤 데이터 구조가 바뀜) 저널에 남기고 거짓 — 던지지 않는다(테스트 씬은 새 판으로 간다).
        public static bool Load(GameSession session, string name, ulong? seed = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (session.Data.InTransaction) throw new InvalidOperationException("[MomoiProject] 행동 실행 중에는 테스트 상황을 불러올 수 없습니다. 행동 밖(버튼 등)에서 부르세요.");
            var path = ReadPathOf(name);
            if (path == null)
            {
                var bundled = BundledFolder;
                session.Journal.Append(JournalKind.Warning, "Test", $"테스트 상황 '{name}'이 없습니다({PathOf(name ?? "")}{(bundled != null ? " · 빌드에 넣은 상황 " + bundled : "")}).");
                return false;
            }
            Dictionary<string, object> root;
            try { root = MiniJson.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>; }
            catch (Exception e)
            {
                session.Journal.Append(JournalKind.Error, "Test", $"테스트 상황 '{name}'을(를) 읽지 못했습니다({path}): {e.Message}");
                return false;
            }
            if (root == null || !(root.TryGetValue("run", out var run) && run is string runJson))
            {
                session.Journal.Append(JournalKind.Error, "Test", $"테스트 상황 '{name}'의 형식이 올바르지 않습니다.");
                return false;
            }
            try
            {
                if (root.TryGetValue("meta", out var meta) && meta is string metaJson) session.ImportMeta(metaJson);
                session.ImportRun(runJson);
            }
            catch (Exception e)
            {
                session.Journal.Append(JournalKind.Error, "Test", $"테스트 상황 '{name}'을(를) 불러오지 못했습니다(저장한 뒤 데이터 구조가 바뀌었으면 다시 저장하세요): {e.Message}");
                return false;
            }
            if (seed.HasValue) session.Random.Reseed(seed.Value);
            session.Journal.Append(JournalKind.System, "Test", $"테스트 상황 불러옴: {name}" + (seed.HasValue ? $" (시드 {seed.Value})" : ""));
            GBibleRuntime.NotifyRunLoaded(session);
            return true;
        }

        // 저장한 상황만 지운다(빌드에 같이 넣은 상황은 읽기 전용이라 거짓).
        public static bool Delete(string name)
        {
            if (string.IsNullOrEmpty(name) || !File.Exists(PathOf(name))) return false;
            File.Delete(PathOf(name));
            var meta = PathOf(name) + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
            RefreshAssets();
            return true;
        }

        // ── 자동 시뮬레이션(밸런싱) ───────────────────────────────
        // 같은 상황을 시드만 바꿔 runs번 돌린다. 한 번마다: 상황을 불러오고(seed = firstSeed + i) → step이 거짓을 돌려주거나 maxSteps가 될 때까지
        // step(session)을 부르고(한 번 = 플레이어의 한 수: 프로젝트가 행동을 골라 Game.Do) → measure(session)으로 숫자를 잰다.
        // step·measure가 던지면 그 회차는 Failures로 세고(저널에 이유) 다음 회차로 간다. 회차마다(실패해도) 화면이 쌓은 연출을 비우고,
        // 끝나면(도중에 던져도) 상황을 처음 시드로 다시 불러 둔다. 턴제에 맞다(실시간은 step이 화면 판정을 근사해야 한다).
        //   var report = TestScenarios.Simulate(session, "보스 직전", 200, s => AutoPlay.Step(s), s => new Dictionary<string, double> { ["win"] = Won(s) ? 1 : 0, ["turns"] = Turns(s) });
        //   Debug.Log(report.ToText());  TestScenarios.SaveReport(report);
        public static TestReport Simulate(GameSession session, string scenario, int runs, Func<GameSession, bool> step,
            Func<GameSession, IReadOnlyDictionary<string, double>> measure, int maxSteps = 1000, ulong firstSeed = 1)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (step == null) throw new ArgumentNullException(nameof(step));
            if (measure == null) throw new ArgumentNullException(nameof(measure));
            var report = new TestReport { Scenario = scenario };
            var loaded = false;
            try
            {
                for (int i = 0; i < Math.Max(1, runs); i++)
                {
                    var seed = firstSeed + (ulong)i;
                    if (!Load(session, scenario, seed)) break;
                    loaded = true;
                    var steps = 0;
                    var measuring = false;
                    try
                    {
                        while (steps < maxSteps && step(session)) steps++;
                        measuring = true;
                        var row = new Dictionary<string, double>(StringComparer.Ordinal) { ["seed"] = seed, ["steps"] = steps };
                        foreach (var pair in measure(session) ?? new Dictionary<string, double>()) row[pair.Key] = pair.Value;
                        report.Runs.Add(row);
                    }
                    catch (Exception e)
                    {
                        session.Journal.Append(JournalKind.Error, "Test", measuring
                            ? $"시뮬레이션 {i + 1}번째(시드 {seed})의 측정(measure)이 {steps}수 뒤에 실패했습니다: {e.Message}"
                            : $"시뮬레이션 {i + 1}번째(시드 {seed})가 {steps}수에서 멈췄습니다: {e.Message}");
                        report.Failures++;
                    }
                    finally
                    {
                        if (session == GBibleRuntime.Session) GameView.ClearPresentation();
                    }
                }
            }
            finally
            {
                // 한 번이라도 불러왔으면 처음 시드의 상황으로 되돌려 둔다(시뮬레이션이 끝난 판에 남지 않게).
                if (loaded) Load(session, scenario, firstSeed);
            }
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
        // step·measure가 던진 회차 수(그 회차는 Runs에 없다, 이유는 저널).
        public int Failures;

        public List<string> Columns => Runs.SelectMany(r => r.Keys).Distinct().ToList();
        public double Average(string column) => Values(column).DefaultIfEmpty(0).Average();
        public double Min(string column) => Values(column).DefaultIfEmpty(0).Min();
        public double Max(string column) => Values(column).DefaultIfEmpty(0).Max();

        IEnumerable<double> Values(string column) => Runs.Where(r => r.ContainsKey(column)).Select(r => r[column]);

        public string ToText()
        {
            var text = new StringBuilder();
            text.AppendLine($"시뮬레이션 '{Scenario}': {Runs.Count}번" + (Failures > 0 ? $"(실패 {Failures} — 저널)" : ""));
            foreach (var column in Columns.Where(c => c != "seed"))
                text.AppendLine($"  {column}: 평균 {Average(column):0.###} · 최소 {Min(column):0.###} · 최대 {Max(column):0.###}");
            return text.ToString();
        }

        // RFC 4180: 줄은 CRLF로 나누고, 쉼표·큰따옴표·줄바꿈이 든 칸은 큰따옴표로 감싸며 안의 큰따옴표는 둘로 쓴다(measure의 열 이름에 무엇이 들어도 칸이 밀리지 않게).
        public string ToCsv()
        {
            var columns = Columns;
            var text = new StringBuilder();
            text.Append(string.Join(",", columns.Select(CsvField))).Append("\r\n");
            foreach (var row in Runs)
                text.Append(string.Join(",", columns.Select(c => row.TryGetValue(c, out var v) ? CsvField(v.ToString("R", CultureInfo.InvariantCulture)) : ""))).Append("\r\n");
            return text.ToString();
        }

        static readonly char[] CsvSpecial = { ',', '"', '\r', '\n' };

        static string CsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            return field.IndexOfAny(CsvSpecial) < 0 ? field : "\"" + field.Replace("\"", "\"\"") + "\"";
        }
    }
}
