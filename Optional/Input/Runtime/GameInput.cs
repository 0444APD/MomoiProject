using System;
using System.Linq;
using UnityEngine.InputSystem;

namespace GBible
{
    // 입력(선택 기능, Input System 패키지): 키 바꾸기와 그 저장. 바꾼 키는 설정 파일(기기마다 하나, GameSettings)에 남는다.
    //   게임이 켜질 때 한 번: GameInput.Use(inputActions);           // 프로젝트의 .inputactions. 저장해 둔 키를 덮어 씌우고 켠다
    //   설정 화면: GameInput.BindingText(action, i) 로 보이고, 누르면 GameInput.Rebind(action, i, result => …)
    //   되돌리기: GameInput.ResetBindings() / ResetBindings(action)
    // 입력을 읽는 것은 Input System 그대로다(action.WasPressedThisFrame() …). 게임 규칙은 입력이 아니라 행동(Game.Do)으로 바뀐다.
    public static class GameInput
    {
        const string SettingsKey = "momoi.input";

        public static InputActionAsset Actions { get; private set; }

        // 키가 바뀌었을 때(바꾸기·되돌리기). 설정 화면이 글자를 다시 그린다.
        public static event Action Changed;

        static InputActionRebindingExtensions.RebindingOperation _operation;
        // 바꾸는 동안 Esc(취소)가 화면의 뒤로·일시정지 메뉴로 가지 않게 잡아 둔다.
        static IDisposable _backHold;

        public static bool IsRebinding => _operation != null;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            // 지난 플레이(도메인 리로드를 끈 설정)에서 끝나지 않은 키 바꾸기: 취소해 입력 이벤트 듣기를 끊고 메모리(네이티브)를 놓는다.
            // 그 플레이의 화면 콜백(done)은 부르지 않는다 — 먼저 비워 두면 Finish가 지난 것으로 알아본다.
            var stale = _operation;
            _operation = null;
            _backHold = null;
            if (stale != null)
            {
                try { stale.Cancel(); }
                finally { stale.Dispose(); }
            }
            Actions = null;
            Changed = null;
        }

        // 플레이가 끝날 때(게임을 끔·에디터에서 플레이를 멈춤) 진행 중인 키 바꾸기를 취소한다 — 끝난 뒤(에디터 편집 중)에 누른 키가 바꾸기를 마치고 설정에 쓰지 않게.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Hook()
        {
            UnityEngine.Application.quitting -= CancelRebind;
            UnityEngine.Application.quitting += CancelRebind;
        }

        public static void Use(InputActionAsset actions, bool enable = true)
        {
            Actions = actions ?? throw new ArgumentNullException(nameof(actions));
            var saved = GameSettings.Raw(SettingsKey);
            if (!string.IsNullOrEmpty(saved))
            {
                try { actions.LoadBindingOverridesFromJson(saved); }
                catch (Exception e) { UnityEngine.Debug.LogWarning("[MomoiProject] 저장된 키 설정을 읽지 못해 기본 키로 시작합니다: " + e.Message); }
            }
            if (enable) actions.Enable();
        }

        // "Player/Jump" 또는 "Jump"(겹치지 않을 때).
        public static InputAction Find(string action) =>
            Actions?.FindAction(action) ?? throw new InvalidOperationException($"[MomoiProject] 입력 동작 '{action}'이(가) 없습니다(GameInput.Use로 넘긴 .inputactions 확인).");

        // 키 표시 글자(예: "Space", "W/A/S/D"). bindingIndex가 -1이면 그 동작의 모든 키.
        public static string BindingText(InputAction action, int bindingIndex = -1) =>
            bindingIndex < 0 ? action.GetBindingDisplayString() : action.GetBindingDisplayString(bindingIndex);

        // 키 하나를 바꾼다: 다음에 누른 키로(Esc는 취소). 같은 묶음(맵)의 다른 키와 겹치면 바꾸지 않고 Conflict에 그 동작을 담는다.
        public static void Rebind(InputAction action, int bindingIndex, Action<RebindResult> done = null, bool allowDuplicates = false)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (bindingIndex < 0 || bindingIndex >= action.bindings.Count) throw new ArgumentOutOfRangeException(nameof(bindingIndex));
            if (action.bindings[bindingIndex].isComposite)
                throw new InvalidOperationException("[MomoiProject] 묶음 키(WASD 같은 composite)는 그 안의 칸 하나씩 바꿉니다(bindingIndex + 1 …).");
            CancelRebind();
            var previous = action.bindings[bindingIndex].overridePath;
            var wasEnabled = action.enabled;
            action.Disable();
            _operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f)
                .OnCancel(operation => Finish(operation, action, wasEnabled, new RebindResult { Cancelled = true }, done))
                .OnComplete(operation =>
                {
                    var conflict = allowDuplicates ? null : ConflictOf(action, bindingIndex);
                    if (conflict != null)
                    {
                        if (string.IsNullOrEmpty(previous)) action.RemoveBindingOverride(bindingIndex);
                        else action.ApplyBindingOverride(bindingIndex, previous);
                        Finish(operation, action, wasEnabled, new RebindResult { Conflict = conflict }, done);
                        return;
                    }
                    Save();
                    Finish(operation, action, wasEnabled, new RebindResult { Done = true }, done);
                    Changed?.Invoke();
                });
            _backHold?.Dispose();
            _backHold = GameScreens.HoldBack();
            _operation.Start();
        }

        public static void CancelRebind() => _operation?.Cancel();

        // 바꾼 키를 기본으로 되돌린다(동작을 주면 그 동작만).
        public static void ResetBindings(InputAction action = null)
        {
            if (Actions == null) return;
            if (action == null) Actions.RemoveAllBindingOverrides();
            else action.RemoveAllBindingOverrides();
            Save();
            Changed?.Invoke();
        }

        static InputAction ConflictOf(InputAction action, int bindingIndex)
        {
            var path = action.bindings[bindingIndex].effectivePath;
            if (string.IsNullOrEmpty(path)) return null;
            var map = action.actionMap;
            if (map == null) return null;
            foreach (var other in map.actions)
                for (int i = 0; i < other.bindings.Count; i++)
                {
                    if (other == action && i == bindingIndex) continue;
                    var binding = other.bindings[i];
                    if (!binding.isComposite && string.Equals(binding.effectivePath, path, StringComparison.OrdinalIgnoreCase)) return other;
                }
            return null;
        }

        static void Finish(InputActionRebindingExtensions.RebindingOperation operation, InputAction action, bool wasEnabled, RebindResult result, Action<RebindResult> done)
        {
            operation.Dispose();
            // 지난 플레이에서 남아 ResetStatics가 끝낸 것이면 동작·화면은 건드리지 않는다(그 플레이의 화면은 이미 없다).
            if (operation != _operation) return;
            _operation = null;
            _backHold?.Dispose();
            _backHold = null;
            if (wasEnabled) action.Enable();
            done?.Invoke(result);
        }

        static void Save()
        {
            if (Actions != null) GameSettings.SetRaw(SettingsKey, Actions.SaveBindingOverridesAsJson());
        }
    }

    public struct RebindResult
    {
        // 바뀌었다.
        public bool Done;
        // Esc로 취소했다.
        public bool Cancelled;
        // 같은 묶음의 다른 동작과 겹쳐 바꾸지 않았다(그 동작).
        public InputAction Conflict;
    }
}
