# 23. 텍스트와 현지화

플레이어에게 보이는 모든 글은 텍스트 키로 찾고, 문장 틀에 값을 채워 만든다. 어디서든 `Texts.Get(키, 값…)` 한 줄이면 현재 언어의 최종 글자가 나온다. 한국어 조사(을/를, 이/가 …)와 언어별 복수형은 문장 틀의 문법으로 처리하므로 게임 코드가 언어마다 분기하지 않는다.

## 이 문서에서 쓰는 말

| 말 | 뜻 |
|---|---|
| 텍스트 키 | 문장 하나의 이름. 예: `ui.start`, `reward.gain`. DB 항목의 이름은 `<ID>.name`(예: `instance.potion.name`). |
| 문장 틀 | 키에 들어 있는 글. `{item:을} {count}개 얻었습니다.`처럼 값 자리·조사·복수형을 문법으로 적는다. ICU MessageFormat(업계 공통 규격)의 주요 부분 + 한국어 조사 + 용어. |
| 원문 언어 | 작가가 쓰는 언어(기본 `ko`). 모든 언어의 마지막 대체 언어다. |
| 텍스트 표 | 한 언어의 키 → 문장 틀 목록(`TextTable` 에셋). |
| 시트 | 모든 언어의 표를 한 장으로 주고받는 CSV 파일. 번역가가 구글 시트·엑셀에서 연다. |
| 용어 | 문장 속 `[[id]]`. 키워드·DB 항목 이름을 넣고 색·아이콘·툴팁 링크로 꾸민다. |

구성: `Runtime/Core`의 `TextFormat`(틀 해석), `TextCatalog`(언어별 표·현재 언어·대체 순서), `Texts`(어디서든 부르는 입구), `KoreanParticles`(조사), `PluralRules`(복수 구분), `Csv`·`TextSheet`(시트)는 Unity 없이도 돈다(서버·테스트). `Runtime/Unity/Localization`의 `LocalizationSettings`·`TextTable`·`GameLanguage`·`LocalizedText`가 Unity 쪽이다.

---

## 1. 설정과 텍스트 표 만들기

**한 줄 요약.** 다국어 설정 에셋 하나에 지원 언어·대체 언어·언어별 표·용어 모양을 둔다.

**언제.** 프로젝트에서 처음 글을 다국어로 만들 때 한 번, 언어를 더할 때마다.

### 에디터에서

1. 메뉴 `MomoiProject → 다국어 → 설정 열기`. 설정이 없으면 `Assets/MomoiProject/Data/Resources/MomoiProject/LocalizationSettings.asset`과 원문 표 `Assets/MomoiProject/Data/Texts/Texts_ko.asset`을 만들고 선택한다.
2. 인스펙터에서 고친다.
   - **termStyles**: 용어 모양(3.8절).
   - **sourceLanguage**: 원문 언어(기본 `ko`).
   - **languages**: 언어마다 **code**(BCP-47: `ko`, `en`, `ja`, `zh-Hans`, `zh-Hant`, `pt-BR` …), **nativeName**(언어 선택 화면에 보일 그 언어의 이름: 한국어, English, 日本語), **fallbacks**(이 언어에 없는 문장을 찾을 언어들, 쉼표로), **table**(텍스트 표).
3. 언어를 더하고 표를 비워 두면, 시트 내보내기·가져오기 때 `Texts_<code>.asset`이 자동으로 만들어져 연결된다.
4. 표(`TextTable`)는 **language**와 **entries**(key · text) 목록이다. 인스펙터에서 직접 고치거나 시트로 주고받는다. 직접 만들려면 `Create → MomoiProject → 다국어 → 텍스트 표`(설정은 `… → 다국어 설정`).
5. 메뉴 `MomoiProject → 다국어 → DB 이름 키 채우기`: DB 항목마다 `<ID>.name` 키를 원문 표에 더한다(값은 ID). 콘솔 "[다국어] 원문 표에 이름 키 n개를 더했습니다(값은 ID, 원문 표에서 이름으로 바꿔 주세요)." 그다음 원문 표에서 값을 실제 이름으로 고친다.

### 코드로 쓰는 법

설정은 게임이 켜질 때 `GameLanguage`가 자동으로 읽는다. 코드에서 다룰 일은 언어 바꾸기·목록 정도다(4절).

### 주의할 점

- 표는 그 언어를 처음 쓸 때 읽는다(안 쓰는 언어의 문장을 메모리에 올리지 않는다).
- 설정 에셋 이름·위치(`Resources/MomoiProject/LocalizationSettings`)를 바꾸면 런타임이 찾지 못한다. 그때는 원문 언어만으로 돈다.

---

## 2. 글 가져오기 — `Texts`

**한 줄 요약.** `Texts.Get(키)`가 현재 언어의 글을 돌려준다. 값은 이름 붙인 튜플, 익명 객체, 딕셔너리로 넘긴다.

**언제.** UI 글, 로그 메시지, 아이템 설명, 다이얼로그 밖의 모든 플레이어용 글.

### 코드로 쓰는 법

**`Texts.Get(키)`** — 값 없이.

```csharp
using GBible;

string start = Texts.Get("ui.start");   // "시작하기"
```

**`Texts.Get(키, (이름, 값), …)`** — 값을 튜플로. 가장 가볍다.

```csharp
string line = Texts.Get("reward.gain", ("item", "dragonArmor"), ("count", 3));
// 틀 "{item:을} {count}개 얻었습니다." + dragonArmor.name = "용의 비늘 갑옷"
// → "용의 비늘 갑옷을 3개 얻었습니다."
```

**`Texts.Get(키, new { … })`** — 익명 객체로.

```csharp
string same = Texts.Get("reward.gain", new { item = "dragonArmor", count = 3 });
```

**`Texts.Get(키, IReadOnlyDictionary<string, object>)`** — 값을 모아 둔 딕셔너리로.

**`Texts.NameOf(id)`** — DB ID의 현재 언어 이름(`<id>.name`). 없으면 ID 그대로.

```csharp
string potion = Texts.NameOf("instance.potion");
```

**`Texts.Has(키)`** — 키가 대체 순서 어디에든 있는지.

**`new TextKey(키)`** — 값으로 넘기면 그 키의 현재 언어 글이 들어간다.

```csharp
string log = Texts.Get("log.gain", ("what", new TextKey("ui.gold")), ("count", 50));
```

**`Texts.Language`** — 현재 언어 코드(읽기·쓰기). Unity 게임에서는 `GameLanguage.Set`을 쓴다(저장까지 한다).

**플레이어에게 보일 실패 — `Game.Fail` · `GameFailure`** — 행동 안에서 `throw Game.Fail(키, 기본 문장, (이름, 값)…)`을 던지면 행동이 취소되고, 예외의 `Message`가 그 키의 지금 언어 글이다(표에 없으면 기본 문장, 같은 틀 문법). 라이브러리가 던지는 실패(비용이 모자람·선택 개수·사용 조건)도 같은 형식이라, 그 키(`FailureKeys`, `momoi.fail.*`)를 표에 넣으면 번역된다. 자세히는 `30-Balance-Tables.md` 2-4·5-8.

```csharp
throw Game.Fail("fail.frozen", "얼어 있어 {turns}턴 동안 움직일 수 없습니다.", ("turns", 2));
// 표(en): fail.frozen = "Frozen for {turns, plural, one {# turn} other {# turns}}."
```

### 주의할 점

- 키가 어디에도 없으면 `[키]`를 돌려준다. 개발 빌드에서는 콘솔에 "[다국어] 'en'에 텍스트 'shop.title'가 없습니다." 경고가 남는다.
- 익명 객체 형태는 부를 때마다 리플렉션으로 속성 값을 읽는다(속성 목록은 형식마다 한 번 찾아 둔다). 매 프레임 부르는 곳은 튜플 형태를 쓴다. `[Serializable]`·`[GameEvent]` 객체를 값 묶음으로 넘기면 생성 코드로 필드를 읽는다(리플렉션 없음).
- 문장 틀에 쓴 값이 빠지면 그 자리가 `{name}` 그대로 보인다(빠진 값을 알아보기 쉽게).

---

## 3. 문장 틀 문법

**한 줄 요약.** `{값}`, 서식, 복수형, 선택, 한국어 조사, 용어, 따옴표 규칙. 틀에 문법 오류가 있으면 틀 글자를 그대로 보여 준다.

아래 예시는 틀(표에 적는 글)과, 넘긴 값, 결과를 함께 적는다.

### 3.1 값 넣기 `{name}`

| 틀 | 값 | 결과(ko) |
|---|---|---|
| `{name}님 환영합니다` | `name = "아린"` | 아린님 환영합니다 |
| `골드 {gold}` | `gold = 1234567.5` | 골드 1,234,567.5 |
| `{item} 획득` | `item = "dragonArmor"` | 용의 비늘 갑옷 획득 |

- 숫자는 **문장을 찾은 언어**의 천 단위 표기(ko·en `1,234,567.5`, de `1.234.567,5`), 소수 셋째 자리까지.
- 값이 **ID 형식의 글자**(`dragonArmor`, `instance.potion`)이고 그 언어에 `<값>.name` 텍스트가 있으면 이름으로 바뀐다. 이름 텍스트에도 같은 값들이 넘어가므로 이름이 `{count, plural, …}`로 단수·복수를 가를 수 있다.
- 참거짓은 `true`/`false`, 날짜는 짧은 날짜.

### 3.2 서식 `{n, number}` · `{d, date}` · `{t, time}`

| 틀 | 값 | 결과 |
|---|---|---|
| `{n, number}` | 1234.5678 | 1,234.568 |
| `{n, number, integer}` | 2.6 | 3 (반올림, .5는 0에서 먼 쪽) |
| `{p, number, percent}` | 0.5 | 50% |
| `{n, number, 0.0}` | 3.14159 | 3.1 (.NET 서식 문자열) |
| `{d, date}` / `{d, date, long}` | `DateTime` | 짧은 날짜 / 긴 날짜(`long`·`full`) |
| `{t, time}` / `{t, time, long}` | `DateTime` | 짧은 시각 / 긴 시각(`long`·`medium`) |

### 3.3 복수형 `{n, plural, …}`

```
{n, plural, =0 {메시지 없음} one {# message} other {# messages}}
```

- `#`은 그 숫자(천 단위 표기)다.
- `=숫자` 경우가 먼저 맞춰지고, 그다음 언어별 규칙(zero · one · two · few · many · other)으로 고른다. 한국어·일본어·중국어 등은 항상 `other`, 영어는 정수 1만 `one`, 러시아어는 one/few/many, 아랍어는 zero/one/two/few/many/other.
- `offset:1`을 쓰면 `#`과 규칙 판단에 숫자 − 1을 쓴다(`=숫자`는 원래 숫자로 맞춘다).
- `other` 경우는 반드시 있어야 한다(없으면 문법 오류).
- `selectordinal`은 순서 수 규칙으로 고른다: 영어 `one`(1st·21st) · `two`(2nd) · `few`(3rd) · `other`(4th·11th…), 프랑스어 `one`(1er) · `other`, 그 밖의 언어(한국어 등)는 `other`.

### 3.4 선택 `{v, select, …}`

```
{hero} lost {gender, select, male {his} female {her} other {their}} sword.
```

값을 글자로 바꿔 같은 이름의 경우를 고르고, 없으면 `other`. `other`는 반드시 있어야 한다.

### 3.5 한국어 조사 `{name:을}`

값 뒤에 `:조사`를 붙이면 값의 받침에 맞는 조사가 붙는다. 어느 쪽을 써도 된다(`{x:을}` = `{x:를}`).

| 쌍 | 쌍 | 쌍 |
|---|---|---|
| 을/를 | 이/가 | 은/는 |
| 과/와 | 으로/로 (ㄹ 받침은 로) | 으로서/로서 · 으로써/로써 |
| 아/야 | 이여/여 · 이시여/시여 | 이에요/예요 |
| 이었다/였다 | 이라/라 · 이라고/라고 · 이라는/라는 · 이라면/라면 | 이나/나 · 이랑/랑 |
| 이며/며 · 이다/다 · 이고/고 | 이란/란 · 이든/든 · 이야/야 | 이면/면 · 이지만/지만 |

받침은 **읽는 소리**로 판단한다.

- 한글: 마지막 글자의 받침.
- 숫자: 끝자리를 읽은 소리(`3` 삼 → 을, `2` 이 → 를, `1` 일 → (ㄹ) 로), 0으로 끝나면 단위(`10` 십 → 을, `1000` 천 → 을).
- 영문 약어(모두 대문자 또는 한 글자): 알파벳 이름(`HTML` 엘 → 이, `PC` 씨 → 를). L·M·N·R만 받침.
- 영문 단어: 끝소리가 분명할 때만(…ng·m·n·l → 받침, 모음·s·x·z·f·v·r·w·y → 없음). 애매하면 `을(를)`처럼 둘 다 적는다.
- 태그(`<b>검</b>`)와 끝의 따옴표·괄호·문장부호는 판단에서 빠진다.
- 서식과 함께: `{n, number, integer:을}`.

| 틀 | 값 | 결과 |
|---|---|---|
| `{enemy:이} 나타났다!` | goblin(→고블린) / orc(→오크) | 고블린이 나타났다! / 오크가 나타났다! |
| `{place:으로} 이동합니다.` | castle(성) / village(마을) / road(길) | 성으로 / 마을로 / 길로 |
| `{name:아}, 일어나.` | arin(아린) | 아린아, 일어나. |

판단이 틀리는 단어는 직접 고정한다(대소문자 무시, `true` = 받침 있음).

```csharp
using GBible;

KoreanParticles.Overrides["HP"] = false;   // 에이치피 → 를
KoreanParticles.Overrides["Kit"] = true;   // 킷 → 을
```

코드에서 바로 붙일 때: `KoreanParticles.Pick("고블린", "가")` → `"이"`, `KoreanParticles.Attach("포션", "을")` → `"포션을"`.

### 3.6 용어 `[[id]]`

```
[[burn:이]] 번졌다.            → 화상이 번졌다.
[[potion, count]]              → 넘긴 count 값을 이름 텍스트에 넘긴다
[[potion, count=2]]            → 값을 직접 적는다
[[burn:cap]] spreads.          → Burn spreads.
[[burn:cap:이]]                → 글자 바꾸기 + 조사
```

- 이름은 `<id>.name` 텍스트(없으면 ID 그대로).
- 글자 바꾸기: `:cap`(첫 글자 대문자), `:upper`, `:lower`.
- 조사는 꾸밈(태그) **밖**, 글자 바로 뒤에 붙는다: `<link="term:burn">…</link>이`.
- 꾸밈은 3.8절의 용어 모양으로 정한다.

### 3.7 글자 그대로 쓰기와 태그

| 틀 | 결과 |
|---|---|
| `It''s` | It's (`''` → `'`) |
| `'{literal}'` | {literal} |
| `\[[x]]` | [[x]] |
| `<color=red>{n}</color>` | 태그는 해석하지 않고 그대로 통과(번역에도 같은 태그가 있어야 한다) |

### 3.8 용어 모양 — `termStyles`

다국어 설정의 **termStyles**에 용어마다 **id**(키워드 ID 등), **style**(TextMeshPro 스타일 시트의 스타일 이름), **icon**(TextMeshPro 스프라이트 애셋의 스프라이트 이름)을 적는다. 둘 중 하나라도 있으면 용어가 이렇게 꾸며진다.

```
<link="term:burn"><sprite name="burnIcon"><style="Term">화상</style></link>
```

툴팁은 TextMeshPro의 `TMP_TextUtilities.FindIntersectingLink`로 링크 ID를 찾고, `GameLanguage.TermOfLink("term:burn")` → `"burn"`으로 용어 ID를 꺼낸다.

### 상황별 예제

**보상 알림(ko/en).**

| 키 | ko | en |
|---|---|---|
| `reward.gain` | `{item:을} {count}개 얻었습니다.` | `You got {count} {item}.` |
| `dragonArmor.name` | `용의 비늘 갑옷` | `{count, plural, one {Dragon Scale Armor} other {Dragon Scale Armors}}` |

```csharp
Texts.Get("reward.gain", ("item", "dragonArmor"), ("count", 3));
// ko: "용의 비늘 갑옷을 3개 얻었습니다."  en: "You got 3 Dragon Scale Armors."
```

**우편함 개수.**

```
inbox (en) = {n, plural, =0 {No messages} one {# message} other {# messages}}
```

`n = 0 / 1 / 1200` → "No messages" / "1 message" / "1,200 messages".

**상태 이상 설명에 용어 넣기.**

```
status.burn (ko) = [[burn:이]] 번졌다.
burn.name (ko)   = 화상
```

### 주의할 점

- 틀 문법 오류(짝이 맞지 않는 `}`, `other` 없음, 닫히지 않은 `[[`)가 있으면 틀 글자가 그대로 보인다. `번역 검사`(6절)가 찾는다.
- 값 이름은 원문과 번역이 같아야 한다(`{item}`을 번역에서 `{아이템}`으로 바꾸면 안 된다).

---

## 4. 현재 언어 — `GameLanguage`

**한 줄 요약.** 게임이 켜질 때 언어를 정하고(저장된 언어 → 기기 언어 → 원문 언어), 바꾸면 저장하고 화면 글자를 새로 고친다.

**언제.** 설정 화면의 언어 선택, 첫 실행 언어 정하기.

### 코드로 쓰는 법

**`GameLanguage.Set(코드)`** — 언어를 바꾸고 PlayerPrefs에 저장한다. 지원 언어 중 가장 가까운 것을 고른다(`en-US` → `en`). 저장하지 않으려면 `Set(코드, remember: false)`. 설정 화면이 있으면 DB › 설정에 적용 대상 `언어`인 칸을 두는 편이 낫다: 선택지가 지원 언어로 저절로 채워지고, 값은 설정 파일에 남는다(`31-Slots-Settings-Input.md` 5-4).

```csharp
using GBible;

GameLanguage.Set("en");
```

**`GameLanguage.Current`** — 지금 언어 코드. **`GameLanguage.Available`** — 지원 언어 `(code, nativeName)` 목록.

```csharp
foreach (var (code, nativeName) in GameLanguage.Available)
    UnityEngine.Debug.Log($"{code}: {nativeName}");
```

**`GameLanguage.Changed`** — 언어가 바뀌었거나 표를 다시 읽었을 때. 플레이어 데이터를 함께 보여 주는 화면은 이것 대신 **`GameView.Redraw`**를 쓴다(아래 예제). 언어가 바뀌어도, 값이 바뀌어도 그 프레임 끝에 한 번 다시 그린다.

**`GameView.Redraw(draw, 키…)`** — `IDisposable`을 돌려준다. 플레이어 데이터(키를 주면 그 키만, 비우면 아무 키나)가 바뀌거나 언어가 바뀌면 그 프레임 끝(`LateUpdate`, 행동 밖)에 `draw`를 **한 번만** 부른다. 등록한 프레임 끝에 처음 한 번 그린다. `Dispose`하면 그만 그린다. 연출 대기열이 재생 중이면 끝날 때까지 미룬다(기다리지 않으려면 `Redraw(draw, waitForPresentation: false, 키…)`, `32-View-Presentation-Scenes.md` 2-1).

> [!note]- 왜 알림마다 바로 그리지 않는가
> 값이 바뀌었다는 알림은 한 행동에도 여러 번 온다(행동 중간의 Sync, 실패한 행동의 되돌림, Undo). 알림마다 그리면 같은 화면을 한 프레임에 여러 번 계산한다(보이는 건 프레임당 한 번이라 낭비). 그래서 표시만 해 두고 프레임 끝에 한 번 그린다. 행동 실행 중이면 다음 프레임으로 미룬다. 직접 `_dirty` 표시·`Data.Changed` 받기·`GameLanguage.Changed` 받기·`LateUpdate`를 쓰지 않는다.

**`GameLanguage.Reload()`** — 설정·표를 다시 읽는다(에디터에서 시트를 가져오면 자동으로 부른다).

**`GameLanguage.SystemCode(SystemLanguage)`** — 기기 언어를 BCP-47 코드로(`Korean` → `ko`, `ChineseTraditional` → `zh-Hant` …, 모르는 언어는 `null`).

**`GameLanguage.Settings`** — 읽은 설정 에셋(없으면 `null`).

### 상황별 예제

**언어 선택 드롭다운.**

```csharp
using System.Collections.Generic;
using GBible;
using UnityEngine;

public sealed class LanguageMenu : MonoBehaviour
{
    readonly List<string> _codes = new List<string>();

    public IEnumerable<string> Labels()
    {
        _codes.Clear();
        foreach (var (code, nativeName) in GameLanguage.Available)
        {
            _codes.Add(code);
            yield return nativeName;
        }
    }

    public void OnPicked(int index) => GameLanguage.Set(_codes[index]);
}
```

**직접 그리는 글을 값·언어에 맞춰 다시 만들기.**

```csharp
using System;
using GBible;
using UnityEngine;

public sealed class GoldLabel : MonoBehaviour
{
    public string Text { get; private set; }
    IDisposable _view;

    // playerGold가 바뀌거나 언어가 바뀌면 프레임 끝에 한 번 그린다. 처음 한 번은 바로.
    void OnEnable()  => _view = GameView.Redraw(Redraw, "playerGold");
    void OnDisable() => _view?.Dispose();

    void Redraw() => Text = Texts.Get("ui.gold", ("gold", GBibleRuntime.Session.Data.GetNumber("playerGold")));
}
```

키는 `GameIds.PlayerData.X.Id`로 써도 된다. 값을 보여 주지 않고 언어만 따르는 글이면 키 없이 `GameView.Redraw(Redraw)`도 되지만, 그러면 아무 값이 바뀔 때도 다시 그린다. 그런 글은 보통 `LocalizedText`(5절)로 충분하다.

### 주의할 점

- 대체 순서: 현재 언어 → 설정의 대체 언어들(그 대체 언어의 대체까지) → 기본 언어(`pt-BR` → `pt`) → 원문 언어. 복수 규칙과 숫자 표기는 **실제로 문장을 찾은 언어**를 따른다(대체 언어로 보일 때 문장과 맞도록).
- 저장된 언어가 지원 목록에서 빠졌으면 기기 언어, 그것도 없으면 원문 언어로 시작한다.

---

## 5. 화면에 넣기 — `LocalizedText`

**한 줄 요약.** 글자 컴포넌트(TextMeshPro, UI Text 등 `text` 속성이 있는 것) 옆에 붙이고 키와 값을 적으면, 언어가 바뀔 때마다 다시 넣는다. 코드 없이 쓴다.

### 에디터에서

1. 글자 컴포넌트가 있는 오브젝트에 `Add Component → MomoiProject → 다국어 텍스트`.
2. **key**: 텍스트 키. **args**: 값 목록(**name** · **value**). 값이 숫자로 읽히면 숫자로(복수형·천 단위 표기), 아니면 글자로(DB ID면 이름으로) 넘긴다.
3. **target**: 글자를 넣을 컴포넌트. 비우면 같은 오브젝트에서 `text` 속성이 있는 컴포넌트를 찾는다.
4. **onText**: 글자가 바뀔 때마다 불리는 이벤트(직접 넣을 곳이 따로 있을 때).
5. 편집 모드에서도 동작하므로 인스펙터에서 키를 바꾸면 바로 보인다.

### 코드로 쓰는 법

**`SetArg(이름, 값)`** — 값을 바꾸고 다시 넣는다(인스펙터 값보다 우선).

```csharp
using GBible;
using UnityEngine;

public sealed class CoinCounter : MonoBehaviour
{
    [SerializeField] LocalizedText label;

    public void Show(int coins) => label.SetArg("count", coins);
}
```

**`Key`** — 키를 바꾸면 바로 다시 넣는다. **`Text`** — 마지막으로 만든 글. **`Refresh()`** — 지금 값으로 다시 만든다.

### 상황별 예제

**버튼 글 "시작하기"**: TextMeshProUGUI 옆에 다국어 텍스트, key `ui.start`, args 없음.

**"남은 시도 3회"**: key `retry.left`(틀 `남은 시도 {n}회` / en `{n, plural, one {# try left} other {# tries left}}`), 코드에서 `label.SetArg("n", tries)`.

### 주의할 점

- `text` 속성을 찾지 못하면 아무것도 넣지 않는다(onText는 불린다).
- 키가 비어 있으면 아무 일도 하지 않는다.

---

## 6. 시트(CSV) 주고받기와 번역 검사

**한 줄 요약.** 모든 언어 표를 CSV 한 장으로 내보내 번역가에게 주고, 받은 시트를 머리줄 **이름**으로 읽어 들인다.

**언제.** 번역 의뢰·반영, 원문 오탈자 일괄 수정, 출시 전 점검.

### 에디터에서

**내보내기** — `MomoiProject → 다국어 → 시트 내보내기 (CSV)` → 저장 위치 선택(기본 이름 `texts.csv`). UTF-8(BOM 포함)으로 쓴다.

```
#gbible-text,1
key,dialogue,location,speaker,ko,en,note
ui.start,,,,시작하기,Start,
line.3f9a0c1b2d,shopIntro,상점,상인,어서 오세요!,,반가운 말투
```

- 첫 줄 `#gbible-text,1`은 형식·버전 표시(첫 칸이 `#`인 줄은 읽을 때 건너뛴다).
- 머리줄: `key`, 문맥 열(다이얼로그 시스템이 켜져 있으면 `dialogue`·`location`·`speaker`), 언어 열(원문 언어가 첫 언어 열), `note`(메모).
- 행 순서는 원문 표 순서, 그 뒤에 모듈이 더하는 행(다이얼로그 대본 등).

**가져오기** — `MomoiProject → 다국어 → 시트 가져오기 (CSV)` → 파일 선택 → "어느 열을 가져올까요?" 창에서 **번역 언어만**(원문 열은 건너뜀, 원문은 에디터가 기준) / **취소** / **모든 언어**(원문 열도 덮어씀). 콘솔에 "[다국어] 가져옴: en 120개, ja 98개"와 경고가 찍힌다.

- 열은 머리줄 **이름**으로 찾는다(대소문자·앞뒤 공백 무시). 번역가가 열 순서를 바꾸거나 메모 열을 더해도 된다.
- **빈 칸은 기존 값을 지우지 않는다**(번역 없음으로 본다).
- 문맥 열은 읽기 전용이라 가져올 때 무시한다. 다이얼로그 대사의 원문은 그래프가 기준이라 "모든 언어"로 가져와도 원문 표에 넣지 않는다.
- 같은 키가 두 번 나오면 뒤의 값을 쓰고 경고("n번째 줄: 키 '…'가 두 번 나옵니다").

**번역 검사** — `MomoiProject → 다국어 → 번역 검사`. 문제가 없으면 "[다국어] 문제 없음", 있으면 "[다국어] 번역 검사 n건"과 목록:

- 원문 문법 오류 (`ko · key: 문법 오류: …`)
- 언어별 번역 없음 수 (`en: 번역 없음 12개`)
- 원문에 없는 키 (`en · key: 원문에 없는 키`)
- 원문과 다른 값·용어·태그 ("원문의 값 {item}이(가) 없습니다.", "원문에 없는 용어 [[x]]을(를) 씁니다.", "태그가 원문과 다릅니다(…)")

### 코드로 쓰는 법

에디터 밖(서버·도구)에서도 같은 기능을 쓴다.

**`TextSheet.Export(언어들, 표들)`** — CSV 글자.

```csharp
using System.Collections.Generic;
using GBible;

var tables = new Dictionary<string, IReadOnlyDictionary<string, string>>
{
    ["ko"] = new Dictionary<string, string> { ["ui.start"] = "시작하기", ["ui.quit"] = "끝내기, 정말?" },
    ["en"] = new Dictionary<string, string> { ["ui.start"] = "Start" },
};
string csv = TextSheet.Export(new[] { "ko", "en" }, tables);
```

**`TextSheet.Import(csv, 가져올 언어들)`** — `ImportResult.Tables`(언어 → 키 → 문장), `Warnings`, `Version`.

```csharp
var result = TextSheet.Import(csv, new[] { "en" });
foreach (var warning in result.Warnings) System.Console.WriteLine(warning);
```

**`TextSheet.Check(원문, 번역)`** — 문제 문장 목록(비어 있으면 통과).

```csharp
foreach (var problem in TextSheet.Check("{item:을} {count}개", "{count} items"))
    System.Console.WriteLine(problem);   // 원문의 값 {item}이(가) 없습니다.
```

**`Csv.Read(text)` / `Csv.Write(rows)`** — RFC 4180 CSV(따옴표 안 쉼표·줄바꿈, BOM, `\r\n`·`\n`).

### 주의할 점

- 원문은 에디터(원문 표·다이얼로그 그래프)에서 고치는 것이 기준이다. 시트로 원문을 고칠 때만 "모든 언어"를 고른다.
- 시트 형식 버전이 이 도구보다 높으면 경고가 남는다.

---

## 7. 테스트·도구용 — `TextCatalog`

**한 줄 요약.** `Texts`가 쓰는 카탈로그(언어별 표 + 현재 언어 + 대체 순서). 테스트·도구는 따로 만들어 쓴다.

### 코드로 쓰는 법

```csharp
using System.Collections.Generic;
using GBible;

var texts = new TextCatalog { SourceLanguage = "ko", Language = "ko" };
texts.SetTable("ko", new Dictionary<string, string>
{
    ["enemy.appear"] = "{enemy:이} 나타났다!",
    ["goblin.name"] = "고블린",
});
string line = texts.Get("enemy.appear", ("enemy", "goblin"));   // "고블린이 나타났다!"
```

| 멤버 | 뜻 |
|---|---|
| `SetTable(언어, 표)` / `Merge(언어, 표)` / `Set(언어, 키, 틀)` | 표 통째 바꾸기 / 더하기 / 한 줄 |
| `Fallbacks["zh-Hant"] = new[] { "zh-Hans" }` | 언어별 대체 순서 |
| `Chain(언어)` | 찾는 언어 순서(예: `pt-BR` → `en` → `pt` → `ko`) |
| `Loader` | 아직 없는 언어 표를 처음 찾을 때 부르는 함수 |
| `DecorateTerm` | 용어 꾸밈 함수 `(id, 이름) → 글` |
| `Missing` / `LanguageChanged` | 키를 못 찾았을 때 / 언어가 바뀌었을 때 |
| `Pseudo` | 가짜 번역 |
| `FormatTemplate(틀, 값)` | 표에 없는 틀을 바로 채운다(미리보기) |

**가짜 번역** — 영문 글자를 악센트 글자로 바꾸고 `[…~~]`로 감싸 길이를 30% 늘린다. 번역 안 된 글(악센트가 없는 글)과 길이 넘침을 찾기 쉽다.

```csharp
Texts.Catalog.Pseudo = true;   // "Start" → "[Šţåŕţ~~]"
```

### 주의할 점

- `Texts.Catalog`는 앱 전체에 하나다(언어는 세션이 아니라 앱 단위). 테스트에서 바꿨으면 되돌린다. Unity 플레이를 시작할 때 `GameLanguage`가 새 카탈로그로 바꾼다.
- 틀 안에서 다른 키를 부르는 깊이는 8단계까지다(넘으면 틀 글자 그대로).
