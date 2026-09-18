using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace AVMania
{
    public class AVCapture : MonoBehaviour
    {
        public delegate void OnTextureUpdateDelegate(int trackIndex, CommandBuffer commandBuffer, Texture2D texture, RenderTexture renderTexture);

        [Serializable]
        private struct VideoCaptureSettings
        {
            public bool enabled;
            public int deviceIndex;
        }
        [Serializable]
        private struct AudioCaptureSettings
        {
            public bool enabled;
            public int deviceIndex;
        }
        [Serializable]
        private struct VideoOption
        {
            public VideoFormat videoFormat;
            public int requestWidth;
            public int requestHeight;
            public float requestFps;
        }

        [SerializeField]
        private VideoCaptureSettings videoSettings = default;

        [SerializeField]
        private AudioCaptureSettings audioSettings = default;

        [SerializeField]
        private bool playOnAwake = true;

        [SerializeField]
        private Color color = Color.white;

        [SerializeField]
        private Color altColor = Color.black;

        [SerializeField, Range(0, 1)]
        private float volume = 1.0f;

        [SerializeField, Range(-1200, 1200)]
        private int pitch = 0;

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
        private VideoOption[] videoOptions = null;

        [SerializeField, HideInInspector]
        private Material blitMaterial = null;

        private struct VideoTrackState
        {
            public VideoAttributes attr;
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
            public AudioAttributes attr;
            public AudioRoute activeRoute;
            public OnAudioUpdateDelegate onAudioUpdate;
        }

        private bool setting = true;
        private bool repaint = false;
        private bool startCapture = false;
        private bool preparedAudio = false;
        private bool preparedVideo = false;
        private Coroutine coroutine = null;

        private int audioDevice = -1;
        private int videoDevice = -1;
        private uint audioTrackCount = 0;
        private uint videoTrackCount = 0;

        private VideoTrackState[] videoTrackStates = null;
        private AudioTrackState[] audioTrackStates = null;

        private MaterialPropertyBlock propertyBlock = null;
        private CommandBuffer commandBuffer = null;
        private IntPtr textureUpdateCallback = IntPtr.Zero;

        private static readonly int _Color = Shader.PropertyToID("_Color");
        private static readonly int _Chromakey = Shader.PropertyToID("_Chromakey");
        private static readonly int _ChromakeyLab = Shader.PropertyToID("_ChromakeyLab");
        private static readonly int _ChromakeyParam = Shader.PropertyToID("_ChromakeyParam");
        private static readonly int _MainTex_TexelRegion = Shader.PropertyToID("_MainTex_TexelRegion");
        private static readonly int _Track = Shader.PropertyToID("_Track");
        private static readonly int _Track_TexelRegion = Shader.PropertyToID("_Track_TexelRegion");

        private void SetupMaterial()
        {
            blitMaterial = AVMania.GetUniqueBlitMaterial(blitMaterial);
        }

        private void Reset()
        {
            videoTracks = null;
            audioTracks = null;
            videoOptions = null;
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

        public event Action<AVCapture> OnFrameReady;

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
                AVMania.AVCaptureSetVolume((uint)audioDevice, (uint)trackIndex, v);
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
                AVMania.AVCaptureSetPitch((uint)audioDevice, (uint)trackIndex, p);
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
        public void SetVideoOption(int trackIndex, VideoFormat videoFormat, int requestWidth, int requestHeight, float requestFps)
        {
            if (trackIndex < 0)
            {
                return;
            }
            if (videoOptions == null)
            {
                videoOptions = new VideoOption[trackIndex + 1];
            }
            else if (videoOptions.Length <= trackIndex)
            {
                var nv = new VideoOption[trackIndex + 1];
                for (int i = 0; i < videoOptions.Length; i++)
                {
                    nv[i] = videoOptions[i];
                }
                videoOptions = nv;
            }
            ref var opt = ref videoOptions[trackIndex];
            opt.videoFormat = videoFormat;
            opt.requestWidth = requestWidth;
            opt.requestHeight = requestHeight;
            opt.requestFps = requestFps;
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

        public bool IsStartedVideo => videoDevice >= 0;
        public bool IsStartedAudio => audioDevice >= 0;
        public bool IsPreparedAudioTrack(int trackIndex)
        {
            var track = unchecked((uint)trackIndex);
            if (!IsValidAudioTrack(track) || !preparedAudio)
            {
                return false;
            }
            return true;
        }
        public bool IsPreparedVideo(int trackIndex)
        {
            var track = unchecked((uint)trackIndex);
            if (!IsValidVideoTrack(track) || !preparedVideo)
            {
                return false;
            }
            return true;
        }

        public bool GetVideoAttributes(int trackIndex, out VideoAttributes attributes)
        {
            var track = unchecked((uint)trackIndex);
            if (!IsValidVideoTrack(track) || !preparedVideo)
            {
                attributes = default;
                return false;
            }
            attributes = videoTrackStates[track].attr;
            return true;
        }
        public bool GetAudioAttributes(int trackIndex, out AudioAttributes attributes)
        {
            var track = unchecked((uint)trackIndex);
            if (!IsValidAudioTrack(track) || !preparedAudio)
            {
                attributes = default;
                return false;
            }
            attributes = audioTrackStates[track].attr;
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
            if ((audioDevice < 0) || (trackIndex < 0) || (trackIndex >= audioTrackCount) || (channels <= 0))
            {
                return;
            }
            if (!audioTracks[trackIndex].enabled || (audioTracks[trackIndex].audioRoute != AudioRoute.None))
            {
                return;
            }
            var len = samples.Length / channels;
            var num = len * channels;
            var ptr = AVMania.AVCaptureGetSamples((uint)audioDevice, (uint)trackIndex, (uint)channels, (uint)len);
            System.Runtime.InteropServices.Marshal.Copy(ptr, samples, 0, num);
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
            return (videoDevice >= 0) && (track < videoTrackCount);
        }
        private bool IsValidAudioTrack(uint track)
        {
            return (audioDevice >= 0) && (track < audioTrackCount);
        }


        private void ApplyVolume()
        {
            if (audioDevice < 0)
            {
                return;
            }
            for (uint i = 0; i < audioTrackCount; i++)
            {
                var v = volume * audioTracks[i].volume;
                AVMania.AVCaptureSetVolume((uint)audioDevice, i, v);
            }
        }
        private void ApplyPitch()
        {
            if (audioDevice < 0)
            {
                return;
            }
            for (uint i = 0; i < audioTrackCount; i++)
            {
                var p = Mathf.Clamp(pitch + audioTracks[i].pitch, -1200, +1200);
                AVMania.AVCaptureSetPitch((uint)audioDevice, i, p);
            }
        }

        private bool StartAudioCapture()
        {
            var dev = audioSettings.deviceIndex;
            if (dev < 0)
            {
                return true;
            }
            var dct = AVMania.AVCaptureGetAudioDeviceCount();
            if (dev >= (int)dct)
            {
                AVMania.LogWarning($"Invalid audio device index: {dev}/{dct}");
                return true;
            }
            var tct = AVMania.AVCaptureGetAudioStreamCount((uint)dev);
            if (tct == 0)
            {
                AVMania.LogWarning("no valid tracks");
                return true;
            }

            audioDevice = dev;
            audioTrackCount = tct;

            var outputSampleRate = AudioSettings.outputSampleRate;
            PrepareAudioTracks((int)tct - 1);
            for (uint i = 0; i < tct; i++)
            {
                if (audioTracks[i].enabled)
                {
                    var len = Mathf.Max(audioBuffer * 1000, 33);
                    AVMania.AVCaptureSetupAudioStream((uint)dev, i, true, (uint)len, (uint)outputSampleRate, 9, 22, 17);
                }
                else
                {
                    AVMania.AVCaptureSetupAudioStream((uint)dev, i, false, 0, 0, 0, 0, 0);
                }
            }
            ApplyVolume();
            ApplyPitch();
            if (!AVMania.AVCaptureStartAudio((uint)dev))
            {
                audioDevice = -1;
                return false;
            }
            setting = true;
            return true;
        }
        private bool StartVideoCapture()
        {
            var dev = videoSettings.deviceIndex;
            if (dev < 0)
            {
                return true;
            }
            var dct = AVMania.AVCaptureGetVideoDeviceCount();
            if (dev >= (int)dct)
            {
                AVMania.LogWarning($"Invalid video device index: {dev}/{dct}");
                return true;
            }
            var tct = AVMania.AVCaptureGetVideoStreamCount((uint)dev);
            if (tct == 0)
            {
                AVMania.LogWarning("no valid tracks");
                return true;
            }
            videoDevice = dev;
            videoTrackCount = tct;

            PrepareVideoTracks((int)tct - 1);
            for (uint i = 0; i < tct; i++)
            {
                if (videoTracks[i].enabled)
                {
                    var format = VideoFormat.Void;
                    var width = 0;
                    var height = 0;
                    var fps = 0.0f;
                    if ((videoOptions != null) && (videoOptions.Length > i))
                    {
                        format = videoOptions[i].videoFormat;
                        width = videoOptions[i].requestWidth;
                        height = videoOptions[i].requestHeight;
                        fps = videoOptions[i].requestFps;
                    }
                    AVMania.AVCaptureSetupVideoStream((uint)dev, i, true, format, (uint)width, (uint)height, fps);
                }
                else
                {
                    AVMania.AVCaptureSetupVideoStream((uint)dev, i, false, VideoFormat.Void, 0, 0, 0);
                }
            }
            if (!AVMania.AVCaptureStartVideo((uint)dev))
            {
                videoDevice = -1;
                return false;
            }
            setting = true;
            return true;
        }
        private void StopAudioCapture()
        {
            AVMania.AVCaptureStopAudio((uint)audioDevice);
            audioDevice = -1;
            preparedAudio = false;
        }
        private void StopVideoCapture()
        {
            AVMania.AVCaptureStopVideo((uint)videoDevice);
            videoDevice = -1;
            preparedVideo = false;
        }

        public void StartCapture()
        {
            if ((audioDevice >= 0) || (videoDevice >= 0))
            {
                // already started
                return;
            }
            startCapture = true;
        }
        public void StopCapture()
        {
            startCapture = false;
            if (audioDevice >= 0)
            {
                StopAudioCapture();
            }
            if (videoDevice >= 0)
            {
                StopVideoCapture();
            }
            ClearStates();
            setting = false;
            repaint = true;
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
            if (!preparedVideo || (track >= videoTrackCount))
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
            ref var attr = ref state.attr;
            var width = attr.areaX;
            var height = attr.areaY;
            var size = CalcTextureSize(width, height, conf.alphaSource);
            var aspectNum = (float)attr.aspectRatioNumerator;
            var aspectDen = (float)attr.aspectRatioDenominator;
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
            if (!preparedVideo || (track >= videoTrackCount))
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
            ref var attr = ref state.attr;
            var width = attr.areaX;
            var height = attr.areaY;
            var size = CalcTextureSize(width, height, conf.alphaSource);
            var aspectNum = (float)attr.aspectRatioNumerator;
            var aspectDen = (float)attr.aspectRatioDenominator;
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

        private void SetupTexture(uint track)
        {
            ref var state = ref videoTrackStates[track];
            ref var attr = ref state.attr;
            var width = attr.width;
            var height = attr.height;
            if ((width == 0) || (height == 0))
            {
                return;
            }
            state.region = new Vector4((float)attr.offsetX / width, attr.offsetY / height, (float)attr.areaX / width, (float)attr.areaY / height);
            var bpp = AVMania.GetTextureSize(attr, out int texWidth, out int texHeight);
            var tf = AVMania.GetTextureFormat(attr.videoFormat);
            //AVMania.Log($"{texWidth}x{texHeight} {bpp} {attr.videoFormat} {tf}");
            var tex = state.texture;
            if ((tex != null) && (tex.format != tf))
            {
                AVMania.Destroy(tex);
                tex = null;
            }
            if (tex == null)
            {
                tex = new Texture2D(texWidth, texHeight, tf, false, true);
                tex.hideFlags = HideFlags.DontSave;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Point;
                tex.name = "AVCapture";
                state.texture = tex;
            }
            else if ((tex.width != texWidth) || (tex.height != texHeight))
            {
                tex.Reinitialize(texWidth, texHeight);
                tex.Apply();
            }
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
                    if (state.texture != null)
                    {
                        AVMania.Destroy(state.texture);
                        state.texture = null;
                    }
                    if (state.textureOwner)
                    {
                        state.textureOwner = false;
                        AVMania.Destroy(state.renderTexture);
                        state.renderTexture = null;
                    }
                    continue;
                }
                if (videoDevice >= 0)
                {
                    AVMania.AVCaptureGetVideoAttributes((uint)videoDevice, (uint)i, out state.attr);
                    SetupTexture((uint)i);
                }
                if (conf.targetRenderTexture != null)
                {
                    if (state.textureOwner)
                    {
                        state.textureOwner = false;
                        AVMania.Destroy(state.renderTexture);
                        state.renderTexture = null;
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
                        var width = Mathf.Max(64, state.attr.areaX);
                        var height = Mathf.Max(64, state.attr.areaY);
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
                if ((audioDevice >= 0) && (audioRoute != AudioRoute.None))
                {
                    AVMania.AVCaptureGetAudioAttributes((uint)audioDevice, (uint)i, out state.attr);
                }
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
        private void ClearStates()
        {
            if (videoTrackStates != null)
            {
                for (int i = 0, end = videoTrackStates.Length; i < end; i++)
                {
                    ref var state = ref videoTrackStates[i];
                    state.attr = default;
                }
            }
            if (audioTrackStates != null)
            {
                for (int i = 0, end = audioTrackStates.Length; i < end; i++)
                {
                    ref var state = ref audioTrackStates[i];
                    state.attr = default;
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
                    state.activeRoute = AudioRoute.None;
                    state.onAudioUpdate = null;
                }
            }
            audioTrackStates = null;
        }
        private void UpdateFrame(uint track)
        {
            var tid = AVMania.AVCaptureGetTextureUpdateId((uint)videoDevice, track);
            if (tid != 0)
            {
                ref var state = ref videoTrackStates[track];
                commandBuffer.IssuePluginCustomTextureUpdateV2(textureUpdateCallback, state.texture, tid);
            }
        }
        private void BlitFrame(uint track)
        {
            ref var state = ref videoTrackStates[track];
            var rt = state.renderTexture;
            if (rt == null)
            {
                return;
            }
            ref var conf = ref videoTracks[track];
            ref var attr = ref state.attr;
            if (state.textureOwner)
            {
                var als = conf.alphaSource;
                var size = CalcTextureSize(attr.areaX, attr.areaY, als);
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
                var tex = state.texture;
                state.onTextureUpdate.Invoke((int)track, commandBuffer, tex, rt);
            }
            else
            {
                var als = conf.alphaSource;
                var tex = state.texture;
                if (tex == null)
                {
                    tex = AVMania.whiteTexture;
                }
                commandBuffer.SetGlobalColor(_Color, conf.color * color);
                commandBuffer.SetGlobalVector(_MainTex_TexelRegion, state.region);
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
                        ref var nstate = ref videoTrackStates[alphaTrack];
                        var ntex = nstate.texture;
                        if (ntex == null)
                        {
                            ntex = AVMania.whiteTexture;
                        }
                        commandBuffer.SetGlobalTexture(_Track, ntex);
                        commandBuffer.SetGlobalVector(_Track_TexelRegion, nstate.region);
                    }
                }
                var bf = LocakKeywordMap<BlitFormat>.Keyword(blitMaterial, AVMania.GetBlitFormat(attr.videoFormat));
                var nr = LocakKeywordMap<NominalRange>.Keyword(blitMaterial, attr.nominalRange);
                var tf = LocakKeywordMap<TransferFunction>.Keyword(blitMaterial, attr.transferFunction);
                var ym = LocakKeywordMap<YUVMatrix>.Keyword(blitMaterial, attr.yuvMatrix);
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
            ref var state = ref videoTrackStates[track];
            var rt = state.renderTexture;
            if (rt == null)
            {
                return;
            }
            if (state.textureOwner)
            {
                var width = 64;
                var height = 64;
                if ((rt.width != width) || (rt.height != height))
                {
                    rt.Release();
                    rt.width = width;
                    rt.height = height;
                    rt.Create();
                }
            }
            var tex = AVMania.whiteTexture;
            var reg = new Vector4(0, 0, 1, 1);
            commandBuffer.SetGlobalColor(_Color, videoTracks[track].color * altColor);
            commandBuffer.SetGlobalVector(_MainTex_TexelRegion, reg);
            commandBuffer.Blit(tex, rt, blitMaterial);
        }
        private bool UpdateTexture()
        {
            var frameready = false;
            var rp = repaint;
            repaint = false;
            commandBuffer.Clear();
            var vt = preparedVideo ? videoTrackCount : 0;
            for (uint i = 0; i < vt; i++)
            {
                if (!videoTracks[i].enabled)
                {
                    continue;
                }
                var df = AVMania.AVCaptureIsFrameReady((uint)videoDevice, i);
                if (df)
                {
                    UpdateFrame(i);
                    frameready = true;
                    rp = true;
                }
                ref var attr = ref videoTrackStates[i].attr;
                if (attr.videoFormat != VideoFormat.Void)
                {
                    if (rp)
                    {
                        BlitFrame(i);
                    }
                }
                else
                {
                    if (rp)
                    {
                        BlitWhite(i);
                    }
                }
            }
            if (rp)
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
            return frameready;
        }
        private void OnAudioUpdate(uint track, float[] data, int channels)
        {
            if ((track >= audioTrackStates.Length) || !preparedAudio)
            {
                return;
            }
            var len = data.Length / channels;
            var ptr = AVMania.AVCaptureGetSamples((uint)audioDevice, track, (uint)channels, (uint)len);
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
        public bool IsReady => AVMania.AVCaptureIsReady();

        private IEnumerator UpdateCoroutine()
        {
            //AVMania.Log("UpdateCoroutine");
            SetupStates();
            startCapture = playOnAwake;
            while (!AVMania.AVCaptureIsReady())
            {
                yield return null;
            }
            for (; ; )
            {
                if (startCapture)
                {
                    startCapture = false;
                    if (audioSettings.enabled && (audioDevice < 0))
                    {
                        if (!StartAudioCapture())
                        {
                            startCapture = true;
                        }
                    }
                    if (videoSettings.enabled && (videoDevice < 0))
                    {
                        if (!StartVideoCapture())
                        {
                            startCapture = true;
                        }
                    }
                    if (startCapture)
                    {
                        yield return null;
                        continue;
                    }
                }
                if (setting)
                {
                    SetupStates();
                }
                if (UpdateTexture())
                {
                    OnFrameReady?.Invoke(this);
                }
                yield return null;
                if (audioDevice >= 0)
                {
                    var status = AVMania.AVCaptureGetAudioStatus((uint)audioDevice);
                    var active = (status & AVMania.AVCaptureState.Active) != 0;
                    if (active && !preparedAudio)
                    {
                        preparedAudio = true;
                        setting = true;
                    }
                }
                if (videoDevice >= 0)
                {
                    var status = AVMania.AVCaptureUpdateVideo((uint)videoDevice);
                    var active = (status & AVMania.AVCaptureState.Active) != 0;
                    var changed = (status & AVMania.AVCaptureState.TypeChanged) != 0;
                    if (active && (!preparedVideo || changed))
                    {
                        preparedVideo = true;
                        setting = true;
                    }
                }
            }
        }

        private void SetupTextureUpdate()
        {
            {
                commandBuffer = new();
                commandBuffer.name = "AVCapture";
            }
            {
                propertyBlock = new();
            }
            textureUpdateCallback = AVMania.AVCaptureGetTextureUpdateCallback();
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
        private void Awake()
        {
            AVMania.Initialize();
            AVMania.AVCaptureInitialize();
            SetupMaterial();
            SetupTextureUpdate();
            SetupStates();
            initCount++;
        }
        private void OnDestroy()
        {
            if (--initCount == 0)
            {
                ClearDeviceDescriptors();
            }
            ReleaseStates();
            ReleaseTextureUpdate();
            AVMania.AVCaptureRelease();
            AVMania.Release();
        }
        private void OnEnable()
        {
            coroutine = StartCoroutine(UpdateCoroutine());
        }
        private void OnDisable()
        {
            StopCapture();
            UpdateTexture();
            StopCoroutine(coroutine);
            coroutine = null;
        }



        public class AudioStreamDescriptor
        {
            internal AudioAttributes[] attributes;
            public ReadOnlySpan<AudioAttributes> Descriptor => attributes;
        }
        public class VideoStreamDescriptor
        {
            internal VideoAttributes[] attributes;
            public ReadOnlySpan<VideoAttributes> Descriptor => attributes;
        }
        public class AudioDeviceDescriptor
        {
            public string Name
            {
                get;
                internal set;
            }
            internal AudioStreamDescriptor[] descriptors;
            public ReadOnlySpan<AudioStreamDescriptor> Streams => descriptors;
        }
        public class VideoDeviceDescriptor
        {
            public string Name
            {
                get;
                internal set;
            }
            internal VideoStreamDescriptor[] descriptors;
            public ReadOnlySpan<VideoStreamDescriptor> Streams => descriptors;
        }

        private static int initCount = 0;
        private static AudioDeviceDescriptor[] audioDeviceDescriptors = null;
        private static VideoDeviceDescriptor[] videoDeviceDescriptors = null;

        public ReadOnlySpan<AudioDeviceDescriptor> AudioDeviceDescriptors => audioDeviceDescriptors;
        public ReadOnlySpan<VideoDeviceDescriptor> VideoDeviceDescriptors => videoDeviceDescriptors;

        private void ClearDeviceDescriptors()
        {
            audioDeviceDescriptors = null;
            videoDeviceDescriptors = null;
        }
        public bool SetupDeviceDescriptors()
        {
            if (!AVMania.AVCaptureIsReady())
            {
                return false;
            }
            {
                var ddl = new List<VideoDeviceDescriptor>();
                var sdl = new List<VideoStreamDescriptor>();
                var atl = new List<VideoAttributes>();
                var dc = AVMania.AVCaptureGetVideoDeviceCount();
                for (uint i = 0; i < dc; i++)
                {
                    var dd = new VideoDeviceDescriptor();
                    dd.Name = AVMania.AVCaptureGetVideoDeviceName(i);

                    var sc = AVMania.AVCaptureGetVideoStreamCount(i);
                    for (uint j = 0; j < sc; j++)
                    {
                        var sd = new VideoStreamDescriptor();
                        var k = 0u;
                        while (AVMania.AVCaptureGetVideoStreamDescriptor(i, j, k++, out VideoAttributes at))
                        {
                            atl.Add(at);
                        }
                        sd.attributes = atl.ToArray();
                        atl.Clear();
                        sdl.Add(sd);
                    }
                    dd.descriptors = sdl.ToArray();
                    sdl.Clear();
                    ddl.Add(dd);
                }
                videoDeviceDescriptors = ddl.ToArray();
            }
            {
                var ddl = new List<AudioDeviceDescriptor>();
                var sdl = new List<AudioStreamDescriptor>();
                var atl = new List<AudioAttributes>();
                var dc = AVMania.AVCaptureGetAudioDeviceCount();
                for (uint i = 0; i < dc; i++)
                {
                    var dd = new AudioDeviceDescriptor();
                    dd.Name = AVMania.AVCaptureGetAudioDeviceName(i);

                    var sc = AVMania.AVCaptureGetAudioStreamCount(i);
                    for (uint j = 0; j < sc; j++)
                    {
                        var sd = new AudioStreamDescriptor();
                        var k = 0u;
                        while (AVMania.AVCaptureGetAudioStreamDescriptor(i, j, k++, out AudioAttributes at))
                        {
                            atl.Add(at);
                        }
                        sd.attributes = atl.ToArray();
                        atl.Clear();
                        sdl.Add(sd);
                    }
                    dd.descriptors = sdl.ToArray();
                    sdl.Clear();
                    ddl.Add(dd);
                }
                audioDeviceDescriptors = ddl.ToArray();
            }
            return true;
        }
    }
}
