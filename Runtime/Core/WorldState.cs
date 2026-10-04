using System;
using System.Collections.Generic;

namespace GBible
{
    // 월드 상태: 씬에 놓인 것들의 상태(열린 상자·쓰러뜨린 보스·대화한 NPC·켜 둔 스위치)를 이름으로 기억한다.
    //   Game.SetWorld("forest.chest3", "opened");      if (Game.WorldFlag("forest.boss")) …      Game.World("village.gate") == "open"
    // 이름은 프로젝트가 정한다(씬 이름.오브젝트 이름 권장). 값은 글자 하나(참거짓은 "true"/비움).
    // 플레이어 데이터라 Undo·판 저장·멀티 그대로다. 판이 끝나도 남길 것은 permanent(프로필).
    // 씬 쪽: 컴포넌트 '월드 상태 연결'(WorldStateBinding)이 이 값을 보고 오브젝트를 켜고 끈다.
    [Serializable]
    public sealed class WorldStateTable
    {
        public Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public sealed class WorldStateSystem
    {
        public const string SessionKey = "$world";
        public const string MetaKey = "$world.meta";

        readonly GameSession _session;

        internal WorldStateSystem(GameSession session)
        {
            _session = session;
        }

        // 판 값이 프로필 값보다 앞선다(같은 이름이면).
        public string Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var key in new[] { SessionKey, MetaKey })
                if (Exists(key) && _session.Data.Get<WorldStateTable>(key)?.values.TryGetValue(id, out var value) == true) return value;
            return null;
        }

        public void Set(string id, string value, bool permanent = false)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("[MomoiProject] 월드 상태 이름이 비어 있습니다.");
            var key = permanent ? MetaKey : SessionKey;
            Ensure(key);
            _session.Data.Modify<WorldStateTable>(key, t =>
            {
                if (value == null) t.values.Remove(id);
                else t.values[id] = value;
            });
        }

        public void Clear(string id)
        {
            foreach (var key in new[] { SessionKey, MetaKey })
                if (Exists(key) && _session.Data.Get<WorldStateTable>(key)?.values.ContainsKey(id) == true)
                    _session.Data.Modify<WorldStateTable>(key, t => t.values.Remove(id));
        }

        public static bool IsTrue(string value) => !string.IsNullOrEmpty(value) && value != "false" && value != "0";

        void Ensure(string key)
        {
            if (!_session.Data.IsDefined(key))
                _session.Data.Define(key, key == MetaKey ? DataZone.Meta : DataZone.Session, new WorldStateTable(), key == MetaKey ? "영구 월드 상태" : "월드 상태");
        }

        // 등록돼 있거나, 불러온 저장의 값이 기다리고 있으면(지금 등록한다) 참.
        bool Exists(string key)
        {
            if (_session.Data.IsDefined(key)) return true;
            if (!_session.Data.IsPending(key)) return false;
            Ensure(key);
            return true;
        }
    }
}
