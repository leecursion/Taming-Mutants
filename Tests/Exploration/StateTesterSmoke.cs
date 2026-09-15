using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

public static class StateTesterSmoke
{
    public static void Run(bool exploration)
    {
        var oldMode=InputSystem.settings.updateMode;
        var oldFocus=InputSystem.settings.editorInputBehaviorInPlayMode;
        var oldBackground=InputSystem.settings.backgroundBehavior;
        InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        var keyboard=InputSystem.AddDevice<Keyboard>();
        var root=new GameObject("Shortcut regression");
        var bubble=root.AddComponent<AIAssistantSpeechBubble>();
        var visual=root.AddComponent<AIAssistantVisual>();
        var tester=root.AddComponent<AIAssistantStateTester>();
        tester.enabled=false; // Invoke Update explicitly so the test controls each key frame.
        var update=typeof(AIAssistantStateTester).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
        Action press=()=>{
            InputSystem.QueueStateEvent(keyboard,new KeyboardState()); InputSystem.Update();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Space,Key.Digit2)); InputSystem.Update();
            keyboard.MakeCurrent();
            update.Invoke(tester,null);
        };
        GameObject eventRoot=null, field=null;
        try {
            press();
            if(bubble.messages!=0 || visual.changes!=0) throw new Exception("Default tester consumed Space or number key");
            tester.enableTestShortcuts=true;
            if(exploration) {
                press();
                if(bubble.messages!=0 || visual.changes!=0) throw new Exception("Tester injected sample into AI exploration");
            } else {
                press();
                if(bubble.messages!=1 || visual.changes!=1) throw new Exception("Explicit tester shortcuts stopped working: messages="+bubble.messages+", states="+visual.changes+", space="+keyboard.spaceKey.wasPressedThisFrame);
                var events=EventSystem.current;
                if(events==null) { eventRoot=new GameObject("Input focus regression"); events=eventRoot.AddComponent<EventSystem>(); }
                var previous=events.currentSelectedGameObject;
                field=new GameObject("Chat field",typeof(RectTransform),typeof(InputField));
                events.SetSelectedGameObject(field);
                press();
                events.SetSelectedGameObject(previous);
                if(bubble.messages!=1 || visual.changes!=1) throw new Exception("Typing triggered EGFR sample or state shortcut");
            }
        } finally {
            InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.updateMode=oldMode;
            InputSystem.settings.editorInputBehaviorInPlayMode=oldFocus;
            InputSystem.settings.backgroundBehavior=oldBackground;
            UnityEngine.Object.DestroyImmediate(field);
            UnityEngine.Object.DestroyImmediate(eventRoot);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
