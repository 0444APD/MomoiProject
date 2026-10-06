using System.Text;
using GBible.Data;
using UnityEditor;

namespace GBible.EditorTools
{
    // 플레이 중에 DB › 밸런스 값·가중치 표를 고치면 실행 중인 세션에 바로 반영한다(다음에 읽을 때부터, 저장 전 값도).
    // 수치 조정이 플레이를 멈추지 않고 끝나도록. 0.5초마다 DB가 보는 모습(적용 대기 중인 변경 포함)을 보고, 바뀌었으면
    // 화면 세션과 멀티 시험 모드의 서버 세션에 다시 정의하고 화면을 다시 그린다.
    static class BalanceLiveReload
    {
        const double Interval = 0.5;
        static double _next;
        static string _last;
        static GameSession _session;

        [InitializeOnLoadMethod]
        static void Hook()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        static void Update()
        {
            if (!EditorApplication.isPlaying)
            {
                _last = null;
                _session = null;
                return;
            }
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + Interval;
            var session = GBibleRuntime.Session;
            if (session == null) return;
            var db = EditorDatabase.Instance;
            // 지금 모습(값·곡선·표 줄). 같으면 할 일이 없다 — 세션이 바뀌었으면(다시 시작·슬롯 바꾸기) 처음부터.
            var text = new StringBuilder();
            foreach (var balance in db.OfType<BalanceData>()) text.Append(EditorJsonUtility.ToJson(balance)).Append('\n');
            foreach (var table in db.OfType<WeightTableData>()) text.Append(EditorJsonUtility.ToJson(table)).Append('\n');
            var now = text.ToString();
            if (session == _session && now == _last) return;
            _session = session;
            _last = now;
            Apply(session, db);
            var server = GameNet.Server?.Session;
            if (server != null && server != session) Apply(server, db);
            GameView.RedrawAll();
        }

        static void Apply(GameSession session, EditorDatabase db)
        {
            foreach (var balance in db.OfType<BalanceData>()) balance.Define(session.Balance);
            foreach (var table in db.OfType<WeightTableData>()) session.Tables.Define(table.ToDefinition());
        }
    }
}
