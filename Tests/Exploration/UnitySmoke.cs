using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

[InitializeOnLoad]
public static class ExplorationSmoke
{
    static IEnumerator routine;
    static readonly Stack<IEnumerator> stack=new Stack<IEnumerator>();
    static double started;
    static ExplorationSmoke()
    {
        EditorApplication.playModeStateChanged += state => {
            if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("ExplorationSmoke",false)) {
                SessionState.SetBool("ExplorationSmoke",false);
                routine=RunChecks(); stack.Push(routine); started=EditorApplication.timeSinceStartup;
                EditorApplication.update += Tick;
            }
        };
    }
    public static void Run()
    {
        SessionState.SetBool("ExplorationSmoke",true);
        EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        try {
            if(EditorApplication.timeSinceStartup-started>(Environment.GetEnvironmentVariable("DOCKING_LIVE_UNITY")=="1"?300:90)) throw new Exception("Test timeout");
            if(stack.Count==0) { File.WriteAllText("smoke-result.txt","PASS"); EditorApplication.update-=Tick; EditorApplication.Exit(0); return; }
            bool moved=stack.Peek().MoveNext();
            if(!moved) { (stack.Pop() as IDisposable)?.Dispose(); return; }
            if(stack.Peek().Current is IEnumerator child) stack.Push(child);
        } catch(Exception ex) {
            File.WriteAllText("smoke-result.txt","FAIL "+ex); Debug.LogException(ex);
            EditorApplication.update-=Tick; EditorApplication.Exit(1);
        }
    }
    static void Capture(Camera camera,string filename) {
        var target=new RenderTexture(960,720,24); camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
        var image=new Texture2D(960,720,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,960,720),0,0); image.Apply();
        File.WriteAllBytes(filename,image.EncodeToPNG()); camera.targetTexture=null; RenderTexture.active=null; target.Release();
        UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(image);
    }
    static void Check(bool condition,string reason) { if(!condition) throw new Exception(reason); }
    static IEnumerator RunChecks()
    {
        StateTesterSmoke.Run(false);
        var normalized=new MoleculeViewSpec { pdbId=" 4hhb ", chains=" A, B,A " };
        normalized.NormalizeAndValidate();
        Check(normalized.pdbId=="4HHB" && normalized.chains=="A,B","Resolver formatting rejected");
        foreach(string bad in new[] { "4HHB_1", "../x", "" })
        {
            bool invalid=false;
            try { new MoleculeViewSpec { pdbId=bad }.NormalizeAndValidate(); }
            catch(InvalidOperationException) { invalid=true; }
            Check(invalid,"Invalid PDB ID accepted: "+bad);
        }
        Check(MoleculeCatalog.Match("사람의 헤모글로빈 보여줘")?.id=="hemoglobin","Korean lookup");
        Check(MoleculeCatalog.Match("돼지 인슐린 보여줘")==null,"Species must not silently change");
        Check(MoleculeCatalog.Match("산소 결합 헤모글로빈 보여줘")==null,"State must not silently change");
        bool rejected=false;
        try { ExplorationPdbParser.Parse("garbage"); } catch(FormatException) { rejected=true; }
        Check(rejected,"Bad PDB accepted");
        var cameraObject=new GameObject("Test Camera",typeof(Camera)); var camera=cameraObject.GetComponent<Camera>();
        camera.transform.position=new Vector3(0,0,-2); camera.backgroundColor=new Color(.015f,.03f,.06f);
        camera.clearFlags=CameraClearFlags.SolidColor; camera.fieldOfView=42;
        // 실제 카메라 전환으로 사건 루트와 onEnter가 AI 탐색에서 켜지지 않는지 확인한다.
        var stageObject=new GameObject("Case stage");
        var stage=stageObject.AddComponent<LevelStage>(); stage.level=QuestLevel.Level2_Protein;
        stage.contentRoot=new GameObject("Case domain"); stage.contentRoot.SetActive(false);
        int entered=0; stage.onEnter=new UnityEngine.Events.UnityEvent(); stage.onEnter.AddListener(()=>entered++);
        var directorObject=new GameObject("Camera director"); directorObject.SetActive(false);
        var director=directorObject.AddComponent<CameraTransitionDirector>();
        director.targetCamera=camera; director.stages=new[] { stage }; directorObject.SetActive(true);
        director.fallback.duration=.05f;
        director.GoToCameraOnly(QuestLevel.Level2_Protein);
        while(director.IsTransitioning) { Check(!stage.contentRoot.activeSelf,"Case domain appeared during AI transition"); yield return null; }
        Check(!stage.IsActive && !stage.contentRoot.activeSelf && entered==0,"AI transition activated case content/hooks");
        director.SnapTo(QuestLevel.Level0_Body);
        director.fallback.duration=0f;
        director.GoToCameraOnly(QuestLevel.Level2_Protein);
        Check(!stage.IsActive && entered==0,"Cut transition activated case content/hooks");
        director.SnapTo(QuestLevel.Level0_Body);
        director.GoTo(QuestLevel.Level2_Protein);
        Check(stage.IsActive && stage.contentRoot.activeSelf && entered==1,"Normal case transition no longer activates content");
        UnityEngine.Object.Destroy(stage.contentRoot); UnityEngine.Object.Destroy(stageObject); UnityEngine.Object.Destroy(directorObject);
        camera.transform.position=new Vector3(0,0,-2); camera.transform.rotation=Quaternion.identity;
        var lightObject=new GameObject("Test Light",typeof(Light));
        lightObject.GetComponent<Light>().type=LightType.Directional;
        lightObject.transform.rotation=Quaternion.Euler(35,-30,0);
        RenderSettings.ambientLight=Color.gray;
        var report=new List<string>();
        // Optional opt-in evidence from Check-LiveResolver --local: render actual model-selected PDBs.
        string livePath=Path.GetFullPath(File.Exists("../diverse-local.json")?"../diverse-local.json":"../live-local-resolver.json");
        if(File.Exists(livePath)) {
            var live=JsonUtility.FromJson<LiveReport>("{\"turns\":"+File.ReadAllText(livePath)+"}");
            foreach(var turn in live.turns.Where(t=>t.result!=null && t.result.action=="load")) {
                var spec=turn.result.spec; spec.NormalizeAndValidate();
                string pdb;
                using(var request=UnityWebRequest.Get("https://files.rcsb.org/download/"+spec.pdbId+".pdb")) {
                    request.timeout=20; var operation=request.SendWebRequest();
                    while(!operation.isDone) yield return null;
                    Check(request.result==UnityWebRequest.Result.Success,"Live selected PDB download failed: "+spec.pdbId);
                    pdb=request.downloadHandler.text;
                }
                var liveRoot=new GameObject("Live model geometry "+spec.pdbId);
                var liveRenderer=liveRoot.AddComponent<ExplorationMoleculeRenderer>();
                yield return liveRenderer.Build(ExplorationPdbParser.Parse(pdb),spec,spec.view);
                Check(liveRenderer.HasGeometry && liveRenderer.DisplayedResidues>0,"Live model result produced no geometry");
                report.Add("LIVE LLM + RCSB -> Unity geometry: "+turn.query+" -> "+spec.pdbId+" / "+spec.title+" / "+liveRenderer.DisplayedResidues+" residues PASS");
                UnityEngine.Object.Destroy(liveRoot); yield return null;
            }
        }
        foreach(var spec in MoleculeCatalog.Entries)
        {
            var atoms=ExplorationPdbParser.Parse(File.ReadAllText(Application.streamingAssetsPath+"/exploration/"+spec.pdbId+".pdb"));
            var root=new GameObject(spec.id); var renderer=root.AddComponent<ExplorationMoleculeRenderer>();
            foreach(string view in new[]{"ribbon","atoms"})
            {
                yield return renderer.Build(atoms,spec,view);
                yield return null;
                var meshes=root.GetComponentsInChildren<MeshFilter>();
                Check(renderer.HasGeometry && meshes.Length>0,"No mesh for "+spec.id);
                Check(renderer.DisplayedResidues<=renderer.ResidueLimit,"Residue budget exceeded");
                Check(renderer.DisplayedAtoms<=renderer.AtomLimit,"Atom budget exceeded");
                Check(root.transform.childCount<20,"Per-atom objects created");
                foreach(var mesh in meshes) {
                    Check(mesh.sharedMesh.vertexCount>0,"Empty mesh");
                    Check(mesh.sharedMesh.vertices.All(v=>!float.IsNaN(v.x)&&!float.IsInfinity(v.x)),"Invalid vertices");
                }
                report.Add(spec.id+" "+view+" meshes="+meshes.Length+" residues="+renderer.DisplayedResidues+" atoms="+renderer.DisplayedAtoms);
                var target=new RenderTexture(960,720,24); camera.targetTexture=target; camera.Render();
                RenderTexture.active=target;
                var image=new Texture2D(960,720,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,960,720),0,0); image.Apply();
                File.WriteAllBytes(spec.id+"-"+view+".png",image.EncodeToPNG());
                camera.targetTexture=null; RenderTexture.active=null; target.Release();
                UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(image);
            }
            UnityEngine.Object.Destroy(root); yield return null;
        }
        var hb=ExplorationPdbParser.Parse(File.ReadAllText(Application.streamingAssetsPath+"/exploration/4HHB.pdb"));
        var selectionRoot=new GameObject("Selection regression");
        var selectionRenderer=selectionRoot.AddComponent<ExplorationMoleculeRenderer>();
        var rangeSpec=new MoleculeViewSpec { id="hemoglobin",title="Hemoglobin",pdbId="4HHB",chains="A",residueStart=50,residueEnd=80 };
        yield return selectionRenderer.Build(hb,rangeSpec,"ribbon"); yield return null;
        Check(selectionRenderer.DisplayedResidues==31,"Requested residue range was not rendered");
        rangeSpec.residueStart=rangeSpec.residueEnd=50;
        yield return selectionRenderer.Build(hb,rangeSpec,"ribbon"); yield return null;
        Check(selectionRenderer.HasGeometry && selectionRenderer.DisplayedResidues==1,"Single residue fallback failed");
        rangeSpec.residueStart=rangeSpec.residueEnd=0; rangeSpec.ligand="HEM";
        yield return selectionRenderer.Build(hb,rangeSpec,"ligand"); yield return null;
        Check(selectionRenderer.DisplayedResidues==0 && selectionRenderer.DisplayedAtoms==43,"Ligand-only view includes protein");
        rangeSpec.ligand=""; rangeSpec.secondaryStructure="helix";
        yield return selectionRenderer.Build(hb,rangeSpec,"ribbon"); yield return null;
        Check(selectionRenderer.DisplayedResidues>0 && selectionRenderer.DisplayedResidues<141,"Helix selection ignored");
        Check(selectionRenderer.DisplayedAtoms==0,"Helix-only view includes unrelated ligand atoms");
        rangeSpec.secondaryStructure=""; rangeSpec.chains="A,B,C,D";
        yield return selectionRenderer.Build(hb,rangeSpec,"ribbon"); yield return null;
        Check(selectionRenderer.DisplayedResidues==selectionRenderer.ResidueLimit,"Expanded selection escaped residue budget");
        rangeSpec.chains="Z";
        rejected=false;
        var invalidSelection=selectionRenderer.Build(hb,rangeSpec,"ribbon");
        try { invalidSelection.MoveNext(); } catch(InvalidOperationException) { rejected=true; }
        Check(rejected,"Missing chain silently replaced");
        UnityEngine.Object.Destroy(selectionRoot); yield return null;
        report.Add("Range 50..80, single residue, ligand-only, helix-only, full-selection cap, invalid chain PASS");
        // Explicit load cap: malformed and oversized inputs are refused before rendering.
        rejected=false; try { ExplorationPdbParser.Parse(new string(' ',ExplorationPdbParser.MaxBytes+1)); } catch(FormatException) { rejected=true; }
        Check(rejected,"Oversized file accepted");
        var introObject=new GameObject("Intro");
        var intro=introObject.AddComponent<IntroDirector>(); intro.targetCamera=camera;
        intro.assistant=new GameObject("Test assistant").AddComponent<AIAssistantBrain>();
        intro.board=introObject.AddComponent<QuestSelectionBoard>();
        var controller=introObject.AddComponent<MoleculeExplorationController>(); controller.Initialize(intro);
        controller.ShowModeSelection(); yield return null; yield return null;
        var buttons=UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None);
        Check(buttons.Any(b=>b.name=="사례 탐색") && buttons.Any(b=>b.name=="AI 분자 탐색"),"Mode choices missing");
        Capture(camera,"mode-choice.png");
        buttons.First(b=>b.name=="사례 탐색").onClick.Invoke();
        Check(intro.played,"Case selection did not call existing intro");
        yield return null; yield return null;
        // 사례 탐색에서도 AI 분자 탐색과 같은 우하단 '모드 선택' 버튼으로 빠져나간다.
        var caseExit=UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)
            .FirstOrDefault(b=>b.name=="모드 선택" && b.gameObject.activeInHierarchy);
        Check(caseExit!=null,"Case mode is missing the shared mode button");
        var caseExitRect=(RectTransform)caseExit.transform;
        Check(caseExitRect.anchorMin==new Vector2(1,0) && caseExitRect.anchorMax==new Vector2(1,0),
            "Case mode button is not pinned to the bottom-right corner");
        int returnsBeforeCaseExit=intro.returnedToLab;
        caseExit.onClick.Invoke(); yield return null;
        Check(UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)
            .Any(b=>b.name=="사례 탐색"),"Case mode button did not return to mode selection");
        Check(intro.returnedToLab==returnsBeforeCaseExit+1,"Mode button did not restore the lab background");
        // 배경 전환: AI 분자 탐색으로 들어갈 때는 사건 진입과 같은 연출로 파고들고,
        // 모드 선택으로 나올 때마다 연구실 시점으로 되돌아온다.
        int returnsBefore=intro.returnedToLab;
        controller.StartExploration();
        Check(GameObject.Find("단백질 추천")==null && GameObject.Find("말로 요청")==null,"Removed toolbar buttons still present");
        Check(intro.assistant.spoken.Any(s=>s.Contains("단백질을 추천")),"Initial guidance omitted protein recommendations");
        Check(intro.enteredStage==1,"Exploration mode did not play the case-style stage transition");
        StateTesterSmoke.Run(true);
        report.Add("Space/number test shortcuts: opt-in only, blocked while typing and during AI exploration PASS");
        Check(intro.returnedToLab==returnsBefore,"Entering exploration also pulled the camera back to the lab");
        controller.Submit("사람의 헤모글로빈 보여줘");
        double deadline=EditorApplication.timeSinceStartup+35;
        ExplorationMoleculeRenderer loaded=null;
        while(EditorApplication.timeSinceStartup<deadline) {
            loaded=UnityEngine.Object.FindObjectsByType<ExplorationMoleculeRenderer>(FindObjectsSortMode.None).FirstOrDefault(r=>r.gameObject.activeInHierarchy && r.HasGeometry);
            if(loaded!=null) break;
            yield return null;
        }
        Check(loaded!=null,"Text request did not produce visible geometry");
        yield return null; yield return null;
        Capture(camera,"exploration-ui.png");
        // 구조는 카메라 앞에 한 번만 놓이므로, 진입 연출이 날아가는 동안에는 세우지 않는다.
        intro.IsStageTransitioning=true;
        controller.Submit("리본");
        for(int i=0;i<40;i++) yield return null;
        Check(!UnityEngine.Object.FindObjectsByType<ExplorationMoleculeRenderer>(FindObjectsSortMode.None)
            .Any(r=>r!=loaded && r.gameObject.activeInHierarchy && r.HasGeometry),
            "Structure was placed while the stage transition was still flying");
        intro.IsStageTransitioning=false;
        ExplorationMoleculeRenderer replaced=null;
        double placeDeadline=EditorApplication.timeSinceStartup+15;
        while(EditorApplication.timeSinceStartup<placeDeadline)
        {
            replaced=UnityEngine.Object.FindObjectsByType<ExplorationMoleculeRenderer>(FindObjectsSortMode.None)
                .FirstOrDefault(r=>r!=loaded && r.gameObject.activeInHierarchy && r.HasGeometry);
            if(replaced!=null) break;
            yield return null;
        }
        Check(replaced!=null,"Structure was not placed once the transition finished");
        loaded=replaced;
        // The original regression: a configured model must receive even a known representative name.
        // Return a view command deliberately, proving the client executes model intent rather than its name dictionary.
        var portProbe=new TcpListener(IPAddress.Loopback,0); portProbe.Start();
        int port=((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
        using(var listener=new HttpListener())
        {
            listener.Prefixes.Add("http://localhost:"+port+"/"); listener.Start();
            var responseTask=Task.Run(async()=> {
                var incoming=await listener.GetContextAsync();
                string payload;
                using(var reader=new StreamReader(incoming.Request.InputStream)) payload=await reader.ReadToEndAsync();
                await Task.Delay(900);
                byte[] bytes=Encoding.UTF8.GetBytes("{\"action\":\"view\",\"view\":\"atoms\"}");
                incoming.Response.ContentType="application/json"; incoming.Response.ContentLength64=bytes.Length;
                await incoming.Response.OutputStream.WriteAsync(bytes,0,bytes.Length); incoming.Response.Close();
                return payload;
            });
            controller.resolverEndpoint="http://localhost:"+port+"/api/molecule-resolve";
            intro.assistant.spoken.Clear();
            controller.Submit("사람의 헤모글로빈 보여줘");
            yield return new WaitForSecondsRealtime(.6f);
            controller.Submit("사람의 헤모글로빈 보여줘");
            Check(intro.assistant.spoken.Count(s=>s=="요청을 확인하고 있어요…")==1,"In-flight duplicate repeated the loading announcement");
            double responseDeadline=EditorApplication.timeSinceStartup+12;
            ExplorationMoleculeRenderer changed=null;
            while(EditorApplication.timeSinceStartup<responseDeadline)
            {
                changed=UnityEngine.Object.FindObjectsByType<ExplorationMoleculeRenderer>(FindObjectsSortMode.None)
                    .FirstOrDefault(r=>r!=loaded && r.gameObject.activeInHierarchy && r.HasGeometry);
                if(changed!=null && responseTask.IsCompleted) break;
                yield return null;
            }
            Check(responseTask.IsCompleted && !responseTask.IsFaulted,"Configured representative bypassed resolver");
            string sent=responseTask.Result;
            Check(sent.Contains("current") && sent.Contains("4HHB") && sent.Contains("availableChains"),"Current structure context missing");
            Check(changed!=null && changed.DisplayedAtoms>43,"Model view action was not rendered");
            listener.Stop();
        }
        report.Add("Configured LLM route gets known name + current context; model view command produces atom geometry PASS");
        yield return null; yield return null;
        intro.assistant.spoken.Clear();
        var rendererField=typeof(MoleculeExplorationController).GetField("_renderer",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        var beforeFocus=rendererField.GetValue(controller);
        controller.FocusDockingSite();
        double focusDeadline=EditorApplication.timeSinceStartup+10;
        while(ReferenceEquals(rendererField.GetValue(controller),beforeFocus) && EditorApplication.timeSinceStartup<focusDeadline) yield return null;
        Check(!ReferenceEquals(rendererField.GetValue(controller),beforeFocus),"Docking focus did not finish rebuilding");
        Check((string)typeof(MoleculeExplorationController).GetField("_currentView",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(controller)=="atoms","Docking focus changed atom view to ribbon");
        yield return null; yield return null;
        Check(intro.assistant.spoken.Count==0,"Internal docking focus repeated loading/display speech");
        report.Add("Initial recommendation guidance, removed toolbar, in-flight request deduplication and silent docking focus PASS");
        // A clarification must survive the round trip so a short next answer is meaningful to the model.
        string clarification="혈당과 관련된 인슐린, 글루카곤 중 무엇을 볼까요?";
        var beforeChoice=UnityEngine.Object.FindObjectsByType<ExplorationMoleculeRenderer>(FindObjectsSortMode.None)
            .First(r=>r.gameObject.activeInHierarchy && r.HasGeometry);
        string[] followupPayloads=new string[3];
        for(int turn=0;turn<3;turn++)
        {
            var followupTask=listenerRoundTrip(controller.resolverEndpoint,turn==0?
                "{\"action\":\"clarify\",\"choices\":[\"인슐린\",\"글루카곤\"],\"message\":\""+clarification+"\"}":
                turn==1?"{\"action\":\"explain\",\"message\":\"인슐린은 혈당을 조절하는 역할을 해요.\"}":
                "{\"action\":\"load\",\"spec\":{\"pdbId\":\"1TRZ\",\"title\":\"인슐린\",\"chains\":\"A,B\",\"view\":\"ribbon\"}}");
            controller.Submit(turn==0?"혈당과 관련된 것을 보여줘":turn==1?"첫 번째를 왜 추천했어?":"첫 번째 것으로 보여줘");
            double followupDeadline=EditorApplication.timeSinceStartup+12;
            while(!followupTask.IsCompleted && EditorApplication.timeSinceStartup<followupDeadline) yield return null;
            Check(followupTask.IsCompleted && !followupTask.IsFaulted,"Clarification round trip failed");
            followupPayloads[turn]=followupTask.Result;
            yield return null; yield return null;
        }
        Check(followupPayloads[2].Contains("history") && followupPayloads[2].Contains("혈당과 관련된 것을 보여줘") &&
            followupPayloads[2].Contains("인슐린, 글루카곤"),"Clarification history missing from next request");
        Check(followupPayloads[2].Contains("\"pendingChoices\":[\"인슐린\",\"글루카곤\"]"),"Explanation lost ordered choices before selection");
        ExplorationMoleculeRenderer afterChoice=null;
        double choiceDeadline=EditorApplication.timeSinceStartup+35;
        while(EditorApplication.timeSinceStartup<choiceDeadline) {
            afterChoice=UnityEngine.Object.FindObjectsByType<ExplorationMoleculeRenderer>(FindObjectsSortMode.None)
                .FirstOrDefault(r=>r!=beforeChoice && r.gameObject.activeInHierarchy && r.HasGeometry);
            if(afterChoice!=null) break;
            yield return null;
        }
        Check(afterChoice!=null && afterChoice.DisplayedResidues==51,"Ordinal answer did not load and render the new insulin structure");
        report.Add("Function request -> clarification -> discuss recommendation -> ordinal answer -> NEW insulin geometry (51 residues) PASS");
        var assistantObject=new GameObject("Transition assistant");
        intro.assistant=assistantObject.AddComponent<AIAssistantBrain>();
        intro.IsStageTransitioning=true;
        controller.ShowModeSelection();
        Check(!assistantObject.activeSelf,"Assistant visible during return transition");
        Check(UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).Length==0,"Buttons visible during return transition");
        intro.IsStageTransitioning=false; yield return null; yield return null;
        Check(assistantObject.activeSelf,"Assistant not restored after return");
        intro.IsStageTransitioning=true;
        controller.StartExploration();
        Check(!assistantObject.activeSelf && MoleculeExplorationController.Active==null,"Exploration active before arrival");
        Check(UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).Length==0,"Buttons visible during entry transition");
        intro.IsStageTransitioning=false; yield return null; yield return null;
        Check(assistantObject.activeSelf && MoleculeExplorationController.Active==controller,"Exploration not restored after arrival");
        report.Add("Entry and return hide UI and assistant until stage transition ends PASS");
        int returnsBeforeExit=intro.returnedToLab;
        controller.ShowModeSelection(); yield return null;
        Check(MoleculeExplorationController.Active==null,"Exit did not clear routing");
        Check(UnityEngine.Object.FindObjectsByType<ExplorationMoleculeRenderer>(FindObjectsSortMode.None).Length==0,"Exit leaked model");
        Check(intro.returnedToLab==returnsBeforeExit+1,"Leaving exploration did not restore the lab background");
        report.Add("UI choices, case dispatch, text to live geometry, exit cleanup PASS (isolated scene dependencies)");
        report.Add("Exploration entry plays the case-style background transition and the exit restores the lab PASS");
        File.WriteAllLines("smoke-details.txt",report);
        yield return DockingSmoke.Run(camera,controller);
    }

    static Task<string> listenerRoundTrip(string endpoint,string responseJson)
    {
        var uri=new Uri(endpoint);
        var listener=new HttpListener(); listener.Prefixes.Add(uri.GetLeftPart(UriPartial.Authority)+"/"); listener.Start();
        return Task.Run(async()=> {
            try {
                var incoming=await listener.GetContextAsync();
                string payload;
                using(var reader=new StreamReader(incoming.Request.InputStream)) payload=await reader.ReadToEndAsync();
                byte[] bytes=Encoding.UTF8.GetBytes(responseJson);
                incoming.Response.ContentType="application/json"; incoming.Response.ContentLength64=bytes.Length;
                await incoming.Response.OutputStream.WriteAsync(bytes,0,bytes.Length); incoming.Response.Close();
                return payload;
            }
            finally { listener.Stop(); listener.Close(); }
        });
    }
    [Serializable] class LiveReport { public LiveTurn[] turns; }
    [Serializable] class LiveTurn { public string query; public LiveResult result; }
    [Serializable] class LiveResult { public string action; public MoleculeViewSpec spec; }
}

