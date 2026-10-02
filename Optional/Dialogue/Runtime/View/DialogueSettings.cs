using UnityEngine;

namespace GBible.Dialogue
{
    // 다이얼로그 시스템 설정(Resources/MomoiProject/DialogueSettings). 다이얼로그 UI 템플릿을 만들면 함께 만들어진다.
    [CreateAssetMenu(menuName = "MomoiProject/다이얼로그 시스템/다이얼로그 설정", fileName = "DialogueSettings")]
    public sealed class DialogueSettings : ScriptableObject
    {
        public const string ResourcesPath = "MomoiProject/DialogueSettings";

        [Tooltip("다이얼로그 재생기 프리팹(DialoguePlayer가 붙은 것). 씬에 다이얼로그 재생기가 없으면 이것을 만들어 쓴다.")]
        public GameObject playerPrefab;
        [Tooltip("게임이 시작될 때 미리 만들어 숨겨 둔다(첫 다이얼로그가 바로 뜬다). 끄면 처음 쓸 때 만든다.")]
        public bool createOnStart = true;

        public static DialogueSettings Load() => Resources.Load<DialogueSettings>(ResourcesPath);
    }
}
