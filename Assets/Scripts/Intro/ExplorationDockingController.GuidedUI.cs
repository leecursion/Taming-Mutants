using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public partial class ExplorationDockingController
{
    enum Picker { Candidate, Site, History, Recovery }
    string _recoveryMessage,_retryQuery;
    DockingCandidateChoice[] _stereoChoices=Array.Empty<DockingCandidateChoice>();
    bool _candidateUnresolved,_siteUnresolved,_loadingCandidate,_canRetryLoad;
    void EditInput()
    {
        Open(); _advanced.SetActive(true); _cardScroll.gameObject.SetActive(false);
        UpdateSiteVisibility();
    }
    void ShowRecovery(string message)
    {
        _recoveryMessage=message; OpenPicker(Picker.Recovery); Status(message);
    }
    Picker _picker;
    RectTransform _cards;
    GameObject _advanced;
    ScrollRect _cardScroll;
    Text _stepLabel;
    Button _recommendButton,_anotherButton,_compareButton;
    Button _candidateTab,_siteTab,_historyTab,_poseButton;
    readonly Vector3[] _panelCorners=new Vector3[4];
    public float SearchViewportY(float halfHeight)
    {
        if(!IsOpen) return .15f;
        ((RectTransform)_panel.transform).GetWorldCorners(_panelCorners);
        var canvas=_overlay.GetComponent<Canvas>();
        var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        float bottom=RectTransformUtility.WorldToScreenPoint(camera,_panelCorners[0]).y/Mathf.Max(1,Screen.height);
        return Mathf.Max(halfHeight+.02f,Mathf.Min(.15f,bottom-halfHeight-.015f));
    }
    readonly List<DockingLigand> _knownCandidates=new List<DockingLigand>();
    string[] _candidateSuggestions=Array.Empty<string>();
    bool _suggesting;
    public bool IsOpen => _panel!=null && _panel.activeInHierarchy;

    public void ShowCandidateChoices(string[] choices)
    {
        _suggesting=false;
        _candidateSuggestions=(choices??Array.Empty<string>()).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().Take(3).ToArray();
        OpenPicker(Picker.Candidate);
    }
    public void EndSuggestionRequest() { _suggesting=false; Refresh(); }
    public void HidePanel() { if(_panel!=null) _panel.SetActive(false); UpdateSiteVisibility(); }
    public void NextCandidate()
    {
        if(_busy) return;
        _shownTrial=null; _poseIndex=0; OpenPicker(Picker.Candidate); Redraw();
        Status("단백질과 도킹 부위는 그대로예요. 다른 후보 카드를 고르거나 원하는 분자를 말해 주세요.");
    }
    void OpenPicker(Picker picker)
    {
        Open(); _picker=picker;
        if(_cards!=null) _cards.anchoredPosition=Vector2.zero;
        if(_advanced!=null) _advanced.SetActive(false);
        if(_cardScroll!=null) _cardScroll.gameObject.SetActive(true);
        Refresh();
    }
    void RememberCandidate(DockingLigand candidate)
    {
        if(!_knownCandidates.Any(x=>x.id==candidate.id)) _knownCandidates.Add(candidate);
        if(_knownCandidates.Count>12) _knownCandidates.RemoveAt(0);
    }
    void ChooseKnownCandidate(DockingLigand candidate)
    {
        if(_busy) return;
        Session.SetCandidate(candidate); CandidateSelected();
    }
    void CandidateSelected()
    {
        _candidateUnresolved=false; _stereoChoices=Array.Empty<DockingCandidateChoice>();
        RememberCandidate(Session.Candidate); _shownTrial=null; _poseIndex=0;
        _picker=Session.Site==null?Picker.Site:Picker.Candidate;
        if(_advanced!=null) _advanced.SetActive(false);
        if(_cardScroll!=null) _cardScroll.gameObject.SetActive(true);
        Redraw(); if(Session.Site!=null) _owner.FocusDockingSite(); Refresh();
    }
    void ResetPicker()
    {
        _candidateUnresolved=_siteUnresolved=_loadingCandidate=false; _retryQuery=_recoveryMessage=null;
        _stereoChoices=Array.Empty<DockingCandidateChoice>();
        _knownCandidates.Clear(); _candidateSuggestions=Array.Empty<string>(); _suggesting=false; _picker=Picker.Candidate;
        if(_advanced!=null) _advanced.SetActive(false);
        if(_cardScroll!=null) _cardScroll.gameObject.SetActive(true);
    }
    void RequestCandidates()
    {
        if(_busy || _suggesting || Session.PdbId==null) return;
        _suggesting=true; OpenPicker(Picker.Candidate);
        _owner.Submit("현재 단백질의 도킹 실험에서 비교할 소분자 후보를 3개 추천해줘. 이미 시험한 후보가 있다면 다른 후보를 제안해줘.");
    }
    void SelectSite(int index)
    {
        if(_busy || index<0 || index>=_boundSites.Count) return;
        int previousIndex=_boundIndex; var previousSite=Session.Site;
        _boundIndex=index-1; SelectBoundSite();
        if(ReferenceEquals(previousSite,Session.Site)) _boundIndex=previousIndex;
        Refresh();
    }
    void SelectTrial(int index)
    {
        if(_busy || index<0 || index>=Session.Trials.Count) return;
        _historyIndex=index; ShowHistory(0);
    }
    void CompareTrials()
    {
        if(_busy || Session.Trials.Count==0) return;
        _owner.Submit("현재 단백질에서 실행한 후보들의 실제 도킹 결과를 비교해줘. 동일 조건인 실험끼리 점수와 결합 자세 차이를 설명해줘.");
    }
    void RefreshGuidedUi()
    {
        if(_cards==null) return;
        _stepLabel.text=Session.PdbId==null?"먼저 단백질을 선택해 주세요":_busy?"계산 진행 중 · 후보와 부위 유지":
            _candidateUnresolved?"후보 선택을 완료해 주세요":_siteUnresolved?"도킹 부위를 다시 확인해 주세요":
            _shownTrial!=null?"결과 확인 → 다른 후보로 반복 실험":Session.Candidate==null?"1  후보 선택":Session.Site==null?"2  도킹 부위 선택":"3  준비 완료 · 도킹 실행";
        _recommendButton.interactable=!_busy && !_suggesting && Session.PdbId!=null;
        _anotherButton.interactable=!_busy && Session.PdbId!=null;
        _compareButton.interactable=!_busy && Session.Trials.Count>0;
        _poseButton.gameObject.SetActive(_shownTrial!=null);
        _candidateTab.GetComponent<Image>().color=_picker==Picker.Candidate?new Color(.08f,.36f,.36f):new Color(.04f,.12f,.17f);
        _siteTab.GetComponent<Image>().color=_picker==Picker.Site?new Color(.08f,.36f,.36f):new Color(.04f,.12f,.17f);
        _historyTab.GetComponent<Image>().color=_picker==Picker.History?new Color(.08f,.36f,.36f):new Color(.04f,.12f,.17f);
        foreach(Transform child in _cards) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        float y=0;
        Action<string,string,UnityEngine.Events.UnityAction,bool> card=(name,description,action,selected)=>
        {
            var button=ButtonAt(_cards,name,0,y,474,68,action);
            button.gameObject.name=name; button.interactable=!_busy;
            var label=button.GetComponentInChildren<Text>(); label.fontSize=19; label.alignment=TextAnchor.MiddleLeft;
            Rect(label.gameObject,12,5,450,28);
            Label(button.transform,description,12,34,450,28,16).color=new Color(.58f,.75f,.79f);
            button.GetComponent<Image>().color=selected?new Color(.055f,.29f,.28f,.98f):new Color(.035f,.105f,.145f,.98f);
            y+=76;
        };
        if(_picker==Picker.Recovery)
        {
            var detail=Label(_cards,_recoveryMessage??"다시 선택해 주세요.",0,0,474,140,19);
            detail.resizeTextForBestFit=true; detail.resizeTextMinSize=15; detail.resizeTextMaxSize=19; y=148;
            foreach(var option in _stereoChoices)
            {
                var choice=option;
                card(choice.title,choice.query,()=>LoadCandidate(choice.query),false);
            }
            if(_candidateUnresolved && _canRetryLoad && _stereoChoices.Length==0 && !string.IsNullOrEmpty(_retryQuery))
                card("불러오기 다시 시도",_retryQuery,()=>LoadCandidate(_retryQuery),false);
            if(_candidateUnresolved || _siteUnresolved)
                card("입력 수정","분자 식별자 또는 체인·잔기 범위 직접 지정",EditInput,false);
            if(_candidateUnresolved && Session.Candidate!=null)
                card("이전 후보로 계속",Session.Candidate.title,()=>{ CandidateSelected(); Status("이전 후보를 다시 선택했어요."); },false);
            if(_siteUnresolved && Session.Site!=null)
                card("이전 부위로 계속",Session.Site.label,()=>{ _siteUnresolved=false; OpenPicker(Picker.Site); },false);
            card("다른 후보 선택","후보 목록으로 돌아가기",()=>OpenPicker(Picker.Candidate),false);
            card("부위 다시 선택","기존 결합 분자 주변 또는 직접 입력",()=>OpenPicker(Picker.Site),false);
        }
        else if(_picker==Picker.Site)
        {
            for(int i=0;i<_boundSites.Count;i++)
            {
                int index=i; var first=_boundSites[i].First();
                card(first.residue+" 주변 · "+first.chain+":"+first.number,"구조에서 확인된 결합 분자 주변",()=>SelectSite(index),_boundIndex==i&&Session.Site!=null);
            }
            if(y==0) Label(_cards,"기존 결합 분자가 없어요. 원하는 부위를 말하거나\n직접 입력에서 체인·잔기 범위를 지정해 주세요.",0,0,474,110,20);
        }
        else if(_picker==Picker.History)
        {
            for(int i=Session.Trials.Count-1;i>=0;i--)
            {
                int index=i; var trial=Session.Trials[i];
                string score=trial.result.status=="completed"?trial.result.poses[0].score.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" kcal/mol":"계산 실패";
                card((i+1)+". "+trial.ligand.title,trial.site.label+" · "+score,()=>SelectTrial(index),_shownTrial==trial);
            }
            if(y==0) Label(_cards,"완료된 실험이 여기에 쌓입니다.\n후보를 바꿔도 이전 결과는 유지됩니다.",0,0,474,100,20);
        }
        else
        {
            foreach(string suggestion in _candidateSuggestions)
            {
                string query=suggestion;
                card(query,"AI 제안 · 선택하면 실제 구조 조회",()=>LoadCandidate(query),false);
            }
            foreach(var ligand in _knownCandidates.AsEnumerable().Reverse())
            {
                var candidate=ligand;
                card(candidate.title,"불러온 후보 · 다시 선택",()=>ChooseKnownCandidate(candidate),Session.Candidate==candidate);
            }
            foreach(var group in _boundSites.GroupBy(g=>g.First().residue))
            {
                string code=group.Key;
                card(code+" 불러오기","현재 단백질에 포함된 리간드 · 화학 구조 조회",()=>LoadCandidate("CCD:"+code),false);
            }
            if(y==0) Label(_cards,_suggesting?"비교할 후보를 찾고 있어요…":"‘후보 추천’으로 선택 카드를 받거나\n마이크로 원하는 분자를 말해 주세요.",0,0,474,100,20);
        }
        _cards.sizeDelta=new Vector2(474,Mathf.Max(180,y));
    }
    void BuildGuidedUi()
    {
        _overlay=new GameObject("Exploration docking overlay",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        _overlay.transform.SetParent(transform,false);
        _overlay.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay; _overlay.GetComponent<Canvas>().sortingOrder=55;
        var scaler=_overlay.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080); scaler.matchWidthOrHeight=.5f;
        var controls=new GameObject("Docking controls",typeof(RectTransform),typeof(ScreenSafePanel));
        controls.transform.SetParent(_overlay.transform,false); Rect(controls,28,24,520,830);
        controls.GetComponent<ScreenSafePanel>().bottomReserveFraction=.15f;
        ButtonAt(controls.transform,"도킹 실험",0,0,180,48,()=>{ _panel.SetActive(!_panel.activeSelf); UpdateSiteVisibility(); if(IsOpen) _owner.FocusDockingCamera(); });
        _panel=new GameObject("Docking experiment panel",typeof(RectTransform),typeof(Image));
        _panel.transform.SetParent(controls.transform,false); Rect(_panel,0,62,520,768);
        _panel.GetComponent<Image>().sprite=HoloSpriteFactory.Panel(); _panel.GetComponent<Image>().type=Image.Type.Sliced;
        _panel.GetComponent<Image>().color=new Color(.02f,.055f,.085f,.97f);
        _targetLabel=Label(_panel.transform,"단백질을 먼저 불러와 주세요",18,12,484,46,21);
        _stepLabel=Label(_panel.transform,"",18,64,484,26,21); _stepLabel.color=new Color(.35f,1,.8f);
        _candidateTab=ButtonAt(_panel.transform,"후보 선택",18,100,150,38,()=>OpenPicker(Picker.Candidate));
        _siteTab=ButtonAt(_panel.transform,"부위 선택",184,100,150,38,()=>OpenPicker(Picker.Site));
        _historyTab=ButtonAt(_panel.transform,"실험 기록",350,100,152,38,()=>OpenPicker(Picker.History));
        var viewport=new GameObject("Choice cards",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));
        viewport.transform.SetParent(_panel.transform,false); Rect(viewport,18,148,484,180);
        viewport.GetComponent<Image>().color=new Color(.01f,.025f,.04f,.7f);
        _cardScroll=viewport.GetComponent<ScrollRect>(); _cardScroll.horizontal=false; _cardScroll.movementType=ScrollRect.MovementType.Clamped; _cardScroll.scrollSensitivity=24;
        _cards=(RectTransform)new GameObject("Cards",typeof(RectTransform)).transform; _cards.SetParent(viewport.transform,false); Rect(_cards.gameObject,0,0,474,180);
        _cardScroll.content=_cards; _cardScroll.viewport=(RectTransform)viewport.transform;
        _advanced=new GameObject("Direct input",typeof(RectTransform)); _advanced.transform.SetParent(_panel.transform,false); Rect(_advanced,18,148,484,180);
        Label(_advanced.transform,"분자 이름 · CID · SMILES · SDF 경로",0,0,474,24,18);
        _candidateInput=Input(_advanced.transform,"원하는 분자",0,30,350);
        _loadButton=ButtonAt(_advanced.transform,"불러오기",362,30,112,44,()=>LoadCandidate(_candidateInput.text));
        _candidateInput.onSubmit.AddListener(LoadCandidate);
        _siteInput=Input(_advanced.transform,"잔기 범위 A:123-130",0,90,350);
        _siteButton=ButtonAt(_advanced.transform,"부위 적용",362,90,112,44,()=>SelectResidues(_siteInput.text));
        _boundButton=ButtonAt(_advanced.transform,"기존 결합 분자 주변 선택 / 다음",0,142,474,36,SelectBoundSite);
        _advanced.SetActive(false);
        _recommendButton=ButtonAt(_panel.transform,"후보 추천",18,340,230,38,RequestCandidates);
        ButtonAt(_panel.transform,"직접 입력 / 고급",260,340,242,38,()=>{ bool show=!_advanced.activeSelf; _advanced.SetActive(show); viewport.SetActive(!show); UpdateSiteVisibility(); });
        _candidateLabel=Label(_panel.transform,"",18,390,484,48,20);
        _siteLabel=Label(_panel.transform,"",18,448,484,36,18);
        _dockButton=ButtonAt(_panel.transform,"도킹 실행",18,496,310,48,RunDocking);
        _dockButton.GetComponent<Image>().color=new Color(.16f,.66f,.54f);
        _dockButton.GetComponentInChildren<Text>().color=new Color(.015f,.06f,.07f);
        _cancelButton=ButtonAt(_panel.transform,"취소",340,496,162,48,()=>{ CancelOperation(); Status("실험 요청을 취소했어요. 다른 후보로 다시 시도할 수 있어요."); });
        _historyLabel=Label(_panel.transform,"",18,556,340,64,18);
        _anotherButton=ButtonAt(_panel.transform,"다른 후보 실험",18,632,230,44,NextCandidate);
        _compareButton=ButtonAt(_panel.transform,"AI와 결과 비교",260,632,242,44,CompareTrials);
        _poseButton=ButtonAt(_panel.transform,"다음 자세",366,566,136,38,NextPose);
        _statusLabel=Label(_panel.transform,"",18,688,484,62,17);
        _statusLabel.color=new Color(.61f,.76f,.80f);
        _panel.SetActive(false);
    }
}
