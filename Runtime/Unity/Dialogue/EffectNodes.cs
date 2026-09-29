using System;
using System.Collections.Generic;
using GameSystems.Data;
using UnityEngine;

namespace GameSystems.Dialogue
{
    // 효과 하나가 진행 중 내는 신호 지점(예: 50%).
    [Serializable]
    public sealed class ProgressMark
    {
        [HideInInspector] public string id = GraphNode.NewId();
        [FieldLabel("진행 %"), Range(0, 100)] public float percent = 50f;
    }

    // 연출 효과. '재생' 신호를 받으면 delay 뒤 시작해 duration 동안 진행하고, 시작·진행 지점·완료 때 신호를 낸다.
    // 그 신호에 다른 효과를 이어 달아 체인을 만든다.
    //  · 상태 효과(IsState): 배경·스탠딩·카메라처럼 유지되는 모습을 바꾼다(ApplyTo). 스킵하면 끝 모습으로 바로 간다.
    //  · 큐 효과: 흔들기·효과음처럼 한 번 일어나고 끝난다.
    [Serializable]
    public abstract class EffectNode : GraphNode
    {
        public const string PlayPort = "play";
        public const string StartedPort = "started";
        public const string DonePort = "done";
        public const string MarkPrefix = "mark:";

        [FieldLabel("시작 전 기다림(초)"), Min(0)] public float delay;
        [FieldLabel("진행 시간(초)"), Min(0)] public float duration = 0.4f;
        [FieldLabel("움직임 곡선")] public Ease ease = Ease.OutQuad;
        [FieldLabel("진행 신호 지점")] public List<ProgressMark> marks = new List<ProgressMark>();
        [FieldLabel("다음 대사로 넘어가도 계속(끄면 넘길 때 바로 끝낸다)")] public bool keepAcrossLines;

        public virtual bool IsState => false;
        public virtual bool HasDuration => true;

        // 상태 효과: 끝 모습을 state에 적용한다.
        public virtual void ApplyTo(StageState state, EffectContext context)
        {
        }

        public override IEnumerable<PortInfo> Ports
        {
            get
            {
                yield return PortInfo.SignalIn(PlayPort, "재생");
                yield return PortInfo.SignalOut(StartedPort, "시작");
                foreach (var mark in marks)
                    yield return PortInfo.SignalOut(MarkPrefix + mark.id, $"{mark.percent:0.#}%");
                yield return PortInfo.SignalOut(DonePort, "완료");
            }
        }
    }

    // 효과가 가리키는 대상을 푸는 데 필요한 것(현재 화자 등).
    public sealed class EffectContext
    {
        // 지금 대사의 화자(캐릭터 ID). 캐릭터 칸을 비운 효과는 화자를 가리킨다.
        public string Speaker;

        public string Character(string id) => string.IsNullOrEmpty(id) ? Speaker : id;
    }

    // ── 상태 효과 ────────────────────────────────────────────

    [Serializable, GraphNode("배경", Category = "화면", Graphs = GraphKinds.Both)]
    public sealed class BackgroundEffect : EffectNode
    {
        [FieldLabel("배경 이미지(비우면 배경 없음)")] public Sprite sprite;
        [FieldLabel("검은 화면을 거쳐 바꾼다")] public bool viaBlack;
        public override bool IsState => true;
        public override string Summary => sprite != null ? sprite.name : "없음";
        public override void ApplyTo(StageState state, EffectContext context) => state.background = sprite;
    }

    [Serializable, GraphNode("배경 어둡게", Category = "화면", Graphs = GraphKinds.Both)]
    public sealed class DimEffect : EffectNode
    {
        [FieldLabel("어둡기(0~1)"), Range(0, 1)] public float amount = 0.5f;
        public override bool IsState => true;
        public override string Summary => $"{amount:0.##}";
        public override void ApplyTo(StageState state, EffectContext context) => state.dim = amount;
    }

    [Serializable, GraphNode("레터박스", Category = "화면", Graphs = GraphKinds.Both)]
    public sealed class LetterboxEffect : EffectNode
    {
        [FieldLabel("위아래 띠 두께(화면 높이 비율)"), Range(0, 0.5f)] public float thickness = 0.1f;
        public override bool IsState => true;
        public override string Summary => $"{thickness:0.##}";
        public override void ApplyTo(StageState state, EffectContext context) => state.letterbox = thickness;
    }

    [Serializable, GraphNode("카메라", Category = "화면", Graphs = GraphKinds.Both)]
    public sealed class CameraEffect : EffectNode
    {
        [FieldLabel("확대 %(100 = 원래 크기)"), Min(10)] public float zoomPercent = 100f;
        [FieldLabel("이동(화면 비율)")] public Vector2 offset;
        public override bool IsState => true;
        public override string Summary => $"{zoomPercent:0}%";

        public override void ApplyTo(StageState state, EffectContext context)
        {
            state.cameraZoom = zoomPercent / 100f;
            state.cameraOffset = offset;
        }
    }

    [Serializable, GraphNode("배경 음악", Category = "소리", Graphs = GraphKinds.Both)]
    public sealed class MusicEffect : EffectNode
    {
        [FieldLabel("음악(비우면 멈춤)")] public AudioClip clip;
        [FieldLabel("음량"), Range(0, 1)] public float volume = 1f;
        public override bool IsState => true;
        public override string Summary => clip != null ? clip.name : "멈춤";

        public override void ApplyTo(StageState state, EffectContext context)
        {
            state.music = clip;
            state.musicVolume = volume;
        }
    }

    [Serializable, GraphNode("스탠딩 등장", Category = "스탠딩", Graphs = GraphKinds.Both)]
    public sealed class StandingEnterEffect : EffectNode
    {
        [FieldLabel("캐릭터(비우면 현재 화자)"), DataRef(typeof(CharacterData))] public string character = "";
        [FieldLabel("표정(비우면 기본)")] public string expression = "";
        [FieldLabel("자리")] public StandingSlot slot = StandingSlot.Center;
        [FieldLabel("직접 지정 위치(0~1)")] public float x = 0.5f;
        [FieldLabel("강조")] public bool highlighted = true;
        public override bool IsState => true;
        public override string Summary => $"{(string.IsNullOrEmpty(character) ? "화자" : character)} · {slot}";

        public override void ApplyTo(StageState state, EffectContext context)
        {
            var id = context.Character(character);
            if (string.IsNullOrEmpty(id)) return;
            var standing = state.Standing(id);
            if (standing == null) state.standings.Add(standing = new StandingState { character = id });
            standing.expression = expression;
            standing.x = StandingSlots.X(slot, x);
            standing.highlighted = highlighted;
        }
    }

    [Serializable, GraphNode("스탠딩 퇴장", Category = "스탠딩", Graphs = GraphKinds.Both)]
    public sealed class StandingExitEffect : EffectNode
    {
        [FieldLabel("캐릭터(비우면 현재 화자)"), DataRef(typeof(CharacterData))] public string character = "";
        public override bool IsState => true;
        public override string Summary => string.IsNullOrEmpty(character) ? "화자" : character;

        public override void ApplyTo(StageState state, EffectContext context)
        {
            var id = context.Character(character);
            state.standings.RemoveAll(s => s.character == id);
        }
    }

    [Serializable, GraphNode("표정", Category = "스탠딩", Graphs = GraphKinds.Both)]
    public sealed class ExpressionEffect : EffectNode
    {
        [FieldLabel("캐릭터(비우면 현재 화자)"), DataRef(typeof(CharacterData))] public string character = "";
        [FieldLabel("표정")] public string expression = "";
        public override bool IsState => true;
        public override string Summary => $"{(string.IsNullOrEmpty(character) ? "화자" : character)} · {expression}";

        public override void ApplyTo(StageState state, EffectContext context)
        {
            var standing = state.Standing(context.Character(character));
            if (standing != null) standing.expression = expression;
        }
    }

    [Serializable, GraphNode("스탠딩 이동", Category = "스탠딩", Graphs = GraphKinds.Both)]
    public sealed class StandingMoveEffect : EffectNode
    {
        [FieldLabel("캐릭터(비우면 현재 화자)"), DataRef(typeof(CharacterData))] public string character = "";
        [FieldLabel("자리")] public StandingSlot slot = StandingSlot.Center;
        [FieldLabel("직접 지정 위치(0~1)")] public float x = 0.5f;
        public override bool IsState => true;
        public override string Summary => $"{(string.IsNullOrEmpty(character) ? "화자" : character)} → {slot}";

        public override void ApplyTo(StageState state, EffectContext context)
        {
            var standing = state.Standing(context.Character(character));
            if (standing != null) standing.x = StandingSlots.X(slot, x);
        }
    }

    [Serializable, GraphNode("강조", Category = "스탠딩", Graphs = GraphKinds.Both)]
    public sealed class HighlightEffect : EffectNode
    {
        [FieldLabel("캐릭터(비우면 현재 화자)"), DataRef(typeof(CharacterData))] public string character = "";
        [FieldLabel("강조")] public bool on = true;
        [FieldLabel("나머지는 강조 끄기")] public bool onlyThis = true;
        public override bool IsState => true;
        public override string Summary => $"{(string.IsNullOrEmpty(character) ? "화자" : character)} {(on ? "켜기" : "끄기")}";

        public override void ApplyTo(StageState state, EffectContext context)
        {
            var id = context.Character(character);
            foreach (var standing in state.standings)
            {
                if (standing.character == id) standing.highlighted = on;
                else if (onlyThis && on) standing.highlighted = false;
            }
        }
    }

    // ── 큐 효과(한 번 일어나고 끝) ─────────────────────────

    [Serializable, GraphNode("흔들기", Category = "움직임", Graphs = GraphKinds.Both)]
    public sealed class ShakeEffect : EffectNode
    {
        [FieldLabel("캐릭터를 흔든다(끄면 화면 전체)")] public bool shakeCharacter;
        [FieldLabel("캐릭터(비우면 현재 화자)"), DataRef(typeof(CharacterData))] public string character = "";
        [FieldLabel("세기(화면 높이 비율)"), Min(0)] public float strength = 0.02f;
        public override string Summary => !shakeCharacter ? "화면" : string.IsNullOrEmpty(character) ? "화자" : character;

        // 흔들 대상(캐릭터 ID). null이면 화면 전체.
        public string Target(EffectContext context) => shakeCharacter ? context.Character(character) : null;
    }

    [Serializable, GraphNode("번쩍임", Category = "화면", Graphs = GraphKinds.Both)]
    public sealed class FlashEffect : EffectNode
    {
        [FieldLabel("색")] public Color color = Color.white;
        public override string Summary => "#" + ColorUtility.ToHtmlStringRGB(color);
    }

    [Serializable, GraphNode("이펙트 생성", Category = "움직임", Graphs = GraphKinds.Both)]
    public sealed class SpawnEffect : EffectNode
    {
        [FieldLabel("프리팹")] public GameObject prefab;
        [FieldLabel("위치(화면 비율 0~1)")] public Vector2 screenPosition = new Vector2(0.5f, 0.5f);
        [FieldLabel("붙일 캐릭터(비우면 화면 위치)"), DataRef(typeof(CharacterData))] public string character = "";
        public override string Summary => prefab != null ? prefab.name : "없음";
    }

    [Serializable, GraphNode("효과음", Category = "소리", Graphs = GraphKinds.Both)]
    public sealed class SoundEffect : EffectNode
    {
        [FieldLabel("소리")] public AudioClip clip;
        [FieldLabel("음량"), Range(0, 1)] public float volume = 1f;
        public override bool HasDuration => false;
        public override string Summary => clip != null ? clip.name : "없음";
    }

    [Serializable, GraphNode("기다리기", Category = "흐름 제어", Graphs = GraphKinds.Both)]
    public sealed class WaitEffect : EffectNode
    {
        public WaitEffect()
        {
            duration = 1f;
        }

        public override string Summary => $"{delay + duration:0.##}초";
    }

    // 연출 프리셋을 노드 하나로 쓴다. 포트: 재생 · 시작 · 프리셋이 내보낸 신호들 · 완료(안의 효과가 모두 끝남).
    [Serializable, GraphNode("연출 프리셋", Category = "프리셋", Graphs = GraphKinds.Both, Order = -1)]
    public sealed class PresetEffect : EffectNode
    {
        [FieldLabel("프리셋"), DataRef(typeof(EffectPresetData))] public string preset = "";
        [FieldLabel("대상 캐릭터(프리셋 안에서 캐릭터를 비운 효과가 가리킴, 비우면 현재 화자)"), DataRef(typeof(CharacterData))] public string character = "";

        public const string OutputPrefix = "out:";

        // 편집기·실행기가 프리셋의 내보낸 신호 이름을 채운다(이름 → 포트).
        [NonSerialized] public Func<string, IEnumerable<string>> OutputsOf;
        public static Func<string, IEnumerable<string>> DefaultOutputsOf;

        public PresetEffect()
        {
            duration = 0f;
        }

        public override bool HasDuration => false;
        public override string Summary => string.IsNullOrEmpty(preset) ? "없음" : preset;

        public override IEnumerable<PortInfo> Ports
        {
            get
            {
                yield return PortInfo.SignalIn(PlayPort, "재생");
                yield return PortInfo.SignalOut(StartedPort, "시작");
                var outputs = (OutputsOf ?? DefaultOutputsOf)?.Invoke(preset);
                if (outputs != null)
                    foreach (var name in outputs)
                        yield return PortInfo.SignalOut(OutputPrefix + name, name);
                yield return PortInfo.SignalOut(DonePort, "완료");
            }
        }
    }

    // ── 프리셋 그래프 전용 ───────────────────────────────────

    // 프리셋이 재생되는 순간. 여기서 나가는 신호로 효과들을 잇는다.
    [Serializable, GraphNode("프리셋 시작", Category = "프리셋", Graphs = GraphKinds.Preset, Order = -2)]
    public sealed class PresetEntryNode : GraphNode
    {
        public const string StartPort = "start";

        public override IEnumerable<PortInfo> Ports
        {
            get { yield return PortInfo.SignalOut(StartPort, "시작"); }
        }
    }

    // 프리셋 밖으로 내보내는 신호. 대화 그래프의 프리셋 노드에 같은 이름의 점이 생긴다.
    [Serializable, GraphNode("신호 내보내기", Category = "프리셋", Graphs = GraphKinds.Preset, Order = -1)]
    public sealed class PresetOutputNode : GraphNode
    {
        public const string FirePort = "fire";
        [FieldLabel("신호 이름")] public string signal = "신호";

        public override string Summary => signal;

        public override IEnumerable<PortInfo> Ports
        {
            get { yield return PortInfo.SignalIn(FirePort, "보내기"); }
        }
    }
}

namespace GameSystems.Dialogue
{
    // 효과 진행 곡선. 진행 신호(n%)는 시간 기준이고, 화면 움직임만 곡선을 따른다.
    public enum Ease
    {
        [InspectorName("일정하게")] Linear,
        [InspectorName("천천히 시작")] InQuad,
        [InspectorName("천천히 끝")] OutQuad,
        [InspectorName("천천히 시작·끝")] InOutQuad,
        [InspectorName("살짝 넘쳤다 돌아옴")] OutBack,
        [InspectorName("튕김")] OutBounce,
    }

    public static class Easing
    {
        public static float Apply(Ease ease, float t)
        {
            t = UnityEngine.Mathf.Clamp01(t);
            switch (ease)
            {
                case Ease.InQuad: return t * t;
                case Ease.OutQuad: return 1 - (1 - t) * (1 - t);
                case Ease.InOutQuad: return t < 0.5f ? 2 * t * t : 1 - (-2 * t + 2) * (-2 * t + 2) / 2;
                case Ease.OutBack:
                    const float c1 = 1.70158f;
                    const float c3 = c1 + 1;
                    return 1 + c3 * UnityEngine.Mathf.Pow(t - 1, 3) + c1 * UnityEngine.Mathf.Pow(t - 1, 2);
                case Ease.OutBounce:
                    const float n = 7.5625f;
                    const float d = 2.75f;
                    if (t < 1 / d) return n * t * t;
                    if (t < 2 / d) return n * (t -= 1.5f / d) * t + 0.75f;
                    if (t < 2.5f / d) return n * (t -= 2.25f / d) * t + 0.9375f;
                    return n * (t -= 2.625f / d) * t + 0.984375f;
                default: return t;
            }
        }
    }
}
