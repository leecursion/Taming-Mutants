using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>Independent experiment lifecycle: viewing/chatting never cancels a docking job.</summary>
public partial class ExplorationDockingController : MonoBehaviour
{
    public readonly ExplorationDockingSession Session=new ExplorationDockingSession();
    public bool IsBusy => _busy;
    public bool IsPresenting => _effects!=null && _effects.IsPresenting;
    ExplorationDockingEffects _effects;
    MoleculeExplorationController _owner;
    ExplorationMoleculeRenderer _protein;
    ExplorationLigandRenderer _visual;
    List<ExplorationAtom> _atoms;
    string _pdb;
    GameObject _overlay,_panel;
    InputField _candidateInput,_siteInput;
    Text _targetLabel,_candidateLabel,_siteLabel,_statusLabel,_historyLabel;
    Button _loadButton,_dockButton,_cancelButton,_siteButton,_boundButton;
    readonly List<IGrouping<string,ExplorationAtom>> _boundSites=new List<IGrouping<string,ExplorationAtom>>();
    int _boundIndex=-1,_historyIndex=-1,_poseIndex;
    bool _busy;
    string _activeJob;
    UnityWebRequest _request;
    Coroutine _operation;
    DockingTrial _shownTrial;
    GameObject _siteBox;
    Material _siteMaterial;
    public DockingSite DisplaySite => _shownTrial?.site??Session.Site;
    string _status="단백질을 불러온 뒤 후보와 도킹 부위를 선택해 주세요.";

    public void Initialize(MoleculeExplorationController owner)
    {
        _owner=owner;
        if(_overlay==null) BuildUi();
        _overlay.SetActive(true); Refresh();
    }
    public void BindTarget(MoleculeViewSpec spec,List<ExplorationAtom> atoms,string pdb,ExplorationMoleculeRenderer renderer)
    {
        string chains=string.IsNullOrEmpty(spec.chains)?atoms.First(a=>!a.hetero&&a.name=="CA").chain:spec.chains;
        bool changed=Session.PdbId!=spec.pdbId || Session.Chains!=chains;
        if(changed)
        {
            ResetPicker();
            CancelOperation(false); Session.Bind(spec.pdbId,chains);
            _shownTrial=null; _historyIndex=-1; _boundIndex=-1; _poseIndex=0;
            _boundSites.Clear();
            _boundSites.AddRange(atoms.Where(a=>a.hetero && chains.Split(',').Contains(a.chain)).GroupBy(a=>a.residueKey).Where(g=>g.Count()>=3).Take(40));
            if(_siteInput!=null) _siteInput.text="";
            if(_candidateInput!=null) _candidateInput.text="";
            _status="후보 분자를 불러오고 결합 분자 주변 또는 잔기 범위를 선택해 주세요.";
        }
        _pdb=pdb; _protein=renderer; _atoms=atoms;
        if(_targetLabel!=null) _targetLabel.text=spec.title+" · "+spec.pdbId+" / 체인 "+chains;
        Redraw(); Refresh();
        if(IsOpen) _owner.FocusDockingCamera();
    }
    public void CloseSession()
    {
        CancelOperation(false); ClearVisual(); Session.Reset(); _protein=null; _pdb=null; _atoms=null; _shownTrial=null;
        _boundSites.Clear(); _boundIndex=_historyIndex=-1; _poseIndex=0;
        ResetPicker();
        _status="단백질을 불러온 뒤 후보와 도킹 부위를 선택해 주세요.";
        if(_candidateInput!=null) _candidateInput.text="";
        if(_siteInput!=null) _siteInput.text="";
        if(_targetLabel!=null) _targetLabel.text="단백질을 먼저 불러와 주세요";
        Refresh();
        if(_overlay!=null) _overlay.SetActive(false);
    }
    public void Open() { if(_panel!=null) _panel.SetActive(true); UpdateSiteVisibility(); }
    void UpdateSiteVisibility()
    {
        bool selecting=IsOpen && !_busy && _shownTrial==null &&
            (_picker==Picker.Site || (_advanced!=null && _advanced.activeSelf));
        if(_siteBox!=null) _siteBox.SetActive(selecting);
        if(_effects!=null) _effects.SetRegionVisible(selecting || (IsOpen && _busy && _activeJob!=null));
    }
    public void LoadCandidate(string query)
    {
        Open();
        if(_busy) { Status("현재 요청이 끝나거나 취소된 뒤 다른 후보를 불러와 주세요."); return; }
        if(Session.PdbId==null) { Status("먼저 표적 단백질을 불러와 주세요."); return; }
        if(string.IsNullOrWhiteSpace(query)) { Status("후보 분자 이름 또는 CID를 입력해 주세요."); return; }
        if(_candidateInput!=null) _candidateInput.text=query;
        _operation=StartCoroutine(Guarded(LoadRoutine(query.Trim())));
    }
    IEnumerator LoadRoutine(string query)
    {
        _loadingCandidate=true; _candidateUnresolved=true; _canRetryLoad=false; _retryQuery=query; _stereoChoices=Array.Empty<DockingCandidateChoice>();
        _busy=true; Refresh(); Status("후보 분자의 실제 구조를 불러오는 중이에요…");
        var payload=new Payload { op="ligand",sessionId=Session.Id,query=query };
        if(query.StartsWith("file:",StringComparison.OrdinalIgnoreCase))
        {
            string path=query.Substring(5).Trim().Trim('"');
            if(!string.Equals(Path.GetExtension(path),".sdf",StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || new FileInfo(path).Length>200000)
                throw new InvalidOperationException("200 KB 이하의 단일 분자 SDF 파일 경로를 입력해 주세요.");
            payload.sdf=File.ReadAllText(path); payload.query="";
        }
        DockingReply reply=null;
        yield return Send(payload,r=>reply=r);
        if(reply.status=="needs_selection")
        {
            _busy=_loadingCandidate=false; _stereoChoices=reply.choices??Array.Empty<DockingCandidateChoice>();
            ShowRecovery(reply.error); yield break;
        }
        if(!string.IsNullOrEmpty(payload.sdf) && reply.ligand!=null)
            reply.ligand.title=Path.GetFileNameWithoutExtension(query.Substring(5).Trim().Trim('"'))+" · SDF";
        Session.SetCandidate(reply.ligand); // Failed loading keeps the previous candidate and trials.
        CandidateSelected();
        Status(Session.Candidate.title+"을 불러왔어요. 도킹 부위를 확인한 뒤 실행해 주세요.");
        _busy=_loadingCandidate=false; Refresh();
    }
    public void RunDocking()
    {
        Open();
        if(_busy) { Status("실험이 진행 중이에요. 완료 후 다음 후보를 테스트할 수 있습니다."); return; }
        if(_candidateUnresolved || _siteUnresolved) { ShowRecovery("후보 또는 부위 선택이 완료되지 않았어요. 다시 선택하거나 이전 선택으로 계속해 주세요."); return; }
        _operation=StartCoroutine(Guarded(DockRoutine()));
    }
    IEnumerator DockRoutine()
    {
        DockingTrial trial=Session.Begin();
        if(string.IsNullOrEmpty(_pdb)) throw new InvalidOperationException("원본 단백질 좌표를 다시 불러와 주세요.");
        _busy=true; _activeJob=trial.jobId; Refresh();
        _shownTrial=null; Redraw();
        Status("도킹 계산을 요청했어요. 완료 후 같은 단백질에서 다른 후보도 테스트할 수 있어요.");
        var payload=new Payload { op="start",sessionId=Session.Id,jobId=trial.jobId,pdb=_pdb,chains=trial.chains,
            sdf=trial.ligand.sdf,center=trial.site.center,size=trial.site.size };
        DockingReply reply=null;
        yield return Send(payload,r=>reply=r);
        float deadline=Time.realtimeSinceStartup+600;
        while(reply.status=="queued" || reply.status=="running")
        {
            if(Time.realtimeSinceStartup>deadline) throw new InvalidOperationException("계산 대기 시간이 초과됐어요. 다시 실행해 주세요.");
            _status=reply.status=="queued"?"계산 대기 중…":"결합 자세를 탐색하는 중…"; Refresh();
            yield return new WaitForSecondsRealtime(2f);
            yield return Send(new Payload { op="poll",sessionId=Session.Id,jobId=trial.jobId },r=>reply=r);
        }
        if(reply.status!="completed" && reply.status!="failed") throw new InvalidOperationException("실험이 취소되었거나 결과를 받지 못했습니다.");
        Session.Complete(trial,reply);
        _activeJob=null; _busy=false; _historyIndex=Session.Trials.Count-1;
        SaveTrial(trial);
        if(reply.status=="completed")
        {
            _shownTrial=trial; _poseIndex=0; _picker=Picker.History; Redraw(true);
            Status(trial.ligand.title+" 도킹 완료. 예측 점수 "+reply.poses[0].score.ToString("F2",CultureInfo.InvariantCulture)+
                (reply.poses[0].score>=0?" kcal/mol. 이 조건에서 유리한 예측 점수가 나오지 않았어요. 결합 불가를 확정하는 결과는 아닙니다.":
                " kcal/mol. 다른 후보를 불러와 같은 부위에서 이어서 실험할 수 있어요."));
        }
        else { Redraw(); ShowRecovery((reply.error??"도킹 계산에 실패했습니다.")+" 계산 실패는 결합하지 않는다는 판정이 아닙니다. 이전 기록은 유지됩니다."); }
        Refresh();
    }
    public void SelectResidues(string selection)
    {
        if(_busy) { Status("실험이 끝난 뒤 부위를 바꿔 주세요."); return; }
        try
        {
            var match=Regex.Match(selection.Trim(),@"^([A-Za-z0-9]):(-?\d+)(?:-(-?\d+))?$");
            if(!match.Success || _atoms==null) throw new InvalidOperationException("체인과 잔기 번호를 A:123-130 형식으로 입력해 주세요.");
            int start=int.Parse(match.Groups[2].Value),end=match.Groups[3].Success?int.Parse(match.Groups[3].Value):start;
            string chain=match.Groups[1].Value;
            if(start>end || !Session.Chains.Split(',').Contains(chain)) throw new InvalidOperationException("현재 단백질 체인 안의 잔기 범위를 지정해 주세요.");
            var region=_atoms.Where(a=>!a.hetero&&a.chain==chain&&a.number>=start&&a.number<=end).ToArray();
            if(!region.Any(a=>a.number==start)||!region.Any(a=>a.number==end)) throw new InvalidOperationException("지정한 시작/끝 잔기가 현재 구조에 없습니다.");
            Session.Site=DockingSite.FromAtoms(selection,region); _siteUnresolved=false; _shownTrial=null; Redraw(); Refresh(); _owner.FocusDockingSite();
            Status("도킹 부위를 "+selection+" 주변으로 지정했어요.");
        }
        catch(Exception e) { _siteUnresolved=true; ShowRecovery(e.Message+" 기존 부위와 실험 기록은 유지됩니다."); }
    }
    void SelectBoundSite()
    {
        if(_busy || _boundSites.Count==0) return;
        try
        {
            int index=(_boundIndex+1)%_boundSites.Count;
            var group=_boundSites[index]; var first=group.First();
            Session.Site=DockingSite.FromAtoms(first.residue+" · "+first.chain+":"+first.number+" ("+(index+1)+"/"+_boundSites.Count+")",group);
            _boundIndex=index;
            _siteUnresolved=false;
            _shownTrial=null; Redraw(); Refresh(); _owner.FocusDockingSite();
            Status("기존 결합 분자 "+Session.Site.label+" 주변을 선택했어요. 계산할 때 기존 결합 분자는 제외합니다.");
        }
        catch(Exception e) { _siteUnresolved=true; ShowRecovery(e.Message+" ‘직접 입력 / 고급’에서 더 좁은 잔기 범위를 지정할 수 있어요."); }
    }
    void ShowHistory(int direction)
    {
        if(_busy || Session.Trials.Count==0) return;
        _historyIndex=(_historyIndex+direction+Session.Trials.Count)%Session.Trials.Count;
        var trial=Session.Trials[_historyIndex]; _poseIndex=0;
        if(trial.result.status=="completed") { _shownTrial=trial; Redraw(); }
        else { _shownTrial=null; Redraw(); ShowRecovery(trial.result.error+" 계산 실패는 결합 불가 판정이 아닙니다."); }
        _owner.FocusDockingSite();
        Status(trial.ligand.title+" · "+trial.site.label+" · "+(trial.result.status=="completed"?"완료된 실험을 보고 있어요. ‘다른 후보 실험’으로 이어갈 수 있어요.":trial.result.error)); Refresh();
    }
    void NextPose()
    {
        if(_shownTrial?.result?.poses==null) return;
        _poseIndex=(_poseIndex+1)%_shownTrial.result.poses.Length; Redraw(); Refresh();
    }
    void Redraw(bool animatePose=false)
    {
        ClearVisual();
        if(_protein==null) return;
        DrawSite();
        var effectsRoot=new GameObject("Docking presentation"); effectsRoot.transform.SetParent(_protein.transform,false);
        _effects=effectsRoot.AddComponent<ExplorationDockingEffects>();
        if(DisplaySite!=null) _effects.ShowRegion(_protein.PdbToLocal(new Vector3(DisplaySite.center[0],DisplaySite.center[1],DisplaySite.center[2])),
            DisplaySite.size.Max()*.05f,_activeJob!=null&&_busy);
        UpdateSiteVisibility();
        DockingAtom[] atoms; DockingBond[] bonds;
        Func<Vector3,Vector3> convert;
        if(_shownTrial!=null)
        {
            var pose=_shownTrial.result.poses[_poseIndex]; atoms=pose.atoms; bonds=pose.bonds; convert=_protein.PdbToLocal;
        }
        else
        {
            var ligand=Session.Candidate; if(ligand==null) return;
            atoms=ligand.atoms; bonds=ligand.bonds;
            // Candidate preview sits beside the protein; only computed poses enter the pocket.
            Vector3 center=atoms.Aggregate(Vector3.zero,(sum,a)=>sum+a.Position)/atoms.Length;
            float radius=atoms.Max(a=>(a.Position-center).magnitude)*.1f;
            float extent=_protein.DisplayBounds.extents.x;
            convert=p=>(p-center)*.1f+new Vector3(extent+radius+.25f,0,0);
        }
        var root=new GameObject(_shownTrial==null?"Candidate preview (not docked)":"Computed docking pose");
        root.transform.SetParent(_protein.transform,false);
        _visual=root.AddComponent<ExplorationLigandRenderer>();
        _visual.Build(atoms,bonds,convert,_shownTrial==null?new Color(1,.65f,.25f):new Color(.3f,1,.6f));
        if(_shownTrial!=null)
        {
            var center=atoms.Aggregate(Vector3.zero,(sum,a)=>sum+a.Position)/atoms.Length;
            var from=new Vector3(_protein.DisplayBounds.extents.x+1,0,0)-_protein.PdbToLocal(center);
            _effects.ShowPose(_visual.transform,animatePose?from:Vector3.zero,animatePose);
            _effects.ShowContacts(_atoms.Where(a=>!a.hetero && Session.Chains.Split(',').Contains(a.chain)),atoms,_protein.PdbToLocal);
        }
        else _effects.ShowCandidate(_visual.transform);
    }
    void DrawSite()
    {
        var site=DisplaySite; if(site==null) return;
        _siteBox=new GameObject("Docking search region",typeof(LineRenderer)); _siteBox.transform.SetParent(_protein.transform,false);
        _siteMaterial=new Material(RuntimeMaterials.Solid); _siteMaterial.color=new Color(.2f,.9f,1);
        if(_siteMaterial.HasProperty("_BaseColor")) _siteMaterial.SetColor("_BaseColor",new Color(.2f,.9f,1));
        var line=_siteBox.GetComponent<LineRenderer>(); line.sharedMaterial=_siteMaterial; line.useWorldSpace=false;
        line.startWidth=line.endWidth=.012f;
        var center=new Vector3(site.center[0],site.center[1],site.center[2]); var size=new Vector3(site.size[0],site.size[1],site.size[2]);
        int[] order={0,1,3,2,0,4,5,1,5,7,3,7,6,2,6,4}; line.positionCount=order.Length;
        for(int i=0;i<order.Length;i++)
        {
            int corner=order[i]; var offset=new Vector3((corner&1)==0?-.5f:.5f,(corner&2)==0?-.5f:.5f,(corner&4)==0?-.5f:.5f);
            line.SetPosition(i,_protein.PdbToLocal(center+Vector3.Scale(size,offset)));
        }
        UpdateSiteVisibility();
    }
    void ClearVisual()
    {
        if(_effects!=null) { _effects.gameObject.SetActive(false); Destroy(_effects.gameObject); } _effects=null;
        if(_visual!=null) { _visual.gameObject.SetActive(false); Destroy(_visual.gameObject); } _visual=null;
        if(_siteBox!=null) { _siteBox.SetActive(false); Destroy(_siteBox); } _siteBox=null;
        if(_siteMaterial!=null) Destroy(_siteMaterial); _siteMaterial=null;
    }
    public string DiscussionContext()
    {
        if(Session.PdbId==null) return "";
        var value=new StringBuilder("도킹 실험 문맥(실제 계산과 후보 미리보기를 구별): ");
        value.Append("현재 후보=").Append(Session.Candidate?.title??"없음").Append("; SMILES=").Append(Session.Candidate?.smiles??"");
        value.Append("; 다음 실험 부위=").Append(Session.Site?.label??"미지정");
        value.Append("; 상태=").Append(_busy?"계산/로딩 중":_shownTrial!=null?"계산 자세 표시":"미리보기, 결합 결과 없음");
        foreach(var trial in Session.Trials.Skip(Math.Max(0,Session.Trials.Count-5)))
        {
            value.Append("\n후보=").Append(trial.ligand.title).Append("; 부위=").Append(trial.site.label).Append("; 상태=").Append(trial.result.status);
            if(trial.result.status=="completed") value.Append("; Vina 점수(kcal/mol)=").Append(trial.result.poses[0].score.ToString("F2",CultureInfo.InvariantCulture))
                .Append("; 동일조건 비교키=").Append(trial.result.comparisonKey);
            else value.Append("; 오류=").Append(trial.result.error);
        }
        if(_shownTrial!=null) value.Append("\n현재 표시=").Append(_shownTrial.ligand.title).Append("; 자세 ").Append(_poseIndex+1)
            .Append("; 점수=").Append(_shownTrial.result.poses[_poseIndex].score.ToString("F2",CultureInfo.InvariantCulture))
            .Append("; 원본 단백질에서 중원자 거리 4 Å 이내의 잔기(수소결합 판정 아님)=").Append(_shownTrial.result.poses[_poseIndex].contacts);
        value.Append("\n고정 단백질·비공유결합·물/이온/기존 리간드 제외. 점수는 실측 결합력/약효가 아님. 비교키가 같은 실험만 같은 조건으로 비교.");
        return value.ToString();
    }
    void SaveTrial(DockingTrial trial)
    {
        try
        {
            string dir=Path.Combine(Application.persistentDataPath,"DockingExperiments",Session.Id);
            Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir,trial.jobId+".json"),JsonUtility.ToJson(trial,true));
        }
        catch(Exception) { Debug.LogWarning("[Docking] 실험 결과를 디스크에 저장하지 못했습니다. 이번 세션 기록은 유지됩니다."); }
    }
    public void CancelOperation() { CancelOperation(true); }
    void CancelOperation(bool redraw)
    {
        string job=_activeJob,session=Session.Id;
        if(_request!=null) { _request.Abort(); _request=null; }
        if(_operation!=null) StopCoroutine(_operation); _operation=null;
        _busy=false; _loadingCandidate=false; _activeJob=null;
        if(job!=null && _owner!=null && isActiveAndEnabled) StartCoroutine(CancelRemote(job,session));
        if(redraw) { Redraw(); Refresh(); }
    }
    IEnumerator CancelRemote(string job,string session)
    {
        _owner.GetDockingConnection(out string endpoint,out string token);
        if(string.IsNullOrEmpty(endpoint)) yield break;
        using(var req=MakeRequest(endpoint,token,new Payload { op="cancel",sessionId=session,jobId=job }))
        { req.timeout=10; yield return req.SendWebRequest(); }
    }
    IEnumerator Send(Payload payload,Action<DockingReply> receive)
    {
        _owner.GetDockingConnection(out string endpoint,out string token);
        if(string.IsNullOrWhiteSpace(endpoint)) throw new InvalidOperationException("도킹 서버 연결이 필요해요. dockingEndpoint 또는 기존 프록시 서버를 설정해 주세요.");
        // Start/poll are idempotent; retry a transport failure using the same job ID.
        for(int attempt=0;attempt<2;attempt++)
        {
            using(var req=MakeRequest(endpoint,token,payload))
            {
                _request=req; yield return req.SendWebRequest(); if(_request==req) _request=null;
                if(req.result==UnityWebRequest.Result.ConnectionError && attempt==0 && payload.op!="ligand") { yield return new WaitForSecondsRealtime(1); continue; }
                DockingReply reply=null;
                try { reply=JsonUtility.FromJson<DockingReply>(req.downloadHandler.text); } catch { }
                if(req.result!=UnityWebRequest.Result.Success)
                {
                    _canRetryLoad=req.result==UnityWebRequest.Result.ConnectionError || req.responseCode>=500;
                    throw new InvalidOperationException(reply?.error??"도킹 서버와 연결하지 못했습니다.");
                }
                if(reply==null) throw new InvalidOperationException("도킹 서버 응답이 비어 있습니다.");
                // Failed jobs are valid terminal responses and remain in experiment history.
                if(!string.IsNullOrEmpty(reply.error)&&reply.status!="failed"&&reply.status!="needs_selection") throw new InvalidOperationException(reply.error);
                receive(reply); yield break;
            }
        }
    }
    static UnityWebRequest MakeRequest(string endpoint,string token,Payload payload)
    {
        var req=new UnityWebRequest(endpoint,"POST") { timeout=60,
            uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload))),downloadHandler=new DownloadHandlerBuffer() };
        req.SetRequestHeader("Content-Type","application/json");
        if(!string.IsNullOrEmpty(token)) req.SetRequestHeader("X-App-Token",token);
        return req;
    }
    IEnumerator Guarded(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>(); stack.Push(routine);
        try
        {
            while(stack.Count>0)
            {
                bool moved=false; object next=null; Exception error=null;
                try { moved=stack.Peek().MoveNext(); if(moved) next=stack.Peek().Current; } catch(Exception e) { error=e; }
                if(error!=null)
                {
                    string job=_activeJob; _activeJob=null; _request=null; _busy=false;
                    if(job!=null) StartCoroutine(CancelRemote(job,Session.Id));
                    Redraw();
                    string message=error.Message;
                    if(_loadingCandidate) message+=" 이전 후보와 실험 기록은 유지됩니다. 다시 선택하거나 이전 후보로 계속할 수 있어요.";
                    _loadingCandidate=false; ShowRecovery(message); yield break;
                }
                if(!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if(next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
        }
        finally { while(stack.Count>0) (stack.Pop() as IDisposable)?.Dispose(); }
    }
    void Status(string message) { _status=message; Refresh(); if(_owner!=null) _owner.ReportDockingStatus(message); }
    void Refresh()
    {
        if(_panel==null) return;
        UpdateSiteVisibility();
        _candidateLabel.text=Session.Candidate==null?"후보 없음":Session.Candidate.title+"\n"+Session.Candidate.source;
        _siteLabel.text=Session.Site==null?"도킹 부위 미지정":Session.Site.label+" 주변 · "+string.Join(" × ",Session.Site.size.Select(x=>x.ToString("F1")))+" Å";
        _statusLabel.text=_status;
        _loadButton.interactable=!_busy && Session.PdbId!=null;
        _dockButton.interactable=!_busy && !_candidateUnresolved && !_siteUnresolved && Session.Candidate!=null && Session.Site!=null;
        _cancelButton.interactable=_busy;
        _siteButton.interactable=!_busy && Session.PdbId!=null;
        _boundButton.interactable=!_busy && _boundSites.Count>0;
        _candidateInput.interactable=!_busy; _siteInput.interactable=!_busy;
        string history="실험 기록 "+Session.Trials.Count+"개 (최근 20개)";
        if(_historyIndex>=0 && _historyIndex<Session.Trials.Count) history+=" · "+(_historyIndex+1)+"번";
        if(_shownTrial!=null) history+="\n"+_shownTrial.ligand.title+" · 자세 "+(_poseIndex+1)+"/"+_shownTrial.result.poses.Length+
            " · "+_shownTrial.result.poses[_poseIndex].score.ToString("F2",CultureInfo.InvariantCulture)+" kcal/mol";
        _historyLabel.text=history;
        RefreshGuidedUi();
    }
    void BuildUi() { BuildGuidedUi(); }
    static void Rect(GameObject go,float x,float y,float width,float height)
    {
        var rect=(RectTransform)go.transform; rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);
        rect.anchoredPosition=new Vector2(x,-y); rect.sizeDelta=new Vector2(width,height);
    }
    static Text Label(Transform parent,string value,float x,float y,float w,float h,int size)
    {
        var go=new GameObject("Label",typeof(RectTransform),typeof(Text)); go.transform.SetParent(parent,false); Rect(go,x,y,w,h);
        var text=go.GetComponent<Text>(); text.text=value; text.font=HoloFont.Resolve(); text.fontSize=size;
        text.color=new Color(.85f,.96f,1); text.alignment=TextAnchor.MiddleLeft; text.supportRichText=false; text.raycastTarget=false;
        return text;
    }
    static Button ButtonAt(Transform parent,string title,float x,float y,float w,float h,UnityEngine.Events.UnityAction action)
    {
        var go=new GameObject(title,typeof(RectTransform),typeof(Image),typeof(Button)); go.transform.SetParent(parent,false); Rect(go,x,y,w,h);
        var graphic=go.GetComponent<Image>(); graphic.sprite=HoloSpriteFactory.Panel(); graphic.type=Image.Type.Sliced; graphic.color=new Color(.055f,.22f,.28f,.97f);
        Label(go.transform,title,4,0,w-8,h,20).alignment=TextAnchor.MiddleCenter;
        var button=go.GetComponent<Button>(); button.targetGraphic=graphic; button.onClick.AddListener(action); return button;
    }
    static InputField Input(Transform parent,string placeholder,float x,float y,float w)
    {
        var go=new GameObject("Input",typeof(RectTransform),typeof(Image),typeof(InputField),typeof(RectMask2D)); go.transform.SetParent(parent,false); Rect(go,x,y,w,44);
        go.GetComponent<Image>().color=new Color(.015f,.035f,.05f);
        var input=go.GetComponent<InputField>(); input.targetGraphic=go.GetComponent<Image>(); input.characterLimit=2000;
        input.textComponent=Label(go.transform,"",8,0,w-16,44,19); var hint=Label(go.transform,placeholder,8,0,w-16,44,18);
        hint.color=new Color(.5f,.65f,.7f); input.placeholder=hint; return input;
    }
    void OnDestroy() { CancelOperation(false); ClearVisual(); if(_overlay!=null) Destroy(_overlay); }
    [Serializable] class Payload
    {
        public string op,sessionId,query,sdf,jobId,pdb,chains;
        public float[] center,size;
    }
}
