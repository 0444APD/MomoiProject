using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace GBible
{
    // 화면 연결 — 값 하나 ↔ 화면(코드 없이): 플레이어 데이터의 필드가 바뀌면 글자·숫자·참거짓을 이벤트로 보낸다.
    // 인스펙터에서 이벤트를 TMP 글자(text)·슬라이더(value)·오브젝트 켜기(SetActive)에 연결한다.
    //   키 run, 필드 battle.energy, 글자 틀 "{value} / 3"  → onText → TMP_Text.text
    // 글자 틀에 텍스트 키를 적으면 번역된 틀을 쓴다({value}가 값). 목록이면 {value}는 개수.
    // 연출이 재생 중이면 끝난 뒤에 바뀐다(기다리지 않으려면 '연출 기다리기'를 끈다).
    [AddComponentMenu("MomoiProject/데이터 연결")]
    public sealed class DataBinding : MonoBehaviour
    {
        [Tooltip("플레이어 데이터 ID(DB › 플레이어 데이터)")] public string key = "";
        [Tooltip("필드 경로(비우면 값 전체). 예: battle.energy")] public string path = "";
        [Tooltip("글자 틀. {value}가 값. 텍스트 키를 적으면 번역된 틀")] public string format = "{value}";
        [Tooltip("연출 대기열이 끝난 뒤에 바꾼다")] public bool waitForPresentation = true;
        public UnityEvent<string> onText = new UnityEvent<string>();
        public UnityEvent<float> onNumber = new UnityEvent<float>();
        public UnityEvent<bool> onBool = new UnityEvent<bool>();

        IDisposable _watch;

        void OnEnable() => _watch = GameView.Redraw(Draw, waitForPresentation, key);

        void OnDisable()
        {
            _watch?.Dispose();
            _watch = null;
        }

        void Draw()
        {
            var session = GBibleRuntime.Session;
            if (session == null || string.IsNullOrEmpty(key) || !session.Data.IsDefined(key)) return;
            var root = session.Data.Get(key);
            var value = string.IsNullOrEmpty(path) ? root : FieldPaths.Resolve(root, path).FirstOrDefault();
            if (value is ICollection collection && !(value is string)) value = collection.Count;
            var number = ToNumber(value);
            var template = string.IsNullOrEmpty(format) ? "{value}" : Texts.Has(format) ? null : format;
            var args = TextArgs.From(("value", value));
            onText.Invoke(template == null ? Texts.Get(format, args) : Texts.Catalog.FormatTemplate(template, args));
            onNumber.Invoke((float)number);
            onBool.Invoke(value is bool flag ? flag : number != 0 || (value is string text && text.Length > 0));
        }

        static double ToNumber(object value)
        {
            switch (value)
            {
                case null: return 0;
                case bool flag: return flag ? 1 : 0;
                case BigNumber big: return big.ToDouble();
                case string text: return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
                case IConvertible _:
                    try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
                    catch (Exception) { return 0; }
                default: return 0;
            }
        }
    }
}
