using System;
using GBible.EditorTools.UI;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GBible.EditorTools
{
    // 오디오 창: 채널 음량, 클립 미리 듣기(채널을 골라 게임과 같은 방식으로: 브금·목소리는 교차 페이드, 효과음·UI는 동시에), 지금 나는 소리.
    // 플레이 중이 아니어도 GameAudio를 그대로 쓴다(에디터가 시간을 흘려 준다).
    public sealed class AudioWindow : EditorWindow
    {
        [SerializeField] AudioClip _clip;
        [SerializeField] AudioChannel _channel = AudioChannel.Sfx;

        Label _status;

        [MenuItem("MomoiProject/오디오", priority = 8)]
        public static void Open()
        {
            var window = GsWindows.Get<AudioWindow>();
            window.titleContent = new GUIContent("오디오");
            window.minSize = new Vector2(420, 420);
        }

        void CreateGUI()
        {
            var root = Gs.Col();
            Gs.Apply(root);
            root.style.flexGrow = 1;
            root.style.paddingLeft = 20;
            root.style.paddingRight = 20;
            root.style.paddingTop = 16;
            rootVisualElement.Add(root);

            root.Add(Gs.Label("미리 듣기", "gs-heading"));
            root.Add(Gs.Space(8));
            var clipField = new ObjectField("클립") { objectType = typeof(AudioClip), value = _clip };
            clipField.RegisterValueChangedCallback(e => _clip = e.newValue as AudioClip);
            root.Add(clipField);
            var channelField = new EnumField("채널", _channel);
            channelField.RegisterValueChangedCallback(e => _channel = (AudioChannel)e.newValue);
            root.Add(channelField);
            var buttons = Gs.Row();
            buttons.style.marginTop = 8;
            buttons.Add(new GsButton("▶ 재생", () => AudioPreview.Play(_clip, _channel), GsButtonKind.Primary));
            buttons.Add(Gs.HSpace(6));
            buttons.Add(new GsButton("■ 이 채널 정지", () => GameAudio.Stop(_channel, 0.2f)));
            buttons.Add(Gs.HSpace(6));
            buttons.Add(new GsButton("모두 정지", () => GameAudio.StopAll()));
            root.Add(buttons);
            _status = Gs.Label("", "gs-caption");
            _status.style.marginTop = 8;
            _status.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_status);

            root.Add(Gs.Space(20));
            root.Add(Gs.Label("채널 음량", "gs-heading"));
            var note = Gs.Label("플레이 중에는 플레이어 설정처럼 저장되고, 아니면 이 에디터에서만 적용됩니다. 기본값은 오디오 설정 에셋에서 정합니다.", "gs-caption");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginBottom = 8;
            root.Add(note);
            foreach (AudioChannel channel in Enum.GetValues(typeof(AudioChannel)))
            {
                var c = channel;
                var slider = new Slider(ObjectNames.NicifyVariableName(Label(channel)), 0f, 1f) { value = GameAudio.Volume(channel), showInputField = true };
                slider.RegisterValueChangedCallback(e => GameAudio.SetVolume(c, e.newValue, save: Application.isPlaying));
                root.Add(slider);
            }

            root.Add(Gs.Space(16));
            var settings = GameAudioSettings.Load();
            if (settings == null)
                root.Add(new GsButton("오디오 설정 만들기", () =>
                {
                    Selection.activeObject = AudioPreview.EnsureSettings();
                    rootVisualElement.Clear();
                    CreateGUI();
                }));
            else root.Add(new GsButton("오디오 설정 열기", () => Selection.activeObject = settings, GsButtonKind.Ghost));

            root.schedule.Execute(RefreshStatus).Every(200);
        }

        static string Label(AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Master: return "전체";
                case AudioChannel.Bgm: return "브금";
                case AudioChannel.Voice: return "목소리";
                case AudioChannel.Sfx: return "효과음";
                default: return "UI";
            }
        }

        void RefreshStatus()
        {
            if (_status == null) return;
            var bgm = GameAudio.CurrentBgm;
            var voice = GameAudio.CurrentVoice;
            _status.text = $"브금: {(bgm != null ? bgm.name : "없음")} · 목소리: {(voice != null ? voice.name : "없음")} · 효과음 {GameAudio.Playing(AudioChannel.Sfx)}개 · UI {GameAudio.Playing(AudioChannel.Ui)}개";
        }
    }

    // 에디터에서 소리 내기: 플레이 중이 아닐 때도 GameAudio가 돌도록 시간을 흘리고, 플레이 모드에 들어가면 정리한다.
    [InitializeOnLoad]
    public static class AudioPreview
    {
        static double _last;

        static AudioPreview()
        {
            EditorApplication.update += Pump;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredEditMode) GameAudio.Shutdown();
            };
            AssemblyReloadEvents.beforeAssemblyReload += GameAudio.Shutdown;
        }

        static void Pump()
        {
            var now = EditorApplication.timeSinceStartup;
            var delta = (float)(now - _last);
            _last = now;
            if (!Application.isPlaying && delta > 0 && delta < 1f) GameAudio.Tick(delta);
        }

        public static void Play(AudioClip clip, AudioChannel channel)
        {
            if (clip == null) return;
            GameAudio.Play(clip, channel == AudioChannel.Master ? AudioChannel.Sfx : channel);
        }

        public const string SettingsPath = EditorDatabase.DataRoot + "/Resources/MomoiProject/GameAudioSettings.asset";

        public static GameAudioSettings EnsureSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<GameAudioSettings>(SettingsPath);
            if (settings != null) return settings;
            EditorDatabase.EnsureFolder(System.IO.Path.GetDirectoryName(SettingsPath).Replace('\\', '/'));
            settings = ScriptableObject.CreateInstance<GameAudioSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        [MenuItem("Assets/MomoiProject/오디오 재생", false, 2000)]
        static void PlaySelected()
        {
            foreach (var clip in Selection.GetFiltered<AudioClip>(SelectionMode.Assets)) GameAudio.PlaySfx(clip);
        }

        [MenuItem("Assets/MomoiProject/오디오 재생", true)]
        static bool CanPlaySelected() => Selection.GetFiltered<AudioClip>(SelectionMode.Assets).Length > 0;

        [MenuItem("Assets/MomoiProject/오디오 정지", false, 2001)]
        static void StopAll() => GameAudio.StopAll();
    }
}
