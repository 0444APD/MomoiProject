# Momoi Project 매뉴얼

Momoi Project(코드 이름 `GBible`)의 기능별 사용법이다. 각 장은 같은 순서로 쓰여 있다.

1. 한 줄 요약과 언제 쓰는가
2. 에디터에서 쓰는 법
3. 코드로 쓰는 법: 문법 요소를 하나씩, 순서대로
4. 상황별 예제: 실제 게임 장면을 처음부터 끝까지
5. 주의할 점과 자주 하는 실수

처음이면 **20 → 01 → 02 → 10 → 11 → 12** 순서로 읽는다. 나머지는 필요할 때 찾아 읽는다.

---

## 장 목록

### 데이터와 행동
| 장 | 내용 |
|---|---|
| [01. 플레이어 데이터](01-PlayerData.md) | 게임 중에 바뀌는 값을 두는 곳. 평범한 `[Serializable]` 클래스를 쓰고, 필드는 그냥 고친다(Mirror·Sync가 기록). |
| [02. 행동과 Undo](02-Actions-Undo.md) | 플레이어 입력 하나 = 행동 하나 = Undo 한 단위 = 네트워크 한 묶음. 스냅샷 되감기, 지연 효과. |
| [03. Game API](03-GameApi.md) | `Game.Find`·`All`·`Do`·`Use`·`Random`·`Events` 등 게임 코드의 창구. |
| [04. 인스턴스](04-Instances.md) | 클래스의 원본 값(철검, 고블린 …)을 DB에서 채우고 게임에서는 사본을 쓴다. `[Hide]`·`[ShowIf]`, 기능 필드. |
| [05. 키워드](05-Keywords.md) | 등급·태그처럼 정해진 목록에서 고르는 값. `[KeywordGroup]`·`[Keyword]`. |
| [06. 저장·불러오기](06-SaveLoad.md) | 프로필(메타)과 판(세션) 저장, 버전과 변환. |
| [07. 난수와 시간](07-Random-Time.md) | Undo·되감기와 맞는 난수(`Game.Random`)와 게임 시계. |

### 이벤트·트리거·결과
| 장 | 내용 |
|---|---|
| [10. 이벤트](10-Events.md) | `[GameEvent]` 클래스, 이벤트를 쏜다(`Publish`)·받는다(`Subscribe`), 모니터의 이벤트 호출. |
| [11. Trigger와 조건](11-Triggers-Conditions.md) | "언제": 이벤트 받기 · 조건 충족 · 사용, AND·OR·NOT, 필드 경로 조건, 코드로 쓰는 조건. |
| [12. 결과(Outcome)](12-Outcomes.md) | "무엇을": 플레이어 데이터에 주기 · 로그에 기록 · 대상에게 효과 · 코딩한 결과. 컨텍스트(`TriggerContext`). |
| [13. `[BindTo]`](13-BindTo.md) | 항목 하나의 결과를 함수 하나로. 받을 이벤트 = 첫 매개변수의 형식. |
| [14. 도전과제](14-Achievements.md) | 한 번 달성하는 Trigger → 결과. |
| [15. 로그](15-Stats-Logs.md) | 이벤트를 받을 때마다 쌓는 숫자(처치 수·최대 콤보 …). |
| [16. 능력과 사용 기능](16-Abilities-Usable.md) | 쓰는 순간 발동(스킬·아이템 사용). `Game.Use`, 대상·효과 블록(기본 대상: 목록·이벤트의 개체·거르기·무작위·정렬), 쿨다운. |
| [17. 프로젝트 고유 기능](17-Custom-Features.md) | 먼저 기본 종류 DB › 패시브(가지고 있으면 발동: 유물·특성)로 되는지 본다. 그 밖의 Trigger → 결과 구조의 기능(퀘스트·일일 미션 등)을 직접 만들기. |

### 시작·도구·모듈
| 장 | 내용 |
|---|---|
| [20. 시작하기](20-Setup.md) | 설치, 자동 시작, 세션 옵션, `GameIds`. |
| [21. DB](21-Editor-Database.md) | DB 창의 모든 탭과 상세 창, 검증. |
| [22. 모니터와 디버깅](22-Monitor-Debug.md) | 플레이 중 기록·값·행동·이벤트 호출·디버그 명령. |
| [23. 텍스트와 현지화](23-Text-Localization.md) | `Texts`, 문장 틀(조사·복수·선택), 언어 표, CSV. |
| [24. 다이얼로그](24-Dialogue.md) | 대사 그래프, 재생, 선택지·조건·값 바꾸기, 저장 이어 하기. |
| [25. 인벤토리](25-Inventory.md) | 인스턴스를 담는 인벤토리 칸, 더하기·빼기. |
| [26. 오디오](26-Audio.md) | 채널·음량·배경음·효과음. |
| [27. 필드 규칙과 스프라이트 규격](27-Field-Rules-Sprites.md) | 필수 값, 스프라이트 규격 그룹. |

### 게임 규칙
| 장 | 내용 |
|---|---|
| [28. 능력치·Modifier·가로채기·비용](28-Attributes-Intercepts.md) | 능력치(숫자 필드) + Modifier(같은 칸 합, 칸끼리 곱), 일이 일어나기 직전에 의도를 고치는 가로채기, `Game.Pay`. 효과의 문법(천장). |
| [29. 상태 효과·예약·선택 대기](29-Statuses-Schedules-Choices.md) | DB › 상태 효과(중첩·지속·능력치 보정·걸려 있는 동안 트리거·N초마다 주기, 끝날 때 결과, 걸림·끝남 이벤트, 쿨다운), 행동을 나중에 실행하는 예약(`Game.ScheduleAfter`·`ScheduleOn`), 플레이어에게 고르게 하는 선택 대기(`Game.Ask`·`[Chosen]`·결과 `선택 요구`). |
| [30. 밸런스 값·곡선·가중치 표·플레이어용 실패](30-Balance-Tables.md) | DB › 밸런스 값(`Game.Balance`, 층·레벨 곡선, `BalanceSum`·`BalanceCount`), DB › 가중치 표(`Game.Roll`·결과 `가중치 표에서 주기`, 꽝·안쪽 표·조건·확률·천장), 언어별 실패 메시지(`Game.Fail`·`GameFailure`·`FailureKeys`). |
| [36. 격자·큰 수](36-Grid-BigNumber.md) | 칸 좌표·거리·이웃·이동 범위·길찾기·직선·시야·범위(`Grid`·`Cell`), double을 넘는 큰 수(`BigNumber`, "1.23K"·만·억 표기). |

### 화면·기기·실시간
| 장 | 내용 |
|---|---|
| [31. 슬롯·설정·입력](31-Slots-Settings-Input.md) | 저장 슬롯(`GBibleRuntime.UseSlot`·`SlotInfoOf`), 기기별 설정(DB › 설정·`GameSettings`, 언어·음량·화면 적용 대상), 키 바꾸기(선택 기능 `GameInput`). |
| [32. 화면 연결·연출 대기열·씬 흐름·월드 상태](32-View-Presentation-Scenes.md) | `GameView.Redraw`·`ViewList`·컴포넌트 '데이터 연결', 연출 대기열(`GameView.Present`·`Busy`·속도·건너뛰기), 판 데이터를 따라 씬 열기(`GameScenes`), 씬 오브젝트의 상태(`Game.SetWorld`·'월드 상태 연결'). |
| [33. 고정 틱·미리보기 실행·CSV](33-Tick-Preview-Csv.md) | 실시간 게임의 고정 틱(`TickRate`·`Ticked`·`TickAlpha`), 행동을 해 보고 되돌리는 미리보기(`Game.Preview`·`CanDo`·`IsPreviewing`), DB 목록의 CSV 내보내기·가져오기. |
| [34. Steam](34-Steam.md) | 선택 기능 Steam(Steamworks.NET): DB › Steam 설정, 자동 초기화·Steam으로 다시 시작·오버레이 일시정지·처음 언어, 도전과제·로그 → Steam, 리치 프레즌스(`GameSteam`), Steam Cloud(Auto-Cloud) 설정. |
| [35. 화면(UI Toolkit)](35-Screens.md) | DB › 화면(UXML·층·뒤로 닫기·열 씬)과 화면 코드(`[BindTo]` `GameScreen`), 코드 없이 값 보이기(binding-path), 버튼 → 행동(`OnClick`, 실패는 알림), 목록 칸(`ElementList`), 기본 화면(고르기 창·확인·설정·슬롯·일시정지·알림), 테마 USS·화면 준비. |

---

## 한 번에 보기: 작은 전투 하나

여러 장의 기능이 어떻게 이어지는지 보여 주는 예다. 장마다 자세한 설명이 있다.

```csharp
using System;
using System.Collections.Generic;
using GBible;

// 01. 플레이어 데이터: 평범한 클래스. 필드를 그냥 고치면 기록된다.
[Serializable]
public class Unit
{
    public string id;          // Game.Find로 찾는 id
    public int hp = 30;
    public int exp;
}

[Serializable]
public class Battle
{
    public List<Unit> allies = new List<Unit>();
    public List<Unit> enemies = new List<Unit>();
}

// 10. 이벤트: 일어난 일을 값과 함께 담는다.
[GameEvent("유닛 처치")]
public class UnitKilled
{
    public string killerId;
    public string victimId;
}

// 02. 행동: 플레이어 입력 하나. 대상은 객체로 넣는다(기록에는 id만, 실행 직전에 지금 객체로 다시 찾는다).
[Serializable, BlockInfo("공격")]
public class Attack : GameAction
{
    public Unit attacker;
    public Unit target;
    public int damage = 10;

    protected override void Run()
    {
        target.hp -= damage;                                   // 그냥 대입: 행동 끝 Sync가 기록
        if (target.hp > 0) return;
        var battle = Game.All<Battle>()[0];
        battle.enemies.Remove(target);                         // 리스트도 그냥: Undo하면 같은 객체로 돌아온다
        Game.Events.Publish(new UnitKilled { killerId = attacker.id, victimId = target.id });   // 이벤트를 쏜다
    }
}

// 13. [BindTo]: DB의 도전과제 "firstBlood"(Trigger = 유닛 처치 받기)의 결과를 함수 하나로.
public static class BattleLogic
{
    [BindTo("firstBlood")]
    static void FirstBlood(UnitKilled e, TriggerContext c)    // 받을 이벤트 = 첫 매개변수 형식
    {
        c.Game.Find<Unit>(e.killerId).exp += 50;
    }
}

// 게임 코드
// GBibleRuntime.Session.Data.Define("battle", DataZone.Session, new Battle { … }, "전투");
// GBibleRuntime.Game.Do(new Attack { attacker = hero, target = goblin, damage = 12 });   // hero·goblin: 데이터 안의 Unit 객체
// GBibleRuntime.Session.Undo();   // 피해·제거·처치 이벤트로 생긴 로그 증가까지 행동 하나가 통째로 되돌아간다
```

에디터에서 할 일:
- DB › 도전과제에 `firstBlood`를 만들고, Trigger에 '이벤트 받기 › 유닛 처치'를 넣는다(14장). 결과 칸은 `[BindTo]` 때문에 잠겨 있고 `BattleLogic.FirstBlood`가 보인다(13장).
- DB › 로그에 '처치 수'를 만들면, 같은 이벤트를 받을 때마다 1씩 쌓인다(15장).
- 플레이 중 모니터에서 기록·값·행동을 보고, '이벤트 호출'로 `UnitKilled`를 직접 쏴 볼 수 있다(22장).

---

## 상황별로 찾기

| 하고 싶은 일 | 장 |
|---|---|
| Steam 도전과제·통계·친구 목록 상태, Steam Cloud 저장 동기화 | 34 |
| HUD·상점·타이틀 같은 화면을 UXML로 만들고 값·버튼 잇기 | 35 |
| 고르기 창·설정 화면·세이브 슬롯·일시정지·"정말 할까요?"·알림 | 35(기본 화면) |
| 골드·체력 같은 값을 바꾸고 되돌리기 | 01, 02 |
| "아군 3번 체력 +5", "현재 전투의 적 2 체력 −3" | 03(찾기), 02(행동 안에서 대입) |
| 처치·획득 같은 일이 일어났다고 알리기 | 10 |
| "보스를 잡았고 골드가 100 이상이면" | 11 |
| 조건이 맞으면 보상 주기(코드 없이) | 12, 14 |
| 그 항목만의 특별한 결과를 코드로 | 13 |
| 스킬·포션 사용 | 16, 13 |
| "체력이 가장 낮은 적", "불 속성 적 중 무작위 2", "때린 쪽에게 반사"(대상 고르기) | 16 |
| 가지고 있으면 발동(유물·특성·패시브 스킬·장비 효과) | 17(패시브) |
| 퀘스트·일일 미션·튜토리얼 단계처럼 새 "언제 → 보상" 종류 | 17 |
| 장비로 공격력 +30, 버프 +20%, 영구 강화 | 28 |
| "받는 피해 −5", "죽음 막기", "비용 0", "다음 스킬 2회" | 28 |
| 도발·대신 맞기·중첩만큼 피해 감소 | 28 |
| 에너지·골드를 내고 모자라면 실패 | 28 |
| 독·화상·보호막·분노 같은 상태 효과(중첩·지속 턴 수) | 29 |
| 쿨다운("3턴 동안 다시 못 씀", "쿨다운 −1") | 29, 16 |
| 실시간 지속 피해·재생("0.5초마다") | 29 |
| "3턴 뒤 폭발" 같은 지연 효과 | 29(상태 효과의 끝날 때 결과) |
| 행동을 N초 뒤·이벤트 N번 뒤에 실행(예약) | 29, 02(지연 효과) |
| "카드 1장을 고른다", "보상 3개 중 1개"(플레이어가 고르기) | 29 |
| 시작 에너지·상점 가격처럼 기획자가 고치는 숫자, 층·레벨에 따라 오르는 값 | 30 |
| 드롭·보상·상자 내용물을 확률로 뽑기(꽝, 서로 다른 3장, 3층부터) | 30 |
| 천장(뽑을수록 확률이 오르고 N번째 확정) | 30 |
| "10개 사기", "살 수 있는 만큼" | 30 |
| 현실 시각·오프라인 보상·일일 초기화 | 07 |
| 격자·이동 범위·길찾기·시야 | 36 |
| 큰 수(방치형, double을 넘는 값) | 36 |
| "에너지가 모자랍니다"를 언어마다 다르게 | 30, 23 |
| 수치 수십 개를 구글 시트·엑셀에서 한꺼번에 고치기 | 33(CSV), 30 |
| 타이틀의 저장 슬롯 1·2·3 | 31, 06 |
| 설정 화면(음량·언어·전체 화면·전투 속도), 키 바꾸기 | 31 |
| 적 목록·손패·체력 표시를 데이터에 맞춰 그리기 | 32 |
| 피해 숫자·죽는 모습을 차례로 보여 주고, 그동안 입력 막기 | 32 |
| 맵 → 전투 → 맵, 판이 끝나면 타이틀(씬 바꾸기) | 32 |
| 열린 상자·잡은 보스·켜 둔 스위치 기억하기 | 32 |
| 실시간 게임의 시간 규칙을 기기마다 같게 | 33 |
| 카드에 마우스를 올리면 예상 피해, 낼 수 없는 이유 | 33 |
| 아이템 원본을 DB에서 만들고 게임에서 사본 쓰기 | 04, 25 |
| 등급·태그 목록 관리 | 05 |
| 번역·조사 처리 | 23 |
| 저장 파일이 안 맞을 때 | 06 |
| 멀티플레이를 붙일 때 받는 쪽 처리 | 01(같은 객체), `../Coding.md`(받는 쪽) |
