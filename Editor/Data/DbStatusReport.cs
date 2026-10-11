using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;

namespace GBible.EditorTools
{
    // AI가 읽는 DB 상태 파일: Logs/MomoiProject/DbStatus.txt — 검증 오류·경고와 '적용 전' 변경.
    // AI는 Unity 창(DB 사이드바의 '경고 n건')을 볼 수 없으므로, 에디터가 다시 읽을 때(컴파일 뒤)와 DB가 바뀔 때(1초 모아서) 이 파일을 다시 쓴다.
    // DB 창이 닫혀 있어도 된다. 시각 줄을 뺀 내용이 같으면 쓰지 않는다. 콘솔에는 남기지 않는다(경고가 콘솔을 덮지 않게).
    [InitializeOnLoad]
    public static class DbStatusReport
    {
        public const string Path = "Logs/MomoiProject/DbStatus.txt";
        const double Delay = 1.0;

        static double _due = -1;
        static string _lastBody;

        static DbStatusReport()
        {
            EditorDatabase.Changed += Queue;
            EditorApplication.update += Tick;
            Queue();
        }

        static void Queue() => _due = EditorApplication.timeSinceStartup + Delay;

        static void Tick()
        {
            if (_due < 0 || EditorApplication.timeSinceStartup < _due) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Queue();
                return;
            }
            _due = -1;
            Write();
        }

        // 지금 상태로 파일을 쓴다(바뀐 것이 없으면 그대로). 쓴 내용을 돌려준다.
        public static string Write()
        {
            if (!File.Exists(EditorDatabase.DatabaseAssetPath)) return null;
            var body = new StringBuilder();
            try
            {
                var issues = DataValidator.ValidateAll();
                var errors = issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
                var warnings = issues.Where(i => i.Severity == ValidationSeverity.Warning).ToList();
                body.AppendLine(errors.Count > 0 ? $"상태: 검증 오류 {errors.Count}건(빌드가 막힌다) · 경고 {warnings.Count}건"
                    : warnings.Count > 0 ? $"상태: 경고 {warnings.Count}건" : "상태: 검증 통과");
                Section(body, "오류", errors.Select(Line));
                Section(body, "경고", warnings.Select(Line));
                var pending = EditorDatabase.Staging.Changes();
                Section(body, "적용 전 변경(Ctrl+S를 기다림)", pending.Select(Pending));
            }
            catch (Exception e)
            {
                body.AppendLine("상태를 만들지 못했습니다: " + e.Message);
            }
            var text = body.ToString();
            if (text == _lastBody && File.Exists(Path)) return text;
            _lastBody = text;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.WriteAllText(Path, $"# DB 상태 — {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" + text, new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 다른 프로그램이 파일을 잡고 있으면 다음 변경 때 다시 쓴다.
                _lastBody = null;
            }
            return text;
        }

        static void Section(StringBuilder body, string title, IEnumerable<string> lines)
        {
            var list = lines.ToList();
            body.AppendLine();
            body.AppendLine($"## {title} {list.Count}");
            foreach (var line in list) body.AppendLine("- " + line);
        }

        static string Line(ValidationIssue issue)
        {
            var kind = issue.Asset != null ? DataTypes.Find(issue.Asset.GetType())?.DisplayName ?? issue.Asset.GetType().Name : "DB";
            var id = issue.Asset != null ? issue.Asset.Id : "";
            return $"[{kind}] {(string.IsNullOrEmpty(id) ? "" : id + ": ")}{issue.Message.Replace("\n", " ")}";
        }

        static string Pending(PendingChange change)
        {
            var kind = change.Item != null ? DataTypes.Find(change.Item.GetType())?.DisplayName ?? change.Item.GetType().Name : "";
            var what = change.Kind == PendingKind.Added ? "새 항목" : change.Kind == PendingKind.Removed ? "지움" : "고침";
            var fields = change.Fields.Count == 0 ? "" : " — " + string.Join(", ", change.Fields.Take(6).Select(f => $"{f.Label ?? f.Path}: {f.Old} → {f.New}")) + (change.Fields.Count > 6 ? " …" : "");
            return $"{what} · [{kind}] {change.Label ?? change.Item?.Id}{fields}";
        }
    }
}
