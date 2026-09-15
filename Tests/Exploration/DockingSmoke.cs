using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public static class DockingSmoke
{
    static void Check(bool okay,string reason) { if(!okay) throw new Exception(reason); }
    static Button Control(string name) => UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(x=>x.name==name);
    static DockingLigand Ligand(string id) => new DockingLigand { id=id,title=id,sdf="test structure",
        atoms=new[] { new DockingAtom { element="C" },new DockingAtom { element="C",x=1.5f },new DockingAtom { element="O",x=2.8f } },
        bonds=new[] { new DockingBond { a=0,b=1 },new DockingBond { a=1,b=2 } } };
    static DockingReply Result(DockingLigand ligand) => new DockingReply { status="completed",comparisonKey="same-target-site",
        poses=new[] { new DockingPose { score=-4,atoms=ligand.atoms,bonds=ligand.bonds } } };
    public static IEnumerator Run(Camera camera,MoleculeExplorationController owner)
    {
        var session=new ExplorationDockingSession(); session.Bind("1IEP","A");
        session.Site=new DockingSite { label="Pocket",center=new float[] {15.19f,53.903f,16.917f},size=new float[] {20,20,20} };
        var a=Ligand("A"); var b=Ligand("B"); session.SetCandidate(a);
        var first=session.Begin(); session.Complete(first,Result(a));
        var site=session.Site; session.SetCandidate(b);
        Check(session.Trials.Count==1 && session.Trials[0].ligand==a && session.Site==site,"Replacing candidate lost first trial/site");
        var second=session.Begin(); session.Complete(second,Result(b)); session.Complete(second,Result(b));
        Check(session.Trials.Count==2 && session.Trials[1].ligand==b,"Second candidate or idempotent completion failed");
        bool invalid=false; try { session.SetCandidate(new DockingLigand { sdf="invalid" }); } catch(InvalidOperationException) { invalid=true; }
        Check(invalid && session.Candidate==b && session.Trials.Count==2,"Failed candidate replacement destroyed valid state");
        Check(!session.Bind("1IEP","A") && session.Trials.Count==2,"View refresh erased experiments");
        session.Reset(); session.Bind("1IEP","A"); invalid=false;
        try { session.Complete(first,Result(a)); } catch(InvalidOperationException) { invalid=true; }
        Check(invalid,"Old session's completed request was accepted");
        string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../DockingChecks"));
        if(!File.Exists(Path.Combine(directory,"live-results.json")))
        { File.WriteAllText("docking-smoke.txt","PASS: repeated candidates, failure preservation, stale session rejection. Live poses not available."); yield break; }
        var report=JsonUtility.FromJson<LiveReport>(File.ReadAllText(Path.Combine(directory,"live-results.json")));
        string pdb=File.ReadAllText(Path.Combine(directory,"1IEP.pdb")); var atoms=ExplorationPdbParser.Parse(pdb);
        var root=new GameObject("Real docking receptor"); var protein=root.AddComponent<ExplorationMoleculeRenderer>();
        protein.HideBoundLigands=true;
        var spec=new MoleculeViewSpec { pdbId="1IEP",title="ABL kinase",chains="A" };
        yield return protein.Build(atoms,spec,"ribbon");
        root.transform.position=new Vector3(.3f,.15f,0); root.transform.localScale*=1.4f;
        owner.StartExploration(); yield return null; yield return null;
        var docking=owner.GetComponent<ExplorationDockingController>(); docking.BindTarget(spec,atoms,pdb,protein); docking.Open();
        Check(GameObject.Find("Direct input")==null,"Manual entry must be optional, not the initial UI");
        Check(Control("STI 불러오기")!=null,"Bound ligand candidate card missing");
        Control("부위 선택").onClick.Invoke(); yield return null;
        Control("STI 주변 · A:201").onClick.Invoke(); yield return null;
        Check(docking.Session.Site!=null,"Site card did not select a real pocket");
        Check(GameObject.Find("Docking search region")!=null,"Site selection must show the search box");
        Control("후보 선택").onClick.Invoke(); yield return null;
        Check(GameObject.Find("Docking search region")==null,"Candidate tab retained search box");
        Control("부위 선택").onClick.Invoke(); yield return null;
        Check(GameObject.Find("Docking search region")!=null,"Returning to site selection did not restore search box");
        docking.Session.Site=site;
        docking.SelectResidues("Z:999"); yield return null;
        Check(docking.Session.Site==site,"Invalid site erased previous selection");
        Control("이전 부위로 계속").onClick.Invoke(); yield return null;
        foreach(var live in report.trials)
        {
            docking.Session.SetCandidate(live.ligand); var trial=docking.Session.Begin(); docking.Session.Complete(trial,live.result);
        }
        Control("실험 기록").onClick.Invoke(); yield return null;
        Control("1. "+report.trials[0].ligand.title).onClick.Invoke(); yield return null;
        Check(GameObject.Find("Computed docking pose")!=null,"Actual Vina pose did not produce geometry");
        Check(GameObject.Find("Docking search region")==null,"Completed result retained search box");
        string firstContext=docking.DiscussionContext();
        Check(firstContext.Contains("aspirin")&&firstContext.Contains("caffeine"),"Experiment discussion lost earlier candidate");
        var visual=GameObject.Find("Computed docking pose");
        Check(visual.transform.parent==protein.transform,"Pose does not follow protein rotation");
        var point=report.trials[0].result.poses[0].atoms[0].Position;
        Vector3 before=protein.transform.TransformPoint(protein.PdbToLocal(point));
        protein.transform.Rotate(Vector3.up,45); Vector3 after=protein.transform.TransformPoint(protein.PdbToLocal(point));
        Check(Vector3.Distance(before,after)>.001f,"Pose coordinate transform ignored rotation");
        Control("2. "+report.trials[1].ligand.title).onClick.Invoke(); yield return null;
        Check(docking.Session.Trials.Count==2,"Reviewing past experiments mutated history");
        var nextPose=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(x=>x.name=="다음 자세");
        nextPose.onClick.Invoke(); yield return null;
        Check(docking.DiscussionContext().Contains("자세 2"),"Pose cycling did not reach discussion context");
        docking.BindTarget(spec,atoms,pdb,protein);
        Check(docking.Session.Trials.Count==2,"Renderer replacement cleared experiments");
        Control("실험 기록").onClick.Invoke(); yield return null;
        Check(Control("1. "+report.trials[0].ligand.title)!=null,"Trial cards are missing");
        Control("다른 후보 실험").onClick.Invoke(); yield return null;
        Check(docking.Session.Trials.Count==2 && docking.Session.Site==site && docking.Session.PdbId=="1IEP","Next candidate reset the target/site/history");
        Check(GameObject.Find("Computed docking pose")==null,"Next candidate retained historical pose");
        Check(GameObject.Find("Pocket outline")==null,"Next candidate recreated the blue region ring");
        Control("부위 선택").onClick.Invoke(); yield return null;
        Check(GameObject.Find("Pocket outline")!=null,"Site selection did not restore its region ring");
        docking.HidePanel(); yield return null;
        Check(GameObject.Find("Pocket outline")==null,"Closed panel retained its region ring");
        docking.Open(); yield return null;
        Control("실험 기록").onClick.Invoke(); yield return null;
        Control("2. "+report.trials[1].ligand.title).onClick.Invoke(); yield return null;
        // Exercise the focus motion with the receptor owned by the real exploration component.
        typeof(MoleculeExplorationController).GetField("_renderer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(owner,protein);
        owner.FocusDockingCamera(); yield return new WaitForSecondsRealtime(.55f);
        var focus=camera.WorldToViewportPoint(protein.transform.TransformPoint(protein.PdbToLocal(new Vector3(site.center[0],site.center[1],site.center[2]))));
        Check(Vector2.Distance(new Vector2(focus.x,focus.y),new Vector2(.55f,.46f))<.01f,"Pocket was not framed beside the panel");
        var assistant=GameObject.CreatePrimitive(PrimitiveType.Sphere); assistant.name="Layout test assistant";
        var follower=assistant.AddComponent<AIAssistantFollower>(); follower.followTarget=camera.transform; follower.anchorTarget=protein.transform;
        follower.SnapToAnchor(); yield return null; yield return null;
        var bounds=assistant.GetComponent<Renderer>().bounds;
        Check(camera.WorldToViewportPoint(bounds.center-camera.transform.right*bounds.extents.magnitude).x>.70f,"Assistant intrudes into docking region");
        UnityEngine.Object.Destroy(assistant);
        // Render the screen overlay through the test camera so batch mode can capture it.
        var canvas=GameObject.Find("Exploration docking overlay").GetComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=.4f;
        var target=new RenderTexture(1440,900,24); camera.targetTexture=target; Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active=target;
        var screenshot=new Texture2D(1440,900,TextureFormat.RGB24,false); screenshot.ReadPixels(new Rect(0,0,1440,900),0,0); screenshot.Apply();
        File.WriteAllBytes("docking-ui.png",screenshot.EncodeToPNG()); camera.targetTexture=null; RenderTexture.active=null; target.Release();
        UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(screenshot); canvas.renderMode=RenderMode.ScreenSpaceOverlay;
        if(Environment.GetEnvironmentVariable("DOCKING_LIVE_UNITY")=="1")
        {
            docking.CloseSession(); docking.Initialize(owner); docking.BindTarget(spec,atoms,pdb,protein); docking.Session.Site=site;
            var config=JsonUtility.FromJson<LocalConfig>(File.ReadAllText(Path.Combine(directory,"editor-service.json")));
            owner.dockingEndpoint=config.dockingEndpoint; owner.dockingToken=config.token;
            docking.ShowCandidateChoices(new[] { "aspirin","caffeine" }); yield return null;
            foreach(string query in new[] { "aspirin","caffeine" })
            {
                Control(query).onClick.Invoke();
                double deadline=UnityEditor.EditorApplication.timeSinceStartup+60;
                while(docking.IsBusy && UnityEditor.EditorApplication.timeSinceStartup<deadline) yield return null;
                Check(!docking.IsBusy && docking.Session.Candidate!=null && docking.Session.Candidate.title.Contains(query),"Unity HTTP candidate loading failed: "+query);
                int count=docking.Session.Trials.Count; docking.RunDocking();
                Check(GameObject.Find("Docking search region")==null,"Calculation retained selection box");
                Check(GameObject.Find("Pocket scan")!=null && GameObject.Find("Computed docking pose")==null,"Calculation must show scanning without invented result geometry");
                docking.LoadCandidate("must not replace the active trial");
                Check(docking.Session.Candidate.title.Contains(query),"Active docking allowed candidate replacement");
                deadline=UnityEditor.EditorApplication.timeSinceStartup+120;
                while(docking.IsBusy && UnityEditor.EditorApplication.timeSinceStartup<deadline) yield return null;
                Check(!docking.IsBusy && docking.Session.Trials.Count==count+1 && docking.Session.Trials[count].result.status=="completed","Unity HTTP docking did not finish: "+query);
                Check(docking.IsPresenting,"Real result did not start placement animation");
                while(docking.IsPresenting) yield return null;
                var computed=GameObject.Find("Computed docking pose").transform;
                Check(computed.localPosition==Vector3.zero && computed.localScale==Vector3.one,"Animation changed the final computed coordinates");
                Check(GameObject.Find("Nearby residues (4 Angstrom)")!=null && GameObject.Find("Pocket scan")==null,"Final contacts/scan lifecycle incorrect");
                Control("다른 후보 실험").onClick.Invoke(); yield return null;
                Check(GameObject.Find("Pocket outline")==null,"Live repeat experiment restored the completed region ring");
                Check(docking.Session.Site==site && docking.Session.Trials.Count==count+1,"Repeat button lost scientific state");
            }
            Check(docking.Session.Trials[0].ligand.cid=="2244" && docking.Session.Trials[1].ligand.cid=="2519","Repeated Unity experiments lost candidate identity");
            Check(docking.Session.Trials[0].result.comparisonKey==docking.Session.Trials[1].result.comparisonKey,"Repeated Unity experiments changed target/site");
            Control("후보 선택").onClick.Invoke(); yield return null;
            Control("STI 불러오기").onClick.Invoke();
            double ccdDeadline=UnityEditor.EditorApplication.timeSinceStartup+60;
            while(docking.IsBusy && UnityEditor.EditorApplication.timeSinceStartup<ccdDeadline) yield return null;
            Check(!docking.IsBusy && docking.Session.Candidate.source=="RCSB CCD STI" && docking.Session.Trials.Count==2,"Bound ligand card failed to load CCD or erased history");
            docking.RunDocking(); docking.CancelOperation(); yield return null;
            Check(!docking.IsBusy && GameObject.Find("Pocket scan")==null && docking.Session.Trials.Count==2,"Cancellation leaked scan or changed completed history");
            var previousCandidate=docking.Session.Candidate;
            docking.LoadCandidate("SMILES:CC(O)C(=O)O");
            double stereoDeadline=UnityEditor.EditorApplication.timeSinceStartup+60;
            while(docking.IsBusy && UnityEditor.EditorApplication.timeSinceStartup<stereoDeadline) yield return null;
            Check(docking.Session.Candidate==previousCandidate && !Control("도킹 실행").interactable,"Unresolved stereo silently reused previous candidate");
            var stereoCard=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None).First(b=>b.name.StartsWith("입체형 1"));
            stereoCard.onClick.Invoke();
            while(docking.IsBusy && UnityEditor.EditorApplication.timeSinceStartup<stereoDeadline) yield return null;
            Check(!docking.IsBusy && docking.Session.Candidate.smiles.Contains("@") && Control("도킹 실행").interactable,"Stereo choice did not resolve into dockable structure");
            docking.RunDocking(); stereoDeadline=UnityEditor.EditorApplication.timeSinceStartup+120;
            while(docking.IsBusy && UnityEditor.EditorApplication.timeSinceStartup<stereoDeadline) yield return null;
            Check(docking.Session.Trials.Count==3 && docking.Session.Trials[2].result.status=="completed","Selected stereoisomer failed real docking");
            var resolvedCandidate=docking.Session.Candidate;
            docking.LoadCandidate("SMILES:bad"); stereoDeadline=UnityEditor.EditorApplication.timeSinceStartup+20;
            while(docking.IsBusy && UnityEditor.EditorApplication.timeSinceStartup<stereoDeadline) yield return null;
            Check(docking.Session.Candidate==resolvedCandidate && !Control("도킹 실행").interactable,"Failed loading reused previous candidate without confirmation");
            Control("이전 후보로 계속").onClick.Invoke(); yield return null;
            Check(Control("도킹 실행").interactable && docking.Session.Trials.Count==3,"Explicit recovery did not restore experiment readiness");
            File.WriteAllText("docking-http-smoke.txt","PASS: candidate cards -> Unity -> local Worker -> PubChem/Meeko/Vina -> Unity; aspirin then caffeine, retained first result/site, computed-pose arrival, contact markers, CCD STI card, cancellation cleanup.");
        }
        docking.CloseSession(); yield return null;
        Check(GameObject.Find("Computed docking pose")==null,"Leaving exploration leaked the pose");
        UnityEngine.Object.Destroy(root);
        File.WriteAllText("docking-smoke.txt","PASS: two real Vina candidates, original-coordinate pose meshes, history/pose controls, LLM context, rerender and exit cleanup.");
    }
    [Serializable] class LiveReport { public LiveTrial[] trials; }
    [Serializable] class LiveTrial { public DockingLigand ligand; public DockingReply result; }
    [Serializable] class LocalConfig { public string dockingEndpoint,token; }
}
