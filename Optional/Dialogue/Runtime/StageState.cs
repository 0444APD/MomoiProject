using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GameSystems.Dialogue
{
    // 대화 화면의 '유지되는 모습'. 상태 효과(배경·스탠딩·카메라 …)는 이 값을 바꾸고, 화면은 앞뒤 값을 이어 전환을 그린다.
    // 스킵·되감기·중간 저장은 이 값만 있으면 그 순간의 화면을 그대로 만든다.
    [Serializable]
    public sealed class StageState
    {
        // 지금 장소(장소 ID)와 그 배경 이미지(예외 이미지면 그것).
        public string location;
        public Sprite background;
        [Range(0, 1)] public float dim;
        [Range(0, 0.5f)] public float letterbox;
        public float cameraZoom = 1f;
        public Vector2 cameraOffset;
        public AudioClip music;
        [Range(0, 1)] public float musicVolume = 1f;
        public List<StandingState> standings = new List<StandingState>();
        // 프로젝트가 더한 상태 효과의 값(날씨·시간대 등). 스킵·저장·되감기에 함께 따라간다.
        public List<CustomStateValue> custom = new List<CustomStateValue>();

        public CustomStateValue Custom(string key, bool create = false)
        {
            var found = custom.FirstOrDefault(c => c.key == key);
            if (found == null && create) custom.Add(found = new CustomStateValue { key = key });
            return found;
        }

        public StandingState Standing(string character) => standings.FirstOrDefault(s => s.character == character);

        public StageState Clone()
        {
            var copy = (StageState)MemberwiseClone();
            copy.standings = standings.Select(s => s.Clone()).ToList();
            copy.custom = custom.Select(c => c.Clone()).ToList();
            return copy;
        }
    }

    [Serializable]
    public sealed class StandingState
    {
        public string character;
        public string expression;
        // 스탠딩 ID 대신 직접 꽂은 스프라이트(예외).
        public Sprite sprite;
        // 가로 위치(0 = 왼쪽 끝, 1 = 오른쪽 끝, 밖은 0 미만·1 초과).
        public float x = 0.5f;
        public bool highlighted = true;

        public StandingState Clone() => (StandingState)MemberwiseClone();
    }

    [Serializable]
    public sealed class CustomStateValue
    {
        public string key;
        public string text;
        public float number;
        public UnityEngine.Object asset;

        public CustomStateValue Clone() => (CustomStateValue)MemberwiseClone();
    }

    // 스탠딩 자리. 사용자 지정은 가로 위치 값을 직접 쓴다.
    public enum StandingSlot
    {
        [InspectorName("왼쪽 밖")] OffLeft,
        [InspectorName("왼쪽")] Left,
        [InspectorName("왼쪽 가운데")] CenterLeft,
        [InspectorName("가운데")] Center,
        [InspectorName("오른쪽 가운데")] CenterRight,
        [InspectorName("오른쪽")] Right,
        [InspectorName("오른쪽 밖")] OffRight,
        [InspectorName("직접 지정")] Custom,
    }

    public static class StandingSlots
    {
        public static float X(StandingSlot slot, float custom)
        {
            switch (slot)
            {
                case StandingSlot.OffLeft: return -0.25f;
                case StandingSlot.Left: return 0.2f;
                case StandingSlot.CenterLeft: return 0.35f;
                case StandingSlot.Center: return 0.5f;
                case StandingSlot.CenterRight: return 0.65f;
                case StandingSlot.Right: return 0.8f;
                case StandingSlot.OffRight: return 1.25f;
                default: return custom;
            }
        }
    }
}
