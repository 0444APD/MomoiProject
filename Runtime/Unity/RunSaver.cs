using System;
using GBible.Data;

namespace GBible
{
    // DB › 시작 파일 › Bootstrap의 '판 저장' 설정을 실행한다(게임 코드가 저장 컴포넌트를 따로 쓰지 않게).
    //   행동마다 저장: 바깥 행동이 끝나거나 Undo하면 표시만 남기고, 프레임 끝(행동 밖)에서 판을 저장한다.
    //   N초마다 저장: 판 데이터가 바뀐 뒤 마지막 저장에서 N초(실제 시간)가 지난 프레임 끝에 저장한다.
    //   이벤트를 받을 때 저장: 그 이벤트를 쏜 행동이 끝난 프레임 끝에 저장한다(실패해 취소된 행동이면 저장하지 않는다).
    //   프로필도 행동마다: 같은 때 프로필도 저장한다. 끄더라도, 판을 행동마다 저장할 때 그 행동이 프로필(메타 구역)을 바꿨으면 프로필도 함께 저장한다
    //   (판 파일만 앞서 있다가 강제 종료되면 쓴 스태미나·새 id 번호가 프로필에서 되돌아가 어긋난다).
    //   판 끝 이벤트: 받으면 그 행동이 끝난 뒤 판 저장을 지우고 프로필을 바로 저장한다. 그 뒤로는 판을 저장하지 않다가,
    //                 다른 행동이 판 데이터(세션 구역)를 바꾸면(= 새 판 시작) 다시 저장한다.
    sealed class RunSaver
    {
        readonly GameSession _session;
        readonly bool _eachAction;
        readonly RunSaveMode _mode;
        readonly float _interval;
        readonly Type _saveEvent;
        bool _eventSave;
        long _eventTransaction = long.MinValue;
        float _lastSave;
        readonly bool _profileEachAction;
        readonly Type _endEvent;
        bool _dirty;
        bool _metaDirty;
        bool _endPending;
        long _endTransaction = long.MinValue;

        // 판 끝 이벤트를 받은 뒤 새 판이 시작되기 전까지 참(이때는 끌 때도 판을 저장하지 않는다).
        public bool Ended { get; private set; }

        public RunSaver(GameSession session, RunSaveMode mode, Type endEvent, bool profileEachAction, float interval = 30f, Type saveEvent = null)
        {
            _session = session;
            _mode = mode;
            _interval = Math.Max(1f, interval);
            _saveEvent = mode == RunSaveMode.OnEvent ? saveEvent : null;
            _lastSave = UnityEngine.Time.unscaledTime;
            _eachAction = mode == RunSaveMode.AfterEachAction;
            _profileEachAction = profileEachAction;
            _endEvent = endEvent;
            if (_eachAction || _profileEachAction)
            {
                session.Actions.Executed += OnExecuted;
                session.Data.TransactionUndone += _ => _dirty = true;
            }
            if (_eachAction || mode == RunSaveMode.Interval || mode == RunSaveMode.OnEvent) session.Data.Changed += OnMetaChanged;
            if (mode == RunSaveMode.Interval)
            {
                session.Actions.Executed += OnExecuted;
                session.Data.TransactionUndone += _ => _dirty = true;
            }
            if (_saveEvent != null)
            {
                session.Events.SubscribeAll(e =>
                {
                    if (e == null || !_saveEvent.IsInstanceOfType(e)) return;
                    _eventSave = true;
                    _eventTransaction = _session.Data.ActiveTransactionId;
                });
                session.Actions.TransactionAborted += id => { if (id == _eventTransaction) _eventSave = false; };
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

        void OnMetaChanged(string key)
        {
            try
            {
                if (_session.Data.ZoneOf(key) == DataZone.Meta) _metaDirty = true;
            }
            catch (Exception)
            {
                // 정의되지 않은 키(정리 중 등)는 무시한다.
            }
        }

        // 판을 불러오거나 새로 시작했을 때. 판 끝 뒤 프로필 저장이 남아 있으면(판 끝 이벤트와 같은 프레임에 새 판) 먼저 한다.
        public void Reset()
        {
            if (_endPending) GBibleRuntime.SaveProfile(_session);
            _metaDirty = false;
            _eventSave = false;
            _lastSave = UnityEngine.Time.unscaledTime;
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
                _metaDirty = false;
                return;
            }
            if (_mode == RunSaveMode.Interval)
            {
                if (!_dirty || UnityEngine.Time.unscaledTime - _lastSave < _interval) return;
                _dirty = false;
                Save(!Ended);
                return;
            }
            if (_eventSave)
            {
                _eventSave = false;
                Save(!Ended);
                return;
            }
            if (!_dirty) return;
            _dirty = false;
            var saveRun = _eachAction && !Ended;
            if (_profileEachAction || (saveRun && _metaDirty)) GBibleRuntime.SaveProfile(_session);
            if (saveRun) GBibleRuntime.SaveRun(_session);
            _metaDirty = false;
            if (saveRun) _lastSave = UnityEngine.Time.unscaledTime;
        }

        // N초마다·이벤트 때: 판을 저장하고, 그동안 프로필(메타 구역)이 바뀌었으면 프로필도.
        void Save(bool run)
        {
            if (_profileEachAction || (run && _metaDirty)) GBibleRuntime.SaveProfile(_session);
            if (run) GBibleRuntime.SaveRun(_session);
            _metaDirty = false;
            _lastSave = UnityEngine.Time.unscaledTime;
        }
    }
}
