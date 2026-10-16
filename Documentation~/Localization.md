# 다국어 텍스트

어디서든 `Texts.Get(키, 값…)` 한 줄로 현재 언어의 최종 글자를 받는다. 시트(CSV)의 각 칸은 **문장 틀**이고, 틀은 ICU MessageFormat(업계 공통 규격)의 주요 부분에 한국어 조사와 용어 두 가지를 더한 문법으로 쓴다.

```csharp
Texts.Get("ui.start");                                              // "시작하기"
Texts.Get("sample.reward", ("item", "template.dragonArmor"), ("count", 3));
Texts.Get("sample.reward", new { item = "template.dragonArmor", count = 3 });
GameLanguage.Set("en");                                             // 바꾸고 저장, 화면의 LocalizedText가 새로 고침
```

## 구성

`Runtime/Core`(엔진 없음, 서버·테스트에서도 동작)의 `TextFormat`이 틀을 해석하고 채우며, `TextCatalog`가 언어별 표·현재 언어·대체 순서를 들고, `Texts`가 어디서든 부르는 입구다. `KoreanParticles`는 받침에 맞는 조사를, `PluralRules`는 언어별 복수 구분을, `Csv`·`TextSheet`는 시트 읽기·쓰기와 번역 검사를 맡는다. `Runtime/Unity/Localization`에는 설정 애셋(`LocalizationSettings` — `Assets/MomoiProject/Data/Resources/MomoiProject/LocalizationSettings.asset`), 언어별 표 애셋(`TextTable`), 시작 시 언어를 정하는 `GameLanguage`(저장된 언어 → 기기 언어 → 원문 언어), 글자 컴포넌트에 넣는 `LocalizedText`가 있다. 에디터 메뉴는 `MomoiProject › 다국어 › 시트 내보내기 (CSV)` · `시트 가져오기 (CSV)` · `번역 검사`다. 설정은 시작 설정이 만들고(언어는 시작 설정의 '언어'), DB 이름 키 채우기는 메뉴가 없어 AI가 코드(`TextSheetTools.FillCodexNames(settings)`)로 한다.

## 문장 틀 문법

값은 `{name}`으로 넣는다. 숫자는 언어에 맞는 천 단위 표기(ko·en `1,234,567.5`, de `1.234.567,5`)로, DB ID 글자(`template.dragonArmor`)는 그 언어의 `template.dragonArmor.name` 텍스트로 바뀐다. 이름 텍스트에도 같은 값이 넘어가므로 영어 이름이 `{count, plural, …}`로 단수·복수를 가를 수 있다. `new TextKey("ui.gold")`를 값으로 넘기면 그 키의 현재 언어 글자가 들어간다. 값이 빠지면 틀 조각(`{name}`)이 그대로 보인다.

서식은 `{n, number}`, `{n, number, integer}`(반올림), `{n, number, percent}`(0.5 → 50%), `{n, number, 0.0}`(.NET 서식), `{d, date}`·`{d, date, long}`, `{t, time}`·`{t, time, long}`이다.

복수는 `{n, plural, =0 {없음} one {# item} other {# items}}`이고 `#`은 그 숫자다. `=숫자`가 먼저 맞춰지고, 그다음 언어별 규칙(영어 one/other, 러시아어 one/few/many/other, 한국어는 other만 …)으로 고른다. `offset:1`도 쓸 수 있다. 선택은 `{gender, select, male {his} female {her} other {their}}`이다. 두 경우 모두 `other`가 반드시 있어야 한다.

한국어 조사는 `{name:을}`처럼 값 뒤에 붙인다. 을/를, 이/가, 은/는, 과/와, 으로/로(ㄹ 받침은 로), 아/야, 이나/나, 이랑/랑, 이다/다 등 어느 쪽을 써도 받침에 맞게 바뀐다. 받침은 읽는 소리로 판단한다(숫자 `3` 삼 → 을, `10` 십 → 을, `2` 이 → 를, 영문 약어 `HTML` 엘 → 이, `PC` → 를). 판단이 애매한 영문 단어는 `을(를)`처럼 둘 다 적고, 틀리는 단어는 `KoreanParticles.Overrides["HP"] = false`로 고정한다. 태그(`<b>검</b>`)는 받침 판단에서 빠진다. 서식과 함께 쓰면 `{n, number, integer:을}`이다.

용어는 `[[burn]]`이다. `burn.name` 텍스트가 이름이 되고, 키워드 `burn`에 **용어 스타일**(TextMeshPro 스타일 이름)이나 **용어 아이콘**(스프라이트 이름)을 적어 두면 `<link="term:burn"><sprite name=…><style="…">화상</style></link>`으로 꾸며진다(툴팁은 `TMP_TextUtilities.FindIntersectingLink` → `GameLanguage.TermOfLink`). 조사는 꾸밈 밖에 붙는다: `[[burn:이]]` → `…</link>이`. 값 넘기기는 `[[potion, count]]`(같은 이름의 값) 또는 `[[potion, count=2]]`, 글자 바꾸기는 `:cap`(첫 글자 대문자), `:upper`, `:lower`이고 조사와 함께 `[[burn:cap:이]]`처럼 이어 쓴다.

글자 그대로 쓰려면 ICU 따옴표 규칙을 따른다: `''` → `'`, `'{literal}'` → `{literal}`, `\[[` → `[[`. `<…>` 태그는 해석하지 않고 그대로 통과한다(번역에서도 같은 태그가 있어야 한다). 틀에 문법 오류가 있으면 틀 글자를 그대로 보여 준다.

## 언어와 대체 순서

언어 코드는 BCP-47(`ko`, `en`, `ja`, `zh-Hans`, `zh-Hant`, `pt-BR` …)이다. 키를 찾는 순서는 현재 언어 → 설정의 대체 언어들 → 기본 언어(`pt-BR` → `pt`) → 원문 언어이고, 어디에도 없으면 `[키]`를 돌려주며 개발 빌드에서 경고를 남긴다. 복수 규칙과 숫자 표기는 **실제로 문장을 찾은 언어**를 따른다. `Texts.Catalog.Pseudo = true`로 가짜 번역(악센트 글자 + `[…~~]`)을 켜면 번역 안 된 글자와 길이 넘침을 찾기 쉽다.

## 시트

`시트 내보내기`는 `#gbible-text,1` 형식 줄, 머리줄 `key,ko,en,…,note`, 그리고 원문 표 순서의 행을 UTF-8 CSV로 쓴다. `시트 가져오기`는 머리줄의 **이름**으로 열을 찾으므로 열 순서를 바꾸거나 메모 열을 더해도 된다. 기본은 번역 언어 열만 가져오고(원문은 에디터가 기준), 빈 칸은 기존 값을 지우지 않는다. `번역 검사`는 원문 문법 오류, 번역 누락 수, 원문에 없는 키, 원문과 다른 값·용어·태그를 콘솔에 나열한다. 다이얼로그 시스템을 켰으면 대본(대사 키 `line.…`, 문맥 열 `dialogue`·`location`·`speaker`)도 함께 나간다(`Dialogue.md` 시트).

## 화면에 넣기

UI Toolkit 화면(프로젝트 UI의 기본)은 UXML의 `binding-path`로 넣는다: `@hud.endTurn`(텍스트 키 하나), `run:gold@hud.gold`(값을 그 키의 틀 `{value}`에) — 언어가 바뀌면 다시 넣는다(`ScreenBinding`, 매뉴얼 35장). 코드에서는 `Texts.Get`.

`LocalizedText` 컴포넌트는 그 밖의 글자 컴포넌트(TextMeshPro, UI Text 등 `text` 속성이 있는 것) 옆에 붙이고 키와 값을 적는다. 언어가 바뀌면 다시 넣는다. 코드에서는 `SetArg("count", 3)`으로 값을 바꾸고, 따로 넣을 곳이 있으면 `onText` 이벤트를 쓴다.
