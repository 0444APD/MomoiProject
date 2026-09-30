using System;
using System.Linq;
using GameSystems.Data;
using GameSystems.Dialogue;
using UnityEditor;
using UnityEngine;

namespace GameSystems.EditorTools.Graph
{
    // 그래프 창의 미리보기. 실제 화면(UI Toolkit·스탠딩·카메라)으로 보여 주려고 플레이 모드에서 씬의 DialoguePlayer로 재생한다.
    // 씬에 없으면 대화 UI 템플릿 프리팹을 잠시 꺼내 쓴다(플레이 모드가 끝나면 사라진다).
    // 시간 이동은 처음부터 다시 시작해 그 시점까지 클릭 없이 감는다: 그 사이 시작된 효과는 모두 그 시점의 진행도에서 이어진다.
    // (트윈으로 그리는 효과는 정확히 맞고, 스파인 애니메이션·파티클은 자기 시간으로 흐르므로 처음부터 다시 돈다.)
    [InitializeOnLoad]
    public static class DialoguePreview
    {
        const string PendingKey = "GameSystems.DialoguePreview.Pending";

        [Serializable]
        sealed class Request
        {
            public string dialogue;
            public string node;
            public string preset;
            public float time;
        }

        static DialoguePreview()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                var json = SessionState.GetString(PendingKey, "");
                SessionState.EraseString(PendingKey);
                if (string.IsNullOrEmpty(json)) return;
                var request = JsonUtility.FromJson<Request>(json);
                // 씬의 Awake·Start가 끝난 다음 프레임에.
                EditorApplication.delayCall += () => Execute(request);
            };
        }

        public static DialoguePlayer Player { get; private set; }

        public static bool IsRunning => Application.isPlaying && Player != null && Player.IsPlaying;

        public static void PlayDialogue(DialogueData dialogue, string nodeGuid, float time = 0f) =>
            Send(new Request { dialogue = dialogue.Id, node = nodeGuid, time = time });

        public static void PlayPreset(EffectPresetData preset, float time = 0f) =>
            Send(new Request { preset = preset.Id, time = time });

        static void Send(Request request)
        {
            if (Application.isPlaying)
            {
                Execute(request);
                return;
            }
            SessionState.SetString(PendingKey, JsonUtility.ToJson(request));
            EditorApplication.EnterPlaymode();
        }

        static void Execute(Request request)
        {
            var player = FindPlayer();
            if (player == null) return;
            Player = player;
            player.Paused = false;
            if (!string.IsNullOrEmpty(request.preset))
            {
                if (!player.PlayPreset(request.preset, request.time)) Debug.LogWarning($"[대화 미리보기] 프리셋 '{request.preset}'을(를) 재생하지 못했습니다(게임 데이터베이스에 있는지 확인하세요).");
                return;
            }
            var dialogue = EditorDatabase.Instance.Committed.OfType<DialogueData>().FirstOrDefault(d => d.Id == request.dialogue);
            if (dialogue == null || !player.PlayFrom(dialogue, request.node, request.time))
                Debug.LogWarning($"[대화 미리보기] '{request.dialogue}'을(를) 재생하지 못했습니다.");
        }

        static DialoguePlayer FindPlayer()
        {
            var player = UnityEngine.Object.FindAnyObjectByType<DialoguePlayer>();
            if (player != null) return player;
            var prefab = AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .FirstOrDefault(p => p != null && p.GetComponent<DialoguePlayer>() != null);
            if (prefab == null)
            {
                Debug.LogWarning("[대화 미리보기] 씬에 DialoguePlayer가 없고 대화 UI 프리팹도 없습니다. 'Game Systems › 대화 UI 템플릿 만들기'를 먼저 하세요.");
                return null;
            }
            if (Camera.main == null)
            {
                var cameraObject = new GameObject("PreviewCamera") { tag = "MainCamera" };
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 5f;
                camera.transform.position = new Vector3(0, 0, -10);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
            }
            var instance = (GameObject)UnityEngine.Object.Instantiate(prefab);
            instance.name = prefab.name + " (미리보기)";
            return instance.GetComponent<DialoguePlayer>();
        }

        // ── 조작(플레이 모드 중) ─────────────────────────────

        public static float Time => IsActive ? Player.Runner.Time : 0f;
        static bool IsActive => Application.isPlaying && Player != null;

        public static bool Paused
        {
            get => IsActive && Player.Paused;
            set
            {
                if (IsActive) Player.Paused = value;
            }
        }

        public static bool Auto
        {
            get => IsActive && Player.autoMode;
            set
            {
                if (IsActive) Player.autoMode = value;
            }
        }

        public static void Seek(float time)
        {
            if (IsActive) Player.Seek(Mathf.Max(0, time));
        }

        public static void Next()
        {
            if (IsActive) Player.Advance();
        }

        public static void SkipToChoice()
        {
            if (IsActive) Player.SkipToChoice();
        }

        public static void Stop()
        {
            if (IsActive) Player.Stop();
        }

        public static string Status
        {
            get
            {
                if (!Application.isPlaying) return "플레이 모드에서 재생합니다";
                if (Player == null) return "대기";
                var runner = Player.Runner;
                var phase = runner.Phase switch
                {
                    DialoguePhase.WaitingForText => "연출 중",
                    DialoguePhase.Typing => "타이핑",
                    DialoguePhase.Shown => "클릭 대기",
                    DialoguePhase.Choosing => "선택지",
                    DialoguePhase.Ended => "끝",
                    _ => runner.RunningEffects > 0 ? "효과" : "대기",
                };
                return $"{phase}{(runner.InputBlocked ? " · 클릭 막힘" : "")}{(Player.Paused ? " · 일시정지" : "")}";
            }
        }
    }
}
