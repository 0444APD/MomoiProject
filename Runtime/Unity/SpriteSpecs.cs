using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameSystems.Data
{
    // 스프라이트 규격 묶음: 한 용도의 규격들(예: 캐릭터 일러 = 스탠딩A·스탠딩B·아이콘).
    // 필드 규칙으로 스프라이트 필드에 묶음을 연결하면, 그 필드에 원본을 넣을 때 묶음의 규격이 모두 베이크된다.
    [Database("스프라이트 규격 묶음", Group = "이미지", Order = 0, IdPrefix = "sprites")]
    [CreateAssetMenu(menuName = "Game Systems/스프라이트 규격 묶음", fileName = "NewSpritePresetGroup")]
    public sealed class SpritePresetGroupData : GameData
    {
        [FieldLabel("설명")] public string description = "";
    }

    // 스프라이트 규격 하나: 출력 크기. 바꾸면 이 규격으로 베이크한 이미지가 모두 다시 베이크된다.
    [Database("스프라이트 규격", Group = "이미지", Order = 1, IdPrefix = "preset")]
    [CreateAssetMenu(menuName = "Game Systems/스프라이트 규격", fileName = "NewSpritePreset")]
    public sealed class SpritePresetData : GameData
    {
        [FieldLabel("묶음"), DataRef(typeof(SpritePresetGroupData))] public string group = "";
        [FieldLabel("가로(px)")] public int width = 256;
        [FieldLabel("세로(px)")] public int height = 256;

        // 규격이 바뀌었는지 알아내는 값(베이크 기록과 비교).
        public int Revision => width * 73856093 ^ height * 19349663;

        public override void ValidateData(IValidationContext context)
        {
            if (string.IsNullOrEmpty(group)) context.Error("묶음이 정해지지 않았습니다.");
            if (width < 8 || height < 8 || width > 8192 || height > 8192) context.Error("크기는 8~8192px 사이여야 합니다.");
        }
    }

    // 원본 일러 하나의 맞춤 기록과 베이크 결과. 맞춤은 원본 기준이라 같은 일러를 여러 곳에서 써도 한 번만 맞춘다.
    [Serializable]
    public sealed class SpriteFraming
    {
        public Sprite source;
        public string sourceGuid = "";
        // 이 원본을 베이크하는 규격 묶음(묶음에 규격이 추가되면 그것도 베이크한다).
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

    // 모든 맞춤 기록(Resources에 두어 게임 중에도 읽는다). 에디터 도구만 고친다.
    [HideInDatabase]
    public sealed class SpriteSpecDatabase : ScriptableObject
    {
        public const string ResourcesPath = "GameSystems/SpriteSpecs";
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

        public void EditorRemove(SpriteFraming framing) => framings.Remove(framing);
#endif
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
