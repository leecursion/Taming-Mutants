import math
import unittest
from unittest.mock import patch
import uuid
from fastapi.testclient import TestClient
import app as service
from chemistry import prepare_ligand, receptor_selection, validate_box, run_checked


def pdb_fixture():
    return '\n'.join(f'ATOM  {i:5d}  CA  ALA A{i:4d}    {i:8.3f}{0:8.3f}{0:8.3f}  1.00 20.00           C  ' for i in range(1, 12))


class ChemistryTests(unittest.TestCase):
    def test_undefined_stereo_returns_selectable_structures_without_changing_specified_centers(self):
        from chemistry import StereoSelectionRequired
        from rdkit import Chem
        with self.assertRaises(StereoSelectionRequired) as caught:
            prepare_ligand('SMILES:C[C@H](F)C(O)CC')
        choices=caught.exception.choices
        self.assertEqual(len(choices),2)
        reference=Chem.MolFromSmiles('C[C@H](F)C(O)CC')
        for choice in choices:
            ligand=prepare_ligand(choice['query'])
            mol=Chem.MolFromSmiles(ligand['smiles'])
            self.assertTrue(mol.HasSubstructMatch(reference,useChirality=True))
            self.assertTrue(all(label!='?' for _,label in Chem.FindMolChiralCenters(mol,includeUnassigned=True)))
        reply=service.dispatch(dict(op='ligand',sessionId=uuid.uuid4().hex,query='SMILES:CC(O)C(=O)O'))
        self.assertEqual(reply['status'],'needs_selection')
        self.assertEqual(len(reply['choices']),2)
    def test_preparation_error_identifies_residues_instead_of_claiming_connection_failure(self):
        import subprocess
        failure = subprocess.CalledProcessError(1, ['meeko'], output="No template matched for residue_key='D:17'\nheavy_miss=6")
        with patch('chemistry.subprocess.run', side_effect=failure), patch('sys.stderr'):
            with self.assertRaisesRegex(ValueError, 'D:17') as caught:
                run_checked(['meeko'], '단백질 전처리', 120)
            self.assertNotIn('서버 설정', str(caught.exception))
        with patch('chemistry.subprocess.run', side_effect=FileNotFoundError()):
            with self.assertRaisesRegex(ValueError, '실행 파일'):
                run_checked(['vina'], 'Vina 실행 확인', 10)
    def test_original_coordinates_chain_and_first_model(self):
        pdb = pdb_fixture()
        selected = receptor_selection(pdb + '\nENDMDL\n' + pdb.replace('ALA A', 'ALA B'), 'A')
        self.assertEqual(selected.count('ATOM'), 11)
        self.assertIn('  11.000', selected)
        with self.assertRaises(ValueError): receptor_selection(pdb, 'B')
        with self.assertRaises(ValueError): receptor_selection(pdb, '../A')
        with self.assertRaises(ValueError): receptor_selection(pdb.replace('ATOM  ', 'HETATM'), 'A')

    def test_box_rejects_nonfinite_and_unbounded(self):
        validate_box([1, 2, 3], [20, 20, 20])
        for center, size in [([math.nan, 0, 0], [20]*3), ([0]*3, [100]*3), ([0]*3, [True]*3), ([0, 0], [20]*3)]:
            with self.assertRaises(ValueError): validate_box(center, size)

    def test_smiles_sdf_round_trip_preserves_identity_bonds_and_charge(self):
        ligand = prepare_ligand('SMILES:CC(=O)[O-]')
        self.assertIn('[O-]', ligand['smiles'])
        self.assertEqual(len(ligand['atoms']), 4)
        self.assertTrue(any(b['order'] == 2 for b in ligand['bonds']))
        restored = prepare_ligand(sdf=ligand['sdf'])
        self.assertEqual(ligand['smiles'], restored['smiles'])
        for before, after in zip(ligand['atoms'], restored['atoms']):
            self.assertEqual(before['element'], after['element'])
            for axis in 'xyz': self.assertAlmostEqual(before[axis], after[axis], delta=0.000051)  # SDF V2000 precision

    def test_reject_mixtures_unknown_stereo_and_metal(self):
        for query in ['SMILES:CCO.CCN', 'SMILES:CC(O)C(=O)O', 'SMILES:[Zn]CC', 'SMILES:bad']:
            with self.assertRaises(ValueError): prepare_ligand(query)

    def test_bound_ligand_card_fetches_ccd_structure_and_validates_identity(self):
        from rdkit import Chem
        from rdkit.Chem import AllChem
        import httpx
        mol = Chem.AddHs(Chem.MolFromSmiles('c1ccccc1'))
        AllChem.EmbedMolecule(mol, randomSeed=42)
        with patch('httpx.Client') as client:
            client.return_value.__enter__.return_value.get.return_value = httpx.Response(200, text=Chem.MolToMolBlock(mol))
            ligand = prepare_ligand('CCD:BNZ')
            self.assertEqual(ligand['source'], 'RCSB CCD BNZ')
            self.assertEqual(ligand['smiles'], 'c1ccccc1')
            client.return_value.__enter__.return_value.get.assert_called_once_with('https://files.rcsb.org/ligands/download/BNZ_ideal.sdf')
        with self.assertRaises(ValueError): prepare_ligand('CCD:../BNZ')


class ServiceTests(unittest.TestCase):
    def setUp(self):
        service.jobs.clear()
        self.session = uuid.uuid4().hex
        self.body = dict(op='start', sessionId=self.session, jobId=uuid.uuid4().hex,
                         pdb=pdb_fixture(), chains='A', sdf='x'*30, center=[1, 2, 3], size=[20]*3)

    def test_start_retry_cancel_and_second_candidate(self):
        with patch.object(service.executor, 'submit') as submit:
            first = service.dispatch(self.body)
            self.assertEqual(service.dispatch(self.body), first)
            self.assertEqual(submit.call_count, 1)
            with self.assertRaises(ValueError): service.dispatch(dict(self.body, jobId=uuid.uuid4().hex))
            cancelled = service.dispatch(dict(op='cancel', sessionId=self.session, jobId=first['jobId']))
            self.assertEqual(cancelled['status'], 'cancelled')
            second = service.dispatch(dict(self.body, jobId=uuid.uuid4().hex))
            self.assertNotEqual(first['jobId'], second['jobId'])
            self.assertEqual(service.dispatch(dict(op='poll', sessionId=self.session, jobId=first['jobId']))['status'], 'cancelled')

    def test_job_ownership_and_expiry(self):
        with patch.object(service.executor, 'submit'):
            service.dispatch(self.body)
            with self.assertRaises(ValueError): service.dispatch(dict(op='poll', sessionId=uuid.uuid4().hex, jobId=self.body['jobId']))
            with self.assertRaises(ValueError): service.dispatch(dict(op='poll', sessionId=self.session, jobId=uuid.uuid4().hex))

    def test_cancel_before_start_upload_prevents_late_job(self):
        with patch.object(service.executor, 'submit') as submit:
            cancelled = service.dispatch(dict(op='cancel', sessionId=self.session, jobId=self.body['jobId']))
            self.assertEqual(cancelled['status'], 'cancelled')
            self.assertEqual(service.dispatch(self.body)['status'], 'cancelled')
            submit.assert_not_called()

    def test_auth_and_bad_json_never_dispatch(self):
        with patch.dict('os.environ', {'DOCKING_SERVICE_TOKEN': 'test-secret'}), patch.object(service, 'dispatch') as dispatch:
            client = TestClient(service.app)
            self.assertEqual(client.post('/api/docking', json=self.body).status_code, 401)
            self.assertEqual(client.post('/api/docking', content='broken', headers={'X-App-Token':'test-secret'}).status_code, 400)
            dispatch.assert_not_called()


if __name__ == '__main__': unittest.main()
