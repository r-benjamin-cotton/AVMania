using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class SineWave : MonoBehaviour
{
    [SerializeField]
    private float frequency = 220.0f;
    private float phase = 0;
    private int sampleRate = 0;

    private void Awake()
    {
        sampleRate = AudioSettings.outputSampleRate;
    }
    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (sampleRate == 0)
        {
            return;
        }
        var step = frequency / sampleRate;
        for (int i = 0; i < data.Length / channels; i++)
        {
            float v = Mathf.Sin(Mathf.PI * 2.0f * phase) * 0.5f;
            data[i * channels] += v;

            phase += step;
            phase -= (int)phase;
        }
    }
}
