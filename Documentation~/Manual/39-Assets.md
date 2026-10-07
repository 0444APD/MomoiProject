# 39. 에셋 관리 — 넣을 것 · 가져오기 · 바꾸기 · 안 쓰는 것 · 폴더 정돈

**한 줄 요약**: 그림·소리·글꼴 같은 리소스는 개발자가 가져온다. AI는 필요한 곳에 **임시 에셋**(단색 사각형·무음)을 최종 경로에 만들고 **넣을 것** 목록(`AssetRequests`)에 올린다. 개발자는 메뉴 **MomoiProject › 에셋 관리**에서 그 줄에 진짜 파일을 고른다 — **같은 경로에 내용을 덮으므로 GUID가 그대로**라 그 에셋을 쓰는 씬·프리팹·DB가 모두 새 것을 쓴다. 같은 창에서 종류별 목록·쓰는 곳·바꾸기·안 쓰는 것(옮김, 지우지 않음)·폴더 정돈을 한다.

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
