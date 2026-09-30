using System.Collections;
using GameSystems.Dialogue;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSystems.Tests
{
    // 대화 관리자: 설정의 프리팹으로 재생기를 만들어 씬이 바뀌어도 남겨 둔다.
    public sealed class DialogueManagerTests
    {
        [UnityTest]
        public IEnumerator 설정의_프리팹으로_숨은_재생기를_하나만_만든다()
        {
            var template = new GameObject("TestDialoguePlayer");
            template.SetActive(false);
            template.AddComponent<DialoguePlayer>();
            var settings = ScriptableObject.CreateInstance<DialogueSettings>();
            settings.playerPrefab = template;
            Dialogues.SettingsSource = () => settings;
            try
            {
                // 씬에 이미 재생기가 있으면 그것을 쓴다: 이 테스트는 없는 상태에서 시작한다.
                Assume.That(Object.FindAnyObjectByType<DialoguePlayer>() == null);
                var player = Dialogues.Player;
                Assert.IsNotNull(player);
                Assert.AreSame(player, Dialogues.Player, "한 번만 만든다");
                Assert.AreEqual("DontDestroyOnLoad", player.gameObject.scene.name, "씬이 바뀌어도 남는다");
                Assert.IsFalse(Dialogues.IsPlaying, "만들어도 숨은 상태");
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("시작 ID"));
                Assert.IsFalse(Dialogues.Play("noSuchDialogue"));
                yield return null;
                Object.Destroy(player.gameObject);
            }
            finally
            {
                Dialogues.SettingsSource = DialogueSettings.Load;
                Object.Destroy(template);
                Object.Destroy(settings);
            }
        }
    }
}
