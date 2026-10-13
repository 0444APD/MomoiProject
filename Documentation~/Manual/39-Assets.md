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

- **관리 대상**: `Assets/` 아래 에셋 중 코드(`.cs`·`.asmdef`)·`Assets/MomoiProject/Code`·`Assets/MomoiProject/Data`·`Assets/MomoiProject/Generated`·`Plugins`·`StreamingAssets`·`Editor`·`Resources` 안·`Assets/Dev/_Unused`를 뺀 것.
- **가져오기**(`AssetTools.Import`): 밖의 파일·폴더를 종류별 폴더로 복사한다(폴더는 안의 파일을 하나씩, `.meta`는 빼고). 같은 이름이 있으면 `이름 1.png`처럼 번호를 붙인다.
- **바꾸기**(`AssetTools.Replace`): 같은 종류만(그림 ↔ 그림). 확장자가 다르면 먼저 `AssetDatabase.MoveAsset`으로 확장자를 바꿔(GUID 유지) 내용을 덮는다. 넣을 것 목록에 있으면 넣음(✓)으로 표시한다.
- **쓰는 곳**(`AssetTools.UsedBy`): 모든 에셋의 의존(`AssetDatabase.GetDependencies`)을 거꾸로 묶은 표. 프로젝트가 바뀌면 다시 만든다.
- **안 쓰는 것**(`AssetTools.Unused`): 개발자 폴더(`Assets/Dev`) 안에서, 뿌리(빌드 목록의 씬 · `Assets/Dev` 안의 모든 씬(빌드 목록에 아직 없어도) · `Assets/MomoiProject`(DB 데이터·UI·AI 씬) · `Resources` 안 · 그 밖의 DB 항목 · Addressables 항목(쓰면) · 넣을 것 목록)에서 닿지 않는 그림·소리·글꼴·모델·프리팹·머티리얼·애니메이션·영상. 코드가 든 폴더(가져온 패키지)는 보지 않는다. **코드가 경로 글자로 불러오는 것은 연결이 보이지 않는다** — 옮기기 전에 묻는 창에서 확인한다. 옮기면 `Assets/Dev/_Unused/<원래 경로>`(지우지 않음)이고, 옮긴 목록이 `MomoiProjectBackups/<시각>-unused/moves.tsv`에 남아 '마지막 옮기기 되돌리기'로 되돌린다.
- **폴더 정돈**(`AssetTools.Misplaced` · `Tidy`): 개발자 폴더(`Assets/Dev`) 안에서 종류별 폴더 밖의 에셋을 그 폴더로 옮긴다(`MoveAsset` — GUID 유지). 하위 폴더 모양은 그대로 둔다(`Assets/Dev/Cards/Fire/a.png` → `Assets/Dev/Art/Cards/Fire/a.png`, 다른 종류 폴더 안이면 그 폴더 이름만 뺀다). `Assets/Dev` 밖(시스템 폴더·패키지·Plugins·에셋 스토어 폴더)과 코드가 든 폴더(가져온 패키지)·씬 옆 같은 이름 폴더(라이트맵 등)·`Resources`·`Editor`·Addressables 폴더 항목은 건드리지 않는다. 옮기기 전에 몇 개인지와 앞의 몇 개를 보여 묻고, 옮긴 목록(원래 경로 → 새 경로)을 `MomoiProjectBackups/<시각>-tidy/moves.tsv`에 남긴다 — '마지막 정돈 되돌리기'(`AssetTools.UndoMoves`)로 되돌린다.

## 2. 에디터에서 쓰는 법

메뉴 **MomoiProject › 에셋 관리**: 탭 넣을 것 · 목록 · 안 쓰는 것 · 폴더 정돈. 탐색기에서 파일·폴더를 창에 끌어 놓으면 가져오기.

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

- 이미 파일이 있으면 임시 에셋을 만들지 않는다(개발자가 넣은 것을 덮지 않는다). 같은 경로를 다시 부르면 설명만 고친다.
- 임시 에셋을 화면·DB가 바로 쓰게 둔다(스프라이트 칸에 그 경로의 에셋을 넣는다). 진짜 파일이 들어와도 연결을 다시 할 필요가 없다.

| 멤버 | 하는 일 |
|---|---|
| `AssetRequests.Sprite(path, w, h, 쓰는 곳, 느낌)` · `Audio(path, 쓰는 곳, seconds, 느낌)` · `Request(path, 쓰는 곳, 크기, 느낌)` | 임시 에셋 + 넣을 것 |
| `AssetRequests.Load()` · `MarkDone(path)` · `Remove(path)` | 목록 |
| `AssetTools.Import(paths, folder)` · `Replace(asset, file)` · `UsedBy(asset)` · `Unused()` · `MoveToUnused(paths)` · `Misplaced()` · `Tidy(moves)` · `FolderFor(kind)` · `KindOf(path)` · `EnsureFolder(folder)` | 가져오기·바꾸기·찾기·옮기기 |

## 4. 주의할 점

- 바꾸기는 되돌리기(Undo)가 없다. 원래 파일이 필요하면 Git으로 되돌린다.
- 넣을 것 목록 파일은 프로젝트와 함께 커밋한다(누가 무엇을 넣어야 하는지 남는다).
- 스프라이트를 잘라 둘 크기는 용도마다 틀 파일 하나의 크기 표로 정한다: Project 창에서 우클릭 › Create › MomoiProject › 스프라이트 틀로 만들고 인스펙터에서 채운다(27장 2절).

## 5. 스파인 들이기(Spine — 외부 런타임)

스파인 캐릭터는 개발자가 스파인 에디터에서 내보낸 파일로 들어온다. AI는 런타임 패키지를 들이고(내려받기라 먼저 묻는다 — 관례 0절) 가져오기 설정을 맞춘 뒤 띄워 확인한다. 스파인 에디터가 없어도 내보낸 파일만으로 재생 · 섞기 · 스킨 · 부착물 바꾸기 · 이벤트 · 뼈 따라가기는 된다. 뼈 · 메시 · 키를 고치거나 다시 내보내는 일(버전 · 알파 방식 · 아틀라스 다시 묶기)은 에디터가 있어야 한다. 아래는 2026-10-13 UI 실험실(Unity 6000.6.3 · 스파인 4.2 파일)에서 실제로 해 본 순서다.

### 5-1. 버전 — 파일에 맞춘다

- 런타임과 내보낸 파일은 **앞 두 자리(4.2 · 4.3)가 같아야** 한다. 파일 버전: JSON은 맨 앞 `"skeleton": { … "spine": "4.2.43" }`, 바이너리는 머리 몇 바이트 뒤의 글자(`4.2.33`).
- 공식 지원 Unity(2026-10 다운로드 페이지): 4.3 = 6000.4까지, 4.2 = 6000.3까지. Unity 6.3 이상에서 4.2를 쓰면 고쳐야 컴파일된다(5-3).
- 개발자가 스파인 에디터를 가졌으면 4.3으로 내보내 4.3 런타임을 쓰는 것이 가장 쉽다(고칠 것 없음 · 스트레이트 알파가 기본). 파일만 있으면 파일 버전을 따른다.

### 5-2. 설치(AI)

1. 묻는다: 받을 것(git 주소 두 개 — 저장소 전체는 600MB 남짓, 실험실에서는 40초 안에 받았다) · 버전 · 넣을 프로젝트.
2. `Packages/manifest.json`의 `dependencies`에 두 줄(4.3이면 끝을 `#4.3`):
   ```json
   "com.esotericsoftware.spine.spine-csharp": "https://github.com/EsotericSoftware/spine-runtimes.git?path=spine-csharp/src#4.2",
   "com.esotericsoftware.spine.spine-unity": "https://github.com/EsotericSoftware/spine-runtimes.git?path=spine-unity/Assets/Spine#4.2",
   ```
   unitypackage도 있지만 `Assets/Spine` · `Assets/Spine Examples`가 생겨 쓰지 않는다(Assets 바로 아래 폴더 — 관례 2절).
3. **Unity가 manifest를 다시 읽게 한다.** Unity 창이 뒤에 있으면 요청 파일의 새로 읽기(`AssetDatabase.Refresh`)만으로는 패키지를 다시 풀지 않는다. 프로젝트 `Code/Editor`에 아래 도구를 두면 컴파일 뒤 manifest가 lock 파일보다 새로울 때 다시 푼다. 컴파일이 깨져 있으면 도메인이 다시 읽히지 않아 이 도구도 돌지 않는다 — 개발자에게 Unity 창을 한 번 눌러 달라고 한다(창이 앞으로 오면 Unity가 다시 푼다).
   ```csharp
   [InitializeOnLoadMethod]
   static void ResolveIfManifestChanged()
   {
       var manifest = new FileInfo("Packages/manifest.json");
       var lockFile = new FileInfo("Packages/packages-lock.json");
       if (!manifest.Exists || (lockFile.Exists && lockFile.LastWriteTimeUtc >= manifest.LastWriteTimeUtc)) return;
       UnityEditor.PackageManager.Client.Resolve();
   }
   ```
4. `Library/PackageCache`에 두 패키지가 생기고 `Logs/Editor.log`에 `error CS`가 없는지 본다.
5. 스파인이 처음 돌 때 설정 파일 `Assets/Editor/SpineSettings.asset`을 만든다. 경로가 스파인 코드에 고정돼 있어 옮기지 않는다(관례의 예외 — 프로젝트와 함께 커밋).

### 5-3. Unity 6.3 이상에서 4.2를 쓸 때 — 패키지를 들여와 고친다

Unity 6.6은 `Object.GetInstanceID()`를 오류로 막았다(`CS0619 … Use GetEntityId instead`). spine-unity 4.2(4.2.120)는 이것을 쓰고, 4.3은 `#if`로 고쳐 두었다. 4.2를 써야 하면 4.3과 같은 방식으로 고친다.

1. `Library/PackageCache/com.esotericsoftware.spine.spine-unity@…`를 `Packages/com.esotericsoftware.spine.spine-unity`로 복사하고(embedded 패키지 — 프로젝트와 함께 커밋, 그 뒤 git으로 저절로 바뀌지 않는다) manifest의 spine-unity 줄을 지운다. spine-csharp는 고칠 것이 없어 git 그대로 둔다. 다시 푼다(5-2의 3).
2. 아래를 고친다. 옛 줄은 `#else` 쪽에 남긴다.

| 파일(`Runtime/spine-unity/…` · `Editor/spine-unity/Editor/…`) | 고칠 것 |
|---|---|
| `Components/SkeletonMecanim.cs` | `AnimationClipEqualityComparer`: `#if UNITY_6000_3_OR_NEWER` → `x.GetEntityId() == y.GetEntityId()` · `o.GetHashCode()` |
| `Asset Types/SpineAtlasAssetInspector.cs`(2곳) · `Components/SkeletonGraphicInspector.cs` | `AssetDatabase.GetAssetPath(texture.GetInstanceID())` → `AssetDatabase.GetAssetPath(texture)` |
| `Utility/AssetUtility.cs` | `#if UNITY_6000_3_OR_NEWER` → `var skeletonGraphicID = skeletonGraphic.GetEntityId()` · `….GetEntityId().IsValid()` · `EditorUtility.EntityIdToObject(….GetEntityId())` |
| `Utility/DataReloadHandler.cs` | 표 `Dictionary<EntityId, string>` · `EntityId sgID = skeletonGraphic.GetEntityId()` |
| `Utility/SpineEditorUtilities.cs` | 맨 위 `#if UNITY_6000_4_OR_NEWER` `#define HIERARCHY_WINDOW_ENTITY_ID_CALLBACK`, 네임스페이스 안 `using EntityIdType = EntityId;`(아니면 `System.Int32`). 하이어라키 아이콘 표 4개의 키 `EntityIdType` · `IconsOnGUI(EntityIdType …)` · `hierarchyWindowItemByEntityIdOnGUI` · 끌어 놓기 `DragAndDrop.AddDropHandlerV2`/`RemoveDropHandlerV2` + `HandleDragAndDrop(EntityId …)`(본문은 `EditorUtility.EntityIdToObject`) · `gameObject.GetEntityOrInstanceId()` 확장 함수(파일 끝 `EntityIdExtensions`) |

3. 다시 컴파일해 `error CS`가 없는지 본다. 이 개발 환경에서는 UI 실험실(`UITemplateLab`)의 `Packages/com.esotericsoftware.spine.spine-unity`가 고친 사본(4.2.120)이다 — 같은 버전이면 그것을 복사해도 된다.

### 5-4. 알파 — PMA인지 스트레이트인지

- 텍스처가 PMA(premultiplied alpha)인지 스트레이트인지에 가져오기를 맞춘다. 어긋나면 가장자리가 검거나 하얗게 뜬다. 런타임 기본은 **4.3 = 스트레이트, 4.2 = PMA**.
- 아는 법: 4.x 아틀라스(`.atlas.txt`)에 `pma:true` 줄이 있으면 PMA, 없으면 스트레이트. 밖에서 받은 파일은 픽셀로 확인한다 — 반투명 픽셀에서 RGB가 알파보다 큰 칸이 많으면 스트레이트(PMA는 늘 RGB ≤ 알파).
- **파일을 넣기 전에** `Assets/Editor/SpineSettings.asset`을 고친다 — 설정은 .meta가 없는 새 텍스처를 가져올 때만 적용된다.
  - 스트레이트: `textureSettingsReference: Packages/com.esotericsoftware.spine.spine-unity/Editor/spine-unity/Editor/ImporterPresets/StraightAlphaPreset.preset`, `blendModeMaterialMultiply` · `Screen` · `Additive`에 `SkeletonStraightMultiply` · `Screen` · `Additive.mat`(패키지 `Runtime/spine-unity/SkeletonDataModifierAssets/BlendModeMaterials/` — `{fileID: 2100000, guid: <.meta의 guid>, type: 2}`).
  - PMA: `textureSettingsReference`를 비우고(또는 `PMATexturePreset.preset`) 블렌드 머티리얼은 `SkeletonPMA…`.
- 확인: 생긴 머티리얼의 `_StraightAlphaInput: 1`(스트레이트) · 텍스처 .meta의 `sRGBTexture: 1` · `alphaIsTransparency: 1`. 이미 잘못 들어왔으면 생긴 `_Atlas` · 머티리얼 · `_SkeletonData`와 텍스처 .meta를 지우고 다시 넣는다.

### 5-5. 파일 넣기

- 개발자의 내보내기 설정(가이드 06): 바이너리 `.skel.bytes`(또는 JSON `.json`) · 아틀라스 확장자 `.atlas.txt` · `Animation cleanup` 끔 · `Nonessential data` 켬 · 4.3이면 `Premultiply alpha` 끔 + `Bleed` 켬.
- 자리: `Assets/Dev/Art/Spine/<이름>/`에 뼈대 데이터 **하나**(`.skel.bytes` 또는 `.json` — 둘 다 넣으면 `_SkeletonData`가 두 벌) · `.atlas.txt` · `.png`(아틀라스 장마다). `.spine`(에디터 프로젝트 파일)은 넣지 않는다. 확장자만 다르면(`.skel` · `.atlas`) 이름을 바꿔 넣는다 — 그대로면 읽지 못한다.
- 새로 읽으면 `<이름>_Atlas` · 장마다 머티리얼 · `<이름>_SkeletonData`가 저절로 생긴다(크기 0.01 — 스파인 1px = 0.01 유닛).
- 밖에서 받은 자료(다른 게임에서 나온 파일 등)는 저장소에 넣지 않는다(`.gitignore`).

### 5-6. 코드에서 쓰기 · 확인

- 스파인을 부르는 프로젝트 코드는 asmdef 참조에 `spine-unity` · `spine-csharp`를 더한다(`MomoiProject.Client` — 선택 기능 동기화는 GBible 모듈 참조만 고쳐 지워지지 않는다). 다이얼로그 스탠딩(`SpineStandingView`)은 참조 없이 이름으로 찾는다(24장).
- 띄우기 `SkeletonAnimation.NewSkeletonAnimationGameObject(skeletonDataAsset)`, 재생 `AnimationState.SetAnimation(트랙, 이름, 반복)` · 이어서 `AddAnimation`, 애니메이션 목록 `skeletonDataAsset.GetSkeletonData(true).Animations`.
- **조종 뼈를 마우스로 끌기**(시선 · 손 따라가기 같은 조종용 뼈 — 다른 뼈가 IK · transform 제약으로 그 뼈를 따른다):
  1. 데이터에서 찾는다: 제약의 `target`이 되는 뼈(예: 눈 · 머리카락 · 얼굴 IK가 따르는 `Touch_Point`)와, 그 제약의 세기(mix)를 올리고 내리는 애니메이션(예: `Look_01_M` · `LookEnd_01_M`).
  2. 누르면 위 트랙(1번)에 세기를 올리는 애니메이션을 켠다.
  3. 포인터 자리는 `camera.ScreenToWorldPoint` → `transform.InverseTransformPoint`로 스켈레톤 공간으로 바꾸고, 제자리에서 얼마나 떨어졌나를 구해 거리를 제한한다.
  4. 그 오프셋을 **셋업 자세에서 한 번 잡아 둔** 부모 행렬(`skeleton.SetToSetupPose()` → `UpdateWorldTransform(Physics.None)` → `parent.GetWorldToLocalMatrix`)로 로컬 오프셋으로 바꿔, 매 프레임 애니메이션이 적용된 뒤(`SkeletonAnimation.UpdateLocal`) 뼈의 로컬 x · y에 더한다. 애니메이션이 키를 준 뼈는 그 값에, 키가 없는 뼈는 처음 값에 더한다.
     - **지금 자세로 화면 자리를 고정하면(`bone.SetPositionSkeletonSpace`) 튄다.** 조종 뼈가 머리의 자식이면, 그 뼈가 IK로 머리를 돌리고 머리가 다시 그 뼈를 옮기는 되먹임이 생긴다. 그래서 그 뼈를 따르는 뼈(머리 장식 등)가 한 프레임에 크게 튄다. 실험실에서 잰 값은 평균의 35배였고, 셋업 행렬 방식은 튀지 않았다.
  5. 놓으면 끝 애니메이션 + `AddEmptyAnimation`을 틀고, 오프셋을 0으로 부드럽게 줄인 뒤 원래 값으로 되돌린다. 애니메이션이 키를 주지 않는 뼈는 옮긴 값이 그대로 남기 때문이다.
  6. 화면(UI) 칸 위에서 누른 것은 보내지 않는다(관례 21절).
  - **시선 뼈**(눈동자 · 눈썹이 따르는 뼈 — 예: `Touch_Eye`)는 누르지 않아도 포인터를 따라가게 둔다. 제약의 세기가 셋업에 이미 들어 있어서 애니메이션이 필요 없다.
  - 예: UI 실험실 `Code/Client/Lab/Spine/SpineDrag.cs`. 테스트 명령 `spine.drag.face` · `hand` · `eye`로 마우스 없이 확인하고, `spine.drag.sweep`(`.old`)로 좌우 왕복 시 뼈마다 프레임당 이동을 비교한다.
- 기본 셰이더 `Spine/Skeleton`은 URP에서도 그린다(빛을 받지 않음). 빛을 받게 하려면 URP 셰이더 패키지(`com.esotericsoftware.spine.urp-shaders`).
- 화면(UI Toolkit) 안에 그리려면 4.3의 UI Toolkit 확장(`com.esotericsoftware.spine.ui-toolkit`, Unity 6000.4)이 있다 — 아직 써 보지 않았다. 4.2는 UI Toolkit을 지원하지 않는다: 월드 장면에 두거나 `SkeletonGraphic`(UGUI)인데, UI 가이드는 UGUI를 들이지 않으므로 그때 묻는다.
- 확인: 테스트 명령으로 띄우고 플레이 요청 `capture`로 찍는다(38장 2-5). 카메라는 데이터의 범위(`SkeletonData.X · Y · Width · Height` × 크기)에 맞춘다. 데이터 안에 카메라 뼈가 있는 장면형(로비 일러스트)은 애니메이션이 화면을 확대 · 이동한다 — 오류가 아니다.
- 라이선스: 스파인 런타임을 넣은 제품은 사용자마다 스파인 에디터 라이선스가 있어야 한다(Spine Runtimes License). 출시 전에 개발자에게 알린다.
