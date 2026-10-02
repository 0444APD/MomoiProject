using System;
using System.Collections.Generic;
using UnityEngine;

namespace GBible.Data
{
    // 원본 일러 하나의 맞춤 기록과 베이크 결과. 맞춤은 원본 기준이라 같은 일러를 여러 곳에서 써도 한 번만 맞춘다.
    [Serializable]
    public sealed class SpriteFraming
    {
        public Sprite source;
        public string sourceGuid = "";
        // 이 원본을 베이크하는 규격 그룹(그룹에 규격이 추가되면 그것도 베이크한다).
        public string group = "";
        // 중심(원본 안의 0~1 좌표, 왼쪽 아래 기준)과 배율(1 = 규격 비율로 원본에 꽉 차게, 클수록 확대).
        public Vector2 center = new Vector2(0.5f, 0.5f);
        public float zoom = 1f;
        // 예외 촬영: 특정 규격만 따로 잡은 위치. 규격이 바뀌면 needsReview가 켜져 개발자에게 알린다.
        public List<SpriteFramingOverride> overrides = new List<SpriteFramingOverride>();
        public List<SpriteBake> baked = new List<SpriteBake>();

        public SpriteFramingOverride OverrideFor(string preset) => overrides.Find(o => o.preset == preset);
        public SpriteBake BakeFor(string preset) => baked.Find(b => b.preset == preset);
    }

    [Serializable]
    public sealed class SpriteFramingOverride
    {
        public string preset = "";
        public Vector2 center = new Vector2(0.5f, 0.5f);
        public float zoom = 1f;
        public bool needsReview;
        public string note = "";
    }

    [Serializable]
    public sealed class SpriteBake
    {
        public string preset = "";
        public Sprite sprite;
        public int revision;
    }


    // 게임 코드용: 원본 스프라이트의 규격 베이크본. 없으면 원본을 돌려준다.
    //   image.sprite = SpriteSpecs.Get(item.icon, GameIds.SpritePreset.Icon);
    public static class SpriteSpecs
    {
        static SpriteSpecDatabase _database;

        public static SpriteSpecDatabase Database => _database != null ? _database : _database = Resources.Load<SpriteSpecDatabase>(SpriteSpecDatabase.ResourcesPath);

        public static Sprite Get(Sprite source, string preset)
        {
            if (source == null || string.IsNullOrEmpty(preset)) return source;
            var bake = Database?.Find(source)?.BakeFor(preset);
            return bake != null && bake.sprite != null ? bake.sprite : source;
        }

        public static bool Has(Sprite source, string preset) => Database?.Find(source)?.BakeFor(preset)?.sprite != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => _database = null;

        // 에디터가 새로 베이크하면 캐시를 비운다.
        public static void ClearCache() => _database = null;
    }
}
