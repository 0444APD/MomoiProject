using NUnit.Framework;
using UnityEngine;

namespace GameSystems.Tests
{
    // 오디오 채널: 브금·목소리는 하나씩(교차 페이드), 효과음·UI는 동시에. 재생 여부는 오디오 시계로 판단하므로 오디오 장치 없이도 검사된다.
    public sealed class GameAudioTests
    {
        AudioClip _a;
        AudioClip _b;
        AudioClip _short;
        float _savedMaster;
        float _savedBgm;

        [SetUp]
        public void SetUp()
        {
            GameAudio.Shutdown();
            _a = AudioClip.Create("a", 44100 * 10, 1, 44100, false);
            _b = AudioClip.Create("b", 44100 * 10, 1, 44100, false);
            _short = AudioClip.Create("short", 44100, 1, 44100, false);
            _savedMaster = GameAudio.Volume(AudioChannel.Master);
            _savedBgm = GameAudio.Volume(AudioChannel.Bgm);
        }

        [TearDown]
        public void TearDown()
        {
            GameAudio.Shutdown();
            GameAudio.SetVolume(AudioChannel.Master, _savedMaster, save: false);
            GameAudio.SetVolume(AudioChannel.Bgm, _savedBgm, save: false);
            Object.DestroyImmediate(_a);
            Object.DestroyImmediate(_b);
            Object.DestroyImmediate(_short);
        }

        static AudioSource Source(AudioHandle handle) => handle.Source;

        [Test]
        public void 브금은_하나씩_바뀔_때_교차_페이드()
        {
            var first = GameAudio.PlayBgm(_a, 1f, fade: 0f);
            Assert.AreEqual(_a, GameAudio.CurrentBgm);
            var second = GameAudio.PlayBgm(_b, 1f, fade: 1f);
            Assert.AreEqual(_b, GameAudio.CurrentBgm);
            GameAudio.Tick(0.5f);
            var effective = GameAudio.Effective(AudioChannel.Bgm);
            Assert.AreEqual(0.5f * effective, Source(first).volume, 0.02f, "이전 곡은 줄어드는 중");
            Assert.AreEqual(0.5f * effective, Source(second).volume, 0.02f, "새 곡은 커지는 중");
            GameAudio.Tick(0.6f);
            Assert.IsFalse(first.IsPlaying, "페이드 아웃이 끝나면 멈춘다");
            Assert.IsTrue(second.IsPlaying);
            Assert.AreEqual(1, GameAudio.Playing(AudioChannel.Bgm));

            var same = GameAudio.PlayBgm(_b, 0.5f, fade: 0f);
            Assert.AreSame(Source(second), Source(same), "같은 곡이면 이어서(음량만)");
            GameAudio.StopBgm(0f);
            Assert.IsNull(GameAudio.CurrentBgm);
        }

        [Test]
        public void 효과음과_UI는_튼_만큼_동시에_끝나면_빠진다()
        {
            var one = GameAudio.PlaySfx(_short);
            var two = GameAudio.PlaySfx(_short);
            GameAudio.PlayUi(_short);
            Assert.AreEqual(2, GameAudio.Playing(AudioChannel.Sfx));
            Assert.AreEqual(1, GameAudio.Playing(AudioChannel.Ui));
            Assert.AreNotSame(Source(one), Source(two));
            GameAudio.Tick(1.1f);
            Assert.AreEqual(0, GameAudio.Playing(AudioChannel.Master), "1초짜리는 1초 뒤 끝");
            var again = GameAudio.PlaySfx(_short);
            Assert.AreSame(Source(one), Source(again), "빈 자리를 다시 쓴다");
            Assert.IsFalse(one.IsPlaying, "옛 손잡이는 다른 소리를 멈추지 못한다");
        }

        [Test]
        public void 목소리는_새_목소리가_나오면_이전을_끊는다()
        {
            var first = GameAudio.PlayVoice(_a, fade: 0f);
            var second = GameAudio.PlayVoice(_b, fade: 0f);
            Assert.IsFalse(first.IsPlaying);
            Assert.AreEqual(_b, GameAudio.CurrentVoice);
            Assert.IsTrue(GameAudio.IsVoicePlaying);
            second.Stop();
            Assert.IsFalse(GameAudio.IsVoicePlaying);
        }

        [Test]
        public void 음량은_전체와_채널을_곱한다()
        {
            GameAudio.SetVolume(AudioChannel.Master, 0.5f, save: false);
            GameAudio.SetVolume(AudioChannel.Bgm, 0.4f, save: false);
            var bgm = GameAudio.PlayBgm(_a, 0.5f, fade: 0f);
            Assert.AreEqual(0.1f, Source(bgm).volume, 1e-4, "0.5 × 0.4 × 0.5");
            GameAudio.SetMuted(AudioChannel.Master, true);
            Assert.AreEqual(0f, Source(bgm).volume, 1e-4);
            GameAudio.SetMuted(AudioChannel.Master, false);
        }
    }
}
