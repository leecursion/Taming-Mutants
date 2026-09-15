using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Presentation of real results; motion is never used to infer a score or trajectory.</summary>
public sealed class ExplorationDockingEffects : MonoBehaviour
{
    LineRenderer _ring,_sweep;
    float _radius,_started;
    bool _calculating;
    GameObject _contacts;
    public bool IsPresenting { get; private set; }
    public void ShowRegion(Vector3 center,float radius,bool calculating)
    {
        _radius=radius; _calculating=calculating; _started=Time.unscaledTime;
        _ring=MutationExperimentEffects.Line(transform,"Pocket outline",new Color(.15f,.85f,1,.65f),.012f);
        _ring.transform.localPosition=center;
        _sweep=MutationExperimentEffects.Line(transform,"Pocket scan",new Color(.4f,1,.8f,.8f),.022f);
        _sweep.transform.localPosition=center;
        _sweep.gameObject.SetActive(calculating);
        MutationExperimentEffects.Ring(_ring,radius);
    }
    public void SetRegionVisible(bool visible)
    {
        if(_ring!=null) _ring.gameObject.SetActive(visible);
        if(_sweep!=null) _sweep.gameObject.SetActive(visible && _calculating);
    }
    void Update()
    {
        if(_ring==null || !_ring.gameObject.activeSelf) return;
        float time=Time.unscaledTime-_started;
        MutationExperimentEffects.Ring(_ring,_radius*(1f+.025f*Mathf.Sin(time*3)));
        if(_calculating)
        {
            MutationExperimentEffects.Ring(_sweep,_radius*.95f,.28f);
            _sweep.transform.localRotation=Quaternion.Euler(0,time*32,time*85);
        }
    }
    public void ShowCandidate(Transform candidate) { StartCoroutine(Appear(candidate)); }
    IEnumerator Appear(Transform candidate)
    {
        for(float elapsed=0;elapsed<.35f;elapsed+=Time.unscaledDeltaTime)
        {
            if(candidate==null) yield break;
            candidate.localScale=Vector3.one*Mathf.SmoothStep(.05f,1,elapsed/.35f); yield return null;
        }
        if(candidate!=null) candidate.localScale=Vector3.one;
    }
    public void ShowPose(Transform pose,Vector3 offset,bool animate)
    {
        _calculating=false;
        if(_ring!=null) _ring.gameObject.SetActive(false);
        if(_sweep!=null) _sweep.gameObject.SetActive(false);
        if(!animate) return;
        IsPresenting=true; StartCoroutine(PlacePose(pose,offset));
    }
    IEnumerator PlacePose(Transform pose,Vector3 offset)
    {
        const float duration=1.15f;
        for(float elapsed=0;elapsed<duration;elapsed+=Time.unscaledDeltaTime)
        {
            if(pose==null) { IsPresenting=false; yield break; }
            float amount=Mathf.SmoothStep(0,1,elapsed/duration);
            pose.localPosition=Vector3.Lerp(offset,Vector3.zero,amount);
            pose.localScale=Vector3.one*Mathf.Lerp(.75f,1,amount);
            if(_contacts!=null) _contacts.SetActive(false);
            yield return null;
        }
        if(pose!=null) { pose.localPosition=Vector3.zero; pose.localScale=Vector3.one; }
        if(_contacts!=null) _contacts.SetActive(true);
        IsPresenting=false;
        if(_ring!=null) StartCoroutine(CompletionPulse());
    }
    IEnumerator CompletionPulse()
    {
        var ring=MutationExperimentEffects.Line(transform,"Result arrival",new Color(.4f,1,.65f),.03f);
        ring.transform.localPosition=_ring.transform.localPosition;
        for(float elapsed=0;elapsed<.65f;elapsed+=Time.unscaledDeltaTime)
        {
            float p=elapsed/.65f; MutationExperimentEffects.Ring(ring,_radius*(.65f+.45f*p));
            ring.startColor=ring.endColor=new Color(.4f,1,.65f,1-p); yield return null;
        }
        Destroy(ring.gameObject);
    }
    public void ShowContacts(IEnumerable<ExplorationAtom> protein,DockingAtom[] ligand,Func<Vector3,Vector3> convert)
    {
        _contacts=new GameObject("Nearby residues (4 Angstrom)"); _contacts.transform.SetParent(transform,false);
        // Match the service's geometric 4-Angstrom criterion. Draw at most 24 residue markers.
        foreach(var residue in protein.Where(a=>ligand.Any(b=>(a.position-b.Position).sqrMagnitude<=16f)).GroupBy(a=>a.residueKey).Take(24))
        {
            var nearest=residue.OrderBy(a=>ligand.Min(b=>(a.position-b.Position).sqrMagnitude)).First();
            var marker=MutationExperimentEffects.Line(_contacts.transform,"Contact "+nearest.chain+":"+nearest.number,new Color(1,.78f,.25f),.018f);
            marker.transform.localPosition=convert(nearest.position);
            MutationExperimentEffects.Ring(marker,.11f);
        }
        _contacts.SetActive(!IsPresenting);
    }
}
