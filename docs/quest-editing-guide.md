# 퀘스트 수정 가이드 (사건 추가/수정/삭제 + 화면 구성 요소)

이 문서는 **콘텐츠 담당자/개발자가 실제로 퀘스트("사건")를 추가·수정·삭제할 때 어떤 파일을 건드려야 하는지**와,
화면을 구성하는 패널(HUD, 후보물질 판넬)·AI 비서 컴포넌트가 어떻게 동작하는지를 정리한 레퍼런스입니다.

프로젝트 전반의 개발 진행 상황/History는 [`docs/guide.md`](guide.md)를 참고하세요. 이 문서는 그와 별개로,
**"지금 있는 퀘스트를 어떻게 고치는가"**에만 집중합니다. (2026-08-26 기준 코드/씬 상태로 작성)

---

## 0. 먼저 알아야 할 것: 퀘스트는 두 계층으로 나뉜다

이름이 비슷해서 헷갈리기 쉬운 **두 개의 서로 다른 시스템**이 함께 동작합니다.

| 계층 | 핵심 컴포넌트/클래스 | 저장 형태 | 담당 |
|---|---|---|---|
| **A. 인트로 "사건" 카드/대사** | `QuestDefinition`(ScriptableObject), `QuestCatalog`, `QuestSession`, `AIAssistantBrain`, `QuestManagerSpatialUI` | `Assets/Quests/*.asset` | 인트로 화면 카드 선택, AI 비서의 브리핑/힌트 대사, 5단계 진행률 표시 |
| **B. 실제 도킹 플레이 데이터** | `DockingQuestDefinition`, `DockingQuestCatalog`, `DockingQuestController`, `CompoundData` | `Assets/StreamingAssets/quests/*.json`, `Assets/StreamingAssets/compounds/**/*.json` | 단백질 구조 로드, 포켓 하이라이트, 후보물질 판넬, 도킹 성공/실패 판정 |

두 계층은 **문자열 id로 매칭**됩니다 — A의 `QuestDefinition.questId`와 B의 `DockingQuestDefinition.id`가 정확히 같아야
`DockingQuestCatalog.ApplyForSessionQuest()`(`Assets/Scripts/Quest/DockingQuestCatalog.cs`)가 두 데이터를 하나의 사건으로 연결합니다.

- **A만 있고 B가 없으면**: 인트로 보드에 카드는 뜨지만, 도킹 단계에서 아무 반응이 없거나 이전 퀘스트 데이터가 남아있는 상태로 진행됩니다.
- **B만 있고 A가 없으면**: 도킹 자체는 (단독 실행 모드에서) 동작하지만, 인트로 보드에는 카드가 뜨지 않습니다.
- **완전한 사건 하나를 만들려면 A와 B를 모두 채우고 id를 맞춰야 합니다.**

현재 등록된 5개 사건과 대응 id:

| 사건 | questId / id | 유전자·변이 | 구조 JSON |
|---|---|---|---|
| 사건 1 | `kras_g12c` | KRAS G12C | `structures/P01116.json` |
| 사건 2 | `egfr_l858r` | EGFR L858R | `structures/P00533.json` |
| 사건 3 | `abl1_t315i` | ABL1 T315I | (구조 JSON은 해당 quests json 참고) |
| 사건 4 | `cftr_f508del` | CFTR F508del | `structures/8EJ1.json` (corrector 성공 후 `structures/8EIQ.json`으로 교체) |
| 사건 5 | `p53_y220c` | p53 Y220C | (구조 JSON은 해당 quests json 참고) |

---

## 1. 새 사건(퀘스트) 추가하기

### 1-1. ② 도킹 플레이 데이터부터 만든다 (코드/씬 수정 불필요)

1. **단백질 구조 JSON 생성**
   ```bash
   python pdb_parser_script.py <UniProtID>
   ```
   결과가 `Assets/StreamingAssets/structures/<NEW>.json`에 생성됩니다. 스키마:
   ```json
   {"atoms": [
     {"name":"CA","element":"C","x":1.2,"y":3.4,"z":5.6,
      "bfactor":92.3,"res_name":"LEU","res_id":858,"is_backbone":true}
   ]}
   ```
   (실제 cryo-EM 구조를 쓸 경우 RCSB에서 받은 JSON을 같은 스키마로 직접 배치해도 됩니다 — 사건4의 `8EJ1.json`/`8EIQ.json`이 그 예)

2. **후보물질(화합물) JSON 작성** — `Assets/StreamingAssets/compounds/<new_quest_id>/*.json`
   스키마 (`Assets/Scripts/Quest/CompoundData.cs` 기준):
   ```json
   {
     "id": "my_compound",
     "display_name": "My Compound",
     "subtitle": "억제제 유형",
     "outcome": "Success",
     "affinity": -8.5,
     "result_message": "성공 메시지...",
     "completes_stage": true,
     "requires_prior_success_id": "",
     "order_error_message": "",
     "atoms": [{"element":"C","x":0,"y":0,"z":0,"is_warhead":false}],
     "bonds": [{"a":0,"b":1}]
   }
   ```
   - `outcome`은 `DockingOutcome` enum 이름과 **정확히 일치**해야 합니다(대소문자 포함): `Success`, `NoWarhead`, `StericClash`, `OffTarget` (공용), 또는 `FragmentHit`/`WrongStrategy`/`NoStabilization`/`NonSelective`(부분판정 — p53/CFTR류에서 재사용).
   - `completes_stage: false`로 두면 성공해도 단계가 끝나지 않고 패널이 다시 열립니다 (예: CFTR corrector → potentiator처럼 여러 화합물을 순서대로 골라야 하는 경우).
   - `requires_prior_success_id`에 다른 화합물 id를 적으면, 그 화합물이 먼저 성공(`_succeededCompoundIds`에 등록)하지 않은 상태에서 이 화합물을 고르면 성공/실패 대신 "순서 오류" 연출이 재생됩니다.

3. **퀘스트 정의 JSON 작성** — `Assets/StreamingAssets/quests/<new_quest_id>.json`
   (`kras_g12c.json`을 복사해서 값만 교체하는 것을 권장)
   ```json
   {
     "id": "my_new_quest",
     "title": "표시용 제목",
     "protein_json": "structures/<NEW>.json",
     "target_residue_id": 12,
     "target_atom_name": "SG",
     "pocket_residue_ids": [12, 62, 68, 95, 96, 99],
     "compounds_folder": "compounds/<new_quest_id>",
     "compound_files": ["my_compound.json", "..."],
     "entrance_offset": 0.8,
     "helix_regions": [
       {"label": "Switch-II Helix", "start_res_id": 66, "end_res_id": 74}
     ]
   }
   ```
   | 필드 | 의미 |
   |---|---|
   | `id` | A계층 `questId`와 반드시 동일 |
   | `protein_json` | `StreamingAssets` 기준 상대 경로 |
   | `target_residue_id`/`target_atom_name` | 공유결합 대상 잔기/원자. 이름이 비면 element `S` → `CA` 순 폴백 |
   | `pocket_residue_ids` | 하이라이트할 포켓 잔기 목록 |
   | `compounds_folder`/`compound_files` | 화합물 JSON 폴더/파일명(등장 순서) |
   | `entrance_offset` | 포켓 중심~입구 거리 (0이면 컨트롤러 기본값 유지) |
   | `helix_regions` | `StructureLevelController`에 주입되는 Helix 표시 구간 |

4. **`Assets/StreamingAssets/quests/index.json`에 파일명 추가**
   ```json
   {"quests": ["kras_g12c.json", "egfr_l858r.json", "abl1_t315i.json", "cftr_f508del.json", "p53_y220c.json", "my_new_quest.json"]}
   ```
   **이 배열에 없으면 `DockingQuestCatalog`가 이 퀘스트를 영원히 모릅니다.** 순서 = 인트로 없이 단독 실행할 때의 진행 순서.

여기까지만 하면 도킹 자체는 완성됩니다. 하지만 **인트로 보드에는 뜨지 않습니다.**

### 1-2. ① 인트로 보드용 `QuestDefinition` 만들기

1. Project 창 우클릭 → `Create > Taming Mutants > Quest Definition` → `Assets/Quests/Quest_<이름>.asset`로 저장.
2. Inspector에서 채울 필드 (`Assets/Scripts/Quest/QuestDefinition.cs`):
   - `questId` — 위 1-1의 `id`와 **정확히 동일**하게
   - `title`/`subtitle`/`gene`/`mutation`/`summary`/`difficulty`(1~5)/`accent`(카드색)
   - `structureStreamingPath` — 1-1의 `protein_json`과 동일 경로
   - `mutationResidueIds[]` — `MutationHighlighter`가 강조할 변이 잔기
   - `targetPocketLabel` — 비서 대사/LLM 컨텍스트에 쓰이는 표적 이름
   - `stages[]`(`QuestStageBriefing`) — 단계별 `title`/`objective`/`assistantLines[]`(비서가 항상 먼저 재생하는 대본)/`hints[]`/`llmContext`(LLM에 함께 보낼 배경). **비서 대사는 여기 적힌 것이 항상 우선이고 LLM은 그 위에 얹는 심화 설명일 뿐**이므로, 백엔드가 꺼져 있어도 게임이 끝까지 진행되게 하려면 이 대본을 꼭 채워야 합니다.
   - `candidates[]` — 후보물질 카드 설명(선택 사항, 레거시에 가까움 — 실제 판정은 B계층 `CompoundData`가 담당)
3. `Assets/Quests/QuestCatalog.asset`을 선택 → `quests` 배열에 방금 만든 에셋을 드래그 추가. **이 배열에 없으면 인트로 보드에 카드 자체가 뜨지 않습니다.**

> 참고: `Assets/Scripts/Editor/IntroSetupMenu.cs`(`Tools > Taming Mutants > 인트로 + 퀘스트 카탈로그 생성`)는 이미 KRAS/EGFR/ABL1/CFTR/p53 5종을 하드코딩해서 채우는 스캐폴딩 도구이며, 재실행하면 `LoadOrCreate`가 **기존 에셋 값을 다시 초기화**합니다. 완전히 새로운 6번째 사건을 추가할 때는 이 메뉴를 건드리지 말고 위 1~3단계를 수동으로 하거나, 이 메뉴 스크립트에 `Fill<NewQuest>()` 함수와 `BuildCatalog()`의 배열 항목을 추가하는 방식으로 확장하세요.

### 1-3. (필요한 경우만) 사건 전용 연출/HUD 추가하기

KRAS/EGFR/ABL1은 공용 `DockingQuestController` 연출만으로 충분하지만, **CFTR(사건4)과 p53(사건5)처럼 전용 인트로 연출·HUD가 필요한 경우** 아래 패턴을 따릅니다 (`CftrRescueController`/`CftrHUD`/`CftrFinaleController`, `ThermalStabilityController`/`ThermalStabilityHUD`/`P53QuestDirector`가 실제 예시).

1. `Assets/Scripts/Quest/`에 새 `XxxController.cs` 작성 — `activeForQuestId = "my_new_quest"` 필드로 자기잠금(다른 사건 진행 중엔 아무 반응 안 함). `DockingQuestCatalog.OnQuestStarted` 이벤트를 구독해서 `def.id == activeForQuestId`일 때만 활성화.
2. 필요하면 `XxxHUD.cs` 작성 — 코드로 UI를 조립하고(`CftrHUD.BuildUI()` 패턴 참고) `Awake()`에서 `gameObject.SetActive(false)`로 시작, 담당 컨트롤러가 명시적으로 켬.
3. `DockingQuestController.cs`(`Assets/Scripts/Quest/DockingQuestController.cs`)에 새 컨트롤러용 참조 필드를 추가(`cftr`, `thermal`, `hud`처럼)하고, `Awake()`의 `FindFirstObjectByType` 자동탐색 목록과 `SuccessSequence()`/`FinishFailure()`/`OrderErrorSequence()` 안의 콜백 호출부(`if (cftr != null) cftr.HandleCompoundSuccess(...)` 같은 패턴)에 새 컨트롤러 훅을 추가.
4. `Assets/Scripts/Editor/`에 `XxxSetupMenu.cs` 작성(`CftrRescueSetupMenu.cs` 패턴) — `[MenuItem("Tools/Taming Mutants/...")]`로 씬에 오브젝트를 자동 생성·배선. Play 모드 중 실행은 막아야 함(`EditorApplication.isPlaying` 체크).
5. Unity 에디터에서 새 메뉴를 실행해 `Lab_Desktop.unity` 씬에 실제로 오브젝트를 만들고 저장.

> **⚠ 현재 상태 참고 (2026-08-26 확인)**: 사건4(CFTR)의 JSON 데이터와 스크립트(`CftrHUD`/`CftrRescueController`/`CftrFinaleController`)는 완성되어 있지만, **`Lab_Desktop.unity` 씬에는 아직 이 세 오브젝트가 생성되어 있지 않습니다** (`Tools > Taming Mutants > CFTR 구조_potentiator 퀘스트 오브젝트 생성` 메뉴가 아직 실행되지 않은 상태). 이 메뉴를 실행하지 않으면 `DockingQuestController.cftr`가 null로 남아 F508del 전용 인트로(DNA→결실→8EJ1)와 HUD가 재생되지 않고 공용 KRAS류 연출로만 진행됩니다. 사건5(p53)의 `ThermalStabilityController`/`P53QuestDirector`는 이미 씬에 배치되어 있습니다.

---

## 2. 기존 사건 수정하기 — "무엇을 바꾸려면 어디를 보나"

| 바꾸고 싶은 것 | 수정할 파일 |
|---|---|
| 카드 문구(제목/요약/난이도/색), 단계별 비서 대사·힌트·LLM 컨텍스트 | `Assets/Quests/Quest_*.asset`을 Inspector에서 직접 편집 (또는 `IntroSetupMenu.cs`의 해당 `Fill<Quest>()` 함수를 고치고 메뉴 재실행 — 이 경우 손으로 튜닝한 값이 재실행 시 덮어써짐에 주의) |
| 타깃 잔기/포켓/구조 파일/헬릭스 구간 | `Assets/StreamingAssets/quests/<id>.json` 직접 편집 — 재빌드 불필요, 텍스트 수정만으로 즉시 반영 |
| 후보물질 성패/문구/좌표/친화도/선후관계 | `Assets/StreamingAssets/compounds/**/<file>.json` 직접 편집 |
| 도킹 연출 자체(속도, 색, 이펙트) | `Assets/Scripts/Quest/DockingQuestController.cs`의 `[Header("연출 설정")]` Inspector 필드(`approachDuration`, `pocketHighlightColor`, `successColor` 등) 또는 코루틴(`SuccessSequence`, `MoveTo`, `Shake`, `BurstEffect`, `OrderErrorSequence`) |
| CFTR 전용 연출/HUD 문구 | `CftrRescueController.cs`(wobble/ICL4 틴트/QC파티클/화합물별 반응) / `CftrHUD.cs`(HUD 텍스트·바) / `CftrFinaleController.cs`(도착 연출) |
| p53 전용 연출/HUD 문구 | `ThermalStabilityController.cs` / `ThermalStabilityHUD.cs` / `P53QuestDirector.cs` |
| 단백질 구조 좌표/신뢰도 | `Assets/StreamingAssets/structures/*.json` (파이썬 파서로 재생성 권장) |
| 후보물질 판넬 위치/레이아웃 | `Assets/Scripts/Quest/CompoundSelectionPanel.cs`의 `sideGap`/`topOffset`/`pullTowardCamera`/`panelScale` 등 간격 파라미터만 — **배치 방식(구조 왼쪽 옆 + 사선) 자체는 사용자가 확정한 값이므로 임의로 바꾸지 말 것** (3-2절 참고) |

---

## 3. 사건 삭제하기 — 체크리스트

1. `Assets/StreamingAssets/quests/index.json`에서 해당 파일명 제거 (B계층 로딩 중단).
2. `Assets/Quests/QuestCatalog.asset`의 `quests` 배열에서 해당 `QuestDefinition` 항목 제거(Inspector에서 배열 크기 줄이기), 관련 `Quest_*.asset` 파일 삭제.
3. `Assets/StreamingAssets/quests/<id>.json`, `Assets/StreamingAssets/compounds/<id>/` 폴더 삭제 (`.meta` 파일 포함).
4. 전용 컨트롤러가 있던 사건(CFTR/p53류)이면 — 씬에서 해당 GameObject(`CftrHUD`, `CftrRescueController`, `CftrFinaleController` 등) 삭제, `DockingQuestController`의 해당 참조 필드(`cftr`, `thermal`, `hud`)를 비움. **`DockingQuestController.Awake()`가 `FindFirstObjectByType`로 자동 재탐색하므로, 오브젝트를 씬에 남겨두면 삭제한 사건이 아닌 다른 사건 진행 중에도 (자기잠금 `activeForQuestId` 덕분에 반응은 안 하지만) 불필요하게 씬에 남습니다** — 완전 삭제 시엔 오브젝트 자체를 지우세요.
5. `structures/*.json`이 다른 사건과 공유되지 않는지 확인 후 삭제. 예: CFTR의 `8EJ1.json`/`8EIQ.json`은 서로 참조 관계이므로 둘 다 함께 삭제해야 함 — `CftrRescueController.cs`의 `HandleCompoundSuccess()`가 `"structures/8EIQ.json"` 경로를 하드코딩하고 있습니다.
6. 다른 사건이 화합물을 재사용 중인지 확인. 예: `kras_g12c_inhibitor_like.json`은 KRAS 정답 화합물이 아니라 **CFTR의 표적 불일치 오답 예시**로 `compounds/cftr_f508del/`에 별도 복제되어 있습니다 — 이름만 보고 KRAS 폴더를 지우면 안 됩니다.

---

## 4. 화면 UI 컴포넌트

### 4-1. 이 프로젝트에는 UI 프리팹이 없다

`Assets/Prefabs/`에는 `Atom.prefab`, `Bond.prefab`, `ExperimentTableRoot.prefab` 3개뿐입니다.
**HUD·후보물질 판넬·AI 비서 말풍선·퀘스트 보드는 전부 런타임에 코드(`BuildUI()`/`Build()` 메서드)로 조립됩니다.**
씬(`Lab_Desktop.unity`)에는 빈 GameObject + 컴포넌트 배선만 저장되어 있고(Editor 메뉴들이 자동 생성), 실제 비주얼은 Play 시 코드가 만듭니다.
→ UI 모양을 바꾸려면 씬 파일이 아니라 **해당 컴포넌트 스크립트의 `BuildUI()`/필드**를 고쳐야 합니다.

### 4-2. 후보물질 판넬 (`CompoundSelectionPanel.cs`)

- **역할**: `compounds_folder`/`compound_files`에 지정된 화합물 JSON들을 로드해 `columns`(기본 2) × 자동계산 rows 그리드로 3D 분자를 시각화. 각 칸은 `CompoundSlot`(스포트라이트+바닥글로우로 "전시대" 연출, `Assets/Scripts/Quest/CompoundSlot.cs`).
- **배치 규칙 (임의 변경 금지)**: 사건2(EGFR)를 실측해 고정한 상수 `ReferenceLateralExtent = 2.9637f`, `ReferenceTopExtent = 2.0004f`를 **모든 사건이 공통으로 사용**합니다. 구조 크기가 달라도(KRAS/CFTR/p53 등) 판넬은 항상 카메라 기준 "구조 왼쪽 옆 + 상단 높이 일치 + `diagonalYaw`(기본 25°)만큼 사용자 중앙 시선 쪽으로 사선 회전"에 놓입니다. 매 프레임(`LateUpdate`) 재배치되어 카메라가 계속 움직이는 클로즈업 연출에도 따라갑니다.
- **줌인 예외**: p53 열안정성 클로즈업처럼 카메라가 구조의 좁은 부위로 확 당겨지는 연출 중에는, 그 연출을 트는 컨트롤러가 `SetZoomOverride(true)`를 호출해 판넬을 "카메라 바로 옆 고정"(`PlaceBesideUser()`)으로 임시 전환합니다 — 승인된 유일한 예외이며 인스펙터 기본값에는 영향이 없습니다.
- **표시 조건**: `levelController`(`StructureLevelController`)가 연결돼 있으면 **아미노산(원자) 레벨에서만** 패널이 보입니다.
- **레이아웃 미세조정 파라미터**: `sideGap`(구조 옆 간격), `topOffset`(높이 미세조정), `pullTowardCamera`(화면상 크기), `panelScale`(전체 배율), `diagonalYaw`(사선 각도), `boxSize`/`spacing`(칸 크기/간격) — 이 값들만 조정 대상이고, 배치 "방식"(왼쪽+사선) 자체는 바꾸지 않습니다.
- **한글 표시**: `labelFont`에 한글 폰트를 지정하지 않으면 내장 폰트(`LegacyRuntime.ttf`)가 한글 글리프를 지원하지 않아 결과 메시지가 깨집니다.
- **입력**: PC는 `Update()`의 마우스 레이캐스트 클릭, XR/외부 호출은 `SelectSlot()`.
- **선택 이벤트**: `OnCompoundChosen` → `DockingQuestController.HandleCompoundChosen()`이 구독해 도킹 연출을 시작합니다.

### 4-3. HUD (사건별 전용)

사건4(CFTR)와 사건5(p53)만 전용 HUD를 가지며, 둘 다 화면 좌상단 고정 홀로그램 패널(`ScreenSpaceOverlay` Canvas)로 완전히 코드로 조립됩니다.

`CftrHUD.cs`(`Assets/Scripts/Quest/CftrHUD.cs`) 구성 (위→아래):
1. 3겹 배경(Glow/Panel/Stroke, `HoloSpriteFactory`가 런타임 생성)
2. Title "CFTR RESCUE MONITOR"
3. `SetSurfaceCftr(value01, label)` — "Surface CFTR: ..." + 진행바
4. `SetChannelActivity(value01, label)` — "Channel activity: ..." + 진행바
5. `ShowWarning(text)`/`HideWarning()` — 경고 행(기본 숨김)
6. `ShowMessage(text)` — 결과/설명 문구

시작 시 `gameObject.SetActive(false)`이며, 담당 컨트롤러(`CftrRescueController`)가 아미노산 레벨 진입 시 명시적으로 켭니다. `ThermalStabilityHUD.cs`(p53)도 동일한 조립 기법을 쓰며 p53 총량 바, DNA-binding 표시가 추가됩니다.
→ **HUD 문구/수치를 바꾸려면** 담당 컨트롤러(`CftrRescueController.HandleCompoundSuccess/Failure/OrderError` 등)가 호출하는 `hud.Set...`/`hud.ShowMessage(...)` 인자를 수정하면 됩니다. HUD 자체의 레이아웃(폭, 색, 폰트 크기)을 바꾸려면 `CftrHUD.BuildUI()`를 수정합니다.

### 4-4. 퀘스트 진행률 패널 (`QuestManagerSpatialUI.cs`, `Assets/Scripts/UI/`)

5단계(Quest1~5) 진행률을 보여주는 World Space 홀로그램 패널. `QuestSession`의 단계 이벤트를 구독해 갱신됩니다. 도킹 성공 시 `DockingQuestController.SuccessSequence()`가 `questUI.CompleteCurrentStageAndAdvance()`를 호출해 다음 단계로 넘어갑니다.

### 4-5. 뒤로가기 버튼 (`StructureLevelBackButton.cs`, `Assets/Scripts/UI/`)

화면 하단 중앙 "◀ Back" 버튼(코드로 조립). Ribbon(최상위) 레벨에서는 비활성, Helix/아미노산 레벨에서는 클릭 시 이전 레벨로 복귀(Esc 키와 동일 동작).

---

## 5. AI 비서 컴포넌트 (`Assets/Scripts/AI/`)

| 파일 | 역할 |
|---|---|
| `AIAssistantBrain.cs` | 비서의 행동 두뇌. `QuestSession`의 `OnQuestStarted`/`OnStageEntered`/`OnQuestCompleted` 이벤트를 구독해 **`QuestStageBriefing`의 대본 대사를 항상 먼저** 말하고, `AIChatBackend`가 설정돼 있으면 그 위에 LLM 심화 설명을 얹습니다. 대본이 항상 우선이므로 백엔드 없이도 게임이 끝까지 진행됩니다. `Speak`/`SpeakSequence`/`AskAssistant`/`RequestHint`/`ExplainSelection`/`ReportDockingResult` API 제공. |
| `AIChatBackend.cs` | 추상 기반 클래스 — `IsConfigured`, `Ask(userMessage, context, onReply, onFailed)` 계약. 백엔드 구현체를 교체해도 `AIAssistantBrain`은 무손실로 그대로 동작. |
| `SolarChatClient.cs` | Upstage Solar API 직접 호출(개발/시연용). API 키는 Inspector 또는 환경변수 `UPSTAGE_API_KEY`. **빌드에 키가 실리므로 배포 시 `AICoScientistClient`로 교체 필요.** |
| `AICoScientistClient.cs` | 자체 백엔드 프록시 호출(배포용). |
| `AIAssistantVisual.cs` | 상태(Idle/Listening/Thinking/Speaking/Alert)에 따른 색/발광 표현. |
| `AIAssistantFace.cs` | 눈 깜빡임/시선추적/상태별 눈 모양. |
| `AIAssistantFollower.cs` | 비서 위치 로직 — 사용자 시야 기준 또는 분자 옆 lazy-follow. `SetCloseUpOverride()`로 p53 클로즈업 중 임시 전환. |
| `AIAssistantSpeechBubble.cs` | World Space 말풍선(타이핑 연출 → 유지 → 페이드아웃, 큐잉). |
| `AIAssistantSpin.cs` | 비서 주변 궤도 링 회전. |
| `AIAssistantStateTester.cs` | 디버그용(숫자키로 상태 전환) — LLM 연동 확인 후 제거해도 무방. |

→ **사건별 비서 대사를 바꾸려면** 해당 `QuestDefinition` 에셋의 `stages[].assistantLines`/`hints`/`llmContext`를 수정합니다(2장 표 참고). 비서의 성격/말투(시스템 프롬프트)를 바꾸려면 `SolarChatClient.cs` 또는 `AICoScientistClient.cs`의 시스템 프롬프트 필드를 수정합니다.

---

## 6. 핵심 파일 한눈에 보기

```
Assets/
├── Quests/
│   ├── QuestCatalog.asset          # A계층: 인트로 보드에 뜨는 사건 목록 (quests[] 배열)
│   └── Quest_*.asset               # A계층: 사건별 카드/대사 데이터 (QuestDefinition)
├── StreamingAssets/
│   ├── quests/
│   │   ├── index.json              # B계층: DockingQuestCatalog가 읽는 사건 목록 (순서 중요)
│   │   └── <id>.json               # B계층: 사건별 구조/포켓/화합물 정의 (DockingQuestDefinition)
│   ├── compounds/<id>/*.json       # 사건별 후보물질 데이터 (CompoundData)
│   └── structures/*.json           # 단백질 원자 좌표/신뢰도 (ProteinLoader.ProteinData)
└── Scripts/
    ├── Quest/
    │   ├── QuestDefinition.cs          # A계층 스키마
    │   ├── QuestCatalog.cs             # A계층 카탈로그 (ScriptableObject)
    │   ├── QuestSession.cs             # A계층 진행 상태 단일 소스
    │   ├── DockingQuestDefinition.cs   # B계층 스키마
    │   ├── DockingQuestCatalog.cs      # B계층 로더/진행 엔진 (index.json 읽음)
    │   ├── DockingQuestController.cs   # 도킹 판정/연출 엔진 (모든 사건 공유)
    │   ├── CompoundData.cs             # 화합물 데이터 + DockingOutcome enum
    │   ├── CompoundSelectionPanel.cs   # 후보물질 판넬 (배치/그리드/입력)
    │   ├── CompoundSlot.cs             # 판넬 한 칸
    │   ├── CompoundMoleculeBuilder.cs  # 화합물 3D 분자 생성
    │   ├── RuntimeMaterials.cs         # 런타임 오브젝트 공용 머티리얼
    │   ├── CftrRescueController.cs     # 사건4 전용 연출
    │   ├── CftrHUD.cs                  # 사건4 전용 HUD
    │   ├── CftrFinaleController.cs     # 사건4 도착 연출
    │   ├── ThermalStabilityController.cs # 사건5 전용 연출
    │   ├── ThermalStabilityHUD.cs      # 사건5 전용 HUD
    │   └── P53QuestDirector.cs         # 사건5 도착 연출
    ├── AI/                          # 비서 (5장 참고)
    ├── Protein/ProteinLoader.cs     # 구조 JSON 로드/스폰
    ├── UI/QuestManagerSpatialUI.cs  # 진행률 패널
    ├── UI/StructureLevelBackButton.cs
    └── Editor/                      # Tools > Taming Mutants > ... 메뉴들
        ├── IntroSetupMenu.cs            # A계층 5종 스캐폴딩 + 인트로 오브젝트 생성
        ├── CftrRescueSetupMenu.cs       # 사건4 전용 오브젝트 3종 씬 배치
        └── AIAssistantSetupMenu.cs      # 비서 오브젝트 씬 배치
```
