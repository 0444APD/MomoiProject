using System;
using System.Collections.Generic;
using UnityEngine;

namespace GBible
{
    // 게임 오디오: 어디서든 부른다. 게임이 시작되면 오디오 오브젝트가 자동으로 만들어져 씬이 바뀌어도 계속 돈다.
    //   GameAudio.PlayBgm(clip);            // 브금: 바뀌면 이전 곡이 페이드 아웃, 새 곡이 페이드 인
    //   GameAudio.PlayVoice(clip);          // 목소리: 한 번에 하나, 바뀌면 짧게 교차
    //   GameAudio.PlaySfx(clip);            // 효과음: 튼 만큼 동시에
    //   GameAudio.PlayUi(clip);             // UI 소리: 튼 만큼 동시에
    //   GameAudio.SetVolume(AudioChannel.Bgm, 0.5f);   // 채널 음량(저장됨), 전체는 AudioChannel.Master
    // 시간은 게임 시간과 상관없이 흐른다(일시정지 중에도 페이드·재생 계속).
    public static class GameAudio
    {
        static GameAudioHost _host;
        static GameAudioSettings _settings;
        static readonly float[] Volumes = { 1, 1, 1, 1, 1 };
        static readonly bool[] Muted = new bool[5];
        static bool _volumesLoaded;

        const string PrefsPrefix = "GBible.Audio.";

        // 음량이 바뀌었을 때(설정 화면 갱신용).
        public static event Action<AudioChannel> VolumeChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _host = null;
            _settings = null;
            _volumesLoaded = false;
            VolumeChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() => EnsureHost();

        public static GameAudioSettings Settings
        {
            get
            {
                if (_settings == null) _settings = GameAudioSettings.Load();
                return _settings;
            }
        }

        // 살아 있는 오디오 오브젝트(파괴됐으면 null: 플레이 모드가 끝나는 등).
        static GameAudioHost Live => _host != null ? _host : null;

        static GameAudioHost EnsureHost()
        {
            if (_host != null) return _host;
            LoadVolumes();
            var go = new GameObject("[오디오]");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(go);
            else go.hideFlags = HideFlags.HideAndDontSave;
            _host = go.AddComponent<GameAudioHost>();
            _host.Setup(Settings);
            return _host;
        }

        // 오디오 오브젝트를 없앤다(테스트·에디터 미리 듣기 뒷정리).
        public static void Shutdown()
        {
            if (_host == null)
            {
                _host = null;
                return;
            }
            if (Application.isPlaying) UnityEngine.Object.Destroy(_host.gameObject);
            else UnityEngine.Object.DestroyImmediate(_host.gameObject);
            _host = null;
        }

        // ── 재생 ─────────────────────────────────────────────

        public static AudioHandle Play(AudioClip clip, AudioChannel channel, float volume = 1f, float pitch = 1f)
        {
            switch (channel)
            {
                case AudioChannel.Bgm: return PlayBgm(clip, volume);
                case AudioChannel.Voice: return PlayVoice(clip, volume);
                case AudioChannel.Ui: return PlayUi(clip, volume, pitch);
                default: return PlaySfx(clip, volume, pitch);
            }
        }

        // 브금. 같은 곡이 이미 나오고 있으면 음량만 맞춘다(restart면 처음부터). fade 음수면 설정값.
        public static AudioHandle PlayBgm(AudioClip clip, float volume = 1f, float fade = -1f, bool loop = true, bool restart = false) =>
            clip == null ? null : EnsureHost().Serial(AudioChannel.Bgm).Play(clip, volume, loop, Fade(AudioChannel.Bgm, fade), restart);

        public static void StopBgm(float fade = -1f) => Live?.Serial(AudioChannel.Bgm).Stop(Fade(AudioChannel.Bgm, fade));

        // 목소리. 새 목소리가 나오면 이전 목소리는 짧게 사라진다.
        public static AudioHandle PlayVoice(AudioClip clip, float volume = 1f, float fade = -1f) =>
            clip == null ? null : EnsureHost().Serial(AudioChannel.Voice).Play(clip, volume, false, Fade(AudioChannel.Voice, fade), true);

        public static void StopVoice(float fade = -1f) => Live?.Serial(AudioChannel.Voice).Stop(Fade(AudioChannel.Voice, fade));

        public static AudioHandle PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f) =>
            clip == null ? null : EnsureHost().Parallel(AudioChannel.Sfx).Play(clip, volume, pitch, null);

        // 위치가 있는 효과음(3D).
        public static AudioHandle PlaySfxAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f) =>
            clip == null ? null : EnsureHost().Parallel(AudioChannel.Sfx).Play(clip, volume, pitch, position);

        public static AudioHandle PlayUi(AudioClip clip, float volume = 1f, float pitch = 1f) =>
            clip == null ? null : EnsureHost().Parallel(AudioChannel.Ui).Play(clip, volume, pitch, null);

        // 채널의 소리를 모두 멈춘다(전체면 모든 채널).
        public static void Stop(AudioChannel channel, float fade = 0f)
        {
            var host = Live;
            if (host == null) return;
            foreach (var c in Channels(channel))
            {
                if (c == AudioChannel.Bgm || c == AudioChannel.Voice) host.Serial(c).Stop(fade);
                else host.Parallel(c).StopAll(fade);
            }
        }

        public static void StopAll(float fade = 0f) => Stop(AudioChannel.Master, fade);

        public static AudioClip CurrentBgm => Live?.Serial(AudioChannel.Bgm).Current;
        public static AudioClip CurrentVoice => Live?.Serial(AudioChannel.Voice).Current;
        public static bool IsVoicePlaying => CurrentVoice != null;

        // 지금 내는 소리 수(채널별).
        public static int Playing(AudioChannel channel)
        {
            var host = Live;
            if (host == null) return 0;
            var count = 0;
            foreach (var c in Channels(channel))
                count += c == AudioChannel.Bgm || c == AudioChannel.Voice ? (host.Serial(c).Current != null ? 1 : 0) : host.Parallel(c).Busy;
            return count;
        }

        // 시간을 흘린다(오디오 오브젝트가 매 프레임 부른다. 테스트는 직접 부른다).
        public static void Tick(float deltaTime) => Live?.Step(deltaTime);

        static IEnumerable<AudioChannel> Channels(AudioChannel channel)
        {
            if (channel != AudioChannel.Master)
            {
                yield return channel;
                yield break;
            }
            yield return AudioChannel.Bgm;
            yield return AudioChannel.Voice;
            yield return AudioChannel.Sfx;
            yield return AudioChannel.Ui;
        }

        static float Fade(AudioChannel channel, float fade)
        {
            if (fade >= 0) return fade;
            var settings = Settings;
            if (settings == null) return channel == AudioChannel.Bgm ? 1f : 0.15f;
            return channel == AudioChannel.Bgm ? settings.bgmFade : settings.voiceFade;
        }

        // ── 음량 ─────────────────────────────────────────────

        public static float Volume(AudioChannel channel)
        {
            LoadVolumes();
            return Volumes[(int)channel];
        }

        public static void SetVolume(AudioChannel channel, float volume, bool save = true)
        {
            LoadVolumes();
            Volumes[(int)channel] = Mathf.Clamp01(volume);
            if (save && (Settings == null || Settings.saveVolumes))
            {
                PlayerPrefs.SetFloat(PrefsPrefix + channel, Volumes[(int)channel]);
                PlayerPrefs.Save();
            }
            Live?.RefreshVolumes();
            VolumeChanged?.Invoke(channel);
        }

        public static bool IsMuted(AudioChannel channel) => Muted[(int)channel];

        public static void SetMuted(AudioChannel channel, bool muted)
        {
            Muted[(int)channel] = muted;
            Live?.RefreshVolumes();
            VolumeChanged?.Invoke(channel);
        }

        // 실제로 곱해지는 음량: 전체 × 채널(음소거면 0).
        public static float Effective(AudioChannel channel)
        {
            LoadVolumes();
            if (Muted[0] || Muted[(int)channel]) return 0f;
            return Volumes[0] * (channel == AudioChannel.Master ? 1f : Volumes[(int)channel]);
        }

        static void LoadVolumes()
        {
            if (_volumesLoaded) return;
            _volumesLoaded = true;
            var settings = Settings;
            foreach (AudioChannel channel in Enum.GetValues(typeof(AudioChannel)))
            {
                var fallback = settings != null ? settings.DefaultVolume(channel) : 1f;
                var saved = settings == null || settings.saveVolumes ? PlayerPrefs.GetFloat(PrefsPrefix + channel, fallback) : fallback;
                Volumes[(int)channel] = Mathf.Clamp01(saved);
            }
        }
    }

    // 재생 중인 소리 하나. Stop으로 멈춘다(페이드 가능).
    public sealed class AudioHandle
    {
        internal AudioVoice Voice;
        internal int Serial;

        public AudioChannel Channel { get; internal set; }
        public AudioClip Clip { get; internal set; }

        // 이 소리를 낸 자리(AudioSource: 3D 설정 등을 더 만질 때). 소리가 끝나면 다른 소리에 다시 쓰일 수 있다.
        public AudioSource Source => Voice?.Source;

        // 아직 이 소리를 내고 있는지(같은 소리 자리가 다른 소리에 쓰이면 false).
        public bool IsPlaying => Voice != null && Voice.Serial == Serial && Voice.Busy;

        public void Stop(float fade = 0f)
        {
            if (IsPlaying) Voice.FadeTo(0f, fade, true);
        }
    }
}
