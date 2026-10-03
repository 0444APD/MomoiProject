using System;

namespace GBible
{
    // DB › 시작 파일 › Bootstrap의 'Undo 끊는 이벤트': 그 이벤트를 쏜 바깥 행동이 끝나면 Undo 기록을 비운다.
    // 예: 턴 끝 이벤트 → 이번 턴 안에서는 되돌릴 수 있지만, 지난 턴으로는 되돌릴 수 없다.
    // 행동 밖에서 쏜 이벤트면 바로 비운다. 그 행동이 실패해 취소되면 비우지 않는다.
    static class UndoBoundary
    {
        public static void Watch(GameSession session, Type boundaryEvent)
        {
            var pending = long.MinValue;
            session.Events.SubscribeAll(e =>
            {
                if (e == null || !boundaryEvent.IsInstanceOfType(e)) return;
                var transaction = session.Data.ActiveTransactionId;
                if (transaction < 0) Clear(session, boundaryEvent);
                else pending = transaction;
            });
            session.Actions.Executed += record =>
            {
                if (record.Nested || record.TransactionId != pending) return;
                pending = long.MinValue;
                if (record.Status == ActionStatus.Done) Clear(session, boundaryEvent);
            };
            session.Actions.TransactionAborted += id =>
            {
                if (id == pending) pending = long.MinValue;
            };
        }

        static void Clear(GameSession session, Type boundaryEvent)
        {
            session.ClearUndo();
            session.Journal.Append(JournalKind.Undo, "Undo", $"Undo 기록을 비웠습니다({EventBus.DisplayNameOf(boundaryEvent)}). 이전으로는 되돌릴 수 없습니다.");
        }
    }
}
