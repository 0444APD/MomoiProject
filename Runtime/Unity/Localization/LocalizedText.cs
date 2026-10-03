using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace GBible
{
    // 텍스트 키 하나를 현재 언어 글자로 바꿔 같은 오브젝트의 글자 컴포넌트(TextMeshPro, UI Text 등 'text' 속성이 있는 것)에 넣는다.
    // 언어가 바뀌면 다시 넣는다. 값은 인스펙터 또는 SetArg로 넘긴다.
    [AddComponentMenu("MomoiProject/다국어 텍스트")]
    [ExecuteAlways]
    public sealed class LocalizedText : MonoBehaviour
    {
        [Serializable]
        public struct Arg
        {
            public string name;
            [Tooltip("숫자로 읽히면 숫자로(복수형·천 단위 표기), 아니면 글자로. DB ID면 그 이름으로 바뀐다.")]
            public string value;
        }

        [SerializeField] string key = "";
        [SerializeField] List<Arg> args = new List<Arg>();
        [Tooltip("글자를 넣을 컴포넌트. 비우면 같은 오브젝트에서 'text' 속성이 있는 컴포넌트를 찾는다.")]
        [SerializeField] Component target = null;
        [Tooltip("글자가 바뀔 때마다 불린다(직접 넣을 곳이 따로 있을 때).")]
        [SerializeField] UnityEvent<string> onText = new UnityEvent<string>();

        readonly Dictionary<string, object> _runtimeArgs = new Dictionary<string, object>(StringComparer.Ordinal);
        // 글자 칸의 text 속성 쓰기(붙을 때 한 번 대리자로 만들어 둔다 — 새로 고칠 때마다 리플렉션을 쓰지 않게).
        Action<string> _setText;
        Component _boundTarget;

        public string Key
        {
            get => key;
            set
            {
                key = value;
                Refresh();
            }
        }

        public string Text { get; private set; } = "";

        public void SetArg(string name, object value)
        {
            _runtimeArgs[name] = value;
            Refresh();
        }

        void OnEnable()
        {
            GameLanguage.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => GameLanguage.Changed -= Refresh;

        void OnValidate()
        {
#if UNITY_EDITOR
            // OnValidate 안에서 다른 컴포넌트를 고치면 경고가 나므로 한 프레임 뒤에.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled) Refresh();
            };
#endif
        }

        public void Refresh()
        {
            if (string.IsNullOrEmpty(key)) return;
            GameLanguage.EnsureReady();
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var arg in args)
                if (!string.IsNullOrEmpty(arg.name))
                    values[arg.name] = double.TryParse(arg.value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : (object)arg.value;
            foreach (var pair in _runtimeArgs) values[pair.Key] = pair.Value;
            Text = Texts.Get(key, values);
            Apply(Text);
            onText.Invoke(Text);
        }

        void Apply(string text)
        {
            var component = target;
            if (component == null)
                foreach (var candidate in GetComponents<Component>())
                    if (candidate != this && TextProperty(candidate) != null)
                    {
                        component = candidate;
                        break;
                    }
            if (component == null) return;
            if (_boundTarget != component)
            {
                _boundTarget = component;
                var property = TextProperty(component);
                _setText = property == null ? null : (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), component, property.GetSetMethod());
            }
            _setText?.Invoke(text);
        }

        static PropertyInfo TextProperty(Component component)
        {
            if (component is Transform) return null;
            var property = component.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            return property != null && property.PropertyType == typeof(string) && property.CanWrite ? property : null;
        }
    }
}
