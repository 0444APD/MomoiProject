using System.Collections.Generic;
using GameSystems.Data;
using UnityEngine;

namespace GameSystems.Dialogue
{
    // 지금 보이는 대사 한 줄의 정보(프로젝트 확장이 쓴다).
    public sealed class DialogueLineInfo
    {
        public LineNode Line;
        // 캐릭터 ID(해설이면 빈 값)와 프로필.
        public string Speaker;
        public StandingProfileData Character;
        public string SpeakerName;
        public string Text;
        public Color NameColor = Color.white;
        // 추가 글(키 → 글). 캐릭터의 추가 글 + 대사의 추가 글(대사가 우선), 번역이 있으면 번역.
        public IReadOnlyDictionary<string, string> Extras = new Dictionary<string, string>();
    }

    // 프로젝트마다 다른 UI를 코드로 더할 때: 대화 UI 오브젝트(또는 자식)에 이 인터페이스를 구현한 컴포넌트를 붙이면 순간마다 불린다.
    // 예: 화자 얼굴 아이콘, 음성 재생 표시, 호감도 게이지. 요소는 ui.Element("이름")으로 UXML에서 찾는다.
    // 단순히 글만 더하는 것(소속·직함)은 코드 없이 추가 글 + UXML의 dialogue-extra-<키> 요소로 된다.
    public interface IDialogueUIExtension
    {
        void OnOpened(DialogueUI ui);
        void OnClosed(DialogueUI ui);
        void OnLine(DialogueUI ui, DialogueLineInfo line);
        void OnChoices(DialogueUI ui, IReadOnlyList<DialogueChoice> choices);
    }

    // 필요한 순간만 골라 덮어쓰는 바탕 클래스.
    public abstract class DialogueUIExtension : MonoBehaviour, IDialogueUIExtension
    {
        public virtual void OnOpened(DialogueUI ui)
        {
        }

        public virtual void OnClosed(DialogueUI ui)
        {
        }

        public virtual void OnLine(DialogueUI ui, DialogueLineInfo line)
        {
        }

        public virtual void OnChoices(DialogueUI ui, IReadOnlyList<DialogueChoice> choices)
        {
        }
    }
}
