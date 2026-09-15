using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>Front-facing mode chooser and native 3D exploration. Added by IntroDirector.</summary>
public class MoleculeExplorationController : MonoBehaviour
{
    public static MoleculeExplorationController Active { get; private set; }
    [Tooltip("Optional /api/molecule-resolve proxy. If empty, derive from existing CoScientist proxy.")]
    public string resolverEndpoint;
    public string proxyToken;
    [Tooltip("마우스를 1px 끌 때 구조가 도는 각도.")]
    public float dragDegreesPerPixel = .3f;
    IntroDirector _intro;
    GameObject _ui, _pendingRoot;
    // 화면 네 귀퉁이에 고정하는 버튼들. 판넬(월드 캔버스)에 넣으면 판넬 크기를 따라 커지고
    // 구조 앞을 가리므로, 화면 좌표에 붙는 오버레이 캔버스로 따로 뺀다.
    // 오른쪽 아래 = 나가기(모드 선택), 왼쪽 아래 = 지금 보는 방식(리본/원자 상세).
    GameObject _modeOverlay, _viewOverlay;
    bool _dragging;
    ExplorationMoleculeRenderer _renderer;
    InputField _input;
    UnityWebRequest _request;
    MoleculeViewSpec _current;
    string _currentView = "ribbon";
    List<ExplorationAtom> _currentAtoms;
    readonly List<ConversationTurn> _conversation = new List<ConversationTurn>();
    string[] _pendingChoices = Array.Empty<string>();
    bool _caseMode;
    bool _changingMode;
    GameObject _hiddenAssistant;
    bool _assistantWasActive;
    string _lastSubmittedQuery, _lastStatus;
    float _lastSubmittedAt=-10f, _lastStatusAt=-10f;
    RectTransform _panel;
    readonly Dictionary<string,List<ExplorationAtom>> _cache = new Dictionary<string,List<ExplorationAtom>>();
    // 화면에 상태줄을 두지 않으므로 안내는 전부 비서의 말로 나간다.
    const string Ready = "보고 싶은 분자를 저에게 말하거나 아래 채팅창에 입력해 주세요.";
    // 화면에는 고를 수 있는 카드 두 장만 남기고, 무엇을 고르는 것인지는 비서가 말로 설명한다.
    // 판넬에 제목과 설명까지 얹으면 LateUpdate의 "뷰포트에 맞추기"가 그 높이까지 채우려 들면서
    // 글자가 화면을 뒤덮을 만큼 커진다.
    static readonly string[] ModeSelectionLines =
    {
        "어떤 탐색을 시작할까요?",
        "'사례 탐색'은 준비된 사건을 따라가며 돌연변이 단백질의 구조와 역할을 알아보는 길이에요.",
        "'AI 분자 탐색'은 보고 싶은 분자를 말하거나 입력하면 제가 찾아서 3D로 띄워 드려요.",
    };

    public void Initialize(IntroDirector intro) { _intro=intro; }
    public void ShowModeSelection()
    {
        if(_changingMode) return;
        Cancel(); Active=null; _caseMode=false;
        _conversation.Clear();
        _pendingChoices=Array.Empty<string>();
        if(_renderer!=null) Destroy(_renderer.gameObject);
        _renderer=null; _current=null; _currentAtoms=null;
        BeginModeTransition();
        _intro.PrepareModeSelection();
        // 탐색으로 파고들었던 배경을 연구실로 되돌린다. 사건을 접고 나올 때와 같은 후퇴 연출이다.
        _intro.ReturnToLabStage();
        StartCoroutine(FinishModeTransition(false));
    }
    void BuildModeSelection()
    {
        CreatePanel(900,240,transparent:true);
        CardButton("사례 탐색",new Vector2(-230,0),new Vector2(420,200),StartCases,new Color(.35f,.78f,1f));
        CardButton("AI 분자 탐색",new Vector2(230,0),new Vector2(420,200),StartExploration,new Color(.38f,1f,.78f));
        if(_intro.assistant!=null) _intro.assistant.SpeakSequence(ModeSelectionLines);
    }
    void StartCases()
    {
        Cancel(); Active=null; _caseMode=true;
        // 사례 탐색에서도 나가는 버튼은 AI 분자 탐색과 똑같은 것 — 화면 우하단 오버레이.
        // 월드 판넬로 띄우면 자리도 생김새도 달라서, 두 모드를 오갈 때마다 "여기서
        // 빠져나가는 버튼"을 다시 찾아야 한다.
        ResetUi();
        CreateModeOverlay();
        // 모드 설명이 아직 큐에 남아 있으면 인트로 인사 앞에 끼어들어 선택 화면 이야기를 계속하게 된다.
        if(_intro.assistant!=null) _intro.assistant.ResetConversation();
        _intro.Play();
    }
    public void StartExploration()
    {
        if(_changingMode) return;
        Cancel(); Active=null; _caseMode=false;
        _conversation.Clear();
        _pendingChoices=Array.Empty<string>();
        BeginModeTransition();
        _intro.PrepareModeSelection();
        // 사건을 고른 것과 똑같이 배경이 바뀌며 분자 무대로 들어간다 — 카메라 연출도, 그에 딸린
        // 워프·블러·음향도 사건 쪽과 같은 컴포넌트가 만든다.
        _intro.EnterExplorationStage();
        StartCoroutine(FinishModeTransition(true));
    }
    void BeginModeTransition()
    {
        _changingMode=true;
        ResetUi();
        if(_intro.assistant!=null)
        {
            _intro.assistant.ResetConversation();
            _hiddenAssistant=_intro.assistant.gameObject;
            _assistantWasActive=_hiddenAssistant.activeSelf;
            _hiddenAssistant.SetActive(false);
        }
    }
    IEnumerator FinishModeTransition(bool exploration)
    {
        while(_intro.IsStageTransitioning) yield return null;
        if(_hiddenAssistant!=null) _hiddenAssistant.SetActive(_assistantWasActive);
        _hiddenAssistant=null;
        _changingMode=false;
        if(exploration) { Active=this; BuildExploration(); }
        else BuildModeSelection();
    }
    void BuildExploration()
    {
        // 판넬에는 채팅 한 줄만 둔다. 제목·예시·상태는 비서가 말하고, 버튼은 화면 귀퉁이
        // 오버레이로 나갔다 — 판넬이 높아질수록 뷰포트 맞추기가 판넬을 키워 구조를 가린다.
        CreatePanel(1000,90,transparent:true);
        Color accent=new Color(.38f,1f,.78f);
        var go = new GameObject("Molecule query",typeof(RectTransform),typeof(InputField));
        go.transform.SetParent(_panel,false); Place((RectTransform)go.transform,new Vector2(-85,0),new Vector2(780,66));
        // 채팅창도 사건 카드와 같은 3겹 — 검색 버튼 옆에서 톤이 어긋나지 않게.
        CardLayer(go.transform,"Glow",HoloSpriteFactory.Glow(),new Color(accent.r,accent.g,accent.b,.14f),18f);
        Image field=CardLayer(go.transform,"Panel",HoloSpriteFactory.Panel(),new Color(.02f,.06f,.10f,.94f),0f,true);
        CardLayer(go.transform,"Stroke",HoloSpriteFactory.Stroke(),new Color(accent.r,accent.g,accent.b,.55f),0f);
        _input=go.GetComponent<InputField>();
        _input.targetGraphic=field;
        var text=Label("",new Vector2(14,0),new Vector2(720,52),26,go.transform);
        text.alignment=TextAnchor.MiddleLeft;
        var placeholder=Label("인슐린 보여줘",new Vector2(14,0),new Vector2(720,52),26,go.transform);
        placeholder.alignment=TextAnchor.MiddleLeft; placeholder.color=new Color(.6f,.7f,.75f);
        _input.textComponent=text; _input.placeholder=placeholder; _input.characterLimit=300;
        _input.onSubmit.AddListener(Submit);
        CardButton("검색",new Vector2(395,0),new Vector2(170,66),()=>Submit(_input.text),accent,26);

        CreateModeOverlay();
        CreateViewOverlay();
        // 대표 분자 예시는 버튼으로 늘어놓지 않는다. 무엇을 물어볼 수 있는지는 비서가 말해준다.
        if(_intro.assistant!=null)
        {
            _intro.assistant.SpeakSequence(new[]
            {
                Ready,
                "이름을 몰라도 괜찮아요. 관심 있는 기능을 말하거나, 어떤 단백질을 보면 좋을지 추천해 달라고 해보세요.",
            });
        }
    }
    public void Submit(string query)
    {
        if(Active!=this || string.IsNullOrWhiteSpace(query)) return;
        query=query.Trim(); if(query.Length>300) query=query.Substring(0,300);
        if(query==_lastSubmittedQuery && Time.unscaledTime-_lastSubmittedAt<.5f) return;
        _lastSubmittedQuery=query; _lastSubmittedAt=Time.unscaledTime;
        if(_input!=null) _input.text=query;
        Cancel();
        StartCoroutine(Guarded(ResolveAndLoad(query)));
    }
    IEnumerator ResolveAndLoad(string query)
    {
        MoleculeViewSpec spec = null;
        string endpoint=resolverEndpoint, token=proxyToken;
        var proxy=_intro.assistant!=null ? _intro.assistant.client as AICoScientistClient : null;
        if(string.IsNullOrWhiteSpace(endpoint) && proxy!=null && proxy.IsConfigured)
        {
            endpoint=new Uri(new Uri(proxy.backendEndpoint),"/api/molecule-resolve").ToString();
            token=proxy.proxyToken;
        }
        // A configured model interprets every utterance, including common names and view commands.
        // The small dictionary is only an explicitly offline fallback.
        if(string.IsNullOrWhiteSpace(endpoint))
        {
            spec=MoleculeCatalog.Match(query);
            if(spec==null && (query=="원자 상세" || query=="원자 보여줘" || query=="리본" || query=="리본 보여줘"))
            {
                if(_currentAtoms==null) throw new InvalidOperationException("먼저 분자를 검색해 주세요.");
                yield return Render(_currentAtoms,_current,query.StartsWith("원자")?"atoms":"ribbon");
                yield break;
            }
            if(spec==null) throw new InvalidOperationException("자연어 해석 서버가 연결되지 않았어요. 현재는 인슐린, 헤모글로빈, GFP를 이름으로 불러볼 수 있어요.");
            spec=JsonUtility.FromJson<MoleculeViewSpec>(JsonUtility.ToJson(spec));
        }
        else
        {
            SetStatus("요청의 의미와 현재 구조를 살펴보는 중이에요…");
            Resolution result;
            using(var req=new UnityWebRequest(endpoint,"POST"))
            {
                _request=req; req.timeout=65;
                req.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(
                    new Query { query=query, current=BuildContext(), history=BuildHistory(), pendingChoices=_pendingChoices })));
                req.downloadHandler=new DownloadHandlerBuffer(); req.SetRequestHeader("Content-Type","application/json");
                if(!string.IsNullOrEmpty(token)) req.SetRequestHeader("X-App-Token",token);
                yield return req.SendWebRequest(); _request=null;
                if(req.result!=UnityWebRequest.Result.Success)
                {
                    string message="요청 처리 중 연결에 문제가 생겼어요. 잠시 후 다시 시도해 주세요.";
                    try
                    {
                        var failure=JsonUtility.FromJson<Resolution>(req.downloadHandler.text);
                        if(!string.IsNullOrWhiteSpace(failure?.message)) message=failure.message;
                        else if(!string.IsNullOrWhiteSpace(failure?.error)) message=failure.error;
                    }
                    catch { }
                    Debug.LogWarning("[AI 분자 탐색] Resolver HTTP "+req.responseCode);
                    throw new InvalidOperationException(message);
                }
                result=JsonUtility.FromJson<Resolution>(req.downloadHandler.text);
            }
            if(result==null) throw new InvalidOperationException("요청 처리 결과가 비어 있어요.");
            RememberExchange(query,result.message);
            if(result.action=="clarify") _pendingChoices=result.choices??Array.Empty<string>();
            else if(result.action!="explain") _pendingChoices=Array.Empty<string>();
            switch(result.action)
            {
                case "clarify": case "explain":
                    SetStatus(result.message??"조금 더 구체적으로 알려주세요."); yield break;
                case "zoom":
                    RequireCurrent(); Zoom(Mathf.Clamp(result.amount,.5f,2f));
                    SetStatus("현재 구조의 크기를 조절했어요."); yield break;
                case "rotate":
                    RequireCurrent();
                    _renderer.transform.Rotate(Vector3.up,Mathf.Clamp(result.amount,-180f,180f),Space.World);
                    SetStatus("현재 구조를 돌렸어요."); yield break;
                case "view":
                    RequireCurrent();
                    var changed=JsonUtility.FromJson<MoleculeViewSpec>(JsonUtility.ToJson(_current));
                    if(result.resetSelection) { changed.chains=""; changed.residueStart=changed.residueEnd=0; changed.ligand=""; changed.secondaryStructure=""; }
                    if(!string.IsNullOrEmpty(result.chains)) changed.chains=result.chains;
                    if(result.residueEnd>0) { changed.residueStart=result.residueStart; changed.residueEnd=result.residueEnd; }
                    if(!string.IsNullOrEmpty(result.ligand)) changed.ligand=result.ligand;
                    if(!string.IsNullOrEmpty(result.secondaryStructure)) changed.secondaryStructure=result.secondaryStructure;
                    changed.description="현재 구조에서 요청한 부분을 표시";
                    changed.NormalizeAndValidate();
                    yield return Render(_currentAtoms,changed,ValidateView(string.IsNullOrEmpty(result.view)?_currentView:result.view)); yield break;
                case null: case "": case "load": break; // Older deployed resolvers return only spec.
                default: throw new InvalidOperationException("지원하지 않는 표시 동작입니다.");
            }
            spec=result.spec;
            if(spec==null) throw new InvalidOperationException(result.message??"조건에 맞는 구조를 찾지 못했어요.");
            if(!string.IsNullOrWhiteSpace(result.message)) spec.description += " " + result.message;
        }
        try { spec.NormalizeAndValidate(); }
        catch (InvalidOperationException)
        {
            Debug.LogWarning("[AI 분자 탐색] Invalid resolver selection: "+JsonUtility.ToJson(spec));
            throw;
        }
        if(!_cache.TryGetValue(spec.pdbId,out var atoms))
        {
            SetStatus(spec.title+" · 구조 좌표를 불러오는 중…");
            string pdb=null;
            using(var req=UnityWebRequest.Get("https://files.rcsb.org/download/"+spec.pdbId+".pdb"))
            {
                _request=req; req.timeout=20;
                var op=req.SendWebRequest();
                while(!op.isDone)
                {
                    if(req.downloadedBytes>ExplorationPdbParser.MaxBytes) { req.Abort(); throw new InvalidOperationException("파일이 현재 로딩 제한을 넘었습니다."); }
                    yield return null;
                }
                _request=null;
                if(req.result==UnityWebRequest.Result.Success) pdb=req.downloadHandler.text;
            }
            if(pdb==null && Array.Exists(MoleculeCatalog.Entries,e=>e.pdbId==spec.pdbId))
            {
                using(var req=UnityWebRequest.Get(StreamingAssetsUrl.For("exploration/"+spec.pdbId+".pdb")))
                {
                    _request=req; req.timeout=10; yield return req.SendWebRequest(); _request=null;
                    if(req.result==UnityWebRequest.Result.Success) pdb=req.downloadHandler.text;
                }
            }
            if(pdb==null) throw new InvalidOperationException("구조 다운로드에 실패했습니다. 연결을 확인하거나 다른 구조를 선택해 주세요.");
            atoms=ExplorationPdbParser.Parse(pdb);
            if(_cache.Count>=3) _cache.Clear();
            _cache[spec.pdbId]=atoms;
        }
        yield return Render(atoms,spec,ValidateView(spec.view));
    }
    void RequireCurrent()
    {
        if(_renderer==null || _currentAtoms==null) throw new InvalidOperationException("먼저 분자를 검색해 주세요.");
    }
    static string ValidateView(string view)
    {
        if(string.IsNullOrEmpty(view)) return "ribbon";
        if(view!="ribbon" && view!="atoms" && view!="ligand") throw new InvalidOperationException("지원하지 않는 표시 방식입니다.");
        return view;
    }
    CurrentContext BuildContext()
    {
        if(_current==null || _currentAtoms==null) return null;
        return new CurrentContext {
            pdbId=_current.pdbId,title=_current.title,chains=_current.chains,view=_currentView,
            description=_current.description,displayScope=_renderer!=null?_renderer.DisplayScope:"",
            availableChains=string.Join(",",_currentAtoms.Where(a=>!a.hetero).Select(a=>a.chain).Distinct().Take(80)),
            residueRanges=string.Join("; ",_currentAtoms.Where(a=>!a.hetero).GroupBy(a=>a.chain).Take(80)
                .Select(g=>g.Key+":"+g.Min(a=>a.number)+".."+g.Max(a=>a.number))),
            ligands=string.Join(",",_currentAtoms.Where(a=>a.hetero).Select(a=>a.residue).Distinct().Take(80))
        };
    }

    ConversationTurn[] BuildHistory()
    {
        // 최근 대화만 보내 질문-답변 연결은 유지하고 요청 크기는 제한한다.
        return _conversation.Skip(Mathf.Max(0,_conversation.Count-6)).ToArray();
    }
    void RememberExchange(string query,string answer)
    {
        _conversation.Add(new ConversationTurn { role="user", content=query });
        if(!string.IsNullOrWhiteSpace(answer))
            _conversation.Add(new ConversationTurn { role="assistant", content=answer });
        if(_conversation.Count>8) _conversation.RemoveRange(0,_conversation.Count-8);
    }
    IEnumerator Render(List<ExplorationAtom> atoms,MoleculeViewSpec spec,string view)
    {
        SetStatus(spec.title+" · 3D를 만드는 중…");
        _pendingRoot=new GameObject("AI molecular structure"); _pendingRoot.SetActive(false);
        var candidate=_pendingRoot.AddComponent<ExplorationMoleculeRenderer>();
        yield return candidate.Build(atoms,spec,view);
        // 구조는 카메라 앞 고정 거리에 한 번만 놓는다. 진입 연출이 아직 날아가는 중이면
        // 그 중간 지점에 세워져, 도착했을 땐 화면 밖에 남는다.
        while(_intro.IsStageTransitioning) yield return null;
        var cam=_intro.targetCamera!=null ? _intro.targetCamera : Camera.main;
        if(cam==null) throw new InvalidOperationException("표시할 카메라가 없습니다.");
        candidate.transform.position=cam.ViewportToWorldPoint(new Vector3(.5f,.64f,2f));
        candidate.transform.rotation=cam.transform.rotation;
        // Fit to available viewport, preserving molecular proportions.
        float height=cam.orthographic?2*cam.orthographicSize:4*Mathf.Tan(cam.fieldOfView*Mathf.Deg2Rad*.5f);
        candidate.transform.localScale*=Mathf.Min(height*.43f,height*cam.aspect*.7f);
        if(_renderer!=null) { _renderer.gameObject.SetActive(false); Destroy(_renderer.gameObject); }
        _renderer=candidate; _pendingRoot.SetActive(true); _pendingRoot=null;
        _current=spec; _currentAtoms=atoms; _currentView=view;
        if(_viewOverlay!=null) _viewOverlay.SetActive(true);
        yield return null;
        SetStatus(spec.title+"을 표시했어요. "+spec.description+" "+candidate.DisplayScope+"를 보고 있어요. "+
            "마우스로 끌면 돌려볼 수 있고, 휠을 굴리면 확대돼요.");
    }
    void ChangeView(string view)
    {
        if(_currentAtoms==null) { SetStatus("먼저 분자를 검색해 주세요."); return; }
        Cancel(); StartCoroutine(Guarded(Render(_currentAtoms,_current,view)));
    }
    IEnumerator Guarded(IEnumerator routine)
    {
        var stack=new Stack<IEnumerator>(); stack.Push(routine);
        try
        {
            while(stack.Count>0)
            {
                object next=null; bool moved=false; Exception error=null;
                try { moved=stack.Peek().MoveNext(); if(moved) next=stack.Peek().Current; }
                catch(Exception ex) { error=ex; }
                if(error!=null)
                {
                    _request=null;
                    if(_pendingRoot!=null) Destroy(_pendingRoot); _pendingRoot=null;
                    // 서버의 선택 설명보다 실제 표시 실패가 최신 대화 맥락이어야 한다.
                    _conversation.Add(new ConversationTurn { role="assistant", content="구조 표시 실패: "+error.Message });
                    if(_conversation.Count>8) _conversation.RemoveRange(0,_conversation.Count-8);
                    SetStatus(error.Message); Debug.LogWarning("[AI 분자 탐색] "+error.Message); yield break;
                }
                if(!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if(next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
        }
        finally { while(stack.Count>0) (stack.Pop() as IDisposable)?.Dispose(); }
    }
    void Cancel()
    {
        if(_request!=null) { _request.Abort(); _request.Dispose(); _request=null; }
        StopAllCoroutines();
        if(_pendingRoot!=null) Destroy(_pendingRoot); _pendingRoot=null;
    }
    /// <summary>
    /// 구조를 마우스로 직접 돌리고 확대한다. 버튼으로 25도씩 끊어 돌리면 보고 싶은 각도를
    /// 맞추기까지 여러 번 눌러야 해서, 끌어서 돌리는 편이 훨씬 빠르다.
    ///
    /// 회전축은 화면 기준(카메라의 up/right)으로 잡는다. 월드 축으로 돌리면 구조가 이미
    /// 기울어 있을 때 끄는 방향과 도는 방향이 어긋나 손에 붙지 않는다.
    /// </summary>
    void Update()
    {
        if(Active!=this || _renderer==null) return;
        var mouse=Mouse.current; if(mouse==null) return;
        var cam=_intro!=null&&_intro.targetCamera!=null?_intro.targetCamera:Camera.main; if(cam==null) return;
        // UI 위에서 시작한 드래그는 구조가 아니라 그 UI의 것이다 — 채팅창에서 글을 끌어
        // 선택하는 동안 뒤에서 구조가 따라 도는 것을 막는다.
        bool overUi=EventSystem.current!=null && EventSystem.current.IsPointerOverGameObject();
        if(mouse.leftButton.wasPressedThisFrame) _dragging=!overUi;
        if(!mouse.leftButton.isPressed) _dragging=false;
        if(_dragging)
        {
            Vector2 delta=mouse.delta.ReadValue();
            if(delta.sqrMagnitude>0f)
            {
                _renderer.transform.Rotate(cam.transform.up,delta.x*dragDegreesPerPixel,Space.World);
                _renderer.transform.Rotate(cam.transform.right,-delta.y*dragDegreesPerPixel,Space.World);
            }
        }
        float scroll=mouse.scroll.ReadValue().y;
        if(!overUi && Mathf.Abs(scroll)>.01f) Zoom(scroll>0f?1.08f:1f/1.08f);
    }
    void Zoom(float factor)
    {
        if(_renderer==null) return;
        float scale=_renderer.transform.localScale.x*factor;
        if(scale>.005f && scale<5) _renderer.transform.localScale*=factor;
    }
    /// <summary>
    /// 진행 상황과 실패 안내. 판넬에 상태줄을 두지 않으므로 비서가 대신 말한다 —
    /// <see cref="AIAssistantBrain.SpeakNow"/>는 큐를 비우고 교체하므로, 로딩 중 여러 번
    /// 불러도 마지막 한 마디만 남는다.
    /// </summary>
    void SetStatus(string value)
    {
        if(string.IsNullOrWhiteSpace(value)) return;
        if(value==_lastStatus && Time.unscaledTime-_lastStatusAt<2f) return;
        _lastStatus=value; _lastStatusAt=Time.unscaledTime;
        if(_intro!=null && _intro.assistant!=null) _intro.assistant.SpeakNow(value);
    }
    void LateUpdate()
    {
        // 사례 탐색에는 판넬이 없다 — 사건을 고를 수 있는 동안에만 우하단 '모드 선택'을 켠다.
        // 사건 안으로 들어가면 같은 자리에 StructureLevelBackButton의 '이전'이 서므로 겹치지 않는다.
        if(_caseMode)
        {
            if(_modeOverlay!=null)
                _modeOverlay.SetActive(_intro!=null && _intro.board!=null && _intro.board.IsVisible);
            return;
        }
        if(_ui==null || _intro==null) return;
        var cam=_intro.targetCamera!=null ? _intro.targetCamera : Camera.main; if(cam==null) return;
        // 모드 선택 카드는 화면 아래쪽에 둔다 — 비서는 우상단(assistantViewportPosition
        // 0.88, 0.76)에 서고 말풍선이 그 둘레로 퍼지므로, 위로 올릴수록 카드와 겹친다.
        float y=Active==this?.15f:.24f;
        _ui.transform.position=cam.ViewportToWorldPoint(new Vector3(.5f,y,1.6f));
        _ui.transform.rotation=cam.transform.rotation;
        float height=cam.orthographic?cam.orthographicSize*2:3.2f*Mathf.Tan(cam.fieldOfView*Mathf.Deg2Rad*.5f);
        // 판넬 폭이 화면 가로에서 차지할 비율로 크기를 정한다(사례 보드의 viewportWidthFraction과
        // 같은 방식). 세로 비율은 화면비 2.4:1보다 좁은 화면에서는 걸리지 않는 안전장치다.
        float scale=Active==this
            ? Mathf.Min(height*cam.aspect*.56f/_panel.sizeDelta.x,height*.16f/_panel.sizeDelta.y)
            : Mathf.Min(height*cam.aspect*.52f/_panel.sizeDelta.x,height*.34f/_panel.sizeDelta.y);
        _ui.transform.localScale=Vector3.one*scale;
    }
    /// <summary>앞 화면이 남긴 판넬·오버레이를 치운다. 화면을 새로 짓기 전에 항상 먼저 부른다.</summary>
    void ResetUi()
    {
        if(_ui!=null) { _ui.SetActive(false); Destroy(_ui); } _ui=null;
        DestroyOverlays();
        _input=null; _dragging=false;
    }
    /// <param name="transparent">카드가 스스로 배경을 갖는 화면(모드 선택)에서는 판넬 배경을 지운다.
    /// 알파만 0으로 두면 그래픽이 그대로 레이캐스트를 먹으므로 클릭 대상에서도 뺀다.</param>
    void CreatePanel(float width,float height,bool transparent=false)
    {
        ResetUi();
        _ui=new GameObject("Exploration mode panel",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
        _panel=(RectTransform)_ui.transform; _panel.sizeDelta=new Vector2(width,height);
        var canvas=_ui.GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace; canvas.worldCamera=_intro.targetCamera;
        canvas.sortingOrder=100;
        var background=_ui.AddComponent<Image>();
        background.color=transparent?Color.clear:new Color(.015f,.045f,.075f,.97f);
        background.raycastTarget=!transparent;
    }
    Text Label(string value,Vector2 position,Vector2 size,int fontSize,Transform parent=null)
    {
        var go=new GameObject("Label",typeof(RectTransform),typeof(Text)); go.transform.SetParent(parent??_panel,false);
        Place((RectTransform)go.transform,position,size);
        var text=go.GetComponent<Text>(); text.font=HoloFont.Resolve(); text.fontSize=fontSize;
        text.color=new Color(.85f,.95f,1); text.alignment=TextAnchor.MiddleCenter; text.text=value;
        text.raycastTarget=false; text.supportRichText=false; return text;
    }
    /// <summary>
    /// 사례 카드(<see cref="QuestSelectionBoard"/>)와 같은 템플릿의 버튼 —
    /// 글로우 → 패널 → 외곽선 3겹에 ColorTint 하이라이트. 모드 선택과 사례 선택이
    /// 연달아 나오는 화면이라 둘의 생김새가 다르면 같은 고르기인 줄 알아보기 어렵다.
    /// </summary>
    void CardButton(string caption,Vector2 position,Vector2 size,UnityEngine.Events.UnityAction action,Color accent,
                    int fontSize=34)
    {
        // 이름은 캡션 그대로 — 스모크 테스트가 버튼을 이름으로 찾는다.
        var go=new GameObject(caption,typeof(RectTransform),typeof(Button)); go.transform.SetParent(_panel,false);
        Place((RectTransform)go.transform,position,size);
        CardLayer(go.transform,"Glow",HoloSpriteFactory.Glow(),new Color(accent.r,accent.g,accent.b,.20f),26f);
        // 패널이 카드 전체에 깔려 있고 이것만 클릭을 받는다 — 어디를 눌러도 카드가 선택된다.
        Image panel=CardLayer(go.transform,"Panel",HoloSpriteFactory.Panel(),new Color(.02f,.06f,.10f,.94f),0f,true);
        CardLayer(go.transform,"Stroke",HoloSpriteFactory.Stroke(),new Color(accent.r,accent.g,accent.b,.8f),0f);

        var label=Label(caption,Vector2.zero,size,fontSize,go.transform);
        label.fontStyle=FontStyle.Bold; label.color=Color.white;

        var button=go.GetComponent<Button>();
        button.targetGraphic=panel; button.transition=Selectable.Transition.ColorTint;
        var colors=button.colors;
        colors.normalColor=Color.white;
        colors.highlightedColor=new Color(1.3f,1.3f,1.3f,1f);
        colors.pressedColor=new Color(.8f,.8f,.8f,1f);
        colors.fadeDuration=.12f;
        button.colors=colors;
        button.onClick.AddListener(action);
    }
    static Image CardLayer(Transform parent,string name,Sprite sprite,Color color,float expand,bool raycastTarget=false)
    {
        var go=new GameObject(name,typeof(RectTransform)); go.transform.SetParent(parent,false);
        var rect=(RectTransform)go.transform;
        rect.anchorMin=Vector2.zero; rect.anchorMax=Vector2.one;
        rect.offsetMin=new Vector2(-expand,-expand); rect.offsetMax=new Vector2(expand,expand);
        var image=go.AddComponent<Image>();
        image.sprite=sprite; image.type=Image.Type.Sliced; image.color=color; image.raycastTarget=raycastTarget;
        return image;
    }
    /// <summary>
    /// "모드 선택"을 판넬에서 떼어 화면 우하단에 고정한다. 사례 탐색과 AI 분자 탐색이
    /// 같은 버튼을 쓴다 — 어느 쪽에 있든 나가는 자리가 같아야 한다. 구조를 탐색할 때 누르는 '이전'
    /// (<see cref="StructureLevelBackButton"/>)과 같은 자리·같은 크기·같은 홀로그램 톤이라,
    /// 두 화면을 오가도 "여기서 빠져나가는 버튼"을 같은 곳에서 찾게 된다.
    /// </summary>
    void CreateModeOverlay()
    {
        if(_modeOverlay!=null) { _modeOverlay.SetActive(false); Destroy(_modeOverlay); }
        _modeOverlay=CreateOverlay("Exploration mode overlay");
        OverlayButton(_modeOverlay.transform,"모드 선택",new Vector2(1,0),new Vector2(-28,24),
            new Vector2(150,46),ShowModeSelection);
    }
    /// <summary>
    /// 지금 무엇을 보고 있는지 바꾸는 버튼들 — 나가기 버튼의 반대편인 화면 좌하단.
    /// 볼 구조가 아직 없을 때는 눌러도 할 일이 없으므로 <see cref="Render"/>가 켤 때까지 숨겨둔다.
    /// </summary>
    void CreateViewOverlay()
    {
        if(_viewOverlay!=null) { _viewOverlay.SetActive(false); Destroy(_viewOverlay); }
        _viewOverlay=CreateOverlay("Exploration view overlay");
        OverlayButton(_viewOverlay.transform,"리본",new Vector2(0,0),new Vector2(28,24),
            new Vector2(130,46),()=>ChangeView("ribbon"));
        OverlayButton(_viewOverlay.transform,"원자 상세",new Vector2(0,0),new Vector2(170,24),
            new Vector2(150,46),()=>ChangeView("atoms"));
        _viewOverlay.SetActive(false);
    }
    GameObject CreateOverlay(string name)
    {
        var overlay=new GameObject(name,
            typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        overlay.transform.SetParent(transform,false);
        var canvas=overlay.GetComponent<Canvas>();
        canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=50;
        var scaler=overlay.GetComponent<CanvasScaler>();
        scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution=new Vector2(1920,1080);
        return overlay;
    }
    /// <param name="anchor">화면의 어느 귀퉁이에 붙일지. (1,0)=우하단, (0,0)=좌하단.</param>
    void OverlayButton(Transform parent,string caption,Vector2 anchor,Vector2 offset,Vector2 size,
                       UnityEngine.Events.UnityAction action)
    {
        var go=new GameObject(caption,typeof(RectTransform),typeof(Button),typeof(ScreenSafePanel));
        go.transform.SetParent(parent,false);
        var rect=(RectTransform)go.transform;
        rect.anchorMin=rect.anchorMax=rect.pivot=anchor;
        rect.anchoredPosition=offset; rect.sizeDelta=size;
        Color accent=new Color(.25f,.75f,1f);
        CardLayer(go.transform,"Glow",HoloSpriteFactory.Glow(),new Color(accent.r,accent.g,accent.b,.18f),16f);
        Image panel=CardLayer(go.transform,"Panel",HoloSpriteFactory.Panel(),new Color(.02f,.06f,.10f,.94f),0f,true);
        CardLayer(go.transform,"Stroke",HoloSpriteFactory.Stroke(),new Color(accent.r,accent.g,accent.b,.8f),0f);
        var label=Label(caption,Vector2.zero,size,22,go.transform);
        label.fontStyle=FontStyle.Bold; label.color=new Color(.75f,.95f,1f);
        var button=go.GetComponent<Button>();
        button.targetGraphic=panel; button.transition=Selectable.Transition.ColorTint;
        var colors=button.colors;
        colors.normalColor=Color.white;
        colors.highlightedColor=new Color(1.35f,1.35f,1.35f,1f);
        colors.pressedColor=new Color(.8f,.8f,.8f,1f);
        colors.fadeDuration=.12f;
        button.colors=colors;
        button.onClick.AddListener(action);
    }
    void DestroyOverlays()
    {
        if(_modeOverlay!=null) { _modeOverlay.SetActive(false); Destroy(_modeOverlay); } _modeOverlay=null;
        if(_viewOverlay!=null) { _viewOverlay.SetActive(false); Destroy(_viewOverlay); } _viewOverlay=null;
    }
    static void Place(RectTransform rect,Vector2 position,Vector2 size) { rect.anchoredPosition=position; rect.sizeDelta=size; }
    void OnDestroy()
    {
        if(_hiddenAssistant!=null) _hiddenAssistant.SetActive(_assistantWasActive);
        Cancel(); if(Active==this) Active=null;
        DestroyOverlays();
        if(_ui!=null) Destroy(_ui); if(_renderer!=null) Destroy(_renderer.gameObject);
    }
    [Serializable] class Query { public string query; public CurrentContext current; public ConversationTurn[] history; public string[] pendingChoices; }
    [Serializable] class ConversationTurn { public string role,content; }
    [Serializable] class CurrentContext
    {
        public string pdbId,title,chains,view,description,displayScope,availableChains,residueRanges,ligands;
    }
    [Serializable] class Resolution
    {
        public MoleculeViewSpec spec;
        public string action,message,error,view,chains,ligand,secondaryStructure;
        public string[] choices;
        public float amount;
        public int residueStart,residueEnd;
        public bool resetSelection;
    }
}

