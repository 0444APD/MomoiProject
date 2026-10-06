using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GBible.Data;
using UnityEngine;

namespace GBible
{
    // 설정(DB › 설정)의 지금 값. 기기마다 하나(MomoiProject/settings.json)이고 슬롯·Undo·판 저장·멀티플레이와 상관없다.
    //   var speed = GameSettings.Number(GameIds.Setting.BattleSpeed);
    //   GameSettings.Set(GameIds.Setting.Subtitles, false);          // 저장 + 적용 대상이면 적용 + Changed
    //   GameSettings.Changed += id => { if (id == GameIds.Setting.BattleSpeed) … };
    // 설정 화면: GameSettings.All(분류별로 묶어 그린다) · Label(id) · ChoicesOf(id) · Reset(id) · ResetAll(분류).
    // 모듈·라이브러리의 기기별 값(입력 바인딩·마지막 슬롯 …)은 Raw/SetRaw로 같은 파일에 둔다.
    public static class GameSettings
    {
        public static event Action<string> Changed;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "MomoiProject", "settings.json");

        static Dictionary<string, object> _values;
        static Dictionary<string, SettingData> _definitions;
        static List<SettingData> _ordered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Changed = null;
            _values = null;
            _definitions = null;
            _ordered = null;
            _unsaved = false;
        }

        // 게임이 켜질 때(GBibleRuntime.Start) 한 번: 파일과 DB를 읽고 적용 대상을 적용한다. 다시 불러도 한 번만 한다.
        public static void Load()
        {
            if (_values != null) return;
            _values = new Dictionary<string, object>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(FilePath) && MiniJson.Deserialize(File.ReadAllText(FilePath)) is Dictionary<string, object> saved)
                    foreach (var pair in saved) _values[pair.Key] = pair.Value;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MomoiProject] 설정 파일을 읽지 못해 기본값으로 시작합니다: " + e.Message);
            }
            var database = GameDatabase.Load();
            _ordered = database == null ? new List<SettingData>() : database.All<SettingData>().Where(s => s != null && !string.IsNullOrEmpty(s.Id)).ToList();
            _definitions = _ordered.ToDictionary(s => s.Id, StringComparer.Ordinal);
            foreach (var setting in _ordered) Apply(setting, Current(setting));
        }

        public static IReadOnlyList<SettingData> All
        {
            get
            {
                Load();
                return _ordered;
            }
        }

        public static SettingData Find(string id)
        {
            Load();
            return id != null && _definitions.TryGetValue(id, out var found) ? found : null;
        }

        public static bool Toggle(string id) => Current(Require(id)) is bool value && value;
        public static double Number(string id) => Convert.ToDouble(Current(Require(id)), CultureInfo.InvariantCulture);
        public static string Choice(string id) => Current(Require(id)) as string ?? "";
        public static string Text(string id) => Current(Require(id)) as string ?? "";
        public static object Get(string id) => Current(Require(id));

        // 값을 바꾼다: 종류에 맞게 고치고(숫자는 범위·단위, 선택지는 있는 것만) 저장한 뒤 적용 대상이면 적용하고 Changed를 쏜다.
        // save가 거짓이면 파일에는 아직 쓰지 않는다(슬라이더를 끄는 동안 — 놓을 때 Flush).
        public static void Set(string id, object value, bool save = true)
        {
            var setting = Require(id);
            var next = Coerce(setting, value);
            if (Equals(next, Current(setting))) return;
            _values[id] = next;
            if (save) Save();
            else _unsaved = true;
            Apply(setting, next);
            Changed?.Invoke(id);
        }

        public static void Reset(string id)
        {
            var setting = Require(id);
            if (!_values.Remove(id)) return;
            Save();
            Apply(setting, Current(setting));
            Changed?.Invoke(id);
        }

        // 분류 하나(비우면 전부)를 기본값으로.
        public static void ResetAll(string category = null)
        {
            foreach (var setting in All.Where(s => category == null || s.category == category).ToList()) Reset(setting.Id);
        }

        // 이 설정에 저장된 값이 있는가(플레이어가 바꾼 적이 있는가). 없으면 기본값을 쓰는 중이다.
        public static bool IsSaved(string id)
        {
            Load();
            return id != null && _values.ContainsKey(id);
        }

        // 언어 설정(적용 대상 = 언어)을 플레이어가 고른 적이 있는가.
        public static bool HasSavedLanguage => All.Any(s => s.target == SettingTarget.Language && IsSaved(s.Id));

        // 설정 화면의 칸 이름: 텍스트 표의 'id.name'(없으면 ID).
        public static string Label(string id) => Texts.NameOf(id);

        // 선택지(언어·해상도는 비워 두면 자동으로 채운다). label은 보일 글(텍스트 키면 번역된 글).
        public static IReadOnlyList<(string value, string label)> ChoicesOf(string id)
        {
            var setting = Require(id);
            if (setting.spec.choices.Count > 0)
                return setting.spec.choices.Select(c => (c.value, Texts.Has(c.label) ? Texts.Get(c.label) : string.IsNullOrEmpty(c.label) ? c.value : c.label)).ToList();
            switch (setting.target)
            {
                case SettingTarget.Language:
                    return GameLanguage.Available.Select(l => (l.code, l.nativeName)).ToList();
                case SettingTarget.Resolution:
                    return Screen.resolutions.Select(r => $"{r.width}x{r.height}").Distinct().Reverse().Select(r => (r, r.Replace("x", " × "))).ToList();
                default:
                    return Array.Empty<(string, string)>();
            }
        }

        // ── 기기별 원시 값(모듈·라이브러리용) ────────────────────

        // 파일 안에서는 '$' 접두어로 둔다(DB ID는 '$'로 시작할 수 없어 설정 ID와 겹치지 않는다).
        public static string Raw(string key)
        {
            Load();
            return _values.TryGetValue("$" + key, out var value) ? value as string : null;
        }

        public static void SetRaw(string key, string value)
        {
            Load();
            if (value == null) _values.Remove("$" + key);
            else _values["$" + key] = value;
            Save();
        }

        // ── 안쪽 ───────────────────────────────────────────────

        static SettingData Require(string id) =>
            Find(id) ?? throw new InvalidOperationException($"[MomoiProject] 없는 설정 '{id}'입니다(DB › 설정).");

        static object Current(SettingData setting) =>
            _values.TryGetValue(setting.Id, out var saved) && saved != null ? Coerce(setting, saved) : Default(setting);

        static object Default(SettingData setting)
        {
            var spec = setting.spec;
            switch (spec.kind)
            {
                case SettingKind.Toggle: return spec.defaultToggle;
                case SettingKind.Slider: return Snap(spec, spec.defaultNumber);
                case SettingKind.Choice:
                    if (!string.IsNullOrEmpty(spec.defaultChoice)) return spec.defaultChoice;
                    // 언어: 첫 실행은 기기 언어(선택지에 있으면 — 없으면 같은 언어 계열, 그것도 없으면 첫 선택지).
                    if (setting.target == SettingTarget.Language && spec.choices.Count > 0)
                    {
                        var device = GameLanguage.SystemCode(Application.systemLanguage) ?? "";
                        var family = device.Split('-')[0];
                        var match = spec.choices.FirstOrDefault(c => string.Equals(c.value, device, StringComparison.OrdinalIgnoreCase))
                                    ?? spec.choices.FirstOrDefault(c => c.value != null && string.Equals(c.value.Split('-')[0], family, StringComparison.OrdinalIgnoreCase));
                        if (match != null) return match.value;
                    }
                    if (spec.choices.Count > 0) return spec.choices[0].value;
                    if (setting.target == SettingTarget.Language) return GameLanguage.Current;
                    if (setting.target == SettingTarget.Resolution) return $"{Screen.currentResolution.width}x{Screen.currentResolution.height}";
                    return "";
                default: return spec.defaultText ?? "";
            }
        }

        static object Coerce(SettingData setting, object value)
        {
            var spec = setting.spec;
            switch (spec.kind)
            {
                case SettingKind.Toggle:
                    if (value is bool flag) return flag;
                    if (value is string text && bool.TryParse(text, out var parsed)) return parsed;
                    return value is double number ? number != 0 : Default(setting);
                case SettingKind.Slider:
                    try { return Snap(spec, Convert.ToDouble(value, CultureInfo.InvariantCulture)); }
                    catch (Exception) { return Default(setting); }
                case SettingKind.Choice:
                {
                    var picked = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                    var choices = spec.choices.Count > 0 ? spec.choices.Select(c => c.value) : null;
                    return choices == null || choices.Contains(picked) ? picked : Default(setting);
                }
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
        }

        static double Snap(SettingSpec spec, double value)
        {
            if (spec.max > spec.min) value = Math.Max(spec.min, Math.Min(spec.max, value));
            if (spec.step > 0) value = spec.min + Math.Round((value - spec.min) / spec.step) * spec.step;
            return value;
        }

        // 아직 파일에 쓰지 않은 값(Set(…, save: false))을 쓴다.
        public static void Flush()
        {
            if (_unsaved) Save();
        }

        static bool _unsaved;

        static void Save()
        {
            try
            {
                _unsaved = false;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, MiniJson.Serialize(_values));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MomoiProject] 설정을 저장하지 못했습니다: " + e.Message);
            }
        }

        // 적용 대상: 언어·음량은 언제나, 화면(전체 화면·해상도)은 빌드에서만(에디터 창을 건드리지 않게).
        static void Apply(SettingData setting, object value)
        {
            try
            {
                switch (setting.target)
                {
                    case SettingTarget.Language:
                        if (value is string code && code.Length > 0 && code != GameLanguage.Current) GameLanguage.Set(code, remember: false);
                        break;
                    case SettingTarget.MasterVolume: GameAudio.SetVolume(AudioChannel.Master, (float)Convert.ToDouble(value), save: false); break;
                    case SettingTarget.BgmVolume: GameAudio.SetVolume(AudioChannel.Bgm, (float)Convert.ToDouble(value), save: false); break;
                    case SettingTarget.VoiceVolume: GameAudio.SetVolume(AudioChannel.Voice, (float)Convert.ToDouble(value), save: false); break;
                    case SettingTarget.SfxVolume: GameAudio.SetVolume(AudioChannel.Sfx, (float)Convert.ToDouble(value), save: false); break;
                    case SettingTarget.UiVolume: GameAudio.SetVolume(AudioChannel.Ui, (float)Convert.ToDouble(value), save: false); break;
                    case SettingTarget.VSync: QualitySettings.vSyncCount = value is bool on && on ? 1 : 0; break;
                    case SettingTarget.FrameRate:
                        var fps = (int)Math.Round(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                        Application.targetFrameRate = fps <= 0 ? -1 : fps;
                        break;
                    case SettingTarget.Fullscreen:
                        if (!Application.isEditor) Screen.fullScreen = value is bool full && full;
                        break;
                    case SettingTarget.Resolution:
                        if (Application.isEditor || !(value is string size)) break;
                        var parts = size.Split('x');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h)) Screen.SetResolution(w, h, Screen.fullScreenMode);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MomoiProject] 설정 '{setting.Id}'를 적용하지 못했습니다: {e.Message}");
            }
        }
    }

    // 슬롯 하나의 요약(GBibleRuntime.SlotInfoOf·Slots). 슬롯 고르기 화면에 쓴다.
    public sealed class SlotInfo
    {
        public string Name;
        public bool HasProfile;
        public bool HasRun;
        // 마지막으로 저장한 때(지역 시각). 저장한 적 없으면 default.
        public DateTime SavedAt;
        // 누적 플레이 시간(초).
        public double PlayTime;
        // GBibleRuntime.SlotSummary가 남긴 한 줄.
        public string Summary = "";

        public bool IsEmpty => !HasProfile && !HasRun;
    }
}
