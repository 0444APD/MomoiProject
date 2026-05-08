# Unity Game Systems

다른 Unity 프로젝트에 드래그&드롭으로 이식할 수 있는 독립형 공용 시스템 라이브러리입니다.

---

## 목차

- [설치](#설치)
- [의존성](#의존성)
- [대시보드 (Game Systems Dashboard)](#대시보드)
- [시스템 목록](#시스템-목록)
  - [Events — 이벤트 버스](#events--이벤트-버스)
  - [SaveManager — 세이브 시스템](#savemanager--세이브-시스템)
  - [Logs — 런타임 로그](#logs--런타임-로그)
  - [Achievements — 도전과제](#achievements--도전과제)
  - [LanguageManager / TextTable — 다국어 텍스트](#languagemanager--texttable--다국어-텍스트)
  - [LocalizedText — 자동 텍스트 컴포넌트](#localizedtext--자동-텍스트-컴포넌트)
  - [UIDragController — UI 드래그](#uidragcontroller--ui-드래그)
  - [SystemInitializer — 자동 초기화](#systeminitializer--자동-초기화)
- [옵셔널 시스템](#옵셔널-시스템)
  - [Inventory — 인벤토리](#inventory--인벤토리)
  - [Dialogue — 대화 시스템](#dialogue--대화-시스템)

---

## 설치

1. `Assets/Code/System/` 폴더를 대상 프로젝트의 `Assets` 안 원하는 위치에 복사합니다.
2. Unity 메뉴 **Game Systems → Dashboard** 를 열면 홈 패널이 표시됩니다.
3. 홈 패널의 **"시스템 활성화"** 버튼을 눌러 `GAME_SYSTEMS_READY` 심볼을 추가합니다.  
   → 이후 모든 탭이 잠금 해제됩니다.

---

## 의존성

| 패키지 | 필수 여부 | 대응 심볼 |
|---|---|---|
| [Newtonsoft.Json (com.unity.nuget.newtonsoft-json)](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@latest) | SaveManager 사용 시 필수 | `NEWTONSOFT_JSON` |
| [Addressables (com.unity.addressables)](https://docs.unity3d.com/Packages/com.unity.addressables@latest) | TextTable·Inventory 사용 시 필수 | `Addressable` |
| [Odin Inspector](https://odininspector.com/) | TextTable·UIDragController 사용 시 필수 | `ODIN_INSPECTOR` |
| [TextMesh Pro](https://docs.unity3d.com/Manual/com.unity.textmeshpro.html) | LocalizedText 사용 시 필수 | — |

> 패키지가 없는 환경에서는 해당 심볼이 없으므로 `#if` 블록이 컴파일에서 제외됩니다.  
> Odin·Addressables 심볼은 대시보드 **홈 패널**의 패키지 감지 버튼으로 자동 등록할 수 있습니다.

---

## 대시보드

**메뉴 경로:** `Game Systems → Dashboard`

에디터 전용 창으로, 각 시스템의 상태 모니터링과 버튼 제어를 지원합니다.

- 상단 탭에서 시스템을 전환합니다.
- `GAME_SYSTEMS_READY` 심볼이 없으면 홈 탭만 열리고 나머지는 🔒 표시됩니다.
- 플레이 모드 중 실시간으로 자동 갱신됩니다.
- 우측 상단 **↺** 버튼으로 패널 목록을 수동 새로고침할 수 있습니다.

### 새 패널 추가 방법

`ISystemPanel` 인터페이스를 구현하는 클래스를 `Editor/Panels/` 에 추가하면 대시보드에 자동 등록됩니다. 기존 파일을 수정할 필요가 없습니다.

```csharp
public class MyPanel : ISystemPanel {
    public string Title => "내 시스템";
    public int    Order => 99;          // 탭 순서 (낮을수록 왼쪽)

    public void OnEnable()  { }
    public void OnDisable() { }
    public void OnGUI()     { EditorGUILayout.LabelField("Hello!"); }
}
```

---

## 시스템 목록

---

### Events — 이벤트 버스

**파일:** `Events.cs`

타입 안전한 전역 이벤트 버스입니다. 이벤트 데이터를 `ref`로 전달하므로 구독자가 값을 직접 수정하고, 발행자가 최종 결과를 회수할 수 있습니다.

#### 기본 흐름

**1. 이벤트 타입 정의**  
`struct` 에 `[GameEvent]` 어트리뷰트를 붙이면 대시보드 패널에 자동 등록됩니다.

```csharp
[GameEvent]
public struct PlayerDamageEvent {
    public float BaseDamage;
    public float FinalDamage;
}
```

**2. 구독**

```csharp
public class Equipment : MonoBehaviour {
    private void OnEnable()  => Events.Register<PlayerDamageEvent>(this, OnDamage);
    private void OnDisable() => Events.Cancel<PlayerDamageEvent>(this);

    private void OnDamage(ref PlayerDamageEvent e) {
        e.FinalDamage += e.BaseDamage * 0.1f;   // 10% 추가 피해
    }
}
```

**3. 발행 및 결과 회수**

```csharp
var evt    = new PlayerDamageEvent { BaseDamage = 50f, FinalDamage = 50f };
var result = Events.Trigger(evt);   // 구독자들이 FinalDamage를 수정
ApplyDamage(result.FinalDamage);    // 최종값 사용
```

#### API

| 메서드 | 설명 |
|---|---|
| `Events.Register<T>(observer, action)` | 이벤트 구독 (같은 observer 중복 등록 시 에러 로그) |
| `Events.Cancel<T>(observer)` | 특정 observer의 구독 해제 |
| `Events.Cancel<T>(action)` | 특정 액션의 구독 해제 |
| `Events.Trigger<T>(eventArgs)` | 이벤트 발행, 최종 수정된 struct 반환 |

> 파괴된 `UnityEngine.Object` observer는 Trigger 시점에 자동으로 정리됩니다.

---

### SaveManager — 세이브 시스템

**파일:** `SaveManager.cs`  
**필수:** `NEWTONSOFT_JSON` 심볼

슬롯 기반 JSON 세이브 시스템입니다. `SaveData` partial 클래스를 각 시스템 파일에서 확장하는 방식으로 데이터를 추가합니다.

#### 저장 경로

| 환경 | 경로 |
|---|---|
| 에디터 | `Assets/SaveData/SaveData_{slot}.json` |
| 빌드 | `Application.persistentDataPath/SaveData/SaveData_{slot}.json` |

#### 사용법

```csharp
// 게임 시작 시 로드 (SystemInitializer가 자동으로 슬롯 0 로드)
SaveManager.Load(0);

// 데이터 접근
SaveManager.Current.someValue = 42;

// 저장
SaveManager.Save();
```

#### SaveData 확장

다른 시스템의 데이터를 추가할 때는 `partial class` 를 별도 파일에 작성합니다.

```csharp
// SaveData.MySystem.cs
public partial class SaveData {
    public int playerLevel = 1;
    public List<string> unlockedItems = new();
}
```

#### 수동 모드

`SaveManager.isManualSaveLoad = true` 로 설정하면 `SystemInitializer`의 자동 로드를 건너뜁니다.

#### 에디터 메뉴

- `Tools/Save System/Load Slot 0` — 에디터에서 0번 슬롯 로드
- `Tools/Save System/Save Slot 0` — 플레이 중 0번 슬롯 저장

---

### Logs — 런타임 로그

**파일:** `Logs.cs`

이벤트 기반으로 수치를 자동 수집하는 런타임 통계 시스템입니다. 런 단위 기록(`RunLog`)과 누적 기록(`TotalLog`)을 분리 관리합니다.

#### 로그 타입

| 타입 | 설명 | 예시 |
|---|---|---|
| `LogMergeType.Add` | 런 간 합산 | 총 가한 피해량 |
| `LogMergeType.Max` | 최고치 보존 | 최대 콤보 수 |

#### 사용법

**1. 로그 정의**  
`[LogProvider]` 클래스 안에 `LogDefinition<TEvent>` 를 `static readonly` 필드로 선언합니다.

```csharp
[LogProvider]
public static class MyLogs {
    public static readonly LogDefinition DamageDealt =
        new LogDefinition<PlayerDamageEvent>(
            "TotalDamageDealt", LogMergeType.Add, e => e.FinalDamage);

    public static readonly LogDefinition MaxCombo =
        new LogDefinition<PlayerComboEvent>(
            "MaxComboRecord", LogMergeType.Max, e => e.ComboCount);
}
```

**2. 초기화**

```csharp
Logs.AddDefinition(MyLogs.DamageDealt);
Logs.AddDefinition(MyLogs.MaxCombo);
Logs.Initialize();   // 이벤트 자동 구독 시작
```

**3. 런 종료 처리**

```csharp
Logs.Settle();   // RunLog → TotalLog 합산, RunLog 초기화, 히스토리 저장
```

#### 데이터 접근

```csharp
float totalDmg   = Logs.TotalLog.SumLogs["TotalDamageDealt"];
float maxCombo   = Logs.TotalLog.MaxLogs["MaxComboRecord"];
float runDmg     = Logs.RunLog.SumLogs["TotalDamageDealt"];
var   lastRun    = Logs.RunHistory[^1];   // 최근 런 (최대 5개 보관)
```

---

### Achievements — 도전과제

**파일:** `Achievements.cs`

이벤트 기반 도전과제 시스템입니다. 달성 시 `AchievementUnlockedEvent` 를 발행하므로 UI 연동이 용이합니다.

#### 도전과제 종류

| 클래스 | 설명 |
|---|---|
| `ConditionAchievement<TEvent>` | 이벤트 조건이 충족되면 즉시 달성 |
| `ProgressAchievement<TEvent>` | 누적 수치가 목표치에 도달하면 달성 |

#### 사용법

**1. 도전과제 정의**  
`[AchievementProvider]` 클래스 안에 선언합니다.

```csharp
[AchievementProvider]
public static class MyAchievements {
    // 레벨 10 달성 시 즉시 해금
    public static readonly AchievementDefinition ReachLevel10 =
        new ConditionAchievement<PlayerLevelUpEvent>(
            "ReachLevel10",
            e => e.NewLevel >= 10,
            () => Debug.Log("보상 지급!")   // 선택사항
        );

    // 총 피해량 10,000 누적 시 해금
    public static readonly AchievementDefinition Deal10kDamage =
        new ProgressAchievement<PlayerDamageEvent>(
            "Deal10kDamage",
            10000f,
            e => e.FinalDamage
        );
}
```

**2. 초기화**

```csharp
Achievements.LoadData(SaveManager.Current?.achievementData); // 세이브 데이터 주입 (없으면 생략)
Achievements.AddDefinition(MyAchievements.ReachLevel10);
Achievements.AddDefinition(MyAchievements.Deal10kDamage);
Achievements.Initialize();   // 미달성 항목만 이벤트 구독
```

**3. 달성 이벤트 수신**

```csharp
Events.Register<AchievementUnlockedEvent>(this, OnAchievementUnlocked);

void OnAchievementUnlocked(ref AchievementUnlockedEvent e) {
    ShowPopup(e.AchievementKey);
}
```

---

### LanguageManager / TextTable — 다국어 텍스트

**파일:** `LanguageManager.cs`, `TextTable.cs`  
**필수:** Odin Inspector, Addressables

구글 스프레드시트 CSV를 베이크(Bake)해 ScriptableObject에 저장하고, 런타임에 키로 텍스트를 조회하는 다국어 시스템입니다.

#### 초기 설정

1. `Assets` 우클릭 → `Create/Core/Text Table` 로 `TextTable` SO를 생성합니다.
2. 인스펙터에서 시트 탭 정보(이름, CSV 공개 링크)를 입력합니다.
3. **"구글 시트 데이터 가져오기 (Bake)"** 버튼을 눌러 데이터를 SO에 저장합니다.
4. 해당 SO를 Addressables에 `TextTable` 주소로 등록합니다.

#### CSV 형식

| ID | Kr | En |
|---|---|---|
| `##UI` | _(그룹 prefix 선언)_ | |
| `btn_start` | 시작 | Start |
| `btn_exit` | 종료 | Exit |

- `##그룹명` 행은 이후 ID에 `그룹명_` prefix를 자동으로 붙입니다 → `UI_btn_start`
- `//` 로 시작하는 행은 주석으로 무시됩니다.

#### 텍스트 조회

```csharp
// 기본 조회
string text = TextTable.Get("UI_btn_start");

// 인자 치환 ({arg} 패턴)
string msg = TextTable.Get("msg_welcome", new Dictionary<string, string> {
    { "name", "플레이어" }
});
// → "안녕하세요, 플레이어님!"  (원문: "안녕하세요, {name}님!")

// 키워드 치환 ([[keyword]] 패턴)
// 키워드 시트에 Gold = "<color=yellow>{Gold}</color>" 등록 시
string desc = TextTable.Get("item_desc");
// → "획득 시 <color=yellow>골드</color>가 증가합니다."
```

#### 언어 전환

```csharp
LanguageManager.ChangeLanguage("En");   // "Kr", "En" 등 시트 헤더명과 일치해야 합니다
```

---

### LocalizedText — 자동 텍스트 컴포넌트

**파일:** `LocalizedText.cs`  
**필수:** TextMesh Pro

`TextMeshProUGUI` 에 붙이면 언어 변경 시 자동으로 텍스트를 갱신하는 컴포넌트입니다.

#### 사용법

1. `TextMeshProUGUI` 가 있는 GameObject에 `LocalizedText` 컴포넌트를 추가합니다.
2. 인스펙터에서 **Text Key** 에 TextTable 키를 입력합니다.
3. 런타임 동적 변경이 필요하면 `SetDynamicText()` 를 호출합니다.

```csharp
// 런타임에서 동적으로 키와 인자 변경
localizedText.SetDynamicText("msg_welcome", new Dictionary<string, string> {
    { "name", playerName }
});
```

---

### UIDragController — UI 드래그

**파일:** `UIDragController.cs`  
**필수:** Odin Inspector

카드, 아이템 등 드래그 가능한 UI 요소에 붙이는 컴포넌트입니다. 히트박스와 비주얼을 분리해 부드러운 Lerp 이동을 구현합니다.

#### 주요 기능

| 기능 | 설명 |
|---|---|
| Hitbox / Visual 분리 | 히트박스는 정확한 위치, 비주얼은 Lerp로 부드럽게 따라옴 |
| 호버 확대 | 마우스 오버 시 지정 비율로 확대 |
| 틸트 효과 | 이동 속도에 따라 자연스러운 기울기 |
| 마그넷(Snap) | 지정 태그 오브젝트에 드래그 시 자동 흡착 |
| Free Move | 비활성화 시 드롭 위치가 유효하지 않으면 시작 위치로 복귀 |

#### 인스펙터 설정

| 필드 | 설명 |
|---|---|
| `hitboxRect` | 실제 클릭/드래그를 받는 RectTransform |
| `visualRect` | 화면에 보이는 RectTransform (Lerp로 hitbox 위치를 따라감) |
| `canMove` | 드래그 이동 허용 여부 |
| `canInteract` | 마우스 입력 처리 여부 |
| `isFreeMove` | `true` 이면 드롭 위치 유효성 무관하게 그 자리에 고정 |
| `canMagnet` | 마그넷 흡착 기능 활성화 |
| `magnetTags` | 흡착 대상 태그 목록 (기본: `"Slot"`, `"Tile"`) |
| `hoverParent` | 호버/드래그 중 비주얼이 임시로 이동할 부모 Transform (렌더 순서 제어용) |

#### 이벤트

```csharp
var drag = GetComponent<UIDragController>();

drag.OnClickEvent       += ctrl => Debug.Log("클릭!");
drag.OnBeginDragEvent   += ctrl => Debug.Log("드래그 시작");
drag.OnEndDragEvent     += ctrl => Debug.Log("드래그 종료");
drag.OnMagnetEnterEvent += (ctrl, magnet) => Debug.Log($"{magnet.name} 에 진입");
```

#### 코드 제어

```csharp
drag.SetPosition(newPos);          // canMove 가 true 일 때만 이동
drag.ForceSetPosition(newPos);     // 조건 무관 강제 이동 (visual 즉시 동기화)
drag.ForceSnapToMagnet(slotObj);   // 코드에서 특정 슬롯에 스냅
drag.ClearMagnetSnap();            // 스냅 상태 초기화
drag.ForceHoverExit();             // 카드 파괴 등 예외 상황 시 hover 상태 강제 해제
```

---

### SystemInitializer — 자동 초기화

**파일:** `SystemInitializer.cs`

`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 를 사용해 씬 로드 전에 자동으로 실행됩니다. MonoBehaviour 없이도 동작합니다.

**초기화 순서:**
1. Addressables에서 `"TextTable"` 주소의 SO를 동기 로드 후 `TextTable.InitRuntime()` 호출
2. `SaveManager.isManualSaveLoad` 가 `false` 이면 슬롯 0 자동 로드

> TextTable SO를 Addressables에 `TextTable` 주소로 등록하지 않으면 에러 로그가 출력됩니다.

---

## 옵셔널 시스템

`Optional/` 폴더 안의 시스템은 별도 스크립팅 심볼로 활성화합니다. 대시보드 홈 패널 또는 `Player Settings → Scripting Define Symbols` 에서 추가합니다.

---

### Inventory — 인벤토리

**폴더:** `Optional/Inventory/`  
**활성화 심볼:** `SYSTEM_INVENTORY`  
**필수:** Addressables (`Addressable` 심볼)

슬롯 기반 인벤토리 시스템입니다. 아이템 데이터는 `ItemSO` ScriptableObject로 정의하며, Addressables `"Items"` 레이블로 일괄 로드됩니다.

#### 초기화

`SaveManager.Load()` 이후, 씬 로드 전에 호출합니다.

```csharp
await InventorySystem.InitializeAsync();
```

#### 인벤토리 생성 및 접근

```csharp
// Inventory 컴포넌트를 원하는 GameObject에 추가하면 자동 등록됨
var bag = InventorySystem.GetInventory("PlayerBag");
```

#### 세이브 연동

```csharp
// 저장 전 호출
InventorySystem.FlushToSaveData();
SaveManager.Save();
```

#### 아이템 정의 (ItemSO)

`Create/Core/Item SO` 메뉴로 생성합니다.  
생성한 SO를 Addressables에 `"Items"` 레이블로 등록해야 시스템이 인식합니다.

---

### Dialogue — 대화 시스템

**폴더:** `Optional/Dialogue/`  
**활성화 심볼:** `SYSTEM_DIALOGUE`  
**필수:** Odin Inspector

Timeline 기반 대화 시스템입니다. 캐릭터 정보는 `CharacterDialogueSO` 로 관리합니다.

#### 캐릭터 설정

`Create/Core/Character Dialogue SO` 메뉴로 생성합니다.

| 필드 | 설명 |
|---|---|
| `defaultName` | 대화창에 표시할 기본 캐릭터 이름 |
| `expressions` | 표정 ID → 스프라이트 딕셔너리 (예: `"Normal"`, `"Smile"`, `"Angry"`) |

```csharp
Sprite sprite = characterSO.GetExpression("Smile");
```

#### Timeline 마커

`UniversalMarker` 를 Timeline 트랙에 배치해 대화 이벤트를 구성합니다. 씬 로드 시 `TimelineAutoValidator` 가 마커 유효성을 자동으로 검사합니다.

---

## 라이선스

이 라이브러리는 개인 및 상업 프로젝트에서 자유롭게 사용할 수 있습니다.
