# 06. 저장·불러오기

**한 줄 요약**: 플레이어 데이터는 구역별로 두 파일에 저장된다. **프로필**(메타 구역: 재화·해금·도전과제·누적 로그)과 **판**(세션 구역: 이번 판 내용 + 난수 상태 + 게임 시간)이다. 싱글 게임은 `GBibleRuntime`이 시작할 때 프로필을 불러오고, 끌 때 둘 다 저장한다. 판 이어하기는 게임이 원하는 때 `LoadRun`을 부른다.

**언제 쓰는가**

- 타이틀 화면의 "이어하기"·"새 게임"
- 판이 끝났을 때 프로필은 남기고 판만 지우기
- 출시 후 데이터 구조를 바꿀 때 예전 저장을 이어받기(버전 변환)
- 서버·테스트에서 세션 상태를 JSON 글자로 주고받기

---

## 0. 용어

| 용어 | 뜻 |
|---|---|
| 플레이어 데이터 | 세션의 저장소(`session.Data`)에 ID별로 담긴 값. [01-PlayerData.md](01-PlayerData.md) |
| 구역 (`DataZone`) | `Session`(이번 판) 또는 `Meta`(프로필). 저장 파일이 다를 뿐 쓰는 법·Undo 규칙은 같다. |
| 프로필 | 메타 구역 전체. `ExportMeta`/`ImportMeta`. 파일 `profile.json`. |
| 판(런) | 세션 구역 전체 + 난수 상태 + 게임 시계. `ExportRun`/`ImportRun`. 파일 `run.json`. |
| 저장 트리 | 저장 직전의 중간 모습. 키 → 값, 클래스는 "필드 이름 → 값" 딕셔너리, 리스트는 리스트. JSON으로 바뀐다. |
| 저장 버전 | 저장 파일에 적는 정수. 등록한 변환의 가장 높은 버전을 저절로 따라간다(`session.SaveVersion`, 변환이 없으면 1). |
| 변환(마이그레이션) | 예전 버전의 저장 트리를 지금 구조로 고치는 한 단계(`SaveMigrations`). 키·경로만 받는 변환은 DB › 개발 › 저장 변환 표에, 값을 계산하는 변환은 코드에 쓴다. |
| 새 판 | 이번 판 값을 처음 값으로 되돌리고 판 저장 파일도 지우는 것(`GBibleRuntime.NewRun(session)`). 프로필은 그대로. |
| 개발 중 / 출시됨 | 프로젝트 상태. 개발 중에는 호환되지 않는 저장을 지우고, 출시 후에는 이어받는다. |

---

## 1. 에디터에서 쓰는 법

### 1.1 개발 중 · 출시됨 전환

메뉴 **MomoiProject › 출시됨 (저장 호환 유지)**를 누르면 체크 표시가 바뀐다. 기본은 체크 없음(개발 중).

| 상태 | 게임 시작 때 저장 파일의 데이터 구조가 지금과 다르면 | ID 바꾸기 |
|---|---|---|
| 개발 중 | 키·필드가 없어졌거나 이름·형식이 바뀌었거나 저장된 enum 값 이름이 없어졌으면, 변환하지 않고 **프로필과 판 저장을 모두 지운다**. "개발 중 데이터 구조가 바뀌어 저장을 초기화했습니다: 이유" 경고가 콘솔에 남는다. 새 키·필드를 **더하기만** 했으면 그대로 이어간다 | 이력을 남기지 않는다 |
| 출시됨 | 지우지 않는다. 구조 변경은 저장 버전 + 변환(1.4 · 2.7)으로 이어받는다 | 예전 ID → 새 ID 이력을 남겨 런타임이 자동으로 이어받는다 |

저장 파일에는 저장 당시의 데이터 구조(DB의 플레이어 데이터·로그·도전과제의 키, 필드 경로와 형식, enum 값 이름)가 함께 남는다. 구조 정보가 없는 예전 파일은 호환으로 본다.

### 1.2 모니터에서 저장 다루기

플레이 중 **MomoiProject › 모니터**:

- **데이터** 탭: **판 데이터**("이번 판 내용(세션 구역) · 판 저장에 들어감")와 **프로필 데이터**("판이 바뀌어도 유지(메타 구역) · 프로필 저장에 들어감")를 나눠 보여 준다.
- **명령** 탭의 **저장** 묶음:

| 명령 | 하는 일 |
|---|---|
| 프로필 저장 | 지금 프로필을 `profile.json`에 쓴다 |
| 프로필 삭제 | `profile.json`을 지운다(다음 플레이부터 적용) |
| 판 저장 | 진행 중인 판(세션 구역·난수·시간)을 `run.json`에 쓴다 |
| 판 불러오기 | `run.json`을 불러와 이어서 진행한다 |
| 새 판 | 이번 판 내용을 처음 값으로 되돌리고 판 저장을 지운다(`GBibleRuntime.NewRun`, 2.3) |

### 1.3 ID 바꾸기

DB 상세의 제목(ID)을 눌러 ID를 바꾸면 참조가 모두 따라가고, 저장(Ctrl+S)할 때 적용된다. 출시됨 상태라면 플레이어 데이터·로그·도전과제의 예전 ID로 저장된 값을 새 ID로 이어받는다(런타임이 시작할 때 별칭으로 등록). 개발 중이면 이력이 없으므로 다음 플레이에서 그 저장이 초기화된다.

### 1.4 저장 변환 표: DB › 개발 › 저장 변환

출시 후 저장 구조를 바꿨을 때, 키·경로만으로 정해지는 변환은 코드 대신 DB 표에 적는다. DB 사이드바 그룹 **개발**의 **저장 변환** 탭(`SaveMigrationData`). 항목 하나 = 변환 한 단계.

| 칸 | 뜻 |
|---|---|
| 버전 | 이 버전으로 올릴 때 적용한다(버전 - 1 → 버전). 2부터(처음 출시가 버전 1) |
| 종류 | **필드 옮김** / **키 이름 바꿈** / **키 삭제** |
| 키 | 저장 파일 안의 플레이어 데이터 키(예전 이름). 예: `run` |
| 예전 경로 · 새 경로 | 필드 옮김일 때. 값 안의 필드 경로. 리스트의 모든 요소는 `items[].power`처럼 `[]` |
| 새 키 | 키 이름 바꿈일 때 |

- 게임이 켜질 때 버전 순서(같은 버전은 목록 순서)로 `options.Migrations`에 등록되고, 그다음 코드의 `Configuring`이 더 등록할 수 있다. 저장 버전은 등록된 변환(표 + 코드)의 가장 높은 버전을 저절로 따라간다.
- 표의 세 종류는 코드의 `MoveField`·`RenameKey`·`RemoveKey`(2.7)와 같다. 값을 계산해 바꾸는 변환(`Migrations.Add`)만 코드로 쓴다.
- 빈 키, 2보다 작은 버전, 빈 경로·새 키는 DB 검사가 오류로 알린다.
- DB 항목 ID 바꾸기(1.3)는 이 표가 필요 없다.

예(카드 던전): 버전 2 · 필드 옮김 · 키 `run` · `hero.gold` → `gold`, 버전 3 · 키 삭제 · 키 `oldTutorial`.

---

## 2. 코드로 쓰는 법 — 문법 요소

### 2.1 저장 위치: `GBibleRuntime.SavePath` · `RunSavePath`

```csharp
string profilePath = GBibleRuntime.SavePath;      // {Application.persistentDataPath}/MomoiProject/profile.json
string runPath = GBibleRuntime.RunSavePath;        // {Application.persistentDataPath}/MomoiProject/run.json
bool canContinue = GBibleRuntime.HasSavedRun;     // run.json이 있는가
```

### 2.2 자동으로 일어나는 일

| 때 | `GBibleRuntime`이 하는 일 |
|---|---|
| 게임 시작(씬을 불러오기 전) | 세션을 만들고, 개발 중이면 호환되지 않는 저장을 지운 뒤, `profile.json`을 불러온다(`ImportMeta`) |
| 바깥 행동이 끝날 때·Undo 뒤(판 저장 "행동마다 저장"일 때) | 그 프레임 끝(행동 밖)에서 판을 저장한다 |
| 바깥 행동이 끝날 때·Undo 뒤("프로필도 행동마다 저장"을 켰을 때) | 그 프레임 끝에서 프로필을 저장한다 |
| 판 끝 이벤트를 받은 행동이 끝날 때(설정했을 때) | 판 저장을 지우고 프로필을 바로 저장한다. 그 뒤 다른 행동이 세션 구역 값을 바꿀 때(새 판)까지 판을 저장하지 않는다 |
| 게임 종료(`OnApplicationQuit`) | 프로필을 저장하고, `AutoSaveRun`이 참이면(판 저장이 "저장하지 않음"이 아니면) 판도 저장한다. 판 끝 이벤트 뒤 아직 새 판이 아니면 저장하지 않는다 |

판은 **자동으로 불러오지 않는다.** 타이틀의 "이어하기"처럼 게임이 원하는 때 `LoadRun`을 부른다.

판 저장 시점은 DB › 시작 파일 › **Bootstrap**의 **판 저장**(끌 때 저장 / 행동마다 저장 / 저장하지 않음)과 **판 끝 이벤트**에서 정한다. 코드로 덮어쓸 수도 있다(코드 값이 우선).

```csharp
GBibleRuntime.AutoSaveRun = false;    // 끌 때 판을 저장하지 않는다(설정보다 우선)
```

### 2.3 직접 저장·불러오기: `GBibleRuntime`

```csharp
var session = GBibleRuntime.Session;
GBibleRuntime.SaveProfile(session);         // profile.json 쓰기
GBibleRuntime.SaveRun(session);              // run.json 쓰기
bool loaded = GBibleRuntime.LoadRun(session); // run.json이 있으면 불러오고 true
GBibleRuntime.NewRun(session);               // 새 판: 이번 판 값을 처음 값으로 + run.json 지우기
GBibleRuntime.DeleteRun();                   // run.json만 지우기
```

실패(파일 접근 오류, 형식 오류, 게임보다 새 버전의 저장 등)는 예외를 밖으로 던지지 않고 기록에 오류("판 불러오기 실패: …" 등)로 남기며, `LoadRun`은 `false`를 돌려준다.

**`GBibleRuntime.NewRun(session)`** — 새 판을 여는 한 줄. 다음을 한 번에 한다.

1. 메모리의 판 값(세션 구역)을 처음 값으로 되돌리고 Undo 기록을 비운다(`session.NewRun()`). 프로필은 그대로.
2. 판 저장 파일 `run.json`을 지운다.
3. 판 끝 상태(판 끝 이벤트 뒤 "새 판까지 판을 저장하지 않음")를 푼다.

메모리와 파일을 함께 지워야 "화면은 새 판인데 이어하기는 예전 판"이 생기지 않는다. **행동 밖**(버튼 등)에서 부른다. 행동 안에서 부르면 `InvalidOperationException`("행동 실행 중에는 새 판을 시작할 수 없습니다"). 새 판의 첫 내용(덱·맵)을 만드는 것은 그 뒤에 행동으로 한다.

```csharp
public static void NewGame(GameSession session)
{
    GBibleRuntime.NewRun(session);
    session.Do(new StartRun());     // 새 판 행동(게임이 만든 것)
}
```

### 2.4 글자로 저장·불러오기: `session.ExportMeta` · `ExportRun`

```csharp
string profileJson = session.ExportMeta();   // 메타 구역 → JSON
session.ImportMeta(profileJson);             // JSON → 메타 구역

string runJson = session.ExportRun();        // 세션 구역 + 난수 + 시간 → JSON
session.ImportRun(runJson);                  // 이어하기
session.NewRun();                            // 세션 구역을 정의할 때의 값으로, Undo 이력 비움(프로필은 그대로, 파일은 모른다)
```

| 메서드 | 담는 것 | 주의 |
|---|---|---|
| `ExportMeta()` | 메타 구역 | 판 내용은 들어가지 않는다 |
| `ImportMeta(json)` | 메타 구역을 덮는다 | 빈 글자면 아무 일도 하지 않는다 |
| `ExportRun()` | 세션 구역, 마스터 시드, 난수 스트림 상태, 틱·게임 시간 | 행동 실행 중에 부르면 예외 |
| `ImportRun(json)` | 위를 모두 되살린다 | 불러온 시점부터 Undo 이력·스냅샷을 새로 시작한다 |
| `NewRun()` | 세션 구역을 처음 값으로 | 파일이 없는 테스트·서버용. 판 파일은 지우지 않는다. Unity 게임은 `GBibleRuntime.NewRun(session)`(2.3) |

- **Undo 이력은 저장하지 않는다.** 불러온 시점부터 새로 쌓인다.
- 아직 정의되지 않은 키의 저장 값은 보관했다가 그 키를 `Define`하는 순간 적용한다. 그래서 불러온 뒤에 코드로 정의해도 된다.
- 판을 불러오면 다음 난수 결과까지 저장한 순간과 같다.

### 2.5 저장 파일에 들어가는 모습

```json
{
  "version": 1,
  "state": {
    "player.gold": 150,
    "player.inventory": {
      "items": [
        { "$id": 12, "name": "지팡이", "level": 3, "element": "Ice",
          "effects": [ { "$type": "Game.Burn", "damage": 7 } ] }
      ]
    }
  },
  "schema": { "…": "…" }
}
```

| 규칙 | 내용 |
|---|---|
| 필드 | 필드 **이름**으로 쓴다(필드 순서를 바꿔도 안전) |
| enum | 값 **이름**으로 쓴다(enum 순서를 바꿔도 안전) |
| 다형성 | 선언 형식과 실제 형식이 다르면 `"$type"`에 실제 형식 이름을 남긴다 |
| 컬렉션 요소 번호 | 시스템이 붙인 요소 번호를 `"$id"`로 남겨, 불러온 뒤에도 기록의 `items[#12]`가 같은 요소를 가리킨다 |
| 숫자 단순 값 | 숫자로 쓴다 |
| 에셋 참조 | DB 항목(`GameData`)은 ID로 쓰고, 불러올 때 DB에서 찾는다. 그 밖의 에셋 참조(스프라이트 등)는 저장하지 않고 "저장할 수 없는 참조입니다" 경고를 남긴다 |
| 판 파일 | 위에 `"kind": "run"`, `"seed"`, `"tick"`, `"time"`, `"random"`(스트림별 상태)이 더해진다 |
| `"state"` 키 | 화면에 보이지 않는 이름이며 예전 파일과의 호환을 위해 그대로 둔다 |

불러올 때:

- 저장 파일에 없는 필드는 클래스의 기본값(필드 초기화)을 쓴다.
- 지금 클래스에 없는 필드는 무시하고 "지금 …에 없는 필드라 무시합니다" 경고를 남긴다.
- 형식이 맞지 않는 값은 기본값을 쓰고 경고를 남긴다.
- 없는 enum 이름은 기본값으로 읽고 경고를 남긴다.

### 2.6 필드 이름 바꾸기: `[FormerlySerializedAs]`

```csharp
using System;
using UnityEngine.Serialization;

[Serializable]
public class HeroSave
{
    [FormerlySerializedAs("hp")] public int health;   // 예전 이름 hp로 저장된 값을 이어받는다
}
```

필드 이름만 바꿨다면 변환 없이 이것만 단다.

### 2.7 저장 버전과 변환: `SaveMigrations`

출시 후 데이터 구조를 호환되지 않게 바꿀 때 쓴다(개발 중에는 저장을 지우므로 필요 없다). 변환만 등록하면 된다. 저장 버전은 등록한 변환의 가장 높은 버전(아래 예에서 4)으로 저절로 올라간다.

키·경로만 받는 변환(`RenameKey`·`RemoveKey`·`MoveField`)은 보통 DB › 개발 › 저장 변환 표(1.4)에 적는다. 표가 먼저 등록되고 아래 코드가 그 뒤에 더한다. 코드로는 값을 계산해 바꾸는 `Add`만 쓰는 것이 기본이다(아래 예는 문법을 보이려고 넷을 모두 코드로 썼다).

DB 항목 ID(인스턴스·유물 …)를 바꾼 것은 변환을 쓰지 않아도 된다. 출시됨 상태에서 ID를 바꾸면 이력이 남고, 런타임이 저장을 불러올 때 `[DataRef]`가 붙은 string·string 리스트 필드의 예전 ID를 새 ID로 바꾼다.

```csharp
using System.Collections.Generic;
using GBible;
using UnityEngine;

public static class SaveSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Configuring += options =>
    {
        options.Migrations.RenameKey(2, "player.money", "player.wallet");                      // v1 → v2
        options.Migrations.MoveField(3, "player.inventory", "items[].title", "items[].name");  // v2 → v3
        options.Migrations.RemoveKey(3, "player.oldFlag");                                      // v2 → v3
        options.Migrations.Add(4, "골드 단위 10배", state =>                                    // v3 → v4
        {
            if (state.TryGetValue("player.wallet", out var wallet) && wallet is Dictionary<string, object> w
                && w.TryGetValue("gold", out var gold) && gold is double g)
                w["gold"] = g * 10;
        });
    };
}
```

| 멤버 | 뜻 |
|---|---|
| `session.SaveVersion` | 지금 게임의 저장 버전 = `SessionOptions.SaveVersion`(최솟값, 기본 1)과 등록한 변환의 가장 높은 버전 중 큰 쪽. 저장 파일에 적힌다 |
| `Migrations.Add(toVersion, label, Action<Dictionary<string, object>>)` | `toVersion - 1` → `toVersion` 변환. 저장 트리(키 → 값)를 직접 고친다. `toVersion`은 2부터 |
| `Migrations.RenameKey(toVersion, oldKey, newKey)` | 플레이어 데이터 키 이름 바꾸기 |
| `Migrations.RemoveKey(toVersion, key)` | 더 이상 쓰지 않는 키 지우기 |
| `Migrations.MoveField(toVersion, key, fromPath, toPath)` | 데이터 값 안의 필드를 경로로 옮긴다. 리스트의 모든 요소에 적용하려면 `items[].power`처럼 `[]`를 붙인다 |
| `Migrations.Steps` | 등록된 변환 목록 `(toVersion, label)` |

- 불러올 때 저장 파일의 버전부터 게임 버전까지 변환을 차례로 적용하고, 하나마다 기록에 "저장 데이터 변환 v1 → v2: …"가 남는다.
- **게임보다 새 버전의 저장 파일은 거부한다**(`FormatException`). 예전 게임이 새 저장을 망가뜨리지 않게 하기 위해서다.
- 변환은 불러오기 전에 등록되어야 한다. `GBibleRuntime`은 시작하자마자 프로필을 불러오므로 반드시 `Configuring`에서 등록한다(DB 표는 그 직전에 저절로 등록된다). 세션을 직접 만든다면 `new GameSession(options)` 전에 `options.Migrations`에, 또는 `ImportMeta`·`ImportRun` 전에 `session.Migrations`에 등록한다.
- 저장 트리에서 숫자는 `double`, 클래스는 `Dictionary<string, object>`, 리스트는 `List<object>`다.

### 2.8 예전 키 이어받기: `session.Data.AddAlias`

```csharp
session.Data.AddAlias("m:coins", "m:gold");   // 예전 키 m:coins로 저장된 값을 m:gold로 불러온다
```

DB에서 ID를 바꾸면(출시 후) 런타임이 자동으로 등록한다. 코드로 정의한 키의 이름을 바꿨을 때 직접 쓴다. 불러오기 전에 등록한다.

### 2.9 호환 검사: `GameSession.IsSaveCompatible` · `SaveSchema`

```csharp
bool ok = GameSession.IsSaveCompatible(json, session.SaveSchema, out string reason);
```

`session.SaveSchema`는 저장 파일에 함께 남길 데이터 구조(키·필드 경로 → 형식)다. `GBibleRuntime`이 DB로 채운다. 이 검사가 개발 중 저장 초기화의 판단 기준이다. 구조 정보가 없는 파일은 호환으로 본다.

---

## 3. 상황별 예제

### 3.1 타이틀 화면: 이어하기 / 새 게임

```csharp
using GBible;
using UnityEngine;

public class TitleMenu : MonoBehaviour
{
    public GameObject continueButton;

    void Start() => continueButton.SetActive(GBibleRuntime.HasSavedRun);

    public void OnContinue()
    {
        if (!GBibleRuntime.LoadRun(GBibleRuntime.Session))
            Debug.LogWarning("판을 불러오지 못했습니다. 모니터 타임라인의 오류를 확인하세요.");
        // 전투 씬으로
    }

    public void OnNewGame()
    {
        GBibleRuntime.NewRun(GBibleRuntime.Session);   // 판 값 초기화(Undo 이력 비움) + run.json 삭제
        // 첫 씬으로
    }
}
```

### 3.2 로그라이크: 판이 끝나면 판만 지우고 보상은 프로필에

```csharp
using System;
using GBible;

[Serializable]
public class EndRun : GameAction
{
    public int earnedShards;
    protected override void Run()
    {
        var data = Game.Session.Data;
        data.Set("meta.shards", data.Get<int>("meta.shards") + earnedShards);   // 메타 구역: 프로필에 남는다
    }
}

public static class RunFlow
{
    public static void Finish(GameApi game, int shards)
    {
        game.Do(new EndRun { earnedShards = shards });
        GBibleRuntime.NewRun(game.Session);      // 세션 구역(맵·유닛·이번 판 인벤토리) 초기화 + 이어하기 파일 삭제
        GBibleRuntime.SaveProfile(game.Session); // 프로필은 바로 저장
    }
}
```

`meta.shards`는 메타 구역 정수 플레이어 데이터로 정의되어 있어야 한다. `NewRun`은 행동 밖에서 부르므로 `EndRun` 행동이 끝난 뒤에 둔다. 판 끝을 이벤트로 알리는 게임이라면 DB › Bootstrap의 **판 끝 이벤트**가 판 저장 삭제·프로필 저장을 대신한다(2.2).

### 3.3 체크포인트 저장(판 중간)

```csharp
using GBible;
using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        var session = GBibleRuntime.Session;
        if (session.Data.InTransaction) return;      // 행동 실행 중에는 판을 저장할 수 없다
        GBibleRuntime.SaveRun(session);
    }
}
```

### 3.4 서버·테스트: 글자로 왕복

```csharp
using System;
using System.Collections.Generic;
using GBible;

[Serializable] public class Bag { public int gold; public List<string> items = new List<string>(); }

public static class SaveRoundTrip
{
    public static void Run()
    {
        var played = new GameSession(new SessionOptions { Seed = 11 });
        played.Data.Define("run.bag", DataZone.Session, new Bag());
        played.Data.Define("meta.gold", DataZone.Meta, 0);
        played.Data.Modify<Bag>("run.bag", b => b.items.Add("검"));
        played.Data.Set("meta.gold", 99);
        string profile = played.ExportMeta();
        string run = played.ExportRun();

        var resumed = new GameSession(new SessionOptions { Seed = 11 });
        resumed.ImportMeta(profile);
        resumed.ImportRun(run);
        resumed.Data.Define("run.bag", DataZone.Session, new Bag());    // 불러온 뒤에 정의해도 값이 들어간다
        resumed.Data.Define("meta.gold", DataZone.Meta, 0);
        // resumed.Data.Get<Bag>("run.bag").items[0] == "검", resumed.Data.Get<int>("meta.gold") == 99
    }
}
```

### 3.5 출시 후 필드 구조 바꾸기

v1에서 `Inventory.coins`였던 것을 v2에서 `Inventory.wallet.gold`로 옮긴다.

**DB로**: DB › 개발 › 저장 변환에 항목 하나 — 버전 2 · 필드 옮김 · 키 `player.inventory` · 예전 경로 `coins` · 새 경로 `wallet.gold`. 클래스만 바꾸면 코드는 없다.

**코드로**(같은 일):

```csharp
using System;
using GBible;
using UnityEngine;

[Serializable] public class Wallet { public int gold; }
[Serializable] public class Inventory { public Wallet wallet = new Wallet(); }

public static class InventoryMigration
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void Hook() => GBibleRuntime.Configuring += options =>
    {
        options.Migrations.MoveField(2, "player.inventory", "coins", "wallet.gold");   // 저장 버전은 2로 저절로
    };
}
```

---

## 4. 주의할 점 · 자주 하는 실수

| 증상 | 원인과 해결 |
|---|---|
| 개발 중 플레이를 시작했더니 저장이 사라짐 | 키·필드를 지웠거나 이름·형식을 바꿨다. 개발 중에는 의도된 동작이다(콘솔 경고에 이유가 있다). 출시 후에는 **MomoiProject › 출시됨 (저장 호환 유지)**을 켠다. |
| 불러온 값이 기본값 | 필드 이름을 바꿨다. `[FormerlySerializedAs("예전이름")]`을 단다. |
| "이 게임보다 새 버전입니다" | 저장 파일의 버전이 지금 게임의 저장 버전(등록한 변환의 가장 높은 버전)보다 크다. 게임을 업데이트하거나, 변환을 지우지 않았는지 확인한다. |
| 변환이 적용되지 않음 | `Configuring`보다 늦게(예: `Started`에서) 등록했다. 프로필은 세션을 만들자마자 불러온다. |
| 이어하기 후 Undo가 안 됨 | Undo 이력은 저장하지 않는다. 불러온 시점부터 새로 쌓인다. |
| 판 저장에 프로필 값이 없음 | 판 저장은 세션 구역만 담는다. 메타 구역은 프로필 저장이다. |
| 스프라이트 필드가 불러온 뒤 비어 있음 | DB 항목(`GameData`)이 아닌 에셋 참조는 저장되지 않는다. 플레이어 데이터에는 DB·인스턴스 ID를 두고 그림은 거기서 찾는다. |
| "행동 실행 중에는 판을 저장할 수 없습니다" | 행동의 `Run` 안에서 `ExportRun`·`SaveRun`을 불렀다. 행동이 끝난 뒤 부른다. |
| 새 판을 열었는데 이어하기 버튼이 보임 | `session.NewRun()`만 불렀다(파일을 지우지 않는다). `GBibleRuntime.NewRun(session)`을 쓴다. |
| "행동 실행 중에는 새 판을 시작할 수 없습니다" | 행동의 `Run` 안에서 `GBibleRuntime.NewRun`을 불렀다. 버튼처럼 행동 밖에서 부르고, 새 판 내용은 그 뒤 행동으로 만든다. |
| 저장 변환 표를 적었는데 적용되지 않음 | 버전이 저장 파일의 버전보다 크지 않다(이미 그 버전으로 저장된 파일에는 돌지 않는다). 또는 키가 저장 파일의 예전 이름과 다르다. |
