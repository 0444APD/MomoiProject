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

        public IReadOnlyList<SpriteFraming> Framings => framings;

        public SpriteFraming Find(Sprite source) => source == null ? null : framings.Find(f => f.source == source);

#if UNITY_EDITOR
        public SpriteFraming FindOrAdd(Sprite source, string guid)
        {
            var found = Find(source);
            if (found != null) return found;
            found = new SpriteFraming { source = source, sourceGuid = guid };
            framings.Add(found);
            return found;
        }
#endif
    }
}
