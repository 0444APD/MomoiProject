using UnityEngine;

namespace GameSystems.Dialogue
{
    // 대화 모듈 설정(Resources/GameSystems/DialogueSettings). 대화 UI 템플릿을 만들면 함께 만들어진다.
    [CreateAssetMenu(menuName = "Game Systems/대화/대화 설정", fileName = "DialogueSettings")]
    public sealed class DialogueSettings : ScriptableObject
    {
        public const string ResourcesPath = "GameSystems/DialogueSettings";

        [Tooltip("대화 재생기 프리팹(DialoguePlayer가 붙은 것). 씬에 대화 재생기가 없으면 이것을 만들어 쓴다.")]
        public GameObject playerPrefab;
        [Tooltip("게임이 시작될 때 미리 만들어 숨겨 둔다(첫 대화가 바로 뜬다). 끄면 처음 쓸 때 만든다.")]
        public bool createOnStart = true;

        public static DialogueSettings Load() => Resources.Load<DialogueSettings>(ResourcesPath);
    }
}
