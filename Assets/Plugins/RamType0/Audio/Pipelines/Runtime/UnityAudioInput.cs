using Cysharp.Threading.Tasks;
using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Assertions;
using Unity.Burst;
using System.Threading;

namespace RamType0.Audio.Pipelines
{
    public static class UnityAudioInput
    {
        public static PipeReader CreateMicrophoneAudioReader(string deviceName, int sampleRate, int bufferLengthSec = 1)
        {
            var pipe = new Pipe();
            RunCaptureTask(deviceName,sampleRate,bufferLengthSec, pipe.Writer).Forget();
            return pipe.Reader;
        }
        static async UniTaskVoid RunCaptureTask(string deviceName, int sampleRate, int bufferLengthSec, PipeWriter pipeWriter)
        {
            Exception exception = null;
            try
            {
                await UniTask.SwitchToMainThread();
                var clip = Microphone.Start(deviceName, true, bufferLengthSec, sampleRate);
                while (Microphone.GetPosition(deviceName) <= 0)
                {
                    await UniTask.Yield();
                }
                FlushResult flushResult;
                var previousPos = 0;
                float[] buffer = new float[clip.samples];//AudioClip.GetData raises warning for longer buffer
                do
                {
                    var pos = Microphone.GetPosition(deviceName);
                    bool looped = pos < previousPos;//Can be wrong after long freeze
                    if (looped)
                    {
                        //buffer = ArrayPool<float>.Shared.Rent(clip.samples);
                        clip.GetData(buffer, 0);
                        pipeWriter.Write(MemoryMarshal.AsBytes(buffer.AsSpan(previousPos, clip.samples - previousPos)));
                        pipeWriter.Write(MemoryMarshal.AsBytes(buffer.AsSpan(0, pos)));
                    }
                    else
                    {
                        var newDataLength = pos - previousPos;
                        //buffer = ArrayPool<float>.Shared.Rent(newDataLength);
                        clip.GetData(buffer, previousPos);
                        pipeWriter.Write(MemoryMarshal.AsBytes(buffer.AsSpan(0, newDataLength)));
                    }
                    previousPos = pos;
                    flushResult = await pipeWriter.FlushAsync().ConfigureAwait(false);
                    Assert.IsFalse(flushResult.IsCanceled);
                    await UniTask.Yield();
                } while (!flushResult.IsCompleted);
            }
            catch (Exception e)
            {
                exception = e;
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                Microphone.End(deviceName);
                pipeWriter.Complete(exception);
            }
        }
    }

    public static class AudioSequence
    {
        public static float ComputeAudioLevelFloat32(ReadOnlySequence<byte> audioData)
        {
            float volume = 0;
            var index = 0;
            foreach (var memory in audioData)
            {
                var span = MemoryMarshal.Cast<byte, float>(memory.Span);
                ref var r = ref MemoryMarshal.GetReference(span);
                for (int i = 0; i < span.Length; i++)
                {
                    volume += (Math.Abs(Unsafe.Add(ref r, i)) - volume) / ++index;
                    //volume += (Math.Abs(span[i]) - volume) / ++index;
                }

            }
            return volume;
        }
        [BurstCompile]
        unsafe struct ComputeSpanVolumeJob: IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public float* VolumePtr;
            [NativeDisableUnsafePtrRestriction]
            public int* IndexPtr;
            [NativeDisableUnsafePtrRestriction,ReadOnly]
            public float* SpanPtr;
            [ReadOnly]
            public int SpanLength;

            private ref float Volume => ref *VolumePtr;
            private ref int Index => ref *IndexPtr;
            public void Execute()
            {
                for (int i = 0; i < SpanLength; i++)
                {
                    Volume += (Math.Abs(SpanPtr[i]) - Volume) / ++Index;
                }
            }
        }
    }

}