using System;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Data;
using UnityEngine;

namespace GameSystems.Dialogue
{
    // 대화 화면(IDialogueStage의 기본 구현).
    //  · 게임 카메라 앞(월드): 배경, 배경 어둡게, 스탠딩(스프라이트·스파인), 이펙트 프리팹, 카메라 확대·이동·흔들기
    //  · UI Toolkit(DialogueUI): 글상자, 이름, 선택지, 레터박스, 번쩍임, 검은 화면
    // 상태 효과는 모두 '앞 모습 → 뒤 모습'을 섞는 한 가지 방식으로 그린다(바뀐 값만 움직인다).
    [AddComponentMenu("Game Systems/대화 화면")]
    public sealed class DialogueStage : MonoBehaviour, IDialogueStage
    {
        public DialogueUI ui;
        [Tooltip("비우면 Camera.main")] public Camera stageCamera;
        [Tooltip("카메라에서 대화 화면까지의 거리")] public float distance = 10f;
        public SpriteRenderer backgroundBack;
        public SpriteRenderer backgroundFront;
        [Tooltip("배경 어둡게(흰 스프라이트, 색은 검정)")] public SpriteRenderer dim;
        public Transform standingRoot;
        public Transform effectRoot;
        [Tooltip("이미지 창(CG) 부모. 비우면 대화 화면 아래에 만든다")] public Transform imageRoot;
        [Tooltip("이미지 창 그리기 순서(스탠딩보다 앞)")] public int imageOrder = 500;
        [Tooltip("스탠딩 발 위치(화면 아래 0 ~ 위 1)"), Range(-0.5f, 1f)] public float standingBaseline;
        [Tooltip("스프라이트 스탠딩의 높이(화면 높이 비율)"), Range(0.1f, 2f)] public float standingHeight = 0.95f;
        [Tooltip("강조가 꺼진 스탠딩의 밝기"), Range(0, 1)] public float dimmedBrightness = 0.7f;
        public int backgroundOrder = -100;
        public int standingOrder;
        [Tooltip("대화가 브금을 바꿨으면 끝날 때 대화 전의 브금으로 되돌린다")] public bool restoreMusicOnEnd = true;
        [Tooltip("대화가 끝나면 배경·스탠딩을 지우고 카메라를 되돌린다")] public bool clearOnEnd = true;

        // 캐릭터 ID → 데이터(대화 재생기가 채운다).
        public Func<string, StandingProfileData> Characters = _ => null;

        sealed class Standing
        {
            public StandingView View;
            public float X;
            public float Alpha;
            public float Brightness = 1f;
            public Vector3 Shake;
            public int Order;
        }

        readonly Dictionary<string, Standing> _standings = new Dictionary<string, Standing>();
        int _nextOrder;
        bool _captured;
        Vector3 _cameraBase;
        float _sizeBase;
        float _fovBase;
        float _zoom = 1f;
        Vector2 _offset;
        Vector3 _cameraShake;
        int _visibleTotal;
        AudioClip _musicBefore;
        bool _musicTouched;

        Camera Cam => stageCamera != null ? stageCamera : Camera.main;

        // ── 공간 ─────────────────────────────────────────────

        void Capture()
        {
            if (_captured) return;
            var cam = Cam;
            if (cam == null) return;
            _captured = true;
            _musicBefore = GameAudio.CurrentBgm;
            _musicTouched = false;
            _cameraBase = cam.transform.position;
            _sizeBase = cam.orthographicSize;
            _fovBase = cam.fieldOfView;
            _zoom = 1f;
            _offset = Vector2.zero;
            transform.position = _cameraBase + cam.transform.forward * distance;
            transform.rotation = cam.transform.rotation;
        }

        void Release()
        {
            if (!_captured) return;
            var cam = Cam;
            if (cam != null)
            {
                cam.transform.position = _cameraBase;
                if (cam.orthographic) cam.orthographicSize = _sizeBase;
                else cam.fieldOfView = _fovBase;
            }
            _captured = false;
            _cameraShake = Vector3.zero;
        }

        // 기본 카메라(확대 전)가 보는 화면 크기(월드 단위).
        public Vector2 ViewSize
        {
            get
            {
                var cam = Cam;
                if (cam == null) return new Vector2(16, 9);
                var height = cam.orthographic ? (_captured ? _sizeBase : cam.orthographicSize) * 2f
                                              : 2f * distance * Mathf.Tan((_captured ? _fovBase : cam.fieldOfView) * 0.5f * Mathf.Deg2Rad);
                return new Vector2(height * cam.aspect, height);
            }
        }

        // 화면 비율 좌표(0~1) → 대화 화면 안 좌표.
        public Vector3 Local(float x, float y)
        {
            var size = ViewSize;
            return new Vector3((x - 0.5f) * size.x, (y - 0.5f) * size.y, 0f);
        }

        void LateUpdate() => Sync();

        // 카메라·스탠딩 위치를 지금 값으로 맞춘다(매 프레임 자동).
        public void Sync()
        {
            if (!_captured) return;
            var cam = Cam;
            if (cam != null)
            {
                var size = ViewSize;
                cam.transform.position = _cameraBase + cam.transform.right * (_offset.x * size.x) + cam.transform.up * (_offset.y * size.y) + _cameraShake;
                if (cam.orthographic) cam.orthographicSize = _sizeBase / Mathf.Max(0.01f, _zoom);
                else cam.fieldOfView = Mathf.Clamp(_fovBase / Mathf.Max(0.01f, _zoom), 1f, 179f);
            }
            foreach (var standing in _standings.Values)
                if (standing.View != null)
                    standing.View.transform.localPosition = Local(standing.X, standingBaseline) + standing.Shake + Vector3.back * 0.001f * standing.Order;
        }

        // ── 상태 적용 ─────────────────────────────────────────

        public void ApplyState(StageState state)
        {
            Capture();
            SetBackground(backgroundBack, state.background, 1f);
            SetBackground(backgroundFront, null, 0f);
            SetDim(state.dim);
            ui?.SetLetterbox(state.letterbox);
            ui?.SetFade(0f);
            ui?.SetFlash(Color.clear, 0f);
            _zoom = state.cameraZoom;
            _offset = state.cameraOffset;
            if (state.musicSet)
            {
                _musicTouched = true;
                if (state.music != null) GameAudio.PlayBgm(state.music, state.musicVolume, 0f);
                else GameAudio.StopBgm(0f);
            }
            foreach (var id in _standings.Keys.Where(id => state.Standing(id) == null).ToList()) RemoveStanding(id);
            foreach (var s in state.standings)
            {
                var standing = EnsureStanding(s.character);
                if (standing == null) continue;
                standing.X = s.x;
                standing.Alpha = 1f;
                standing.Brightness = s.highlighted ? 1f : dimmedBrightness;
                standing.View.BeginExpression(s.expression, s.sprite);
                standing.View.SetExpressionBlend(1f);
                standing.View.SetFrame(s.frame, s.frameMask);
                standing.View.SetTint(standing.Brightness, 1f);
            }
            foreach (var slot in _images.Keys.Where(slot => state.Image(slot) == null).ToList()) RemoveImage(slot);
            foreach (var image in state.images)
            {
                var view = EnsureImage(image.slot);
                view.Set(image.sprite, 1f);
                view.Place(this, image.x, image.y, image.size);
            }
            Sync();
        }

        void SetBackground(SpriteRenderer renderer, Sprite sprite, float alpha)
        {
            if (renderer == null) return;
            renderer.sprite = sprite;
            renderer.color = new Color(1, 1, 1, alpha);
            renderer.sortingOrder = renderer == backgroundFront ? backgroundOrder + 1 : backgroundOrder;
            if (sprite == null) return;
            // 화면을 덮는 크기로.
            var size = ViewSize;
            var bounds = sprite.bounds.size;
            var scale = Mathf.Max(size.x / Mathf.Max(0.0001f, bounds.x), size.y / Mathf.Max(0.0001f, bounds.y));
            renderer.transform.localScale = new Vector3(scale, scale, 1f);
            renderer.transform.localPosition = new Vector3(0, 0, 0.02f);
        }

        void SetDim(float amount)
        {
            if (dim == null) return;
            dim.color = new Color(0, 0, 0, Mathf.Clamp01(amount));
            dim.sortingOrder = backgroundOrder + 2;
            var size = ViewSize;
            var bounds = dim.sprite != null ? dim.sprite.bounds.size : Vector3.one;
            dim.transform.localScale = new Vector3(size.x * 1.5f / Mathf.Max(0.0001f, bounds.x), size.y * 1.5f / Mathf.Max(0.0001f, bounds.y), 1f);
            dim.transform.localPosition = new Vector3(0, 0, 0.01f);
        }

        Standing EnsureStanding(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_standings.TryGetValue(id, out var existing) && existing.View != null) return existing;
            var character = Characters(id);
            StandingView view;
            var parent = standingRoot != null ? standingRoot : transform;
            if (character != null && character.standingPrefab != null)
            {
                var instance = Instantiate(character.standingPrefab, parent);
                view = instance.GetComponent<StandingView>() ?? instance.AddComponent<SpriteStandingView>();
                instance.transform.localScale = character.standingPrefab.transform.localScale * character.standingScale;
            }
            else
            {
                var go = new GameObject(id);
                go.transform.SetParent(parent, false);
                view = go.AddComponent<SpriteStandingView>();
            }
            view.name = id;
            view.Bind(character);
            if (character != null && character.standingPrefab == null)
            {
                var sprite = character.SpriteOf(character.defaultExpression);
                if (sprite != null)
                {
                    var scale = standingHeight * ViewSize.y / Mathf.Max(0.0001f, sprite.bounds.size.y) * character.standingScale;
                    view.transform.localScale = new Vector3(scale, scale, 1f);
                }
            }
            var standing = new Standing { View = view, Order = _nextOrder++, Alpha = 0f };
            view.SetSortingOrder(standingOrder + standing.Order * 4);
            view.SetTint(1f, 0f);
            _standings[id] = standing;
            return standing;
        }

        void RemoveStanding(string id)
        {
            if (!_standings.TryGetValue(id, out var standing)) return;
            _standings.Remove(id);
            if (standing.View != null) Destroy(standing.View.gameObject);
        }

        new static void Destroy(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else DestroyImmediate(target);
        }

        // ── 이미지 창 ────────────────────────────────────────

        readonly Dictionary<string, ImageView> _images = new Dictionary<string, ImageView>();

        // 자리 하나의 그림: 두 장을 겹쳐 바꾼다.
        sealed class ImageView
        {
            public SpriteRenderer Current;
            public SpriteRenderer Next;
            public float Alpha;

            public void Set(Sprite sprite, float alpha)
            {
                Current.sprite = sprite;
                Next.sprite = null;
                Alpha = alpha;
                Current.color = new Color(1, 1, 1, alpha);
                Next.color = new Color(1, 1, 1, 0);
            }

            public void Place(DialogueStage stage, float x, float y, float size)
            {
                var sprite = Current.sprite != null ? Current.sprite : Next.sprite;
                var height = sprite != null ? sprite.bounds.size.y : 1f;
                var scale = size * stage.ViewSize.y / Mathf.Max(0.0001f, height);
                var root = Current.transform.parent;
                root.localPosition = stage.Local(x, y) + Vector3.back * 0.5f;
                root.localScale = new Vector3(scale, scale, 1f);
            }
        }

        ImageView EnsureImage(string slot)
        {
            if (_images.TryGetValue(slot, out var existing) && existing.Current != null) return existing;
            if (imageRoot == null)
            {
                imageRoot = new GameObject("Images").transform;
                imageRoot.SetParent(transform, false);
            }
            var root = new GameObject(slot).transform;
            root.SetParent(imageRoot, false);
            SpriteRenderer Renderer(string rendererName, int order)
            {
                var renderer = new GameObject(rendererName).AddComponent<SpriteRenderer>();
                renderer.transform.SetParent(root, false);
                renderer.sortingOrder = order;
                return renderer;
            }
            var order = imageOrder + _images.Count * 2;
            var view = new ImageView { Current = Renderer("Current", order), Next = Renderer("Next", order + 1) };
            view.Set(null, 0f);
            _images[slot] = view;
            return view;
        }

        void RemoveImage(string slot)
        {
            if (!_images.TryGetValue(slot, out var view)) return;
            _images.Remove(slot);
            if (view.Current != null) Destroy(view.Current.transform.parent.gameObject);
        }

        // ── 대사·선택지 ──────────────────────────────────────

        // 문장 틀의 값(대화 재생기가 플레이어 데이터로 채운다).
        public Func<IReadOnlyDictionary<string, object>> TextArguments = () => null;
        // 지금 장소(대화 재생기가 실행기 상태로 채운다)와 장소 찾기.
        public Func<string> CurrentLocation = () => null;
        public Func<string, LocationData> Locations = _ => null;

        public void ShowLine(LineNode line, string speakerName, string text)
        {
            _visibleTotal = DialogueRunner.VisibleLength(text);
            var character = Characters(line.speaker);
            // 목소리: 대사에 목소리가 있으면 재생(이전 목소리는 짧게 사라진다), 없으면 이전 목소리를 멈춘다.
            if (line.voice != null) GameAudio.PlayVoice(line.voice);
            else GameAudio.StopVoice();
            var info = new DialogueLineInfo
            {
                Line = line,
                Speaker = line.speaker,
                Character = character,
                SpeakerName = speakerName,
                Text = text,
                NameColor = character != null ? character.nameColor : Color.white,
                Thought = line.thought,
                Location = CurrentLocation?.Invoke() ?? "",
            };
            var extras = ResolveExtras(line, character);
            // 장소: 기본 추가 글 'location' = 장소 이름(프로필·대사에 같은 키가 있으면 그것), 장소의 추가 글도 더한다.
            if (!string.IsNullOrEmpty(info.Location))
            {
                var place = Locations(info.Location);
                if (place != null)
                    foreach (var extra in place.extras)
                        if (!string.IsNullOrEmpty(extra.key) && !extras.ContainsKey(extra.key))
                            extras[extra.key] = Texts.Has(place.Id + "." + extra.key) ? Texts.Get(place.Id + "." + extra.key) : Texts.Catalog.FormatTemplate(extra.text ?? "");
                if (!extras.ContainsKey("location")) extras["location"] = Texts.NameOf(info.Location);
            }
            info.Extras = extras;
            ui?.ShowLine(info);
        }

        // 추가 글: 대사의 같은 키가 캐릭터 것보다 우선. 번역 표에 '<대사 키>.<키>' 또는 '<캐릭터 ID>.<키>'가 있으면 그것을 쓴다.
        Dictionary<string, string> ResolveExtras(LineNode line, StandingProfileData character) => ResolveExtras(line, character, TextArguments?.Invoke());

        public static Dictionary<string, string> ResolveExtras(LineNode line, StandingProfileData character, IReadOnlyDictionary<string, object> args)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string Resolve(string key, string source) =>
                Texts.Has(key) ? Texts.Get(key, args) : Texts.Catalog.FormatTemplate(source ?? "", args);
            if (character != null)
                foreach (var extra in character.extras)
                    if (!string.IsNullOrEmpty(extra.key)) result[extra.key] = Resolve(character.Id + "." + extra.key, extra.text);
            foreach (var extra in line.extras)
                if (!string.IsNullOrEmpty(extra.key)) result[extra.key] = Resolve(line.lineId + "." + extra.key, extra.text);
            return result;
        }

        public void SetVisibleCharacters(int count) => ui?.SetVisibleCharacters(count, _visibleTotal);

        public void ShowChoices(IReadOnlyList<DialogueChoice> choices) => ui?.ShowChoices(choices);

        public void Hide()
        {
            ui?.SetOpen(false);
            if (!clearOnEnd) return;
            foreach (var id in _standings.Keys.ToList()) RemoveStanding(id);
            foreach (var slot in _images.Keys.ToList()) RemoveImage(slot);
            SetBackground(backgroundBack, null, 0f);
            SetBackground(backgroundFront, null, 0f);
            SetDim(0f);
            ui?.SetLetterbox(0f);
            ui?.SetFlash(Color.clear, 0f);
            ui?.SetFade(0f);
            GameAudio.StopVoice();
            if (_musicTouched && restoreMusicOnEnd)
            {
                if (_musicBefore != null) GameAudio.PlayBgm(_musicBefore);
                else GameAudio.StopBgm();
            }
            _musicTouched = false;            if (effectRoot != null)
                for (var i = effectRoot.childCount - 1; i >= 0; i--)
                    Destroy(effectRoot.GetChild(i).gameObject);
            Release();
        }

        // ── 효과 ─────────────────────────────────────────────

        public IEffectPlayer Begin(EffectNode effect, EffectContext context, StageState before, StageState after)
        {
            Capture();
            var custom = effect.CreatePlayer(this, context, before, after);
            if (custom != null) return custom;
            if (effect.IsState) return new StateBlend(this, effect, before, after);
            switch (effect)
            {
                case ShakeEffect shake:
                    return new ShakePlayer(this, shake.Target(context), shake.strength);
                case FlashEffect flash:
                    return new FlashPlayer(this, flash.color);
                case SpawnEffect spawn:
                    return Spawn(spawn, context);
                case SoundEffect sound:
                    GameAudio.PlaySfx(sound.clip, sound.volume);
                    return null;
                case UiMotionEffect uiMotion:
                    return new UiMotionPlayer(ui, uiMotion);
                default:
                    return null;
            }
        }

        IEffectPlayer Spawn(SpawnEffect spawn, EffectContext context)
        {
            if (spawn.prefab == null) return null;
            var parent = effectRoot != null ? effectRoot : transform;
            var instance = Instantiate(spawn.prefab, parent);
            var target = string.IsNullOrEmpty(spawn.character) ? null : context.Character(spawn.character);
            if (target != null && _standings.TryGetValue(target, out var standing) && standing.View != null)
                instance.transform.position = standing.View.transform.position + Local(0.5f, spawn.screenPosition.y) - Local(0.5f, 0f);
            else instance.transform.localPosition = Local(spawn.screenPosition.x, spawn.screenPosition.y) + Vector3.back * 0.5f;
            // 진행 시간이 있으면 끝날 때 지운다(0이면 이펙트가 스스로 사라지게 둔다).
            return spawn.duration > 0 ? new DestroyPlayer(instance) : null;
        }

        sealed class StateBlend : IEffectPlayer
        {
            readonly DialogueStage _stage;
            readonly StageState _before;
            readonly StageState _after;
            readonly bool _viaBlack;
            readonly bool _background;
            readonly bool _musicChange;
            readonly List<(string id, StandingState from, StandingState to, Standing view)> _standings = new List<(string, StandingState, StandingState, Standing)>();
            readonly List<(string slot, ImageState from, ImageState to, ImageView view, bool swap)> _images = new List<(string, ImageState, ImageState, ImageView, bool)>();
            readonly float _hold;
            bool _swapped;

            public StateBlend(DialogueStage stage, EffectNode effect, StageState before, StageState after)
            {
                _stage = stage;
                _before = before;
                _after = after;
                _viaBlack = effect is BackgroundEffect background && background.viaBlack;
                _hold = _viaBlack && effect.EffectiveDuration > 0 ? Mathf.Clamp(((BackgroundEffect)effect).blackHold / effect.EffectiveDuration, 0f, 0.9f) : 0f;
                _background = before.background != after.background;
                _musicChange = after.musicSet && (before.music != after.music || !Mathf.Approximately(before.musicVolume, after.musicVolume) || !before.musicSet);
                // 브금은 오디오 채널이 교차 페이드한다(효과의 진행 시간 = 페이드 시간).
                if (_musicChange)
                {
                    stage._musicTouched = true;
                    var fade = effect.EffectiveDuration;
                    if (after.music != null) GameAudio.PlayBgm(after.music, after.musicVolume, fade);
                    else GameAudio.StopBgm(fade);
                }
                if (_background && !_viaBlack)
                {
                    stage.SetBackground(stage.backgroundBack, before.background, 1f);
                    stage.SetBackground(stage.backgroundFront, after.background, 0f);
                }
                foreach (var id in before.standings.Select(s => s.character).Union(after.standings.Select(s => s.character)))
                {
                    var from = before.Standing(id);
                    var to = after.Standing(id);
                    if (Same(from, to)) continue;
                    var view = stage.EnsureStanding(id);
                    if (view == null) continue;
                    if (to != null && (from == null || from.expression != to.expression || from.sprite != to.sprite)) view.View.BeginExpression(to.expression, to.sprite);
                    if (to != null && (from == null || from.frame != to.frame || from.frameMask != to.frameMask)) view.View.SetFrame(to.frame, to.frameMask);
                    _standings.Add((id, from, to, view));
                }
                foreach (var slot in before.images.Select(i => i.slot).Union(after.images.Select(i => i.slot)))
                {
                    var from = before.Image(slot);
                    var to = after.Image(slot);
                    if (from != null && to != null && from.sprite == to.sprite && Mathf.Approximately(from.x, to.x) && Mathf.Approximately(from.y, to.y) && Mathf.Approximately(from.size, to.size)) continue;
                    var view = stage.EnsureImage(slot);
                    var swap = from != null && to != null && from.sprite != to.sprite;
                    if (swap) view.Next.sprite = to.sprite;
                    else if (from == null && to != null) view.Set(to.sprite, 0f);
                    _images.Add((slot, from, to, view, swap));
                }
            }

            static bool Same(StandingState a, StandingState b) =>
                a != null && b != null && a.expression == b.expression && a.sprite == b.sprite && Mathf.Approximately(a.x, b.x) && a.highlighted == b.highlighted && a.frame == b.frame && a.frameMask == b.frameMask;

            public void Evaluate(float t)
            {
                var s = _stage;
                if (_background)
                {
                    if (_viaBlack)
                    {
                        // 어두워짐 → (유지) → 밝아짐. 유지 시간은 진행 시간 안에 포함된다.
                        var fadeOut = (1f - _hold) * 0.5f;
                        if (t < fadeOut) s.ui?.SetFade(t / Mathf.Max(0.0001f, fadeOut));
                        else if (t < fadeOut + _hold)
                        {
                            s.ui?.SetFade(1f);
                            if (!_swapped)
                            {
                                _swapped = true;
                                s.SetBackground(s.backgroundBack, _after.background, 1f);
                                s.SetBackground(s.backgroundFront, null, 0f);
                            }
                        }
                        else
                        {
                            if (!_swapped)
                            {
                                _swapped = true;
                                s.SetBackground(s.backgroundBack, _after.background, 1f);
                                s.SetBackground(s.backgroundFront, null, 0f);
                            }
                            s.ui?.SetFade((1f - t) / Mathf.Max(0.0001f, fadeOut));
                        }
                    }
                    else if (s.backgroundFront != null)
                    {
                        if (_after.background != null) s.backgroundFront.color = new Color(1, 1, 1, t);
                        else if (s.backgroundBack != null) s.backgroundBack.color = new Color(1, 1, 1, 1 - t);
                    }
                }
                if (!Mathf.Approximately(_before.dim, _after.dim)) s.SetDim(Mathf.Lerp(_before.dim, _after.dim, t));
                if (!Mathf.Approximately(_before.letterbox, _after.letterbox)) s.ui?.SetLetterbox(Mathf.Lerp(_before.letterbox, _after.letterbox, t));
                if (!Mathf.Approximately(_before.cameraZoom, _after.cameraZoom)) s._zoom = Mathf.LerpUnclamped(_before.cameraZoom, _after.cameraZoom, t);
                if (_before.cameraOffset != _after.cameraOffset) s._offset = Vector2.LerpUnclamped(_before.cameraOffset, _after.cameraOffset, t);

                foreach (var (_, from, to, view) in _standings)
                {
                    var fromAlpha = from != null ? 1f : 0f;
                    var toAlpha = to != null ? 1f : 0f;
                    view.Alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
                    var fromX = from?.x ?? to?.x ?? view.X;
                    var toX = to?.x ?? fromX;
                    view.X = Mathf.LerpUnclamped(fromX, toX, t);
                    var fromB = from == null ? (to != null && to.highlighted ? 1f : s.dimmedBrightness) : from.highlighted ? 1f : s.dimmedBrightness;
                    var toB = to == null ? fromB : to.highlighted ? 1f : s.dimmedBrightness;
                    view.Brightness = Mathf.Lerp(fromB, toB, t);
                    if (to != null && (from == null || from.expression != to.expression || from.sprite != to.sprite)) view.View.SetExpressionBlend(t);
                    view.View.SetTint(view.Brightness, view.Alpha);
                }
                foreach (var (_, from, to, view, swap) in _images)
                {
                    var alpha = Mathf.Lerp(from != null ? 1f : 0f, to != null ? 1f : 0f, t);
                    if (swap)
                    {
                        view.Current.color = new Color(1, 1, 1, 1f - t);
                        view.Next.color = new Color(1, 1, 1, t);
                    }
                    else view.Current.color = new Color(1, 1, 1, alpha);
                    var a = from ?? to;
                    var b = to ?? from;
                    view.Place(s, Mathf.LerpUnclamped(a.x, b.x, t), Mathf.LerpUnclamped(a.y, b.y, t), Mathf.LerpUnclamped(a.size, b.size, t));
                }
            }

            public void Finish()
            {
                var s = _stage;
                if (_background)
                {
                    s.SetBackground(s.backgroundBack, _after.background, 1f);
                    s.SetBackground(s.backgroundFront, null, 0f);
                    if (_viaBlack) s.ui?.SetFade(0f);
                }
                foreach (var (id, _, to, _) in _standings)
                    if (to == null)
                        s.RemoveStanding(id);
                foreach (var (slot, _, to, view, _) in _images)
                {
                    if (to == null) s.RemoveImage(slot);
                    else
                    {
                        view.Set(to.sprite, 1f);
                        view.Place(s, to.x, to.y, to.size);
                    }
                }
            }
        }

        sealed class ShakePlayer : IEffectPlayer
        {
            readonly DialogueStage _stage;
            readonly string _target;
            readonly float _strength;
            readonly float _seed = UnityEngine.Random.value * 100f;

            public ShakePlayer(DialogueStage stage, string target, float strength)
            {
                _stage = stage;
                _target = target;
                _strength = strength;
            }

            public void Evaluate(float t)
            {
                var amplitude = _strength * _stage.ViewSize.y * (1f - t);
                // 진행도로 흔든다(같은 시점이면 같은 모습: 미리보기 시간 이동·일시정지에서도 맞는다).
                var time = t * 25f;
                var offset = new Vector3((Mathf.PerlinNoise(_seed, time) - 0.5f) * 2f, (Mathf.PerlinNoise(time, _seed) - 0.5f) * 2f, 0f) * amplitude;
                Set(offset);
            }

            public void Finish() => Set(Vector3.zero);

            void Set(Vector3 offset)
            {
                if (_target == null) _stage._cameraShake = offset;
                else if (_stage._standings.TryGetValue(_target, out var standing)) standing.Shake = offset;
            }
        }

        sealed class FlashPlayer : IEffectPlayer
        {
            readonly DialogueStage _stage;
            readonly Color _color;

            public FlashPlayer(DialogueStage stage, Color color)
            {
                _stage = stage;
                _color = color;
                stage.ui?.SetFlash(color, 1f);
            }

            public void Evaluate(float t) => _stage.ui?.SetFlash(_color, 1f - t);
            public void Finish() => _stage.ui?.SetFlash(_color, 0f);
        }

        sealed class DestroyPlayer : IEffectPlayer
        {
            readonly GameObject _instance;

            public DestroyPlayer(GameObject instance)
            {
                _instance = instance;
            }

            public void Evaluate(float t)
            {
            }

            public void Finish() => Destroy(_instance);
        }

        sealed class UiMotionPlayer : IEffectPlayer
        {
            readonly DialogueUI _ui;
            readonly UnityEngine.UIElements.VisualElement _element;

            public UiMotionPlayer(DialogueUI ui, UiMotionEffect effect)
            {
                _ui = ui;
                _element = ui != null ? ui.Element(effect.element) : null;
                if (_element != null) ui.Motions.Play(_element, effect.motion);
            }

            public void Evaluate(float t)
            {
            }

            public void Finish()
            {
                if (_element != null) _ui.Motions.Finish(_element);
            }
        }
    }
}
