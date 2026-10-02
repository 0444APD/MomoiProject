using System.Collections.Generic;
using UnityEngine;

namespace GBible
{
    // 오디오 오브젝트(GameAudio가 만든다). 채널마다 소리 자리(AudioSource)를 두고 페이드·음량을 매 프레임 맞춘다.
    [AddComponentMenu("")]
    public sealed class GameAudioHost : MonoBehaviour
    {
        readonly Dictionary<AudioChannel, SerialTrack> _serial = new Dictionary<AudioChannel, SerialTrack>();
        readonly Dictionary<AudioChannel, ParallelPool> _parallel = new Dictionary<AudioChannel, ParallelPool>();
        GameAudioSettings _settings;

        // 오디오 시계(게임 시간과 무관).
        internal float Now { get; private set; }

        internal void Setup(GameAudioSettings settings)
        {
            _settings = settings;
            var max = settings != null ? settings.maxParallel : 32;
            _serial[AudioChannel.Bgm] = new SerialTrack(this, AudioChannel.Bgm);
            _serial[AudioChannel.Voice] = new SerialTrack(this, AudioChannel.Voice);
            _parallel[AudioChannel.Sfx] = new ParallelPool(this, AudioChannel.Sfx, max);
            _parallel[AudioChannel.Ui] = new ParallelPool(this, AudioChannel.Ui, max);
        }

        internal SerialTrack Serial(AudioChannel channel) => _serial[channel];
        internal ParallelPool Parallel(AudioChannel channel) => _parallel[channel];

        void Update() => Step(Time.unscaledDeltaTime);

        internal void Step(float deltaTime)
        {
            Now += Mathf.Max(0, deltaTime);
            foreach (var track in _serial.Values) track.Step(deltaTime);
            foreach (var pool in _parallel.Values) pool.Step(deltaTime);
        }

        internal void RefreshVolumes()
        {
            foreach (var track in _serial.Values) track.Step(0);
            foreach (var pool in _parallel.Values) pool.Step(0);
        }

        internal AudioVoice NewVoice(AudioChannel channel, string voiceName)
        {
            var go = new GameObject(voiceName);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.outputAudioMixerGroup = _settings != null ? _settings.Group(channel) : null;
            return new AudioVoice(this, channel, source);
        }
    }

    // 소리 자리 하나: AudioSource + 기본 음량 + 페이드. 재생 여부는 AudioSource가 아니라 자기 시계로 판단한다(오디오가 꺼진 환경에서도 같게).
    sealed class AudioVoice
    {
        readonly GameAudioHost _host;
        public readonly AudioChannel Channel;
        public readonly AudioSource Source;
        public float BaseVolume = 1f;
        public float Level = 1f;
        public int Serial;
        public float StartedAt;
        float _busyUntil;
        float _fadeFrom;
        float _fadeTo;
        float _fadeTime;
        float _fadeDuration;
        bool _stopAtFadeEnd;

        public AudioVoice(GameAudioHost host, AudioChannel channel, AudioSource source)
        {
            _host = host;
            Channel = channel;
            Source = source;
        }

        public bool Busy => _host.Now < _busyUntil;

        public AudioHandle Start(AudioClip clip, float volume, float pitch, bool loop, float fadeIn)
        {
            Serial++;
            Source.clip = clip;
            Source.loop = loop;
            Source.pitch = pitch <= 0 ? 1f : pitch;
            BaseVolume = volume;
            StartedAt = _host.Now;
            _busyUntil = loop ? float.PositiveInfinity : _host.Now + clip.length / Mathf.Abs(Source.pitch);
            _stopAtFadeEnd = false;
            _fadeDuration = 0;
            Level = fadeIn > 0 ? 0f : 1f;
            if (fadeIn > 0) FadeTo(1f, fadeIn, false);
            Apply();
            Source.Play();
            return new AudioHandle { Voice = this, Serial = Serial, Channel = Channel, Clip = clip };
        }

        public void FadeTo(float level, float duration, bool stopAtEnd)
        {
            _stopAtFadeEnd = stopAtEnd;
            if (duration <= 0)
            {
                Level = level;
                _fadeDuration = 0;
                if (stopAtEnd && level <= 0) Halt();
                Apply();
                return;
            }
            _fadeFrom = Level;
            _fadeTo = level;
            _fadeTime = 0;
            _fadeDuration = duration;
        }

        public void Halt()
        {
            _busyUntil = 0;
            _fadeDuration = 0;
            if (Source == null) return;
            Source.Stop();
            Source.clip = null;
        }

        public void Step(float deltaTime)
        {
            if (_fadeDuration > 0)
            {
                _fadeTime += deltaTime;
                var t = Mathf.Clamp01(_fadeTime / _fadeDuration);
                Level = Mathf.Lerp(_fadeFrom, _fadeTo, t);
                if (t >= 1f)
                {
                    _fadeDuration = 0;
                    if (_stopAtFadeEnd && Level <= 0f) Halt();
                }
            }
            Apply();
        }

        void Apply()
        {
            if (Source != null) Source.volume = BaseVolume * Level * GameAudio.Effective(Channel);
        }
    }

    // 직렬 채널(브금·목소리): 두 자리를 번갈아 써서 교차 페이드.
    sealed class SerialTrack
    {
        readonly AudioVoice _a;
        readonly AudioVoice _b;
        AudioVoice _current;

        public SerialTrack(GameAudioHost host, AudioChannel channel)
        {
            _a = host.NewVoice(channel, channel + " A");
            _b = host.NewVoice(channel, channel + " B");
            _current = _a;
        }

        public AudioClip Current => _current.Busy ? _current.Source.clip : null;

        public AudioHandle Play(AudioClip clip, float volume, bool loop, float fade, bool restart)
        {
            if (!restart && _current.Busy && _current.Source.clip == clip)
            {
                _current.BaseVolume = volume;
                _current.FadeTo(1f, fade, false);
                return new AudioHandle { Voice = _current, Serial = _current.Serial, Channel = _current.Channel, Clip = clip };
            }
            if (_current.Busy) _current.FadeTo(0f, fade, true);
            _current = _current == _a ? _b : _a;
            return _current.Start(clip, volume, 1f, loop, fade);
        }

        public void Stop(float fade)
        {
            if (_a.Busy) _a.FadeTo(0f, fade, true);
            if (_b.Busy) _b.FadeTo(0f, fade, true);
        }

        public void Step(float deltaTime)
        {
            _a.Step(deltaTime);
            _b.Step(deltaTime);
        }
    }

    // 병렬 채널(효과음·UI): 튼 만큼 자리를 늘려 동시에 낸다. 최대 수를 넘으면 가장 오래된 소리를 멈추고 쓴다.
    sealed class ParallelPool
    {
        readonly GameAudioHost _host;
        readonly AudioChannel _channel;
        readonly int _max;
        readonly List<AudioVoice> _voices = new List<AudioVoice>();

        public ParallelPool(GameAudioHost host, AudioChannel channel, int max)
        {
            _host = host;
            _channel = channel;
            _max = Mathf.Max(1, max);
        }

        public int Busy
        {
            get
            {
                var count = 0;
                foreach (var voice in _voices)
                    if (voice.Busy)
                        count++;
                return count;
            }
        }

        public AudioHandle Play(AudioClip clip, float volume, float pitch, Vector3? position)
        {
            AudioVoice chosen = null;
            foreach (var voice in _voices)
                if (!voice.Busy)
                {
                    chosen = voice;
                    break;
                }
            if (chosen == null && _voices.Count < _max)
            {
                chosen = _host.NewVoice(_channel, $"{_channel} {_voices.Count + 1}");
                _voices.Add(chosen);
            }
            if (chosen == null)
            {
                chosen = _voices[0];
                foreach (var voice in _voices)
                    if (voice.StartedAt < chosen.StartedAt)
                        chosen = voice;
                chosen.Halt();
            }
            chosen.Source.spatialBlend = position.HasValue ? 1f : 0f;
            if (position.HasValue) chosen.Source.transform.position = position.Value;
            else chosen.Source.transform.localPosition = Vector3.zero;
            return chosen.Start(clip, volume, pitch, false, 0f);
        }

        public void StopAll(float fade)
        {
            foreach (var voice in _voices)
                if (voice.Busy)
                    voice.FadeTo(0f, fade, true);
        }

        public void Step(float deltaTime)
        {
            foreach (var voice in _voices) voice.Step(deltaTime);
        }
    }
}
