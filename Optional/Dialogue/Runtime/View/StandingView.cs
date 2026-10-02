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

        public override void Bind(StandingProfileData character)
        {
            base.Bind(character);
            _skeleton = FindSkeleton();
            if (_skeleton == null) Debug.LogWarning($"[다이얼로그] '{name}'에서 스파인 컴포넌트(SkeletonAnimation 등)를 찾지 못했습니다.", this);
            if (character != null && !string.IsNullOrEmpty(character.defaultExpression)) BeginExpression(character.defaultExpression);
        }

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
            var state = Get(_skeleton, "AnimationState");
            var set = state?.GetType().GetMethod("SetAnimation", new[] { typeof(int), typeof(string), typeof(bool) });
            if (set == null) return;
            try
            {
                set.Invoke(state, new object[] { track, animation, loop });
            }
            catch (TargetInvocationException e)
            {
                Debug.LogWarning($"[다이얼로그] 스파인 애니메이션 '{animation}'을(를) 재생하지 못했습니다: {e.InnerException?.Message}", this);
            }
        }

        public override void SetTint(float brightness, float alpha)
        {
            if (_skeleton == null) return;
            // SkeletonGraphic은 Graphic.color, 나머지는 Skeleton의 R·G·B·A.
            var colorProperty = _skeleton.GetType().GetProperty("color", typeof(Color));
            if (_skeleton.GetType().Name == "SkeletonGraphic" && colorProperty != null && colorProperty.CanWrite)
            {
                colorProperty.SetValue(_skeleton, new Color(brightness, brightness, brightness, alpha));
                return;
            }
            var skeleton = Get(_skeleton, "Skeleton") ?? Get(_skeleton, "skeleton");
            if (skeleton == null) return;
            Set(skeleton, "R", brightness);
            Set(skeleton, "G", brightness);
            Set(skeleton, "B", brightness);
            Set(skeleton, "A", alpha);
        }

        public override void SetSortingOrder(int order)
        {
            if (!controlSorting) return;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.sortingOrder = order;
        }

        static object Get(object target, string member)
        {
            var type = target.GetType();
            var property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
            if (property != null) return property.GetValue(target);
            var field = type.GetField(member, BindingFlags.Public | BindingFlags.Instance);
            return field?.GetValue(target);
        }

        static void Set(object target, string member, float value)
        {
            var type = target.GetType();
            var property = type.GetProperty(member, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite && property.PropertyType == typeof(float))
            {
                property.SetValue(target, value);
                return;
            }
            var field = type.GetField(member, BindingFlags.Public | BindingFlags.Instance) ?? type.GetField(member.ToLowerInvariant(), BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(float)) field.SetValue(target, value);
        }
    }
}
