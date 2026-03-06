using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Timeline;
using UnityEngine.Rendering;

namespace AVMania
{
    public class AVPlayer : MonoBehaviour, ITimeControl
    {
        public delegate void OnTextureUpdateDelegate(int trackIndex, CommandBuffer commandBuffer, Texture2D texture, RenderTexture renderTexture);

        private const uint InvalidID = 0;

        [SerializeField]
        private PathRoot pathRoot = PathRoot.StreamingAssets;

        [SerializeField]
        private string path = "";

        [SerializeField]
        private bool playOnAwake = true;

        [SerializeField]
        private bool loop = false;

        [SerializeField]
        private float clipIn = 0;

        [SerializeField]
        private float clipOut = 0;

        [SerializeField, Range(0, 8)]
        private float playbackSpeed = 1.0f;

        [SerializeField]
        private Color color = Color.white;

        [SerializeField]
        private Color altColor = Color.black;

        [SerializeField, Range(0, 1)]
        private float volume = 1.0f;

        [SerializeField, Range(-1200, 1200)]
        private int pitch = 0;

        [SerializeField]
        private TimeSource timeSource = TimeSource.AudioDSPTime;

        [SerializeField, Delayed, Range(0.033f, 1.0f)]
        private float audioBuffer = 0.1f;

        [SerializeField]
        private VideoTrackConfiguration[] videoTracks = new VideoTrackConfiguration[1]
        {
            new(),
        };

        [SerializeField]
        private AudioTrackConfiguration[] audioTracks = new AudioTrackConfiguration[1]
        {
            new(),
        };

        [SerializeField]
        private bool accurately = true;

        [SerializeField, HideInInspector]
        private Material blitMaterial = null;

        private struct VideoTrackState
        {
            public VideoAttributes attributes;
            public TextureInfo info;
            public bool textureOwner;
            public Texture2D texture;
            public Vector4 region;
            public RenderTexture renderTexture;
            public Vector3 originalRendererScale;
            public Vector3 previousRendererScale;
            public Vector2 originalRawImageSize;
            public Vector2 previousRawImageSize;
            public OnTextureUpdateDelegate onTextureUpdate;
        }
        private struct AudioTrackState
        {
            public bool valid;
            public AudioAttributes attributes;
            public OnAudioUpdateDelegate onAudioUpdate;
            public AudioRoute activeRoute;
        }


        private string currentUrl = "";
        private bool starting = false;
        private bool setting = true;
        private bool repaint = false;
        private bool timeControl = false;
        private double referenceTime = 0;
        private double previousDSPTime = 0;
        private double currentTime = 0;
        private VideoTrackState[] videoTrackStates = null;
        private AudioTrackState[] audioTrackStates = null;

        private uint id = InvalidID;
        private uint videoTrackCount = 0;
        private uint audioTrackCount = 0;
        private AVMania.AVPlayerState avState = 0;

        private MaterialPropertyBlock propertyBlock = null;
        private CommandBuffer commandBuffer = null;
        private IntPtr textureUpdateCallback = IntPtr.Zero;

        private Coroutine coroutine = null;
#if UNITY_EDITOR
        private IEnumerator coroutineEd = null;
#endif

        private static readonly int _Color = Shader.PropertyToID("_Color");
        private static readonly int _MainTex_TexelRegion = Shader.PropertyToID("_MainTex_TexelRegion");
        private static readonly int _Chromakey = Shader.PropertyToID("_Chromakey");
        private static readonly int _ChromakeyLab = Shader.PropertyToID("_ChromakeyLab");
        private static readonly int _ChromakeyParam = Shader.PropertyToID("_ChromakeyParam");
        private static readonly int _Track = Shader.PropertyToID("_Track");
        private static readonly int _Track_TexelRegion = Shader.PropertyToID("_Track_TexelRegion");

        private const double seekThresholdTime = 0.3;

        private void SetupMaterial()
        {
            blitMaterial = AVMania.GetUniqueBlitMaterial(blitMaterial);
        }

        private void Reset()
        {
            videoTracks = null;
            audioTracks = null;
            PrepareVideoTracks(0);
            PrepareAudioTracks(0);
            videoTracks[0].enabled = true;
            videoTracks[0].targetRawImage = GetComponent<RawImage>();
            videoTracks[0].targetRenderer = GetComponent<Renderer>();
            audioTracks[0].enabled = true;
            audioTracks[0].avSource = GetComponent<AVSource>();
            audioTracks[0].avListener = FindObjectOfType<AVListener>();
            audioTracks[0].audioRoute = (audioTracks[0].avSource != null) ? AudioRoute.AVSource : ((audioTracks[0].avListener != null) ? AudioRoute.AVListener : AudioRoute.None);

            SetupMaterial();
        }

        private void OnValidate()
        {
            SetupMaterial();
            if (IsPrepared)
            {
                Configure();
            }
        }

        public string Path
        {
            get
            {
                return path;
            }
            set
            {
                if (path == value)
                {
                    return;
                }
                Close();
                path = value;
            }
        }
        public PathRoot PathRoot
        {
            get
            {
                return pathRoot;
            }
            set
            {
                if (pathRoot == value)
                {
                    return;
                }
                Close();
                pathRoot = value;
            }
        }
        public bool IsLooping
        {
            get
            {
                return loop;
            }
            set
            {
                if (loop == value)
                {
                    return;
                }
                loop = value;
                if (id != InvalidID)
                {
                    AVMania.AVPlayerLoop(id, loop);
                    if (!loop)
                    {
                        if (referenceTime > GetClipRange().clipOut)
                        {
                            var t = ValidateTime(referenceTime);
                            currentTime = t;
                            referenceTime = t;
                            AVMania.AVPlayerSeek(id, t);
                            IsSeeking = true;
                        }
                    }
                }
            }
        }
        public TimeSource TimeSource
        {
            get
            {
                return timeSource;
            }
            set
            {
                timeSource = value;
            }
        }
        public bool IsFailed
        {
            get;
            private set;
        } = false;
        public bool IsPaused
        {
            get;
            private set;
        } = false;
        public bool IsPlaying
        {
            get;
            private set;
        } = false;
        public bool IsPrepared
        {
            get;
            private set;
        } = false;
        public bool IsFrameReady
        {
            get;
            private set;
        } = false;
        public bool IsSeeking
        {
            get;
            private set;
        } = false;
        public bool IsFinished
        {
            get;
            private set;
        } = false;
        public float Time
        {
            get
            {
                return (float)currentTime;
            }
            set
            {
                if (value == (float)currentTime)
                {
                    return;
                }
                Seek(value);
            }
        }
        public float Position
        {
            get
            {
                var l = Length;
                return (l == 0) ? 0 : (float)(currentTime / l);
            }
            set
            {
                var time = value * Length;
                if (time == (float)currentTime)
                {
                    return;
                }
                Seek(time);
            }
        }
        public double ReferenceTime
        {
            get
            {
                return referenceTime;
            }
            set
            {
                if (timeSource != TimeSource.Manual)
                {
                    AVMania.LogWarning("TimeSource != TimeSourceType.Manual)");
                    return;
                }
                referenceTime = value;
            }
        }
        public float PlaybackSpeed
        {
            get
            {
                return playbackSpeed;
            }
            set
            {
                playbackSpeed = value;
            }
        }
        public Color Color
        {
            get
            {
                return color;
            }
            set
            {
                if (color == value)
                {
                    return;
                }
                color = value;
                repaint = true;
            }
        }
        public Color AltColor
        {
            get
            {
                return altColor;
            }
            set
            {
                if (altColor == value)
                {
                    return;
                }
                altColor = value;
                repaint = true;
            }
        }
        public float Volume
        {
            get
            {
                return volume;
            }
            set
            {
                if (volume == value)
                {
                    return;
                }
                volume = value;
                ApplyVolume();
            }
        }
        public int Pitch
        {
            get
            {
                return pitch;
            }
            set
            {
                if (pitch == value)
                {
                    return;
                }
                pitch = value;
                ApplyPitch();
            }
        }
        /// <summary>
        /// The length of the clip, in seconds.
        /// </summary>
        public float Length
        {
            get;
            private set;
        } = 0;
        public float ClipIn
        {
            get
            {
                return clipIn;
            }
            set
            {
                clipIn = value;
            }
        }
        public float ClipOut
        {
            get
            {
                return clipOut;
            }
            set
            {
                clipOut = value;
            }
        }

        public event Action<AVPlayer> OnStarted;
        public event Action<AVPlayer> OnStopped;
        public event Action<AVPlayer> OnFinished;
        public event Action<AVPlayer> OnErrorReceived;
        public event Action<AVPlayer> OnSeekCompleted;
        public event Action<AVPlayer> OnLoopPointReached;
        public event Action<AVPlayer> OnFrameReady;
        public event Action<AVPlayer> OnPrepareCompleted;
        public event Action<AVPlayer> OnUpdateTime;

        public (float clipIn, float clipOut) GetClipRange()
        {
            var ci = Math.Clamp(clipIn, 0, Length);
            var co = (clipOut == 0) ? Length : Math.Clamp(clipOut, 0, Length);
            if (ci >= co)
            {
                return (0, 0);
            }
            return (ci, co);
        }

        public void EnableVideoTrack(int trackIndex, bool enabled)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareVideoTracks(trackIndex);
            if (videoTracks[trackIndex].enabled == enabled)
            {
                return;
            }
            videoTracks[trackIndex].enabled = enabled;
            if (IsValidVideoTrack((uint)trackIndex))
            {
                AVMania.AVPlayerEnableVideoTrack(id, (uint)trackIndex, enabled);
                setting = true;
            }
        }
        public bool IsVideoTrackEnabled(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return false;
            }
            return videoTracks[trackIndex].enabled;
        }
        public RawImage GetTargetRawImage(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return null;
            }
            return videoTracks[trackIndex].targetRawImage;
        }
        public void SetTargetRawImage(int trackIndex, RawImage rawImage)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareVideoTracks(trackIndex);
            if (ReferenceEquals(videoTracks[trackIndex].targetRawImage, rawImage))
            {
                return;
            }
            videoTracks[trackIndex].targetRawImage = rawImage;
            setting = true;
        }
        public RenderTexture GetTargetRenderTexture(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return null;
            }
            return videoTracks[trackIndex].targetRenderTexture;
        }
        public void SetTargetRenderTexture(int trackIndex, RenderTexture renderTexture)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareVideoTracks(trackIndex);
            if (ReferenceEquals(videoTracks[trackIndex].targetRenderTexture, renderTexture))
            {
                return;
            }
            videoTracks[trackIndex].targetRenderTexture = renderTexture;
            setting = true;
        }
        public Renderer GetTargetRenderer(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return null;
            }
            return videoTracks[trackIndex].targetRenderer;
        }
        public void SetTargetRenderer(int trackIndex, Renderer renderer)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareVideoTracks(trackIndex);
            if (ReferenceEquals(videoTracks[trackIndex].targetRenderer, renderer))
            {
                return;
            }
            videoTracks[trackIndex].targetRenderer = renderer;
            setting = true;
        }
        public Material GetTargetMaterial(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return null;
            }
            return videoTracks[trackIndex].targetMaterial;
        }
        public void SetTargetMaterial(int trackIndex, Material material)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareVideoTracks(trackIndex);
            if (ReferenceEquals(videoTracks[trackIndex].targetMaterial, material))
            {
                return;
            }
            videoTracks[trackIndex].targetMaterial = material;
            setting = true;
        }
        public string GetTextureProperty(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return "";
            }
            return videoTracks[trackIndex].targetProperty;
        }
        public void SetTextureProperty(int trackIndex, string property)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareVideoTracks(trackIndex);
            if (ReferenceEquals(videoTracks[trackIndex].targetProperty, property))
            {
                return;
            }
            videoTracks[trackIndex].targetProperty = property;
            setting = true;
        }
        public ScalingMode GetRendererScale(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return ScalingMode.None;
            }
            return videoTracks[trackIndex].scaling;
        }
        public void SetRendererScale(int trackIndex, ScalingMode adjust)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareVideoTracks(trackIndex);
            if (ReferenceEquals(videoTracks[trackIndex].scaling, adjust))
            {
                return;
            }
            videoTracks[trackIndex].scaling = adjust;
            setting = true;
        }
        public Color GetColor(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return Color.white;
            }
            return videoTracks[trackIndex].color;
        }
        public void SetColor(int trackIndex, Color color)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareVideoTracks(trackIndex);
            if (videoTracks[trackIndex].color == color)
            {
                return;
            }
            videoTracks[trackIndex].color = color;
            repaint = true;
        }
        public AlphaSource GetAlphaSource(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return 0;
            }
            return videoTracks[trackIndex].alphaSource;
        }
        public void SetAlphaSource(int trackIndex, AlphaSource alphaSource)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareVideoTracks(trackIndex);
            if (videoTracks[trackIndex].alphaSource == alphaSource)
            {
                return;
            }
            videoTracks[trackIndex].alphaSource = alphaSource;
            repaint = true;
        }
        public Color GetChromakey(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return Color.clear;
            }
            return videoTracks[trackIndex].chromakey;
        }
        public void SetChromakey(int trackIndex, Color chromakey)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareVideoTracks(trackIndex);
            if (videoTracks[trackIndex].chromakey == chromakey)
            {
                return;
            }
            videoTracks[trackIndex].chromakey = chromakey;
            repaint = true;
        }
        public float GetChromakeyParamA(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return 0;
            }
            return videoTracks[trackIndex].chromakeyParamA;
        }
        public void SetChromakeyParamA(int trackIndex, float chromakeyParamA)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareVideoTracks(trackIndex);
            if (videoTracks[trackIndex].chromakeyParamA == chromakeyParamA)
            {
                return;
            }
            videoTracks[trackIndex].chromakeyParamA = chromakeyParamA;
            repaint = true;
        }
        public float GetChromakeyParamB(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return 0;
            }
            return videoTracks[trackIndex].chromakeyParamB;
        }
        public void SetChromakeyParamB(int trackIndex, float chromakeyParamB)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareVideoTracks(trackIndex);
            if (videoTracks[trackIndex].chromakeyParamB == chromakeyParamB)
            {
                return;
            }
            videoTracks[trackIndex].chromakeyParamB = chromakeyParamB;
            repaint = true;
        }
        public int GetAlphaTrack(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= videoTracks.Length))
            {
                return 0;
            }
            return videoTracks[trackIndex].alphaTrack;
        }
        public void SetAlphaTrack(int trackIndex, int alphaTrack)
        {
            if ((trackIndex < 0) || (alphaTrack < 0))
            {
                return;
            }
            PrepareVideoTracks(Mathf.Max(trackIndex, alphaTrack));
            if (videoTracks[trackIndex].alphaTrack == alphaTrack)
            {
                return;
            }
            videoTracks[trackIndex].alphaTrack = alphaTrack;
            repaint = true;
        }

        public void EnableAudioTrack(int trackIndex, bool enabled)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareAudioTracks(trackIndex);
            if (audioTracks[trackIndex].enabled == enabled)
            {
                return;
            }
            audioTracks[trackIndex].enabled = enabled;
            if (IsValidAudioTrack((uint)trackIndex))
            {
                AVMania.AVPlayerEnableAudioTrack(id, (uint)trackIndex, enabled);
                setting = true;
            }
        }
        public bool IsAudioTrackEnabled(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= audioTracks.Length))
            {
                return false;
            }
            return audioTracks[trackIndex].enabled;
        }
        public float GetVolume(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= audioTracks.Length))
            {
                return 0;
            }
            return audioTracks[trackIndex].volume;
        }
        public void SetVolume(int trackIndex, float volume)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareAudioTracks(trackIndex);
            if (audioTracks[trackIndex].volume == volume)
            {
                return;
            }
            audioTracks[trackIndex].volume = volume;
            if (IsValidAudioTrack((uint)trackIndex))
            {
                var v = this.volume * volume;
                AVMania.AVPlayerSetVolume(id, (uint)trackIndex, v);
            }
        }
        public int GetPitch(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= audioTracks.Length))
            {
                return 0;
            }
            return audioTracks[trackIndex].pitch;
        }
        public void SetPitch(int trackIndex, int pitch)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareAudioTracks(trackIndex);
            if (audioTracks[trackIndex].pitch == pitch)
            {
                return;
            }
            audioTracks[trackIndex].pitch = pitch;
            if (IsValidAudioTrack((uint)trackIndex))
            {
                var p = Mathf.Clamp(this.pitch + pitch, -1200, +1200);
                AVMania.AVPlayerSetPitch(id, (uint)trackIndex, p);
            }
        }
        public AudioRoute GetAudioRoute(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= audioTracks.Length))
            {
                return 0;
            }
            return audioTracks[trackIndex].audioRoute;
        }
        public void SetAudioOutput(int trackIndex, AudioRoute route)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareAudioTracks(trackIndex);
            if (audioTracks[trackIndex].volume == volume)
            {
                return;
            }
            audioTracks[trackIndex].audioRoute = route;
        }
        public AVListener GetAVListenr(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= audioTracks.Length))
            {
                return null;
            }
            return audioTracks[trackIndex].avListener;
        }
        public void SetAVListener(int trackIndex, AVListener avListener)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareAudioTracks(trackIndex);
            if (ReferenceEquals(audioTracks[trackIndex].avListener, avListener))
            {
                return;
            }
            var pvListener = audioTracks[trackIndex].avListener;
            audioTracks[trackIndex].avListener = avListener;
            if (audioTrackStates[trackIndex].activeRoute == AudioRoute.AVListener)
            {
                if (pvListener != null)
                {
                    pvListener.OnAudioUpdate -= audioTrackStates[trackIndex].onAudioUpdate;
                }
                if (avListener != null)
                {
                    avListener.OnAudioUpdate += audioTrackStates[trackIndex].onAudioUpdate;
                }
            }
        }
        public AVSource GetAVSource(int trackIndex)
        {
            if ((trackIndex < 0) || (trackIndex >= audioTracks.Length))
            {
                return null;
            }
            return audioTracks[trackIndex].avSource;
        }
        public void SetAVSource(int trackIndex, AVSource avSource)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareAudioTracks(trackIndex);
            if (ReferenceEquals(audioTracks[trackIndex].avSource, avSource))
            {
                return;
            }
            var pvSource = audioTracks[trackIndex].avSource;
            audioTracks[trackIndex].avSource = avSource;
            if (audioTrackStates[trackIndex].activeRoute == AudioRoute.AVSource)
            {
                if (pvSource != null)
                {
                    pvSource.OnAudioUpdate -= audioTrackStates[trackIndex].onAudioUpdate;
                }
                if (avSource != null)
                {
                    avSource.OnAudioUpdate += audioTrackStates[trackIndex].onAudioUpdate;
                }
            }
        }

        public bool GetVideoAttributes(int trackIndex, out VideoAttributes attributes)
        {
            var track = unchecked((uint)trackIndex);
            if (!IsValidVideoTrack(track))
            {
                attributes = default;
                return false;
            }
            attributes = videoTrackStates[track].attributes;
            return true;
        }
        public bool GetAudioAttributes(int trackIndex, out AudioAttributes attributes)
        {
            var track = unchecked((uint)trackIndex);
            if (!IsValidAudioTrack(track))
            {
                attributes = default;
                return false;
            }
            attributes = audioTrackStates[track].attributes;
            return true;
        }

        public void OverrideTextureUpdate(int trackIndex, OnTextureUpdateDelegate onTextureUpdate)
        {
            if (trackIndex < 0)
            {
                return;
            }
            PrepareAudioTracks(trackIndex);
            videoTrackStates[trackIndex].onTextureUpdate = onTextureUpdate;
        }
        public bool GetTextureInfo(int trackIndex, out TextureInfo info)
        {
            var track = unchecked((uint)trackIndex);
            if (!IsValidVideoTrack(track))
            {
                info = default;
                return false;
            }
            info = videoTrackStates[track].info;
            return true;
        }
        public Texture2D GetRawTexture(int trackIndex)
        {
            var track = unchecked((uint)trackIndex);
            if (!IsValidVideoTrack(track))
            {
                return null;
            }
            return videoTrackStates[trackIndex].texture;
        }
        public RenderTexture GetRenderTexture(int trackIndex)
        {
            var track = unchecked((uint)trackIndex);
            if (!IsValidVideoTrack(track))
            {
                return null;
            }
            return videoTrackStates[trackIndex].renderTexture;
        }
        public void GetAudioSamples(int trackIndex, float[] samples, int channels)
        {
            if ((id == InvalidID) || (trackIndex < 0) || (trackIndex >= audioTrackCount) || (channels <= 0))
            {
                return;
            }
            if (!audioTracks[trackIndex].enabled || (audioTracks[trackIndex].audioRoute != AudioRoute.None))
            {
                return;
            }
            var len = samples.Length / channels;
            var num = len * channels;
            var ptr = AVMania.AVPlayerGetSamples(id, (uint)trackIndex, (uint)channels, (uint)len);
            System.Runtime.InteropServices.Marshal.Copy(ptr, samples, 0, num);
        }

        public void Close()
        {
            if (id != InvalidID)
            {
                AVMania.AVPlayerClose(id);
                id = InvalidID;
            }
            starting = false;
            currentUrl = "";
            videoTrackCount = 0;
            audioTrackCount = 0;
            Length = 0;
            IsFailed = false;
            IsFinished = false;
            IsPrepared = false;
            IsPlaying = false;
            IsPaused = false;
            IsFrameReady = false;
            repaint = true;
        }
        public void Prepare()
        {
            var url = AVMania.GetPath(pathRoot, path);
            if ((id != InvalidID) && (url == currentUrl))
            {
                return;
            }
            Close();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            currentUrl = url;
            currentTime = 0;
            referenceTime = 0;
            id = AVMania.AVPlayerOpen(url);
            if (id == InvalidID)
            {
                Fail();
                return;
            }
        }
        public void Seek(float time)
        {
            if ((id == InvalidID) || IsFailed)
            {
                return;
            }
            IsFinished = false;
            if (Length != 0)
            {
                var t = ValidateTime(time);
                currentTime = t;
                referenceTime = t;
                AVMania.AVPlayerSeek(id, t);
                IsSeeking = true;
            }
            else
            {
                currentTime = time;
                referenceTime = time;
            }
        }
        public void Play()
        {
            Prepare();
            if ((id == InvalidID) || IsFailed)
            {
                return;
            }
            if (!IsPrepared)
            {
                starting = true;
                return;
            }
            if (IsFinished)
            {
                Seek(0);
            }
            AVMania.AVPlayerPause(id, false);
            var paused = IsPaused;
            starting = false;
            IsPlaying = true;
            IsPaused = false;
            if (!paused)
            {
                OnStarted?.Invoke(this);
            }
        }
        public void Pause()
        {
            if ((id == InvalidID) || IsFailed)
            {
                return;
            }
            if (!IsPlaying && !starting)
            {
                return;
            }
            if (IsPaused)
            {
                return;
            }
            AVMania.AVPlayerPause(id, true);
            starting = false;
            IsSeeking = false;
            IsPlaying = false;
            IsPaused = true;
            repaint = true;
        }
        public void Stop()
        {
            if ((id == InvalidID) || IsFailed)
            {
                return;
            }
            if (!IsPaused && !IsPlaying && !starting)
            {
                return;
            }
            AVMania.AVPlayerPause(id, true);
            Time = GetClipRange().clipIn;
            starting = false;
            IsFinished = false;
            IsPlaying = false;
            IsPaused = false;
            IsSeeking = false;
            repaint = true;
            if (IsPrepared)
            {
                OnStopped?.Invoke(this);
            }
        }
        private void Finish()
        {
            if (!IsPaused && !IsPlaying && !starting)
            {
                return;
            }
            AVMania.Assert(!IsFailed && (id != InvalidID));
            AVMania.AVPlayerPause(id, true);
            starting = false;
            IsFinished = true;
            IsPaused = true;
            OnFinished?.Invoke(this);
        }
        private void Fail()
        {
            if (IsFailed)
            {
                return;
            }
            if (id != InvalidID)
            {
                AVMania.AVPlayerPause(id, true);
                id = InvalidID;
            }
            IsFailed = true;
            IsSeeking = false;
            IsPlaying = false;
            IsPaused = false;
            IsPrepared = false;
            IsFinished = false;
            starting = false;
            //AVMania.Log("OnErrorReceived");
            OnErrorReceived?.Invoke(this);
        }

        private double ValidateTime(double time)
        {
            var cr = GetClipRange();
            var d = cr.clipOut - cr.clipIn;
            if (d <= 0)
            {
                return 0;
            }
            if (IsLooping)
            {
                var m = (time - cr.clipIn) % d;
                if (m < 0)
                {
                    m += d;
                }
                return m + cr.clipIn;
            }
            else
            {
                return Math.Clamp(time, cr.clipIn, cr.clipOut);
            }
        }
        private void PrepareVideoTracks(int trackIndex)
        {
            if (trackIndex < 0)
            {
                return;
            }
            if (videoTracks == null)
            {
                videoTracks = new VideoTrackConfiguration[trackIndex + 1];
            }
            if (trackIndex >= videoTracks.Length)
            {
                var vt = new VideoTrackConfiguration[trackIndex + 1];
                for (int i = 0; i < videoTracks.Length; i++)
                {
                    var t = videoTracks[i];
                    if (t == null)
                    {
                        t = new VideoTrackConfiguration
                        {
                            enabled = false,
                        };
                    }
                    vt[i] = t;
                }
                for (int i = videoTracks.Length; i < trackIndex + 1; i++)
                {
                    var t = new VideoTrackConfiguration
                    {
                        enabled = false,
                    };
                    vt[i] = t;
                }
                videoTracks = vt;
            }
            else
            {
                for (int i = 0; i < videoTracks.Length; i++)
                {
                    var t = videoTracks[i];
                    if (t != null)
                    {
                        continue;
                    }
                    t = new VideoTrackConfiguration
                    {
                        enabled = false,
                    };
                    videoTracks[i] = t;
                }
            }
            if (videoTrackStates == null)
            {
                videoTrackStates = new VideoTrackState[trackIndex + 1];
            }
            if (trackIndex >= videoTrackStates.Length)
            {
                var vs = new VideoTrackState[trackIndex + 1];
                for (int i = 0; i < videoTrackStates.Length; i++)
                {
                    vs[i] = videoTrackStates[i];
                }
                videoTrackStates = vs;
            }
        }
        private void PrepareAudioTracks(int trackIndex)
        {
            if (trackIndex < 0)
            {
                return;
            }
            if (audioTracks == null)
            {
                audioTracks = new AudioTrackConfiguration[trackIndex + 1];
            }
            if (trackIndex >= audioTracks.Length)
            {
                var at = new AudioTrackConfiguration[trackIndex + 1];
                for (int i = 0; i < audioTracks.Length; i++)
                {
                    var t = audioTracks[i];
                    if (t == null)
                    {
                        t = new AudioTrackConfiguration
                        {
                            enabled = false,
                        };
                    }
                    at[i] = t;
                }
                for (int i = audioTracks.Length; i < trackIndex + 1; i++)
                {
                    var t = new AudioTrackConfiguration
                    {
                        enabled = false,
                    };
                    at[i] = t;
                }
                audioTracks = at;
            }
            else
            {
                for (int i = 0; i < audioTracks.Length; i++)
                {
                    var t = audioTracks[i];
                    if (t != null)
                    {
                        continue;
                    }
                    t = new AudioTrackConfiguration
                    {
                        enabled = false,
                    };
                    audioTracks[i] = t;
                }
            }
            if (audioTrackStates == null)
            {
                audioTrackStates = new AudioTrackState[trackIndex + 1];
            }
            if (trackIndex >= audioTrackStates.Length)
            {
                var vs = new AudioTrackState[trackIndex + 1];
                for (int i = 0; i < audioTrackStates.Length; i++)
                {
                    vs[i] = audioTrackStates[i];
                }
                audioTrackStates = vs;
            }
        }
        private bool IsValidVideoTrack(uint track)
        {
            return (id != InvalidID) && (track < videoTrackCount);
        }
        private bool IsValidAudioTrack(uint track)
        {
            return (id != InvalidID) && (track < audioTrackCount);
        }

        private void ApplyVolume()
        {
            if (id == InvalidID)
            {
                return;
            }
            for (int i = 0; i < audioTrackCount; i++)
            {
                uint trackIndex = (uint)i;
                var volume = audioTracks[i].volume;
                //AVMania.Log("volume master=" + this.volume + " track:" + trackIndex + "=" + volume);
                AVMania.AVPlayerSetVolume(id, trackIndex, this.volume * volume);
            }
        }
        private void ApplyPitch()
        {
            if (id == InvalidID)
            {
                return;
            }
            for (int i = 0; i < audioTrackCount; i++)
            {
                uint trackIndex = (uint)i;
                var pitch = audioTracks[i].pitch;
                //AVMania.Log("pitch master=" + this.pitch + " track:" + trackIndex + "=" + pitch);
                var p = Mathf.Clamp(this.pitch + pitch, -1200, +1200);
                AVMania.AVPlayerSetPitch(id, trackIndex, p);
            }
        }
        private void SetupAudioBuffer()
        {
            if (id == InvalidID)
            {
                return;
            }
            var outputSampleRate = AudioSettings.outputSampleRate;
            var len = Mathf.Max(audioBuffer * 1000, 33);
            for (int i = 0; i < audioTrackCount; i++)
            {
                var trackIndex = (uint)i;
                var track = audioTracks[i];
                if (!track.enabled)
                {
                    continue;
                }
                AVMania.AVPlayerSetupAudioBuffer(id, trackIndex, (uint)len, (uint)outputSampleRate, 9, 22, 17);
            }
        }

        private void Configure()
        {
            Length = (float)AVMania.AVPlayerGetDuration(id);
            {
                videoTrackCount = AVMania.AVPlayerGetVideoTrackCount(id);
                PrepareVideoTracks((int)videoTrackCount - 1);
                for (uint i = 0, end = videoTrackCount; i < end; i++)
                {
                    var enabled = videoTracks[i].enabled;
                    AVMania.AVPlayerEnableVideoTrack(id, i, enabled);
                    if (enabled)
                    {
                        AVMania.AVPlayerGetVideoAttributes(id, i, out videoTrackStates[i].attributes);
                    }
                    else
                    {
                        videoTrackStates[i].attributes = default;
                    }
                }
            }
            {
                audioTrackCount = AVMania.AVPlayerGetAudioTrackCount(id);
                PrepareAudioTracks((int)audioTrackCount - 1);
                SetupAudioBuffer();
                for (uint i = 0, end = audioTrackCount; i < end; i++)
                {
                    var enabled = audioTracks[i].enabled;
                    AVMania.AVPlayerEnableAudioTrack(id, i, enabled);
                    if (enabled)
                    {
                        AVMania.AVPlayerGetAudioAttributes(id, i, out audioTrackStates[i].attributes);
                    }
                    else
                    {
                        audioTrackStates[i].attributes = default;
                    }
                }
            }
            ApplyVolume();
            ApplyPitch();
            AVMania.AVPlayerLoop(id, loop);
            AVMania.AVPlayerAccurately(id, accurately);
            Seek((float)currentTime);
            setting = true;
        }

        private static Vector2 CalcTextureSize(float w, float h, AlphaSource alphaSource)
        {
            switch (alphaSource)
            {
                default:
                case AlphaSource.One:
                case AlphaSource.Zero:
                case AlphaSource.Alpha:
                case AlphaSource.Chromakey:
                case AlphaSource.Track:
                    return new Vector2(w, h);
                case AlphaSource.Bottom:
                    return new Vector2(w, h / 2);
                case AlphaSource.Right:
                    return new Vector2(w / 2, h);
#if false
                case AlphaSource.BottomHalf:
                    return new Vector2(w, h * 2 / 3);
                case AlphaSource.RightHalf:
                    return new Vector2(w * 2 / 3, h);
#endif
            }
        }
        private void ApplyRendererScale(uint track)
        {
            if (!IsPrepared || (track >= videoTrackCount))
            {
                return;
            }
            ref var conf = ref videoTracks[track];
            var targetRenderer = conf.targetRenderer;
            if (targetRenderer == null)
            {
                return;
            }
            ref var state = ref videoTrackStates[track];
            ref var attributes = ref state.attributes;
            var width = Mathf.Max(64, attributes.width);
            var height = Mathf.Max(64, attributes.height);
            var size = CalcTextureSize(width, height, conf.alphaSource);
            var aspectNum = (float)attributes.aspectRatioNumerator;
            var aspectDen = (float)attributes.aspectRatioDenominator;
            var aspect = (aspectDen == 0) ? 1.0f : ((float)aspectNum / aspectDen);
            var ax = size.x / size.y * aspect;
            if ((state.originalRendererScale == Vector3.zero) || (state.previousRendererScale != targetRenderer.transform.localScale))
            {
                state.originalRendererScale = targetRenderer.transform.localScale;
            }
            var scale = state.originalRendererScale;
            switch (conf.scaling)
            {
                default:
                case ScalingMode.None:
                    break;
                case ScalingMode.Vertical:
                    {
                        scale.x = scale.y * ax;
                    }
                    break;
                case ScalingMode.Horizontal:
                    {
                        scale.y = scale.x / ax;
                    }
                    break;
                case ScalingMode.Expand:
                    if (ax < 1.0f)
                    {
                        scale.y = scale.x / ax;
                    }
                    else
                    {
                        scale.x = scale.y * ax;
                    }
                    break;
                case ScalingMode.Shrink:
                    if (ax < 1.0f)
                    {
                        scale.x = scale.y * ax;
                    }
                    else
                    {
                        scale.y = scale.x / ax;
                    }
                    break;
            }
            state.previousRendererScale = scale;
            targetRenderer.transform.localScale = scale;
        }
        private void ApplyRawImageSize(uint track)
        {
            if (!IsPrepared || (track >= videoTrackCount))
            {
                return;
            }
            ref var conf = ref videoTracks[track];
            var targetRawImage = conf.targetRawImage;
            if (targetRawImage == null)
            {
                return;
            }
            ref var state = ref videoTrackStates[track];
            ref var attributes = ref state.attributes;
            var width = attributes.width;
            var height = attributes.height;
            var size = CalcTextureSize(width, height, conf.alphaSource);
            var aspectNum = (float)attributes.aspectRatioNumerator;
            var aspectDen = (float)attributes.aspectRatioDenominator;
            var aspect = (aspectDen == 0) ? 1.0f : ((float)aspectNum / aspectDen);
            var ax = size.x / size.y * aspect;
            var rect = targetRawImage.rectTransform.rect;
            if ((state.originalRawImageSize == Vector2.zero) || (state.previousRawImageSize != rect.size))
            {
                state.originalRawImageSize = rect.size;
            }
            var sz = state.originalRawImageSize;
            switch (conf.scaling)
            {
                default:
                case ScalingMode.None:
                    break;
                case ScalingMode.Vertical:
                    {
                        sz.x = sz.y * ax;
                    }
                    break;
                case ScalingMode.Horizontal:
                    {
                        sz.y = sz.x / ax;
                    }
                    break;
                case ScalingMode.Expand:
                    if (ax < 1.0f)
                    {
                        sz.y = sz.x / ax;
                    }
                    else
                    {
                        sz.x = sz.y * ax;
                    }
                    break;
                case ScalingMode.Shrink:
                    if (ax < 1.0f)
                    {
                        sz.x = sz.y * ax;
                    }
                    else
                    {
                        sz.y = sz.x / ax;
                    }
                    break;
            }
            state.previousRawImageSize = sz;
            var dt = sz - rect.size;
            targetRawImage.rectTransform.sizeDelta += dt;
        }
        private void SetupStates()
        {
            setting = false;
            PrepareVideoTracks(videoTracks.Length - 1);
            for (int i = 0, end = videoTracks.Length; i < end; i++)
            {
                ref var conf = ref videoTracks[i];
                ref var state = ref videoTrackStates[i];
                if (!conf.enabled)
                {
                    if (state.textureOwner)
                    {
                        state.textureOwner = false;
                        AVMania.Destroy(state.renderTexture);
                    }
                    continue;
                }
                if (conf.targetRenderTexture != null)
                {
                    if (state.textureOwner)
                    {
                        state.textureOwner = false;
                        AVMania.Destroy(state.renderTexture);
                    }
                    if (!ReferenceEquals(state.renderTexture, conf.targetRenderTexture))
                    {
                        state.renderTexture = conf.targetRenderTexture;
                        repaint = true;
                    }
                }
                else
                {
                    if (!state.textureOwner)
                    {
                        state.textureOwner = true;
                        var width = Mathf.Max(64, state.attributes.width);
                        var height = Mathf.Max(64, state.attributes.height);
                        var size = CalcTextureSize(width, height, conf.alphaSource);
                        state.renderTexture = new RenderTexture((int)size.x, (int)size.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                        state.renderTexture.hideFlags = HideFlags.DontSave;
                        repaint = true;
                    }
                }
                if (conf.targetMaterial != null)
                {
                    conf.targetMaterial.SetTexture(conf.targetProperty, state.renderTexture);
                }
                if (conf.targetRenderer != null)
                {
                    conf.targetRenderer.GetPropertyBlock(propertyBlock);
                    propertyBlock.SetTexture(conf.targetProperty, state.renderTexture);
                    conf.targetRenderer.SetPropertyBlock(propertyBlock);
                    propertyBlock.Clear();
                    ApplyRendererScale((uint)i);
                }
                if (conf.targetRawImage != null)
                {
                    conf.targetRawImage.texture = state.renderTexture;
                    ApplyRawImageSize((uint)i);
                }
            }
            PrepareAudioTracks(audioTracks.Length - 1);
            for (int i = 0, end = audioTracks.Length; i < end; i++)
            {
                ref var conf = ref audioTracks[i];
                var audioRoute = conf.audioRoute;
                if (!conf.enabled)
                {
                    audioRoute = AudioRoute.None;
                }
                ref var state = ref audioTrackStates[i];
                if (audioRoute != state.activeRoute)
                {
                    if (state.onAudioUpdate == null)
                    {
                        var track = (uint)i;
                        state.onAudioUpdate = (samples, channel) => OnAudioUpdate(track, samples, channel);
                    }
                    switch (state.activeRoute)
                    {
                        case AudioRoute.None:
                            break;
                        case AudioRoute.AVListener:
                            if (conf.avListener != null)
                            {
                                conf.avListener.OnAudioUpdate -= state.onAudioUpdate;
                            }
                            break;
                        case AudioRoute.AVSource:
                            if (conf.avSource != null)
                            {
                                conf.avSource.OnAudioUpdate -= state.onAudioUpdate;
                            }
                            break;
                    }
                    switch (audioRoute)
                    {
                        case AudioRoute.None:
                            break;
                        case AudioRoute.AVListener:
                            if (conf.avListener != null)
                            {
                                conf.avListener.OnAudioUpdate += state.onAudioUpdate;
                            }
                            break;
                        case AudioRoute.AVSource:
                            if (conf.avSource != null)
                            {
                                conf.avSource.OnAudioUpdate += state.onAudioUpdate;
                            }
                            break;
                    }
                    state.activeRoute = audioRoute;
                }
            }
        }
        private void ReleaseStates()
        {
            if (videoTrackStates != null)
            {
                for (int i = 0, end = videoTrackStates.Length; i < end; i++)
                {
                    ref var state = ref videoTrackStates[i];
                    if (state.texture != null)
                    {
                        AVMania.Destroy(state.texture);
                    }
                    state.texture = null;
                    if ((state.renderTexture != null) && state.textureOwner)
                    {
                        AVMania.Destroy(state.renderTexture);
                    }
                    state.renderTexture = null;
                    state.textureOwner = false;
                }
            }
            videoTrackStates = null;
            if (audioTrackStates != null)
            {
                for (int i = 0, end = audioTrackStates.Length; i < end; i++)
                {
                    ref var conf = ref audioTracks[i];
                    ref var state = ref audioTrackStates[i];
                    switch (state.activeRoute)
                    {
                        case AudioRoute.None:
                            break;
                        case AudioRoute.AVListener:
                            if (conf.avListener != null)
                            {
                                conf.avListener.OnAudioUpdate -= state.onAudioUpdate;
                            }
                            break;
                        case AudioRoute.AVSource:
                            if (conf.avSource != null)
                            {
                                conf.avSource.OnAudioUpdate -= state.onAudioUpdate;
                            }
                            break;
                    }
                    state.onAudioUpdate = null;
                }
            }
            audioTrackStates = null;
        }

        private double dspDelta = 0;
        private void UpdateDSPDelta()
        {
            var dspTime = AudioSettings.dspTime;
            dspDelta = dspTime - previousDSPTime;
            previousDSPTime = dspTime;
        }
        private double GetDeltaTime()
        {
            var deltaTime = 0.0;
            switch (TimeSource)
            {
                case TimeSource.AudioDSPTime:
                    deltaTime = dspDelta;
                    break;
                case TimeSource.GameTime:
                    deltaTime = UnityEngine.Time.deltaTime;
                    break;
                case TimeSource.UnscaledGameTime:
                    deltaTime = UnityEngine.Time.unscaledDeltaTime;
                    break;
                default:
                case TimeSource.Manual:
                    deltaTime = 0;
                    break;
            }
            return deltaTime;
        }
        private void UpdateTime()
        {
            var hold = !IsPlaying || IsFinished || IsPaused;
            var busy = !accurately && AVMania.AVPlayerIsUpdating(id);
            if (!busy && !hold && !timeControl)
            {
                var deltaTime = GetDeltaTime();
                deltaTime *= playbackSpeed;
                var rt = referenceTime + deltaTime;
                if (!IsSeeking)
                {
                    var cr = GetClipRange();
                    if (IsLooping)
                    {
                        if ((rt < cr.clipIn) || (rt >= cr.clipOut))
                        {
                            var jt = ValidateTime(rt);
                            AVMania.AVPlayerSeek(id, jt);
                            rt = jt;
                        }
                    }
                    else
                    {
                        if (rt < cr.clipIn)
                        {
                            IsFinished = true;
                            hold = true;
                            rt = cr.clipIn;
                        }
                        if (rt >= cr.clipOut)
                        {
                            IsFinished = true;
                            hold = true;
                            rt = cr.clipOut;
                        }
                    }
                }
                referenceTime = rt;
            }
            AVMania.AVPlayerPause(id, hold);
            avState = AVMania.AVPlayerUpdate(id, referenceTime);
#if false
            var tt = $"{referenceTime:f3} ({deltaTime:f3})";
            if (IsPrepared)
            {
                tt += " prepared";
            }
            if (starting)
            {
                tt += " starting";
            }
            if (IsPlaying)
            {
                tt += " playing";
            }
            if (IsFailed)
            {
                tt += " failed";
            }
            if (IsPaused)
            {
                tt += " paused";
            }
            if (IsSeeking)
            {
                tt += " seeking";
            }
            if (avClip.IsFrameReady)
            {
                tt += " frameready";
            }
            AVMania.Log(tt);
#endif
            AVMania.DumpLogMessage();
        }
        private void UpdateFrame(uint track)
        {
            if (!AVMania.AVPlayerGetTextureInfo(id, track, out TextureInfo info))
            {
                return;
            }
            ref var state = ref videoTrackStates[track];
            state.info = info;
            //AVMania.Log($"{info.texWidth}x{info.texHeight} {info.bpp} {info.videoFormat}");
            var tf = AVMania.GetTextureFormat(info.videoFormat);
            var dr = false;
            var tex = state.texture;
            if ((tex != null) && (tex.format != tf))
            {
                AVMania.Destroy(tex);
                tex = null;
            }
            if (tex == null)
            {
                tex = new Texture2D((int)info.texWidth, (int)info.texHeight, tf, false, true);
                tex.hideFlags = HideFlags.DontSave;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Point;
                tex.name = "AVPlayer";
                state.texture = tex;
                dr = true;
            }
            else if ((tex.width != info.texWidth) || (tex.height != info.texHeight))
            {
                tex.Reinitialize((int)info.texWidth, (int)info.texHeight);
                tex.Apply();
                dr = true;
            }
            if (dr)
            {
                var width = info.width;
                var height = info.height;
                var region = new Vector4(0, 0, (float)width / info.width, (float)height / info.height);
                state.region = region;
            }
            var tid = AVMania.AVPlayerGetTextureUpdateId(id, track);
            if (tid != 0)
            {
                commandBuffer.IssuePluginCustomTextureUpdateV2(textureUpdateCallback, tex, tid);
            }
        }
        private void BlitFrame(uint track)
        {
            ref var conf = ref videoTracks[track];
            ref var state = ref videoTrackStates[track];
            var rt = state.renderTexture;
            if (rt == null)
            {
                return;
            }
            ref var info = ref state.info;
            var tex = state.texture;
            var reg = state.region;
            var als = conf.alphaSource;
            if (state.textureOwner)
            {
                var width = info.width;
                var height = info.height;
                var size = CalcTextureSize(width, height, als);
                if ((rt.width != size.x) || (rt.height != size.y))
                {
                    rt.Release();
                    rt.width = (int)size.x;
                    rt.height = (int)size.y;
                    rt.Create();
                }
            }
            if (state.onTextureUpdate != null)
            {
                state.onTextureUpdate.Invoke((int)track, commandBuffer, tex, rt);
            }
            else
            {
                if (tex == null)
                {
                    tex = AVMania.whiteTexture;
                }
                commandBuffer.SetGlobalColor(_Color, conf.color * color);
                commandBuffer.SetGlobalVector(_MainTex_TexelRegion, reg);
                if (als == AlphaSource.Chromakey)
                {
                    commandBuffer.SetGlobalVector(_Chromakey, conf.chromakey);
                    commandBuffer.SetGlobalVector(_ChromakeyLab, AVColorUtil.Color2Lab(conf.chromakey));
                    var a = conf.chromakeyParamA;
                    var b = conf.chromakeyParamB;
                    if (b < 0.01f)
                    {
                        b = 0.01f;
                    }
                    commandBuffer.SetGlobalVector(_ChromakeyParam, new Vector4(a, 1.0f / b, 0, 0));
                }
                if (als == AlphaSource.Track)
                {
                    var alphaTrack = conf.alphaTrack;
                    if ((alphaTrack < 0) || (alphaTrack >= videoTrackCount))
                    {
                        als = AlphaSource.One;
                    }
                    else
                    {
                        var ntex = state.texture;
                        var nreg = state.region;
                        if (ntex == null)
                        {
                            ntex = AVMania.whiteTexture;
                        }
                        commandBuffer.SetGlobalTexture(_Track, ntex);
                        commandBuffer.SetGlobalVector(_Track_TexelRegion, nreg);
                    }
                }
                var bf = LocakKeywordMap<BlitFormat>.Keyword(blitMaterial, AVMania.GetBlitFormat(info.videoFormat));
                var nr = LocakKeywordMap<NominalRange>.Keyword(blitMaterial, info.nominalRange);
                var tf = LocakKeywordMap<TransferFunction>.Keyword(blitMaterial, info.transferFunction);
                var ym = LocakKeywordMap<YUVMatrix>.Keyword(blitMaterial, info.yuvMatrix);
                var al = LocakKeywordMap<AlphaSource>.Keyword(blitMaterial, als);
                commandBuffer.EnableKeyword(blitMaterial, bf);
                commandBuffer.EnableKeyword(blitMaterial, nr);
                commandBuffer.EnableKeyword(blitMaterial, tf);
                commandBuffer.EnableKeyword(blitMaterial, ym);
                commandBuffer.EnableKeyword(blitMaterial, al);
                commandBuffer.Blit(tex, rt, blitMaterial);
                commandBuffer.DisableKeyword(blitMaterial, bf);
                commandBuffer.DisableKeyword(blitMaterial, nr);
                commandBuffer.DisableKeyword(blitMaterial, tf);
                commandBuffer.DisableKeyword(blitMaterial, ym);
                commandBuffer.DisableKeyword(blitMaterial, al);
            }
        }
        private void BlitWhite(uint track)
        {
            var rt = videoTrackStates[track].renderTexture;
            if (rt == null)
            {
                return;
            }
            var tex = AVMania.whiteTexture;
            var reg = new Vector4(0, 0, 1, 1);
            commandBuffer.SetGlobalColor(_Color, videoTracks[track].color * altColor);
            commandBuffer.SetGlobalVector(_MainTex_TexelRegion, reg);
            commandBuffer.Blit(tex, rt, blitMaterial);
        }
        private void UpdateTexture()
        {
            var rep = repaint;
            repaint = false;
            commandBuffer.Clear();
            var vt = (IsPrepared && (IsPlaying || IsPaused)) ? videoTrackCount : 0;
            for (uint i = 0; i < vt; i++)
            {
                if (!videoTracks[i].enabled)
                {
                    continue;
                }
                var df = AVMania.AVPlayerIsFrameReady(id, i);
                if (df)
                {
                    UpdateFrame(i);
                }
                if (df || rep)
                {
                    BlitFrame(i);
                }
            }
            if (rep)
            {
                for (uint i = vt, end = (uint)videoTracks.Length; i < end; i++)
                {
                    if (!videoTracks[i].enabled)
                    {
                        continue;
                    }
                    {
                        BlitWhite(i);
                    }
                }
            }
            if (commandBuffer.sizeInBytes != 0)
            {
                Graphics.ExecuteCommandBuffer(commandBuffer);
                commandBuffer.Clear();
            }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.SceneView.RepaintAll();
            }
#endif
        }
        private void OnAudioUpdate(uint track, float[] data, int channels)
        {
            var id = this.id;
            if ((id == InvalidID) || !IsPrepared || IsFailed || (track >= audioTrackCount))
            {
                return;
            }
            var len = data.Length / channels;
            var ptr = AVMania.AVPlayerGetSamples(id, track, (uint)channels, (uint)len);
            if (ptr == IntPtr.Zero)
            {
                return;
            }
            unsafe
            {
                var num = len * channels;
                var rp = new ReadOnlySpan<float>((float*)ptr, num);
                for (int i = 0; i < num; i++)
                {
                    data[i] += rp[i];
                }
            }
        }
        private IEnumerator UpdateCoroutine()
        {
            //AVMania.Log("UpdateCoroutine");
            if (playOnAwake)
            {
                Play();
            }
            else
            {
                Prepare();
            }
            previousDSPTime = AudioSettings.dspTime;
            for (; ; )
            {
                OnUpdateTime?.Invoke(this);
                if (setting)
                {
                    SetupStates();
                }
                {
                    UpdateTexture();
                }
                yield return null;

                UpdateDSPDelta();
                if ((id == InvalidID) || IsFailed)
                {
                    continue;
                }
                UpdateTime();
                var failed = (avState & AVMania.AVPlayerState.Failed) != 0;
                if (failed)
                {
                    Fail();
                    continue;
                }
                var seeking = (avState & AVMania.AVPlayerState.Seeking) != 0;
                var prepared = (avState & AVMania.AVPlayerState.Prepared) != 0;
                var frameready = (avState & AVMania.AVPlayerState.FrameReady) != 0;
                if (!IsPrepared)
                {
                    if (!prepared)
                    {
                        continue;
                    }
                    if (AVMania.AVPlayerGetDuration(id) == 0)
                    {
                        Fail();
                        continue;
                    }
                    if (Length == 0)
                    {
                        Configure();
                        continue;
                    }
                    if (seeking)
                    {
                        continue;
                    }
                    IsPrepared = true;
                    repaint = true;
                    SetupStates();
                    //AVMania.Log("OnPrepareCompleted");
                    OnPrepareCompleted?.Invoke(this);
                    if (starting)
                    {
                        Play();
                    }
                }
                IsFrameReady = frameready;
                if (setting)
                {
                    SetupStates();
                }
                {
                    UpdateTexture();
                }
                if (!IsPlaying && !IsPaused)
                {
                    continue;
                }
                var finished = (avState & AVMania.AVPlayerState.Finished) != 0;
                if (IsFinished || finished)
                {
                    Finish();
                    continue;
                }
                var looped = false;
                if (!seeking)
                {
                    var pos = AVMania.AVPlayerGetPosition(id);
                    if (!IsSeeking && (pos < currentTime))
                    {
                        looped = true;
                    }
                    currentTime = pos;
                    //AVMania.Log($"{currentPosition} -> {pos} {IsSeeking} {avClip.IsSeeking} {referenceTime}");
                }
                if (IsSeeking && !seeking)
                {
                    IsSeeking = false;
                    //AVMania.Log("OnSeekCompleted");
                    OnSeekCompleted?.Invoke(this);
                }
                if (IsLooping && looped)
                {
                    //AVMania.Log("OnLoopPointReached");
                    OnLoopPointReached?.Invoke(this);
                }
                if (IsFrameReady)
                {
                    //AVMania.Log("OnFrameReady");
                    OnFrameReady?.Invoke(this);
                }
            }
        }
        private void SetupTextureUpdate()
        {
            {
                commandBuffer = new CommandBuffer();
                commandBuffer.name = "AVPlayer";
            }
            {
                propertyBlock = new();
            }
            textureUpdateCallback = AVMania.AVPlayerGetTextureUpdateCallback();
        }
        private void ReleaseTextureUpdate()
        {
            {
                commandBuffer?.Dispose();
                commandBuffer = null;
            }
            {
                propertyBlock = null;
            }
            textureUpdateCallback = IntPtr.Zero;
        }
        private void Initialize()
        {
            AVMania.Initialize();
            AVMania.AVPlayerInitialize();
            SetupMaterial();
            SetupTextureUpdate();
            SetupStates();
        }
        private void Release()
        {
            ReleaseStates();
            ReleaseTextureUpdate();
            AVMania.AVPlayerRelease();
            AVMania.Release();
        }
        private void Awake()
        {
            //AVMania.Log("Awake");
            Initialize();
        }
        private void OnDestroy()
        {
            //AVMania.Log("OnDestroy");
            Release();
        }
        private void OnEnable()
        {
            //AVMania.Log("OnEnable");
            coroutine = StartCoroutine(UpdateCoroutine());
        }
        private void OnDisable()
        {
            //AVMania.Log("OnDisable");
            Close();
            StopCoroutine(coroutine);
            coroutine = null;
        }
#if UNITY_EDITOR
        private void EditorUpdate()
        {
            //AVMania.Log("EditorUpdate()");
            coroutineEd?.MoveNext();
        }
#endif
        void ITimeControl.OnControlTimeStart()
        {
            //AVMania.Log("OnControlTimeStart()");
            if (this == null)
            {
                return;
            }
            timeControl = true;
            if (!accurately)
            {
                accurately = true;
                AVMania.AVPlayerAccurately(id, true);
            }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Initialize();
                coroutineEd = UpdateCoroutine();
                UnityEditor.EditorApplication.update += EditorUpdate;
            }
#endif
            Play();
        }
        void ITimeControl.OnControlTimeStop()
        {
            //AVMania.Log("OnControlTimeStop()");
            if (this == null)
            {
                return;
            }
            Stop();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                coroutineEd = null;
                Close();
                Release();
                UnityEditor.EditorApplication.update -= EditorUpdate;
            }
#endif
            timeControl = false;
        }
        void ITimeControl.SetTime(double time)
        {
            //AVMania.Log($"SetTime({time})");
            if (this == null)
            {
                return;
            }
            var pos = ValidateTime(playbackSpeed * time + GetClipRange().clipIn);
            if ((pos < referenceTime) || (pos > referenceTime + seekThresholdTime))
            {
                Seek((float)pos);
            }
            else
            {
                referenceTime = pos;
            }
        }
    }
}
