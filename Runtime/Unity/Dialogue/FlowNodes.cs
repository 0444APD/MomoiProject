using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using UnityEngine;

namespace GameSystems.Dialogue
{
    // 대화에 들어오는 곳. 코드·다른 대화가 이 ID로 시작한다.
    [Serializable, GraphNode("시작", Category = "흐름", Order = -10)]
    public sealed class StartNode : GraphNode
    {
        public const string NextPort = "next";
        public const string StartedPort = "started";

        [FieldLabel("시작 ID(한 대화 안에서 겹치지 않게)")] public string entryId = "start";

        public override string Summary => entryId;

        public override IEnumerable<PortInfo> Ports
        {
            get
            {
                yield return PortInfo.SignalOut(StartedPort, "시작 신호");
                yield return PortInfo.FlowOut(NextPort, "다음");
            }
        }
    }

    [Serializable]
    public sealed class ChoiceOption
    {
        [HideInInspector] public string id = GraphNode.NewId();
        // 번역 시트 키(자동, 바꾸지 않는다).
        [HideInInspector] public string lineId = DialogueIds.NewLineId();
        [FieldLabel("선택지 글"), TextArea(1, 4)] public string text = "";
        [FieldLabel("보일 조건(비우면 항상)"), SerializeReference] public Condition condition;
        [FieldLabel("조건이 안 맞아도 흐리게 보여 준다")] public bool showWhenLocked;
    }

    // 대사 한 줄. 선택지가 있으면 선택지마다 나가는 흐름 점이 생긴다.
    //  신호: 진입(이 줄에 들어옴) · 타이핑 완료 · 넘김(다음으로 가기 직전)
    //  '대사 시작'에 신호를 이으면 그 신호가 올 때까지 글자가 나오지 않는다(연출이 끝난 뒤 말하기).
    [Serializable, GraphNode("대사", Category = "흐름", Order = -9)]
    public sealed class LineNode : GraphNode
    {
        public const string NextPort = "next";
        public const string TextStartPort = "textStart";
        public const string EnterPort = "enter";
        public const string TypedPort = "typed";
        public const string LeavePort = "leave";
        public const string OptionPrefix = "opt:";

        [FieldLabel("화자(비우면 해설)"), DataRef(typeof(CharacterData))] public string speaker = "";
        [FieldLabel("화자 표정(비우면 그대로)")] public string expression = "";
        [FieldLabel("대사(원문)"), TextArea(2, 10)] public string text = "";
        [FieldLabel("선택지")] public List<ChoiceOption> options = new List<ChoiceOption>();
        [FieldLabel("타이핑 속도(초당 글자, 0이면 기본)"), Min(0)] public float typingSpeed;
        [FieldLabel("자동 넘김(초, 0 미만이면 클릭 대기)")] public float autoAdvance = -1f;
        // 번역 시트 키(자동, 바꾸지 않는다).
        [HideInInspector] public string lineId = DialogueIds.NewLineId();

        public override string Summary => string.IsNullOrEmpty(speaker) ? "해설" : speaker;

        public override IEnumerable<PortInfo> Ports
        {
            get
            {
                yield return PortInfo.FlowIn();
                yield return PortInfo.SignalIn(TextStartPort, "대사 시작");
                yield return PortInfo.SignalOut(EnterPort, "진입");
                yield return PortInfo.SignalOut(TypedPort, "타이핑 완료");
                yield return PortInfo.SignalOut(LeavePort, "넘김");
                if (options.Count == 0) yield return PortInfo.FlowOut(NextPort, "다음");
                else
                    for (var i = 0; i < options.Count; i++)
                        yield return PortInfo.FlowOut(OptionPrefix + options[i].id, string.IsNullOrEmpty(options[i].text) ? $"선택지 {i + 1}" : options[i].text);
            }
        }
    }

    [Serializable, GraphNode("조건 분기", Category = "흐름")]
    public sealed class BranchNode : GraphNode
    {
        public const string TruePort = "true";
        public const string FalsePort = "false";

        [FieldLabel("조건"), SerializeReference] public Condition condition;

        public override IEnumerable<PortInfo> Ports
        {
            get
            {
                yield return PortInfo.FlowIn();
                yield return PortInfo.FlowOut(TruePort, "참");
                yield return PortInfo.FlowOut(FalsePort, "거짓");
            }
        }
    }

    // 플레이어 데이터를 바꾼다(골드 지급, 아이템 지급 …). 도전과제 보상과 같은 블록을 쓴다.
    [Serializable, GraphNode("값 바꾸기", Category = "흐름")]
    public sealed class SetNode : GraphNode
    {
        public const string NextPort = "next";

        [FieldLabel("바꿀 값"), SerializeReference] public List<Reward> rewards = new List<Reward>();

        public override IEnumerable<PortInfo> Ports
        {
            get
            {
                yield return PortInfo.FlowIn();
                yield return PortInfo.FlowOut(NextPort, "다음");
            }
        }
    }

    // 게임 코드에 신호를 보낸다(DialogueRunner.Signaled). 상점 열기, 퀘스트 시작 등.
    [Serializable, GraphNode("신호 보내기", Category = "흐름")]
    public sealed class SignalNode : GraphNode
    {
        public const string NextPort = "next";

        [FieldLabel("신호 이름")] public string signal = "";

        public override string Summary => signal;

        public override IEnumerable<PortInfo> Ports
        {
            get
            {
                yield return PortInfo.FlowIn();
                yield return PortInfo.FlowOut(NextPort, "다음");
            }
        }
    }

    [Serializable, GraphNode("다른 대화로", Category = "흐름")]
    public sealed class JumpNode : GraphNode
    {
        [FieldLabel("대화(비우면 이 대화)"), DataRef(typeof(DialogueData))] public string dialogue = "";
        [FieldLabel("시작 ID")] public string entryId = "start";

        public override string Summary => $"{(string.IsNullOrEmpty(dialogue) ? "이 대화" : dialogue)} · {entryId}";

        public override IEnumerable<PortInfo> Ports
        {
            get { yield return PortInfo.FlowIn(); }
        }
    }

    [Serializable, GraphNode("끝", Category = "흐름")]
    public sealed class EndNode : GraphNode
    {
        public override IEnumerable<PortInfo> Ports
        {
            get { yield return PortInfo.FlowIn(); }
        }
    }

    public static class DialogueIds
    {
        // 번역 시트 키. 구조 의미가 없는 고정 ID(대화·노드를 옮기거나 이름을 바꿔도 그대로).
        public static string NewLineId() => "line." + Guid.NewGuid().ToString("N").Substring(0, 10);
    }
}
