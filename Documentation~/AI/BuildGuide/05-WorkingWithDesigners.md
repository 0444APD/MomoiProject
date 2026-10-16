# 05. 개발자와 함께 일하기

개발자는 코드를 읽지 않는다. DB(`MomoiProject › DB`)·모니터에서 읽고, 값을 바꾸고, 시험한다(관례 18절). AI는 클래스·Block·규칙·DB 항목을 만들 때 그 화면에서 헷갈릴 칸과 잘못 넣을 값을 미리 막는다. 이 장은 그 다듬기다.

> [!note]- 이 장에서 쓰는 말
> | 말 | 뜻 |
> |---|---|
> | 구조 잠금 | 개발자의 DB 창은 값(숫자·고르는 값·참조·데이터 목록의 줄)만 열고, 구조(Block 더하기·빼기·바꾸기, 기능 필드 붙이기·떼기, 로직 쪽 항목의 추가·삭제·ID 바꾸기, 키워드 만들기, `[Hide]` 같은 코드 연결)는 잠근다(`EditorPolicy`). 잠긴 곳에는 "구조(블록·항목 추가·삭제)는 AI에게 요청하세요. 여기서는 값을 바꿉니다."가 보인다. 메뉴 `MomoiProject › 개발 › 구조 편집 열기`는 라이브러리 개발용이다. |
> | 작업본 · 적용 전 | DB에서 고친 내용을 에셋에 쓰기 전에 모아 두는 사본. 저장(Ctrl+S)할 때 한 번에 쓴다. 반영 스크립트의 고치기·지우기·ID 바꾸기도 여기 '적용 전'으로 남는다(새 항목은 저절로 적용). |
> | 검증 | 끊긴 참조·빈 필수 값·잘못된 ID 같은 문제를 찾는 것. 오류가 하나라도 있으면 빌드가 막힌다. AI는 `Logs/MomoiProject/DbStatus.txt`로 읽는다. |

---

## 1. 나눌 일 정하기

- **AI**: 클래스(어떤 칸이 있나)·행동·Block·`[BindTo]` 함수·프로젝트 DB 종류·로직 쪽 DB 항목(반영 스크립트)·키워드·화면·테스트 도구.
- **개발자**: 값(밸런싱 값·DB 칸·인스턴스 필드), 다이얼로그 그래프·레벨 배치·에셋, 플레이어에게 보일 글, 시험. 로직 쪽 항목의 추가·삭제·로직 변경은 말로 요청한다.

> [!note]- 누가 무엇을, 어디서
> | 이런 것 | 누가 | 어디서 |
> |---|---|---|
> | 카드·몬스터·물약의 수치·그림·고르는 값 | 개발자가 바꾼다(항목은 AI가 만든다) | DB › 인스턴스·밸런싱 값 |
> | Block 안의 숫자·참조(문장의 칸) | 개발자가 바꾼다 | 그 항목의 트리거·결과 칸 |
> | 트리거·결과의 조립(어떤 Block을 끼우나) | AI(반영 스크립트) | `Editor/Seeds` |
> | 등급·속성 같은 고르는 값의 목록 | AI(코드가 원본) | 키워드 그룹 코드 — DB › 키워드는 읽기 전용 |
> | 효과 하나·대상 고르기 하나·조건 하나의 동작 | AI | Block 클래스 |
> | Block 조합으로 나타내기 어려운 그 항목만의 결과 | AI | `[BindTo]` 함수 |
> | 데이터 모양(어떤 칸이 있는가)·플레이어 입력 | AI | 클래스·행동 |
> | 플레이어에게 보일 이름·글 | 원문은 명세서·개발자, 키는 AI | 텍스트 표(`<ID>.name`) |
> | 레벨 배치·다이얼로그 그래프 | 개발자(AI는 데이터 모양과 연결만, 요청받으면 예시 하나) | `DeveloperAuthored` DB 종류·씬·그래프 창 |
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

**목록은 코드가 원본이다.** 키워드를 더하고 지우는 것은 AI가 코드에서 한다(개발자가 요청한다). DB › **키워드** 탭은 읽기 전용이고, 개발자는 칸에서 고르기만 한다.

> [!note]- 키워드 자세히
> - 코드에 상수를 더하면 다시 컴파일된 뒤 탭과 칸의 목록에 나온다.
> - **enum이 아닌 이유**: 키워드는 글자 ID로 저장되므로 순서를 바꾸거나 중간에 끼워도 저장 파일이 깨지지 않는다. enum은 숫자로 저장되어 깨진다.
> - 이름을 바꿀 때는 상수 이름만 바꾸고 저장되는 ID(값)는 그대로 둔다. 값을 바꾸면 이미 채운 인스턴스와 저장 파일에 옛 ID가 남는다(검증 오류 "…은(는) 키워드 그룹 …에 없는 키워드입니다").
> - 카드 던전은 등급·속성·카드 종류가 모두 키워드 그룹이다(`Rules/Data/Keywords.cs`). 화상 같은 상태는 중첩·지속이 있으므로 키워드가 아니라 DB › 상태 효과의 항목이다.

## 3. 인스턴스 화면 정리

- 개발자가 볼 필요 없는 칸 → `[Hide]`.
- 다른 칸에 따라 의미가 생기는 칸 → `[ShowIf]`.
- 있을 수도 없을 수도 있는 기능 → `[SerializeReference]` 기능 필드.
- 칸 이름만으로 뜻이 모자라면 → `[FieldLabel("한글")]`(이름은 `NamingGuide.md`).

카드 원본 `Card`의 일부:

```csharp
public bool upgradable;
[ShowIf(nameof(upgradable)), DataRef(typeof(Card))] public string upgradeTo;   // 강화 가능할 때만 보인다
[Hide] public int balanceScore;                                                // 밸런스 도구가 채우는 값
[SerializeReference] public Usable usable;                                     // 기능 필드
```

> [!note]- 칸을 어떻게 보이게 하나
> | 이럴 때 | 이것 |
> |---|---|
> | 원본에 있지만 개발자가 볼 필요 없는 칸(도구가 채우는 값) | `[Hide]`. 게임 중에 바뀌는 값은 원본이 아니라 판 데이터 클래스(`DeckCard` 같은)에 둔다 |
> | 다른 칸이 켜졌거나 어떤 값일 때만 의미 있는 칸 | `[ShowIf(nameof(필드))]` · `[ShowIf(nameof(필드), 값)]` |
> | 있을 수도 없을 수도 있는 기능(사용 기능, 장비 기능) | `[SerializeReference]` 기능 필드. 붙이고 떼는 것은 구조라 AI가 반영 스크립트로 한다 |
> | 정해진 목록에서 고르는 글자 | 키워드(`[Keyword(typeof(그룹))]`, 2절) |
> | 다른 DB 항목의 ID | `[DataRef(typeof(종류))]`(4절) |
> | 비면 안 되는 칸 | 필드 규칙의 **필수**(6절) |
> | 칸 이름만으로 뜻이 모자람 | `[FieldLabel("설명")]`. DB 칸의 이름표는 변수 이름 그대로이고, 한글 이름은 마우스를 올리면 보인다 |

> [!note]- 원본과 판 데이터를 나눈 이유
> 카드 던전은 카드를 두 클래스로 나눈다. DB › 인스턴스의 **원본** `Card`(비용·등급·그림·사용 기능)와, 판 데이터의 덱에 드는 **한 장** `DeckCard`(장마다 다른 `id`, 원본 ID `card`, 강화 여부 `upgraded`)다. 개발자의 DB 화면에는 원본만 보이므로 게임이 채우는 칸이 처음부터 섞이지 않는다.

> [!note]- `[Hide]`·`[ShowIf]`·기능 필드 자세히
> - 기능 필드가 비어 있으면 인스턴스 화면에 그 기능이 없다고 보이고, 붙어 있으면 트리거·결과 칸이 열린다(Block 조립은 잠겨 있고 안의 값만 바뀐다).
> - `[Hide]`·`[ShowIf]`는 인스턴스 편집 화면에만 적용된다. 모니터는 모든 칸을 보여 준다.
> - `[Hide]`는 AI가 코드에 쓴다. DB › 클래스의 숨김 토글은 코드를 고치는 연결이라 구조 편집에서만 열린다.

> [!note]- 다른 장르라면 — 한 아이템에 기능이 여럿
> RPG처럼 아이템 종류가 많고 한 아이템이 장비이면서 사용 가능하면, 종류별 상속 대신 한 아이템 클래스에 기능 필드를 여럿 둔다(`[SerializeReference] public Equip equip;`, `[SerializeReference] public Usable usable;`). 아이템마다 어느 기능을 붙일지는 반영 스크립트가 정한다.

## 4. 다른 항목을 가리키는 칸은 `[DataRef]`

다른 DB 항목의 ID를 담는 `string`(또는 `List<string>`) 칸에는 모두 `[DataRef(typeof(종류))]`를 붙인다. 원본 클래스와 판 데이터 클래스 둘 다다.

| 칸 | 붙인 것 |
|---|---|
| `Card.upgradeTo`, `DeckCard.card`, `Profile.unlocked` | `[DataRef(typeof(Card))]` — 값이 `Card`인 인스턴스만 목록에 뜨고, 다른 클래스를 고르면 검증 오류 |
| `Enemy.monster` | `[DataRef(typeof(MonsterData))]` |
| 클래스를 가리지 않는 칸(인벤토리 슬롯의 아이템 등) | `[DataRef(typeof(InstanceData))]` — 모든 인스턴스 |
| `RunState.relics` | `[DataRef(typeof(PassiveData))]`(유물은 패시브 효과, 04장 8절) |
| 비우는 것이 뜻이 있는 칸(적 편성의 빈 칸, 비우면 끄는 표) | `[DataRef(typeof(…), Optional = true)]` — 비어도 경고하지 않는다. 꼭 골라야 하는 칸만 경고로 남긴다 |

> [!note]- 붙이면 무엇이 달라지나
> - 칸이 항목 목록 드롭다운이 된다. 모니터에서도 목록으로 고른다.
> - 검증이 없는 ID를 찾아낸다("…: 없는 … '…'를 가리킵니다").
> - DB 창에서 가리키는 항목을 지우려 하면 **삭제할 수 없습니다** 창에 사용처를 보여 준다.
> - ID를 바꾸면 이 칸의 값도 따라 바뀐다. 출시 뒤에는 저장 파일 안의 옛 ID도 불러올 때 새 ID로 바뀐다(8절).
>
> 그냥 `string`이면 개발자가 ID를 손으로 적어야 하고, 위의 어느 것도 되지 않는다.

> [!note]- 다른 종류를 가리키는 어트리뷰트
> | 가리키는 것 | 어트리뷰트 |
> |---|---|
> | 다른 DB 항목(인스턴스·유물 …), 규격을 붙인 종류(`typeof(IStatusData)`) | `[DataRef(typeof(종류))]` |
> | 플레이어 데이터 | `[PlayerDataRef]` |
> | 로그 | `[StatRef]` |
> | 도전과제 | `[AchievementRef]` |
> | 이벤트 형식 | `[EventTypeRef]` |

> [!note]- 다른 장르라면 — 서로 가리키는 데이터
> 턴제 전술처럼 유닛·스테이지·웨이브가 서로를 가리키면 `StageData` 같은 DB 종류를 만들고 `[DataRef(typeof(InstanceData))] List<string> mobs`로 잇는다. 스테이지를 개발자가 배치하면 그 종류에 `DeveloperAuthored = true`(02장 7절). 스테이지가 쓰는 몬스터는 DB 창에서 지울 수 없게 되고, 몬스터 ID를 바꾸면 스테이지 값도 따라 바뀐다.

## 5. Block에 이름표 달기

모든 Block에 `[BlockInfo]`로 표시 이름·분류(`Category`)·설명(`Description`)을 준다. 칸이 있는 Block에는 한 줄 문장(`Sentence`)도 준다. 행동·이벤트·행동 직전 이벤트도 한글 표시 이름을 준다(관례 2절, 이름은 `NamingGuide.md`).

```csharp
[Serializable, BlockInfo("피해", Category = "카드", Description = "대상에게 피해를 줍니다(방어도 먼저).", Sentence = "{amount} 피해")]
public class DealDamage : Effect<Fighter> { … }

[Serializable, BlockInfo("무작위 적", Category = "대상", Description = "살아 있는 적 중에서 무작위로 고릅니다(고른 대상은 버립니다).", Sentence = "무작위 적 {count}명")]
public class RandomEnemy : TargetSelector<Fighter> { … }
```

이제 개발자는 DB에서 "6 피해"를 문장으로 읽고 6만 눌러 고친다.

> [!note]- 화면에 어떻게 보이나
> - `Sentence`의 `{필드이름}` 자리가 눌러서 바꾸는 칸이 된다. `Sentence`가 없으면 Block 이름 아래에 칸이 나열된 상자로 보인다.
> - Block 목록(구조 편집·참조 문서)의 이름은 `[BlockInfo]`의 표시 이름, 묶음은 `Category`, 이름 옆 설명은 `Description`이다. `Description`은 꼭 적는다.
> - `[BindTo]` 함수는 DB에서 이름·설명만 보이므로 그 안의 숫자는 항목 칸·밸런싱 값에서 읽는다(04장 2절).

## 6. 필드 규칙: 필수와 스프라이트 틀

필드마다 "필수"·"스프라이트 틀"·"미리보기 크기"를 정한다. 코드 어트리뷰트가 아니라 DB에 저장되는 **필드 규칙**(DB 항목 `FieldRuleData` — 칸 `targetType`·`field`·`required`·`spriteFrame`·`previewSize`, 매뉴얼 [27장](../../Manual/27-Field-Rules-Sprites.md))이다.

1. **필수**: AI가 정한다 — `Card.art`처럼 비면 안 되는 칸. DB › 클래스의 필드 줄에서 정하는 것과 같은 항목이라 반영 스크립트로 만들 수 있다.
2. **스프라이트 틀 파일**: `Assets/Dev` 아래 Project 창 Create › MomoiProject › 스프라이트 틀(예: 틀 `card`, 크기 표 `cardArt` 300×420 · `cardIcon` 64×64). 개발자 폴더의 파일이라 개발자가 만든다 — AI는 필요한 틀과 크기를 알린다.
3. **연결**: 그 틀을 `art`의 스프라이트 틀로, `cardIcon`을 미리보기 크기로 정한다(보통 AI).

이제 개발자가 카드 그림을 넣으면 맞춤 창이 열리고, 중심·배율을 한 번 정하면 틀의 모든 크기가 함께 구워진다. 화면 코드는 원본 그림과 크기 이름으로 구운 이미지를 꺼낸다.

```csharp
var icon = SpriteSpecs.Get(Rules.CardData(game, deckCard.card).art, GameIds.SpriteSize.Card.CardIcon);
```

> [!note]- 자세히
> - 그림을 빠뜨린 카드는 검증 오류("필수 값 '…'(…)이 비어 있습니다.")로 빌드가 막힌다. 숫자 0은 비었다고 보지 않으므로 `cost` 같은 숫자 칸에는 필수가 효과가 없다.
> - `SpriteSpecs.Get`은 굽기 전에는 원본을 돌려주므로 화면이 깨지지 않는다.
> - `MonsterData.portrait`·패시브 효과(유물)의 `icon`도 같은 방식으로 틀을 정해 두면, 개발자가 크기를 맞추느라 그림 파일을 따로 자를 일이 없다.
> - 그림이 아직 없으면 반영 스크립트에서 `AssetRequests.Sprite(경로, 너비, 높이, 쓰는 곳, 느낌)`로 임시 그림과 넣을 것 목록을 만든다(관례 18절, 매뉴얼 39장).
> - 틀의 크기 표·굽기는 저장 없이 바로 에셋에 쓰인다.

## 7. 고유 검사 넣기

"이 값이면 안 된다"를 검증에 더한다. 오류는 빌드를 막는다.

- **직접 만든 DB 종류**(04장 8절의 `RelicData` 같은 것): `ValidateData`를 덮어쓴다.
- **Block**: `Validate`를 덮어쓴다.

```csharp
// RelicData(04장 8절)
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
> - `context.Error`는 빌드를 막고, `context.Warning`은 표시만 한다. 메시지에 필드 이름이 들어 있으면 상세 창에서 그 칸도 빨갛게 보인다. 문장은 개발자가 읽고 스스로 고칠 수 있게 쓴다.
> - `ValidateData`에서는 안쪽 Trigger·Outcome의 검사도 이어서 부른다(위 코드의 `trigger.Validate`·`outcome.Validate`).
> - `Validate`를 덮어쓸 수 있는 Block: 조건 Block(`SimpleCondition`·`Condition`), 코딩한 결과(`Reward`), 효과 Block(`Effect<T>`), 대상 Block(`TargetSelector<T>`). 효과·대상 Block의 검사는 그 Block을 담은 '대상에게 효과' 결과의 검증이 불러 준다.
> - 실행 중에 걸러 내는 것보다 DB에서 값을 바꾸는 순간 빨간 글씨로 보이는 편이 개발자에게 빠르다. AI는 같은 결과를 `DbStatus.txt`로 읽는다.
> - 인스턴스 값 자체(카드·몬스터 클래스)에는 형식 검사 외에 고유 검사를 넣을 곳이 없다. 필수·키워드·`[DataRef]`로 막을 수 있는 만큼 막고, 나머지는 [08장](08-TestAndDebug.md)의 EditMode 테스트로 확인한다.

## 8. ID 바꾸기와 지우기

- **짓기**: AI가 항목을 만들 때 한 번 짓는다(관례 2절). lowerCamelCase(`strike`, `burningBlood`), 분류가 필요하면 점(`blessing.dawn`). 영문·숫자만 쓰고 밑줄·띄어쓰기는 없다.
- **바꾸기**: 로직 쪽 항목의 ID는 개발자 DB에서 잠겨 있다(`DeveloperAuthored` 종류만 열린다). AI는 버전을 올린 반영 스크립트에서 `EditorDatabase.Staging.Rename(항목, 새 ID)` — 이 ID를 가리키는 `[DataRef]` 칸이 함께 바뀌고 '적용 전'으로 남는다. 개발자가 Ctrl+S로 적용하면 에셋 파일 이름·`GameIds`가 바뀌므로, 그 상수를 쓰는 코드도 새 이름으로 고친다.
- **지우기**: 버전을 올린 반영 스크립트에서 `EditorDatabase.Staging.Remove(항목)` — '적용 전'으로 남는다. 반영 스크립트의 `Remove`는 사용처를 막지 않으므로 가리키는 칸과 그 ID를 쓰는 코드(`GameIds` 상수)를 먼저 고친다(남으면 검증 오류 "없는 … 를 가리킵니다").

코드에는 ID 문자열 대신 늘 `GameIds` 상수를 쓴다. ID가 바뀌면 상수 이름이 바뀌어 컴파일 오류로 고칠 곳이 드러난다.

> [!note]- 출시 뒤 ID 바꾸기
> 출시 상태(`MomoiProject › 개발 › 출시됨 (저장 호환 유지)`)를 켠 뒤의 ID 변경은 이력으로 남고, 예전 저장을 불러올 때 저절로 이어진다.
> - **플레이어 데이터·로그·도전과제**: 예전 ID로 저장된 값을 새 ID가 이어받는다.
> - **저장 파일 안에 글자로 들어간 DB 항목 ID**(덱의 `DeckCard.card`, `relics`, `unlocked`, 물약 슬롯의 `item`): `[DataRef]`가 붙은 칸이면 불러올 때 새 ID로 바뀐다.
> - `[DataRef]`도 없고 필드 규칙(값 출처 "DB 항목")으로 연결하지도 않은 `string` 칸은 바뀌지 않는다. 그래서 판 데이터의 ID 칸에도 빠짐없이 `[DataRef]`를 붙인다(4절).
> - 출시 전(개발 중)에는 이력이 남지 않는다. 대신 구조가 맞지 않는 저장은 지워진다([10장](10-SaveAndRelease.md)).

> [!note]- 지우기 자세히
> DB 창에서 지울 때(개발자가 만드는 종류, 구조 편집) 어딘가에서 가리키면 지워지지 않고 **삭제할 수 없습니다** 창에 사용처를 최대 10곳 보여 준다. 지운 항목은 적용(Ctrl+S)할 때 휴지통으로 간다.

## 9. 개발자에게 넘기기

개발자에게 함께 알려 줄 것(진행 매뉴얼 13절의 '알릴 것'):

- **적용**: 반영 스크립트의 새 항목은 저절로 적용되지만, 이미 있는 항목의 고치기·지우기·ID 바꾸기는 '적용 전'으로 남는다. 무엇을 바꿨는지와 Ctrl+S가 필요하다는 것을 알린다. 개발자가 DB에서 바꾼 값도 작업본에 모였다가 **저장 Ctrl+S**에서 한 번에 쓰인다(플레이를 시작하면 DB가 저장할지 묻는다).
- **검증 오류 찾기**: 사이드바 아래 "검증 오류 n건 · 빌드가 막힙니다"가 보이면 검색 칸에 `invalid`를 넣어 문제 항목만 본다.
- **이름 텍스트**: 플레이어에게 보일 이름은 항목의 칸이 아니라 텍스트 `<ID>.name`이다. AI가 반영 스크립트에서 `TextSheetTools.FillCodexNames(TextSheetTools.EnsureSettings())`로 키를 만들고(값은 ID), 원문은 명세서·개발자의 답으로 채운다. 개발자는 텍스트 표에서 고친다([06장](06-Presentation.md) 5절).
- **시험**: 테스트 명령·이벤트 호출·트리거로 달성으로 보는 법([04장](04-AddingContent.md) 11절, [08장](08-TestAndDebug.md)).
- **바꾸면 안 되는 ID**: 출시 뒤 `[DataRef]` 없이 저장 파일에 글자로 들어가는 ID가 있으면 알린다(8절).
- **바꿀 수 없는 곳**: Block 조립·항목 추가·삭제는 잠겨 있으니 말로 요청하면 된다는 것.

> [!note]- 다른 장르라면 — 텍스트가 많은 게임
> 대사·텍스트가 많은 게임은 번역가·작가가 시트로 일한다(메뉴 `MomoiProject › 다국어 › 시트 내보내기 · 가져오기 · 번역 검사`). 텍스트 키를 정하는 규칙(`hud.`·`popup.`·`fail.`·`<ID>.name` — 관례 2절)을 이 단계에서 같이 정해 둔다([06장](06-Presentation.md)).

## 10. 체크리스트

- [ ] AI가 만들 것과 개발자가 바꿀 것을 나눠 알렸다.
- [ ] 고르는 값은 모두 키워드 그룹이고 필드에 `[Keyword]`가 있다(enum 칸이 없다).
- [ ] 게임이 채우는 칸은 `[Hide]`, 조건부 칸은 `[ShowIf]`, 뜻이 모자란 칸은 `[FieldLabel]`로 정리했다.
- [ ] 다른 DB 항목을 가리키는 칸(원본·판 데이터 모두)에 `[DataRef]`가 있고, 비워도 되는 칸만 `Optional = true`다.
- [ ] 모든 Block에 `[BlockInfo]` 이름·분류·설명이 있고, 칸이 있는 Block에는 `Sentence`가 있다.
- [ ] 비면 안 되는 칸에 필수, 그림 칸에 스프라이트 틀을 정했다(틀 파일은 개발자에게 요청).
- [ ] DB 종류·Block에 필요한 `ValidateData`·`Validate`를 넣었다.
- [ ] 코드에는 ID 문자열 대신 `GameIds`를 쓰고, ID 바꾸기·지우기는 반영 스크립트(`Rename`·`Remove`)로 했다.
- [ ] `DbStatus.txt`가 "검증 통과"이고, '적용 전' 항목은 Ctrl+S를 요청했다.

> [!note]- 더 보기(매뉴얼)
> - [21. DB](../../Manual/21-Editor-Database.md) — 탭·저장 흐름·잠금(3-1)·ID 바꾸기·삭제·검증 메시지 모음
> - [04. 인스턴스](../../Manual/04-Instances.md) — `[Hide]`·`[ShowIf]`·기능 필드
> - [05. 키워드](../../Manual/05-Keywords.md) — 그룹·하위 그룹·연결
> - [27. 필드 규칙과 스프라이트 틀](../../Manual/27-Field-Rules-Sprites.md) — 필수·맞춤·굽기
> - [11. Trigger와 조건](../../Manual/11-Triggers-Conditions.md) · [12. 결과(Outcome)](../../Manual/12-Outcomes.md) — `[BlockInfo]`·`Validate`
> - [17. 프로젝트 고유 기능](../../Manual/17-Custom-Features.md) — `ValidateData`
> - [39. 에셋](../../Manual/39-Assets.md) — 넣을 에셋 자리
