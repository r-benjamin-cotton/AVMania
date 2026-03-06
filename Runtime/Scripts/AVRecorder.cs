using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace AVMania
{
    public class AVRecorder : MonoBehaviour
    {
        [SerializeField]
        private PathRoot pathRoot = PathRoot.PersistentData;

        [SerializeField]
        private string path = "";

        [SerializeField]
        private bool recordVideo = true;
        [SerializeField]
        private int width = 0;
        [SerializeField]
        private int height = 0;
        [SerializeField]
        private int frameRateNumerator = 30;
        [SerializeField]
        private int frameRateDenominator = 1;
        [SerializeField]
        private int videoBitRate = 10000000;

        [SerializeField]
        private bool recordAudio = true;
        [SerializeField]
        private int channelCount = 2;
        [SerializeField]
        private int sampleRate = 48000;
        [SerializeField]
        private int audioBitRate = 192000;

        [SerializeField]
        private bool recordOnAwake = false;
        [SerializeField]
        private float duration = 0;
        [SerializeField]
        private Texture sourceTexture = null;
        [SerializeField]
        private AudioRoute audioRoute = AudioRoute.None;
        [SerializeField]
        private AVListener avListener = null;
        [SerializeField]
        private AVSource avSource = null;

        [SerializeField]
        private bool manual = false;


        private struct State
        {
            public uint id;
            public VideoCodec videoCodec;
            public AudioCodec audioCodec;
            public VideoRecorderSettings videoSettings;
            public AudioRecorderSettings audioSettings;
        }
        private bool started = false;

        private State state = new();
        private Coroutine coroutine = null;

        private AsyncGPUReadbackRequest request = default;


        public PathRoot PathRoot
        {
            get { return pathRoot; }
            set
            {
                StopRecording();
                pathRoot = value;
            }
        }
        public string Path
        {
            get { return path; }
            set
            {
                StopRecording();
                path = value;
            }
        }
        public bool RecordVideo
        {
            get { return recordVideo; }
            set
            {
                StopRecording();
                recordVideo = value;
            }
        }
        public bool RecordAudio
        {
            get { return recordAudio; }
            set
            {
                StopRecording();
                recordAudio = value;
            }
        }
        public int Width
        {
            get { return width; }
            set
            {
                StopRecording();
                width = value;
            }
        }
        public int Height
        {
            get { return height; }
            set
            {
                StopRecording();
                height = value;
            }
        }
        public int FrameRateNumerator
        {
            get { return frameRateNumerator; }
            set
            {
                StopRecording();
                frameRateNumerator = value;
            }
        }
        public int FrameRateDenominator
        {
            get { return frameRateDenominator; }
            set
            {
                StopRecording();
                frameRateDenominator = value;
            }
        }
        public int VideoBitRate
        {
            get { return videoBitRate; }
            set
            {
                StopRecording();
                videoBitRate = value;
            }
        }
        public int ChannelCount
        {
            get { return channelCount; }
            set
            {
                StopRecording();
                channelCount = value;
            }
        }
        public int SampleRate
        {
            get { return sampleRate; }
            set
            {
                StopRecording();
                sampleRate = value;
            }
        }
        public int AudioBitRate
        {
            get { return audioBitRate; }
            set
            {
                StopRecording();
                audioBitRate = value;
            }
        }
        public bool Manual
        {
            get { return manual; }
            set
            {
                StopRecording();
                manual = value;
            }
        }
        public Texture SourceTexture
        {
            get { return sourceTexture; }
            set
            {
                StopRecording();
                sourceTexture = value;
            }
        }
        public AudioRoute AudioRoute
        {
            get { return audioRoute; }
            set
            {
                StopRecording();
                audioRoute = value;
            }
        }
        public AVListener AVListener
        {
            get { return avListener; }
            set
            {
                StopRecording();
                avListener = value;
            }
        }
        public AVSource AVSource
        {
            get { return avSource; }
            set
            {
                StopRecording();
                avSource = value;
            }
        }
        public bool IsRecording
        {
            get;
            private set;
        } = false;
        public bool IsFailed
        {
            get;
            private set;
        } = false;
        public bool IsReady
        {
            get;
            private set;
        } = false;

        public bool BeginRecording()
        {
            if (!manual)
            {
                AVMania.LogWarning("Not manual mode");
                return false;
            }
            return StartRecording();
        }
        public void EndRecording()
        {
            if (!manual)
            {
                AVMania.LogWarning("Not manual mode");
                return;
            }
            StopRecording();
        }
        public void UpdateStatus()
        {
            if (!IsRecording)
            {
                return;
            }
            var status = AVMania.AVRecorderGetStatus(state.id);
            if ((status & (AVMania.AVRecorderState.Failed | AVMania.AVRecorderState.Invalid)) != 0)
            {
                IsFailed = true;
            }
            if ((status & AVMania.AVRecorderState.Ready) != 0)
            {
                IsReady = true;
            }
        }
        public void AddFrame(Texture texture)
        {
            if (!manual)
            {
                AVMania.LogWarning("Not manual mode");
                return;
            }
            if (!IsRecording)
            {
                AVMania.LogWarning("Not recording");
                return;
            }
            if (!recordVideo)
            {
                AVMania.LogWarning("Not recordImage");
                return;
            }
            UpdateStatus();
            if (IsFailed)
            {
                AVMania.LogWarning("Failed");
                return;
            }
            if (!IsReady)
            {
                AVMania.LogWarning("Not ready");
                return;
            }
            PutFrame(texture);
        }
        public void AddSamples(float[] data, int channels)
        {
            if (!manual)
            {
                AVMania.LogWarning("Not manual mode");
                return;
            }
            if (!IsRecording)
            {
                AVMania.LogWarning("Not recording");
                return;
            }
            if (!recordAudio)
            {
                AVMania.LogWarning("Not recordAudio");
                return;
            }
            UpdateStatus();
            if (IsFailed)
            {
                AVMania.LogWarning("Failed");
                return;
            }
            if (!IsReady)
            {
                AVMania.LogWarning("Not ready");
                return;
            }
            PutSamples(data, channels);
        }

        private bool StartRecording()
        {
            if (IsRecording)
            {
                return false;
            }
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            if (recordVideo && !manual)
            {
                if (sourceTexture == null)
                {
                    AVMania.LogWarning($"souceTextur is null");
                    return false;
                }
                if (sourceTexture.graphicsFormat != UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm)
                {
                    AVMania.LogWarning($"souceTextur.graphicsFormat<{sourceTexture.graphicsFormat}> is not supported");
                    return false;
                }
                if (((width != 0) && (width != sourceTexture.width)) || ((height != 0) && (height != sourceTexture.height)))
                {
                    AVMania.LogWarning($"souceTextur.size<{sourceTexture.width}x{sourceTexture.height}> != <{width}x{height}>");
                    return false;
                }
                width = sourceTexture.width;
                height = sourceTexture.height;
            }
            if (recordAudio && !manual)
            {
                if (sampleRate != AudioSettings.outputSampleRate)
                {
                    AVMania.LogWarning($"sampleRate:{sampleRate} != AudioSettings.outputSampleRate:{AudioSettings.outputSampleRate}");
                    return false;
                }
                switch (audioRoute)
                {
                    case AudioRoute.None:
                        break;
                    case AudioRoute.AVListener:
                        if (avListener != null)
                        {
                            avListener.OnAudioUpdate += OnAudioUpdate;
                        }
                        break;
                    case AudioRoute.AVSource:
                        if (avSource != null)
                        {
                            avSource.OnAudioUpdate += OnAudioUpdate;
                        }
                        break;
                }
            }
            if (recordVideo)
            {
                state.videoCodec = VideoCodec.H264;
                state.videoSettings.width = width;
                state.videoSettings.height = height;
                state.videoSettings.frameRateDenominator = frameRateDenominator;
                state.videoSettings.frameRateNumerator = frameRateNumerator;
                state.videoSettings.bitRate = videoBitRate;
            }
            else
            {
                state.videoCodec = VideoCodec.Disable;
            }
            if (recordAudio)
            {
                state.audioCodec = AudioCodec.AAC;
                state.audioSettings.channelCount = channelCount;
                state.audioSettings.sampleRate = sampleRate;
                state.audioSettings.bitRate = audioBitRate;
            }
            else
            {
                state.audioCodec = AudioCodec.Disable;
            }
            var url = AVMania.GetPath(pathRoot, path);
            var id = AVMania.AVRecorderCreate(url, state.videoCodec, state.videoSettings, state.audioCodec, state.audioSettings);
            if (id == 0)
            {
                IsFailed = true;
                return false;
            }
            state.id = id;
            if (!manual)
            {
                coroutine = StartCoroutine(UpdateCoroutine());
            }
            IsRecording = true;
            return true;
        }
        private void StopRecording()
        {
            request.WaitForCompletion();
            IsReady = false;
            IsFailed = false;
            if (!IsRecording)
            {
                return;
            }
            AVMania.AVRecorderClose(state.id);
            state.id = 0;
            IsRecording = false;
            if (recordAudio && !manual)
            {
                switch (audioRoute)
                {
                    case AudioRoute.None:
                        break;
                    case AudioRoute.AVListener:
                        if (avListener != null)
                        {
                            avListener.OnAudioUpdate -= OnAudioUpdate;
                        }
                        break;
                    case AudioRoute.AVSource:
                        if (avSource != null)
                        {
                            avSource.OnAudioUpdate -= OnAudioUpdate;
                        }
                        break;
                }
            }
            if (coroutine != null)
            {
                StopCoroutine(coroutine);
            }
            coroutine = null;
        }

        private NativeArray<uint> readbackFrame;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle atomicSafetyHandle;
#endif

        private void ReadBackCallback(AsyncGPUReadbackRequest req)
        {
            if (req.hasError)
            {
                AVMania.LogWarning("AsyncGPUReadback failed..");
                AVMania.AVRecorderDiscardFrame(state.id);
            }
            else
            {
                AVMania.AVRecorderCommitFrame(state.id);
            }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(atomicSafetyHandle);
            AtomicSafetyHandle.Release(atomicSafetyHandle);
            atomicSafetyHandle = default;
#endif
            readbackFrame.Dispose();
            readbackFrame = default;
        }
        private void PutFrame(Texture src)
        {
            if ((src.width != state.videoSettings.width) || (src.height != state.videoSettings.height))
            {
                AVMania.LogWarning($"texture size not match {state.videoSettings.width}x{state.videoSettings.height} != {width}x{height}");
                return;
            }
            if (src.graphicsFormat != UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm)
            {
                AVMania.LogWarning($"graphicsFormat<{src.graphicsFormat}> is not supported");
                return;
            }
            request.WaitForCompletion();
            var ptr = AVMania.AVRecorderStageFrame(state.id);
            if (ptr == IntPtr.Zero)
            {
                AVMania.LogWarning($"can't put frame..");
                return;
            }
            AVMania.Assert(!readbackFrame.IsCreated);
            unsafe
            {
                var len = state.videoSettings.width * state.videoSettings.height;
                readbackFrame = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<uint>((uint*)ptr, len, Allocator.None);
            }
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            atomicSafetyHandle = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref readbackFrame, atomicSafetyHandle);
#endif

            request = AsyncGPUReadback.RequestIntoNativeArray(ref readbackFrame, src, 0, TextureFormat.BGRA32, ReadBackCallback);
        }

        private void PutSamples(float[] data, int channels)
        {
#if false
            if (state.audioSettings.channelCount != channels)
            {
                AVMania.LogWarning($"channel count not match {state.audioSettings.channelCount} != {channels}");
                return;
            }
#endif
            using var buf = new NativeArray<float>(data.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            buf.CopyFrom(data);
            unsafe
            {
                var ptr = (IntPtr)buf.GetUnsafeReadOnlyPtr();
                AVMania.AVRecorderPutSamples(state.id, ptr, (uint)buf.Length, (uint)channels);
            }
        }

        private static readonly YieldInstruction yieldInstruction = new WaitForEndOfFrame();

        private IEnumerator UpdateCoroutine()
        {
            yield return null;
            var t0 = Time.unscaledTimeAsDouble;
            var frame = -1L;
            for (; ; )
            {
                yield return yieldInstruction;

                UpdateStatus();
                if (IsFailed)
                {
                    AVMania.LogWarning("recording failed..");
                    yield break;
                }
                if (!IsReady)
                {
                    continue;
                }

                var time = Time.unscaledTimeAsDouble - t0;
                if (recordVideo)
                {
                    var num = state.videoSettings.frameRateNumerator;
                    var den = state.videoSettings.frameRateDenominator;
                    var fc = (uint)(time * num / den);
                    var dc = fc - frame;
                    //Debug.Log($"{fc} {dc}");
                    while (dc-- > 0)
                    {
                        PutFrame(sourceTexture);
                    }
                    frame = fc;
                }
                if ((duration != 0) && (time >= duration))
                {
                    //AVMania.Log($"recorded: {duration} {time} {frame}");
                    StopRecording();
                    yield break;
                }
            }
        }
        private void OnAudioUpdate(float[] data, int channels)
        {
            if (!recordAudio || !IsReady)
            {
                return;
            }
            PutSamples(data, channels);
        }


#if true
        // closeの後処理でAVRecorderReleaseがブロックする事があるので
        // アプリケーションの終了まで解放を遅らせる
        private static bool initialized = false;
        private void Awake()
        {
            AVMania.Initialize();
            AVMania.AVRecorderInitialize();

            if (!initialized)
            {
                initialized = true;
                AVMania.Initialize();
                AVMania.AVRecorderInitialize();
                Application.quitting += ApplicationOnQuitting;
            }
        }
        private static void ApplicationOnQuitting()
        {
            AVMania.AVRecorderRelease();
            AVMania.Release();
        }
#else
        private void Awake()
        {
            AVMania.Initialize();
            AVMania.AVRecorderInitialize();
        }
#endif
        private void OnDestroy()
        {
            AVMania.AVRecorderRelease();
            AVMania.Release();
        }
        private void OnEnable()
        {
            if (started && recordOnAwake)
            {
                StartRecording();
            }
        }
        private void Start()
        {
            started = true;
            if (recordOnAwake)
            {
                StartRecording();
            }
        }
        private void OnDisable()
        {
            {
                StopRecording();
            }
        }
    }
}
