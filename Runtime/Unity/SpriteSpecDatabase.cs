using System;
using System.Collections.Generic;
using UnityEngine;

namespace GBible.Data
{
    // 모든 맞춤 기록(Resources에 두어 게임 중에도 읽는다). 에디터 도구만 고친다.
    [HideInDatabase]
    public sealed class SpriteSpecDatabase : ScriptableObject
    {
        public const string ResourcesPath = "MomoiProject/SpriteSpecs";
        [SerializeField] List<SpriteFraming> framings = new List<SpriteFraming>();

        // 원본 → 기록(처음 찾을 때 만든다 — 게임 중 SpriteSpecs.Get이 목록을 매번 훑지 않게).
        [NonSerialized] Dictionary<Sprite, SpriteFraming> _bySource;

        public IReadOnlyList<SpriteFraming> Framings => framings;

        public SpriteFraming Find(Sprite source)
        {
            if (source == null) return null;
            if (_bySource == null)
            {
                _bySource = new Dictionary<Sprite, SpriteFraming>();
                foreach (var framing in framings)
                    if (framing?.source != null) _bySource[framing.source] = framing;
            }
            return _bySource.TryGetValue(source, out var found) ? found : null;
        }

        void OnValidate() => _bySource = null;

#if UNITY_EDITOR
        public SpriteFraming FindOrAdd(Sprite source, string guid)
        {
            var found = Find(source);
            if (found != null) return found;
            found = new SpriteFraming { source = source, sourceGuid = guid };
            framings.Add(found);
            _bySource = null;
            return found;
        }

        // 기록을 뺀다(원본이 사라졌거나 틀에서 뺐을 때). 구운 파일은 부른 쪽이 정리한다.
        public bool Remove(SpriteFraming framing)
        {
            _bySource = null;
            return framings.Remove(framing);
        }

        // 기록을 고친 뒤(원본을 바꿈 등) 찾기 표를 다시 만들게 한다.
        public void Changed() => _bySource = null;
#endif
    }
}
