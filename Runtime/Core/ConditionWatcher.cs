using System;
using System.Collections.Generic;

namespace GBible
{
    // 조건 평가기. 폴링하지 않는다.
    // 관찰 대상 키 → 그 키를 보는 조건(역색인)을 두고, 바뀐 키에 연결된 조건만 Flush 때 한 번씩 평가한다.
    public sealed class ConditionWatcher
    {
        sealed class Entry
        {
            public string Owner;
            public Condition Condition;
            public Action OnSatisfied;
            public readonly List<string> Keys = new List<string>();
            // 조건이 CollectDependencies로 선언한 키(평가 때 읽은 키는 매번 다시 모은다).
            public readonly HashSet<string> Declared = new HashSet<string>(StringComparer.Ordinal);
            public double PollInterval;
            public double NextPoll;
            // 이 조건을 다시 평가하게 만든 값 변화의 기록 id(인과 추적용).
            public long Cause;
        }

        readonly Dictionary<string, Entry> _watches = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly Dictionary<string, List<Entry>> _index = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        readonly HashSet<Entry> _dirty = new HashSet<Entry>();
        readonly List<Entry> _polling = new List<Entry>();
        readonly GameSession _session;
        const int MaxPasses = 16;

        public ConditionWatcher(GameSession session)
        {
            _session = session;
            // 값이 바뀌는 즉시 반응한다(달성은 조건을 만족하는 순간 일어난다).
            session.Data.Changed += OnDataChanged;
            session.Data.KeyDefined += OnKeyDefined;
        }

        public int EvaluationCount { get; private set; }
        public int WatchCount => _watches.Count;

        public void Watch(string owner, Condition condition, Action onSatisfied)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            Unwatch(owner);

            var watch = new Entry { Owner = owner, Condition = condition, OnSatisfied = onSatisfied };
            var keys = new HashSet<string>(StringComparer.Ordinal);
            condition.CollectDependencies(keys);
            // 선언하지 않은 것도: 한 번 평가해 보며 실제로 읽은 키를 모은다(결과는 쓰지 않는다. 달성은 값이 바뀔 때·EvaluateAll 때).
            watch.Declared.UnionWith(keys);
            Probe(condition, keys);
            watch.PollInterval = condition.PollInterval;

            _watches[owner] = watch;
            Index(watch, keys);
            if (watch.PollInterval > 0)
            {
                watch.NextPoll = _session.Clock.Time + watch.PollInterval;
                _polling.Add(watch);
            }
        }

        void Probe(Condition condition, HashSet<string> keys)
        {
            try
            {
                using (_session.Data.RecordReads(keys)) condition.Evaluate(_session);
            }
            catch (Exception)
            {
                // 아직 정의되지 않은 값을 보는 조건 등: 선언한 키만으로 감시하고, 처음 평가될 때 다시 모은다.
            }
        }

        // 감시 키를 바꾼다(역색인 갱신).
        void Index(Entry watch, HashSet<string> keys)
        {
            foreach (var key in watch.Keys)
                if (_index.TryGetValue(key, out var old)) old.Remove(watch);
            watch.Keys.Clear();
            watch.Keys.AddRange(keys);
            foreach (var key in watch.Keys)
            {
                if (!_index.TryGetValue(key, out var list)) _index[key] = list = new List<Entry>();
                list.Add(watch);
            }
        }

        public void Unwatch(string owner)
        {
            if (!_watches.TryGetValue(owner, out var watch)) return;
            _watches.Remove(owner);
            foreach (var key in watch.Keys)
                if (_index.TryGetValue(key, out var list)) list.Remove(watch);
            _polling.Remove(watch);
            _dirty.Remove(watch);
        }

        public bool IsWatching(string owner) => _watches.ContainsKey(owner);

        public IEnumerable<string> KeysOf(string owner) =>
            _watches.TryGetValue(owner, out var watch) ? watch.Keys : (IEnumerable<string>)Array.Empty<string>();

        void OnDataChanged(string key)
        {
            if (!_index.TryGetValue(key, out var list)) return;
            var cause = _session.Data.LastEntryId(key);
            foreach (var watch in list)
            {
                watch.Cause = cause;
                _dirty.Add(watch);
            }
        }

        // 새 키가 정의됐다(처음 쓸 때 정의되는 $statuses·$world·$modifiers …, 나중에 정의한 플레이어 데이터). 키가 없을 때 조건은 그 키를
        // 읽지 못해(정의를 보고 건너뛰거나 읽다가 던짐) 읽은 키에 없다 → 그 키가 생기고 바뀌어도 다시 평가되지 않는다.
        // 읽은 값으로 감시하는 조건과 그 키를 선언한 조건을 다음 Flush에서 다시 평가한다(그때 읽은 키로 다시 감시한다).
        void OnKeyDefined(string key)
        {
            _index.TryGetValue(key, out var declared);
            foreach (var watch in _watches.Values)
            {
                if (!watch.Condition.AutoDependencies && (declared == null || !declared.Contains(watch))) continue;
                if (_dirty.Add(watch)) watch.Cause = 0;
            }
        }

        public void EvaluateAll()
        {
            foreach (var watch in _watches.Values)
            {
                watch.Cause = 0;
                _dirty.Add(watch);
            }
            Flush();
        }

        // 그 감시를 다음 Flush에서 다시 평가한다(값이 바뀌지 않았어도 — 불러온 뒤 새로 감시를 시작한 것).
        public void Touch(string owner)
        {
            if (!_watches.TryGetValue(owner, out var watch)) return;
            watch.Cause = 0;
            _dirty.Add(watch);
        }

        public void Poll(double time)
        {
            foreach (var watch in _polling)
            {
                if (time < watch.NextPoll) continue;
                watch.NextPoll = time + watch.PollInterval;
                watch.Cause = 0;
                _dirty.Add(watch);
            }
        }

        // 게임 시계를 되돌리거나 불러온 판의 시각으로 옮긴 뒤: 다음 폴링을 새 시각에서 한 간격 뒤로(감시를 시작할 때와 같다).
        // 그러지 않으면 시계를 뒤로 돌린 뒤 예전 시각까지 폴링을 건너뛴다.
        public void ResetPolls(double time)
        {
            foreach (var watch in _polling) watch.NextPoll = time + watch.PollInterval;
        }

        // 조건 충족 → 보상 → 다른 값 변화 → 다른 조건 충족으로 이어질 수 있으므로 더 바뀌지 않을 때까지 반복한다.
        public void Flush()
        {
            // 멀티 클라이언트: 조건 충족(달성·트리거)은 서버가 판정한다.
            if (_session.Role == SessionRole.Client)
            {
                _dirty.Clear();
                return;
            }
            for (int pass = 0; _dirty.Count > 0; pass++)
            {
                if (pass >= MaxPasses)
                {
                    _session.Journal.Append(JournalKind.Warning, "Conditions", $"조건 연쇄 평가가 {MaxPasses}회를 넘었습니다. 순환 참조를 확인하세요.");
                    _dirty.Clear();
                    return;
                }

                var batch = new List<Entry>(_dirty);
                _dirty.Clear();
                foreach (var watch in batch)
                {
                    if (!_watches.TryGetValue(watch.Owner, out var current) || current != watch) continue;
                    EvaluationCount++;
                    bool satisfied;
                    var read = new HashSet<string>(watch.Declared, StringComparer.Ordinal);
                    try
                    {
                        using (_session.Data.RecordReads(read)) satisfied = watch.Condition.Evaluate(_session);
                    }
                    catch (Exception e)
                    {
                        // 던지기 전까지 읽은 키로도 감시한다(그 값이 바뀌면 다시 본다 — 등록할 때 미리 평가한 것과 같다).
                        if (!read.SetEquals(watch.Keys)) Index(watch, read);
                        _session.Journal.Append(JournalKind.Error, "Conditions", $"조건 평가 오류 ({watch.Owner}): {e.Message}");
                        continue;
                    }
                    if (!read.SetEquals(watch.Keys)) Index(watch, read);
                    if (!satisfied) continue;
                    Unwatch(watch.Owner);
                    try
                    {
                        if (watch.Cause != 0)
                        {
                            using (_session.Journal.Cause(watch.Cause)) watch.OnSatisfied?.Invoke();
                        }
                        else
                        {
                            watch.OnSatisfied?.Invoke();
                        }
                    }
                    catch (Exception e) when (!_session.Data.InTransaction)
                    {
                        // 행동 밖(행동이 끝난 뒤·프레임 끝): 발동한 결과 하나의 오류가 이미 성공한 행동을 실패로 보이게 하거나 남은 조건 평가를 막지 않게
                        // 기록에 남기고 넘어간다(이벤트를 받는 쪽의 오류와 같다). 행동 안이면 그대로 올라가 그 행동이 실패한다.
                        _session.Journal.Append(JournalKind.Error, "Conditions", $"조건 충족 처리 오류 ({watch.Owner}): {e.Message}");
                    }
                }
            }
        }
    }
}
