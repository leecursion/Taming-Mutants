# 돌연변이 길들이기 — Desktop PC 우선 개발 가이드 (Meta 장비 없음)

## 진행 현황 (2026-07-28 기준)

이 문서는 계획서이자 현재 리포지토리의 실제 상태 기록입니다. 4장의 파일 구조와 하이어라키는 실제 상태에 맞춰 갱신되어 있으며, 아직 만들지 않은 항목은 `(예정)`으로 표시했습니다.

| 단계 | 상태 |
|---|---|
| 1. Unity 프로젝트 생성 (URP, PC 타겟) | **완료** — Unity 6000.0.79f1 / URP 17.0.4 |
| 2. Desktop 대체 컴포넌트 작성 | **스크립트만 완료** — 3개 파일 존재, `DesktopTablePlacement`만 프리팹에 부착됨 |
| 3. 가상 테이블 배치 | **완료** — `ExperimentTableRoot` 프리팹이 씬에 배치됨 |
| 4. AlphaFold 데이터 로드 → 단백질 시각화 | **완료 (MVP 1차 완료 지점)** — 원자 2,146개 로드 |
| 5. 마우스 회전/줌/원자 선택 | **미완** — `InteractionManager` 오브젝트가 씬에 없음 |
| 6. 홀로그램 퀘스트 UI 연결 | **미완** — `QuestPanel`이 빈 Canvas 상태 |
| 7. 레이어 분해 + 변이 하이라이트 + 퀘스트 로직 | **미완** — `MutationHighlighter` 배선 없음 |
| 8. AI Co-Scientist 연동 | **미착수** — 스크립트만 존재, 엔드포인트는 placeholder |
| 9~10. XR 전환 / 실기기 검증 | **미착수 (계획대로 보류)** |

### 지금 Lab_Desktop 씬을 Play하면 실제로 일어나는 일
- `ExperimentTableRoot`가 고정 좌표에 배치되고 `TableModel`이 홀로그램 머티리얼로 렌더링됨
- `ProteinAnchor_Main`의 `ProteinLoader`가 `P00533.json`을 읽어 원자 2,146개와 결합을 생성하고 pLDDT 색상을 적용함
- **마우스 조작(회전/줌/원자 선택)은 동작하지 않음** — 5단계 컴포넌트가 씬의 어떤 오브젝트에도 붙어 있지 않기 때문
- **변이 부위 하이라이트도 동작하지 않음** — `ProteinLoader.OnLoaded` → `MutationHighlighter.IndexAtoms()`를 이어주는 코드가 아직 없음

남은 배선 작업은 7장에 정리했습니다.

### 최근 변경 사항 (2026-08-20)

MVP 10단계 표와는 별도로 진행 중인 **환경 아트(배경/창문/천장) 작업** 기록입니다. 자세한 내용은 8장 참고.

- `BackgroundSetupMenu` / `WindowBackdropSetupMenu` 에디터 메뉴로 실험실 배경 glb 배치 + 창문 밖 우주 배경(별하늘)을 자동 생성하도록 구현.
- **미해결 이슈 진단 완료, 수정은 아직 미적용**: 창문을 통해 흰 천장과 우주 배경 사이의 경계선(seam)이 보이는 문제. 원인은 수작업으로 배치한 `Ceiling_Patch`(흰 평면, 12.2m×12.2m)가 `WindowBackdrop` 우주 박스 내부 중간 높이(y≈3.24)에 떠 있어서, 창문을 통과하는 시선이 이 평면을 스치듯 지나가며 흰색/우주 경계가 생기는 것. 진단 및 수정 절차는 [8.3](#83-알려진-문제-창문에-천장-우주-경계선seam이-보임)절 참고.

---

## 0. 현재 가능한 것 / 불가능한 것

**가능 (Desktop PC, Simulator 불필요)**
- 마우스+키보드 입력으로 전체 씬 실행, 카메라 이동, 오브젝트 회전/줌/선택
- AlphaFold 데이터 로드 → 단백질 시각화, pLDDT 색상, 변이 하이라이트
- 홀로그램 UI, 퀘스트 진행 로직, AI Co-Scientist 연동 테스트
- 화면 녹화(OBS 등)로 시연 영상 제작

**불가능 (실 기기 또는 최소 Simulator 필요)**
- 실제 손 제스처/시선 추적 트래킹 검증
- Passthrough, 실제 공간 스캔, Spatial Anchor 저장/로드 검증
- Quest 기준 90FPS 등 온디바이스 성능 검증

**결론**: 지금 단계(MVP: 데이터 로드 + 배경/테이블 + 기본 상호작용)는 **Meta XR SDK나 Simulator 설치 없이 순수 Desktop PC 3D 씬**으로 전부 구현하고 녹화까지 가능합니다. Meta XR SDK 설치는 이후 실 기기 검증 단계에서만 필요합니다. 상호작용 코드는 처음부터 **인터페이스로 분리**해 두어, 나중에 헤드셋이 생기면 구현체만 교체합니다.

---

## 1. 설계 원칙: 컴포넌트 교체 방식 (Desktop ↔ XR 전환 가능하게)

입력/배치 로직은 **필드명과 이벤트가 동일한 한 쌍의 컴포넌트**로 만들어서, 나중에 헤드셋이 생기면 Desktop용 컴포넌트를 끄고 XR용 컴포넌트를 켜기만 하면 되도록 설계합니다. **지금은 왼쪽(Desktop) 구현체만 필요합니다.**

| 역할 | Desktop 구현체 (지금 사용) | XR 구현체 (나중, 실기기/Simulator) | 교체 방법 |
|---|---|---|---|
| 회전/줌 (F-02.2) | `DesktopFallbackController.cs` | `HandGestureController.cs` | 둘 다 `target` 필드를 가짐 → 컴포넌트만 교체 |
| 원자 선택 (F-04.1 대체) | `MouseWorldSelector.cs` | Gaze Interactor 기반 셀렉터 | 둘 다 `MutationHighlighter.SelectResidue()` 호출 |
| 오브젝트 배치 (F-01.1) | `DesktopTablePlacement.cs` | `ExperimentTableAnchor.cs` | 둘 다 `OnPlacementConfirmed` 이벤트 제공 |
| 카메라/이동 | Main Camera (기본 Unity 카메라) | Meta Camera Rig (OVRCameraRig) | 씬 전환 시 카메라만 교체 |

이렇게 해두면 나머지 로직(ProteinLoader, PLDDTColorizer, MutationHighlighter, QuestManagerSpatialUI, AICoScientistClient 등)은 **입력 방식이 무엇이든 전혀 수정할 필요가 없습니다.**

---

## 2. 단계별 진행 순서

각 단계 옆에 실행 가능 환경을 표시합니다. **지금 시점에서는 전부 "PC"만으로 진행 가능합니다.**

| 단계 | 내용 | 실행 환경 | 현황 |
|---|---|---|---|
| 1 | Unity 프로젝트 생성 (Standalone/PC 빌드 타겟, URP) | **PC** | 완료 |
| 2 | Desktop 대체 컴포넌트 작성 (마우스 회전/줌, 원자 선택, 테이블 고정 배치) | **PC** | 스크립트 완료 / 씬 부착은 5단계에서 |
| 3 | 가상 테이블 배치 (고정 좌표, 앵커링 없이) | **PC** | 완료 |
| 4 | AlphaFold 데이터 로드 → 단백질 시각화 *(MVP 1차 완료 지점)* | **PC** | 완료 |
| 5 | 마우스로 회전/줌/원자 선택 상호작용 | **PC** | **← 다음 작업** |
| 6 | 홀로그램 UI(퀘스트 진행 패널) 연결 *(MVP 2차 완료 지점: 데이터+배경 종료)* | **PC** | 미완 |
| 7 | 단백질 레이어 분해(F-03.2) + 변이 하이라이트(F-02.3) + Quest 1~5 진행 로직 | **PC** | 미완 |
| 8 | AI Co-Scientist(GPT-4o, 백엔드 프록시) 연동 | **PC** | 미착수 |
| 9 | (이후 단계) Meta XR SDK 설치, 인터페이스를 XR 구현체로 교체 | **Simulator 또는 실기기** — 지금은 스킵 | 미착수 |
| 10 | (이후 단계) Passthrough/Spatial Anchor/손 제스처 실기기 검증 | **실기기 필수** — 지금은 스킵 | 미착수 |

> 7단계까지 끝나면(퀘스트 로직 포함) **Desktop PC만으로 전체 신약개발 퀘스트 흐름을 처음부터 끝까지 녹화 시연**할 수 있습니다. 8~10단계는 헤드셋이 확보된 이후 진행하면 됩니다.

---

## 3. Desktop PC 환경 세팅 (지금 필요한 것만)

1. **Unity Hub → Unity 6 (URP 템플릿)** 설치
2. `File > Build Settings` → 플랫폼을 **PC, Mac & Linux Standalone**으로 유지 (Android로 전환하지 않음 — 그건 8단계 이후)
3. XR Plug-in Management, Meta XR SDK, OpenXR — **지금은 설치하지 않습니다.** (나중에 9단계에서 추가)
4. 필요한 패키지: `DOTween`(Asset Store, 무료), `Newtonsoft Json.NET`(Package Manager), 표준 Unity `Input System` 또는 레거시 Input Manager
5. 화면 녹화: OBS Studio 등으로 Game View를 녹화하면 바로 시연 영상 확보

이것으로 끝입니다. Meta 계정, Quest 개발자 모드, Simulator 설치는 전부 9단계 이후로 미룹니다.

### 3.1 실제 설치 상태 (완료)

| 항목 | 실제 버전 / 위치 |
|---|---|
| Unity 에디터 | 6000.0.79f1 |
| Universal RP | 17.0.4 (`Assets/Settings/`에 PC·Mobile 렌더러 에셋) |
| Input System | 1.19.0 (`Assets/InputSystem_Actions.inputactions`) |
| Newtonsoft Json.NET | 3.2.2 (`com.unity.nuget.newtonsoft-json`) |
| DOTween | `Assets/Plugins/Demigiant/DOTween/`, 설정은 `Assets/Resources/DOTweenSettings.asset` |
| XR / Meta XR SDK | **미설치 (의도된 상태)** — `Packages/manifest.json`에 XR 플러그인 없음 |
| 빌드 타겟 | PC, Mac & Linux Standalone 유지 |

> 참고: 현재 `ProteinLoader`는 `JsonUtility`로 파싱하므로 Newtonsoft는 아직 사용처가 없습니다. AI Co-Scientist 응답 파싱(8단계)에서 쓰게 됩니다.
>
> 참고: DOTween은 설치되어 있지만 `SceneTransitionManager.cs`는 파일 전체가 `/* */`로 주석 처리된 상태입니다(6장 표 참조).

---

## 4. Hierarchy / Assets 파일 구조

### 4.1 Assets 폴더 구조 (실제 상태)
```
Assets/
├── Scripts/
│   ├── Desktop/                     # 지금 사용하는 구현체 (PC 전용)
│   │   ├── DesktopFallbackController.cs   # 마우스 우클릭 드래그 회전 + 휠 줌 — 씬 미부착
│   │   ├── MouseWorldSelector.cs          # 마우스 좌클릭 레이캐스트로 원자 선택 — 씬 미부착
│   │   └── DesktopTablePlacement.cs       # 테이블 고정 좌표 배치 — ExperimentTableRoot에 부착됨
│   ├── XR/                          # (예정) 9단계 이후 - 현재 폴더만 있고 파일 없음
│   │   ├── HandGestureController.cs       # (예정) DesktopFallbackController와 동일한 target 필드
│   │   └── ExperimentTableAnchor.cs       # (예정) DesktopTablePlacement와 동일한 OnPlacementConfirmed 이벤트
│   ├── Protein/
│   │   ├── ProteinLoader.cs               # ProteinAnchor_Main에 부착됨
│   │   ├── PLDDTColorizer.cs              # Atom.prefab에 부착됨
│   │   ├── AtomInfo.cs                    # Atom.prefab에 부착됨
│   │   ├── ComparisonController.cs        # 작성만 완료 - 비교 대상 앵커가 아직 없어 사용처 없음
│   │   └── MutationHighlighter.cs         # 작성만 완료 - 씬 미부착, IndexAtoms 호출부 없음
│   ├── UI/
│   │   └── QuestManagerSpatialUI.cs       # 작성만 완료 - 씬 미부착
│   ├── AI/                          # 비서(Brain/Follower/SpeechBubble 등) + LLM 클라이언트 — 6장 표 참고
│   ├── Intro/
│   │   └── IntroDirector.cs               # 인트로 진행 총괄 — 6장 표, 7장 참고
│   ├── Quest/                       # 퀘스트/도킹/후보물질 — 6장 표, 7장 6번 항목 참고
│   ├── Editor/                      # 에디터 전용 배치 메뉴(Tools > Taming Mutants > ...) — 6장 표, 8장 참고
│   └── SceneManagement/
│       └── SceneTransitionManager.cs      # 파일 전체가 /* */ 로 주석 처리된 상태
├── Shaders/
│   ├── Hologram.shader
│   └── SpaceBackdrop.shader         # 창문 밖 우주 배경 — 8.2절
├── Materials/
│   ├── Hologram_Blue.mat            # TableModel에 적용됨
│   ├── WindowBackdrop_Space.mat     # 창문 밖 우주 배경 — 8.2절
│   └── Ceiling_White.mat            # 천장 구멍 패치용 — 8.3절 (알려진 문제의 원인)
├── Models/
│   └── CHEMISTRYlabdemoscene.glb    # 실험실 배경 원본 모델 — 8.1절
├── Quests/
│   └── QuestCatalog.asset           # 인트로 보드용 QuestDefinition[] 카탈로그 — 7장 6번 ①
├── Prefabs/
│   ├── ExperimentTableRoot.prefab   # 초안의 ExperimentTable.prefab → 씬 루트와 이름을 맞춰 변경
│   ├── Atom.prefab                  # Sphere + SphereCollider + PLDDTColorizer + AtomInfo (URP Lit 기본 머티리얼)
│   ├── Bond.prefab                  # Cylinder + CapsuleCollider
│   └── QuestPanel.prefab            # (예정) 아직 프리팹화하지 않음 - 씬 안 오브젝트로만 존재
├── Plugins/
│   └── Demigiant/DOTween/           # DOTween 설치 위치
├── Resources/
│   └── DOTweenSettings.asset
├── Settings/                        # URP 렌더러/RP 에셋 (템플릿 기본)
├── StreamingAssets/
│   ├── structures/
│   │   └── P00533.json              # 5장 2절 전처리 결과물 (원자 2,146개, 잔기 712~979)
│   ├── compounds/                   # 후보물질 JSON — 7장 6번 ②
│   └── quests/                      # 도킹 퀘스트 정의 JSON + index.json — 7장 6번 ②
└── Scenes/
    ├── Lab_Desktop.unity            # 지금 작업하는 메인 씬 (PC 전용)
    ├── SampleScene.unity            # URP 템플릿 기본 씬 (미사용)
    └── Lab_XR.unity                 # (예정) 9단계 이후 XR 씬 - 아직 생성하지 않음
```

> `Scripts/AI`, `Scripts/Intro`, `Scripts/Quest`, `Scripts/Editor`는 4단계 이후 새로 추가된 폴더라 파일 단위로는 나열하지 않았습니다 — 파일별 상태는 6장 스크립트 표를 참고하세요.

Unity `Assets/` 바깥, 프로젝트 루트에 있는 파일들:
```
Taming-Mutants/
├── pdb_parser_script.py             # 5장 2절 전처리 스크립트의 실제 파일
├── structures/P00533.pdb            # AlphaFold에서 받은 원본 PDB (전처리 입력)
├── docs/guide.md                    # 이 문서
└── figures/                         # 카메라 스케일 & 화면 전환 프로세스 설계 다이어그램
```

### 4.2 씬 내 Hierarchy 구조 (Lab_Desktop.unity)

**현재 실제 하이어라키:**
```
Lab_Desktop (Scene)
├── Main Camera                       # 위치 (0, 1.5, -2), X축 10도 하향 (테이블을 내려다봄)
├── Lighting                          # Directional Light 1개, Intensity 2
├── EventSystem                       # World Space Canvas 클릭 처리에 필요
├── Global Volume                     # URP 템플릿 기본 포스트프로세싱 볼륨
├── QuestPanel                        # World Space Canvas만 있는 빈 상태 (자식 UI·스크립트 없음)
└── ExperimentTableRoot               # 프리팹 인스턴스 + DesktopTablePlacement (placedPosition = 0, 0.8, 1.2)
    ├── TableModel                    # 테이블 메쉬 + Hologram_Blue 머티리얼
    └── ProteinAnchor_Main            # ProteinLoader (structures/P00533.json, atomScale 0.25, 결합 1.9Å)
```

**아직 만들지 않은 오브젝트 (5~8단계에서 추가):**

| 오브젝트 | 붙일 컴포넌트 | 필요 단계 |
|---|---|---|
| `InteractionManager` | `MouseWorldSelector` + `DesktopFallbackController` (target = `ProteinAnchor_Main`) | 5 |
| `QuestPanel` 내용 채우기 | `QuestManagerSpatialUI` + 자식 Text/Panel, Hologram 머티리얼 | 6 |
| `MutationHighlighter` | `ExperimentTableRoot` 또는 `ProteinAnchor_Main`에 컴포넌트로 부착 | 7 |
| `ProteinAnchor_Mutant` | 두 번째 `ProteinLoader` + `ComparisonController`(master/slave) | 7 |
| `AICoScientistManager` | `AICoScientistClient` | 8 |

> 초안에서는 `ProteinAnchor_WildType` / `ProteinAnchor_Mutant` 두 개를 두기로 했으나, 현재는 단일 구조만 표시하는 4단계까지만 구현했으므로 앵커가 `ProteinAnchor_Main` 하나뿐입니다. 비교 관찰(F-03.4)을 붙이는 7단계에서 Wild Type / Mutant 2개 체계로 바꾸고 `ComparisonController`를 연결합니다.

> `InteractionManager`에 `MouseWorldSelector`(원자 선택)와 `DesktopFallbackController`(회전/줌 대상 조작)를 함께 붙입니다. 두 컴포넌트는 마우스 버튼이 겹치지 않게 분리되어 있습니다 — 선택은 좌클릭, 회전은 우클릭(`rotateMouseButton = 1`). 나중에 XR로 전환할 때는 이 두 컴포넌트를 비활성화하고 `HandGestureController`(동일한 `target` 필드)와 Gaze 기반 셀렉터를 활성화하면 됩니다.

---

## 5. AlphaFold PDB 데이터 가져오기

### 5.1 AlphaFold DB API 개요
UniProt Accession(예: `P00533`)을 기준으로 예측 구조를 제공합니다.

**Step 1. 메타데이터 조회**
```
GET https://alphafold.ebi.ac.uk/api/prediction/{UniProt_ID}
```
응답 예시(일부):
```json
[
  {
    "entryId": "AF-P00533-F1",
    "uniprotAccession": "P00533",
    "latestVersion": 4,
    "pdbUrl": "https://alphafold.ebi.ac.uk/files/AF-P00533-F1-model_v4.pdb",
    "confidenceType": "pLDDT"
  }
]
```
`pdbUrl`은 항상 메타데이터 응답에서 그대로 가져다 쓰고 버전 번호를 하드코딩하지 않습니다.

### 5.2 Python 사전 파싱 (구현 완료)

실제 스크립트는 프로젝트 루트의 **[`pdb_parser_script.py`](../pdb_parser_script.py)** 입니다. 필요 패키지: `pip install biopython requests`

```bash
python pdb_parser_script.py     # UNIPROT_ID = "P00533" (EGFR)
```

실행하면 `structures/P00533.pdb`(원본)와 `Assets/StreamingAssets/structures/P00533.json`(Unity가 읽는 결과물)이 함께 생성됩니다. Unity가 읽는 위치에 바로 쓰므로 수동 복사 단계가 없습니다.

초안 스니펫에서 실제 구현이 달라진 부분:

| 항목 | 내용 |
|---|---|
| `residue_range` 필터 | 전체 1,210 잔기 대신 **키나아제 도메인 712~979**만 추출 → 원자 2,146개. 전체를 로드하면 원자 수가 너무 많아 데스크톱에서도 무거워짐 |
| centroid 재배치 | 모든 원자 좌표에서 중심점을 빼서 **원점 기준**으로 옮김. 앵커에 그대로 붙여도 화면 밖으로 벗어나지 않음 |
| `is_mutation_site` 필드 추가 | 잔기 858, 790(L858R / T790M)에 해당하면 `true` — 해당 원자 15개 |
| float 변환 | `atom.coord`(numpy.float32)를 `float()`로 먼저 변환. 안 하면 `json.dump`가 TypeError로 죽음 |
| 원자적 저장 | 임시 파일에 쓴 뒤 `os.replace`로 교체하고, 저장 직후 다시 `json.load`로 검증 |

**주의:** `is_mutation_site`는 JSON에 들어 있지만 `ProteinLoader.AtomRecord`에는 대응 필드가 없어 현재 무시됩니다. 7단계에서 변이 하이라이트를 붙일 때 `AtomRecord`에 필드를 추가하거나, `MutationHighlighter.mutationSites`에 잔기 858·790을 인스펙터로 직접 넣는 방식 중 하나를 선택해야 합니다.

### 5.3 생성되는 JSON 스키마
```json
{"atoms": [
  {"name": "CA", "element": "C", "x": 1.234, "y": -5.678, "z": 9.012,
   "bfactor": 92.31, "res_name": "LEU", "res_id": 858,
   "is_backbone": true, "is_mutation_site": true}
]}
```
`bfactor`에 pLDDT 값이 담기며, `ProteinLoader`가 이 값을 `PLDDTColorizer.ApplyConfidence()`로 넘겨 색상을 결정합니다. 좌표 단위는 옹스트롬이고 `ProteinLoader`가 씬에 배치할 때 0.1배로 축소합니다.

---

## 6. 스크립트 목록

| 파일 | 명세서 대응 | 환경 | 현황 |
|---|---|---|---|
| `DesktopFallbackController.cs` | F-02.2 대체(마우스 드래그 회전 + 휠 줌) | **PC** | 작성 완료 / **씬 미부착** |
| `MouseWorldSelector.cs` | F-04.1 대체(마우스 클릭으로 원자/변이 선택) | **PC** | 작성 완료 / **씬 미부착** |
| `DesktopTablePlacement.cs` | F-01.1 대체(고정 좌표 배치) | **PC** | **동작 중** (ExperimentTableRoot) |
| `ProteinLoader.cs` | F-03.1 | PC / XR 공통 | **동작 중** (ProteinAnchor_Main) |
| `PLDDTColorizer.cs` | F-03.3 | PC / XR 공통 | **동작 중** (Atom.prefab) |
| `AtomInfo.cs` | 보조 | PC / XR 공통 | **동작 중** (Atom.prefab) |
| `ComparisonController.cs` | F-03.4 | PC / XR 공통 | 작성 완료 / 사용처 없음 |
| `MutationHighlighter.cs` | F-02.3, F-02.4 | PC / XR 공통 | 작성 완료 / **씬 미부착 + IndexAtoms 호출부 없음** |
| `QuestManagerSpatialUI.cs` | F-01.3 | PC / XR 공통 | 작성 완료 / **씬 미부착** |
| `AICoScientistClient.cs` | F-06 | PC / XR 공통 | 작성 완료 / 미부착, 엔드포인트 placeholder |
| `SceneTransitionManager.cs` | F-05.1 | PC / XR 공통 | **파일 전체 주석 처리 상태** |
| `Hologram.shader` | F-01.2, F-01.3, F-02.1 등 | PC / XR 공통 | **동작 중** (Hologram_Blue.mat) |
| `HandGestureController.cs` | F-02.2 (실기기용, 지금은 미사용) | Simulator/실기기 | **미작성 (예정)** |
| `ExperimentTableAnchor.cs` | F-01.1 (실기기용, 지금은 미사용) | Simulator/실기기 | **미작성 (예정)** |
| `Quest/CompoundData.cs` | F-04 후보물질 데이터 모델 | PC / XR 공통 | 작성 완료 |
| `Quest/CompoundMoleculeBuilder.cs` | F-04 화합물 3D 분자 생성 (CPK 색) | PC / XR 공통 | 작성 완료 |
| `Quest/CompoundSlot.cs` | F-04.2 선택 박스 1칸 (와이어프레임 + 분자) | PC / XR 공통 | 작성 완료 (패널이 런타임 생성) |
| `Quest/CompoundSelectionPanel.cs` | F-04.2 후보물질 선택 패널 (박스 4개 + 라벨) | PC(마우스) / XR(`SelectSlot()` 호출) | 작성 완료 / **씬 미부착** |
| `Quest/DockingQuestController.cs` | F-04.3 도킹 연출 + 포켓 하이라이트 + 결과 판정 | PC / XR 공통 | 작성 완료 / **씬 미부착** |
| `Quest/DockingQuestDefinition.cs` | F-04 퀘스트 정의 JSON 스키마 | PC / XR 공통 | 작성 완료 |
| `Quest/DockingQuestCatalog.cs` | F-04 도킹 퀘스트 카탈로그 — `StreamingAssets/quests/index.json`을 읽어 시작·자동 전환. **과거 이 표에 "QuestCatalog"로 적혀 있던 MonoBehaviour가 이 이름으로 분리됨** | PC / XR 공통 | **동작 중** |
| `Quest/QuestCatalog.cs` | 인트로 퀘스트 보드용 카탈로그 **에셋**(`ScriptableObject`, `Assets/Quests/QuestCatalog.asset`). `QuestDefinition[]`을 담을 뿐이며 위 `DockingQuestCatalog`(런타임 컴포넌트)와는 완전히 다른 클래스 — 이름이 같아 혼동 주의 | PC / XR 공통 | **동작 중** |
| `Quest/RuntimeMaterials.cs` | 런타임 생성 오브젝트(후보물질 패널 등)에 쓰는 공용 머티리얼 헬퍼 | PC / XR 공통 | **동작 중** |
| `Quest/WireBox.cs` | 와이어프레임 박스 공용 빌더 | PC / XR 공통 | 작성 완료 |
| `AI/AIAssistantBrain.cs` | AI 비서 상태 관리 + 대사 재생(`SpeakGreeting`/`SpeakNow`) + Solar LLM 연동 | PC / XR 공통 | **동작 중** |
| `Intro/IntroDirector.cs` | 인트로 진행 총괄 (페이드 → 비서 인사 → 퀘스트 보드 → 선택 → 퀘스트 시작) | **PC** | **동작 중** |
| `Editor/BackgroundSetupMenu.cs` | 메뉴 `Tools > Taming Mutants > 배경(실험실 모델) 배치` | 에디터 전용 | **동작 중** (8.1절) |
| `Editor/WindowBackdropSetupMenu.cs` | 메뉴 `Tools > Taming Mutants > 창문 우주 배경 마감` | 에디터 전용 | **동작 중** (8.2절) |
| `Editor/IntroSetupMenu.cs` | 메뉴 `Tools > Taming Mutants > 인트로 + 퀘스트 카탈로그 생성` — KRAS/EGFR 기본 `QuestDefinition` 2종 + 인트로 오브젝트 생성 (재실행 안전) | 에디터 전용 | **동작 중** (9.1절) |
| `Editor/AIAssistantSetupMenu.cs` | AI 비서 오브젝트/컴포넌트 배치 메뉴 | 에디터 전용 | **동작 중** |
| `Shaders/SpaceBackdrop.shader` | 창문 밖 절차적 별하늘(`Custom/SpaceBackdrop`, Cull Front, 텍스처 없는 해시 기반 별) | PC / XR 공통 | **동작 중** (8.2절) |
| `UI/StructureLevelBackButton.cs` | 표시 레벨 뒤로가기 버튼 (화면 하단, 런타임 생성) | **PC** (XR은 월드 버튼으로 교체 예정) | 작성 완료 / **씬 미부착** |

> 표에서 "PC / XR 공통"인 스크립트들은 입력 방식과 무관하게 동작하므로 지금 만들어두면 나중에 그대로 재사용됩니다. "실기기용" 두 개는 9단계 이후에만 작성해서 씬에 추가하면 됩니다.

---

## 7. 다음 작업 (남은 배선 목록)

스크립트는 대부분 작성돼 있고, 실제로 막혀 있는 것은 **씬 배선**입니다. 위에서부터 순서대로 처리하면 5~7단계가 끝납니다.

1. **InteractionManager 오브젝트 생성 (5단계)**
   빈 GameObject를 만들어 `DesktopFallbackController`(target = `ProteinAnchor_Main`)와 `MouseWorldSelector`(targetCamera 비워두면 `Camera.main`)를 붙입니다. 이것만 하면 마우스 회전·줌·원자 선택이 바로 동작합니다.

2. **MutationHighlighter 배선 (7단계)**
   현재 `ProteinLoader.OnLoaded` 이벤트를 구독하는 코드가 프로젝트에 한 줄도 없습니다. 로드 완료 후 생성된 `AtomInfo`들을 모아 `MutationHighlighter.IndexAtoms()`에 넘기는 글루 코드를 작성하고, 컴포넌트를 씬에 붙인 뒤 `MouseWorldSelector.mutationHighlighter`에 연결해야 합니다. 변이 부위는 EGFR L858R(858), T790M(790).

3. **QuestPanel 채우기 (6단계)**
   지금은 Canvas·CanvasScaler·GraphicRaycaster만 있는 빈 오브젝트입니다. `QuestManagerSpatialUI`를 붙이고 스테이지별 자식 패널/텍스트를 만든 뒤 배경에 Hologram 머티리얼을 적용합니다.

4. **SceneTransitionManager 주석 해제 (7~8단계)**
   DOTween이 실제로 설치돼 있으므로 `/* */`를 풀면 컴파일될 가능성이 높습니다. 컴파일 확인 후 `cameraRig` 등 참조를 채웁니다.

5. **Wild Type / Mutant 2구조로 확장 (7단계)**
   `ProteinAnchor_Mutant`를 추가하고 `ComparisonController`로 회전/스케일을 동기화합니다.

6. **F-04 후보물질 도킹 퀘스트 배선 (KRAS G12C) — 실행 방법**
   모든 퀘스트 내용(단백질/타깃 잔기/후보물질)은 `StreamingAssets/quests/*.json`에 정의되며 `DockingQuestCatalog`가 진행을 총괄합니다. 씬 배선은 컴포넌트 3개 부착 + 참조 연결이 전부입니다:
   1. **CompoundPanel**: 빈 GameObject를 만들고 `CompoundSelectionPanel` 부착. `atomPrefab`/`bondPrefab`에 기존 Atom/Bond 프리팹을 연결. 배치는 **단백질 원자들의 실측 경계(bounds)를 기준으로 "왼쪽 옆 + 상단 높이 일치 + 사선(diagonalYaw)"에 자동 계산**됨 — `DockingQuestCatalog`를 쓰면 `proteinLoader`/`levelController` 참조도 자동 배선되므로 추가 연결 불필요. `levelController`가 연결되면 **아미노산(원자) 레벨에서만 패널이 표시**됨. 화합물 4개는 하나의 외곽 박스 안에 2x2 그리드(columns=2), 원자는 "홀로-오브" 스타일(CPK 발광 코어 + Custom/Hologram 프레넬 셸). **한글 결과 메시지를 위해 `labelFont`에 한글 폰트(예: NotoSansKR) 지정 필수** (내장 폰트는 한글 글리프 없음). 간격·높이 미세조정: `sideGap`, `topOffset`. **⚠ 이 사선 배치는 사용자가 명시적으로 확정한 값입니다 — 임의로 레이아웃(왼쪽/사선/높이 정렬)을 바꾸지 마세요.**
   1-1. **BackButton**: 빈 GameObject에 `StructureLevelBackButton` 부착 — `levelController`는 비워둬도 씬에서 자동 탐색. 화면 하단 중앙에 "◀ Back" 버튼이 항상 표시되며, 리본(최상위) 레벨에서는 회색 비활성, Helix/아미노산 레벨에서 클릭 시 이전 레벨로 복귀(Esc 키와 동일).
   1-2. **리본/Helix/결합의 "실제" 표시**: Bond.prefab이 Hologram_Blue.mat을 쓰므로 세그먼트가 홀로그램으로 보였음 → `StructureLevelController.solidSegments`(기본 켜짐)와 `ProteinLoader.solidBonds`(기본 켜짐)가 URP Lit 불투명 재질로 자동 교체. 원래 홀로그램 룩을 원하면 체크 해제.
   2. **DockingQuest**: 빈 GameObject에 `DockingQuestController` 부착 → `proteinLoader`(ProteinAnchor_Main), `selectionPanel`, (있다면) `levelController`, `questUI` 연결.
   3. **DockingQuestCatalog**: 빈 GameObject에 `DockingQuestCatalog` 부착(구 설계에서는 이 컴포넌트 이름이 `QuestCatalog`였으나, 인트로 보드용 `QuestCatalog`(ScriptableObject, 아래 참고)와 이름이 겹쳐 `DockingQuestCatalog`로 분리됨) → `proteinLoader`, `selectionPanel`, `dockingController`, (있다면) `levelController`, `questSession`(인트로가 있는 씬이면 자동 탐색됨) 연결. Play 시 `quests/index.json`을 읽어 퀘스트를 로드합니다 — 인트로(`QuestSession`)가 있으면 사용자가 보드에서 고른 퀘스트에 맞는 정의를 적용하고, 없으면 첫 퀘스트를 자동 시작합니다(`autoStartFirstQuest`). ProteinLoader의 기존 자동 로드는 카탈로그가 알아서 끕니다(`loadOnStart=false` 처리).
   4. 플레이 흐름: 박스 클릭 → 포켓 잔기 시안색 펄스 → 분자 비행 → 결과별 연출(성공: 타깃 잔기 섬광 + 녹색 공유결합 + 포켓 락인 / 오답: 튕김·충돌 셸·자석 반발 후 재도전) → 메시지·ΔG 표시. 성공 시 `QuestManagerSpatialUI` 단계 완료 + (설정 시) 다음 퀘스트 자동 전환.

   **새 퀘스트/새 단백질 구조 추가 절차 — 두 계층을 모두 채워야 합니다.**

   현재 구조에는 콘텐츠가 두 군데로 나뉘어 있습니다: ①인트로 퀘스트 보드에 카드로 띄울 **서사/대사 데이터**(`QuestDefinition` 에셋)와, ②실제 도킹 플레이를 구동하는 **구조/포켓/화합물 데이터**(`StreamingAssets/quests/*.json`). 인트로 보드에서 고를 수 있게 하려면 ①을, 실제로 플레이가 되게 하려면 ②를 만들고, `questId`(또는 `protein_json` ↔ `structureStreamingPath`)로 서로 매칭시켜야 `DockingQuestCatalog.ApplyForSessionQuest`가 둘을 연결합니다.

   **① 인트로 보드용 `QuestDefinition` (코드 수정 불필요, 에디터 작업만)**
   1. Project 창 우클릭 → `Create > Taming Mutants > Quest Definition` → `Assets/Quests/` 아래 저장 (예: `Quest_EGFR_L858R.asset`)
   2. Inspector에서 `questId`(도킹 JSON의 `id`와 반드시 동일하게), `title`/`subtitle`/`gene`/`mutation`/`summary`/`difficulty`/`accent`, `structureStreamingPath`(도킹 JSON의 `protein_json`과 동일 경로), `mutationResidueIds`, `targetPocketLabel`을 채웁니다.
   3. `stages`(단계별 비서 대사·힌트·LLM 컨텍스트)와 `candidates`(후보물질 카드 설명)는 선택 사항이지만, 채워두면 `AIAssistantBrain`이 그 대사를 그대로 재생합니다.
   4. `Assets/Quests/QuestCatalog.asset`을 선택 → `quests` 배열에 방금 만든 에셋을 드래그해 추가. **이 배열에 없으면 인트로 보드에 카드 자체가 뜨지 않습니다.**
   5. (참고) `IntroSetupMenu`의 메뉴 `Tools > Taming Mutants > 인트로 + 퀘스트 카탈로그 생성`은 KRAS/EGFR 두 종류를 하드코딩해서 채우는 초기 스캐폴딩 도구입니다 — 완전히 새로운 퀘스트를 추가할 때는 이 메뉴가 아니라 위 1~4단계를 수동으로 합니다.

   **② 도킹 플레이용 데이터 (JSON, 코드·씬 수정 불필요)**
   1. 구조 JSON 생성: `python pdb_parser_script.py <UniProtID>` — `CONFIGS`에 잔기 범위·변이 부위만 등록 (변이 원자 치환이 필요하면 G12C처럼 후처리)
   2. 후보물질 JSON들을 `StreamingAssets/compounds/`에 추가 (스키마: `CompoundData.cs` 참고, outcome은 Success/NoWarhead/StericClash/OffTarget)
   3. 퀘스트 정의 JSON 1개를 `StreamingAssets/quests/`에 작성 (`kras_g12c.json`을 복사해 `id`·`protein_json`·`target_residue_id`·`pocket_residue_ids`·`compound_files`·`helix_regions`만 교체 — `id`는 ①의 `questId`와 동일하게)
   4. `quests/index.json` 목록에 파일명 추가 — 나열 순서가 (인트로 없이 단독 실행할 때의) 진행 순서

   > 인트로 흐름 없이 도킹 씬만 단독 테스트하려면 ①은 생략하고 ②만으로 `DockingQuestCatalog.autoStartFirstQuest`가 첫 퀘스트를 자동 시작합니다.

### 알아둘 만한 사항
- `ProteinLoader.BuildBonds`는 O(n²)로 원자 쌍을 전부 훑습니다. 현재 2,146개 원자 기준 약 230만 쌍이며 로딩 시 1회만 수행하므로 데스크톱에서는 문제없지만, 잔기 범위를 넓히거나 Quest 실기기로 넘어갈 때는 공간 분할(그리드 해싱)이 필요합니다.
- `Bond.prefab`에 CapsuleCollider가 있어 결합 실린더가 원자 클릭을 가로챌 수 있습니다. 선택이 부정확하면 `MouseWorldSelector.selectableLayers`로 원자 레이어만 지정하거나 Bond의 콜라이더를 제거하세요.

---

## 8. 환경 아트: 배경 / 창문 우주 배경 / 천장

실험실 배경은 코드로 절차 생성하지 않고, **glb 모델 배치 + 에디터 메뉴 2개**로 구성합니다. 둘 다 재실행해도 안전합니다(기존 오브젝트를 지우고 다시 만듦).

### 8.1 배경 모델 배치 — `BackgroundSetupMenu`

- 메뉴: `Tools > Taming Mutants > 배경(실험실 모델) 배치`
- `Assets/Models/CHEMISTRYlabdemoscene.glb`를 `Background_ChemistryLab`이라는 이름으로 씬에 인스턴스화합니다. glb 임포트에는 glTFast 패키지(`com.unity.cloud.gltfast`, `Packages/manifest.json`에 등록됨)가 필요합니다.
- 배치 후 카메라(`0, 1.5, -4`)와 테이블(`0, 0.8, -1`) 기준으로 위치/회전/크기를 수동 조정해야 합니다 — 이 메뉴는 모델을 원점에 놓기만 합니다.

### 8.2 창문 밖 우주 배경 — `WindowBackdropSetupMenu` + `SpaceBackdrop.shader`

- 메뉴: `Tools > Taming Mutants > 창문 우주 배경 마감`
- 동작 원리: `Background_ChemistryLab`의 모든 렌더러 bounds를 측정 → 그 전체를 넉넉히(margin 6m) 감싸는 큰 박스(`WindowBackdrop`)를 생성 → 안쪽 면에 `Custom/SpaceBackdrop` 머티리얼(`Assets/Materials/WindowBackdrop_Space.mat`)을 입힙니다.
- 셰이더가 `Cull Front`라 **박스 안쪽에서만** 보입니다 — 창문이 어느 방향에 있든 그 너머로 별하늘이 보이고, 박스 바깥(카메라가 밖으로 나갈 일은 없지만)에서는 아무것도 안 보입니다. 텍스처 없이 해시 함수로 별을 찍는 절차적 셰이더입니다.
- 별하늘 룩 조정: `WindowBackdrop_Space.mat`(또는 스크립트의 상수)에서 `_StarDensity`(격자 촘촘함), `_StarSize`(별 크기, 작을수록 점처럼 작아짐), `_StarChance`(별이 나올 확률), `_TwinkleSpeed`(반짝임 속도), `_SkyColor`/`_StarColor`를 조정합니다. Play 모드가 아니어도 Scene 뷰에서 즉시 반영됩니다.
- 이전 버전에서 만들었던 벽-천장 인방(lintel) 트림(`Ceiling_WallTrim`)은 창문 위에 원치 않는 막대로 보인다는 피드백으로 제거되었고, `Setup()`을 재실행하면 과거 산출물도 함께 정리됩니다.

### 8.3 알려진 문제 — 창문에 천장/우주 경계선(seam)이 보임

**증상**: 창문을 통해 밖을 보면 위쪽은 흰 천장색, 아래쪽은 우주(별하늘)로 화면이 갈라져 보임 (2026-08-20 스크린샷으로 확인).

**원인**: `Ceiling_Patch`(`Assets/Materials/Ceiling_White.mat`를 쓰는 흰 평면, 스크립트가 아니라 에디터에서 수작업 배치 — 글b 모델 천장의 구멍을 가리기 위한 패치)가 `WindowBackdrop` 우주 박스 **내부의 중간 높이**(y≈3.24, 박스 안쪽 천장은 y≈6.24)에 12.2m×12.2m 크기로 떠 있습니다. 창문을 통과하는 시선이 이 패치 평면을 스치듯 지나가면 흰색이, 패치 범위를 벗어나면 그 뒤의 우주 박스가 보여서 경계선이 생깁니다.

**수정 방법 (미적용 — 에디터에서 눈으로 보며 수동 조정 필요)**:
1. `Ceiling_Patch`를 잠깐 비활성화하고 Scene 뷰를 천장 위로 올려 실제 천장 구멍의 위치/크기를 확인, 다시 켬
2. `Background_ChemistryLab` 아래에서 창문 메쉬를 찾아 월드 좌표를 확인 (창문이 여러 개면 전부)
3. Scene/Game 뷰를 같이 띄운 상태로 `Ceiling_Patch`의 `Transform.Scale.x`/`Scale.z`(현재 1.2219 — Plane 메쉬 기본 10×10 기준이므로 실면적 = 값×10m)를 줄이거나, `Position.x`/`Position.z`를 창문 반대쪽으로 밀면서 창문에서 흰 띠가 사라지는 지점을 찾음
4. 조정 후 천장 위에서 다시 내려다봐서 원래 구멍이 여전히 안 가려지는지, 모든 창문에서 문제가 재현되지 않는지 재확인 후 씬 저장

자세한 진단 과정은 이 문서를 만든 대화 세션 기록 참고. 아직 수치가 확정되지 않아 씬 파일은 건드리지 않은 상태입니다.

---

## 9. 자주 하는 수정 방법 모음 (Cookbook)

| 하고 싶은 것 | 어디를 건드리나 | 참고 |
|---|---|---|
| 새 퀘스트 추가 (인트로 보드 + 실제 도킹 플레이) | `Assets/Quests/*.asset`(카드/대사) + `StreamingAssets/quests/*.json`(구조/포켓/화합물) | 7장 6번 항목 |
| 새 후보물질(화합물) 추가/수정 | `StreamingAssets/compounds/*.json` (`CompoundData.cs` 스키마) | 7장 6번 ② |
| 새 단백질 구조 로드 | `python pdb_parser_script.py <UniProtID>` → `Assets/StreamingAssets/structures/*.json` 생성 | 5.2절 |
| 비서 대사·힌트·LLM 컨텍스트 수정 | 해당 `QuestDefinition` 에셋의 `stages[].assistantLines`/`hints`/`llmContext` | 7장 6번 ①, `QuestDefinition.cs` |
| 인트로 진행 타이밍/비서 위치 조정 | `IntroDirector` 컴포넌트의 Inspector 필드 (`startDelay`, `beatBeforeBoard`, `boardDistance`, `assistantViewportPosition` 등) | `Intro/IntroDirector.cs` |
| 후보물질 패널 위치/레이아웃 변경 | `CompoundSelectionPanel`의 `sideGap`/`topOffset` 등 간격 파라미터만 — **배치 방식(왼쪽+사선) 자체는 사용자가 확정한 값이므로 임의 변경 금지** | 7장 6번 1번 |
| 실험실 배경 모델 교체/재배치 | `Tools > Taming Mutants > 배경(실험실 모델) 배치` 재실행 후 Transform 수동 조정 | 8.1절 |
| 창문 밖 별하늘 룩(밀도/크기/반짝임) 조정 | `WindowBackdrop_Space.mat`의 `_StarDensity`/`_StarSize`/`_StarChance`/`_TwinkleSpeed` | 8.2절 |
| 창문에 천장 흰 경계선이 보이는 문제 대응 | `Ceiling_Patch`의 Scale/Position 조정 (아직 미적용) | 8.3절 |
| 리본/Helix/결합을 홀로그램 대신 실물처럼 보이게/되돌리기 | `StructureLevelController.solidSegments`, `ProteinLoader.solidBonds` 체크박스 | 7장 6번 1-2 |
| 뒤로가기 버튼 동작 확인 | `StructureLevelBackButton` — 리본 레벨에서는 비활성, 하위 레벨에서 Esc와 동일 동작 | 7장 6번 1-1 |