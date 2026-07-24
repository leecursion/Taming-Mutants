# 돌연변이 길들이기 — Desktop PC 기준 개발 가이드 (Meta 장비 없음)

기능명세서(F-01~F-06)를 기준으로, **Meta 실기기 없이 Desktop PC만으로 녹화 시연까지 가능한 범위**를 정리한 가이드입니다. 단계마다 실행 가능한 환경(PC / 시뮬레이터 / 헤드셋)을 표기했습니다.

---

## 0. 현재 환경에서 가능한 것 / 불가능한 것

| 환경 | 시연(녹화) 가능 여부 | 비고 |
|---|---|---|
| **Desktop PC (Unity Editor Play Mode)** | ✅ 가능 | 데이터 로딩, 3D 시각화, 퀘스트 진행, AI 대화까지 전부 화면 녹화로 시연 가능 |
| Meta XR Simulator | ▲ 가능하지만 불필요 | Passthrough/손 모양까지 흉내내지만, 버전 불일치 크래시 위험이 있고 지금 목표(데이터 로드→배경→퀘스트)에는 이득이 없음 |
| 실제 Meta Quest 헤드셋 | ❌ 보유 안 함 | 확보 전까지 진행 불가 |

**결론: 이번 단계는 Simulator 없이 Desktop PC만으로 전체 MVP와 시연 녹화를 진행합니다.** 아래는 그 기준으로 다시 정리한 환경 세팅과 파일 구조입니다. Passthrough·공간 앵커 저장/로드·손 제스처·시선 추적처럼 **실제 하드웨어 신호가 있어야만 의미 있는 기능**만 헤드셋 확보 후로 미루고, 나머지(약 90%)는 지금 전부 완성합니다.

---

## 1. 환경 세팅 `[PC]`

### 1.1 필수 설치
- **Unity Hub** 설치 → Unity 계정 로그인 `[PC]`
- **Unity Editor: Unity 6 (6000.0.66f2 이상)** 설치 `[PC]`
- 화면 녹화 도구: **OBS Studio** 또는 Unity 패키지 매니저의 **Unity Recorder** `[PC]` — 시연 영상 제작용

### 1.2 프로젝트 생성 (XR 미사용)
1. Unity Hub에서 **3D (URP)** 템플릿으로 새 프로젝트 생성 `[PC]`
2. Build Settings는 **PC, Mac & Linux Standalone**으로 유지 (Android/XR로 전환하지 않음) `[PC]`
3. `Edit > Project Settings > XR Plug-in Management`는 **설치하지 않거나 비워둠** — 지금은 순수 3D 프로젝트로 개발 `[PC]`

### 1.3 나중을 위한 선택 사항 (지금 실행하지 않음)
- Meta XR All-in-One SDK, Android Build Support, Meta Horizon 개발자 계정, Building Blocks(Camera Rig/Passthrough/Hand Tracking/Spatial Anchor 등) → **전부 헤드셋 확보 후 진행** `[헤드셋 필요]`
- 지금 미리 설치해 둬도 프로젝트에 해가 되지는 않지만, 필수 작업은 아닙니다.

---

## 2. MVP ① 데이터 로드 — AlphaFold PDB 가져오기 `[PC]`

이 장 전체가 **Desktop Play Mode에서 100% 검증 가능**합니다. 헤드셋 여부와 무관합니다.

### 2.1 AlphaFold DB API 개요 `[PC]`
UniProt Accession(예: EGFR의 `P00533`)을 기준으로 예측 구조를 제공합니다.

**Step 1. 메타데이터 조회**
```
GET https://alphafold.ebi.ac.uk/api/prediction/{UniProt_ID}
```
응답 JSON 예시(일부):
```json
[
  {
    "entryId": "AF-P00533-F1",
    "uniprotAccession": "P00533",
    "latestVersion": 4,
    "pdbUrl": "https://alphafold.ebi.ac.uk/files/AF-P00533-F1-model_v4.pdb",
    "cifUrl": "https://alphafold.ebi.ac.uk/files/AF-P00533-F1-model_v4.cif",
    "confidenceType": "pLDDT"
  }
]
```

**Step 2. 구조 파일 다운로드**
- `pdbUrl` 필드를 그대로 사용하세요. 버전 번호(`v4`)를 직접 하드코딩하지 말고, 항상 메타데이터 응답의 URL을 사용해야 향후 DB 버전이 올라가도 코드 수정이 필요 없습니다.

### 2.2 Python 사전 파싱 파이프라인 `[PC]`
Quest뿐 아니라 PC에서도 원본 PDB(수천~수만 라인)를 매번 파싱하면 비효율적이므로, **오프라인 Python 전처리 → 경량 JSON → Unity 로딩** 구조를 그대로 유지합니다.

1. Python(+ Biopython)으로 PDB/CIF 파싱
2. 원자·잔기·pLDDT 값·변이 메타데이터를 4단계 레이어 JSON으로 저장
   - Layer 1: 백본(backbone) 좌표만
   - Layer 2: 곁사슬 포함 전체 원자
   - Layer 3: 결합 포켓 후보 잔기
   - Layer 4: 변이 위치 하이라이트 메타데이터
3. 결과 JSON을 `StreamingAssets`에 저장

```python
import json, requests
from Bio.PDB import PDBParser

def fetch_alphafold(uniprot_id: str, out_dir="structures"):
    meta = requests.get(f"https://alphafold.ebi.ac.uk/api/prediction/{uniprot_id}").json()[0]
    pdb_text = requests.get(meta["pdbUrl"]).text
    path = f"{out_dir}/{uniprot_id}.pdb"
    with open(path, "w") as f:
        f.write(pdb_text)
    return path, meta

def pdb_to_layered_json(pdb_path, out_json_path):
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
            "bfactor": round(atom.get_bfactor(), 2),  # AlphaFold: pLDDT 값이 저장되는 필드
            "res_name": res.get_resname(),
            "res_id": res.get_id()[1],
            "is_backbone": atom.get_name() in ("N", "CA", "C", "O"),
        })
    with open(out_json_path, "w") as f:
        json.dump({"atoms": atoms}, f)

path, meta = fetch_alphafold("P00533")
pdb_to_layered_json(path, "structures/P00533.json")
```

### 2.3 Unity에서 로딩 확인 `[PC]`
빈 3D 씬(카메라 + 조명만 있으면 충분, XR 컴포넌트 불필요)에 `ProteinLoader.cs`를 붙이고 위에서 만든 JSON을 `StreamingAssets`에 넣으면 원자·결합이 렌더링됩니다. 헤드셋·Simulator 어느 쪽도 필요하지 않습니다.

---

## 3. MVP ② 배경 — 가상 실험 테이블 + 홀로그램 UI `[PC 대체 연출]`

Passthrough(실제 방 보여주기)는 헤드셋 카메라가 있어야만 동작하므로, PC 단계에서는 **"MR처럼 보이는" 화면을 다른 방식으로 대체**합니다. 최종 목표(F-01.1)는 그대로 유지하되, 지금은 검증 가능한 부분만 진행합니다.

| 구성 요소 | PC에서 하는 방식 | 헤드셋에서 최종적으로 하는 방식 |
|---|---|---|
| 배경 | 어두운 단색/그라데이션 Skybox (또는 카메라 배경색을 짙은 남색 계열로 설정) | Passthrough로 실제 방 표시 |
| 테이블 위치 | 고정 좌표(`transform.position`)에 배치 | `OVRSpatialAnchor`로 실제 공간에 저장·재로드 |
| 테이블 모델 | 미니멀한 콘솔형 3D 모델 1개 (직접 ProBuilder 제작 또는 Sketchfab 무료 모델) | 동일 모델 그대로 사용 |
| 홀로그램 UI | `Hologram.shader`(반투명+Fresnel+스캔라인) 그대로 적용, 배경이 어두우므로 오히려 대비가 잘 보임 | 동일 셰이더 그대로 사용 |

### 3.1 테이블 모델 준비 `[PC]`
1. **직접 제작(권장)**: Unity ProBuilder(무료, Package Manager 내장)로 사각 테이블 상판 + 얇은 프레임만 제작
2. **에셋 대체**: Sketchfab에서 "sci-fi console", "holographic table" 검색 → **Downloadable + CC License** 필터로 무료 모델 확보 후 fbx/glTF로 임포트 (머티리얼은 URP Unlit/Transparent로 재매핑)

### 3.2 홀로그램 셰이더 적용 `[PC]`
`Hologram.shader`(URP Unlit, 반투명 + Fresnel 가장자리 발광 + 스캔라인)를 테이블/DNA/UI 패널에 공통 적용합니다. 어두운 배경 위에서 셰이더의 발광 효과가 실제 Passthrough 위에 뜬 홀로그램과 유사한 인상을 줍니다.

### 3.3 공간 앵커링(F-01.1) — 지금은 고정 좌표로 대체 `[PC]` / 실제 저장·로드는 `[헤드셋 필요]`
`ExperimentTableAnchor.cs`는 `OVRSpatialAnchor`가 있어야 동작하므로 지금은 씬에서 비활성화하고, 테이블 오브젝트에 고정 좌표만 지정합니다.
```csharp
// PC 테스트 씬에서는 이 정도로 충분합니다.
tableRoot.transform.position = new Vector3(0f, 0.9f, 1.2f);
```
헤드셋을 확보하면 이 오브젝트에 `OVRSpatialAnchor` + `ExperimentTableAnchor.cs`를 다시 활성화해 실제 방에 고정하는 흐름(1.5절 워크플로)으로 교체합니다.

---

## 4. MVP ③ 단백질 분해 및 신약개발 퀘스트 진행 `[PC]`

데이터 로드(2장)와 배경(3장)이 끝난 뒤 진행하는 본 게임 로직입니다. **입력 방식(마우스 vs 손 제스처)만 제외하면 전부 지금 완성 가능**하며, 헤드셋 확보 후에는 입력 계층만 교체합니다.

### 4.1 입력 계층 분리 설계 `[PC / 헤드셋 공용 설계]`
나중에 코드를 갈아엎지 않도록, "회전시켜라", "이 원자를 선택했다" 같은 의도를 인터페이스로 분리합니다.
```csharp
public interface IRotateZoomInput
{
    event Action<Vector2> OnRotateDelta;
    event Action<float> OnZoomDelta;
}

public interface IPointSelectInput
{
    event Action<Vector3> OnSelectWorldPoint;
}
```
- 지금: `DesktopFallbackController.cs`(마우스 드래그 회전 + 휠 확대) / `MouseWorldSelector.cs`(마우스 클릭 레이캐스트 선택)가 이 역할
- 나중: `HandGestureController.cs`(핀치 제스처), Gaze 기반 셀렉터가 동일 인터페이스로 교체
- `ProteinLoader`, `MutationHighlighter` 등 로직 스크립트는 입력 소스를 몰라도 되므로 **교체 시 로직 코드 수정이 필요 없습니다.**

### 4.2 단백질 구조 로딩·분해 `[PC]`
- `ProteinLoader.cs`: JSON을 읽어 원자(Sphere)+결합(Cylinder)으로 인스턴스화, 공유결합 거리(1.2~1.9Å) 이내만 결합 생성
- `PLDDTColorizer.cs`: pLDDT 값 기준 색상 매핑(90+: 파랑, 70~90: 하늘색, 50~70: 노랑, 50미만: 주황)
- `ComparisonController.cs`: Wild Type/Mutant 두 구조를 나란히 배치하고 한쪽 조작을 다른 쪽에 동기화

### 4.3 변이 부위 상호작용 `[PC]`
- `MutationHighlighter.cs`: 지정된 잔기(residue)에 펄스 발광 표시, 선택 시 `OnMutationSelected` 이벤트 발행
- `MouseWorldSelector.cs`: 마우스 클릭 지점에서 카메라 방향으로 레이캐스트해 원자를 선택 → `MutationHighlighter.SelectResidue()` 호출 (F-02.4, F-04.1을 대체)

### 4.4 회전/확대 조작 `[PC]`
- `DesktopFallbackController.cs`: 오른쪽 마우스 버튼 드래그로 회전, 마우스 휠로 확대/축소 (왼쪽 버튼은 `MouseWorldSelector`의 선택 전용으로 분리해 동시 입력 충돌을 방지)

### 4.5 퀘스트 진행 UI `[PC]`
- `QuestManagerSpatialUI.cs`: 5단계 퀘스트 진행률 관리. 지금은 **World Space Canvas 대신 Screen Space Canvas**로 임시 배치해도 로직은 동일하게 검증됩니다. (헤드셋 확보 후 World Space로 되돌리기만 하면 됨)

### 4.6 AI Co-Scientist 연동 `[PC]`
- `AICoScientistClient.cs`: GPT-4o API를 자체 백엔드 프록시 경유로 호출 (API 키를 클라이언트에 직접 넣지 않음). 버튼 클릭 → 응답 수신까지 XR과 완전히 무관하므로 PC에서 전부 확인 가능
- 퀘스트 가이드(F-06.2), 변이 브리핑(F-02.4), 퀴즈(F-06.3) 모두 동일 클라이언트로 처리

### 4.7 씬 전환 연출 `[PC]`
- `SceneTransitionManager.cs`: DOTween(Ease.InOutSine)으로 분자 뷰 ↔ 세포 뷰 카메라 전환 + 3D 공간 오디오(`AudioSource.spatialBlend = 1`) 재생. PC 스피커/헤드폰으로도 동일하게 확인 가능

---

## 5. 파일 구조 요약

| 파일 | 명세서 대응 | 지금(PC) 상태 | 헤드셋 확보 후 |
|---|---|---|---|
| `ProteinLoader.cs` | F-03.1 | 사용 `[PC]` | 그대로 사용 |
| `PLDDTColorizer.cs` | F-03.3 | 사용 `[PC]` | 그대로 사용 |
| `ComparisonController.cs` | F-03.4 | 사용 `[PC]` | 그대로 사용 |
| `MutationHighlighter.cs` | F-02.3, F-02.4 | 사용 `[PC]` | 그대로 사용 |
| `QuestManagerSpatialUI.cs` | F-01.3 | 사용, Screen Space Canvas로 배치 `[PC]` | World Space Canvas로 전환 |
| `AICoScientistClient.cs` | F-06.1~F-06.3 | 사용 `[PC]` | 그대로 사용 |
| `SceneTransitionManager.cs` | F-05.1 | 사용 `[PC]` | 그대로 사용 |
| `Hologram.shader` | F-01.2, F-01.3, F-02.1 등 | 사용 `[PC]` | 그대로 사용 |
| `DesktopFallbackController.cs` | F-02.2 대체 | 사용 `[PC]` | 비활성화, `HandGestureController`로 교체 |
| `MouseWorldSelector.cs` | F-02.4, F-04.1 대체 | 사용 `[PC]` | 비활성화, 시선/그랩 기반 셀렉터로 교체 |
| `HandGestureController.cs` | F-02.2 | 비활성화 `[헤드셋 필요]` | 활성화 |
| `ExperimentTableAnchor.cs` | F-01.1 | 비활성화, 고정 좌표로 대체 `[헤드셋 필요]` | 활성화 (`OVRSpatialAnchor` 필요) |

---

## 6. 헤드셋 확보 후에만 진행 가능한 항목 (참고)

아래는 실제 Meta Quest 하드웨어 신호(카메라, 트래킹 센서)가 있어야만 의미가 있어 지금은 검증 자체가 불가능한 항목입니다. 헤드셋을 확보하면 이어서 진행합니다.

- Passthrough 실제 배경 표시, Room Mesh(Scene/MRUK) 인식
- `OVRSpatialAnchor` 저장/재로드 실제 동작 확인
- 손 제스처(Hand Tracking), 시선 추적(Eye Tracking) 실제 인식
- Quest 기기에서의 90FPS 성능 프로파일링
- (선택) Meta XR Simulator를 이용한 사전 검증 — 실기기 없이 MR 입력을 흉내내고 싶을 때만 필요하며, SDK와 Simulator의 버전을 반드시 동일하게 맞춰야 합니다.
