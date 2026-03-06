using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using AVMania;
using System.Text;

public class DebugLogCaptureDeviceDescriptors : MonoBehaviour
{
    [SerializeField]
    private AVCapture avCapture = null;

    private void Reset()
    {
        avCapture = FindObjectOfType<AVCapture>();
    }
    private IEnumerator Start()
    {
        if (avCapture == null)
        {
            yield break;
        }
        while (!avCapture.IsReady)
        {
            yield return null;
        }
        avCapture.SetupDeviceDescriptors();

        var sb = new StringBuilder();
        {
            var vds = avCapture.VideoDeviceDescriptors;
            for (int i = 0; i < vds.Length; i++)
            {
                var vd = vds[i];
                sb.AppendLine($"device: {i}: {vd.Name}");
                var sds = vd.Streams;
                for (int j = 0; j < sds.Length; j++)
                {
                    sb.AppendLine($"  stream: {j}");
                    var sd = sds[j];
                    for (int k = 0; k < sd.Descriptor.Length; k++)
                    {
                        var at = sd.Descriptor[k];
                        sb.AppendLine($"    {k}:");
                        sb.AppendLine($"      VideoFormat: {at.videoFormat}");
                        sb.AppendLine($"      Width: {at.width}");
                        sb.AppendLine($"      Height: {at.height}");
                        sb.AppendLine($"      AspectRatio: {(float)at.aspectRatioNumerator / at.aspectRatioDenominator}");
                        sb.AppendLine($"      FrameRate: {(float)at.frameRateNumerator / at.frameRateDenominator}");
                        sb.AppendLine($"      YUVMatrix: {at.yuvMatrix}");
                        sb.AppendLine($"      NominalRange: {at.nominalRange}");
                        sb.AppendLine($"      TransferFunction: {at.transferFunction}");
                    }
                }
            }
        }
        sb.AppendLine();
        {
            var ads = avCapture.AudioDeviceDescriptors;
            for (int i = 0; i < ads.Length; i++)
            {
                var vd = ads[i];
                sb.AppendLine($"device: {i}: {vd.Name}");
                var sds = vd.Streams;
                for (int j = 0; j < sds.Length; j++)
                {
                    sb.AppendLine($"  stream: {j}");
                    var sd = sds[i];
                    for (int k = 0; k < sd.Descriptor.Length; k++)
                    {
                        var at = sd.Descriptor[k];
                        sb.AppendLine($"    {k}:");
                        sb.AppendLine($"      SamplesPerSecond: {at.samplesPerSecond}");
                        sb.AppendLine($"      NumChannels: {at.numChannels}");
                        sb.AppendLine($"      ChannelMask: {at.channelMask}");
                    }
                }
            }
        }
        Debug.Log(sb.ToString());
    }
}
