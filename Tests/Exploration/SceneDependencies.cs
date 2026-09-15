using System.Collections.Generic;
using UnityEngine;
public class IntroDirector:MonoBehaviour { public Camera targetCamera; public AIAssistantBrain assistant; public QuestSelectionBoard board; public bool played; public int enteredStage,returnedToLab; public bool IsStageTransitioning { get; set; } public void PrepareModeSelection() { } public void Play() { played=true; } public void EnterExplorationStage() { enteredStage++; } public void ReturnToLabStage() { returnedToLab++; } }
public class QuestSelectionBoard:MonoBehaviour { public bool IsVisible=true; }
public class AIAssistantBrain:MonoBehaviour { public AIChatBackend client; public void SpeakNow(string text) {} public void SpeakSequence(IEnumerable<string> lines) {} public void ResetConversation() {} }
public class AIChatBackend:MonoBehaviour {}
public class AICoScientistClient:AIChatBackend { public bool IsConfigured=false; public string backendEndpoint,proxyToken; }
public enum AIAssistantState { Idle, Listening, Thinking, Speaking, Alert }
public class AIAssistantVisual:MonoBehaviour { public AIAssistantState CurrentState; public int changes; public void SetState(AIAssistantState state) { CurrentState=state; changes++; } }
public class AIAssistantSpeechBubble:MonoBehaviour { public int messages; public void Say(string message) { messages++; } }
