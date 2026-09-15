using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Uses explicit chemical bonds and the protein's original-coordinate transform.</summary>
public class ExplorationLigandRenderer : MonoBehaviour
{
    readonly List<Mesh> _meshes=new List<Mesh>();
    readonly List<Material> _materials=new List<Material>();
    public void Build(DockingAtom[] atoms,DockingBond[] bonds,Func<Vector3,Vector3> convert,Color carbon)
    {
        ExplorationDockingSession.ValidateGeometry(atoms,bonds);
        var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere); sphere.SetActive(false);
        var cylinder=GameObject.CreatePrimitive(PrimitiveType.Cylinder); cylinder.SetActive(false);
        try
        {
            foreach(var group in atoms.GroupBy(a=>a.element))
            {
                Color color=group.Key=="O"?new Color(1,.25f,.25f):group.Key=="N"?new Color(.3f,.5f,1):group.Key=="S"?Color.yellow:carbon;
                Add(group.Select(a=>new CombineInstance { mesh=sphere.GetComponent<MeshFilter>().sharedMesh,
                    transform=Matrix4x4.TRS(convert(a.Position),Quaternion.identity,Vector3.one*.14f) }).ToArray(),color);
            }
            var lines=new List<CombineInstance>();
            foreach(var bond in bonds)
            {
                Vector3 a=convert(atoms[bond.a].Position),b=convert(atoms[bond.b].Position),delta=b-a;
                lines.Add(new CombineInstance { mesh=cylinder.GetComponent<MeshFilter>().sharedMesh,
                    transform=Matrix4x4.TRS((a+b)*.5f,Quaternion.FromToRotation(Vector3.up,delta),new Vector3(.045f,delta.magnitude*.5f,.045f)) });
            }
            if(lines.Count>0) Add(lines.ToArray(),carbon);
        }
        finally { Destroy(sphere); Destroy(cylinder); }
    }
    void Add(CombineInstance[] parts,Color color)
    {
        var mesh=new Mesh { indexFormat=IndexFormat.UInt32 }; mesh.CombineMeshes(parts,true,true); _meshes.Add(mesh);
        var material=new Material(RuntimeMaterials.Solid); material.color=color;
        if(material.HasProperty("_BaseColor")) material.SetColor("_BaseColor",color);
        _materials.Add(material);
        var go=new GameObject("Candidate geometry",typeof(MeshFilter),typeof(MeshRenderer)); go.transform.SetParent(transform,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh; go.GetComponent<MeshRenderer>().sharedMaterial=material;
    }
    void OnDestroy()
    {
        foreach(var mesh in _meshes) if(mesh!=null) Destroy(mesh);
        foreach(var material in _materials) if(material!=null) Destroy(material);
    }
}
