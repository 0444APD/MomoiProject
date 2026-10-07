using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace GBible.Data
{
    // 원본 일러 하나의 맞춤 기록과 구운 결과. 맞춤은 원본 기준이라 같은 일러를 여러 곳에서 써도 한 번만 맞춘다.
    [Serializable]
    public sealed class SpriteFraming
    {
        public Sprite source;
        public string sourceGuid = "";
        // 이 원본을 굽는 틀(SpriteFrameData의 ID). 틀에 크기가 더해지면 그것도 굽는다.
        [FormerlySerializedAs("group")] public string frame = "";
        // 중심(원본 안의 0~1 좌표, 왼쪽 아래 기준)과 배율(1 = 크기 비율로 원본에 꽉 차게, 클수록 확대). 예외가 없는 크기 모두에 쓴다.
        public Vector2 center = new Vector2(0.5f, 0.5f);
        public float zoom = 1f;
        // 예외 촬영: 특정 크기만 따로 잡은 위치. 크기가 바뀌면 needsReview가 켜져 개발자에게 알린다.
        public List<SpriteFramingOverride> overrides = new List<SpriteFramingOverride>();
        public List<SpriteBake> baked = new List<SpriteBake>();

        public SpriteFramingOverride OverrideFor(string size) => overrides.Find(o => o.size == size);
        public SpriteBake BakeFor(string size) => baked.Find(b => b.size == size);
    }

    [Serializable]
    public sealed class SpriteFramingOverride
    {
        // 틀의 크기 이름.
        [FormerlySerializedAs("preset")] public string size = "";
        public Vector2 center = new Vector2(0.5f, 0.5f);
        public float zoom = 1f;
        public bool needsReview;
        public string note = "";
    }

    [Serializable]
    public sealed class SpriteBake
    {
        // 틀의 크기 이름.
        [FormerlySerializedAs("preset")] public string size = "";
        public Sprite sprite;
        public int revision;
    }


    // 게임 코드용: 원본 스프라이트의 크기별 구운 이미지. 없으면 원본을 돌려준다.
    //   image.sprite = SpriteSpecs.Get(item.portrait, GameIds.SpriteSize.Character.Icon);
    public static class SpriteSpecs
    {
        static SpriteSpecDatabase _database;

        public static SpriteSpecDatabase Database => _database != null ? _database : _database = Resources.Load<SpriteSpecDatabase>(SpriteSpecDatabase.ResourcesPath);

        public static Sprite Get(Sprite source, string size)
        {
            if (source == null || string.IsNullOrEmpty(size)) return source;
            var bake = Database?.Find(source)?.BakeFor(size);
            return bake != null && bake.sprite != null ? bake.sprite : source;
        }

        public static bool Has(Sprite source, string size) => Database?.Find(source)?.BakeFor(size)?.sprite != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => _database = null;

        // 에디터가 새로 구우면 캐시를 비운다.
        public static void ClearCache() => _database = null;
    }
}
