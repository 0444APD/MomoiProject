# 16. 능력과 사용 기능

**한 줄 요약**: 능력은 "쓰는 순간" 발동하는 기능(스킬, 아이템 사용 효과, 버프 발동)이다. Trigger = **사용**(+ 원하면 AND로 사용 조건), 결과 = 대상에게 효과 · 플레이어 데이터에 주기 · 코딩한 결과. 게임 코드는 `Game.Use(ID, 사용자, 대상들)` 한 줄로 쓰고(되는 것만 쓰려면 `Game.TryUse`), 그 전체가 행동 하나라 Undo 한 번에 되돌아간다. **사용 기능**(`Usable`)은 같은 것을 아이템 같은 인스턴스에 필드로 붙이는 형태다.

> **선택 요소**: 능력의 DB 종류(데이터 칸 클래스)는 GBible에 없다 — 쓰는 프로젝트가 템플릿 `Documentation~/AI/Templates/Elements/AbilityData.cs.txt`를 `Assets/MomoiProject/Code/Rules/Elements/AbilityData.cs`로 들인다(AI가 단계 1에서). 칸이 더 필요하면 그 클래스에 더한다(프로젝트 변경). 엔진(이 장의 동작·기본 Block·`Game` 함수)은 GBible에 있고, 이 장의 칸 설명은 템플릿 그대로일 때다. 들이지 않은 프로젝트에는 탭·기본 Block이 보이지 않는다(AI 문서 Reference 4-1).

**언제 쓰는가**

- 플레이어가 고른 대상에게 스킬을 쓸 때: "파이어볼(고른 적 + 주변 1칸에 10 피해)".
- 아이템을 쓸 때: "포션(사용자 체력 30 회복)".
- 쓸 수 있는지 미리 보여 줄 때: 버튼을 흐리게 하고 이유를 툴팁으로(`Game.CanUse`).
- 조각(카드·조커·유닛)을 차례로 돌며 쓸 수 있는 것만 쓸 때: 실패를 던지지 않는 `Game.TryUse`(2-10).
- "두 번 발동", "봉인·침묵"처럼 쓰는 것 자체를 고칠 때: 기본 행동 직전 이벤트 `사용`을 받아 고친다(2-11).
- "이 카드의 수치 2배"처럼 결과의 수치를 한꺼번에 곱할 때: 기본 행동 직전 이벤트 `효과 배율`을 받아 고친다(2-12).

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| Trigger | 언제 발동하는지(→ `11-Triggers-Conditions.md`). 능력은 `사용` 잎이 기본이다. |
| Outcome | 무엇을 하는지(→ `12-Outcomes.md`). |
| Block | 에디터에서 조립하는 로직 단위. 능력에서는 대상 Block(`TargetSelector`)·효과 Block(`Effect`)·조건 Block(`Condition`)을 주로 쓴다. |
| 사용자 | 능력을 쓰는 개체(`user`). |
| 고른 대상 | 플레이어가 고른 대상들(`targets`, 컨텍스트에서는 `Picked`). |
| 행동 (`GameAction`) | 플레이어 입력 하나. 데이터라서 서버 전송·리플레이가 되고, 실행 한 번이 Undo 한 단위다. |
| TriggerContext (컨텍스트) | 사용하는 순간의 정보(`IsUse = true`, `User`, `Picked`, `Owner` = 능력 ID). |
| 인스턴스 | DB › 인스턴스에서 만든 클래스 객체 원본(아이템 A …). |
| 행동 직전 이벤트 `사용` (`UseIntent`) | 능력이 결과를 내기 직전의 내용(`ability` · `userId` · `times` · `cancelled`). 행동 직전 이벤트가 횟수를 바꾸거나 막는다(→ `28-Attributes-Intercepts.md` 4-14). |
| 행동 직전 이벤트 `효과 배율` (`PowerIntent`) | 이번 사용의 수치 배율(`ownerId` · `userId` · `multiplier`). 돌아온 값이 컨텍스트의 `Power`가 되고 기본 효과 Block이 곱한다(2-12). |

---

## 1. 에디터에서 쓰는 법

### 1-1. 능력 만들기(DB › 능력)

1. `MomoiProject → DB` → 사이드바 **DB** 그룹의 **능력** 탭 → 목록 줄의 **`+ 능력`**. 제목(= ID)을 짓는다(예: `fireball`).
2. `trigger`(툴팁 "트리거"): 기본으로 `사용` 하나가 들어 있다(요약 "사용할 때").
   - 사용 조건을 붙이려면 `+ 블럭` → **조건 충족** 그룹의 조건 Block(예: 프로젝트가 만든 `사용자 체력 있음`, 기본 제공 `상태 효과 검사` — 쿨다운 3-12)을 더한다. `사용`과 `AND`로 이어진다.
   - 이 칸에서 고를 수 있는 것은 `사용`과 조건 충족뿐이다.
3. `outcomes`(툴팁 "결과"): `+ 결과 추가` →
   - `대상에게 효과` → `+ 대상 추가`(비우면 고른 대상 그대로) → `+ 효과 추가`. 기본 제공 대상 Block(`사용자 자신`, 그리고 `플레이어 데이터 목록`·`이벤트의 개체`·`조건으로 거르기`·`상태 효과로 거르기`·`무작위 n개`·`정렬해서 n개`, 2-6)으로 "체력이 가장 낮은 적", "불 속성 적 중 무작위 2"를 코드 없이 고른다(3-11). 기본 제공 효과 `능력치 보정`·`능력치 보정 떼기`(분류 "능력치")로 버프를 코드 없이 만든다(3-10). 독·보호막처럼 몇 턴 동안 이어지는 효과와 쿨다운은 `상태 효과 걸기`·`상태 효과 풀기`·`상태 효과 줄이기`(분류 "상태 효과", → `29-Statuses-Schedules-Choices.md`, 쿨다운은 3-12).
   - `선택 요구` → 후보 중에서 플레이어가 고르게 하고, 고른 것마다 효과를 준다(→ `29-Statuses-Schedules-Choices.md` 3-3).
   - `플레이어 데이터에 주기 · …` → 필드와 값.
   - 코딩한 결과(`Reward` 클래스).
4. `failText`(툴팁 "쓸 수 없을 때 글 / 사용 조건이 맞지 않을 때 플레이어에게 보일 글. 텍스트 키(fail.needEnergy)면 번역된 글, 아니면 그 글 그대로({ability}·{condition} 틀). 비우면 기본 글"): 사용 조건이 거짓일 때 `Game.Use`의 실패 글이자 `Game.CanUse`의 이유(2-1·2-3). 예: `마나가 모자라 {ability}을(를) 쓸 수 없습니다.` 또는 텍스트 키 `fail.needMana`. 비우면 기본 글 "{ability}: 사용 조건을 만족하지 않습니다({condition})."이다.
5. `icon`(툴팁 "아이콘"): 스킬 버튼에 그릴 `Sprite`. 규칙은 쓰지 않고 화면 코드가 읽는다(3-7).
6. 검증 경고: "트리거에 '사용'이 없습니다. 쓰는 순간과 상관없이 조건만으로 쓸 수 있는지가 정해집니다." / "결과가 없습니다. '+ 결과 추가'로 무엇을 할지 더하세요."

결과를 코드로 쓰려면 `[BindTo("fireball")]`(→ `13-BindTo.md`). 연결되면 결과 칸이 잠긴다.

### 1-2. 인스턴스에 사용 기능 붙이기(DB › 인스턴스)

먼저 코드에서 아이템 클래스에 사용 기능 필드를 둔다(2-8).

1. `MomoiProject → DB` → **인스턴스** 탭 → **`+ 인스턴스`** → 제목(= 인스턴스 ID, 예: `potion`)을 짓는다.
2. `instanceType`(툴팁 "형식")에서 아이템 클래스(`Item`)를 고른다. 값(`value`)이 새로 만들어진다.
3. 값 탐색기 아래 **`+ 기능`**을 누르고 목록에서 `usable`(오른쪽 작은 글씨: 형식 이름)을 고른다. 사용 기능이 붙고 그 칸이 보인다. 줄의 빼기로 다시 뗀다.
4. 사용 기능의 트리거(`when`)는 기본 `사용`이다. `failText`(툴팁 "쓸 수 없을 때 글(텍스트 키 또는 글, 비우면 기본 글)")는 능력의 `failText`와 같다(1-1).

> 인스턴스 값 안의 사용 기능(`when`·`then`)은 능력 탭과 같은 트리거·결과 편집기로 그려진다. 결과를 코드로 쓰려면 `[BindTo(인스턴스 ID)]`를 붙인다(그러면 결과 칸은 잠기고 연결된 함수 이름·cs 열기가 보인다). 여러 아이템이 같은 효과를 나눠 쓰면 아이템이 **능력 ID를 가리키게** 하고 그 능력을 DB › 능력에서 조립해도 된다(3-5, 3-6).

### 1-3. 모니터에서 확인하기

능력 사용은 행동이라 모니터 **행동** 탭에 "능력 사용: fireball (hero)"처럼 남고, 거기서 Undo할 수 있다. **타임라인**에서 그 행동을 펼치면 효과로 바뀐 값과 `능력 사용` 이벤트가 이어서 보인다.

---

## 2. 코드로 쓰는 법 — 문법 요소를 하나씩

### 2-1. `Game.Use(능력 ID, 사용자, 대상들…)`

```csharp
GameApi Game => GBibleRuntime.Game;

void OnClickSkill(Unit caster, Unit target) => Game.Use("fireball", caster, target);
void OnClickPotion(Unit hero)               => Game.Use("potion", hero);              // 대상 없이
void OnClickById()                          => Game.Use("fireball", "hero", "orc1", "orc2");   // id 문자열도 된다
```

| 인자 | 뜻 |
|---|---|
| `ability` | 능력 ID 또는 사용 기능이 붙은 인스턴스 ID |
| `user` | 사용자 객체 또는 id 문자열. 객체면 `id` 필드가 있어야 한다 |
| `targets` (`params object[]`) | 고른 대상들(객체 또는 id 문자열) |

- 돌려주는 값은 행동 기록(`ActionRecord`)이다.
- 내부에서 `UseAbility` 행동 하나를 `Game.Do`한다. 다른 행동 안에서 부르면 그 행동의 일부로 그 자리에서 실행된다(Undo 한 단위 = 바깥 행동).
- 실패하면 예외를 던지고 **아무것도 바뀌지 않는다**:
  - 없는 ID: `InvalidOperationException` "[MomoiProject] 능력 '…'이(가) 없습니다."(프로그래머 실수)
  - 사용 조건 거짓: 플레이어용 실패 `GameFailure`(`InvalidOperationException`을 상속). 값은 `{ability}`(이름: 텍스트 표의 `<ID>.name`) · `{condition}`(트리거 문장, DB의 한국어 요약).
    - 그 능력·아이템에 **쓸 수 없을 때 글**(`failText`, 1-1)이 있으면 그 글이 키이자 기본 문장이다. 텍스트 표에 그 키가 있으면 지금 언어의 번역, 없으면 적은 글을 틀로 쓴다.
    - 비어 있으면 키 `FailureKeys.CannotUse`(`momoi.fail.cannotUse`), 기본 문장 "{ability}: 사용 조건을 만족하지 않습니다({condition})." 텍스트 표에 키를 넣으면 번역된다(`30-Balance-Tables.md` 2-4). `{condition}`은 번역되지 않으므로 플레이어에게 보일 글은 능력·아이템마다 `failText`로 정한다.
- 행동 직전 이벤트 `사용`이 막으면(봉인·침묵) 실패가 아니다. 예외 없이 아무 일도 하지 않고 끝난다(2-11).
- 사용 조건이 안 맞을 때 실패 대신 거짓을 받으려면 `Game.TryUse`(2-10).

### 2-2. 실행 흐름

1. 행동 시작(Undo 단위 시작).
2. 사용자·대상 id를 객체로 찾는다(플레이어 데이터 전체에서 `id`가 같은 객체).
3. **Trigger 검사**: 사용하는 순간(`IsUse = true`, `User`, `Picked`, `Owner` = 능력 ID)으로 검사한다. 거짓이면 예외 → 행동 중단(`Game.TryUse`면 예외 없이 끝난다). 조건 `확률`은 여기서 한 번 굴린다.
4. **행동 직전 이벤트 `사용`**: `UseIntent { ability, userId, times = 1 }`를 행동 직전 이벤트에 통과시킨다(2-11). 막혔거나 `times`가 0 이하이면 여기서 조용히 끝난다. 결과도 `능력 사용` 이벤트도 없다.
5. **행동 직전 이벤트 `효과 배율`**: `PowerIntent { ownerId = 능력 ID, userId, multiplier = 1 }`를 한 번 통과시켜 돌아온 `multiplier`를 컨텍스트의 `Power`에 둔다(2-12).
6. **결과를 차례로**, `times`번 되풀이한다:
   - 대상에게 효과: 대상 Block을 위에서부터(앞 결과가 다음 입력, 비면 고른 대상) → 최종 대상마다 효과 Block.
   - 플레이어 데이터에 주기 · 코딩한 결과: 같은 컨텍스트로 실행.
7. **`AbilityUsed` 이벤트**(표시 이름 "능력 사용")를 쏜다: `Ability`(ID), `User`(id), `Targets`(효과를 받은 최종 대상들의 id, 모든 횟수를 합쳐 중복 없이), `TargetCount`, `Times`(결과를 낸 횟수 — 행동 직전 이벤트 `사용`이 바꾼 값, 기본 1).
8. 행동 끝 → 바뀐 값 기록, 조건 다시 평가(도전과제 등).

### 2-3. `Game.CanUse` — 쓸 수 있는지만 보기

```csharp
bool ok = Game.CanUse("fireball", caster, target);                     // 대상까지
if (!Game.CanUse("potion", hero, out var why)) potionButton.tooltip = why;   // 이유
bool ok2 = Game.CanUse("fireball", caster, new object[] { target }, out var reason);
```

- Trigger(사용 조건)만 검사하고 아무것도 바꾸지 않는다. 버튼을 미리 흐리게 할 때 쓴다.
- 미리 보기라 조건 `확률`을 굴리지 않고 참으로 본다(난수를 쓰지 않는다). 그래서 `CanUse`가 참이어도 실제로 쓸 때 확률이 빗나갈 수 있다(2-10).
- 행동 직전 이벤트 `사용`의 행동 직전 이벤트(봉인·침묵)도 보지 않는다. 봉인 중인지 버튼에 보이려면 그 상태를 사용 조건(`상태 효과 검사` 등)으로도 둔다.
- 이유 글자: 없는 ID면 "'…'은(는) 쓸 수 있는 능력·아이템이 아닙니다.", 조건이 거짓이면 `Game.Use`가 던지는 실패와 같은 글이다(지금 언어). 쓸 수 없을 때 글(`failText`)이 있으면 그 글, 비었으면 `FailureKeys.CannotUse`의 글(기본 문장 "{ability}: 사용 조건을 만족하지 않습니다({condition}).")이다(2-1). 텍스트 표에서 번역한 글이 툴팁에도 그대로 보인다.

### 2-4. `UseAbility` — 능력 사용 행동(데이터)

```csharp
Game.Do(new UseAbility { ability = "fireball", user = "hero", targets = { "orc1", "orc2" } });   // Game.Use와 같다
```

| 필드 | 뜻 |
|---|---|
| `ability` | 능력 ID |
| `user` | 사용자 id |
| `targets` (`List<string>`) | 고른 대상 id들 |
| `quiet` | 참이면 사용 조건이 안 맞을 때 실패를 던지지 않고 끝난다(`Game.TryUse`가 켠다) |

객체가 아니라 id만 담은 데이터라서, 클라이언트는 이 행동을 서버에 보내고 서버는 같은 행동을 실행하면 된다(리플레이도 같은 데이터로). 효과 Block의 난수는 `Game.Random`을 쓰므로 같은 시드·같은 행동이면 같은 결과가 나오고, Undo하면 난수도 되돌아간다.

### 2-5. `Effect<T>`·`TargetSelector<T>`·`SelfTarget`

```csharp
[Serializable, BlockInfo("피해", Category = "전투", Sentence = "{amount} 피해")]
public class Damage : Effect<Unit>
{
    public int amount = 10;
    public int spread;                                                       // 0 ~ spread 추가 피해
    protected override void Apply(Unit target) => target.hp -= amount + Game.Random("damage").Range(0, spread + 1);
}

[Serializable, BlockInfo("주변 n칸의 적 추가", Category = "대상", Sentence = "주변 {range}칸의 적 추가")]
public class AddEnemiesAround : TargetSelector<Unit>
{
    public int range = 1;
    protected override IEnumerable<Unit> Select(IEnumerable<Unit> targets, Unit user)
    {
        var list = targets.ToList();
        return list.Concat(list.SelectMany(t => Game.All<Unit>().Where(u => u != t && Distance(t, u) <= range)))
                   .Where(u => user == null || u.team != user.team).Distinct();
    }
    static int Distance(Unit a, Unit b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
}
```

| 효과 Block(`Effect<T>`)에서 | 뜻 |
|---|---|
| `Apply(T target)` | 대상 하나에 할 일(대상이 `T`가 아니면 불리지 않는다) |
| `User<TUser>()` | 사용자를 형식으로. 예: `var caster = User<Unit>();` |
| `Use.Ability` | 실행 중인 능력 정의(`AbilityDefinition`: `Id`, `DisplayName` …) |
| `Use.User` / `Use.Picked` / `Use.Context` | 사용자 / 고른 대상 / 컨텍스트 |
| `Game` | `GameApi` |

| 대상 Block(`TargetSelector<T>`)에서 | 뜻 |
|---|---|
| `Select(IEnumerable<T> targets, T user)` | 앞 대상들과 사용자를 받아 다음 대상들을 돌려준다(고르기만) |
| `Game`, `Use` | 효과 Block과 같다 |

기본 제공 `SelfTarget`(`사용자 자신`): 고른 대상을 버리고 사용자 하나를 낸다. 장르 공통의 나머지 기본 대상 Block은 2-6.

기본 제공 효과 Block(→ `28-Attributes-Intercepts.md` 3-1·3-2, `29-Statuses-Schedules-Choices.md` 3-2):

| 효과 | 코드 형식 | 하는 일 |
|---|---|---|
| 능력치 보정 | `AttributeModifierEffect` | 대상의 숫자 필드에 Modifier를 붙인다(`attribute`·`kind`·`value`·`permanent`). 출처 = 이 능력 ID(인스턴스면 인스턴스 ID). 값 × 효과 배율(2-12) |
| 능력치 보정 떼기 | `RemoveModifiersEffect` | 대상에서 그 출처의 Modifier를 모두 뗀다(`source`, 비우면 이 능력 ID) |
| 상태 효과 걸기 | `ApplyStatusEffect` | 대상에게 상태 효과(DB › 상태 효과)를 건다(`status`·`stacks`·`length`). 이미 걸려 있으면 그 상태 효과의 다시 걸 때 규칙(중첩·갱신)을 따른다. 중첩 × 효과 배율(반올림, 최소 1), 건 개체 = 사용자 |
| 상태 효과 풀기 | `RemoveStatusEffect` | 대상의 상태 효과를 푼다(`status`·`stacks`, 0이면 모두). 끝날 때 결과는 돌지 않는다 |
| 상태 효과 줄이기 | `ShortenStatusEffect` | 대상의 상태 효과의 남은 지속을 줄인다(`status`·`amount`). 다 되면 끝난다. 쿨다운 −1(3-12) |

### 2-6. 기본 대상 Block — 목록 · 이벤트 · 거르기 · 상태 효과 · 무작위 · 정렬

"누구에게"의 흔한 모양은 기본 제공 대상 Block(분류 "대상")을 이어 붙여 코드 없이 만든다. 위에서부터 차례로 돌고, 앞 Block이 낸 대상이 다음 Block의 입력이다(비어 있으면 고른 대상에서 출발). 프로젝트만의 대상("옆 칸의 적", "주인공")은 그대로 `TargetSelector<T>`를 상속해 더한다(2-5).

| 화면 이름 | 코드 형식 | 칸 | 하는 일 |
|---|---|---|---|
| 사용자 자신 | `SelfTarget` | — | 앞 대상을 버리고 사용자(상태 효과·패시브면 가진 쪽) 하나 |
| 플레이어 데이터 목록 | `DataListTarget` | `key`(플레이어 데이터) · `path`(필드(목록), 비우면 값 전체) | **앞 대상을 버리고** 그 목록의 요소 모두. 요소가 id 글자면 그 id의 개체를 찾는다(손패 `battle.hand` 같은 id 목록). 경로 끝이 목록이 아니라 id 글자 하나(`battle.targetId`)면 그 개체 하나 |
| 이벤트의 개체 | `EventFieldTarget` | `field`(이벤트 필드(id 또는 개체)) | 받은 이벤트(행동 직전 이벤트 포함)의 그 필드가 가리키는 개체. 글자면 그 id의 개체, 객체면 그대로(id가 있는 것만), 목록이면 요소마다. 예: 피해 이벤트의 `sourceId` = 때린 쪽(반사), `targetId` = 맞은 쪽 |
| 조건으로 거르기 | `FilterTarget` | `path`(필드) · `op`(비교, `FieldOp`) · `value`(값) | 앞 대상 중 필드 검사가 참인 것만. 비교는 값 조건과 같다(`hp 이하 10`, `element 일치 fire`, `tags 포함 boss`, 11장 3-2) |
| 상태 효과로 거르기 | `StatusFilterTarget` | `status`(상태 효과) · `kind`(종류(상태 효과를 비웠을 때)) · `op`(비교 `>=` `<=` `>` `<` `==`) · `stacks`(중첩(종류면 가짓수)) | 앞 대상 중 그 상태 효과의 중첩 비교가 참인 것만. 걸려 있지 않으면 0이다. "독에 걸린 적 모두" = `poison` `>=` `1`, "디버프가 없는 아군" = `status` 비우고 `kind` 디버프 `<=` `0`. 조건 `상태 효과 검사`(29장)와 같은 비교 |
| 무작위 n개 | `RandomTarget` | `count`(개수, 기본 1) · `stream`(난수 흐름, 기본 `target`) | 앞 대상 중 서로 다른 n개(같은 개체가 여럿 들어 있어도 한 번만 센다 — 맞힌 목록의 같은 적). 결정론 난수 흐름 `Game.Random(stream)`이라 Undo하고 다시 해도 같은 결과다. 모자라면 있는 만큼 |
| 정렬해서 n개 | `SortTarget` | `path`(숫자 필드) · `descending`(큰 것부터) · `count`(개수(0 = 모두), 기본 1) | 앞 대상을 숫자로 정렬해 앞에서 n개. 기본은 작은 것부터. 값이 같으면 원래 순서. 필드 이름이 하나(`hp`)이고 대상에 id가 있으면 **능력치**(Modifier까지 반영한 `Game.Attribute`)로 재고, 경로(`stats.speed`)면 그 값으로 잰다 |

- 요약은 이어 붙인 Block이 화살표로 보인다(아래 코드의 조립이면 `battle › units → team enemy 일치 → hp 0 초과 → hp 작은 것부터 1개: 피해`).
- `플레이어 데이터 목록`은 앞을 버리고 새로 고르므로 보통 맨 앞에 둔다. `조건으로 거르기`·`상태 효과로 거르기`·`무작위 n개`·`정렬해서 n개`는 앞 대상을 받아 줄인다.
- 검증 오류: "상태 효과로 거르기: 상태 효과나 종류를 고르세요."
- `이벤트의 개체`는 이벤트를 받아 발동한 결과(상태 효과·패시브·도전과제의 이벤트 받기·행동 직전 이벤트)에서만 뜻이 있다. 능력·사용 기능은 사용하는 순간이라 이벤트가 없어 아무도 고르지 않는다.
- 쓰러진 적처럼 남아 있으면 안 되는 대상은 `조건으로 거르기`(`hp 초과 0`)로 뺀다. 목록에서 이미 빠진 개체는 처음부터 나오지 않는다.
- 무작위의 난수 흐름은 쓰는 곳마다 이름을 다르게 주면, 한 곳에 뽑기를 더해도 다른 곳의 결과가 바뀌지 않는다(07장).

```csharp
// "체력이 가장 낮은 적에게 20" — 코드로 조립하면
var finisher = new TargetedOutcome
{
    targets =
    {
        new DataListTarget { key = "battle", path = "units" },
        new FilterTarget { path = "team", op = FieldOp.Equal, value = "enemy" },
        new FilterTarget { path = "hp", op = FieldOp.Greater, value = "0" },
        new SortTarget { path = "hp", count = 1 },
    },
    effects = { new Damage { amount = 20 } },
};
```

### 2-7. `AbilityDefinition` · `session.Abilities` — 코드로 정의하기

```csharp
var fireball = new AbilityDefinition
{
    Id = "fireball",
    DisplayName = "파이어볼",
    When = new AllTrigger { triggers = { new UseTrigger(), new ConditionTrigger { condition = new UserAlive() } } },
};
fireball.Outcomes.Add(new TargetedOutcome
{
    targets = { new AddEnemiesAround { range = 1 } },
    effects = { new Damage { amount = 10 } },
});
session.Abilities.Define(fireball);
```

| 멤버 | 뜻 |
|---|---|
| `Id` | 필수 |
| `DisplayName` | 표시 이름(없으면 ID) |
| `When` | Trigger. `null`이면 언제나 쓸 수 있다 |
| `FailText` | 쓸 수 없을 때 글(텍스트 키 또는 글, `{ability}`·`{condition}` 틀). 비우면 `FailureKeys.CannotUse`의 기본 글(2-1). DB 칸 `failText` |
| `Outcomes` | 결과 |
| `Targets` / `Effects` | 짧은 형태: 결과 앞에 "대상에게 효과" 하나로 실행된다 |

| `session.Abilities` | 뜻 |
|---|---|
| `Define(definition)` | 등록. 정의를 **복사**해 둔다(Block 인스턴스가 세션마다 따로라 여러 세션이 섞이지 않는다). 등록한 뒤 원본을 고쳐도 반영되지 않는다. 그 ID에 `[BindTo]`가 있으면 결과가 그 코드로 바뀐다 |
| `Get(id)` / `All` | 찾기 / 전체 |

게임에서는 런타임이 시작할 때 DB의 능력 전부와, 사용 기능이 붙은 인스턴스 전부를 등록한다.

### 2-8. `Usable` — 클래스에 붙이는 사용 기능

```csharp
using System;
using GBible;
using UnityEngine;

[Serializable]
public class Item
{
    public string id;
    public string name;
    [SerializeReference] public Usable usable;      // null = 쓸 수 없는 아이템
}
```

문법 요소를 차례로:

1. **필드 형식은 정확히 `Usable`**. 런타임이 시작할 때 각 인스턴스 값에서 이 형식의 필드를 찾는다(부모 클래스 필드 포함, 형식마다 한 번만 찾는다).
2. **`[SerializeReference]`** — 붙여야 인스턴스 편집에서 "기능 필드"(비어 있을 수 있는 필드)가 되고 `+ 기능`으로 붙일 수 있다.
3. **`null`이면 기능 없음.** 게임 코드는 `if (item.usable is { } use) …`로 확인한다.
4. `Usable`의 칸:
   - `when`(`Trigger`, 기본 `new UseTrigger()`, 잠금 `사용 · 조건 충족`)
   - `then`(`List<Outcome>`, 잠금 `대상에게 효과 · 플레이어 데이터에 주기 · 코딩한 결과`)
   - `failText`(`string`, 기본 빈 글): 쓸 수 없을 때 글(`AbilityDefinition.FailText`가 된다, 2-1)
5. 등록 ID = **인스턴스 ID**(DB › 인스턴스 항목의 ID). `Item.id` 필드 값과는 상관없다.
6. 코드로 능력 정의를 만들려면 `usable.ToDefinition(id, displayName)`.

개수 줄이기 같은 뒤처리는 게임 코드가 한다(결과 안에서 하면 같은 Undo 단위가 된다. 3-4).

### 2-9. 사용 조건 Block

```csharp
[Serializable, BlockInfo("사용자 체력 있음", Category = "사용")]
public class UserAlive : SimpleCondition
{
    protected override bool Check() => Context?.User is Unit u && u.hp > 0;
}
```

`Context.User`·`Context.Picked`·`Context.Owner`(능력·인스턴스 ID)를 본다. `Game.Use`·`Game.TryUse`·`Game.CanUse` 모두 이 조건으로 판정한다.

- `Game.CanUse`의 미리 보기 순간이면 `Context.IsCheck`가 참이다. 난수를 쓰는 조건을 직접 만들면 이때 굴리지 말고 참을 돌려준다(기본 조건 `확률`이 이렇게 한다). 굴리고 싶으면 `Game.Chance(확률, 흐름)`을 쓴다(행동 직전 이벤트 `확률`을 거친다, `28-Attributes-Intercepts.md` 4-14).

### 2-10. `Game.TryUse` — 되면 쓰고, 안 되면 거짓

```csharp
bool fired = Game.TryUse("luckyCat", joker);                 // 쓸 수 있으면 쓰고 true
foreach (var card in hand) Game.TryUse(card.ability, hero);  // 되는 것만 발동
```

| 인자 | 뜻 |
|---|---|
| `ability` · `user` · `targets` | `Game.Use`와 같다(2-1) |

- 사용 조건이 맞으면 `Game.Use`와 똑같이 쓰고 `true`를 돌려준다.
- 사용 조건이 안 맞으면 **아무것도 하지 않고** `false`다. 실패(`GameFailure`)를 던지지 않으므로 바깥 행동이 취소되지 않는다.
- 행동 직전 이벤트 `사용`이 막았거나 `times`를 0으로 만들었을 때도 `false`다(결과를 내지 않았으므로).
- 조건을 한 번만 검사한다. 조건 `확률`도 한 번만 굴린다. 미리 `CanUse`로 묻고 다시 `Use`하는 두 번 검사가 아니다.
- 없는 ID는 그대로 예외다(프로그래머 실수).
- 조각을 차례로 돌며 "되는 것만 발동"할 때 쓴다: 손패의 카드마다, 조커 칸의 조커마다, 보드의 유닛마다.
- 행동 하나 안에서 부른다(점수 계산 행동, 턴 끝 행동 …). 행동 밖에서 부르면 부를 때마다 행동 하나가 되고, 쓰지 못한 것도 값이 바뀌지 않은 Undo 한 단위로 남는다.

**확률이 든 능력은 `TryUse`로.** `Game.Use`는 확률이 빗나가면 사용 조건 실패를 던지고 행동이 취소된다. 행동이 취소되면 난수도 그 전으로 돌아가므로, 같은 상태에서 다시 `Use`하면 같은 수가 나와 또 빗나간다. `TryUse`는 빗나가도 행동이 끝나므로 굴린 난수가 남고, 다음에는 다음 수를 굴린다.

**비용도 "되면 낸다"면 `Game.TryPay`.** 결과 `비용 내기`·`Game.Pay`는 모자라면 실패를 던져 바깥 행동 전체가 취소된다. 틱 묶음 행동(33장 5-7) 안에서 "MP가 되면 주문을 쓴다"처럼 모자란 것이 정상인 비용은 `Game.TryPay(key, path, 양, forId, user)`로 낸다. 낼 수 있으면 내고 `true`, 모자라면 **아무것도 바꾸지 않고** `false`다(실패를 던지지 않는다). 행동 직전 이벤트 `비용 지불` 받기은 그대로 거친다(`forId` = 무엇을 위한 비용, `user` = 내는 쪽).

```csharp
// 틱 묶음 행동 안: 지팡이마다 MP가 되면 주문을 쓰고, 모자라면 그 지팡이만 건너뛴다
foreach (var wand in run.wands)
    if (Game.TryPay("run", "mana", wand.cost, forId: wand.spell, user: wand))
        Game.Use(wand.spell, wand);
```

### 2-11. 행동 직전 이벤트 `사용` — 두 번 발동 · 봉인

`Game.Use`·`Game.TryUse`는 사용 조건을 통과한 뒤, 결과를 내기 직전에 기본 행동 직전 이벤트 `사용`(`UseIntent`)을 행동 직전 이벤트에 통과시킨다(→ `28-Attributes-Intercepts.md` 2-3·4-14).

| `UseIntent` 필드 | 뜻 · 고치면 |
|---|---|
| `ability` | 능력·사용 아이템 ID. 행동 직전 이벤트 조건으로 거를 때 쓴다(바꿔도 다른 능력이 되지 않는다) |
| `userId` | 사용자 id. 거르기용 |
| `times` | 결과를 몇 번 내나(기본 1). 더하기 1 = 두 번 발동, 0이면 내지 않는다 |
| `cancelled` | 막기 = 봉인·침묵. 실패를 던지지 않고 아무 일 없이 끝난다 |

- `times`가 2면 결과 목록 전체를 두 번 낸다. 대상 Block도 매번 다시 고른다(`무작위 n개`는 횟수마다 새로 뽑는다).
- 결과 안의 `비용 내기`도 횟수만큼 낸다. 한 번만 낼 비용은 `Game.Use`를 부르는 행동이 먼저 낸다(`28-Attributes-Intercepts.md` 5-6의 카드 내기).
- `능력 사용` 이벤트는 한 번 쏘고, `Times`에 낸 횟수가 실린다. 연출·로그가 "두 번 발동"을 안다.
- 막히면 이벤트도 없다. 화면이 "봉인됨"을 보이려면 행동 직전 이벤트를 받는 쪽이 따로 이벤트를 쏘거나 상태 효과를 본다.
- 행동 직전 이벤트를 받는 쪽은 패시브(유물·조커)·상태 효과(광란 3턴)·행동 직전 이벤트를 연 프로젝트 기능이다. 능력의 트리거 칸에는 행동 직전 이벤트가 없다.

### 2-12. 행동 직전 이벤트 `효과 배율` — 수치 2배(Lumin · Critical), 프로젝트 Block에서 읽기

`Game.Use`·`Game.TryUse`는 행동 직전 이벤트 `사용`을 지난 뒤, 결과를 내기 전에 기본 행동 직전 이벤트 `효과 배율`(`PowerIntent`)을 한 번 거친다. 돌아온 `multiplier`가 컨텍스트의 `Power`(효과·대상 Block에서는 `Use.Context.Power`)다. 패시브도 발동할 때마다 같은 행동 직전 이벤트를 거친다(17장 0-1).

| `PowerIntent` 필드 | 뜻 · 고치면 |
|---|---|
| `ownerId` | 능력·패시브 ID. 거르기용 |
| `userId` | 사용자 id(패시브면 가진 쪽 id). 거르기용 |
| `multiplier` | 곱할 배율(기본 1). `곱하기 2` = 수치 2배 |

- 기본 효과 Block이 곱한다: `능력치 보정`의 값, `상태 효과 걸기`의 중첩(반올림, 최소 1), `필드 바꾸기`의 `더하기` 값(12장 2-4). `플레이어 데이터에 주기`·`비용 내기`는 곱하지 않는다.
- 배율은 사용 한 번에 한 번 정한다. `times`(두 번 발동)로 결과를 되풀이해도 같은 배율이다.
- Lumin("이것이 걸린 유닛의 수치 2배") = 상태 효과: `trigger` `행동 직전 이벤트 받기` → `효과 배율`, `holderField`(가진 쪽 필드) `userId`(걸린 쪽이 쓸 때만), `outcomes` `행동 직전 이벤트 고치기` `multiplier` `곱하기` `2`(28장 5-12). Critical("맨 오른쪽이면 2배")은 같은 행동 직전 이벤트에 조건 `자리 검사`를 `AND`로 둔다.
- `Game.CanUse`는 이 행동 직전 이벤트를 보지 않는다. 행동 직전 이벤트의 자세한 규칙은 28장 4-14.

프로젝트 효과 Block은 배율을 직접 곱한다.

```csharp
[Serializable, BlockInfo("피해", Category = "전투", Sentence = "{amount} 피해")]
public class Damage : Effect<Unit>
{
    public int amount = 10;
    protected override void Apply(Unit target)
    {
        var power = Use?.Context?.Power ?? 1;                         // 효과 배율(기본 1)
        target.hp -= (int)Math.Round(amount * power);
    }
}
```

결과(`Outcome`·`Reward`)에서는 `context.Power`·`Context.Power`, `[BindTo]` 함수에서는 `c.Power`다.

---

## 3. 상황별 예제

예제는 다음 형식을 쓴다(DB › 플레이어 데이터에 `battle`(Battle), `party`(Party)가 있다고 본다).

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using GBible;
using UnityEngine;

[Serializable] public class Unit { public string id; public string team; public int x, y; public int hp = 30, maxHp = 30, mana = 10; }
[Serializable] public class Battle { public List<Unit> units = new List<Unit>(); public int turn; }
[Serializable] public class Party { public int gold; public Dictionary<string, int> bag = new Dictionary<string, int>(); }   // 인스턴스 ID → 개수
[Serializable] public class Item { public string id; public string name; [SerializeReference] public Usable usable; }
```

### 3-1. 스플래시: 고른 적 + 주변 1칸의 적에게 10 피해

**에디터**(DB › 능력 `splash`): `trigger` = `사용` / `outcomes` = `대상에게 효과`(대상 `주변 n칸의 적 추가` range 1, 효과 `피해` amount 10).

**게임 코드**: `Game.Use("splash", hero, orc1);`

결과: `orc1`과 그 주변 1칸의 적(같은 팀 제외)의 체력이 10 줄고, `AbilityUsed.Targets`에 그 id들이 담긴다. `Game.Session.Undo()` 한 번에 모두 돌아간다.

### 3-2. 자가 회복

**에디터**: `outcomes` = `대상에게 효과` → 대상 `사용자 자신` → 효과 `체력 회복`(→ `12-Outcomes.md` 3-5의 `Heal`, amount 15).

```csharp
var heal = new AbilityDefinition { Id = "selfHeal" };
heal.Targets.Add(new SelfTarget());
heal.Effects.Add(new Heal { amount = 15 });     // 짧은 형태
session.Abilities.Define(heal);
session.Game.Use("selfHeal", "hero");
```

### 3-3. 마나를 쓰는 능력: 조건 + 소모 + 피해

```csharp
[Serializable, BlockInfo("사용자 마나", Category = "사용", Sentence = "사용자 마나 {cost} 이상")]
public class UserManaAtLeast : SimpleCondition
{
    public int cost = 3;
    protected override bool Check() => Context?.User is Unit u && u.mana >= cost;
}

[Serializable, BlockInfo("마나 소모", Category = "전투", Sentence = "마나 {cost} 소모")]
public class SpendMana : Effect<Unit>
{
    public int cost = 3;
    protected override void Apply(Unit target) => target.mana -= cost;
}
```

**에디터**(DB › 능력 `fireball`):

- `trigger`: `사용` AND `사용자 마나`(cost 3)
- `outcomes`:
  1. `대상에게 효과` → 대상 `사용자 자신` → 효과 `마나 소모`(cost 3)
  2. `대상에게 효과` → 대상 비움(고른 대상) → 효과 `피해`(amount 12)

사용 조건 `사용자 마나`는 기본 조건 `사용자 필드 검사`(`path` `mana`, `op` 이상, `value` `3`, 11장 2-8)로 코드 없이도 된다. 마나가 모자라면 `Game.Use`는 예외로 실패하고 아무것도 바뀌지 않는다. UI는 `Game.CanUse("fireball", caster, out var why)`로 미리 흐리게 한다. `failText`에 `마나가 모자라 {ability}을(를) 쓸 수 없습니다.`를 적어 두면 실패 글과 `why`가 그 글이다(언어마다 다르게 하려면 텍스트 키 `fail.needMana`를 적고 텍스트 표에 넣는다).

### 3-4. 포션(사용 기능 + `[BindTo]`)

1. 코드: 위의 `Item` 클래스.
2. DB › 인스턴스 `potion`: 형식 `Item`, `+ 기능` → `usable`.
3. 결과를 코드로:

```csharp
public static class ItemUses
{
    [BindTo("potion")]
    static void Potion(TriggerContext c)
    {
        var bag = c.Session.Data.Get<Party>("party").bag;
        if (!bag.TryGetValue(c.Owner, out var count) || count <= 0)
            throw new InvalidOperationException("포션이 없습니다.");   // 행동이 중단되고 아무것도 바뀌지 않는다
        bag[c.Owner] = count - 1;
        if (c.User is Unit u) u.hp = Math.Min(u.maxHp, u.hp + 30);
    }
}
```

4. 게임 코드: `Game.Use("potion", hero);` — 회복과 개수 줄이기가 한 행동이라 Undo 한 번에 함께 돌아간다.

결과 코드에서 예외를 던지면 그 사용 행동 전체가 취소된다. 단, `Game.CanUse`는 Trigger만 보므로 이 검사를 모른다. 버튼을 미리 흐리게 하려면 가방 개수를 UI 코드가 직접 보거나 3-6의 방식으로 사용 조건 Block을 쓴다.

### 3-5. 아이템이 능력을 가리키게 하기(에디터에서 전부 조립)

```csharp
using GBible.Data;

[Serializable]
public class Consumable
{
    public string id;
    [DataRef(typeof(AbilityData))] public string ability;   // DB › 능력 목록에서 고르는 칸
    public int maxStack = 10;
}

// 게임 코드: 가방에서 아이템을 쓸 때
void UseItem(Consumable item, Unit user, params object[] targets) => Game.Use(item.ability, user, targets);
```

능력 쪽은 1-1처럼 트리거(사용 조건 포함)·결과를 DB에서 조립한다. 같은 능력을 여러 아이템이 함께 쓸 수 있다.

### 3-6. 가방에 있을 때만 쓸 수 있는 능력

```csharp
[Serializable, BlockInfo("가방에 있음", Category = "사용", Sentence = "가방에 {item} 있음")]
public class InBag : SimpleCondition
{
    public string item;    // 인스턴스 ID(비우면 이 능력·인스턴스 ID)
    protected override bool Check()
    {
        var id = string.IsNullOrEmpty(item) ? Context?.Owner : item;
        var party = Game.All<Party>().FirstOrDefault();
        return party != null && id != null && party.bag.TryGetValue(id, out var n) && n > 0;
    }
}
```

능력 `drinkPotion`: `trigger` = `사용` AND `가방에 있음`(item `potion`), `outcomes` = `대상에게 효과`(사용자 자신 → 체력 회복 30) + `플레이어 데이터에 주기 · party` › `bag`(키와 값 넣기: `potion` → `-1`). 코드 없이 조립되고 `Game.CanUse`도 개수를 안다.

### 3-7. 사용 버튼 상태

```csharp
public class SkillButton : MonoBehaviour
{
    public string abilityId;
    public UnityEngine.UI.Button button;
    string _casterId;

    public void Bind(string casterId) => _casterId = casterId;

    void Update()
    {
        var game = GBibleRuntime.Game;
        if (game == null || _casterId == null) return;
        button.interactable = game.CanUse(abilityId, _casterId, out _);
    }

    public void OnClick(Unit target)
    {
        try { GBibleRuntime.Game.Use(abilityId, _casterId, target); }
        catch (InvalidOperationException e) { Debug.Log(e.Message); }
    }
}
```

(사용자는 오래 들고 있지 말고 id로 들고 있는다. Undo·불러오기 뒤에는 다른 인스턴스일 수 있다.)

버튼 그림은 DB › 능력의 `icon`이다. 능력 정의(`AbilityDefinition`)에는 그림이 없으므로 DB 항목(`GBible.Data.AbilityData`)에서 읽는다.

```csharp
// Start 등에서 한 번
var data = GameDatabase.Load().All<AbilityData>().FirstOrDefault(a => a.Id == abilityId);
if (data != null) iconImage.sprite = data.icon;      // iconImage: UnityEngine.UI.Image
```

### 3-8. 능력 사용 연출

```csharp
IDisposable _sub = GBibleRuntime.Game.Events.Subscribe<AbilityUsed>(e =>
{
    foreach (var id in e.Targets) PlayHitEffect(id);        // 효과를 받은 최종 대상들
});
```

### 3-9. 결과를 함수로: 고른 대상에게 튕기는 번개

```csharp
[BindTo("chainLightning")]
static void ChainLightning(TriggerContext c)
{
    var damage = 20;
    foreach (var t in c.Picked.OfType<Unit>()) { t.hp -= damage; damage = damage * 7 / 10; }
}
```

DB › 능력 `chainLightning`의 트리거는 에디터에서 그대로 정하고(사용 조건 포함), 결과만 코드다.

### 3-10. 버프: 사용자 공격력 +20%(코드 없음)

`Unit`에 숫자 필드 `public double attack = 10;`이 있다고 본다.

**에디터**:

- DB › 능력 `rage`: `outcomes` = `대상에게 효과` → 대상 `사용자 자신` → 효과 `능력치 보정`(attribute `attack`, kind `퍼센트(0.2 = +20%)`, value `0.2`). 요약: `사용자 자신: attack +20%`.
- DB › 능력 `calm`: `outcomes` = `대상에게 효과` → 대상 `사용자 자신` → 효과 `능력치 보정 떼기`(source `rage`).

**게임 코드**: `Game.Use("rage", hero);` → `Game.Attribute(hero, nameof(Unit.attack))`가 12가 된다(`hero.attack` 필드는 10 그대로). 전투가 끝날 때 한꺼번에 떼려면 그 행동 안에서 `Game.RemoveModifiers("rage");`.

- 출처가 능력 ID(`rage`)라서 두 번 쓰면 +20%가 두 줄 붙어 +40%가 되고, 떼기는 둘 다 뗀다. 겹치지 않게 하려면 `rage`의 트리거에 사용 조건 Block을 AND로 둔다(예: `Check() => Context?.User is Unit u && Game.Modifiers(u).All(m => m.source != "rage")`).
- 사용이 행동이라 Undo 한 번에 Modifier도 돌아간다. 계산 규칙·영구 보정·코드 API는 `28-Attributes-Intercepts.md`.
- "3턴 동안 +20%"처럼 저절로 끝나야 하거나 "최대 3중첩"이 있으면 DB › 상태 효과에 능력치 보정을 두고, 능력에서는 `상태 효과 걸기`만 한다(→ `29-Statuses-Schedules-Choices.md` 5-2).

### 3-11. 기본 대상 Block으로 고르기(코드 없음)

`Unit`에 `public string element;`(속성)가 있다고 본다. 세 가지 모두 대상 Block만 이어 붙인다(2-6).

**체력이 가장 낮은 적에게 20** — 능력 `finisher`: `대상에게 효과` →

1. `+ 대상 추가` → `플레이어 데이터 목록`(`key` `battle`, `path` `units`)
2. `조건으로 거르기`(`team` `일치` `enemy`)
3. `조건으로 거르기`(`hp` `초과` `0`)
4. `정렬해서 n개`(`path` `hp`, 큰 것부터 끔, `count` `1`)
5. 효과 `피해`(amount 20)

`hp`는 필드 이름 하나라 Modifier까지 반영한 값으로 잰다. 체력이 같으면 목록에서 앞의 적이다. "공격력이 가장 높은 적 2명"은 `정렬해서 n개`(`attack`, 큰 것부터 켬, `2`).

**불 속성 적 중 무작위 2에게 화상** — 능력 `wildfire`: `플레이어 데이터 목록`(`battle` › `units`) → `조건으로 거르기`(`team` `일치` `enemy`) → `조건으로 거르기`(`element` `일치` `fire`) → `무작위 n개`(`count` `2`, `stream` `wildfire`) → 효과 `상태 효과 걸기`(`burn`).

- 불 속성 적이 하나뿐이면 하나만, 없으면 아무 일도 없다.
- 결정론 난수라 Undo하고 다시 써도 같은 두 적이 걸린다. `stream`을 능력마다 다르게 주면 다른 능력의 무작위와 섞이지 않는다.

**때린 쪽에게 반사 3** — 받은 피해 이벤트가 있어야 하므로 능력이 아니라 상태 효과(또는 패시브)의 결과에 둔다. 피해 규칙이 `[GameEvent("피해")] DamageDealt { sourceId, targetId, amount }`를 쏜다고 본다(28장 5-3).

1. DB › 상태 효과 `thorns`(가시): `trigger` = `이벤트 받기` → `피해`, `holderField` `targetId`(내가 맞을 때만).
2. `outcomes`: `대상에게 효과` → 대상 `이벤트의 개체`(`field` `sourceId`) → 효과 `피해`(3).
3. 능력 `thornShield`: `대상에게 효과` → `사용자 자신` → `상태 효과 걸기`(`thorns`).

- 때린 쪽이 없는 피해(독처럼 `sourceId`가 비어 있는 것)는 아무도 고르지 않는다.
- 반사 피해가 다시 피해 규칙(이벤트를 쏘는 것)을 거치면, 서로 가시를 가진 둘이 주고받다 사슬 한도(16단계)에서 끊긴다. 반사 피해는 출처 없이(`sourceId` 비움) 주거나 피해 이벤트를 쏘지 않는 효과로 준다.
- 가진 목록(유물·특성)에 있는 동안 늘 반사하려면 같은 트리거·결과를 DB › 패시브에 둔다(→ `17-Custom-Features.md` 0절).

### 3-12. 쿨다운 3턴 · 쿨다운 −1(코드 없음)

"화염구: 쓰면 3턴 동안 다시 쓸 수 없다." 턴 끝 이벤트 `[GameEvent("턴 끝")] TurnEnded { turn }`을 턴을 끝내는 행동이 쏜다고 본다.

1. DB › 상태 효과 `fireballCd`: `maxStacks` `1`, `reapply` `지속 갱신`, `duration` › `kind` `이벤트 횟수` · `length` `3` · `tickEvent` `턴 끝`, `hidden`(화면에 숨김) 켬. 트리거·결과는 비운다.
2. DB › 능력 `fireball`:
   - `trigger`: `사용` AND `상태 효과 검사`(`who` `사용자·가진 쪽`, `status` `fireballCd`, `op` `<=`, `stacks` `0`).
   - `outcomes`: ① `대상에게 효과` → 대상 비움 → `피해`(12) ② `대상에게 효과` → `사용자 자신` → `상태 효과 걸기`(`fireballCd`).
   - `failText`: `{ability}은(는) 아직 쓸 수 없습니다.`
3. "쿨다운 −1" 능력 `recharge`: `대상에게 효과` → `사용자 자신` → `상태 효과 줄이기`(`fireballCd`, `1`).

- 턴 1에 쓰면 남은 횟수 3 → 턴 1·2·3 끝에 줄어 턴 4부터 다시 쓴다. 중간에 `recharge`를 쓰면 한 턴 일찍.
- 사용 조건이라 `Game.CanUse("fireball", hero, out var why)`가 쿨다운을 알고 `why`가 `failText`다(3-7의 버튼이 저절로 흐려진다).
- 남은 턴 표시: `Game.Statuses(hero).FirstOrDefault(s => s.status == "fireballCd")?.remaining`.
- 사용자마다 따로이고, Undo·판 저장이 그대로 따라온다. 쿨다운 필드·턴 끝마다 줄이는 코드가 필요 없다.
- 실시간 쿨다운은 지속을 `게임 시간(초)`로 둔다. 숫자로 따라가기·남은 초 계산·아이콘 줄에서 숨기기는 `29-Statuses-Schedules-Choices.md` 5-12·5-13.

### 3-13. 두 번 발동 · 침묵(행동 직전 이벤트 `사용`, 코드 없음)

**광란 — 이 유물을 가진 동안 `strike`가 레벨만큼 더 발동한다.** 가진 목록이 ID → 레벨 딕셔너리 `run › relics`라고 본다.

1. DB › 패시브 `frenzy`: 가진 목록 `run › relics`.
2. `trigger`: `행동 직전 이벤트 받기` → 행동 직전 이벤트 `사용`, 행동 직전 이벤트 조건 `ability` `일치` `strike`.
3. `outcomes`: `행동 직전 이벤트 고치기` → 필드 `times`, 방법 `더하기`, 값 `{level}`.

| `frenzy` 레벨 | `Game.Use("strike", hero)` |
|---|---|
| 없음 | 결과 한 번, `AbilityUsed.Times` 1 |
| 2 | 결과 세 번(1 + 2), `Times` 3 |

**침묵 — 2턴 동안 능력을 쓰지 못한다.** DB › 상태 효과 `silence`: 지속 `이벤트 횟수` 2(셀 이벤트 턴 끝), `trigger` = `행동 직전 이벤트 받기` → `사용`, `holderField` `userId`(걸린 쪽이 쓸 때만). `outcomes` = `행동 직전 이벤트 고치기` `막기`.

- 침묵에 걸린 쪽이 `Game.Use`를 부르면 실패 없이 아무 일도 일어나지 않는다. 결과도 `능력 사용` 이벤트도 없다.
- 버튼을 흐리게 하려면 능력의 사용 조건에 `상태 효과 검사`(`사용자·가진 쪽`, `silence`, `<=`, `0`)도 둔다(`CanUse`는 행동 직전 이벤트를 보지 않는다, 2-3).

### 3-14. 50% 확률로 발동하는 조각을 차례로(`Game.TryUse`)

"행운의 고양이: 점수를 낼 때 50% 확률로 골드 +3." 플레이어 데이터 `run`(`RunState`)에 `gold`와 조커 칸 `jokers`(`List<Joker>`, `Joker { id, ability }` — 조커마다 쓸 능력 ID)가 있다고 본다.

**에디터**(DB › 능력 `luckyCat`): `trigger` = `사용` AND `확률`(`percent` `50`, `stream` `luckyCat`). `outcomes` = `플레이어 데이터에 주기 · run` › `gold` +3.

```csharp
[Serializable, BlockInfo("점수 내기")]
public class ScoreHand : GameAction
{
    protected override void Run()
    {
        var run = Game.Get<RunState>("run");
        foreach (var joker in run.jokers.ToList())
            Game.TryUse(joker.ability, joker);            // 빗나가면 거짓, 실패를 던지지 않는다
    }
}
```

- 조커마다 조건을 한 번만 검사하고 확률도 한 번만 굴린다. 빗나간 조커는 아무것도 하지 않고, 행동은 그대로 이어진다.
- `Game.Use`로 쓰면 첫 빗나감에서 실패가 던져져 `ScoreHand` 전체가 취소된다.
- 점수 버튼의 미리 보기 `Game.CanUse("luckyCat", joker)`는 확률을 굴리지 않고 참이다.
- 같은 시드·같은 행동이면 같은 조커가 맞힌다. Undo하면 굴린 난수도 돌아간다(07장).

---

## 4. 주의할 점 / 자주 하는 실수

- **대상·사용자에 `id`가 없다.** 사용자·대상은 id로 실려 다니고, 끝날 때 최종 대상의 id로 `능력 사용` 이벤트를 쏜다. `public string id;`가 없는 객체가 최종 대상이 되면 예외로 사용 전체가 취소된다.
- **id가 겹친다.** 사용자·대상은 형식 없이 id만으로 플레이어 데이터 전체에서 찾는다. 서로 다른 형식이라도 같은 id가 있으면 먼저 찾은 것이 쓰인다. 소환 개체 등은 `Game.NewId("unit")`로 겹치지 않게 만든다.
- **사용 기능을 인스턴스의 `id` 필드로 부른다.** `Game.Use`의 ID는 DB › 인스턴스 항목의 ID다. 가방에는 인스턴스 ID를 담아 두는 것이 편하다.
- **사용 기능 필드의 형식이 다르다.** `Usable`을 상속한 형식이나 `object` 필드는 찾지 않는다(필드 형식이 정확히 `Usable`이어야 한다). 인스턴스 값의 맨 위 필드만 본다.
- **사용 조건을 결과 코드의 예외로만 둔다.** 동작은 하지만 `Game.CanUse`가 모른다. 미리 보여야 하는 조건은 Trigger에 조건 Block으로 둔다.
- **플레이어에게 "…: 사용 조건을 만족하지 않습니다(트리거 문장)."가 보인다.** 쓸 수 없을 때 글이 비어 기본 글이 쓰였고, `{condition}`은 DB의 한국어 요약이라 언어를 바꿔도 그대로다. 능력·아이템마다 `failText`를 적는다(1-1).
- **쓸 수 없을 때 글에 적은 키(`fail.needMana`)가 글자 그대로 보인다.** 텍스트 표에 그 키가 없어 적은 글을 틀로 썼다. 원문 표·언어 표에 키를 넣는다(`23-Text-Localization.md` 1절).
- **`Define` 뒤에 정의를 고친다.** 능력 정의는 등록할 때 복사된다. 바꾸려면 다시 `Define`한다.
- **사용자 없이 `사용자 자신`을 쓴다.** 사용자가 `null`이거나 찾지 못하면 `사용자 자신`은 아무도 고르지 않는다.
- **능력에 `이벤트의 개체`를 둔다.** 사용하는 순간에는 받은 이벤트가 없어 아무도 고르지 않는다. 반사·반격은 상태 효과·패시브의 이벤트 받기로 만든다(3-11).
- **`플레이어 데이터 목록`을 중간에 둔다.** 앞 대상을 버리고 새로 고르므로, 앞에서 거른 것이 사라진다. 맨 앞에 둔다(2-6).
- **쿨다운을 필드와 결과 코드의 예외로 만든다.** 동작은 하지만 `Game.CanUse`가 모르고 턴마다 줄이는 코드가 생긴다. 숨긴 상태 효과 + 사용 조건 `상태 효과 검사`로 만든다(3-12).
- **도전과제의 결과에서 고른 대상을 기대한다.** 고른 대상(`Picked`)은 `Game.Use`로 쓸 때만 있다.
- **확률이 든 능력을 `Game.Use`로 쓴다.** 빗나가면 사용 조건 실패가 던져지고 바깥 행동까지 취소된다. 난수도 돌아가 같은 상태에서는 또 빗나간다. `Game.TryUse`로 쓴다(2-10).
- **`CanUse`가 참이니 반드시 쓰인다고 본다.** 미리 보기는 확률을 굴리지 않고 참, 행동 직전 이벤트 `사용`의 봉인도 보지 않는다(2-3).
- **조각을 차례로 쓰려고 `CanUse` 뒤에 `Use`를 부른다.** 조건을 두 번 검사하고, 확률 조건이면 미리 보기와 실제가 다르다. `TryUse` 한 번으로 쓴다.
- **`TryUse`를 행동 밖에서 반복한다.** 부를 때마다 행동 하나, Undo 한 단위가 된다(쓰지 못한 것도). 점수 계산 같은 행동 하나 안에서 부른다.
- **두 번 발동하는 능력의 결과에 비용을 둔다.** 행동 직전 이벤트 `사용`의 `times`만큼 결과 전체가 되풀이되어 `비용 내기`도 두 번 낸다. 한 번만 낼 비용은 부르는 행동이 낸다(2-11).
- **소모(마나 −3)를 `필드 바꾸기`로 둔다.** `더하기` 값에는 효과 배율이 곱해져 수치 2배가 걸리면 소모도 2배다. 소모는 `비용 내기`·`Game.Pay`·`Game.TryPay`(배율과 상관없다)나 프로젝트 효과 Block으로 둔다(2-12).
- **틱 묶음 행동 안에서 `Game.Pay`로 낸다.** 모자라면 실패가 던져져 그 틱의 다른 일까지 취소된다. 모자란 것이 정상인 비용은 `Game.TryPay`(2-10).
- **행동 직전 이벤트 `사용`의 `ability`를 바꿔 다른 능력으로 바꾸려 한다.** 거르기용이라 원래 능력이 쓰인다. 다른 능력으로 바꾸려면 규칙 함수에 프로젝트 행동 직전 이벤트를 둔다(`28-Attributes-Intercepts.md` 5-7).
