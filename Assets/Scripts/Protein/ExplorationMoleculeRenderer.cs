using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Bounded exploration renderer; never instantiates one GameObject per atom.</summary>
public class ExplorationMoleculeRenderer : MonoBehaviour
{
    public int ResidueLimit => Application.isMobilePlatform ? 240 : 400;
    public int AtomLimit => Application.isMobilePlatform ? 180 : 320;
    public int DisplayedResidues { get; private set; }
    public int DisplayedAtoms { get; private set; }
    public string DisplayScope { get; private set; }
    public bool HasGeometry => _meshes.Count > 0;
    readonly List<Mesh> _meshes = new List<Mesh>();
    readonly List<Material> _materials = new List<Material>();
    List<ExplorationAtom> _atoms;
    Vector3 _center;
    Vector3 _geometryOffset;
    public Vector3 PdbToLocal(Vector3 angstroms) => angstroms*.1f-_center-_geometryOffset;
    public Vector3? PreferredFocus;
    public bool HideBoundLigands;
    public Bounds DisplayBounds { get; private set; }
    static readonly Color[] Palette = { new Color(.2f,.85f,.95f), new Color(1,.58f,.28f), new Color(.6f,.45f,1) };

    public IEnumerator Build(List<ExplorationAtom> source, MoleculeViewSpec spec, string view)
    {
        Clear();
        var chains = string.IsNullOrWhiteSpace(spec.chains)
            ? source.Where(a => !a.hetero && a.name == "CA").Select(a => a.chain).Distinct().Take(1).ToArray()
            : spec.chains.Split(',');
        if (chains.Length == 0) throw new InvalidOperationException("현재는 단백질 주사슬이 있는 구조를 지원합니다.");
        if (chains.Any(c=>!source.Any(a=>!a.hetero && a.chain==c)))
            throw new InvalidOperationException("요청한 체인이 현재 좌표에 없습니다.");
        if (spec.residueStart<0 || spec.residueEnd<spec.residueStart || spec.residueEnd>9999)
            throw new InvalidOperationException("잔기 범위가 올바르지 않습니다.");
        var selected = source.Where(a => chains.Contains(a.chain)).ToList();
        var residues = selected.Where(a => !a.hetero && a.name == "CA" &&
            (spec.residueEnd==0 || (a.number>=spec.residueStart && a.number<=spec.residueEnd)))
            .Select(a => a.residueKey).Distinct().ToList();
        if (residues.Count == 0) throw new InvalidOperationException("요청한 잔기 범위에 표시할 좌표가 없습니다.");
        List<ExplorationAtom> focusLigand = null;
        if (!string.IsNullOrEmpty(spec.ligand) || view=="ligand")
        {
            var match=selected.FirstOrDefault(a=>a.hetero && (string.IsNullOrEmpty(spec.ligand)||a.residue==spec.ligand));
            if(match==null) throw new InvalidOperationException("선택한 체인에서 요청한 결합 분자를 찾지 못했어요.");
            focusLigand=selected.Where(a=>a.hetero && a.residueKey==match.residueKey).Take(AtomLimit).ToList();
            // Choose the visible region around the requested cofactor, not arbitrarily the chain's first residues.
            Vector3 focus=focusLigand.Aggregate(Vector3.zero,(v,a)=>v+a.position)/focusLigand.Count;
            var ranked=selected.Where(a=>!a.hetero && a.name=="CA" && residues.Contains(a.residueKey))
                .OrderBy(a=>(a.position-focus).sqrMagnitude).Take(ResidueLimit).Select(a=>a.residueKey);
            var nearby=new HashSet<string>(ranked);
            residues=residues.Where(nearby.Contains).ToList();
        }
        if(PreferredFocus.HasValue)
        {
            var nearby=new HashSet<string>(selected.Where(a=>!a.hetero && a.name=="CA" && residues.Contains(a.residueKey))
                .OrderBy(a=>(a.position-PreferredFocus.Value).sqrMagnitude).Take(ResidueLimit).Select(a=>a.residueKey));
            residues=residues.Where(nearby.Contains).ToList();
        }
        var allowed = new HashSet<string>(residues.Take(ResidueLimit));
        var protein = selected.Where(a => !a.hetero && allowed.Contains(a.residueKey)).ToList();
        _center = protein.Aggregate(Vector3.zero,(v,a) => v+a.position) / protein.Count * .1f;
        var ligand = focusLigand ?? selected.Where(a => a.hetero && protein.Any(p => (p.position-a.position).sqrMagnitude < 36f))
            .Take(80).ToList();
        if(HideBoundLigands && view!="ligand") ligand=new List<ExplorationAtom>();
        _atoms = protein.Concat(ligand).ToList();
        DisplayedResidues = allowed.Count;
        DisplayScope = "체인 " + string.Join(", ",chains) + $" · {allowed.Count} 잔기";
        if (spec.residueEnd>0) DisplayScope += $" · 요청 범위 {spec.residueStart}~{spec.residueEnd}번";
        int total=selected.Where(a=>!a.hetero && a.name=="CA").Select(a=>a.residueKey).Distinct().Count();
        if(total>allowed.Count) DisplayScope += $" · 체인 전체 {total}잔기 중 일부";
        if(residues.Count>allowed.Count) DisplayScope += " · 표시량 제한 적용";
        if(HideBoundLigands && view!="ligand") DisplayScope += " · 기존 결합 분자 제외";
        if(view=="ligand")
        {
            BuildAtoms(ligand);
            DisplayedResidues=0;
            DisplayScope="체인 "+string.Join(", ",chains)+" · "+ligand[0].residue+" 결합 분자 하나만 표시";
            FitSize(); yield break;
        }
        if(allowed.Count==1 && view=="ribbon")
        {
            view="atoms"; DisplayScope+=" · 잔기 하나는 원자로 표시";
        }
        DisplayedAtoms = 0;
        if (view == "atoms")
        {
            Vector3 focus = PreferredFocus??(ligand.Count > 0 ? ligand[0].position : protein[protein.Count/2].position);
            var detail = _atoms.OrderBy(a => (a.position-focus).sqrMagnitude).Take(AtomLimit).ToList();
            BuildAtoms(detail);
            DisplayScope += " · 선택 부근의 가까운 원자만 표시";
        }
        else
        {
            int color = 0, visibleResidues = 0;
            foreach (string chainId in chains)
            {
                var list = protein.Where(a=>a.chain==chainId).ToList();
                if (list.Count == 0) continue;
                // Adapt each chain independently; preserve gaps and insertion-code identities.
                var records = new List<ProteinLoader.AtomRecord>();
                int localId = 0, previousNumber = int.MinValue;
                Vector3 lastCa = Vector3.zero; bool hasPrevious = false;
                string previousSegment = null;
                foreach (var residue in list.GroupBy(a=>a.residueKey))
                {
                    var ca = residue.FirstOrDefault(a=>a.name=="CA");
                    if (ca == null) continue;
                    string segment = ca.residueKey.Split(':')[1];
                    bool gap = hasPrevious && (ca.number > previousNumber+1 || ca.number < previousNumber ||
                        segment != previousSegment || Vector3.Distance(lastCa,ca.position)>4.5f);
                    localId += gap ? 2 : 1;
                    foreach (var a in residue) records.Add(new ProteinLoader.AtomRecord { name=a.name,
                        res_name=a.residue, res_id=localId, x=a.position.x,y=a.position.y,z=a.position.z });
                    previousNumber=ca.number; previousSegment=segment; lastCa=ca.position; hasPrevious=true;
                }
                var backbone = BackboneChain.Extract(new ProteinLoader.ProteinData { atoms=records }, _center);
                var ss = SecondaryStructureAssigner.Assign(backbone);
                var style = RibbonMeshBuilder.Style.FromRadius(.085f);
                style.samplesPerResidue = Application.isMobilePlatform ? 2 : 4;
                style.sides = 8;
                var allPieces = RibbonMeshBuilder.Build(backbone,ss,style);
                var pieces = allPieces.Where(p=>string.IsNullOrEmpty(spec.secondaryStructure) || spec.secondaryStructure=="all" ||
                    (spec.secondaryStructure=="helix" && p.type==SecondaryStructureAssigner.Type.Helix) ||
                    (spec.secondaryStructure=="sheet" && p.type==SecondaryStructureAssigner.Type.Strand)).ToList();
                visibleResidues += pieces.Count;
                if (pieces.Count > 0)
                {
                    var mesh = new Mesh { indexFormat=IndexFormat.UInt32, name="Exploration ribbon" };
                    mesh.CombineMeshes(pieces.Select(p=>new CombineInstance { mesh=p.mesh,transform=Matrix4x4.identity }).ToArray(),true,false);

                    AddMesh(mesh,Palette[color++ % Palette.Length]);
                }
                foreach(var piece in allPieces) Destroy(piece.mesh);
                yield return null;
            }
            if (spec.id == "insulin") ligand.AddRange(protein.Where(a=>a.residue=="CYS" && a.name=="SG"));
            if(string.IsNullOrEmpty(spec.secondaryStructure)||spec.secondaryStructure=="all") BuildAtoms(ligand);
            else
            {
                DisplayedResidues=visibleResidues;
                DisplayScope += (spec.secondaryStructure=="helix" ? " · 나선 " : " · 시트 ") + visibleResidues + "잔기만 표시";
            }
        }
        if (!HasGeometry) throw new InvalidOperationException("선택 범위에 표시할 리본 또는 요청한 2차 구조가 없습니다.");
        FitSize();
    }
    void BuildAtoms(List<ExplorationAtom> atoms)
    {
        if (atoms.Count == 0) return;
        DisplayedAtoms = atoms.Count;
        var sphereObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var cylinderObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        sphereObject.SetActive(false); cylinderObject.SetActive(false);
        Mesh sphere = sphereObject.GetComponent<MeshFilter>().sharedMesh;
        Mesh cylinder = cylinderObject.GetComponent<MeshFilter>().sharedMesh;
        foreach (var group in atoms.GroupBy(a=>a.element))
        {
            var combines = group.Select(a=>new CombineInstance { mesh=sphere,
                transform=Matrix4x4.TRS(a.position*.1f-_center,Quaternion.identity,Vector3.one*(a.element=="FE"?.19f:.12f)) }).ToArray();
            var mesh=new Mesh { indexFormat=IndexFormat.UInt32 };
            mesh.CombineMeshes(combines,true,true);
            AddMesh(mesh,ElementColor(group.Key));
        }
        var bonds = new List<CombineInstance>();
        // Bounded to at most AtomLimit^2 comparisons, only when changing the view.
        for(int i=0;i<atoms.Count;i++) for(int j=i+1;j<atoms.Count;j++)
        {
            var a=atoms[i]; var b=atoms[j];
            if(a.element=="FE" || b.element=="FE") continue;
            float distance=Vector3.Distance(a.position,b.position);
            float threshold = a.element=="S" || b.element=="S" ? 2.2f : 1.9f;
            if(distance<.4f || distance>threshold) continue;
            if(a.chain!=b.chain && !(a.element=="S" && b.element=="S")) continue;
            Vector3 delta=(b.position-a.position)*.1f;
            bonds.Add(new CombineInstance { mesh=cylinder, transform=Matrix4x4.TRS(
                (a.position+b.position)*.05f-_center,Quaternion.FromToRotation(Vector3.up,delta),new Vector3(.035f,delta.magnitude*.5f,.035f)) });
        }
        if(bonds.Count>0)
        {
            var mesh=new Mesh { indexFormat=IndexFormat.UInt32 };
            mesh.CombineMeshes(bonds.ToArray(),true,true); AddMesh(mesh,new Color(.65f,.72f,.78f));
        }
        Destroy(sphereObject); Destroy(cylinderObject);
    }
    void AddMesh(Mesh mesh,Color color)
    {
        _meshes.Add(mesh);
        var go=new GameObject("Molecular geometry",typeof(MeshFilter),typeof(MeshRenderer));
        go.transform.SetParent(transform,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;
        var mat=new Material(RuntimeMaterials.Solid); mat.color=color;
        if(mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor",color);
        if(mat.HasProperty("_EmissionColor")) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor",color*.2f); }
        _materials.Add(mat); go.GetComponent<MeshRenderer>().sharedMaterial=mat;
    }
    void FitSize()
    {
        var bounds=new Bounds(); bool first=true;
        foreach(var mesh in _meshes) { if(first) { bounds=mesh.bounds; first=false; } else bounds.Encapsulate(mesh.bounds); }
        foreach(Transform child in transform) child.localPosition=-bounds.center;
        _geometryOffset=bounds.center;
        DisplayBounds=new Bounds(Vector3.zero,bounds.size);
        float size=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
        transform.localScale=Vector3.one*(1f/Mathf.Max(.1f,size));
    }
    static Color ElementColor(string e)
    {
        switch(e) { case "O":return new Color(1,.25f,.25f); case "N":return new Color(.3f,.45f,1);
            case "S":return Color.yellow; case "FE":return new Color(1,.5f,.15f); default:return new Color(.65f,.85f,.85f); }
    }
    public void Clear()
    {
        foreach(Transform child in transform) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        foreach(var mesh in _meshes) if(mesh!=null) Destroy(mesh);
        foreach(var mat in _materials) if(mat!=null) Destroy(mat);
        _meshes.Clear(); _materials.Clear(); _atoms=null;
        transform.localScale=Vector3.one;
    }
    void OnDestroy() { Clear(); }
}
