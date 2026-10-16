# 39. 에셋 관리 — 넣을 것 · 가져오기 · 바꾸기 · 안 쓰는 것 · 폴더 정돈 · 스파인

**한 줄 요약**: 그림·소리·글꼴 같은 리소스는 개발자가 가져온다. AI는 필요한 곳에 **임시 에셋**(단색 사각형·무음)을 최종 경로에 만들고 **넣을 것** 목록(`AssetRequests`)에 올린다. 개발자는 메뉴 **MomoiProject › 에셋 관리**에서 그 줄에 진짜 파일을 고른다 — **같은 경로에 내용을 덮으므로 GUID가 그대로**라 그 에셋을 쓰는 씬·프리팹·DB가 모두 새 것을 쓴다. 같은 창에서 종류별 목록·쓰는 곳·바꾸기·안 쓰는 것(옮김, 지우지 않음)·폴더 정돈을 한다. 스파인 캐릭터(외부 런타임 패키지 + 내보낸 파일)를 들이는 법은 5절.

이 문서에서 쓰는 용어:

| 용어 | 뜻 |
|---|---|
| 넣을 것 | AI가 올린 에셋 요청 한 줄(경로 · 쓰는 곳 · 크기 · 느낌 · 넣었나). `Assets/MomoiProject/Data/AssetRequests.json`. |
| 임시 에셋 | 요청 경로에 AI가 만든 자리(그림 = 단색 PNG를 Sprite로, 소리 = 무음 WAV). |
| 바꾸기(교체) | 에셋 파일의 내용을 다른 파일로 덮는다. 경로·GUID·.meta는 그대로. |
| 종류별 폴더 | 그림 `Assets/Dev/Art` · 소리 `Assets/Dev/Audio` · 글꼴 `Assets/Dev/Fonts` · 모델 `Assets/Dev/Models` · 프리팹 `Assets/Dev/Prefabs` · 씬 `Assets/Dev/Scenes` · 머티리얼 `Assets/Dev/Materials` · 애니메이션 `Assets/Dev/Animations` · UI `Assets/Dev/UI` · 영상 `Assets/Dev/Video`. |

---

## 1. 동작 규칙

- **관리 대상**(목록 탭): `Assets/` 아래 에셋 중 시스템 폴더 `Assets/MomoiProject` 전체·`Assets/Plugins`·`Assets/StreamingAssets`·`Assets/Editor`·`Assets/Gizmos`·`Assets/TextMesh Pro`·`Assets/Settings`·`Assets/Dev/_Unused`·어디든 `Resources`·`Editor` 폴더 안·코드(`.cs`·`.asmdef`·`.asmref`)를 뺀 것.
- **종류**는 확장자로 정한다(`AssetTools.KindOf` — 그림 png·jpg·psd … / 소리 wav·mp3·ogg … / 글꼴 ttf·otf … / 모델 · 프리팹 · 씬 · 머티리얼 · 애니메이션 · UI uxml·uss · 영상, 그 밖은 기타).
- **가져오기**(`AssetTools.Import`): 밖의 파일·폴더를 종류별 폴더로 복사한다(폴더는 안의 파일을 하나씩, `.meta`는 빼고). 같은 이름이 있으면 `이름 1.png`처럼 번호를 붙인다.
- **바꾸기**(`AssetTools.Replace`): 같은 종류만(그림 ↔ 그림, 기타는 같은 확장자만 — 어기면 "종류가 다릅니다"·"확장자가 다릅니다" 예외). 확장자가 다르면 먼저 `AssetDatabase.MoveAsset`으로 확장자를 바꿔(GUID 유지) 내용을 덮는다. '여러 개로 자르기'인데 잘린 칸이 하나뿐인 스프라이트는 한 장짜리로 바꾼다(옛 그림 크기로 잘리지 않게 — `SingleSprite`). 넣을 것 목록에 있으면 넣음(✓)으로 표시한다.
- **넣을 것의 상태는 저절로 정해진다**(`AssetRequests.StateOf`): 줄마다 그 자리 에셋의 GUID를 적어 두고 지금 경로를 GUID로 찾는다(개발자가 옮기거나 확장자를 바꿔도 같은 줄 — `PathOf`). 파일이 없으면 '파일 없음', 종류가 다르면 '종류가 다릅니다', AI가 만든 임시 파일과 내용이 같으면 '아직 임시 에셋', 다르면 넣음(✓). 목록만 올린 줄(`Request`)은 맞는 종류의 파일이 생기면 넣음. 그러니 AI가 넣음 표시를 손으로 고칠 일은 없다. 파일이 없는 줄의 '파일 넣기…'는 요청한 자리에 넣은 파일의 확장자로 가져온다(`AssetRequests.Put`).
- **프리팹은 AI가 만든다**(개발자 2026-10-16, 관례 6절): 개발자는 리소스(스프라이트 시트 · 애니메이션 클립 · 소리)를 종류별 폴더에 두고 프리팹의 양식(무엇이 붙나)을 말로 준다. AI가 반영 스크립트로 `Assets/MomoiProject/Prefabs/`(시스템 폴더 — 에셋 관리 목록 밖)에 만들고 리소스를 적용한다(3절 예). 개발자는 프리팹을 만지지 않고, 바꿀 것은 말로 피드백한다. `Assets/Dev/Prefabs`는 개발자가 따로 가져온 프리팹(에셋 스토어 등)의 자리다.
- **쓰는 곳**(`AssetTools.UsedBy`): 모든 에셋의 의존(`AssetDatabase.GetDependencies`)을 거꾸로 묶은 표. 프로젝트가 바뀌면 다시 만든다.
- **안 쓰는 것**(`AssetTools.Unused`): 개발자 폴더(`Assets/Dev`) 안에서, 뿌리(빌드 목록의 씬 · `Assets/Dev` 안의 모든 씬(빌드 목록에 아직 없어도) · `Assets/MomoiProject`(DB 데이터·UI·AI 씬) · `Resources` 안 · 그 밖의 DB 항목 · Addressables 항목(쓰면) · 넣을 것 목록)에서 닿지 않는 그림·소리·글꼴·모델·프리팹·머티리얼·애니메이션·영상. 코드가 든 폴더(가져온 패키지)는 보지 않는다. **코드가 경로 글자로 불러오는 것은 연결이 보이지 않는다** — 옮기기 전에 묻는 창에서 확인한다. 옮기면 `Assets/Dev/_Unused/<원래 경로>`(지우지 않음)이고, 옮긴 목록이 `MomoiProjectBackups/<시각>-unused/moves.tsv`에 남아 '마지막 옮기기 되돌리기'로 되돌린다.
- **폴더 정돈**(`AssetTools.Misplaced` · `Tidy`): 개발자 폴더(`Assets/Dev`) 안에서 종류별 폴더 밖의 에셋을 그 폴더로 옮긴다(`MoveAsset` — GUID 유지). 하위 폴더 모양은 그대로 둔다(`Assets/Dev/Cards/Fire/a.png` → `Assets/Dev/Art/Cards/Fire/a.png`, 다른 종류 폴더 안이면 그 폴더 이름만 뺀다). `Assets/Dev` 밖(시스템 폴더·패키지·Plugins·에셋 스토어 폴더)과 코드가 든 폴더(가져온 패키지)·씬 옆 같은 이름 폴더(라이트맵 등)·`Resources`·`Editor`·Addressables 폴더 항목은 건드리지 않는다. 옮기기 전에 몇 개인지와 앞의 몇 개를 보여 묻고, 옮긴 목록(원래 경로 → 새 경로)을 `MomoiProjectBackups/<시각>-tidy/moves.tsv`에 남긴다 — '마지막 정돈 되돌리기'(`AssetTools.UndoMoves`)로 되돌린다.

## 2. 에디터에서 쓰는 법

메뉴 **MomoiProject › 에셋 관리**: 탭 넣을 것(줄마다 '파일 넣기…'·'다시 넣기…'·'위치') · 목록(종류별, '쓰는 곳 n'·'바꾸기…'·'위치') · 안 쓰는 것('찾기' → '모두 _Unused로 옮기기'·줄마다 '옮기기'·'마지막 옮기기 되돌리기') · 폴더 정돈('모두 정돈'·줄마다 '옮기기'·'마지막 정돈 되돌리기'). 탐색기에서 파일·폴더를 창에 끌어 놓으면 가져오기.

## 3. 코드로 쓰는 법(AI — DB 반영 스크립트에서)

```csharp
[DbSeed("1")]
static void Art()
{
    AssetRequests.Sprite("Assets/Dev/Art/Cards/fireball.png", 256, 256, "카드 '파이어볼' 그림", "주황 불꽃, 카드 틀 안쪽");
    AssetRequests.Audio("Assets/Dev/Audio/Sfx/hit.wav", "맞을 때 효과음", seconds: 0.3f);
    AssetRequests.Request("Assets/Dev/Fonts/Main.ttf", "모든 화면의 한글 글꼴", note: "가독성 좋은 고딕");   // 임시 파일 없이 목록만
}
```

- 이미 파일이 있으면(개발자가 옮겼거나 확장자를 바꿨어도 — GUID로 찾는다) 임시 에셋을 만들지 않는다(개발자가 넣은 것을 덮지 않는다). 같은 경로를 다시 부르면 설명만 고친다. 그래서 반영 스크립트가 다시 돌아도 안전하다.
- 임시 에셋을 화면·DB가 바로 쓰게 둔다(스프라이트 칸에 그 경로의 에셋을 넣는다). 진짜 파일이 들어와도 연결을 다시 할 필요가 없다. 함수는 지금 경로를 돌려준다(임시 그림은 그 자리에서 가져오므로 바로 읽힌다). 원본 인스턴스의 스프라이트 칸에 넣는 예(새 항목을 만들 때 — 04장 1.1):
  ```csharp
  var art = AssetRequests.Sprite("Assets/Dev/Art/Cards/fireball.png", 256, 256, "카드 '파이어볼' 그림");
  var data = (InstanceData)EditorDatabase.Staging.Add(typeof(InstanceData), "fireball");
  data.instanceType = typeof(CardDef).FullName;
  data.value = new CardDef { cost = 1, art = AssetDatabase.LoadAssetAtPath<Sprite>(art) };   // CardDef.art: Sprite 칸
  ```
- 임시 그림은 Sprite(한 장짜리)로 가져온다. 임시 소리 길이는 `seconds`(기본 0.5초).

**프리팹 만들기** — 개발자가 말로 준 양식대로(예: 캐릭터 = 몸 스프라이트 + 애니메이터(대기 · 걷기 · 공격) + 발밑 원 충돌체). 리소스 경로는 개발자가 둔 자리(없으면 위 `AssetRequests`로 임시 자리를 먼저).

```csharp
// Assets/MomoiProject/Code/Editor/Seeds/PrefabSeeds.cs
using GBible; using GBible.Data; using GBible.EditorTools;
using UnityEditor; using UnityEditor.Animations; using UnityEngine;

static class PrefabSeeds
{
    [DbSeed("1")]   // 피드백으로 양식이 바뀌면 "2" — 같은 경로에 다시 만든다(GUID 그대로, 씬에 놓인 것도 따라온다)
    static void Hero()
    {
        const string folder = "Assets/MomoiProject/Prefabs";
        EditorDatabase.EnsureFolder(folder);
        var root = new GameObject("Hero");
        root.AddComponent<SpriteRenderer>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Dev/Art/Hero/hero.png");
        var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Hero.controller");
        foreach (var name in new[] { "Idle", "Walk", "Attack" })                       // 개발자가 둔 애니메이션 클립
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>($"Assets/Dev/Animations/Hero/{name}.anim") is { } clip) controller.AddMotion(clip);
        root.AddComponent<Animator>().runtimeAnimatorController = controller;
        root.AddComponent<CircleCollider2D>().radius = 0.3f;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Hero.prefab");
        Object.DestroyImmediate(root);
        if (EditorDatabase.Instance.FindById("heroPrefab") != null) return;
        ((PrefabData)EditorDatabase.Staging.Add(typeof(PrefabData), "heroPrefab")).prefab = prefab;
    }
}
// 화면 코드(ViewList의 프리팹): Game.Entry<PrefabData>(GameIds.Prefab.HeroPrefab).prefab
```

- 프리팹은 AI 것이라 고칠 때 '적용 전'을 거치지 않는다(DB 항목이 아니라 에셋 — 반영 스크립트가 다시 돌면 바로 바뀐다). DB › 프리팹 항목은 처음 한 번만 만든다.
- 크기 · 위치처럼 숫자가 프리팹에 박히는 것도 양식의 일부다 — 개발자가 바꾸고 싶다고 하면 그 값을 양식에 반영해 다시 만든다(개발자가 프리팹을 직접 고치지 않으므로 덮어써도 잃는 것이 없다).

| 멤버 | 하는 일 |
|---|---|
| `AssetRequests.Sprite(path, w, h, 쓰는 곳, 느낌)` · `Audio(path, 쓰는 곳, seconds, 느낌)` · `Request(path, 쓰는 곳, 크기, 느낌)` | 임시 에셋 + 넣을 것(`Request`는 임시 파일 없이 목록만) |
| `AssetRequests.Load()` · `PathOf(줄)` · `StateOf(줄)` · `Refresh()` · `Put(줄, 파일)` · `MarkDone(path)` · `Remove(path)` | 목록 · 지금 경로 · 상태 · 목록을 지금 에셋에 맞추기 · 파일 넣기 · 넣음 표시(바꾸기가 부른다) · 줄 지우기(파일은 그대로) |
| `AssetTools.Import(paths, folder)` · `Replace(asset, file)` · `UsedBy(asset)` · `Unused()` · `MoveToUnused(paths)` · `Misplaced()` · `Tidy(moves)` · `LastMoveLog(tag)` · `UndoMoves(log)` · `FolderFor(kind)` · `KindOf(path)` · `EnsureFolder(folder)` | 가져오기·바꾸기·찾기·옮기기·되돌리기(`tag` = `"unused"`·`"tidy"`) |

## 4. 주의할 점

- 바꾸기는 되돌리기(Undo)가 없다. 원래 파일이 필요하면 Git으로 되돌린다.
- 넣을 것 목록 파일은 프로젝트와 함께 커밋한다(누가 무엇을 넣어야 하는지 남는다).
- 스프라이트를 잘라 둘 크기는 용도마다 틀 파일 하나의 크기 표로 정한다: Project 창에서 우클릭 › Create › MomoiProject › 스프라이트 틀로 만들고 인스펙터에서 채운다(27장 2절).

## 5. 스파인 들이기(Spine — 외부 런타임)

스파인 캐릭터는 개발자가 스파인 에디터에서 내보낸 파일로 들어온다. AI는 런타임 패키지를 들이고(내려받기라 먼저 묻는다 — 관례 0절), 가져오기 설정을 맞추고, 띄워서 확인한다.

스파인 에디터가 없어도 내보낸 파일만으로 할 수 있는 것: 재생 · 섞기 · 스킨 · 부착물 바꾸기 · 이벤트 · 뼈 따라가기 · 조종 뼈 움직이기.

에디터가 있어야 하는 것: 뼈 · 메시 · 키 고치기, 다시 내보내기(버전 · 알파 방식 · 아틀라스).

아래의 버전 숫자는 **2026-10 기준 예**다. 버전이 바뀌면 굵은 글씨의 규칙을 따르고, 숫자는 그때 다시 확인한다.

### 5-1. 버전 호환 — 규칙은 그대로, 숫자는 바뀐다

- **런타임은 파일과 앞 두 자리(major.minor)가 같아야 한다.**
  - 파일 버전은 JSON 맨 앞 `"skeleton": { … "spine": "4.2.43" }`에서 본다. 바이너리는 머리 몇 바이트 뒤의 글자(`4.2.33`)다.
  - 끝자리(patch)는 달라도 된다.
- **런타임마다 지원하는 Unity 범위가 다르다.** 받기 전에 공식 다운로드 페이지(`esotericsoftware.com/spine-unity-download`)에서 그 런타임의 지원 범위와 최신 날짜를 확인한다.
- **고르는 순서:**
  1. 파일 버전의 런타임이 프로젝트의 Unity를 지원하면 그것을 쓴다.
  2. 지원하지 않으면, 개발자가 스파인 에디터로 지원되는 버전으로 다시 내보낼 수 있는지 묻는다. 그러면 고칠 것이 없다.
  3. 파일만 있으면 그 런타임을 들여와 고친다(5-3).
- **런타임 버전마다 기본값이 다르다**(알파 방식 등). 넣을 때마다 5-4로 확인한다.
- **버전을 올릴 때:**
  - 파일을 새 에디터로 다시 내보내고, manifest 주소 끝(`#x.y`)을 바꾼다.
  - 5-3으로 고친 사본(`Packages/` 안)이 있으면, 새 버전이 그 Unity를 지원하는지 보고 사본을 지운 뒤 git 주소로 되돌린다.
- 기록(2026-10, 다운로드 페이지와 실험실 확인):

| 런타임 | 공식 지원 Unity | 알파 기본 | Unity 6000.6에서 |
|---|---|---|---|
| 4.3 | 6000.4까지 | 스트레이트 | 확인 안 함 |
| 4.2 | 6000.3까지 | PMA | 4.2.120은 컴파일 오류 → 5-3으로 고쳐 씀(UI 실험실) |

### 5-2. 설치(AI)

1. [필수] 먼저 묻는다: 받을 것(git 주소 두 개) · 버전 · 넣을 프로젝트.
2. `Packages/manifest.json`의 `dependencies`에 두 줄을 넣는다(`<버전>` = `4.2` · `4.3` …):
   ```json
   "com.esotericsoftware.spine.spine-csharp": "https://github.com/EsotericSoftware/spine-runtimes.git?path=spine-csharp/src#<버전>",
   "com.esotericsoftware.spine.spine-unity": "https://github.com/EsotericSoftware/spine-runtimes.git?path=spine-unity/Assets/Spine#<버전>",
   ```
   unitypackage는 쓰지 않는다. `Assets/Spine` · `Assets/Spine Examples`가 생기기 때문이다(관례 2절).
3. **패키지는 GBible이 저절로 받는다**(`PackageSync`). Unity는 창이 앞에 있을 때만 manifest를 다시 읽는데, GBible이 manifest가 lock 파일보다 새로우면 창이 뒤에 있어도 다시 푼다(몇 초마다 · 요청 파일을 처리하기 전 — 38장 2-4·2-5). 프로젝트 코드가 컴파일되지 않아도 돈다. 다 들어왔는지는 `Logs/Editor.log`의 `[MomoiProject] … 패키지를 다시 풉니다`와 `Packages/packages-lock.json`에 새 줄이 생겼는지로 본다.
4. `Logs/Editor.log`에 `error CS`가 없는지 본다. 있으면 5-3.
5. [필수] 스파인은 처음 돌 때 `Assets/Editor/SpineSettings.asset`을 만든다. 경로가 스파인 코드에 고정돼 있어 옮기지 않는다(관례의 예외 — 프로젝트와 함께 커밋).

### 5-3. 지원 범위 밖 Unity에서 옛 런타임을 쓸 때 — 들여와 고친다

1. `Library/PackageCache/com.esotericsoftware.spine.spine-unity@…`를 `Packages/com.esotericsoftware.spine.spine-unity`로 복사한다(embedded 패키지 — 프로젝트와 함께 커밋). manifest에서 그 줄을 지우고 다시 푼다(5-2의 3).
2. **`error CS`만 고친다**(경고는 둔다). 대개 Unity가 오류로 막은 옛 API다(`CS0619 … obsolete`).
3. **고치는 법은 직접 짓지 않는다.** 같은 저장소의 새 버전 브랜치가 같은 줄을 어떻게 바꿨는지 보고 그대로 따른다. 새 줄은 `#if UNITY_xxxx_OR_NEWER` 쪽에, 옛 줄은 `#else` 쪽에 남긴다.
4. 다시 컴파일한다. spine-csharp(런타임 핵심)는 Unity API를 쓰지 않아 대개 고칠 것이 없으니 git 그대로 둔다.
5. 이 사본은 git으로 저절로 바뀌지 않는다. 버전을 올릴 때는 5-1을 따른다.

예(2026-10 · Unity 6000.6 · spine-unity 4.2.120 — `Object.GetInstanceID()`가 오류로 막힘, 4.3 브랜치를 따라 고침):

| 파일(`Runtime/spine-unity/…` · `Editor/spine-unity/Editor/…`) | 고친 것 |
|---|---|
| `Components/SkeletonMecanim.cs` | `AnimationClipEqualityComparer` → `GetEntityId()` 비교 · `GetHashCode()` |
| `Asset Types/SpineAtlasAssetInspector.cs`(2곳) · `Components/SkeletonGraphicInspector.cs` | `GetAssetPath(texture.GetInstanceID())` → `GetAssetPath(texture)` |
| `Utility/AssetUtility.cs` | `GetEntityId()` · `GetEntityId().IsValid()` · `EditorUtility.EntityIdToObject` |
| `Utility/DataReloadHandler.cs` | 표 키 `EntityId` · `GetEntityId()` |
| `Utility/SpineEditorUtilities.cs` | `HIERARCHY_WINDOW_ENTITY_ID_CALLBACK`(6000.4+) · `EntityIdType` 별칭 · 하이어라키 아이콘 표 키 · `hierarchyWindowItemByEntityIdOnGUI` · `DragAndDrop.AddDropHandlerV2` + `HandleDragAndDrop(EntityId …)` · `GetEntityOrInstanceId()` 확장 함수 |

이 개발 환경에서는 UI 실험실(`UITemplateLab`)의 `Packages/com.esotericsoftware.spine.spine-unity`가 이렇게 고친 4.2.120 사본이다. 같은 버전이면 복사해 쓴다.

### 5-4. 알파 — PMA인지 스트레이트인지

텍스처의 알파 방식과 가져오기 설정이 어긋나면 가장자리가 검거나 하얗게 뜬다. 기본값은 런타임 버전마다 다르다(5-1 기록).

- **아는 법:**
  - 4.x 아틀라스(`.atlas.txt`)에 `pma:true` 줄이 있으면 PMA, 없으면 스트레이트다.
  - 밖에서 받은 파일은 픽셀로 확인한다. 반투명 픽셀에서 RGB가 알파보다 큰 칸이 많으면 스트레이트다(PMA는 늘 RGB ≤ 알파).
- **파일을 넣기 전에** `Assets/Editor/SpineSettings.asset`을 고친다. 이 설정은 .meta가 없는 새 텍스처를 가져올 때만 적용된다.
  - 스트레이트:
    - `textureSettingsReference` = 패키지의 `Editor/spine-unity/Editor/ImporterPresets/StraightAlphaPreset.preset` 경로
    - `blendModeMaterialMultiply` · `Screen` · `Additive` = 패키지 `Runtime/spine-unity/SkeletonDataModifierAssets/BlendModeMaterials/`의 `SkeletonStraight…` 머티리얼(`{fileID: 2100000, guid: <.meta의 guid>, type: 2}`)
  - PMA: `textureSettingsReference`를 비우고(또는 `PMATexturePreset.preset`), 블렌드 머티리얼은 `SkeletonPMA…`.
- **확인:**
  - 스트레이트면 생긴 머티리얼에 `_StraightAlphaInput: 1`, 텍스처 .meta에 `sRGBTexture: 1` · `alphaIsTransparency: 1`이 있다.
  - 잘못 들어왔으면 생긴 에셋과 텍스처 .meta를 지우고 다시 넣는다.

### 5-5. 파일 넣기

개발자의 내보내기 설정은 가이드 06에 있다.

- 자리는 `Assets/Dev/Art/Spine/<이름>/`이다.
- 뼈대 데이터는 **하나만** 넣는다(`.skel.bytes` 또는 `.json`). 둘 다 넣으면 `_SkeletonData`가 두 벌 생긴다.
- 확장자가 `.skel` · `.atlas`면 `.skel.bytes` · `.atlas.txt`로 바꿔 넣는다. 그대로면 읽지 못한다.
- `.spine`(에디터 프로젝트 파일)은 넣지 않는다.
- 밖에서 받은 자료(다른 게임에서 나온 파일 등)는 저장소에 넣지 않는다(`.gitignore`).

### 5-6. 띄우기 · 재생 · 애니메이션 맵핑

- **띄우기.** 파일을 넣으면 `<이름>_SkeletonData.asset`이 생긴다.
  - 게임에서는 화면 · DB 칸 · 씬 오브젝트가 이 에셋을 참조로 가진다. 경로 글자로 찾지 않는다.
  - 코드로 띄울 때는 `SkeletonAnimation.NewSkeletonAnimationGameObject(데이터)`를 쓴다. 씬에 둘 때는 에셋을 하이어라키로 끌어 놓는다.
  - 월드 장면에 두는 것이다. 화면(UI Toolkit) 안에 그리는 것은 5-8.
- **트랙.**
  - 0번 트랙은 바탕이다. 시작을 한 번 틀고 기본 반복을 잇는다: `SetAnimation(0, 시작, false)` → `AddAnimation(0, 반복, true, 0)`.
  - 1번 위 트랙은 덧입히기다(조종 · 표정 · 반응). 켜고 끌 때는 섞는다(`SetEmptyAnimation` · `AddEmptyAnimation` + `MixDuration`). 그냥 바꾸면 한 프레임에 튄다.
- **애니메이션 맵핑 — 이름은 데이터.**
  - [필수] 코드는 역할(시작 · 기본 반복 · 조종 시작 · 조종 끝 · 반응 …)만 알고, 역할마다 어느 애니메이션을 쓸지는 캐릭터의 DB 칸에 둔다(관례 18절 — 개발자가 이름을 바꾸거나 다른 애니메이션으로 갈아 끼운다).
  - 애니메이션 · 뼈 · 스킨 이름은 데이터에서 읽는다: `데이터.GetSkeletonData(true)`의 `Animations` · `Bones` · `Skins`. 없는 이름이면 경고하고 첫 애니메이션으로 넘어간다.
  - 조종 뼈의 맵핑(뼈 → 따라가는 제약 → 세기를 올리고 내리는 애니메이션 → 범위)도 같은 칸에 둔다(5-7의 1).
- **확인용 테스트 명령.** 애니메이션마다 명령 하나를 저절로 만든다(`spine.play.<이름>` — 데이터의 `Animations`를 돌며 `session.Debug.Register`). 개발자가 이름을 몰라도 테스트 패널에서 모두 틀어 본다.
- 예: UI 실험실 `Code/Client/Lab/Spine/SpineLab.cs`(띄우기 · 시작 → 반복 · 애니메이션마다 명령 · 카메라 맞추기).

### 5-7. 조종 뼈 — 조이스틱식

조종 뼈는 다른 뼈들이 IK · transform 제약으로 따라가는 뼈다. 예: 고개와 얼굴이 따라가는 `Touch_Point`, 눈동자가 따라가는 `Touch_Eye`, 손이 따라가는 `HandFollow`. 이 뼈를 마우스로 움직인다.

**원칙: 고개는 훨씬 덜 돌린다**(개발자 2026-10-15).
- 조종 뼈의 범위를 작게 잡아 고개가 몇 도만 돌게 한다(CH0334는 약 ±5° — 아래 값).
- 범위를 크게 잡으면 IK 목표가 목 · 머리 회전축을 넘어가는 자리에 닿는다. 그러면 고개가 갑자기 꺾이거나 뒤집힌다.

1. **데이터에서 찾는다.**
   - 조종 뼈: 제약의 `target`이 되는 뼈.
   - 그 제약의 세기(mix)를 올리고 내리는 애니메이션(예: `Look_01_M` · `LookEnd_01_M`).
   - 세기가 셋업에 이미 들어 있는 뼈(시선)는 애니메이션이 필요 없다.
2. **입력 — 조이스틱.**
   - 가운데는 조종 뼈의 셋업 자리다.
   - 포인터까지의 거리를 **화면 px 반지름**(예: 160px)으로 나누고, 길이가 1을 넘으면 1로 자른다(원 테두리). 정사영 카메라면 `Screen.height / (orthographicSize × 2)`로 px를 유닛으로 바꾼다.
   - 그 값에 뼈가 움직일 범위를 곱한다. 범위는 방향마다 다르게 둘 수 있다(좌우 · 위 `reach`, 아래 `reachDown`). IK가 꺾이는 쪽만 좁힌다.
   - 반지름을 화면 px로 잡지 않으면 몇 px만 움직여도 끝까지 가서, 테두리 근처에서 방향이 확 바뀐다.
3. **출력 — 셋업 행렬로 로컬에 더한다.**
   - 시작할 때 `skeleton.SetToSetupPose()` → `UpdateWorldTransform(Physics.None)` → `parent.GetWorldToLocalMatrix`로 부모 행렬을 한 번 잡아 둔다.
   - 매 프레임 애니메이션이 적용된 뒤(`SkeletonAnimation.UpdateLocal`), 오프셋을 그 행렬로 바꿔 뼈의 로컬 x · y에 더한다. 애니메이션이 키를 준 뼈는 그 값에, 키가 없는 뼈는 처음 값에 더한다.
   - **지금 자세로 화면 자리를 고정하면(`bone.SetPositionSkeletonSpace`) 튄다.** 조종 뼈가 머리의 자식이면, 그 뼈가 IK로 머리를 돌리고 머리가 다시 그 뼈를 옮기는 되먹임이 생기기 때문이다.
4. **움직임 — 부드럽게(`SmoothDamp`), 섞어서(`MixDuration`).**
   - 오프셋은 `Vector2.SmoothDamp(현재, 목표, ref 속도, smoothTime, maxSpeed)`로 따라가게 하고(CH0334: `smoothTime` 0.25), 뼈마다 최고 속도를 둔다.
   - 누르면 조종 애니메이션을 위 트랙에 **섞어서** 켠다. 트랙이 비어 있으면 `SetEmptyAnimation(트랙, 0)` → `AddAnimation(…).MixDuration = mixTime`이다(CH0334: 0.3초). 그냥 `SetAnimation`하면 IK 세기가 한 프레임에 올라 잡는 순간 고개가 확 돈다.
5. **놓으면** 끝 애니메이션 + `AddEmptyAnimation`(같은 섞임 시간)을 튼다. 오프셋이 0으로 돌아오면 뼈를 원래 값으로 되돌린다(키가 없는 뼈는 옮긴 값이 남기 때문이다).
6. **범위 정하기.**
   - 고개 뼈: 방향마다 자리를 옮겨 가며 고개 각도를 재고, 각도가 갑자기 뛰기 전까지로 정한다. 그 안에서 원칙대로 작게 잡는다.
   - 시선 뼈: 눈동자가 눈 안에서 움직이는 거리로 정한다. 흰자와 눈동자의 폭 차이(아틀라스 크기 ÷ 아틀라스 `scale`)의 반쪽에서 70% 정도다.
7. **시선 뼈는 잡지 않는다.** 누르고 있는 동안 어디서든 포인터를 따라가고, 놓으면 돌아온다(개발자 2026-10-14).
8. 화면(UI) 칸 위의 포인터는 보내지 않는다(관례 21절).

- 예: UI 실험실 `Code/Client/Lab/Spine/`
  - `SpineDrag.cs`(구현)
  - `SpineDragMarkers.cs`(조종 뼈 표시 — 점 = 지금 자리, 십자 = 가운데, 고리 = 목표, 타원 = 범위, 흐린 원 = 조이스틱 반지름)
  - `SpineLab.cs`(테스트 명령 `spine.drag.*` · `spine.markers`)
  - **값을 맞출 때는 표시를 켜고 본다.**
- CH0334 값: 조이스틱 반지름 160px · `smoothTime` 0.25 · `mixTime` 0.3. 얼굴은 `reach` 1 · `reachDown` 0.5 · `maxSpeed` 1.2(고개 약 ±5°)이고, 시선은 `reach` 1(눈동자 약 9px)이다. 고개를 더 덜 돌리려면 얼굴 `reach` · `reachDown`을 줄인다.

### 5-8. 그 밖

- **코드에서 부르기:**
  - 스파인을 부르는 프로젝트 코드는 asmdef 참조에 `spine-unity` · `spine-csharp`를 더한다. 선택 기능 동기화는 GBible 모듈 참조만 고치므로 지워지지 않는다.
  - 다이얼로그 스탠딩(`SpineStandingView`)은 참조 없이 이름으로 찾는다(24장).
- **URP:** 기본 셰이더 `Spine/Skeleton`은 URP에서도 그린다(빛을 받지 않음). 빛을 받으려면 URP 셰이더 패키지(`com.esotericsoftware.spine.urp-shaders`)를 쓴다.
- **화면(UI Toolkit) 안에 그리기:** 4.3부터 UI Toolkit 확장(`com.esotericsoftware.spine.ui-toolkit`)이 있다(2026-10 기준 Unity 6000.4). 아직 써 보지 않았다. 확장이 없는 버전이면 월드 장면에 둔다. `SkeletonGraphic`(UGUI)은 UI 가이드가 UGUI를 들이지 않으므로 그때 묻는다.
- **확인:**
  - 테스트 명령으로 띄우고 플레이 요청 `capture`로 찍는다(38장 2-5).
  - 카메라는 데이터 범위(`SkeletonData.X · Y · Width · Height` × 크기)에 맞춘다.
  - 카메라 뼈가 든 장면형 데이터(로비 일러스트)는 애니메이션이 화면을 확대 · 이동한다. 오류가 아니다.
- **라이선스:** 스파인 런타임을 넣은 제품은 사용자마다 스파인 에디터 라이선스가 있어야 한다(Spine Runtimes License). 출시 전에 개발자에게 알린다.
