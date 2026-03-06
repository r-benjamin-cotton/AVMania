//#define AVLISTENER_DEBUG_WRITE_WAV

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AVMania
{
    [RequireComponent(typeof(AudioListener))]
    [DisallowMultipleComponent]
    public class AVListener : MonoBehaviour
    {
#if AVLISTENER_DEBUG_WRITE_WAV
        private DbgWaveWriter waveWrite = null;
#endif
        public event OnAudioUpdateDelegate OnAudioUpdate;

#if AVLISTENER_DEBUG_WRITE_WAV
        private void OnEnable()
        {
            waveWrite = new DbgWaveWriter($"{gameObject.name}.wav", AudioSettings.outputSampleRate, DbgWaveWriter.AudioChannels);
        }
        private void OnDisable()
        {
            waveWrite.Close();
            waveWrite.Dispose();
            waveWrite = null;
        }
#endif
        private void OnAudioFilterRead(float[] data, int channels)
        {
            OnAudioUpdate?.Invoke(data, channels);
#if AVLISTENER_DEBUG_WRITE_WAV
            waveWrite?.Write(data);
#endif
        }
    }
}
