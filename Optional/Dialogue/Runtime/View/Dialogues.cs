using System;
using GBible.Data;
using UnityEngine;

namespace GBible.Dialogue
{
    // 다이얼로그 관리자: 어디서든 Dialogues.Play("다이얼로그 ID")로 다이얼로그를 연다.
    // 씬에 DialoguePlayer가 있으면 그것을, 없으면 설정의 프리팹을 만들어 씬이 바뀌어도 남겨 두고(숨김) 필요할 때 바로 보인다.
    public static class Dialogues
    {
        static DialoguePlayer _player;

        // 설정 읽기(테스트는 바꿔 끼운다).
        public static Func<DialogueSettings> SettingsSource = DialogueSettings.Load;

        public static event Action<string> Started;
        public static event Action Ended;
        public static event Action<string> Signaled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _player = null;
            SettingsSource = DialogueSettings.Load;
            Started = null;
            Ended = null;
            Signaled = null;
        }

        // 판 저장에 다이얼로그 진행 칸이 들어가도록 세션이 만들어질 때마다 정의한다(불러오기 전에 있어야 한다).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void HookSessions()
        {
            GBibleRuntime.Started += DialoguePlayer.DefineProgress;
            DialoguePlayer.DefineProgress(GBibleRuntime.Session);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var settings = SettingsSource();
            if (settings != null && settings.createOnStart) Ensure();
        }

        // 지금 쓰는 다이얼로그 재생기(없으면 만든다). 만들 수 없으면 null.
        public static DialoguePlayer Player => Ensure();

        public static bool IsPlaying => _player != null && _player.IsPlaying;

        static DialoguePlayer Ensure()
        {
            if (_player != null) return _player;
            var found = UnityEngine.Object.FindAnyObjectByType<DialoguePlayer>();
            if (found == null)
            {
                var settings = SettingsSource();
                if (settings == null || settings.playerPrefab == null)
                {
                    Debug.LogWarning("[다이얼로그] 다이얼로그 재생기가 없습니다. 씬에 다이얼로그 UI 프리팹을 두거나 'MomoiProject › 다이얼로그 시스템 › UI 템플릿 만들기'로 설정을 만드세요.");
                    return null;
                }
                var instance = UnityEngine.Object.Instantiate(settings.playerPrefab);
                instance.name = "[다이얼로그] " + settings.playerPrefab.name;
                UnityEngine.Object.DontDestroyOnLoad(instance);
                found = instance.GetComponent<DialoguePlayer>();
                if (found == null)
                {
                    Debug.LogWarning("[다이얼로그] 설정의 프리팹에 DialoguePlayer가 없습니다.", settings);
                    UnityEngine.Object.Destroy(instance);
                    return null;
                }
            }
            Attach(found);
            return _player;
        }

        static void Attach(DialoguePlayer player)
        {
            _player = player;
            player.Signaled += signal => Signaled?.Invoke(signal);
            player.onEnded.AddListener(() => Ended?.Invoke());
        }

        public static bool Play(string dialogueId, string entryId = "start")
        {
            var player = Ensure();
            if (player == null) return false;
            if (!player.Play(dialogueId, entryId)) return false;
            Started?.Invoke(dialogueId);
            return true;
        }

        // 판을 불러온 뒤: 저장된 다이얼로그 진행이 있으면 그 대사부터 이어 간다.
        public static bool ResumeSaved()
        {
            var player = Ensure();
            return player != null && player.ResumeSaved();
        }

        public static bool Advance() => _player != null && _player.Advance();
        public static bool Choose(int index) => _player != null && _player.Choose(index);
        public static void SkipToChoice() => _player?.SkipToChoice();

        public static void Stop()
        {
            if (_player != null) _player.Stop();
        }
    }

    // 보상·값 바꾸기 블록에서 다이얼로그를 연다(도전과제 달성 연출, 다이얼로그에서 다른 다이얼로그로 넘기기 등).
    [Serializable]
    [BlockInfo("다이얼로그 재생", Category = "다이얼로그 시스템", Description = "다이얼로그를 엽니다(다이얼로그 관리자).", Sentence = "다이얼로그 {dialogue} · {entryId}")]
    public sealed class PlayDialogueReward : Reward
    {
        [FieldLabel("다이얼로그"), DataRef(typeof(DialogueData))] public string dialogue = "";
        [FieldLabel("시작 ID")] public string entryId = "start";

        // 게임 상태를 바꾸지 않는 연출이다(되돌리기에 영향 없음).
        public override RewardEffect Effect => RewardEffect.Reversible;

        public override void Grant(GameSession session)
        {
            if (Application.isPlaying) Dialogues.Play(dialogue, entryId);
        }

        public override string Describe(INameResolver names) => $"다이얼로그 {dialogue} · {entryId}";
        public override string Natural(INameResolver names) => $"다이얼로그 '{dialogue}' 재생";

        public override void Validate(IValidationContext context)
        {
            if (string.IsNullOrEmpty(dialogue)) context.Error("재생할 다이얼로그가 선택되지 않았습니다.");
        }
    }
}
