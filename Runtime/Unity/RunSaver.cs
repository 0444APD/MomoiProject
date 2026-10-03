using System;
using GBible.Data;

namespace GBible
{
    // DB › 시작 파일 › Bootstrap의 '판 저장' 설정을 실행한다(게임 코드가 저장 컴포넌트를 따로 쓰지 않게).
    //   행동마다 저장: 바깥 행동이 끝나거나 Undo하면 표시만 남기고, 프레임 끝(행동 밖)에서 판을 저장한다.
    //   판 끝 이벤트: 받으면 그 행동이 끝난 뒤 판 저장을 지우고 프로필을 바로 저장한다. 그 뒤로는 판을 저장하지 않다가,
    //                 다른 행동이 판 데이터(세션 구역)를 바꾸면(= 새 판 시작) 다시 저장한다.
    sealed class RunSaver
    {
        readonly GameSession _session;
        readonly bool _eachAction;
        readonly Type _endEvent;
        bool _dirty;
        bool _endPending;
        long _endTransaction = long.MinValue;

        // 판 끝 이벤트를 받은 뒤 새 판이 시작되기 전까지 참(이때는 끌 때도 판을 저장하지 않는다).
        public bool Ended { get; private set; }

        public RunSaver(GameSession session, RunSaveMode mode, Type endEvent)
        {
            _session = session;
            _eachAction = mode == RunSaveMode.AfterEachAction;
            _endEvent = endEvent;
            if (_eachAction)
            {
                session.Actions.Executed += OnExecuted;
                session.Data.TransactionUndone += _ => _dirty = true;
            }
            if (_endEvent != null)
            {
                session.Events.SubscribeAll(OnEvent);
                session.Data.Changed += OnChanged;
                // 판 끝 이벤트를 쏜 행동이 실패해 취소되면 판도 끝나지 않은 것이다.
                session.Actions.TransactionAborted += id =>
                {
                    if (id != _endTransaction) return;
                    Ended = false;
                    _endPending = false;
                };
            }
        }

        void OnExecuted(ActionRecord record)
        {
            if (record.Nested || record.Status != ActionStatus.Done) return; // 안쪽 행동은 바깥 행동이 끝날 때 한 번에
            _dirty = true;
        }

        void OnEvent(object e)
        {
            if (e == null || !_endEvent.IsInstanceOfType(e)) return;
            Ended = true;
            _endPending = true;
            _endTransaction = _session.Data.ActiveTransactionId;
        }

        void OnChanged(string key)
        {
            if (!Ended || _session.Data.ActiveTransactionId == _endTransaction) return; // 판을 끝낸 행동 자신의 변경은 제외
            try
            {
                if (_session.Data.ZoneOf(key) == DataZone.Session) Ended = false;
            }
            catch (Exception)
            {
                // 정의되지 않은 키(정리 중 등)는 무시한다.
            }
        }

        // 판을 불러오거나 새로 시작했을 때.
        public void Reset()
        {
            Ended = false;
            _endPending = false;
            _dirty = false;
        }

        // 프레임 끝(행동 밖)에서 부른다.
        public void FrameEnd()
        {
            if (_session.Data.InTransaction) return;
            if (_endPending)
            {
                _endPending = false;
                _dirty = false;
                GBibleRuntime.DeleteRun();
                GBibleRuntime.SaveProfile(_session);
                return;
            }
            if (!_dirty) return;
            _dirty = false;
            if (!Ended) GBibleRuntime.SaveRun(_session);
        }
    }
}
