using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameSystems.Dialogue
{
    // UI Toolkit 요소용 애니메이션 에셋. Animator·AnimationClip은 UI Toolkit 요소를 움직이지 못하므로 이것을 쓴다.
    // 채널마다 곡선(가로 = 0~1 진행, 세로 = 값)을 그리면 키프레임 애니메이션이 된다. 스프라이트 프레임 애니메이션도 된다.
    // 간단한 나타나기·사라지기는 USS 전환(transition) + 상태 클래스로 충분하고, 반복·키프레임·프레임 애니메이션에 이 에셋을 쓴다.
    [CreateAssetMenu(menuName = "Game Systems/대화/UI 모션", fileName = "NewUiMotion")]
    public sealed class UiMotion : ScriptableObject
    {
        public enum LoopMode
        {
            [InspectorName("한 번")] Once,
            [InspectorName("반복")] Loop,
            [InspectorName("왕복 반복")] PingPong,
        }

        [Min(0.01f)] public float duration = 0.3f;
        public LoopMode loop = LoopMode.Once;
        [Tooltip("끝나면(또는 멈추면) 이 모션이 바꾼 값을 원래대로 돌린다.")]
        public bool restoreWhenDone;

        public bool opacity;
        public AnimationCurve opacityCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Tooltip("이동(픽셀)")]
        public bool move;
        public AnimationCurve moveX = AnimationCurve.Constant(0, 1, 0);
        public AnimationCurve moveY = AnimationCurve.EaseInOut(0, 20, 1, 0);

        public bool scale;
        public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0.9f, 1, 1);

        [Tooltip("회전(도)")]
        public bool rotate;
        public AnimationCurve rotateCurve = AnimationCurve.Constant(0, 1, 0);

        public bool tint;
        public Gradient tintGradient = new Gradient();

        [Tooltip("배경 이미지를 차례로 바꾼다(프레임 애니메이션). 진행 시간 동안 한 바퀴.")]
        public Sprite[] frames = Array.Empty<Sprite>();

        public bool IsLooping => loop != LoopMode.Once;

        // 요소에 t(0~1) 시점의 모습을 적용한다.
        public void Apply(VisualElement element, float t)
        {
            if (opacity) element.style.opacity = opacityCurve.Evaluate(t);
            if (move) element.style.translate = new Translate(moveX.Evaluate(t), moveY.Evaluate(t));
            if (scale)
            {
                var s = scaleCurve.Evaluate(t);
                element.style.scale = new Scale(new Vector2(s, s));
            }
            if (rotate) element.style.rotate = new Rotate(rotateCurve.Evaluate(t));
            if (tint) element.style.unityBackgroundImageTintColor = tintGradient.Evaluate(t);
            if (frames != null && frames.Length > 0)
            {
                var index = Mathf.Clamp(Mathf.FloorToInt(t * frames.Length), 0, frames.Length - 1);
                element.style.backgroundImage = new StyleBackground(frames[index]);
            }
        }

        // 이 모션이 건드린 인라인 스타일을 지운다(USS 값으로 돌아간다).
        public void Restore(VisualElement element)
        {
            if (opacity) element.style.opacity = StyleKeyword.Null;
            if (move) element.style.translate = StyleKeyword.Null;
            if (scale) element.style.scale = StyleKeyword.Null;
            if (rotate) element.style.rotate = StyleKeyword.Null;
            if (tint) element.style.unityBackgroundImageTintColor = StyleKeyword.Null;
            if (frames != null && frames.Length > 0) element.style.backgroundImage = StyleKeyword.Null;
        }

        // 경과 시간 → 진행(0~1). 한 번이면 1에서 멈춘다.
        public float Progress(float elapsed)
        {
            var raw = elapsed / Mathf.Max(0.01f, duration);
            switch (loop)
            {
                case LoopMode.Loop: return raw - Mathf.Floor(raw);
                case LoopMode.PingPong: return Mathf.PingPong(raw, 1f);
                default: return Mathf.Clamp01(raw);
            }
        }
    }

    // 요소별로 모션을 재생한다(요소 하나에 모션 하나, 새로 틀면 이전 것을 멈춘다). 매 프레임 Update를 부른다.
    public sealed class UiMotionRunner
    {
        sealed class Playing
        {
            public VisualElement Element;
            public UiMotion Motion;
            public float Elapsed;
            public Action Done;
        }

        readonly List<Playing> _playing = new List<Playing>();

        public int Count => _playing.Count;

        public void Play(VisualElement element, UiMotion motion, Action done = null)
        {
            if (element == null || motion == null)
            {
                done?.Invoke();
                return;
            }
            Stop(element);
            var playing = new Playing { Element = element, Motion = motion, Done = done };
            _playing.Add(playing);
            motion.Apply(element, 0f);
        }

        public bool IsPlaying(VisualElement element) => _playing.Exists(p => p.Element == element);

        public void Stop(VisualElement element)
        {
            for (var i = _playing.Count - 1; i >= 0; i--)
            {
                var p = _playing[i];
                if (p.Element != element) continue;
                _playing.RemoveAt(i);
                if (p.Motion.restoreWhenDone) p.Motion.Restore(p.Element);
                p.Done?.Invoke();
            }
        }

        // 바로 끝 모습으로(한 번 모션). 반복 모션은 멈춘다.
        public void Finish(VisualElement element)
        {
            for (var i = _playing.Count - 1; i >= 0; i--)
            {
                var p = _playing[i];
                if (p.Element != element) continue;
                if (!p.Motion.IsLooping) p.Motion.Apply(p.Element, 1f);
                Stop(element);
                return;
            }
        }

        public void StopAll()
        {
            foreach (var p in _playing.ToArray()) Stop(p.Element);
        }

        public void Update(float deltaTime)
        {
            foreach (var p in _playing.ToArray())
            {
                if (!_playing.Contains(p)) continue;
                p.Elapsed += deltaTime;
                var t = p.Motion.Progress(p.Elapsed);
                p.Motion.Apply(p.Element, t);
                if (!p.Motion.IsLooping && p.Elapsed >= p.Motion.duration)
                {
                    _playing.Remove(p);
                    if (p.Motion.restoreWhenDone) p.Motion.Restore(p.Element);
                    p.Done?.Invoke();
                }
            }
        }
    }
}
