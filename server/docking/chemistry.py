"""Non-covalent, rigid-receptor docking. Coordinates remain in PDB Angstroms."""
import hashlib
import json
import math
import os
from pathlib import Path
import re
import subprocess
import sys


def receptor_selection(pdb, chains):
    if not isinstance(pdb, str) or len(pdb) > 24 * 1024 * 1024:
        raise ValueError('단백질 좌표 파일의 크기가 올바르지 않습니다.')
    if not isinstance(chains, str) or not re.fullmatch(r'[A-Za-z0-9](,[A-Za-z0-9]){0,7}', chains):
        raise ValueError('도킹할 단백질 체인을 선택해 주세요.')
    selected, seen, present = [], set(), set()
    for line in pdb.splitlines():
        if line.startswith('ENDMDL'):
            break
        if not line.startswith('ATOM  ') or len(line) < 54 or line[21] not in chains.split(','):
            continue
        if line[16] not in (' ', 'A'):
            continue
        key = line[21:27] + line[12:16]
        if key in seen:
            continue
        seen.add(key)
        xyz = [float(line[start:start+8]) for start in (30, 38, 46)]
        if not all(math.isfinite(x) for x in xyz):
            raise ValueError('유효하지 않은 원자 좌표입니다.')
        present.add(line[21])
        selected.append(line[:16] + ' ' + line[17:])
    if present != set(chains.split(',')) or not 10 <= len(selected) <= 20000:
        raise ValueError('체인이 없거나 도킹 범위(10~20,000 단백질 원자)를 넘었습니다.')
    # Bound ligands, solvent and ions are deliberately excluded, never treated as protein.
    return '\n'.join(selected) + '\nEND\n'


def validate_box(center, size):
    for value in (center, size):
        if not isinstance(value, list) or len(value) != 3 or any(type(x) not in (int, float) or not math.isfinite(x) for x in value):
            raise ValueError('도킹 부위 좌표가 올바르지 않습니다.')
    if any(abs(x) > 10000 for x in center) or any(x < 10 or x > 30 for x in size):
        raise ValueError('도킹 영역은 각 변이 10~30 Å인 범위로 지정해 주세요.')


def molecule_geometry(mol, conformer=0):
    from rdkit import Chem
    mol = Chem.RemoveHs(mol)
    conf = mol.GetConformer(conformer)
    return dict(atoms=[dict(element=a.GetSymbol(), x=float(conf.GetAtomPosition(a.GetIdx()).x),
                            y=float(conf.GetAtomPosition(a.GetIdx()).y), z=float(conf.GetAtomPosition(a.GetIdx()).z)) for a in mol.GetAtoms()],
                bonds=[dict(a=b.GetBeginAtomIdx(), b=b.GetEndAtomIdx(), order=b.GetBondTypeAsDouble()) for b in mol.GetBonds()])


class StereoSelectionRequired(ValueError):
    def __init__(self, mol):
        from rdkit import Chem
        from rdkit.Chem.EnumerateStereoisomers import EnumerateStereoisomers, StereoEnumerationOptions
        options = StereoEnumerationOptions(onlyUnassigned=True, unique=True, maxIsomers=9, rand=42)
        isomers = list(EnumerateStereoisomers(Chem.RemoveHs(mol), options=options))
        self.choices = [dict(title=f'입체형 {i+1} · ' + ', '.join(f'원자 {atom+1}: {label}' for atom, label in Chem.FindMolChiralCenters(m)), query='SMILES:' + Chem.MolToSmiles(m, isomericSmiles=True))
                        for i, m in enumerate(isomers[:8])]
        super().__init__('입체형이 여러 개예요. 아래 구조식 카드를 선택해 주세요. 기존에 지정된 입체형은 유지합니다.' +
                         (' 가능한 형태가 많아 최대 8개를 표시합니다. 정확한 CID나 SMILES도 입력할 수 있어요.' if len(isomers)>8 else ''))


def prepare_ligand(query='', sdf=''):
    from rdkit import Chem
    from rdkit.Chem import AllChem, Descriptors
    import httpx
    cid, source = '', '입력한 구조'
    if sdf:
        if len(sdf) > 200000 or sdf.count('$$$$') > 1:
            raise ValueError('SDF에는 후보 분자 하나만 넣어 주세요.')
        mol = Chem.MolFromMolBlock(sdf, removeHs=False)
        title = mol.GetProp('_Name').strip()[:100] if mol is not None and mol.HasProp('_Name') and mol.GetProp('_Name').strip() else 'SDF 후보'
    elif query.startswith('SMILES:'):
        mol = Chem.MolFromSmiles(query[7:].strip())
        title = 'SMILES 후보'
    elif query.startswith('CCD:'):
        code = query[4:].strip().upper()
        if not re.fullmatch(r'[A-Z0-9]{1,8}', code):
            raise ValueError('결합 분자의 식별자가 올바르지 않습니다.')
        with httpx.Client(timeout=20, follow_redirects=False) as client:
            response = client.get(f'https://files.rcsb.org/ligands/download/{code}_ideal.sdf')
            if response.status_code != 200 or len(response.content) > 200000:
                raise ValueError('선택한 결합 분자의 화학 구조를 불러오지 못했어요.')
            mol = Chem.MolFromMolBlock(response.text, removeHs=False)
        title, source = code + ' · 구조에 포함된 리간드', 'RCSB CCD ' + code
    else:
        from urllib.parse import quote
        query = query.strip()
        if not query or len(query) > 200:
            raise ValueError('분자 이름 또는 PubChem CID를 입력해 주세요.')
        numeric = re.fullmatch(r'(?:CID\s*:?\s*)?(\d+)', query, re.I)
        namespace, value = ('cid', numeric[1]) if numeric else ('name', query)
        base = 'https://pubchem.ncbi.nlm.nih.gov/rest/pug/compound/'
        with httpx.Client(timeout=20, follow_redirects=False) as client:
            response = client.get(base + namespace + '/' + quote(value, safe='') + '/cids/JSON')
            if response.status_code != 200:
                raise ValueError('PubChem에서 후보를 찾지 못했어요. 정확한 영문 이름이나 CID를 입력해 주세요.')
            ids = response.json().get('IdentifierList', {}).get('CID', [])
            if len(ids) != 1:
                raise ValueError('여러 구조가 검색됐어요. 원하는 입체형의 PubChem CID를 입력해 주세요.')
            cid = str(ids[0])
            response = client.get(base + 'cid/' + cid + '/SDF?record_type=2d')
            if response.status_code != 200 or len(response.content) > 200000:
                raise ValueError('후보 분자의 구조를 내려받지 못했어요.')
            mol = Chem.MolFromMolBlock(response.text, removeHs=False)
        title, source = query + ' · CID ' + cid, 'PubChem CID ' + cid
    if mol is None or len(Chem.GetMolFrags(mol)) != 1:
        raise ValueError('단일 소분자 구조가 필요합니다. 염/혼합물은 원하는 성분을 지정해 주세요.')
    if not 3 <= mol.GetNumHeavyAtoms() <= 120 or Descriptors.NumRotatableBonds(mol) > 20:
        raise ValueError('지원 범위는 중원자 3~120개, 회전 가능한 결합 20개 이하입니다.')
    if any(a.GetAtomicNum() not in (1, 6, 7, 8, 9, 15, 16, 17, 35, 53) for a in mol.GetAtoms()):
        raise ValueError('이 후보의 원소는 현재 도킹 준비에서 지원하지 않습니다.')
    if Chem.FindMolChiralCenters(mol, includeUnassigned=True) and any(label == '?' for _, label in Chem.FindMolChiralCenters(mol, includeUnassigned=True)):
        raise StereoSelectionRequired(mol)
    smiles = Chem.MolToSmiles(Chem.RemoveHs(mol))
    if query.startswith('SMILES:'):
        title = 'SMILES: ' + smiles[:60] + ('…' if len(smiles) > 60 else '')
    mol = Chem.AddHs(mol)
    # Generate the same starting conformer for repeated trials. Preserve user bond/charge identity.
    if not mol.GetNumConformers() or not mol.GetConformer().Is3D():
        params = AllChem.ETKDGv3(); params.randomSeed = 42
        if AllChem.EmbedMolecule(mol, params) != 0:
            raise ValueError('후보의 3D 구조를 만들지 못했어요.')
        if AllChem.MMFFHasAllMoleculeParams(mol):
            AllChem.MMFFOptimizeMolecule(mol, maxIters=500)
    block = Chem.MolToMolBlock(mol)
    return dict(id=hashlib.sha256(block.encode()).hexdigest(), title=title, source=source,
                cid=cid, smiles=smiles, sdf=block, **molecule_geometry(mol))


def run_checked(command, stage, timeout):
    try:
        return subprocess.run(command, check=True, timeout=timeout, capture_output=True,
                              text=True, encoding='utf-8', errors='replace')
    except subprocess.TimeoutExpired as error:
        raise ValueError(stage + ' 시간이 초과됐어요. 더 작은 구조로 다시 시도해 주세요.') from error
    except FileNotFoundError as error:
        raise ValueError(stage + ' 실행 파일이 없습니다. 로컬 도킹 서비스를 다시 시작해 주세요.') from error
    except subprocess.CalledProcessError as error:
        diagnostics = (error.stdout or '') + '\n' + (error.stderr or '')
        print(diagnostics[-20000:], file=sys.stderr, flush=True)
        if stage == '단백질 전처리':
            residues = list(dict.fromkeys(re.findall(r"No template matched for residue_key='([^']+)'", diagnostics)))
            if residues:
                shown = ', '.join(residues[:8]) + (f' 외 {len(residues)-8}개' if len(residues)>8 else '')
                raise ValueError('단백질의 원자 누락 또는 잔기 형식 불일치로 계산을 준비할 수 없어요: ' + shown +
                                 '. 원자가 완전한 같은 단백질의 다른 PDB 구조를 선택해 주세요.') from error
            raise ValueError('단백질 전처리에 실패했어요. 선택 체인의 잔기와 원자 구성을 확인해 주세요. 상세 원인은 계산 서버 로그에 기록했습니다.') from error
        raise ValueError(stage + '에 실패했어요. 상세 원인은 계산 서버 로그에 기록했습니다.') from error


def run_docking(body, directory):
    from rdkit import Chem
    from meeko import MoleculePreparation, PDBQTWriterLegacy, PDBQTMolecule, RDKitMolCreate
    directory = Path(directory)
    receptor = receptor_selection(body['pdb'], body['chains'])
    validate_box(body['center'], body['size'])
    receptor_file = directory / 'receptor.pdb'
    receptor_file.write_text(receptor, encoding='utf-8')
    # Strict Meeko templates add hydrogens/types. Missing or unsupported residues fail visibly.
    run_checked([sys.executable, '-m', 'meeko.cli.mk_prepare_receptor', '--read_pdb', str(receptor_file),
                 '-o', str(directory / 'receptor'), '-p'], '단백질 전처리', 120)
    mol = Chem.MolFromMolBlock(body['sdf'], removeHs=False)
    if mol is None or mol.GetNumHeavyAtoms() > 120 or len(Chem.GetMolFrags(mol)) != 1:
        raise ValueError('후보 구조가 올바르지 않습니다. 다시 불러와 주세요.')
    setups = MoleculePreparation().prepare(mol)
    if len(setups) != 1:
        raise ValueError('이 후보의 도킹 준비에는 추가 설정이 필요합니다.')
    ligand_pdbqt, ok, message = PDBQTWriterLegacy.write_string(setups[0])
    if not ok:
        raise ValueError('후보의 도킹 입력을 만들지 못했어요: ' + message[:160])
    (directory / 'ligand.pdbqt').write_text(ligand_pdbqt, encoding='utf-8')
    output = directory / 'poses.pdbqt'
    executable = os.environ.get('VINA_EXECUTABLE', 'vina')
    version = run_checked([executable, '--version'], 'Vina 실행 확인', 10).stdout
    if not re.search(r'\bv1\.2\.7\b', version):
        raise ValueError('계산 서버에 AutoDock Vina 1.2.7이 필요합니다.')
    command = [executable, '--receptor', str(directory / 'receptor.pdbqt'),
               '--ligand', str(directory / 'ligand.pdbqt'), '--out', str(output), '--cpu', '2',
               '--seed', '42', '--exhaustiveness', '8', '--num_modes', '5']
    for axis, center, size in zip('xyz', body['center'], body['size']):
        command += ['--center_' + axis, str(center), '--size_' + axis, str(size)]
    run_checked(command, 'Vina 도킹 계산', 240)
    pdbqt = output.read_text(encoding='utf-8')
    scores = [float(x) for x in re.findall(r'REMARK VINA RESULT:\s*([-\d.]+)', pdbqt)]
    poses_mol = RDKitMolCreate.from_pdbqt_mol(PDBQTMolecule(pdbqt, skip_typing=True))[0]
    if poses_mol is None or not scores or poses_mol.GetNumConformers() != len(scores):
        raise ValueError('도킹 결과 좌표를 복원하지 못했습니다.')
    from scipy.spatial import cKDTree
    receptor_atoms = [line for line in receptor.splitlines() if line.startswith('ATOM  ') and line[76:78].strip() not in ('H', 'D')]
    coordinates = [[float(line[start:start+8]) for start in (30, 38, 46)] for line in receptor_atoms]
    tree = cKDTree(coordinates)
    poses = []
    for i, score in enumerate(scores):
        geometry = molecule_geometry(poses_mol, i)
        nearby = tree.query_ball_point([[a['x'], a['y'], a['z']] for a in geometry['atoms']], 4.0)
        indices = sorted({index for neighbors in nearby for index in neighbors})
        contacts = sorted({receptor_atoms[index][21] + ':' + receptor_atoms[index][22:27].strip() + ' ' + receptor_atoms[index][17:20].strip() for index in indices})
        poses.append(dict(score=score, contacts=', '.join(contacts), **geometry))
    receptor_hash = hashlib.sha256(receptor.encode()).hexdigest()
    comparison_key = hashlib.sha256((receptor_hash + json.dumps([float(x) for x in body['center'] + body['size']]) + 'vina-1.2.7-meeko-0.7.1-seed42-e8').encode()).hexdigest()
    return dict(poses=poses, receptorHash=receptor_hash, comparisonKey=comparison_key,
                engine='AutoDock Vina 1.2.7', seed=42, exhaustiveness=8,
                assumptions='고정 단백질 · 비공유결합 · 물/이온/기존 리간드 제외 · 입력 리간드의 전하/입체형 유지. 도킹 점수는 실측 결합력이나 약효가 아닙니다.')
