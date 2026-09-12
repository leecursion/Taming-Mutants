using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Candidate inspection, prediction, experiment feedback and session-local evidence log.</summary>
public class LabExperimentUI : MonoBehaviour
{
    private CompoundSelectionPanel _panel;
    private CompoundSlot _selected;
    private GameObject _root;
    private RectTransform _notebookRect;
    private Text _title, _detail, _status, _stamp;
    private Coroutine _recordFeedback;
    private Button _start, _history;
    private readonly List<Button> _predictions = new List<Button>();
    private readonly List<string> _records = new List<string>();
    private readonly string[] _choices = { "목표 효과", "부분 효과", "효과 부족" };
    private string _prediction;
    private bool _showHistory, _running, _hasContent;
    private int _historyPage;
    private Action _verification;
    private string _verificationLabel;
    public string Prediction => _prediction;

    // Same scale as the notebook's CanvasScaler (1600 x 900, match = 0.5).
    // Reserve this space even before inspection so selecting a candidate never moves the grid.
    public static float ReservedBottomViewportHeight
    {
        get
        {
            float scale = Mathf.Sqrt((Screen.width / 1600f) * (Screen.height / 900f));
            return (Screen.safeArea.yMin + Mathf.Max(16f * scale, 20f) + 224f * scale) / Mathf.Max(Screen.height, 1);
        }
    }

    public void Inspect(CompoundSelectionPanel panel, CompoundSlot slot)
    {
        _panel = panel;
        StopRecordFeedback();
        RestorePreview();
        _selected = slot;
        slot.MoleculeRoot.transform.localScale = Vector3.one * slot.DisplayFitScale * 1.12f;
        slot.Inspecting = true;
        _prediction = null;
        _showHistory = false;
        EnsureUI();
        _stamp.text = "";
        _detail.fontSize = 16;
        _title.text = slot.Data.display_name + " · 관찰";
        _detail.text = slot.Data.subtitle + "\n예상 효과를 고른 뒤 실험하세요. 분자는 좌우로 돌려 볼 수 있어요.";
        _status.text = panel.zoomOverrideActive ? "옅은 잔상: 가열 전 위치 · 보라색: 변이 부위" : "관찰 → 예상 → 실험 → 기록";
        _status.color = Color.cyan;
        RefreshButtons();
    }

    public void ResetExperiment()
    {
        StopRecordFeedback();
        RestorePreview();
        _hasContent = false;
        _historyPage = 0;
        _records.Clear();
        _prediction = null;
        _running = false;
        _verification = null;
        _showHistory = false;
        if (_root != null) _root.SetActive(false);
    }

    private void RestorePreview()
    {
        if (_selected != null && _selected.MoleculeRoot != null)
        {
            _selected.MoleculeRoot.transform.localScale = Vector3.one * _selected.DisplayFitScale;
            _selected.Inspecting = false;
        }
        _selected = null;
    }

    public void SetPhase(string text)
    {
        EnsureUI();
        _status.text = text;
    }

    public void Record(CompoundData data, Color color, bool orderError)
    {
        _running = false;
        EnsureUI();
        string observed = orderError ? "선행 작업 필요" :
            (!string.IsNullOrEmpty(data.observation) ? data.observation : data.outcome);
        string expected = _prediction ?? "예상 없음";
        string actual = orderError ? "준비 미완료" : data.Outcome == DockingOutcome.Success ?
            "목표 효과" : (data.Outcome == DockingOutcome.PartialRecovery || data.Outcome == DockingOutcome.FragmentHit) ?
            "부분 효과" : "효과 부족";
        string comparison = orderError ? "순서를 확인하세요" : expected == actual ? "예상과 일치" : "예상을 수정해 보세요";
        _records.Add(data.display_name + " · 예상: " + expected + "\n관찰: " + observed);
        _title.text = "실험 " + _records.Count + " · " + comparison;
        _detail.text = observed + "\n예상: " + expected + " / 관찰: " + actual;
        _status.text = data.Outcome == DockingOutcome.Success && !orderError ?
            (data.completes_stage ? "효과 관찰 완료 · 기능 확인" : "첫 작업 완료 · 다음 후보를 선택하세요") : "기록 획득 · 다음 가설을 세워 보세요";
        _status.color = color;
        ShowRecordFeedback(false);
        RestorePreview();
        _prediction = null;
        RefreshButtons();
    }

    public void OfferVerification(string label, Action action)
    {
        EnsureUI();
        _verificationLabel = label;
        _verification = action;
        _status.text = label + " · 직접 확인하세요";
        RefreshButtons();
    }

    private void Update()
    {
        if (_root == null) return;
        // 후보물질 칸과 정확히 같은 판단을 따른다. 레벨만 보면(예전 방식) 사건을 끝내고
        // 연구실로 돌아왔을 때 레벨이 아미노산인 채로 무대만 꺼져서 노트만 화면에 남는다.
        bool visible = _hasContent && _panel != null && _panel.isActiveAndEnabled && _panel.ContentVisible;
        _root.SetActive(visible);
        if (visible)
        {
            UpdateLayout();
            RefreshButtons();
        }
    }

    private void OnDisable() { StopRecordFeedback(); if (_root != null) _root.SetActive(false); }
    private void OnDestroy() { RestorePreview(); if (_root != null) Destroy(_root); }

    private void RefreshButtons()
    {
        if (_start == null) return;
        bool canInspect = !_running && _selected != null && _panel != null && _panel.CanStartExperiment;
        for (int i = 0; i < _predictions.Count; i++)
        {
            _predictions[i].interactable = canInspect;
            _predictions[i].GetComponent<Image>().color = _prediction == _choices[i] ?
                new Color(0.1f, 0.5f, 0.6f, 1f) : new Color(0.08f, 0.17f, 0.23f, 1f);
        }
        _start.interactable = !_running && (_verification != null || (canInspect && _prediction != null));
        _start.GetComponentInChildren<Text>().text = _verification != null ? _verificationLabel : "실험 시작";
        _history.interactable = _records.Count > 0 && !_running;
    }

    private void StartSelected()
    {
        if (_verification != null && !_running)
        {
            Action action = _verification;
            _verification = null;
            _running = true;
            RefreshButtons();
            action();
            return;
        }
        if (_selected == null || _prediction == null || _panel == null || !_panel.CanStartExperiment) return;
        _running = true;
        _showHistory = false;
        _detail.text = "예상: " + _prediction + " · 분자의 움직임과 기능 변화를 관찰하세요.";
        _status.color = Color.cyan;
        _panel.StartExperiment(_selected);
        RefreshButtons();
    }

    public void FinishVerification(string evidence)
    {
        _running = false;
        _records.Add("검증 · " + evidence);
        _title.text = "검증 완료";
        _detail.text = evidence;
        _status.text = "관찰 증거를 기록했습니다";
        _status.color = Color.green;
        ShowRecordFeedback(true);
        RefreshButtons();
    }

    private void StopRecordFeedback()
    {
        if (_recordFeedback != null) StopCoroutine(_recordFeedback);
        _recordFeedback = null;
        if (_history != null)
        {
            _history.transform.localScale = Vector3.one;
            _history.GetComponentInChildren<Text>().text = "실험 기록";
        }
        if (_stamp != null) { _stamp.text = ""; _stamp.transform.localScale = Vector3.one; }
    }

    private void ShowRecordFeedback(bool verified)
    {
        StopRecordFeedback();
        _recordFeedback = StartCoroutine(RecordFeedback(verified));
    }

    private IEnumerator RecordFeedback(bool verified)
    {
        // The completion title is short enough to leave room for this stamp.
        if (verified) _stamp.text = "✓ 검증 완료";
        string original = _history.GetComponentInChildren<Text>().text;
        _history.GetComponentInChildren<Text>().text = "기록 +1";
        for (float t = 0f; t < 1.2f; t += Time.unscaledDeltaTime)
        {
            float pulse = Mathf.Sin(Mathf.Clamp01(t / .4f) * Mathf.PI);
            _history.transform.localScale = Vector3.one * (1f + .04f * pulse);
            if (verified) _stamp.transform.localScale = Vector3.one * (1f + .1f * pulse);
            yield return null;
        }
        _history.GetComponentInChildren<Text>().text = original;
        _history.transform.localScale = _stamp.transform.localScale = Vector3.one;
        _recordFeedback = null;
    }

    private void UpdateLayout()
    {
        _notebookRect.anchorMin = _notebookRect.anchorMax = Vector2.zero;
        _notebookRect.pivot = Vector2.zero;
        _notebookRect.anchoredPosition = new Vector2(16f, 16f);
    }

    private void EnsureUI()
    {
        _hasContent = true;
        if (_panel == null) _panel = GetComponent<CompoundSelectionPanel>();
        if (_root != null) return;
        _root = new GameObject("ExperimentNotebook", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 65;
        CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f);
        scaler.matchWidthOrHeight = 0.5f;
        var background = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(_root.transform, false);
        RectTransform rt = background.GetComponent<RectTransform>();
        background.AddComponent<ScreenSafePanel>();
        _notebookRect = rt;
        UpdateLayout();
        rt.sizeDelta = new Vector2(510f, 208f);
        background.GetComponent<Image>().color = new Color(0.02f, 0.055f, 0.09f, 0.96f);
        _title = Label(rt, "후보를 관찰하세요", 16, 12, 478, 25, 19);
        _stamp = Label(rt, "", 338, 12, 156, 25, 17);
        _stamp.alignment = TextAnchor.MiddleRight;
        _stamp.color = new Color(.25f, 1f, .8f);
        _detail = Label(rt, "", 16, 42, 478, 64, 16);
        _status = Label(rt, "", 16, 108, 478, 22, 15);
        _status.color = Color.cyan;
        for (int i = 0; i < _choices.Length; i++)
        {
            int index = i;
            _predictions.Add(MakeButton(rt, _choices[i], 16 + i * 112, 137, 106, 28,
                () => { _prediction = _choices[index]; RefreshButtons(); }));
        }
        MakeButton(rt, "◀", 358, 137, 62, 28, () => RotatePreview(-30));
        MakeButton(rt, "▶", 430, 137, 62, 28, () => RotatePreview(30));
        _history = MakeButton(rt, "실험 기록", 16, 173, 136, 27, () =>
        {
            _showHistory = !_showHistory;
            _historyPage = Mathf.Max(0, _records.Count - 1);
            _detail.text = _showHistory ? _records[_historyPage] :
                (_selected != null ? _selected.Data.subtitle : "다른 후보를 선택해 관찰할 수 있어요.");
            if (_showHistory) _status.text = "◀ ▶ 로 이전 실험 기록을 비교하세요";
            _detail.fontSize = _showHistory ? 14 : 16;
        });
        _start = MakeButton(rt, "실험 시작", 164, 173, 328, 27, StartSelected);
    }

    private void RotatePreview(float angle)
    {
        if (_showHistory && _records.Count > 0)
        {
            _historyPage = Mathf.Clamp(_historyPage + (angle > 0 ? 1 : -1), 0, _records.Count - 1);
            _detail.text = _records[_historyPage];
            _status.text = $"실험 기록 {_historyPage + 1} / {_records.Count} · ◀ ▶ 비교";
            return;
        }
        if (_selected != null && !_running && _panel.CanStartExperiment)
            _selected.MoleculeRoot.transform.Rotate(Vector3.up, angle, Space.Self);
    }

    private static Text Label(Transform parent, string value, float x, float y, float width, float height, int size)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Place(go.GetComponent<RectTransform>(), x, y, width, height);
        Text text = go.GetComponent<Text>();
        text.font = HoloFont.Resolve(); text.fontSize = size; text.text = value;
        text.color = Color.white; text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static Button MakeButton(Transform parent, string value, float x, float y, float width, float height, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(value, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Place(go.GetComponent<RectTransform>(), x, y, width, height);
        go.GetComponent<Image>().color = new Color(0.08f, 0.17f, 0.23f, 1f);
        Button button = go.GetComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        button.onClick.AddListener(action);
        Text label = Label(go.transform, value, 0, 0, width, height, 15);
        label.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    private static void Place(RectTransform rt, float x, float y, float width, float height)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(width, height);
    }
}
