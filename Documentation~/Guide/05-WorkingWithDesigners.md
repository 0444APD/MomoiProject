# 05. 기획자와 함께 일하기

기획자가 코드를 열지 않고 DB(`MomoiProject → DB`)만으로 카드·몬스터·물약·유물을 채우고 고치게 만든다. 프로그래머는 클래스·Block·규칙을 먼저 갖추고, 기획자 화면에서 헷갈릴 칸과 잘못 넣을 값을 미리 막는다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | DB | 게임 데이터를 만들고 고치는 에디터 창. 항목은 ID로만 식별한다. |
> | 인스턴스 | 코드로 만든 클래스의 원본 하나(타격 카드, 고블린 …)를 DB에서 값으로 채운 것. 게임은 원본을 읽거나 사본을 만들어 쓴다. |
> | Block | DB의 트리거·결과 칸에 끼워 넣는 작은 동작 단위 클래스. 프로그래머가 만들고 기획자가 조립한다(효과 Block "피해", 대상 Block "모든 적" …). |
> | Trigger · Outcome | "언제"를 정하는 칸과 "무엇을"을 정하는 칸. 이 가이드에서는 트리거·결과라고도 쓴다. |
> | 작업본 | DB에서 고친 내용을 에셋에 쓰기 전에 모아 두는 사본. 저장(Ctrl+S)할 때 한 번에 쓴다. |
> | 검증 | DB가 끊긴 참조·빈 필수 값·잘못된 ID 같은 문제를 찾는 것. 오류가 하나라도 있으면 빌드가 막힌다. |

---

## 1. 나눌 일 정하기

- **프로그래머**: 클래스(어떤 칸이 있나)·행동·Block·`[BindTo]` 함수·프로젝트 고유 기능.
- **기획자**: 인스턴스 값·키워드 목록·트리거와 결과 조립·이름 텍스트.

이 나눔을 표로 적어 공유한다.

> [!note]- 누가 무엇을, 어디서
> | 이런 것 | 누가 | 어디서 |
> |---|---|---|
> | 카드·몬스터·물약의 수치·그림·이름 | 기획자 | DB › 인스턴스, 텍스트 표 |
> | 등급·속성 같은 고르는 값의 목록 | 기획자(코드가 원본이라 저장하면 코드 파일이 바뀐다) | DB › 키워드 |
> | "언제 무엇을"의 조립(유물 발동·도전과제·로그) | 기획자 | 트리거·결과 칸 |
> | 효과 하나·대상 고르기 하나·조건 하나의 동작 | 프로그래머 | Block 클래스 |
> | Block 조합으로 나타내기 어려운 그 항목만의 결과 | 프로그래머 | `[BindTo]` 함수 |
> | 데이터 모양(어떤 칸이 있는가)·플레이어 입력 | 프로그래머 | 클래스·행동 |
>
> Block과 `[BindTo]`의 기준은 [04장](04-AddingContent.md) 2절.

## 2. 고르는 값은 키워드로

등급·속성·카드 종류처럼 목록에서 고르는 칸은 enum 대신 키워드 그룹으로 둔다. Rules에 그룹을 쓰고, 필드에 `[Keyword]`를 붙인다.

```csharp
[KeywordGroup("등급")]
public static class Rarity
{
    public const string Common = "common";
    public const string Rare = "rare";
    public const string Legendary = "legendary";
}

[Keyword(typeof(Rarity))] public string rarity = Rarity.Common;
```

이후 목록은 기획자가 DB › **키워드**에서 더하고 지운다.

> [!note]- 키워드 자세히
> - **코드가 원본이다.** 기획자가 키워드 탭에서 더하거나 지우면 저장할 때 이 코드 파일이 고쳐지고 Unity가 다시 컴파일한다. 프로그래머가 코드에 상수를 더해도 탭에 바로 나온다.
> - **enum이 아닌 이유**: 키워드는 글자 ID로 저장되므로 순서를 바꾸거나 중간에 끼워도 저장 파일이 깨지지 않는다. enum은 숫자로 저장되어 깨진다.
> - 키워드 탭에서 키워드를 누르면 나오는 **코드 이름 바꾸기**는 상수 이름만 바꾸고 저장되는 ID는 그대로 둔다. ID 자체를 코드에서 바꾸면 이미 채운 인스턴스와 저장 파일에 옛 ID가 남으므로 하지 않는다.
> - 카드 던전은 등급·속성·카드 종류가 모두 키워드 그룹이다(`Rules/Data/Keywords.cs`). 화상 같은 상태는 이름만 고르는 값이 아니라 중첩·지속이 있으므로 키워드가 아니라 DB › 상태 효과의 항목이다.

## 3. 인스턴스 화면 정리

- 기획자가 볼 필요 없는 칸 → `[Hide]`.
- 다른 칸에 따라 의미가 생기는 칸 → `[ShowIf]`.
- 있을 수도 없을 수도 있는 기능 → `[SerializeReference]` 기능 필드.

카드 원본 `Card`의 일부:

```csharp
public bool upgradable;
[ShowIf(nameof(upgradable)), DataRef(typeof(Card))] public string upgradeTo;   // 강화 가능할 때만 보인다
[Hide] public int balanceScore;                                                       // 밸런스 도구가 채우는 값
[SerializeReference] public Usable usable;                                            // 기능 필드
```

> [!note]- 칸을 어떻게 보이게 하나
> | 이럴 때 | 이것 |
> |---|---|
> | 원본에 있지만 기획자가 볼 필요 없는 칸(도구가 채우는 값) | `[Hide]`. 게임 중에 바뀌는 값은 원본이 아니라 판 데이터 클래스(`DeckCard` 같은)에 둔다 |
> | 다른 칸이 켜졌거나 어떤 값일 때만 의미 있는 칸 | `[ShowIf(nameof(필드))]` · `[ShowIf(nameof(필드), 값)]` |
> | 있을 수도 없을 수도 있는 기능(사용 기능, 장비 기능) | `[SerializeReference]` 기능 필드(+ 기능으로 붙인다) |
> | 정해진 목록에서 고르는 글자 | 키워드(`[Keyword(typeof(그룹))]`, 2절) |
> | 다른 DB 항목의 ID | `[DataRef(typeof(종류))]`(4절) |
> | 비면 안 되는 칸 | 클래스 상세의 **필수**(6절) |
> | 칸 이름만으로 뜻이 모자람 | `[FieldLabel("설명")]`. 인스턴스 화면에서는 마우스를 올리면 보이는 설명이 되고, 칸 이름표는 변수 이름 그대로다 |

> [!note]- 원본과 판 데이터를 나눈 이유
> 카드 던전은 카드를 두 클래스로 나눈다. DB › 인스턴스에서 기획자가 채우는 **원본** `Card`(이름·비용·등급·그림·사용 기능)와, 판 데이터의 덱에 드는 **한 장** `DeckCard`(장마다 다른 `id`, 원본 ID `card`, 강화 여부 `upgraded`)다. 기획자 화면에는 원본만 보이므로 게임이 채우는 칸이 처음부터 섞이지 않는다.

> [!note]- `[Hide]`·`[ShowIf]`·기능 필드 자세히
> - `usable`이 비어 있으면 인스턴스 화면에 **+ 기능** 버튼이 보이고, 붙이면 트리거·결과 칸이 열린다.
> - `[Hide]`·`[ShowIf]`는 인스턴스 편집 화면에만 적용된다. 모니터는 모든 칸을 보여 준다.
> - DB › 클래스 › `Card`의 **인스턴스에서 숨김  [Hide]** 토글로 기획자가 직접 숨김을 바꿀 수도 있다. 저장하면 코드에 어트리뷰트가 쓰인다. `[ShowIf]`는 그 자리에 조건이 보이기만 한다.

> [!note]- 다른 장르라면 — 한 아이템에 기능이 여럿
> RPG처럼 아이템 종류가 많고 한 아이템이 장비이면서 사용 가능하면, 종류별 상속 대신 한 아이템 클래스에 기능 필드를 여럿 둔다(`[SerializeReference] public Equip equip;`, `[SerializeReference] public Usable usable;`). 기획자는 아이템마다 **+ 기능**으로 붙였다 뗀다.

## 4. 다른 항목을 가리키는 칸은 `[DataRef]`

다른 DB 항목의 ID를 담는 `string`(또는 `List<string>`) 칸에는 모두 `[DataRef(typeof(종류))]`를 붙인다. 원본 클래스와 판 데이터 클래스 둘 다다.

| 칸 | 붙인 것 |
|---|---|
| `Card.upgradeTo`, `DeckCard.card`, `Profile.unlocked` | `[DataRef(typeof(Card))]` — 값이 `Card`인 인스턴스만 목록에 뜨고, 다른 클래스를 고르면 검증 오류 |
| `Enemy.monster` | `[DataRef(typeof(MonsterData))]` |
| 클래스를 가리지 않는 칸(가방 칸의 아이템 등) | `[DataRef(typeof(InstanceData))]` — 모든 인스턴스 |
| `RunState.relics` | `[DataRef(typeof(RelicData))]` |

> [!note]- 붙이면 무엇이 달라지나
> - 칸이 항목 목록 드롭다운이 된다. 모니터에서도 목록으로 고른다.
> - 검증이 없는 ID를 찾아낸다.
> - 가리키는 항목을 지우려 하면 DB가 **삭제할 수 없습니다** 창에 사용처를 보여 준다.
> - ID를 바꾸면 이 칸의 값도 따라 바뀐다. 출시 뒤에는 저장 파일 안의 옛 ID도 불러올 때 새 ID로 바뀐다(8절).
>
> 그냥 `string`이면 기획자가 ID를 손으로 적어야 하고, 위의 어느 것도 되지 않는다.

> [!note]- 다른 종류를 가리키는 어트리뷰트
> | 가리키는 것 | 어트리뷰트 |
> |---|---|
> | 다른 DB 항목(인스턴스·유물 …) | `[DataRef(typeof(종류))]` |
> | 플레이어 데이터 | `[PlayerDataRef]` |
> | 로그 | `[StatRef]` |
> | 도전과제 | `[AchievementRef]` |
> | 이벤트 형식 | `[EventTypeRef]` |

> [!note]- 다른 장르라면 — 서로 가리키는 데이터
> 턴제 전술처럼 유닛·스테이지·웨이브가 서로를 가리키면 `StageData` 같은 DB 종류를 만들고 `[DataRef(typeof(InstanceData))] List<string> mobs`로 잇는다. 스테이지가 쓰는 몬스터는 지울 수 없게 되고, 몬스터 ID를 바꾸면 스테이지 값도 따라 바뀐다.

## 5. Block에 이름표 달기

모든 Block에 `[BlockInfo]`로 표시 이름·분류(`Category`)·설명(`Description`)을 준다. 칸이 있는 Block에는 한 줄 문장(`Sentence`)도 준다.

```csharp
[Serializable, BlockInfo("피해", Category = "카드", Description = "대상에게 피해를 줍니다(방어도 먼저).", Sentence = "{amount} 피해")]
public class DealDamage : Effect<Fighter> { … }

[Serializable, BlockInfo("무작위 적", Category = "대상", Description = "살아 있는 적 중에서 무작위로 고릅니다(고른 대상은 버립니다).", Sentence = "무작위 적 {count}명")]
public class RandomEnemy : TargetSelector<Fighter> { … }
```

이제 기획자는 "6 피해"의 6만 눌러 고친다.

> [!note]- 화면에 어떻게 보이나
> - 기획자는 결과 칸의 **+ 결과 추가**·**+ 효과 추가**·**+ 대상 추가**와 트리거 칸의 **+ 블럭** 목록에서 Block을 고른다. 목록의 이름은 `[BlockInfo]`의 표시 이름, 묶음은 `Category`, 이름 옆 설명은 `Description`이다.
> - `Sentence`의 `{필드이름}` 자리가 눌러서 바꾸는 칸이 된다. `Sentence`가 없으면 Block 이름 아래에 칸이 나열된 상자로 보인다.
> - 카드 던전의 Block에는 아직 `Sentence`가 없고, `RandomEnemy`("무작위 적")는 `Description`도 없어 목록에서 이름만 보인다. 위처럼 더한다. 처음 보는 Block일수록 설명을 단다.

## 6. 필드 규칙: 필수와 스프라이트 규격

DB › 클래스 › `Card`를 열면 필드마다 설정이 있다. 코드는 쓰지 않는다.

1. `title`·`art`에 **필수 (비어 있으면 DB 검증 오류)** 를 켠다.
2. DB › **스프라이트 규격** 탭에서 그룹 `card`를 만들고 규격을 둔다(예: `cardArt` 300×420, `cardIcon` 64×64).
3. `art`의 **스프라이트 규격 그룹**에 `card`를 연결한다.

이제 기획자가 카드 그림을 넣으면 맞춤 창이 열리고, 중심·배율을 한 번 정하면 모든 규격이 함께 베이크된다. 화면 코드는 원본 그림과 규격 ID로 베이크본을 꺼낸다.

```csharp
var icon = SpriteSpecs.Get(Rules.CardData(game, deckCard.card).art, "cardIcon");
```

> [!note]- 자세히
> - 그림을 빠뜨린 카드는 검증 오류로 빌드가 막힌다. 숫자 0은 비었다고 보지 않으므로 `cost` 같은 숫자 칸에는 필수가 효과가 없다.
> - `SpriteSpecs.Get`은 베이크 전에는 원본을 돌려주므로 화면이 깨지지 않는다.
> - `MonsterData.portrait`·`RelicData.icon`도 같은 방식으로 그룹을 정해 두면, 기획자가 크기를 맞추느라 그림 파일을 따로 자를 일이 없다. 유물 같은 DB 종류는 그 탭 위의 **필드 연결** 버튼으로 같은 화면을 연다.
> - 이 설정은 코드가 아니라 DB에 저장된다(Ctrl+S).

## 7. 고유 검사 넣기

"이 값이면 안 된다"를 검증에 더한다. 오류는 빌드를 막는다.

- **DB 종류**(유물): `ValidateData`를 덮어쓴다.
- **Block**: `Validate`를 덮어쓴다.

```csharp
// RelicData
public override void ValidateData(IValidationContext context)
{
    if (trigger == null) context.Error("발동(트리거)이 없습니다.");
    else trigger.Validate(context);
    foreach (var outcome in outcomes)
        if (outcome == null) context.Error("비어 있는 효과 칸이 있습니다.");
        else outcome.Validate(context);
}

// DealDamage
public override void Validate(IValidationContext context)
{
    if (amount <= 0) context.Error("피해는 1 이상이어야 합니다.");
}
```

> [!note]- 자세히
> - `context.Error`는 빌드를 막고, `context.Warning`은 표시만 한다. 메시지에 필드 이름이 들어 있으면 상세 창에서 그 칸도 빨갛게 보인다.
> - `ValidateData`에서는 안쪽 Trigger·Outcome의 검사도 이어서 부른다(위 코드의 `trigger.Validate`·`outcome.Validate`).
> - `Validate`를 덮어쓸 수 있는 Block: 조건 Block(`SimpleCondition`·`Condition`), 코딩한 결과(`Reward`), 효과 Block(`Effect<T>`), 대상 Block(`TargetSelector<T>`). 효과·대상 Block의 검사는 그 Block을 담은 '대상에게 효과' 결과의 검증이 불러 준다.
> - 실행 중에 걸러 내는 것보다 DB에서 저장할 때 빨간 글씨로 보이는 편이 기획자에게 빠르다.
> - 인스턴스 값 자체(카드·몬스터 클래스)에는 형식 검사 외에 고유 검사를 넣을 곳이 없다. 필수·키워드·`[DataRef]`로 막을 수 있는 만큼 막고, 나머지는 [08장](08-TestAndDebug.md)의 EditMode 테스트로 확인한다.

## 8. ID 바꾸기와 지우기

- **짓기**: 기획자가 만들 때 한 번 짓는다. lowerCamelCase(`strike`, `burningBlood`), 분류가 필요하면 점(`blessing.dawn`).
- **바꾸기**: 상세 창 제목(ID)을 눌러 고친다. 저장하면 이 ID를 가리키는 `[DataRef]` 칸·에셋 파일 이름·`GameIds`가 함께 바뀐다.
- **지우기**: 상세 창 아래 **삭제**. 어딘가에서 가리키면 지워지지 않고 사용처를 보여 준다.

코드에는 ID 문자열 대신 늘 `GameIds` 상수를 쓴다. ID가 바뀌면 상수 이름이 바뀌어 컴파일 오류로 고칠 곳이 드러난다.

> [!note]- 출시 뒤 ID 바꾸기
> 출시 상태(`MomoiProject → 출시됨 (저장 호환 유지)`)를 켠 뒤의 ID 변경은 이력으로 남고, 예전 저장을 불러올 때 저절로 이어진다.
> - **플레이어 데이터·로그·도전과제**: 예전 ID로 저장된 값을 새 ID가 이어받는다.
> - **저장 파일 안에 글자로 들어간 DB 항목 ID**(덱의 `DeckCard.card`, `relics`, `unlocked`, 물약 칸의 `item`): `[DataRef]`가 붙은 칸이면 불러올 때 새 ID로 바뀐다.
> - `[DataRef]`도 없고 필드 규칙(값 출처 "DB 항목")으로 연결하지도 않은 `string` 칸은 바뀌지 않는다. 그래서 판 데이터의 ID 칸에도 빠짐없이 `[DataRef]`를 붙인다(4절).
> - 출시 전(개발 중)에는 이력이 남지 않는다. 대신 구조가 맞지 않는 저장은 지워진다([10장](10-SaveAndRelease.md)).

> [!note]- 지우기 자세히
> 사용처는 최대 10곳까지 보인다. 먼저 그곳들의 선택을 바꾼 뒤 지운다. 지운 항목은 저장할 때 휴지통으로 간다.

## 9. 기획자에게 넘기기

함께 알려 줄 것:

- **저장**: DB에서 바꾼 것은 작업본에 모였다가 **저장  Ctrl+S**에서 한 번에 쓰인다. 저장 전 항목은 플레이에도 `GameIds`에도 없다(플레이를 시작하면 DB가 저장할지 묻는다).
- **검증 오류 찾기**: 사이드바 아래 "검증 오류 n건 · 빌드가 막힙니다"가 보이면 검색 칸에 `invalid`를 넣어 문제 항목만 본다.
- **이름 텍스트**: 플레이어에게 보일 이름은 항목의 칸이 아니라 텍스트 `<ID>.name`이다. `MomoiProject → 다국어 → DB 이름 키 채우기`로 키를 만들고 원문 표에서 이름을 적는다([06장](06-Presentation.md)).
- **시험**: 만든 유물·도전과제가 발동하는지 모니터의 **이벤트 호출**·**트리거로 달성**으로 본다([04장](04-AddingContent.md) 11절, [08장](08-TestAndDebug.md)).
- **바꾸면 안 되는 ID**: 출시 뒤 `[DataRef]` 없이 저장 파일에 글자로 들어가는 ID가 있으면 공유한다(8절).

> [!note]- 다른 장르라면 — 텍스트가 많은 게임
> 대사·텍스트가 많은 게임은 기획자 대신 번역가·작가가 시트(CSV)로 일한다. 텍스트 키를 정하는 규칙(`ui.`·`popup.`·`<ID>.name`)을 이 단계에서 같이 정해 둔다([06장](06-Presentation.md)).

## 10. 체크리스트

- [ ] 프로그래머가 만들 것과 기획자가 채울 것을 표로 적어 공유했다.
- [ ] 고르는 값은 모두 키워드 그룹이고 필드에 `[Keyword]`가 있다(enum 칸이 없다).
- [ ] 게임이 채우는 칸은 `[Hide]`, 조건부 칸은 `[ShowIf]`로 정리했다.
- [ ] 다른 DB 항목을 가리키는 칸(원본·판 데이터 모두)에 `[DataRef]`가 있다.
- [ ] 모든 Block에 `[BlockInfo]` 이름·분류·설명이 있고, 칸이 있는 Block에는 `Sentence`가 있다.
- [ ] 비면 안 되는 칸에 필수, 그림 칸에 스프라이트 규격 그룹을 정했다.
- [ ] DB 종류·Block에 필요한 `ValidateData`·`Validate`를 넣었다.
- [ ] 코드에는 ID 문자열 대신 `GameIds`를 쓴다.
- [ ] DB 사이드바가 "검증 통과"다.

> [!note]- 더 보기(매뉴얼)
> - [21. DB](../Manual/21-Editor-Database.md) — 탭·저장 흐름·ID 바꾸기·삭제·검증 메시지 모음
> - [04. 인스턴스](../Manual/04-Instances.md) — `[Hide]`·`[ShowIf]`·기능 필드
> - [05. 키워드](../Manual/05-Keywords.md) — 그룹·하위 그룹·연결
> - [27. 필드 규칙과 스프라이트 규격](../Manual/27-Field-Rules-Sprites.md) — 필수·맞춤·베이크
> - [11. Trigger와 조건](../Manual/11-Triggers-Conditions.md) · [12. 결과(Outcome)](../Manual/12-Outcomes.md) — `[BlockInfo]`·`Validate`
> - [17. 프로젝트 고유 기능](../Manual/17-Custom-Features.md) — `ValidateData`
