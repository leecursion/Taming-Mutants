# 돌연변이 길들이기 — Desktop PC 우선 개발 가이드 (Meta 장비 없음)

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

| 단계 | 내용 | 실행 환경 |
|---|---|---|
| 1 | Unity 프로젝트 생성 (Standalone/PC 빌드 타겟, URP) | **PC** |
| 2 | Desktop 대체 컴포넌트 작성 (마우스 회전/줌, 원자 선택, 테이블 고정 배치) | **PC** |
| 3 | 가상 테이블 배치 (고정 좌표, 앵커링 없이) | **PC** |
| 4 | AlphaFold 데이터 로드 → 단백질 시각화 *(MVP 1차 완료 지점)* | **PC** |
| 5 | 마우스로 회전/줌/원자 선택 상호작용 | **PC** |
| 6 | 홀로그램 UI(퀘스트 진행 패널) 연결 *(MVP 2차 완료 지점: 데이터+배경 종료)* | **PC** |
| 7 | 단백질 레이어 분해(F-03.2) + 변이 하이라이트(F-02.3) + Quest 1~5 진행 로직 | **PC** |
| 8 | AI Co-Scientist(GPT-4o, 백엔드 프록시) 연동 | **PC** |
| 9 | (이후 단계) Meta XR SDK 설치, 인터페이스를 XR 구현체로 교체 | **Simulator 또는 실기기** — 지금은 스킵 |
| 10 | (이후 단계) Passthrough/Spatial Anchor/손 제스처 실기기 검증 | **실기기 필수** — 지금은 스킵 |

> 7단계까지 끝나면(퀘스트 로직 포함) **Desktop PC만으로 전체 신약개발 퀘스트 흐름을 처음부터 끝까지 녹화 시연**할 수 있습니다. 8~10단계는 헤드셋이 확보된 이후 진행하면 됩니다.

---

## 3. Desktop PC 환경 세팅 (지금 필요한 것만)

1. **Unity Hub → Unity 6 (URP 템플릿)** 설치
2. `File > Build Settings` → 플랫폼을 **PC, Mac & Linux Standalone**으로 유지 (Android로 전환하지 않음 — 그건 8단계 이후)
3. XR Plug-in Management, Meta XR SDK, OpenXR — **지금은 설치하지 않습니다.** (나중에 9단계에서 추가)
4. 필요한 패키지: `DOTween`(Asset Store, 무료), `Newtonsoft Json.NET`(Package Manager), 표준 Unity `Input System` 또는 레거시 Input Manager
5. 화면 녹화: OBS Studio 등으로 Game View를 녹화하면 바로 시연 영상 확보

이것으로 끝입니다. Meta 계정, Quest 개발자 모드, Simulator 설치는 전부 9단계 이후로 미룹니다.

---

## 4. Hierarchy / Assets 파일 구조

### 4.1 Assets 폴더 구조
```
Assets/
├── Scripts/
│   ├── Desktop/                     # 지금 사용하는 구현체 (PC 전용)
│   │   ├── DesktopFallbackController.cs   # 마우스 드래그 회전 + 휠 줌 (target 대상)
│   │   ├── MouseWorldSelector.cs          # 마우스 클릭 레이캐스트로 원자 선택
│   │   └── DesktopTablePlacement.cs       # 테이블 고정 좌표 배치
│   ├── XR/                          # 나중(9단계 이후) 사용할 구현체 - 지금은 씬에서 비활성
│   │   ├── HandGestureController.cs       # DesktopFallbackController와 동일한 target 필드
│   │   └── ExperimentTableAnchor.cs       # DesktopTablePlacement와 동일한 OnPlacementConfirmed 이벤트
│   ├── Protein/
│   │   ├── ProteinLoader.cs
│   │   ├── PLDDTColorizer.cs
│   │   ├── AtomInfo.cs
│   │   ├── ComparisonController.cs
│   │   └── MutationHighlighter.cs
│   ├── UI/
│   │   └── QuestManagerSpatialUI.cs
│   ├── AI/
│   │   └── AICoScientistClient.cs
│   └── SceneManagement/
│       └── SceneTransitionManager.cs
├── Shaders/
│   └── Hologram.shader
├── Prefabs/
│   ├── ExperimentTable.prefab
│   ├── Atom.prefab                  # Sphere + PLDDTColorizer + AtomInfo
│   ├── Bond.prefab                  # Cylinder
│   └── QuestPanel.prefab            # World Space Canvas + Hologram 머티리얼
├── Materials/
│   └── Hologram_Blue.mat
├── StreamingAssets/
│   └── structures/
│       └── P00533.json              # 5장 2절 파이썬 전처리 결과물
└── Scenes/
    └── Lab_Desktop.unity            # 지금 작업하는 메인 씬 (PC 전용)
    └── Lab_XR.unity                 # 9단계 이후 XR 씬 (지금은 생성만, 비워둠)
```

### 4.2 씬 내 Hierarchy 구조 (Lab_Desktop.unity)
```
Lab_Desktop (Scene)
├── Main Camera                       # 기본 Unity 카메라, 테이블을 바라보는 고정 위치 (헤드 트래킹 불필요)
├── EventSystem                        # World Space Canvas 클릭 처리에 필요
├── ExperimentTableRoot                # DesktopTablePlacement (나중 ExperimentTableAnchor로 교체)
│   ├── TableModel                     # 2.2절 테이블 메쉬 + Hologram 머티리얼
│   ├── ProteinAnchor_WildType          # ProteinLoader, ComparisonController(master)
│   ├── ProteinAnchor_Mutant             # ProteinLoader, ComparisonController(slave)
│   └── MutationHighlighter (컴포넌트)
├── QuestPanel                          # World Space Canvas + QuestManagerSpatialUI
├── AICoScientistManager                # 빈 오브젝트 + AICoScientistClient
├── InteractionManager                  # 빈 오브젝트 + MouseWorldSelector + DesktopFallbackController(target = ProteinAnchor_WildType 등 선택된 대상)
└── Lighting                            # Directional Light 1개 (실 조명 대체용, 최소 설정)
```

> `InteractionManager`에 `MouseWorldSelector`(원자 선택)와 `DesktopFallbackController`(회전/줌 대상 조작)를 함께 붙입니다. 나중에 XR로 전환할 때는 이 두 컴포넌트를 비활성화하고 `HandGestureController`(동일한 `target` 필드)와 Gaze 기반 셀렉터를 활성화하면 됩니다.

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

### 5.2 Python 사전 파싱 (권장)
```python
import json, os, requests
from Bio.PDB import PDBParser

def fetch_alphafold(uniprot_id: str, out_dir="structures"):
    os.makedirs(out_dir, exist_ok=True)
    meta = requests.get(f"https://alphafold.ebi.ac.uk/api/prediction/{uniprot_id}").json()[0]
    pdb_text = requests.get(meta["pdbUrl"]).text
    path = f"{out_dir}/{uniprot_id}.pdb"
    with open(path, "w") as f:
        f.write(pdb_text)
    return path, meta

def pdb_to_layered_json(pdb_path, out_json_path):
    os.makedirs(os.path.dirname(out_json_path), exist_ok=True)
    parser = PDBParser(QUIET=True)
    structure = parser.get_structure("protein", pdb_path)
    atoms = []
    for atom in structure.get_atoms():
        res = atom.get_parent()
        atoms.append({
            "name": atom.get_name(),
            "element": atom.element,
            "x": round(atom.coord[0], 3),
            "y": round(atom.coord[1], 3),
            "z": round(atom.coord[2], 3),
            "bfactor": round(atom.get_bfactor(), 2),  # AlphaFold: pLDDT 값
            "res_name": res.get_resname(),
            "res_id": res.get_id()[1],
            "is_backbone": atom.get_name() in ("N", "CA", "C", "O"),
        })
    with open(out_json_path, "w") as f:
        json.dump({"atoms": atoms}, f)

path, meta = fetch_alphafold("P00533")
pdb_to_layered_json(path, "Assets/StreamingAssets/structures/P00533.json")
```
`pdb_to_layered_json`이 `Assets/StreamingAssets/structures/`에 바로 JSON을 생성하므로, 별도의 수동 복사 단계 없이 결과물이 Unity가 읽는 위치에 놓입니다.

---

## 6. 스크립트 목록 (첨부 파일)

| 파일 | 명세서 대응 | 환경 |
|---|---|---|
| `DesktopFallbackController.cs` | F-02.2 대체(마우스 드래그 회전 + 휠 줌) | **PC** |
| `MouseWorldSelector.cs` | F-04.1 대체(마우스 클릭으로 원자/변이 선택) | **PC** |
| `DesktopTablePlacement.cs` | F-01.1 대체(고정 좌표 배치) | **PC** |
| `ProteinLoader.cs` | F-03.1 | PC / XR 공통 |
| `PLDDTColorizer.cs` | F-03.3 | PC / XR 공통 |
| `AtomInfo.cs` | 보조 | PC / XR 공통 |
| `ComparisonController.cs` | F-03.4 | PC / XR 공통 |
| `MutationHighlighter.cs` | F-02.3, F-02.4 | PC / XR 공통 |
| `QuestManagerSpatialUI.cs` | F-01.3 | PC / XR 공통 |
| `AICoScientistClient.cs` | F-06 | PC / XR 공통 |
| `SceneTransitionManager.cs` | F-05.1 | PC / XR 공통 |
| `Hologram.shader` | F-01.2, F-01.3, F-02.1 등 | PC / XR 공통 |
| `HandGestureController.cs` | F-02.2 (실기기용, 지금은 미사용) | Simulator/실기기 |
| `ExperimentTableAnchor.cs` | F-01.1 (실기기용, 지금은 미사용) | Simulator/실기기 |

> 표에서 "PC / XR 공통"인 스크립트들은 입력 방식과 무관하게 동작하므로 지금 만들어두면 나중에 그대로 재사용됩니다. "실기기용" 두 개는 9단계 이후에만 씬에 추가하면 됩니다.