using Cysharp.Threading.Tasks;
using RamType0.Audio.Pipelines;
using System.Collections;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class MicrophoneAudioLevelWindow : EditorWindow
{
    [MenuItem("Window/MicrophoneAudioLevel")]
    public static void ShowWindow()
    {
        var display = GetWindow<MicrophoneAudioLevelWindow>();
        display.titleContent = new GUIContent("MicrophoneAudioLevel");
    }

    [SerializeField] VisualTreeAsset visualTreeAsset = default;

    CancellationTokenSource cts;
    void OnEnable()
    {
        
        cts = new CancellationTokenSource();
        foreach (var deviceName in Microphone.devices)
        {
            ShowAudioLevel(deviceName,cts.Token).Forget();
        }
        
    }
    void OnDisable()
    {
        cts.Cancel();
    }

    async UniTaskVoid ShowAudioLevel(string deviceName,CancellationToken cancellationToken)
    {
        await UniTask.SwitchToMainThread();
        
        Microphone.GetDeviceCaps(deviceName, out var minFreq, out var maxFreq);
        var freq = (minFreq == 0 & maxFreq == 0) ? 48000 : maxFreq;

        var bufferLength = sizeof(float) * freq * 2;
        var pipeReader = UnityAudioInput.CreateMicrophoneAudioReader(deviceName,freq);
        var root = rootVisualElement;
        var element = visualTreeAsset.Instantiate();
        var label = element.Q<Label>(); //new Label(deviceName);
        label.text = deviceName;
        var audioLevel = element.Q<ProgressBar>();
        root.Add(element);
        try
        {
            while (true)
            {
                var readResult = await pipeReader.ReadAsync(cancellationToken).ConfigureAwait(false);
                var buffer = readResult.Buffer;
                var volume = AudioSequence.ComputeAudioLevelFloat32(buffer);
                
                buffer = buffer.Length > bufferLength ? buffer.Slice(bufferLength) : buffer;
                pipeReader.AdvanceTo(buffer.Start,buffer.End);
                await UniTask.SwitchToMainThread();
                audioLevel.value = volume;
                audioLevel.title = volume.ToString();
            }
        }
        finally
        {
            pipeReader.Complete();
        }
        
        
    }
}
