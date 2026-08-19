"""
AlphaFold PDB -> 레이어드 JSON 전처리 스크립트 (수정판)

수정 사항:
1. [버그 수정] atom.coord(numpy.float32)를 round() 하면 numpy.float64가 되어
   표준 json 모듈이 직렬화하지 못하고 TypeError로 죽는 문제 -> float()로 먼저 변환.
2. [안전장치] 임시 파일에 먼저 쓰고 완료 후에만 최종 파일명으로 교체(os.replace) ->
   중간에 예외가 나도 이전 결과물이나 빈 파일이 최종 경로에 남지 않음.
3. [검증] 저장 직후 json.load()로 다시 읽어봐서 실제로 유효한 JSON인지 확인.
4. [로그] 각 단계 진행 상황과 원자 개수를 출력해서 어디서 멈췄는지 바로 알 수 있게 함.

필요 패키지: pip install biopython requests --break-system-packages
"""
import json
import os
import tempfile
import traceback
import requests
from Bio.PDB import PDBParser


def fetch_alphafold(uniprot_id: str, out_dir: str = "structures") -> tuple[str, dict]:
    os.makedirs(out_dir, exist_ok=True)

    print(f"[1/4] AlphaFold 메타데이터 조회: {uniprot_id}")
    meta_resp = requests.get(
        f"https://alphafold.ebi.ac.uk/api/prediction/{uniprot_id}", timeout=30
    )
    meta_resp.raise_for_status()
    meta_list = meta_resp.json()
    if not meta_list:
        raise ValueError(f"'{uniprot_id}'에 대한 AlphaFold 예측 결과가 없습니다.")
    meta = meta_list[0]

    print(f"[2/4] PDB 구조 파일 다운로드: {meta['pdbUrl']}")
    pdb_resp = requests.get(meta["pdbUrl"], timeout=60)
    pdb_resp.raise_for_status()

    path = os.path.join(out_dir, f"{uniprot_id}.pdb")
    with open(path, "w") as f:
        f.write(pdb_resp.text)
    print(f"      -> 저장됨: {path} ({len(pdb_resp.text)} bytes)")

    return path, meta


def pdb_to_layered_json(pdb_path: str, out_json_path: str,
                         residue_range: tuple[int, int] | None = None,
                         mutation_sites: tuple[int, ...] = ()) -> None:
    print(f"[3/4] PDB 파싱 및 JSON 변환: {pdb_path}")
    parser = PDBParser(QUIET=True)
    structure = parser.get_structure("protein", pdb_path)

    raw_atoms = []
    for atom in structure.get_atoms():
        res = atom.get_parent()
        res_id = res.get_id()[1]
        if residue_range and not (residue_range[0] <= res_id <= residue_range[1]):
            continue  # 관심 영역(예: 키나아제 도메인)만 필터링
        raw_atoms.append((atom, res, res_id))

    # centroid 계산 후 원점 기준으로 재배치
    cx = sum(float(a.coord[0]) for a, _, _ in raw_atoms) / len(raw_atoms)
    cy = sum(float(a.coord[1]) for a, _, _ in raw_atoms) / len(raw_atoms)
    cz = sum(float(a.coord[2]) for a, _, _ in raw_atoms) / len(raw_atoms)

    atoms = []
    for atom, res, res_id in raw_atoms:
        atoms.append({
            "name": atom.get_name(),
            "element": atom.element,
            "x": round(float(atom.coord[0]) - cx, 3),
            "y": round(float(atom.coord[1]) - cy, 3),
            "z": round(float(atom.coord[2]) - cz, 3),
            "bfactor": round(float(atom.get_bfactor()), 2),
            "res_name": res.get_resname(),
            "res_id": res_id,
            "is_backbone": atom.get_name() in ("N", "CA", "C", "O"),
            "is_mutation_site": res_id in mutation_sites,
        })
    print(f"      -> 원자 {len(atoms)}개 파싱 완료")

    # 임시 파일에 먼저 쓰고, 성공하면 최종 경로로 원자적 교체
    out_dir = os.path.dirname(out_json_path) or "."
    os.makedirs(out_dir, exist_ok=True)
    fd, tmp_path = tempfile.mkstemp(dir=out_dir, suffix=".json.tmp")
    try:
        with os.fdopen(fd, "w") as f:
            json.dump({"atoms": atoms}, f)
        os.replace(tmp_path, out_json_path)  # 완료된 경우에만 원래 이름으로 교체
    except Exception:
        if os.path.exists(tmp_path):
            os.remove(tmp_path)  # 실패 시 임시 파일 잔여물 제거
        raise

    print(f"[4/4] 검증: {out_json_path} 다시 읽어서 확인 중...")
    with open(out_json_path, "r") as f:
        loaded = json.load(f)
    print(f"      -> 정상 JSON 확인, atoms 개수: {len(loaded['atoms'])}")



# 단백질별 전처리 설정: 관심 잔기 범위 / 변이 부위 하이라이트
CONFIGS = {
    "P00533": {"range": (712, 979), "mutations": (858, 790)},  # EGFR: L858R, T790M (키나아제 도메인)
    "P01116": {"range": None, "mutations": (12,)},             # KRAS: G12C (F-04 도킹 퀘스트 타깃)
}

if __name__ == "__main__":
    import sys
    UNIPROT_ID = sys.argv[1] if len(sys.argv) > 1 else "P00533"
    cfg = CONFIGS.get(UNIPROT_ID, {"range": None, "mutations": ()})
    try:
        pdb_path, meta = fetch_alphafold(UNIPROT_ID)
        pdb_to_layered_json(
            pdb_path,
            f"Assets/StreamingAssets/structures/{UNIPROT_ID}.json",
            residue_range=cfg["range"],
            mutation_sites=tuple(cfg["mutations"]),
        )
        print("완료.")
    except Exception:
        print("전처리 중 오류 발생:")
        traceback.print_exc()