using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable] public class DockingAtom { public string element; public float x,y,z; public Vector3 Position => new Vector3(x,y,z); }
[Serializable] public class DockingBond { public int a,b; public float order; }
[Serializable] public class DockingLigand
{
    public string id,title,source,cid,smiles,sdf;
    public DockingAtom[] atoms;
    public DockingBond[] bonds;
}
[Serializable] public class DockingPose { public float score; public string contacts; public DockingAtom[] atoms; public DockingBond[] bonds; }
[Serializable] public class DockingSite
{
    public string label;
    public float[] center,size;
    public static DockingSite FromAtoms(string label,IEnumerable<ExplorationAtom> source)
    {
        var atoms=source.ToArray();
        if(atoms.Length==0) throw new InvalidOperationException("지정한 부위에 원자가 없습니다.");
        var bounds=new Bounds(atoms[0].position,Vector3.zero);
        foreach(var atom in atoms) bounds.Encapsulate(atom.position);
        var size=bounds.size+Vector3.one*12f;
        if(size.x>30 || size.y>30 || size.z>30) throw new InvalidOperationException("도킹 부위가 너무 넓어요. 더 좁은 잔기 범위를 선택해 주세요.");
        return new DockingSite { label=label,center=new[] { bounds.center.x,bounds.center.y,bounds.center.z },
            size=new[] { Mathf.Max(20,size.x),Mathf.Max(20,size.y),Mathf.Max(20,size.z) } };
    }
}
[Serializable] public class DockingReply
{
    public string jobId,status,error,engine,assumptions,receptorHash,comparisonKey;
    public int seed,exhaustiveness;
    public DockingLigand ligand;
    public DockingPose[] poses;
    public DockingCandidateChoice[] choices;
}
[Serializable] public class DockingCandidateChoice { public string title,query; }
[Serializable] public class DockingTrial
{
    public string jobId,pdbId,chains,createdAt,sessionId;
    public DockingLigand ligand;
    public DockingSite site;
    public DockingReply result;
}

/// <summary>Candidate replacement never resets the target/site or completed trials.</summary>
public sealed class ExplorationDockingSession
{
    public string Id { get; private set; }=Guid.NewGuid().ToString("N");
    public string PdbId { get; private set; }
    public string Chains { get; private set; }
    public DockingLigand Candidate { get; private set; }
    public DockingSite Site { get; set; }
    public readonly List<DockingTrial> Trials=new List<DockingTrial>();
    public bool Bind(string pdbId,string chains)
    {
        if(PdbId==pdbId && Chains==chains) return false;
        Reset(); PdbId=pdbId; Chains=chains; return true;
    }
    public void Reset()
    {
        Id=Guid.NewGuid().ToString("N"); PdbId=null; Chains=null; Candidate=null; Site=null; Trials.Clear();
    }
    public void SetCandidate(DockingLigand ligand)
    {
        if(ligand==null || string.IsNullOrWhiteSpace(ligand.sdf)) throw new InvalidOperationException("후보 좌표가 비어 있습니다.");
        ValidateGeometry(ligand.atoms,ligand.bonds); Candidate=ligand;
    }
    public DockingTrial Begin()
    {
        if(PdbId==null || Candidate==null || Site==null) throw new InvalidOperationException("단백질, 후보 분자, 도킹 부위를 먼저 선택해 주세요.");
        return new DockingTrial { jobId=Guid.NewGuid().ToString("N"),pdbId=PdbId,chains=Chains,sessionId=Id,
            createdAt=DateTime.UtcNow.ToString("o"),ligand=Candidate,site=Site };
    }
    public void Complete(DockingTrial trial,DockingReply result)
    {
        if(trial.pdbId!=PdbId || trial.chains!=Chains || trial.sessionId!=Id) throw new InvalidOperationException("다른 단백질 또는 이전 세션의 실험 결과입니다.");
        if(result.status=="completed")
        {
            if(result.poses==null || result.poses.Length==0 || result.poses.Length>5) throw new InvalidOperationException("도킹 자세가 없습니다.");
            foreach(var pose in result.poses)
            {
                if(float.IsNaN(pose.score)||float.IsInfinity(pose.score)) throw new InvalidOperationException("도킹 점수가 올바르지 않습니다.");
                ValidateGeometry(pose.atoms,pose.bonds);
            }
        }
        trial.result=result;
        if(Trials.Any(t=>t.jobId==trial.jobId)) return;
        Trials.Add(trial); if(Trials.Count>20) Trials.RemoveAt(0);
    }
    public static void ValidateGeometry(DockingAtom[] atoms,DockingBond[] bonds)
    {
        if(atoms==null || atoms.Length<3 || atoms.Length>120 || bonds==null || bonds.Length>240)
            throw new InvalidOperationException("지원 범위를 벗어난 후보 구조입니다.");
        foreach(var atom in atoms)
            if(atom==null || string.IsNullOrEmpty(atom.element) || new[] { atom.x,atom.y,atom.z }.Any(x=>float.IsNaN(x)||float.IsInfinity(x)||Mathf.Abs(x)>10000))
                throw new InvalidOperationException("후보 원자 좌표가 올바르지 않습니다.");
        foreach(var bond in bonds)
            if(bond==null || bond.a<0 || bond.b<0 || bond.a>=atoms.Length || bond.b>=atoms.Length || bond.a==bond.b)
                throw new InvalidOperationException("후보 결합 정보가 올바르지 않습니다.");
    }
}
