using System;
using System.Collections.Generic;
using GBible.Data;

namespace GBible
{
    // DB › 시작 파일 › Bootstrap의 '판 저장' 설정을 실행한다(게임 코드가 저장 컴포넌트를 따로 쓰지 않게).
    //   행동마다 저장: 바깥 행동이 끝나거나 Undo하면 표시만 남기고, 프레임 끝(행동 밖)에서 판을 저장한다.
    //   N초마다 저장: 판 데이터가 바뀐 뒤 마지막 저장에서 N초(실제 시간)가 지난 프레임 끝에 저장한다.
    //   이벤트를 받을 때 저장: 그 이벤트를 쏜 행동이 끝난 프레임 끝에 저장한다(실패해 취소된 행동이면 저장하지 않는다).
    //   프로필도 행동마다: 프로필(메타 구역)을 바꾼 행동마다 프로필을 저장한다(판 안의 전투 행동처럼 프로필을 건드리지 않은 행동에서는 쓰지 않는다 —
    //   실시간 게임이 초당 수십 번 쓰지 않게). 끄더라도, 판을 행동마다 저장할 때 그 행동이 프로필(메타 구역)을 바꿨으면 프로필도 함께 저장한다
    //   (판 파일만 앞서 있다가 강제 종료되면 쓴 스태미나·새 id 번호가 프로필에서 되돌아가 어긋난다).
    //   판 끝 이벤트: 받으면 그 행동이 끝난 뒤 판 저장을 지우고 프로필을 바로 저장한다. 그 뒤로는 판을 저장하지 않다가,
    //                 다른 플레이어 행동이 판 데이터(세션 구역)를 바꾸면(= 새 판 시작) 다시 저장한다. 판 끝 행동을 Undo하면 판이 이어진다.
    //   판이 살아 있을 때만 판을 저장한다(Live): 판을 불러왔거나, 새 판을 시작했거나, 성공한 행동이 판 데이터를 바꿨을 때.
    //   타이틀에서 판을 읽지 않고 끄거나 슬롯을 바꿔도 저장된 판을 빈 판으로 덮지 않는다.
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
        bool _profileDirty;
        long _endTransaction = long.MinValue;
        long _endJournal = long.MaxValue;
        // 지금 행동이 판 데이터(세션 구역)를 바꿨나 — 행동이 성공하면 판이 살아 있다(·판 끝 뒤면 새 판).
        long _sessionTouched = long.MinValue;
        static readonly Dictionary<Type, bool> SystemActions = new Dictionary<Type, bool>();

        // 판이 살아 있다: 불러왔거나, 새로 시작했거나, 성공한 행동이 판 데이터를 바꿨다. 아니면 판을 저장하지 않는다.
        public bool Live { get; private set; }

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
            session.Data.Changed += OnSessionTouched;
            session.Actions.Executed += OnActionDone;
            session.Actions.TransactionAborted += id => { if (id == _sessionTouched) _sessionTouched = long.MinValue; };
            if (_eachAction || _profileEachAction)
            {
                session.Actions.Executed += OnExecuted;
                session.Data.TransactionUndone += _ => _dirty = true;
            }
            if (_eachAction || _profileEachAction || mode == RunSaveMode.Interval || mode == RunSaveMode.OnEvent) session.Data.Changed += OnMetaChanged;
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
            // 스냅샷 방식 되감기(Undo): 되감은 모습을 저장하고, 판 끝 앞으로 되감았으면 판은 끝나지 않은 것이다.
            session.Rewound += journalId =>
            {
                _dirty = true;
                _profileDirty = true;
                _metaDirty = true;
                if (!Ended || _endJournal <= journalId) return;
                Ended = false;
                _endPending = false;
            };
            if (_endEvent != null)
            {
                session.Events.SubscribeAll(OnEvent);
                // 판을 끝낸 행동을 Undo하면 판은 끝나지 않은 것이다.
                session.Data.TransactionUndone += tx =>
                {
                    if (tx == null || tx.Id != _endTransaction) return;
                    Ended = false;
                    _endPending = false;
                };
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
            if (GBibleRuntime.IsSaving) return;                              // 저장 직전(Saving)에 담은 행동은 이번 저장에 들어간다
            // 시스템 행동(상태 효과 주기·만료·예약·패시브 효과 주기 …)은 틱마다 돌 수 있다: '행동마다' 저장은 플레이어 행동에만 —
            // 그 변경은 다음 플레이어 행동·끌 때 함께 저장된다(매 프레임 판 파일을 쓰지 않게). 'N초마다'는 간격이 있으니 그대로 센다.
            if (_mode != RunSaveMode.Interval && IsSystem(record.Action)) return;
            _dirty = true;
            _profileDirty = true;
        }

        void OnSessionTouched(string key)
        {
            if (!_session.Data.InTransaction) return;
            try
            {
                if (_session.Data.ZoneOf(key) == DataZone.Session) _sessionTouched = _session.Data.ActiveTransactionId;
            }
            catch (Exception)
            {
                // 정의되지 않은 키(정리 중 등)는 무시한다.
            }
        }

        // 바깥 행동이 성공했을 때: 판 데이터를 바꿨으면 판이 살아 있다. 판이 끝난 뒤 플레이어 행동이 판 데이터를 바꾸면 새 판이다
        // (시간 규칙 — 상태 효과 주기·만료·예약 — 은 [SystemAction]이라 끝난 판을 다시 살리지 않는다).
        void OnActionDone(ActionRecord record)
        {
            if (record.Nested || record.Status != ActionStatus.Done || _sessionTouched == long.MinValue) return;
            var touchedBy = _sessionTouched;
            _sessionTouched = long.MinValue;
            if (Ended && touchedBy != _endTransaction && !IsSystem(record.Action)) Ended = false;
            if (!Ended) Live = true;
        }

        static bool IsSystem(GameAction action)
        {
            if (action == null) return false;
            var type = action.GetType();
            lock (SystemActions)
            {
                if (!SystemActions.TryGetValue(type, out var system))
                    SystemActions[type] = system = type.IsDefined(typeof(SystemActionAttribute), true);
                return system;
            }
        }

        void OnEvent(object e)
        {
            if (e == null || !_endEvent.IsInstanceOfType(e)) return;
            Ended = true;
            _endPending = true;
            _endTransaction = _session.Data.ActiveTransactionId;
            _endJournal = _session.Journal.LastId;
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
            _profileDirty = false;
            Live = true;
        }

        // 판 끝을 지금 마무리한다(판 끝과 같은 프레임에 끄기·슬롯 바꾸기·복사할 때): 판 저장을 지우고 프로필을 저장한다.
        public void FlushEnd()
        {
            if (!_endPending) return;
            _endPending = false;
            _dirty = false;
            GBibleRuntime.DeleteRun();
            GBibleRuntime.SaveProfile(_session);
            _metaDirty = false;
            _profileDirty = false;
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
                // '프로필도 행동마다'는 간격을 기다리지 않는다(프로필이 바뀐 행동이 있었을 때만).
                if (_profileEachAction && _profileDirty)
                {
                    _profileDirty = false;
                    if (_metaDirty)
                    {
                        _metaDirty = false;
                        GBibleRuntime.SaveProfile(_session);
                    }
                }
                if (!_dirty || UnityEngine.Time.unscaledTime - _lastSave < _interval) return;
                _dirty = false;
                Save(!Ended && Live, profile: false);
                return;
            }
            if (_eventSave)
            {
                _eventSave = false;
                Save(!Ended && Live);
                return;
            }
            if (!_dirty) return;
            _dirty = false;
            _profileDirty = false;
            var saveRun = _eachAction && !Ended && Live;
            // 저장 직전 알림(Saving)을 먼저 — 그 안에서 바뀐 메타 값도 이번 프로필 저장에 들어가게.
            if (saveRun) GBibleRuntime.NotifySaving(_session);
            if ((_profileEachAction || saveRun) && _metaDirty) GBibleRuntime.SaveProfile(_session);
            if (saveRun) GBibleRuntime.WriteRun(_session);
            _metaDirty = false;
            if (saveRun) _lastSave = UnityEngine.Time.unscaledTime;
        }

        // N초마다·이벤트 때: 판을 저장하고, 그동안 프로필(메타 구역)이 바뀌었으면 프로필도.
        void Save(bool run, bool profile = true)
        {
            if (run) GBibleRuntime.NotifySaving(_session);
            if (((profile && _profileEachAction) || run) && _metaDirty) GBibleRuntime.SaveProfile(_session);
            if (run) GBibleRuntime.WriteRun(_session);
            _metaDirty = false;
            _lastSave = UnityEngine.Time.unscaledTime;
        }
    }
}
