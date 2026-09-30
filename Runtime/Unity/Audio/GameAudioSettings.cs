using UnityEngine;
using UnityEngine.Audio;

namespace GameSystems
{
    // 오디오 설정(선택, Resources/GameSystems/GameAudioSettings). 없으면 기본값으로 동작한다.
    [CreateAssetMenu(menuName = "Game Systems/오디오 설정", fileName = "GameAudioSettings")]
    public sealed class GameAudioSettings : ScriptableObject
    {
        public const string ResourcesPath = "GameSystems/GameAudioSettings";

        [Header("기본 음량(플레이어가 바꾼 값이 저장돼 있으면 그것)")]
        [Range(0, 1)] public float master = 1f;
        [Range(0, 1)] public float bgm = 0.8f;
        [Range(0, 1)] public float voice = 1f;
        [Range(0, 1)] public float sfx = 1f;
        [Range(0, 1)] public float ui = 1f;

        [Header("직렬 채널이 바뀔 때 페이드 시간(초)")]
        [Min(0)] public float bgmFade = 1f;
        [Min(0)] public float voiceFade = 0.15f;

        [Header("병렬 채널이 한꺼번에 낼 수 있는 최대 소리 수(넘으면 가장 오래된 것을 멈춘다)")]
        [Min(1)] public int maxParallel = 32;

        [Header("믹서 그룹(선택: 이펙트·덕킹 등을 믹서에서 걸 때)")]
        public AudioMixerGroup bgmGroup;
        public AudioMixerGroup voiceGroup;
        public AudioMixerGroup sfxGroup;
        public AudioMixerGroup uiGroup;

        [Tooltip("플레이어가 바꾼 채널 음량을 기기에 저장한다(PlayerPrefs)")]
        public bool saveVolumes = true;

        public static GameAudioSettings Load() => Resources.Load<GameAudioSettings>(ResourcesPath);

        public float DefaultVolume(AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Bgm: return bgm;
                case AudioChannel.Voice: return voice;
                case AudioChannel.Sfx: return sfx;
                case AudioChannel.Ui: return ui;
                default: return master;
            }
        }

        public AudioMixerGroup Group(AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Bgm: return bgmGroup;
                case AudioChannel.Voice: return voiceGroup;
                case AudioChannel.Sfx: return sfxGroup;
                case AudioChannel.Ui: return uiGroup;
                default: return null;
            }
        }
    }
}
