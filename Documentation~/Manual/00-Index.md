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
| [16. 능력과 사용 기능](16-Abilities-Usable.md) | 쓰는 순간 발동(스킬·아이템 사용). `Game.Use`, 대상·효과 블록. |
| [17. 프로젝트 고유 기능](17-Custom-Features.md) | Trigger → 결과 구조의 기능(퀘스트 등)을 직접 만들기. |

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

// 02. 행동: 플레이어 입력 하나. 대상은 id로 저장되고 Run에서 객체로 받는다.
[Serializable]
public class Attack : GameAction<Unit>
{
    public string attacker;
    public int damage = 10;

    protected override void Run(Unit target)
    {
        target.hp -= damage;                                   // 그냥 대입: 행동 끝 Sync가 기록
        if (target.hp > 0) return;
        var battle = Game.All<Battle>()[0];
        battle.enemies.Remove(target);                         // 리스트도 그냥: Undo하면 같은 객체로 돌아온다
        Game.Events.Publish(new UnitKilled { killerId = attacker, victimId = target.id });   // 이벤트를 쏜다
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
// GBibleRuntime.Game.Do(new Attack { target = "goblin1", attacker = "hero", damage = 12 });
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
| 골드·체력 같은 값을 바꾸고 되돌리기 | 01, 02 |
| "아군 3번 체력 +5", "현재 전투의 적 2 체력 −3" | 03(찾기), 02(행동 안에서 대입) |
| 처치·획득 같은 일이 일어났다고 알리기 | 10 |
| "보스를 잡았고 골드가 100 이상이면" | 11 |
| 조건이 맞으면 보상 주기(코드 없이) | 12, 14 |
| 그 항목만의 특별한 결과를 코드로 | 13 |
| 스킬·포션 사용 | 16, 13 |
| 3초 뒤에 발동하는 효과 | 02(지연 효과) |
| 아이템 원본을 DB에서 만들고 게임에서 사본 쓰기 | 04, 25 |
| 등급·태그 목록 관리 | 05 |
| 번역·조사 처리 | 23 |
| 저장 파일이 안 맞을 때 | 06 |
| 멀티플레이를 붙일 때 받는 쪽 처리 | 01(같은 객체), `../Coding.md`(받는 쪽) |
