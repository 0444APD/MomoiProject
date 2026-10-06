using System;
using System.Collections.Generic;
using System.Linq;

namespace GBible
{
    // 되감기 지점 하나: 그때의 세션 구역 플레이어 데이터(키 → 저장 트리, JSON).
    [Serializable]
    public sealed class RewindMark
    {
        public long serial;
        public string label;
        public string json;
    }

    [Serializable]
    public sealed class RewindMarks
    {
        public List<RewindMark> marks = new List<RewindMark>();
        public long nextSerial = 1;
    }

    // 되감기(규칙의 기믹): "n턴 전으로" · "이 유닛만 2턴 전 모습으로" · "이 플레이어 것만 되돌리기".
    //   턴이 시작될 때(턴 시작 행동 안에서): Game.MarkRewind("3턴");
    //   기믹 행동 안에서:  Game.Rewind(2);                    // 세션 구역 전체를 2지점 전 모습으로
    //                      Game.RewindKey("hand:2", 1);        // 플레이어 데이터 하나만
    //                      Game.RewindEntity(unit, 2);         // 객체 하나만(id로 찾는다) — 그 객체에 남이 한 일(피해 등)도 함께 돌아가고, 그 객체가 남에게 한 일은 남는다
    // 되감기도 하나의 행동 안의 변경이라 Undo·판 저장·멀티플레이 동기화·리플레이에 그대로 남는다(Undo와 다르다: Undo는 행동을 이력에서 지운다).
    // 지점은 플레이어 데이터 '$rewind'(세션 구역, 서버만 — 클라이언트에 보내지 않는다)에 남아 판 저장에도 들어간다.
    // 난수 상태는 되감지 않는다(되감은 뒤 같은 행동을 해도 결과가 다를 수 있다 — 같게 하려면 Undo를 쓴다).
    public sealed class RewindSystem
    {
        public const string Key = "$rewind";

        readonly GameSession _session;

        // 남겨 둘 지점 수(오래된 것부터 버린다). 지점마다 세션 구역 전체를 담으므로 너무 크게 두지 않는다.
        public int Keep = 10;

        internal RewindSystem(GameSession session)
        {
            _session = session;
        }

        public int Count => Exists() ? _session.Data.Get<RewindMarks>(Key).marks.Count : 0;

        public IReadOnlyList<RewindMark> Marks => Exists() ? _session.Data.Get<RewindMarks>(Key).marks : (IReadOnlyList<RewindMark>)Array.Empty<RewindMark>();

        // 지금 모습을 지점으로 남긴다(행동 안에서). 돌려주는 값: 지점 번호.
        public long Mark(string label = null)
        {
            RequireAction("MarkRewind");
            Ensure();
            var data = _session.Data;
            var trees = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var key in data.Keys.ToArray())
            {
                if (key == Key || data.ZoneOf(key) != DataZone.Session) continue;
                trees[key] = data.ExportTree(key);
            }
            var json = MiniJson.Serialize(trees);
            long serial = 0;
            data.Modify<RewindMarks>(Key, list =>
            {
                serial = list.nextSerial++;
                list.marks.Add(new RewindMark { serial = serial, label = label ?? "", json = json });
                var keep = Math.Max(1, Keep);
                if (list.marks.Count > keep) list.marks.RemoveRange(0, list.marks.Count - keep);
            });
            return serial;
        }

        // 세션 구역 전체를 marksBack번째 앞 지점 모습으로(1 = 마지막 지점). 그 지점 뒤에 생긴 키는 기본값으로.
        public void Rewind(int marksBack = 1)
        {
            RequireAction("Rewind");
            var trees = TreesOf(marksBack);
            var data = _session.Data;
            foreach (var key in data.Keys.ToArray())
            {
                if (key == Key || data.ZoneOf(key) != DataZone.Session) continue;
                if (trees.TryGetValue(key, out var tree)) Restore(key, tree);
                else data.ResetKey(key);
            }
        }

        // 플레이어 데이터 하나만 되돌린다.
        public void RewindKey(string key, int marksBack = 1)
        {
            RequireAction("RewindKey");
            if (!_session.Data.IsDefined(key)) throw new ArgumentException($"[MomoiProject] 플레이어 데이터 '{key}'가 없습니다.");
            var trees = TreesOf(marksBack);
            if (trees.TryGetValue(key, out var tree)) Restore(key, tree);
            else _session.Data.ResetKey(key);
        }

        // 객체 하나(id가 있는 객체)만 그때 모습으로. 그 지점에 없던 객체면 false(아무것도 바꾸지 않는다).
        public bool RewindEntity(object entity, int marksBack = 1)
        {
            RequireAction("RewindEntity");
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            var id = GameApi.IdOf(entity);
            if (string.IsNullOrEmpty(id)) throw new ArgumentException($"[MomoiProject] 되감을 객체({entity.GetType().Name})에 id가 없습니다.");
            var data = _session.Data;
            var key = data.KeyOf(entity) ?? throw new ArgumentException($"[MomoiProject] '{id}'은(는) 플레이어 데이터 안의 객체가 아닙니다.");
            var trees = TreesOf(marksBack);
            if (!trees.TryGetValue(key, out var tree)) return false;
            var past = data.FromTree(key, tree);
            if (past == null) return false;
            var node = Mirror.Make(past);
            var then = Mirror.FindById(node, entity.GetType(), id);
            if (then == null) return false;
            DataModel.ApplyInPlace(entity, then, data.Identity);
            return true;
        }

        Dictionary<string, object> TreesOf(int marksBack)
        {
            if (marksBack < 1) throw new ArgumentOutOfRangeException(nameof(marksBack), "[MomoiProject] 몇 지점 앞인지는 1 이상입니다.");
            var marks = Marks;
            if (marks.Count < marksBack)
                throw new GameFailure(FailureKeys.RewindTooFar, TextArgs.From(("steps", marksBack), ("marks", marks.Count)), "{steps}지점 앞으로 되감을 수 없습니다(남은 지점 {marks}개).");
            var json = marks[marks.Count - marksBack].json;
            return MiniJson.Deserialize(json) as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        void Restore(string key, object tree)
        {
            var data = _session.Data;
            if (!data.IsData(key))
            {
                data.Set(key, data.ScalarFromTree(key, tree));
                return;
            }
            var past = data.FromTree(key, tree);
            if (past != null) data.Set(key, past);
        }

        void RequireAction(string what)
        {
            if (!_session.Data.InTransaction)
                throw new InvalidOperationException($"[MomoiProject] {what}은(는) 행동 안에서 부릅니다(되감기도 하나의 행동으로 기록된다).");
        }

        void Ensure()
        {
            if (_session.Data.IsDefined(Key)) return;
            _session.Data.Define(Key, DataZone.Session, new RewindMarks(), "되감기 지점");
            _session.Data.SetVisibility(Key, DataVisibility.Server);
        }

        bool Exists()
        {
            if (_session.Data.IsDefined(Key)) return true;
            if (!_session.Data.IsPending(Key)) return false;
            Ensure();
            return true;
        }
    }
}
