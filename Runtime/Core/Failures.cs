using System;
using System.Collections.Generic;

namespace GBible
{
    // 플레이어에게 보일 실패(다국어). 행동 안에서 던지면 행동 전체가 취소되고, Message는 지금 언어의 글이다.
    //   throw Game.Fail("fail.notEnoughEnergy", ("need", 2));
    //   화면: try { Game.Do(…); } catch (GameFailure f) { toast.Show(f.Message); }
    // 텍스트 표에 키가 있으면 그 틀을 값으로 채우고, 없으면 기본 문장(fallback, 같은 {값} 틀)을 쓴다. 둘 다 없으면 '[키]'.
    // 라이브러리의 실패(비용이 모자람·선택 개수)도 이 형식이다: 그 키를 텍스트 표에 넣으면 번역된다(키는 FailureKeys).
    // 프로그래머의 실수(없는 ID·잘못된 필드)는 그대로 InvalidOperationException이다 — 플레이어에게 보일 글이 아니다.
    public sealed class GameFailure : InvalidOperationException
    {
        public string Key { get; }
        public IReadOnlyDictionary<string, object> Args { get; }
        public string Fallback { get; }

        public GameFailure(string key, IReadOnlyDictionary<string, object> args = null, string fallback = null) : base(key)
        {
            Key = key ?? "";
            Args = args ?? new Dictionary<string, object>();
            Fallback = fallback;
        }

        // 지금 언어로 다시 만든다(언어를 바꾼 뒤에 다시 보여 줄 때도 맞는 글).
        public override string Message => Text();

        public string Text()
        {
            if (Texts.Has(Key)) return Texts.Get(Key, Args);
            if (!string.IsNullOrEmpty(Fallback)) return Texts.Catalog.FormatTemplate(Fallback, Args);
            return "[" + Key + "]";
        }
    }

    // 라이브러리가 던지는 플레이어용 실패의 텍스트 키(텍스트 표에 넣으면 번역된다). 값 이름은 각 줄의 설명대로.
    public static class FailureKeys
    {
        // {what}: 낼 것(플레이어 데이터 › 필드), {have}: 가진 양, {need}: 낼 양
        public const string NotEnough = "momoi.fail.notEnough";
        // {count}: 골라야 할 개수, {picked}: 고른 개수
        public const string ChooseExactly = "momoi.fail.chooseExactly";
        // {min}, {max}, {picked}
        public const string ChooseBetween = "momoi.fail.chooseBetween";
        // {item}: 후보가 아닌 것
        public const string NotACandidate = "momoi.fail.notACandidate";
        public const string ChosenTwice = "momoi.fail.chosenTwice";
        public const string ChoiceGone = "momoi.fail.choiceGone";
        // {ability}: 능력·아이템 이름, {condition}: 사용 조건 글 — Game.Use
        public const string CannotUse = "momoi.fail.cannotUse";
        // {steps}: 되감으려는 지점 수, {marks}: 남은 지점 수 — Game.Rewind
        public const string RewindTooFar = "momoi.fail.rewindTooFar";
        // 다른 플레이어에게 온 선택에 답하려 함(멀티플레이)
        public const string NotYourChoice = "momoi.fail.notYourChoice";
    }
}
