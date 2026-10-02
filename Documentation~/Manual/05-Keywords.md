# 05. 키워드

**한 줄 요약**: 등급·부위·속성·태그처럼 "정해진 목록에서 고르는 값"은 키워드다. 코드에 `[KeywordGroup]` static class와 `const string` 상수로 정의하고, 필드에 `[Keyword(typeof(그룹))]`를 붙여 그 그룹에서 고르게 한다. 코드가 원본이고, 도감의 키워드 탭은 그 코드를 읽고 고친다.

**언제 쓰는가**

- 아이템 등급(`common`·`rare`·`legendary`), 장비 부위, 몬스터 태그, 속성처럼 기획자가 목록에서 고르는 값
- 하나만 고르는 칸(`string`)과 여러 개 고르는 칸(`List<string>`·`string[]`)
- 저장 파일에 남아도 순서를 바꾸거나 항목을 끼워 넣을 때 깨지지 않아야 하는 값

---

## 0. 용어

| 용어 | 뜻 |
|---|---|
| 키워드 | 필드에 저장되는 글자 ID 하나(예: `"rare"`). 코드에서는 상수(`Rarity.Rare`)로 쓴다. |
| 키워드 ID | 저장되는 글자. lowerCamelCase(영문 소문자로 시작, 단어 사이는 대문자)이고, **모든 그룹을 통틀어 하나뿐**이다. |
| 상수 이름 | 코드에서 쓰는 이름(예: `Rare`). 바꿔도 저장된 데이터는 그대로다. |
| 키워드 그룹 | 키워드 모음. `[KeywordGroup]`을 붙인 static class 하나. |
| 하위 그룹 | 그룹 static class 안에 둔 안쪽 static class. |
| 연결 | 필드가 어느 그룹에서 값을 고르는지. 필드의 `[Keyword(typeof(그룹))]`로 정한다. |

enum 대신 키워드를 쓰는 이유: enum은 Unity 직렬화에서 숫자로 저장되어 순서를 바꾸거나 중간에 끼우면 저장된 값이 다른 뜻이 된다. 키워드는 글자 ID가 저장되므로 그런 일이 없다.

---

## 1. 에디터에서 쓰는 법

### 1.1 키워드 탭

1. 메뉴 **MomoiProject › 도감**을 연다.
2. 사이드바 **선언** 그룹의 **키워드** 탭을 고른다. 위쪽 설명에 "그룹 N개 · 키워드 N개 · 코드가 원본(저장하면 그룹 코드에 씁니다) · 필드 연결: [Keyword(typeof(그룹))]"가 보인다.
3. 그룹은 폴더, 키워드는 항목으로 보인다. 하위 그룹은 상위 그룹 폴더 안에 있다.

### 1.2 그룹 만들기

1. 오른쪽 위 **+ 새 그룹**을 누른다.
2. **새 키워드 그룹** 창에서 채운다.
   - **그룹 이름(코드)**: 대문자로 시작하는 영문(예: `Rarity`, `ItemTag`). 코드에서 `Rarity.Rare`처럼 쓰는 이름이다.
   - **표시 이름 (선택)**: 도감에 보일 이름(예: `등급`).
   - **키워드 ID (선택)**: 여러 개는 줄바꿈·띄어쓰기·쉼표로 나눈다. 예: `common rare legendary`.
3. **만들기**. 새 그룹에는 "새 그룹 · 저장 전" 배지가 붙는다.

### 1.3 그룹 폴더의 도구

| 버튼 | 하는 일 |
|---|---|
| **+ 키워드** | "'그룹'에 키워드 추가" 창에서 **키워드 ID**를 적고 **추가**. 상수 이름은 ID에서 만든다(`legendary` → `Legendary`). |
| **+ 하위 그룹** | "'그룹' 안에 하위 그룹" 창. 상위 그룹 코드 안에 안쪽 클래스로 만든다. |
| **cs 열기** | 그룹이 정의된 코드 파일을 연다. |
| **⋯ › 그룹 지우기** | 그룹 코드(하위 그룹 포함)를 저장할 때 지운다. 이 그룹을 쓰는 필드의 `[Keyword]`는 컴파일 오류가 나므로 직접 고친다. |

### 1.4 키워드 항목

- 키워드를 누르면 **'id' 코드 이름 바꾸기** 창이 뜬다. **새 이름**을 적고 **바꾸기**. 코드의 상수 이름만 바뀌고 저장되는 ID는 그대로라 데이터가 이어진다. 이 상수를 쓰는 게임 코드는 직접 고친다(컴파일 오류로 드러난다).
- 키워드 줄의 **✕**는 저장할 때 코드에서 그 키워드를 뺀다.
- 저장 전인 키워드에는 "저장 전" 배지가 붙는다.

### 1.5 연결된 필드 보기

그룹 폴더 아래 **연결된 필드**에 이 그룹을 쓰는 필드(`클래스.필드`)가 나열된다. 칩에 마우스를 올리면 "하나 고름"/"여러 개 고름"이 보이고, 누르면 그 클래스의 상세가 열린다. 연결이 없으면 "없음 — 필드에 [Keyword(typeof(그룹))]를 붙이거나 클래스 상세에서 연결하세요"가 보인다.

### 1.6 필드를 그룹에 연결하기 (클래스 상세)

1. **선언 › 클래스** 탭에서 클래스를 누른다(도감 종류 탭이면 **필드 연결** 버튼).
2. `string`·`string` 리스트·배열 필드 줄에 **키워드 그룹** 칸이 있다. 옆에 "하나 고름" 또는 "여러 개 고름"이 보인다.
3. 값을 누르면 목록이 열린다: **연결 없음**(자유롭게 글자를 입력), 그룹들, **+ 새 그룹 만들어 연결**.
4. 고르면 저장할 때 그 필드 선언 위에 `[Keyword(typeof(그룹))]`가 쓰인다(이미 있으면 바뀐다).

용도가 정해진 필드(다른 도감 항목을 고르는 `[DataRef]` 같은 어트리뷰트가 붙은 필드)에는 키워드 그룹 칸이 나오지 않는다.

### 1.7 저장

키워드 탭과 클래스 상세에서 한 변경은 모아 두었다가 **저장  Ctrl+S**를 누를 때 코드 파일에 한 번에 쓰고, Unity가 다시 컴파일한다. 새 그룹은 `Assets/Code/Keywords/그룹이름.cs` 파일로 만들어진다. 프로그래머가 다른 파일에 쓴 그룹도 찾아서 그 파일을 고친다.

### 1.8 값 고르기와 검증

연결된 필드는 인스턴스·도감 편집 화면에서 그 그룹의 키워드 목록(하나) 또는 칩(여러 개)으로 고른다. 도감 검증은 그룹에 없는 값이 들어 있으면 "…은(는) 키워드 그룹 …에 없는 키워드입니다. 목록에서 다시 고르거나 키워드 탭에서 더하세요." 오류를 띄운다.

### 1.9 예전 키워드 에셋 옮기기

키워드가 코드 원본이 되기 전의 키워드 그룹·키워드 도감 에셋이 프로젝트에 남아 있으면, 에디터를 열 때 한 번 자동으로 코드로 옮긴다.

- 그룹마다 `Assets/Code/Keywords/그룹.cs`(하위 그룹은 안쪽 클래스), 키워드는 `const string`. ID는 그대로라 저장된 값이 이어진다.
- 그룹에 속하지 않은 키워드는 '이전한 키워드' 그룹으로 모인다.
- 예전 필드 규칙의 키워드 연결은 프로젝트 코드의 필드에 `[Keyword(typeof(그룹))]`로 쓴다. 라이브러리 클래스 필드의 연결은 옮기지 않는다.
- 옛 에셋 파일은 지우지 않고 프로젝트 폴더의 `_RemovedAssets`로 옮긴다.
- 옮기지 못하면 콘솔에 "예전 키워드 에셋을 코드로 옮기지 못했습니다" 오류가 남는다.

---

## 2. 코드로 쓰는 법 — 문법 요소

### 2.1 그룹: `[KeywordGroup("표시 이름")]` static class

```csharp
using GBible;

[KeywordGroup("등급")]
public static class Rarity
{
    public const string Common = "common";
    public const string Rare = "rare";
    public const string Legendary = "legendary";
}
```

- `static class`에 붙인다. 표시 이름은 생략할 수 있다(`[KeywordGroup]`이면 클래스 이름이 표시 이름).
- 그 클래스에 **직접 선언한** `public const string` 상수가 키워드다. 값(`"common"`)이 저장되는 ID, 상수 이름(`Common`)이 코드 이름이다. 빈 글자 상수는 키워드가 아니다.
- 그룹 ID는 형식 전체 이름이다(안쪽 클래스는 `+` 대신 `.`).

### 2.2 하위 그룹: 안쪽 static class

```csharp
using GBible;

[KeywordGroup("몬스터 태그")]
public static class MonsterTag
{
    public const string Boss = "boss";
    public const string Elite = "elite";

    [KeywordGroup("속성")]
    public static class Element
    {
        public const string Fire = "fire";
        public const string Ice = "ice";
    }
}
```

안쪽 클래스에도 `[KeywordGroup]`을 붙인다. 코드에서는 `MonsterTag.Element.Fire`. 하위 그룹의 키워드도 ID는 모든 그룹에서 하나뿐이어야 한다.

하위 그룹은 정리용이다. 필드를 상위 그룹(`MonsterTag`)에 연결하면 그 그룹에 **직접 선언한** 키워드(`boss`·`elite`)만 고를 수 있고, 하위 그룹의 키워드(`fire`·`ice`)는 목록에 나오지 않으며 넣으면 검증 오류다. 하위 그룹의 키워드를 고르게 하려면 필드를 하위 그룹(`MonsterTag.Element`)에 연결한다.

### 2.3 필드 연결: `[Keyword(typeof(그룹))]`

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable]
public class Monster
{
    public string id;
    [Keyword(typeof(Rarity))] public string rarity = Rarity.Common;                     // 하나 고름
    [Keyword(typeof(MonsterTag))] public List<string> tags = new List<string>();        // 여러 개 고름
    [Keyword(typeof(MonsterTag.Element))] public string[] weakTo = new string[0];       // 배열도 여러 개
}
```

| 필드 형식 | 고르는 개수 |
|---|---|
| `string` | 하나 |
| `List<string>` · `string[]` | 여러 개 |

키워드 코드 자체에는 어느 필드와 엮이는지 적지 않는다. 연결은 필드 쪽 어트리뷰트가 정한다. 같은 그룹을 여러 클래스의 여러 필드가 함께 쓸 수 있다.

### 2.4 게임 코드에서 쓰기: 상수로 비교

```csharp
if (monster.rarity == Rarity.Legendary) PlayFanfare();
if (monster.tags.Contains(MonsterTag.Boss)) StartBossMusic();

switch (monster.rarity)
{
    case Rarity.Common: break;
    case Rarity.Rare: break;
}
```

상수라서 `switch`의 `case`에도 쓸 수 있다. 필드에는 글자 ID가 들어 있으므로 글자로 직접 비교해도 되지만, 상수를 쓰면 오타가 컴파일 오류로 드러난다.

### 2.5 실행 중 목록 조회: `Keywords`

```csharp
using GBible;

KeywordGroupInfo group = Keywords.GroupOf(typeof(Rarity));
string label = group.DisplayName;                      // "등급"
foreach (KeywordInfo k in group.Keywords)
{
    string id = k.Id;        // "common"
    string name = k.Name;    // "Common"
}

KeywordInfo found = Keywords.Find("legendary");        // 없으면 null
string groupLabel = found?.Group.DisplayName;          // "등급"
```

| 멤버 | 뜻 |
|---|---|
| `Keywords.Groups` | 모든 그룹(그룹 ID 순) |
| `Keywords.All` | 모든 키워드 |
| `Keywords.Find(id)` | ID로 키워드 하나(`KeywordInfo`) |
| `Keywords.GroupOf(Type)` | 그룹 클래스 → `KeywordGroupInfo` |
| `Keywords.GroupById(id)` | 그룹 ID → `KeywordGroupInfo` |
| `Keywords.GroupOf(FieldInfo)` | 필드에 연결된 그룹(`[Keyword]`가 없으면 `null`) |
| `Keywords.Reset()` | 다시 모으게 한다(에디터 도구용) |

| `KeywordGroupInfo` | 뜻 |
|---|---|
| `Type` · `Id` · `Name` · `Label` | 그룹 클래스, 그룹 ID, 클래스 이름, `[KeywordGroup]`의 표시 이름 |
| `DisplayName` | 표시 이름(없으면 클래스 이름) |
| `Parent` · `Children` | 상위 그룹, 하위 그룹들 |
| `Keywords` | 이 그룹에 직접 선언한 키워드(`KeywordInfo`: `Id`·`Name`·`Group`) |

목록은 처음 쓸 때 한 번 모은다(리플렉션은 이때만). `Keywords.GroupOf(FieldInfo)`는 부를 때마다 어트리뷰트를 읽으므로 매 프레임 부르지 않는다.

---

## 3. 상황별 예제

### 3.1 RPG 장비: 부위(하나) · 등급(하나) · 태그(여러 개)

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;

[KeywordGroup("장비 부위")]
public static class EquipSlot
{
    public const string Weapon = "weapon";
    public const string Armor = "armor";
    public const string Accessory = "accessory";
}

[KeywordGroup("장비 태그")]
public static class EquipTag
{
    public const string Melee = "melee";
    public const string Ranged = "ranged";
    public const string Pierce = "pierce";
}

[Serializable]
public class Equipment
{
    public string id;
    public string name;
    [Keyword(typeof(EquipSlot))] public string slot = EquipSlot.Weapon;
    [Keyword(typeof(Rarity))] public string rarity = Rarity.Common;
    [Keyword(typeof(EquipTag))] public List<string> tags = new List<string>();
    public int power;
}

[Serializable]
public class Loadout
{
    public Dictionary<string, Equipment> worn = new Dictionary<string, Equipment>();   // 키 = 부위 키워드
}

[Serializable]
public class EquipItem : SimpleAction
{
    public string itemId;
    protected override void Run()
    {
        var item = Game.Find<Equipment>(itemId);
        Game.All<Loadout>()[0].worn[item.slot] = item;     // 부위 키워드를 딕셔너리 키로
    }
}

public static class EquipRules
{
    public static int MeleeBonus(Loadout l) =>
        l.worn.Values.Where(e => e.tags.Contains(EquipTag.Melee)).Sum(e => e.power);
}
```

### 3.2 몬스터 태그로 처치 이벤트 거르기

```csharp
using System;
using System.Collections.Generic;
using GBible;

[GameEvent("적 처치")]
public sealed class EnemyKilled
{
    public string enemy;
    [Keyword(typeof(MonsterTag))] public List<string> tags = new List<string>();
}

[Serializable]
public class Finish : GameAction<Monster>
{
    protected override void Run(Monster m)
    {
        Game.Events.Publish(new EnemyKilled { enemy = m.id, tags = new List<string>(m.tags) });
    }
}
```

이벤트 필드에도 키워드를 연결할 수 있다. 로그·도전과제의 "이벤트 받기" 트리거에서 `tags`에 `boss`가 들어 있는지를 목록에서 골라 조건으로 쓴다.

### 3.3 UI에 등급 이름 보이기

```csharp
using GBible;

public static class RarityText
{
    public static string Label(string rarityId) =>
        Keywords.Find(rarityId)?.Name ?? rarityId;     // 코드 이름(예: "Legendary")
}
```

플레이어에게 보일 번역된 이름은 다국어 텍스트에서 키워드 ID로 찾는다.

### 3.4 카드 게임: 하위 그룹으로 정리한 키워드

```csharp
using System;
using System.Collections.Generic;
using GBible;

[KeywordGroup("카드 키워드")]
public static class CardKeyword
{
    [KeywordGroup("발동")]
    public static class Trigger
    {
        public const string OnPlay = "onPlay";
        public const string OnDeath = "onDeath";
    }

    [KeywordGroup("상태")]
    public static class Status
    {
        public const string Taunt = "taunt";
        public const string Stealth = "stealth";
    }
}

[Serializable]
public class Minion
{
    public string id;
    [Keyword(typeof(CardKeyword.Trigger))] public List<string> triggers = new List<string>();
    [Keyword(typeof(CardKeyword.Status))] public List<string> statuses = new List<string>();
}
```

---

## 4. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 상수의 **값**(ID)을 코드에서 바꿨더니 데이터가 깨짐 | 저장 파일·인스턴스에는 예전 ID가 남아 있다. 이름만 바꾸려면 상수 **이름**만 바꾼다(키워드 탭의 "코드 이름 바꾸기"가 그렇게 한다). |
| 두 그룹에 같은 ID | 키워드 ID는 모든 그룹에서 하나뿐이다. 에디터는 "이미 있습니다"로 막는다. 코드에서 직접 겹치게 쓰면 `Keywords.Find`는 먼저 모은 하나만 준다. |
| 새 ID가 거절됨 | ID 규칙(lowerCamelCase: 영문 소문자로 시작, 띄어쓰기·밑줄 없음)을 따른다. |
| 그룹 이름이 거절됨 | 그룹 이름(코드)은 대문자로 시작하는 영문·숫자·`_`다. |
| 키워드 탭에 그룹이 안 보임 | `static class`가 아니거나 `[KeywordGroup]`이 없거나, 아직 컴파일 전이다. 키워드 상수가 `public const string`인지 확인한다. |
| 키워드 그룹 칸이 나오지 않음 | 필드가 `string`·`string` 리스트·배열이 아니거나, 이미 용도가 정해진 어트리뷰트(예: `[DataRef]`)가 붙어 있다. |
| 에디터에서 바꿨는데 코드가 그대로 | 저장(Ctrl+S) 전이다. |
| 그룹을 지웠더니 컴파일 오류 | 그 그룹을 쓰던 `[Keyword(typeof(그룹))]`·상수 사용처를 직접 고친다. |
| enum 필드를 키워드로 쓰고 싶음 | 키워드는 글자 필드에 연결한다. 고르는 값은 enum 대신 키워드로 만든다. |
