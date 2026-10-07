using System;
using System.Collections.Generic;
using System.Reflection;
using GBible.Data;
using UnityEngine;

namespace GBible.Dialogue
{
    // 스탠딩 하나를 그리는 컴포넌트. 다이얼로그 화면이 위치·밝기·투명도·표정을 알려 주고, 그리는 방식은 종류마다 다르다.
    // 캐릭터의 '스탠딩 프리팹'에 이 컴포넌트(또는 상속한 것)를 붙인다. 비워 두면 SpriteStandingView를 만들어 표정 스프라이트로 그린다.
    public abstract class StandingView : MonoBehaviour
    {
        public StandingProfileData Character { get; private set; }

        public virtual void Bind(StandingProfileData character) => Character = character;

        // 스탠딩 바꾸기 시작(스탠딩 ID, 또는 예외로 꽂은 스프라이트). 이후 SetExpressionBlend(0→1)로 전환이 진행된다(바로 바꾸는 방식이면 무시해도 된다).
        public abstract void BeginExpression(string expression, Sprite sprite = null);
        public virtual void SetExpressionBlend(float t)
        {
        }

        // 밝기(강조 1, 비강조는 0.7 정도)와 투명도.
        public abstract void SetTint(float brightness, float alpha);

        // 그리기 순서(뒤에 등장한 캐릭터가 앞). order ~ order+3을 이 스탠딩이 쓴다.
        public virtual void SetSortingOrder(int order)
        {
        }

        // 창 모드: 창 이미지(테두리)와 창 모양(마스크). frame이 null이면 일반 스탠딩.
        public virtual void SetFrame(Sprite frame, Sprite mask)
        {
            if (frame != null) Debug.LogWarning($"[다이얼로그] '{name}' 스탠딩은 창 모드를 지원하지 않습니다.", this);
        }
    }

    // 표정 스프라이트 두 장을 겹쳐 바꾸는 기본 스탠딩(크로스페이드).
    public sealed class SpriteStandingView : StandingView
    {
        [SerializeField] SpriteRenderer current;
        [SerializeField] SpriteRenderer next;
        SpriteRenderer _frame;
        SpriteMask _mask;
        int _order;
        float _brightness = 1f;
        float _alpha = 1f;
        float _blend = 1f;

        void Awake() => EnsureRenderers();

        void EnsureRenderers()
        {
            if (current == null) current = Child("Current");
            if (next == null) next = Child("Next");
        }

        SpriteRenderer Child(string childName)
        {
            var found = transform.Find(childName);
            var child = found != null ? found.gameObject : new GameObject(childName);
            child.transform.SetParent(transform, false);
            var renderer = child.GetComponent<SpriteRenderer>();
            return renderer != null ? renderer : child.AddComponent<SpriteRenderer>();
        }

        public override void Bind(StandingProfileData character)
        {
            base.Bind(character);
            EnsureRenderers();
            current.sprite = character != null ? character.SpriteOf(character.defaultExpression) : null;
            next.sprite = null;
            _blend = 1f;
            Apply();
        }

        public override void BeginExpression(string expression, Sprite overrideSprite = null)
        {
            EnsureRenderers();
            var sprite = overrideSprite != null ? overrideSprite : Character != null ? Character.SpriteOf(expression) : null;
            if (sprite == current.sprite)
            {
                next.sprite = null;
                _blend = 1f;
            }
            else
            {
                next.sprite = sprite;
                _blend = 0f;
            }
            Apply();
        }

        public override void SetExpressionBlend(float t)
        {
            if (next.sprite == null) return;
            _blend = Mathf.Clamp01(t);
            if (_blend >= 1f)
            {
                current.sprite = next.sprite;
                next.sprite = null;
            }
            Apply();
        }

        public override void SetTint(float brightness, float alpha)
        {
            _brightness = brightness;
            _alpha = alpha;
            Apply();
        }

        public override void SetSortingOrder(int order)
        {
            EnsureRenderers();
            _order = order;
            current.sortingOrder = order;
            next.sortingOrder = order + 1;
            if (_frame != null) _frame.sortingOrder = order + 2;
            if (_mask != null)
            {
                // 이 스탠딩의 그림만 가린다(다른 캐릭터에는 영향 없음).
                _mask.isCustomRangeActive = true;
                _mask.backSortingOrder = order - 1;
                _mask.frontSortingOrder = order + 1;
            }
        }

        // 창 모드: 창 모양 안쪽만 보이고 창 이미지를 위에 그린다. 창은 캐릭터 그림의 가운데에 놓인다.
        public override void SetFrame(Sprite frame, Sprite mask)
        {
            EnsureRenderers();
            var on = frame != null;
            if (on && _frame == null)
            {
                _frame = new GameObject("Frame").AddComponent<SpriteRenderer>();
                _frame.transform.SetParent(transform, false);
                _mask = new GameObject("FrameMask").AddComponent<SpriteMask>();
                _mask.transform.SetParent(transform, false);
            }
            if (_frame != null)
            {
                _frame.sprite = frame;
                _frame.gameObject.SetActive(on);
                _mask.sprite = mask != null ? mask : frame;
                _mask.gameObject.SetActive(on);
                var center = current.sprite != null ? current.sprite.bounds.center : Vector3.zero;
                _frame.transform.localPosition = center + Vector3.back * 0.001f;
                _mask.transform.localPosition = center;
            }
            var interaction = on ? SpriteMaskInteraction.VisibleInsideMask : SpriteMaskInteraction.None;
            current.maskInteraction = interaction;
            next.maskInteraction = interaction;
            SetSortingOrder(_order);
            Apply();
        }

        void Apply()
        {
            var color = new Color(_brightness, _brightness, _brightness, _alpha);
            current.color = next.sprite == null ? color : new Color(color.r, color.g, color.b, color.a * (1f - _blend * 0.999f));
            next.color = new Color(color.r, color.g, color.b, color.a * _blend);
            if (_frame != null) _frame.color = color;
        }
    }

    // 스파인 스탠딩. 스파인 런타임을 직접 참조하지 않고(설치하지 않은 프로젝트도 컴파일되도록) 이름으로 찾아 부른다.
    // 같은 오브젝트(또는 자식)의 SkeletonAnimation·SkeletonGraphic·SkeletonMecanim 중 하나를 쓴다.
    // 표정 ID = 애니메이션 이름(다르면 목록에 짝을 적는다). 표정 전환은 스파인의 믹스(AnimationStateData)로 섞인다.
    // 이름으로 찾는 일(리플렉션)은 붙일 때(Bind) 한 번만 하고 대리자로 들고 있는다 — 매 프레임 부르는 밝기·투명도는 대리자로만.
    // 다이얼로그 시간(멈추지 않는 시간)에 맞춰 스켈레톤의 unscaledTime을 켠다(pauseGame으로 게임 시간이 멈춰도 움직인다).
    public sealed class SpineStandingView : StandingView
    {
        [Serializable]
        public struct ExpressionAnimation
        {
            public string expression;
            public string animation;
            public bool loop;
        }

        [Tooltip("표정 → 애니메이션(비우면 표정 ID와 같은 이름, 반복)")]
        public List<ExpressionAnimation> animations = new List<ExpressionAnimation>();
        [Tooltip("표정 애니메이션을 재생할 트랙")]
        public int track;
        [Tooltip("MeshRenderer의 정렬 순서를 다이얼로그 화면이 정하게 한다")]
        public bool controlSorting = true;

        Component _skeleton;
        // Bind에서 한 번 만든 대리자.
        Func<object> _getState;
        Func<object> _getSkeleton;
        Action<Color> _setGraphicColor;
        MethodInfo _setAnimationMethod;
        PropertyInfo _r, _g, _b, _a;
        // 스켈레톤·애니메이션 상태 객체가 바뀌면(스파인이 다시 초기화) 그때만 다시 묶는다.
        object _boundState;
        Func<int, string, bool, object> _setAnimation;
        object _boundSkeleton;
        Action<float> _setR, _setG, _setB, _setA;

        public override void Bind(StandingProfileData character)
        {
            base.Bind(character);
            _skeleton = FindSkeleton();
            if (_skeleton == null) Debug.LogWarning($"[다이얼로그] '{name}'에서 스파인 컴포넌트(SkeletonAnimation 등)를 찾지 못했습니다.", this);
            Prepare();
            if (character != null && !string.IsNullOrEmpty(character.defaultExpression)) BeginExpression(character.defaultExpression);
        }

        // 이름으로 찾는 일을 여기서 한 번만 한다.
        void Prepare()
        {
            _getState = null;
            _getSkeleton = null;
            _setGraphicColor = null;
            _setAnimationMethod = null;
            _r = _g = _b = _a = null;
            _boundState = null;
            _setAnimation = null;
            _boundSkeleton = null;
            _setR = _setG = _setB = _setA = null;
            if (_skeleton == null) return;
            var type = _skeleton.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            // 멈추지 않는 시간: SkeletonAnimation·SkeletonGraphic의 unscaledTime(SkeletonMecanim은 애니메이터 — 다이얼로그 화면이 Unscaled로 바꾼다).
            var unscaled = type.GetField("unscaledTime", flags);
            if (unscaled != null && unscaled.FieldType == typeof(bool)) unscaled.SetValue(_skeleton, true);
            else
            {
                var property = type.GetProperty("UnscaledTime", flags);
                if (property != null && property.CanWrite && property.PropertyType == typeof(bool)) property.SetValue(_skeleton, true);
            }
            _getState = Getter(_skeleton, type.GetProperty("AnimationState", flags));
            if (type.Name == "SkeletonGraphic")
            {
                var color = type.GetProperty("color", flags);
                if (color != null && color.CanWrite && color.PropertyType == typeof(Color))
                    _setGraphicColor = (Action<Color>)Delegate.CreateDelegate(typeof(Action<Color>), _skeleton, color.GetSetMethod());
                return;
            }
            var skeletonProperty = type.GetProperty("Skeleton", flags);
            _getSkeleton = Getter(_skeleton, skeletonProperty);
            var skeletonType = skeletonProperty?.PropertyType;
            if (skeletonType == null) return;
            _r = FloatProperty(skeletonType, "R");
            _g = FloatProperty(skeletonType, "G");
            _b = FloatProperty(skeletonType, "B");
            _a = FloatProperty(skeletonType, "A");
        }

        static Func<object> Getter(object target, PropertyInfo property)
        {
            var get = property?.GetGetMethod();
            if (get == null || property.PropertyType.IsValueType) return null;
            return (Func<object>)Delegate.CreateDelegate(typeof(Func<object>), target, get);
        }

        static PropertyInfo FloatProperty(Type type, string member)
        {
            var property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
            return property != null && property.CanWrite && property.PropertyType == typeof(float) ? property : null;
        }

        static Action<float> Setter(object target, PropertyInfo property) =>
            target == null || property == null ? null : (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), target, property.GetSetMethod());

        Component FindSkeleton()
        {
            foreach (var component in GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var typeName = component.GetType().Name;
                if (typeName == "SkeletonAnimation" || typeName == "SkeletonGraphic" || typeName == "SkeletonMecanim") return component;
            }
            return null;
        }

        public override void BeginExpression(string expression, Sprite sprite = null)
        {
            if (sprite != null) Debug.LogWarning($"[다이얼로그] 스파인 스탠딩 '{name}'에는 예외 스프라이트를 꽂을 수 없습니다(스탠딩 ID로 애니메이션을 고르세요).", this);
            if (_skeleton == null || string.IsNullOrEmpty(expression)) return;
            var animation = expression;
            var loop = true;
            foreach (var pair in animations)
                if (pair.expression == expression)
                {
                    animation = string.IsNullOrEmpty(pair.animation) ? expression : pair.animation;
                    loop = pair.loop;
                }
            var state = _getState?.Invoke();
            if (state == null) return;
            if (!ReferenceEquals(state, _boundState))
            {
                // 애니메이션 상태가 처음이거나 바뀌었을 때만 묶는다(스파인이 다시 초기화한 경우).
                _boundState = state;
                _setAnimationMethod ??= state.GetType().GetMethod("SetAnimation", new[] { typeof(int), typeof(string), typeof(bool) });
                _setAnimation = _setAnimationMethod == null || _setAnimationMethod.ReturnType.IsValueType
                    ? null
                    : (Func<int, string, bool, object>)Delegate.CreateDelegate(typeof(Func<int, string, bool, object>), state, _setAnimationMethod);
            }
            if (_setAnimation == null) return;
            try
            {
                _setAnimation(track, animation, loop);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[다이얼로그] 스파인 애니메이션 '{animation}'을(를) 재생하지 못했습니다: {e.Message}", this);
            }
        }

        // 매 프레임 불린다(전환 중): Bind에서 만든 대리자만 쓴다.
        public override void SetTint(float brightness, float alpha)
        {
            if (_skeleton == null) return;
            // SkeletonGraphic은 Graphic.color, 나머지는 Skeleton의 R·G·B·A.
            if (_setGraphicColor != null)
            {
                _setGraphicColor(new Color(brightness, brightness, brightness, alpha));
                return;
            }
            var skeleton = _getSkeleton?.Invoke();
            if (skeleton == null) return;
            if (!ReferenceEquals(skeleton, _boundSkeleton))
            {
                _boundSkeleton = skeleton;
                _setR = Setter(skeleton, _r);
                _setG = Setter(skeleton, _g);
                _setB = Setter(skeleton, _b);
                _setA = Setter(skeleton, _a);
            }
            _setR?.Invoke(brightness);
            _setG?.Invoke(brightness);
            _setB?.Invoke(brightness);
            _setA?.Invoke(alpha);
        }

        public override void SetSortingOrder(int order)
        {
            if (!controlSorting) return;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.sortingOrder = order;
        }
    }
}
