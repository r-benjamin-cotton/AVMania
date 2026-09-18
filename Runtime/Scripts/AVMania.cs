#if UNITY_EDITOR || DEVELOPMENT_BUILD
#define AVMANIA_ENABLE_LOGMESSAGE
//#define AVMANIA_ENABLE_LOGWRITER
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

using Debug = UnityEngine.Debug;
using System.Linq;

namespace AVMania
{
    public delegate void OnAudioUpdateDelegate(float[] data, int channels);


    public enum TimeSource
    {
        AudioDSPTime,
        GameTime,
        UnscaledGameTime,
        Manual,
    }

    public enum AlphaSource
    {
        One,
        Zero,
        Bottom,
        Right,
#if false
        BottomHalf,
        RightHalf,
#endif
        Alpha,
        Chromakey,
        Track,
    }

    public enum ScalingMode
    {
        None,
        Vertical,
        Horizontal,
        Expand,
        Shrink,
    }

    public enum PathRoot
    {
        Direct,
        ApplicationRoot,
        Data,
        PersistentData,
        StreamingAssets,
        TemporaryCache,
        //AbsoluteURL,
    }

    public enum AudioRoute
    {
        None,
        AVListener,
        AVSource,
    }

    [Serializable]
    public class VideoTrackConfiguration
    {
        public bool enabled = true;
        public RenderTexture targetRenderTexture = null;
        public Renderer targetRenderer = null;
        public RawImage targetRawImage = null;
        public Material targetMaterial = null;
        public string targetProperty = "_MainTex";
        public ScalingMode scaling = ScalingMode.Vertical;
        public Color color = Color.white;
        public AlphaSource alphaSource = AlphaSource.One;
        public Color chromakey = Color.green;
        [Range(0.0f, 100.0f)]
        public float chromakeyParamA = 16.0f;
        [Range(0.01f, 100.0f)]
        public float chromakeyParamB = 16.0f;
        public int alphaTrack = 0;
    }

    [Serializable]
    public class AudioTrackConfiguration
    {
        public bool enabled = true;
        [Range(0, 1)]
        public float volume = 1;
        [Range(-1200, 1200)]
        public int pitch = 0;
        public AudioRoute audioRoute;
        public AVListener avListener;
        public AVSource avSource;
    }


    public enum VideoCodec : uint
    {
        Disable = 0,
        H264 = 1,
    }
    public enum AudioCodec : uint
    {
        Disable = 0,
        AAC = 1,
    }
    public enum VideoFormat : uint
    {
        Void = 0,
        L8 = 1,
        L16 = 2,
        D16 = 3,
        NV12 = 4,
        YUY2 = 5,
        AYUV = 6,
        RGB32 = 7,
        ARGB32 = 8,
    };
    public enum YUVMatrix : uint
    {
        Unknown = 0,
        BT709 = 1,
        BT601 = 2,
        SMPTE240M = 3,
    };
    public enum ChromaSiting : uint
    {
        Unknown = 0,
        Vertically_AlignedChromaPlanes = 0x1,
        Vertically_Cosited = 0x2,
        Horizontally_Cosited = 0x4,
        ProgressiveChroma = 0x8,
    };
    public enum NominalRange : uint
    {
        Unknown = 0,
        _0_255 = 1,
        _16_235 = 2,
        _48_208 = 3,
        _64_127 = 4,
    };
    public enum TransferFunction : uint
    {
        Unknown = 0, // 709
        _10 = 1,
        _18 = 2,
        _20 = 3,
        _22 = 4,
        _709 = 5,
        _240M = 6,
        _sRGB = 7,
    };
    public enum ChannelMask : uint
    {
        FontLeft = 0x01,
        FrontRight = 0x02,
        FrontCenter = 0x04,
        LowFrequency = 0x08,
        BackLeft = 0x10,
        BackRight = 0x20,
        FrontLeftOfCenter = 0x40,
        FrontRightOfCenter = 0x80,
        BackCenter = 0x100,
        SideLeft = 0x200,
        SideRight = 0x400,
        TopCenter = 0x800,
        TopFrontLeft = 0x1000,
        TopFrontCenter = 0x2000,
        TopFrontRight = 0x4000,
        TopBackLeft = 0x8000,
        TopBackCenter = 0x10000,
        TopBackRight = 0x20000,
    };

    internal enum BlitFormat : uint
    {
        Y,
        NV12,
        YUY2,
        AYUV,
        ARGB,
    };

    [Serializable, StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct VideoRecorderSettings
    {
        public int width;
        public int height;
        public int frameRateNumerator;
        public int frameRateDenominator;
        public int bitRate;
        public override readonly string ToString()
        {
            var fps = (frameRateDenominator == 0) ? 0.0f : ((float)frameRateNumerator / frameRateDenominator);
            var str = $"size:{width}x{height} frameRate:{fps} bitRate:{bitRate}";
            return str;
        }
    }
    [Serializable, StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct AudioRecorderSettings
    {
        public int channelCount;    // 1, 2, 6
        public int sampleRate;      // 44100,48000
        public int bitRate;         // 96k,128k,160k,192k
        public override readonly string ToString()
        {
            var str = $"sampleRate:{sampleRate} channelCount:{channelCount} bitRate:{bitRate}";
            return str;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct VideoAttributes
    {
        public int offsetX;
        public int offsetY;
        public int areaX;
        public int areaY;
        public uint width;
        public uint height;
        public uint frameRateNumerator;
        public uint frameRateDenominator;
        public uint aspectRatioNumerator;
        public uint aspectRatioDenominator;
        public VideoFormat videoFormat;
        public YUVMatrix yuvMatrix;
        public ChromaSiting chromaSiting;
        public NominalRange nominalRange;
        public TransferFunction transferFunction;
        public override readonly string ToString()
        {
            var fps = (frameRateDenominator == 0) ? 0.0f : ((float)frameRateNumerator / frameRateDenominator);
            var aspect = (aspectRatioDenominator == 0) ? 0.0f : ((float)aspectRatioNumerator / aspectRatioDenominator);
            var str = $"size:{width}x{height} area:{offsetX},{offsetY},{areaX},{areaY} frameRate:{fps} aspectRatio:{aspect} videoFormat:{videoFormat} yuvMatrix:{yuvMatrix} nominalRange:{nominalRange} transferFunction:{transferFunction}";
            return str;
        }
    };
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct AudioAttributes
    {
        public uint numChannels;
        public uint samplesPerSecond;
        public ChannelMask channelMask;
        public override readonly string ToString()
        {
            var str = $"numChannels:{numChannels} samplesPerSecond:{samplesPerSecond} channelMask:{channelMask}";
            return str;
        }
    };

    internal static class AVMania
    {
        [Flags]
        internal enum AVPlayerState : uint
        {
            Failed = 1U << 0,
            Finished = 1U << 1,
            Prepared = 1U << 2,
            Seeking = 1U << 3,
            Paused = 1U << 4,
            FrameReady = 1U << 8,
            TypeChanged = 1U << 9,
            Invalid = 1U << 31,
        };

        [Flags]
        internal enum AVRecorderState : uint
        {
            Failed = 1U << 0,
            Ready = 1U << 1,
            Invalid = 1U << 31,
        };

        [Flags]
        internal enum AVCaptureState : uint
        {
            Failed = 1U << 0,
            Active = 1U << 1,
            FrameReady = 1U << 8,
            TypeChanged = 1U << 9,
            Invalid = 1U << 31,
        };

        [DllImport("AVMania", CharSet = CharSet.Unicode)]
        private static extern void AVManiaActivateMessageLog();

        [DllImport("AVMania", CharSet = CharSet.Unicode)]
        private static extern string AVManiaGetMessageLog();



        [DllImport("AVMania")]
        internal static extern bool AVPlayerInitialize();
        [DllImport("AVMania")]
        internal static extern bool AVPlayerRelease();

        [DllImport("AVMania", CharSet = CharSet.Unicode)]
        internal static extern uint AVPlayerOpen(string url);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerClose(uint id);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerPause(uint id, bool pause);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerLoop(uint id, bool loop);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerAccurately(uint id, bool accuracy);
        [DllImport("AVMania")]
        internal static extern double AVPlayerGetDuration(uint id);
        [DllImport("AVMania")]
        internal static extern double AVPlayerGetPosition(uint id);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerSeek(uint id, double time);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerIsUpdating(uint id);
        [DllImport("AVMania")]
        internal static extern AVPlayerState AVPlayerUpdate(uint id, double time);


        [DllImport("AVMania")]
        internal static extern uint AVPlayerGetVideoTrackCount(uint id);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerSetVideoFormat(uint id, uint track, VideoFormat videoFormat);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerEnableVideoTrack(uint id, uint track, bool enable);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerGetVideoAttributes(uint id, uint track, out VideoAttributes attr);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerIsFrameReady(uint id, uint track);
        [DllImport("AVMania")]
        internal static extern uint AVPlayerGetTextureUpdateId(uint id, uint track);
        [DllImport("AVMania")]
        internal static extern IntPtr AVPlayerGetTextureUpdateCallback();


        [DllImport("AVMania")]
        internal static extern uint AVPlayerGetAudioTrackCount(uint id);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerEnableAudioTrack(uint id, uint track, bool enable);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerGetAudioAttributes(uint id, uint track, out AudioAttributes attr);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerSetupAudioBuffer(uint id, uint track, uint p0, uint p1, uint p2, uint p3, uint p4);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerSetVolume(uint id, uint track, float volume);
        [DllImport("AVMania")]
        internal static extern bool AVPlayerSetPitch(uint id, uint track, int pitch);
        [DllImport("AVMania")]
        internal static extern IntPtr AVPlayerGetSamples(uint id, uint track, uint channels, uint length);



        [DllImport("AVMania")]
        internal static extern bool AVRecorderInitialize();
        [DllImport("AVMania")]
        internal static extern bool AVRecorderRelease();
        [DllImport("AVMania", CharSet = CharSet.Unicode)]
        internal static extern uint AVRecorderCreate(string path, VideoCodec videoCodec, VideoRecorderSettings videoSettings, AudioCodec audioCodec, AudioRecorderSettings audioSettings);
        [DllImport("AVMania")]
        internal static extern bool AVRecorderClose(uint id);
        [DllImport("AVMania")]
        internal static extern bool AVRecorderPutSamples(uint id, IntPtr data, uint length, uint channels);
        [DllImport("AVMania")]
        internal static extern IntPtr AVRecorderStageFrame(uint id);
        [DllImport("AVMania")]
        internal static extern bool AVRecorderCommitFrame(uint id);
        [DllImport("AVMania")]
        internal static extern bool AVRecorderDiscardFrame(uint id);
        [DllImport("AVMania")]
        internal static extern AVRecorderState AVRecorderGetStatus(uint id);




        [DllImport("AVMania")]
        internal static extern bool AVCaptureInitialize();
        [DllImport("AVMania")]
        internal static extern bool AVCaptureRelease();
        [DllImport("AVMania")]
        internal static extern bool AVCaptureIsReady();

        [DllImport("AVMania")]
        internal static extern uint AVCaptureGetAudioDeviceCount();
        [DllImport("AVMania", CharSet = CharSet.Unicode)]
        internal static extern string AVCaptureGetAudioDeviceName(uint device);
        [DllImport("AVMania")]
        internal static extern uint AVCaptureGetAudioStreamCount(uint device);
        [DllImport("AVMania")]
        internal static extern bool AVCaptureGetAudioStreamDescriptor(uint device, uint stream, uint index, out AudioAttributes attr);
        [DllImport("AVMania")]
        internal static extern bool AVCaptureSetupAudioStream(uint device, uint stream, bool enable, uint length, uint frequency, uint overlap, uint wlimit, uint rlimit);
        [DllImport("AVMania")]
        internal static extern bool AVCaptureSetVolume(uint device, uint stream, float volume);
        [DllImport("AVMania")]
        internal static extern bool AVCaptureSetPitch(uint device, uint stream, int pitch);
        [DllImport("AVMania")]
        internal static extern bool AVCaptureGetAudioAttributes(uint device, uint stream, out AudioAttributes attr);
        [DllImport("AVMania")]
        internal static extern IntPtr AVCaptureGetSamples(uint device, uint stream, uint channels, uint length);
        [DllImport("AVMania")]
        internal static extern bool AVCaptureStartAudio(uint device);
        [DllImport("AVMania")]
        internal static extern bool AVCaptureStopAudio(uint device);
        [DllImport("AVMania")]
        internal static extern AVCaptureState AVCaptureGetAudioStatus(uint device);

        [DllImport("AVMania")]
        internal static extern uint AVCaptureGetVideoDeviceCount();
        [DllImport("AVMania", CharSet = CharSet.Unicode)]
        internal static extern string AVCaptureGetVideoDeviceName(uint device);
        [DllImport("AVMania")]
        internal static extern uint AVCaptureGetVideoStreamCount(uint device);
        [DllImport("AVMania")]
        internal static extern bool AVCaptureGetVideoStreamDescriptor(uint device, uint stream, uint index, out VideoAttributes attr);
        [DllImport("AVMania")]
        internal static extern bool AVCaptureSetupVideoStream(uint device, uint stream, bool enable, VideoFormat format, uint width, uint height, float fps);
        [DllImport("AVMania")]
        internal static extern bool AVCaptureGetVideoAttributes(uint device, uint stream, out VideoAttributes attr);
        [DllImport("AVMania")]
        internal static extern bool AVCaptureIsFrameReady(uint device, uint stream);
        [DllImport("AVMania")]
        internal static extern IntPtr AVCaptureLockImage(uint device, uint stream, uint width, uint height, uint bpp);
        [DllImport("AVMania")]
        internal static extern void AVCaptureUnlockImage(uint device, uint stream);
        [DllImport("AVMania")]
        internal static extern uint AVCaptureGetTextureUpdateId(uint device, uint stream);
        [DllImport("AVMania")]
        internal static extern IntPtr AVCaptureGetTextureUpdateCallback();
        [DllImport("AVMania")]
        internal static extern bool AVCaptureStartVideo(uint device);
        [DllImport("AVMania")]
        internal static extern bool AVCaptureStopVideo(uint device);
        [DllImport("AVMania")]
        internal static extern AVCaptureState AVCaptureUpdateVideo(uint device);


        private const string blitShaderName = "Hidden/AVManiaVideo";
        private static Material blitMaterial = null;

        internal static TextureFormat GetTextureFormat(VideoFormat videoFormat)
        {
            switch (videoFormat)
            {
                case VideoFormat.L8:
                    return TextureFormat.R8;
                case VideoFormat.L16:
                case VideoFormat.D16:
                    return TextureFormat.R16;
                default:
                case VideoFormat.NV12:
                    return TextureFormat.R8;
                case VideoFormat.YUY2:
                    return TextureFormat.RG16;
                case VideoFormat.AYUV:
                case VideoFormat.RGB32:
                case VideoFormat.ARGB32:
                    return TextureFormat.RGBA32;
            }
        }
        internal static int GetTextureSize(VideoAttributes attr, out int texWidth, out int texHeight)
        {
            switch (attr.videoFormat)
            {
                default:
                case VideoFormat.Void:
                    texWidth = 0;
                    texHeight = 0;
                    return 0;
                case VideoFormat.L8:
                    texWidth = (int)attr.width;
                    texHeight = (int)attr.height;
                    return 1;
                case VideoFormat.L16:
                case VideoFormat.D16:
                    texWidth = (int)attr.width;
                    texHeight = (int)attr.height;
                    return 2;
                case VideoFormat.NV12:
                    texWidth = (int)attr.width;
                    texHeight = (int)attr.height * 3 / 2;
                    return 1;
                case VideoFormat.YUY2:
                    texWidth = (int)attr.width;
                    texHeight = (int)attr.height;
                    return 2;
                case VideoFormat.AYUV:
                case VideoFormat.RGB32:
                case VideoFormat.ARGB32:
                    texWidth = (int)attr.width;
                    texHeight = (int)attr.height;
                    return 4;
            }
        }
        internal static BlitFormat GetBlitFormat(VideoFormat videoFormat)
        {
            switch (videoFormat)
            {
                default:
                case VideoFormat.Void:
                    return BlitFormat.NV12;
                case VideoFormat.L8:
                case VideoFormat.L16:
                case VideoFormat.D16:
                    return BlitFormat.Y;
                case VideoFormat.NV12:
                    return BlitFormat.NV12;
                case VideoFormat.YUY2:
                    return BlitFormat.YUY2;
                case VideoFormat.AYUV:
                    return BlitFormat.AYUV;
                case VideoFormat.RGB32:
                case VideoFormat.ARGB32:
                    return BlitFormat.ARGB;
            }
        }

        internal static Material GetUniqueBlitMaterial(Material material)
        {
            if (blitMaterial == null)
            {
                if ((material != null) && (material.shader != null) && material.shader.name.Equals(blitShaderName))
                {
                    blitMaterial = material;
                }
                else
                {

                    var shader = Shader.Find(blitShaderName);
                    if (shader == null)
                    {
                        LogError($"\"{blitShaderName}\" shader not found\nplease add to ProjectSettings/Graphics/AlwaysIncludeShaders");
                    }
                    blitMaterial = new Material(shader);
                }
            }
            return blitMaterial;
        }

        internal static void Destroy(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(obj);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        public static string GetPath(PathRoot pathRoot, string path)
        {
            switch (pathRoot)
            {
                case PathRoot.Direct:
                default:
                    return path;
                case PathRoot.ApplicationRoot:
                    return Path.Combine(Application.dataPath, "..", path);
                case PathRoot.Data:
                    return Path.Combine(Application.dataPath, path);
                case PathRoot.PersistentData:
                    return Path.Combine(Application.persistentDataPath, path);
                case PathRoot.StreamingAssets:
                    return Path.Combine(Application.streamingAssetsPath, path);
                case PathRoot.TemporaryCache:
                    return Path.Combine(Application.temporaryCachePath, path);
#if false
                case PathRoot.AbsoluteURL:
                    return Path.Combine(Application.absoluteURL, path);
#endif
            }
        }

        internal static Texture2D whiteTexture = null;

        private static void SetupStaticTexture()
        {
            var texture = new Texture2D(64, 96, TextureFormat.R8, false, true);
            texture.hideFlags = HideFlags.DontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Point;
            texture.name = "AVPlayer.WhiteTexture";
            whiteTexture = texture;
            FillTexture(texture, 235);
        }
        private static void ReleaseStaticTexture()
        {
            Destroy(whiteTexture);
            whiteTexture = null;
        }
        private static void FillTexture(Texture2D texture, byte y)
        {
            // nv12
            var len = texture.width * texture.height;
            var buf = new Color32[len];
            var yy = new Color32(y, 0, 0, 0);
            for (int j = 0, end = len * 2 / 3; j < end; j++)
            {
                buf[j] = yy;
            }
            var uv = new Color32(128, 0, 0, 0);
            for (int j = len * 2 / 3; j < len; j++)
            {
                buf[j] = uv;
            }
            texture.SetPixels32(0, 0, texture.width, texture.height, buf, 0);
            texture.Apply();
        }

        private static int initCount = 0;
#if AVMANIA_ENABLE_LOGWRITER
        private static StreamWriter streamWriter = null;
        private static DateTime dateTime = default;
        private static void OnLogMessageReceived(string condition, string stackTrace, UnityEngine.LogType type)
        {
            if (streamWriter == null)
            {
                return;
            }
            lock (streamWriter)
            {
                var t0 = DateTime.Now;
                if ((type == LogType.Log) || (type == LogType.Warning))
                {
                    streamWriter.WriteLine($"{Mathf.Min(999, (t0 - dateTime).Milliseconds):d3}: {type}: {condition}");
                }
                else
                {
                    streamWriter.WriteLine($"{Mathf.Min(999, (t0 - dateTime).Milliseconds):d3}: {type}: {condition}\n{stackTrace}");
                }
                dateTime = t0;
            }
        }
#endif
        internal static void Initialize()
        {
            //Debug.Log($"AVMania::Initialize() initCount={initCount}");
            if (initCount++ == 0)
            {
#if AVMANIA_ENABLE_LOGMESSAGE
#if AVMANIA_ENABLE_LOGWRITER
                dateTime = DateTime.Now;
                var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "log.txt"));
                //Debug.Log(path);
                streamWriter = new StreamWriter(path, false, System.Text.Encoding.UTF8);
                Application.logMessageReceivedThreaded += OnLogMessageReceived;
#endif
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.EditorApplication.update += DumpLogMessage;
                }
                else
#endif
                {
                    Application.onBeforeRender += DumpLogMessage;
                }
                AVManiaActivateMessageLog();
#endif
                SetupStaticTexture();
            }
        }
        internal static void Release()
        {
            //Debug.Log($"AVMania::Release() initCount={initCount}");
            if (--initCount == 0)
            {
                ReleaseStaticTexture();
#if AVMANIA_ENABLE_LOGMESSAGE
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.EditorApplication.update -= DumpLogMessage;
                }
                else
#endif
                {
                    Application.onBeforeRender -= DumpLogMessage;
                }
                DumpLogMessage();
#if AVMANIA_ENABLE_LOGWRITER
                Application.logMessageReceivedThreaded -= OnLogMessageReceived;
                streamWriter?.Close();
                streamWriter?.Dispose();
                streamWriter = null;
#endif
#endif
            }
        }
#if !AVMANIA_ENABLE_LOGMESSAGE
        [Conditional("XXXAVMANIA_ENABLE_LOGMESSAGEXXX")]
#endif
        public static void DumpLogMessage()
        {
            var msg = AVManiaGetMessageLog();
            if (!string.IsNullOrEmpty(msg))
            {
                Debug.Log(msg);
            }
        }

#if !AVMANIA_ENABLE_LOGMESSAGE
        [Conditional("XXXAVMANIA_ENABLE_LOGMESSAGEXXX")]
#endif
        public static void Assert(bool condition)
        {
            UnityEngine.Debug.Assert(condition);
        }
#if !AVMANIA_ENABLE_LOGMESSAGE
        //[Conditional("XXXAVMANIA_ENABLE_LOGMESSAGEXXX")]
#endif
        public static void LogError(object message)
        {
            UnityEngine.Debug.LogError(message);
        }
#if !AVMANIA_ENABLE_LOGMESSAGE
        [Conditional("XXXAVMANIA_ENABLE_LOGMESSAGEXXX")]
#endif
        public static void LogWarning(object message)
        {
            UnityEngine.Debug.LogWarning(message);
        }
#if !AVMANIA_ENABLE_LOGMESSAGE
        [Conditional("XXXAVMANIA_ENABLE_LOGMESSAGEXXX")]
#endif
        public static void Log(object message)
        {
            UnityEngine.Debug.Log(message);
        }
    }

    internal static class LocakKeywordMap<T> where T : Enum
    {
        private static readonly Dictionary<int, Dictionary<T, LocalKeyword>> dict = new();
        public static LocalKeyword Keyword(Shader shader, T value)
        {
            if (shader == null)
            {
                return default;
            }
            var id = shader.GetInstanceID();
            if (!dict.TryGetValue(id, out Dictionary<T, LocalKeyword> map))
            {
                map = new();
                dict.Add(id, map);
            }
            if (!map.TryGetValue(value, out LocalKeyword lk))
            {
                var typeName = typeof(T).Name;
                var kw = (typeName + value.ToString()).Replace("_", "");
                lk = shader.keywordSpace.keywords.FirstOrDefault((t) => t.name.Replace("_", "").Equals(kw, StringComparison.OrdinalIgnoreCase));
                map.Add(value, lk);
            }
            return lk;
        }
        public static LocalKeyword Keyword(Material material, T value)
        {
            if (material == null)
            {
                return default;
            }
            return Keyword(material.shader, value);
        }
    }

    internal static class AVColorUtil
    {
        public static Vector3 Color2Lab(Color col)
        {
            var xn = 95.05f / 100.0f;
            var yn = 100.00f / 100.0f;
            var zn = 108.90f / 100.0f;
            var rgb = new Vector3(col.r, col.g, col.b);
            var x = Vector3.Dot(rgb, new Vector3(0.4124f / xn, 0.3576f / xn, 0.1805f / xn));
            var y = Vector3.Dot(rgb, new Vector3(0.2126f / yn, 0.7152f / yn, 0.0722f / yn));
            var z = Vector3.Dot(rgb, new Vector3(0.0193f / zn, 0.1192f / zn, 0.9505f / zn));
            var s = 6.0f / 29.0f;
            var sss = s * s * s;
            var iss3 = 1.0f / (3.0f * s * s);
            var fx = (x > sss) ? Mathf.Pow(x, 1.0f / 3.0f) : ((x * iss3) + (4.0f / 29.0f));
            var fy = (y > sss) ? Mathf.Pow(y, 1.0f / 3.0f) : ((y * iss3) + (4.0f / 29.0f));
            var fz = (z > sss) ? Mathf.Pow(z, 1.0f / 3.0f) : ((z * iss3) + (4.0f / 29.0f));
            var lab = new Vector3((116.0f * fy) - 16.0f, 500.0f * (fx - fy), 200.0f * (fy - fz));
            return lab;
        }
        public static Color Lab2Color(Vector3 lab)
        {
            var ll = (lab.x + 16.0f) * (1.0f / 116.0f);
            var fx = ll + lab.y * (1.0f / 500.0f);
            var fy = ll;
            var fz = ll - lab.z * (1.0f / 200.0f);
            var s = 6.0f / 29.0f;
            var ss3 = 3.0f * s * s;
            Vector3 xyz;
            xyz.x = (fx > s) ? Mathf.Pow(fx, 3.0f) : ((fx - (4.0f / 29.0f)) * ss3);
            xyz.y = (fy > s) ? Mathf.Pow(fy, 3.0f) : ((fy - (4.0f / 29.0f)) * ss3);
            xyz.z = (fz > s) ? Mathf.Pow(fz, 3.0f) : ((fz - (4.0f / 29.0f)) * ss3);
            var xn = 95.05f / 100.0f;
            var yn = 100.00f / 100.0f;
            var zn = 108.90f / 100.0f;
            Color rgb;
            rgb.r = Vector3.Dot(xyz, new Vector3(+3.2406f * xn, -1.5372f * yn, -0.4986f * zn));
            rgb.g = Vector3.Dot(xyz, new Vector3(-0.9689f * xn, +1.8758f * yn, +0.0415f * zn));
            rgb.b = Vector3.Dot(xyz, new Vector3(+0.0557f * xn, -0.2040f * yn, +1.0570f * zn));
            rgb.a = 1;
            return rgb;
        }
    }
    internal class DbgWaveWriter : IDisposable
    {
        private bool disposed = false;
        private FileStream fileStream;
        private BinaryWriter binaryWriter;
        private readonly long dataPos;

        public static int AudioChannels
        {
            get
            {
                switch (AudioSettings.speakerMode)
                {
                    default:
                        return 2;
                    case AudioSpeakerMode.Mono:
                        return 1;
                    case AudioSpeakerMode.Stereo:
                        return 2;
                    case AudioSpeakerMode.Quad:
                        return 4;
                    case AudioSpeakerMode.Surround:
                        return 5;
                    case AudioSpeakerMode.Mode5point1:
                        return 6;
                    case AudioSpeakerMode.Mode7point1:
                        return 8;
                    case AudioSpeakerMode.Prologic:
                        return 2;
                }
            }
        }
        public DbgWaveWriter(string path, int freq, int channels)
        {
            fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            binaryWriter = new BinaryWriter(fileStream);

            binaryWriter.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            binaryWriter.Write((UInt32)(4 + 4 + 4 + 2 + 2 + 4 + 4 + 2 + 2 + 4 + 4 + 0 * 2 * 2));   // size

            binaryWriter.Write(new byte[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
            binaryWriter.Write(new byte[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            binaryWriter.Write((UInt32)16);                 // fmt size
            binaryWriter.Write((UInt16)3);                  // format 3:32bit float
            binaryWriter.Write((UInt16)channels);           // channels

            binaryWriter.Write((UInt32)freq);               // samples per sec
            binaryWriter.Write((UInt32)(4 * channels * freq));// bytes per sec
            binaryWriter.Write((UInt16)(4 * channels));     // bytes per sample * channels
            binaryWriter.Write((UInt16)32);                 // bits per sample

            binaryWriter.Write(new byte[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            binaryWriter.Write((UInt32)0);
            dataPos = binaryWriter.Seek(0, SeekOrigin.Current);
        }
        public void Dispose()
        {
            Dispose(true);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    Close();
                }
                disposed = true;
            }
        }
        public void Close()
        {
            if (fileStream != null)
            {
                long len = binaryWriter.Seek(0, SeekOrigin.Current);
                binaryWriter.Seek(4, SeekOrigin.Begin);
                binaryWriter.Write((UInt32)(len - 8));
                binaryWriter.Seek((int)(dataPos - 4), SeekOrigin.Begin);
                binaryWriter.Write((UInt32)(len - dataPos));
                binaryWriter.Close();
                binaryWriter = null;
                fileStream.Close();
                fileStream = null;
            }
        }
        public void Write(ReadOnlySpan<float> stereoSamples)
        {
            for (int i = 0, end = stereoSamples.Length; i < end; i++)
            {
                var v0 = stereoSamples[i];
                binaryWriter.Write(v0);
            }
        }
    }
}
