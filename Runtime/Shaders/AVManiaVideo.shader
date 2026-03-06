Shader "Hidden/AVManiaVideo"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ALPHA_SOURCE_ONE _ALPHA_SOURCE_ZERO _ALPHA_SOURCE_BOTTOM _ALPHA_SOURCE_RIGHT _ALPHA_SOURCE_ALPHA _ALPHA_SOURCE_CHROMAKEY _ALPHA_SOURCE_TRACK// _ALPHA_SOURCE_BOTTOM_HALF _ALPHA_SOURCE_RIGHT_HALF
            #pragma multi_compile_local_fragment _BLITFORMAT_NV12 _BLITFORMAT_Y _BLITFORMAT_YUY2 _BLITFORMAT_AYUV _BLITFORMAT_ARGB
            #pragma multi_compile_local_fragment _NOMINAL_RANGE_16_235 _NOMINAL_RANGE_0_255
            #pragma multi_compile_local_fragment _TRANSFER_FUNCTION_709 _TRANSFER_FUNCTION_10 _TRANSFER_FUNCTION_18 _TRANSFER_FUNCTION_20 _TRANSFER_FUNCTION_22 _TRANSFER_FUNCTION_240M _TRANSFER_FUNCTION_SRGB
            #pragma multi_compile_local_fragment _YUV_MATRIX_BT709 _YUV_MATRIX_BT601 _YUV_MATRIX_SMPTE
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
#if _ALPHA_SOURCE_BOTTOM || _ALPHA_SOURCE_RIGHT// || _ALPHA_SOURCE_BOTTOM_HALF || _ALPHA_SOURCE_RIGHT_HALF
                float3 uv : TEXCOORD0;
#else
                float2 uv : TEXCOORD0;
#endif
#if _ALPHA_SOURCE_TRACK
                float2 uv1 : TEXCOORD1;
#endif
            };

            Texture2D _MainTex;
            SamplerState sampler_MainTex;
            float4 _MainTex_TexelSize;
            float4 _MainTex_TexelRegion;
            float4 _Color;
#if _ALPHA_SOURCE_TRACK
            Texture2D _Track;
            float4 _Track_TexelSize;
            float4 _Track_TexelRegion;
#endif

#if _ALPHA_SOURCE_CHROMAKEY
            float3 _Chromakey;
            float3 _ChromakeyLab;
            float2 _ChromakeyParam;
#endif

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float2 uv = v.uv;
                // flip y
                uv.y = 1 - uv.y;
#if _ALPHA_SOURCE_BOTTOM
                o.uv.x = uv.x * _MainTex_TexelRegion.z + _MainTex_TexelRegion.x;
                o.uv.y = (uv.y * 0.5) *_MainTex_TexelRegion.w + _MainTex_TexelRegion.y;
                o.uv.z = (uv.y * 0.5 + 0.5) * _MainTex_TexelRegion.w + _MainTex_TexelRegion.y;
#elif 0//_ALPHA_SOURCE_BOTTOM_HALF
                o.uv.x = uv.x * _MainTex_TexelRegion.z + _MainTex_TexelRegion.x;
                o.uv.y = (uv.y * 0.75) * _MainTex_TexelRegion.w + _MainTex_TexelRegion.y;
                o.uv.z = (uv.y * 0.25 + 0.75) * _MainTex_TexelRegion.w + _MainTex_TexelRegion.y;
#elif _ALPHA_SOURCE_RIGHT
                o.uv.x = (uv.x * 0.5) * _MainTex_TexelRegion.z + _MainTex_TexelRegion.x;
                o.uv.y = uv.y * _MainTex_TexelRegion.w + _MainTex_TexelRegion.y;
                o.uv.z = (uv.x * 0.5 + 0.5) * _MainTex_TexelRegion.z + _MainTex_TexelRegion.x;
#elif 0//_ALPHA_SOURCE_RIGHT_HALF
                o.uv.x = (uv.x * 0.75) * _MainTex_TexelRegion.z + _MainTex_TexelRegion.x;
                o.uv.y = uv.y * _MainTex_TexelRegion.w + _MainTex_TexelRegion.y;
                o.uv.z = (uv.x * 0.25 + 0.75) * _MainTex_TexelRegion.z + _MainTex_TexelRegion.x;
#else
                o.uv = uv * _MainTex_TexelRegion.zw + _MainTex_TexelRegion.xy;
#endif
#if _ALPHA_SOURCE_TRACK
                o.uv1 = uv * _Track_TexelRegion.zw + _Track_TexelRegion.xy;
#endif
                return o;
            }



            half3 rgb2lab(half3 rgb)
            {
                half xn = 100.0 / 95.05;
                half yn = 100.0 / 100.00;
                half zn = 100.0 / 108.90;
                half x = dot(rgb, half3(0.4124, 0.3576, 0.1805) * xn);
                half y = dot(rgb, half3(0.2126, 0.7152, 0.0722) * yn);
                half z = dot(rgb, half3(0.0193, 0.1192, 0.9505) * zn);
                half s = 6.0 / 29.0;
                half sss = s * s * s;
                half iss3 = 1.0f / (3.0 * s * s);
                half fx = (x > sss) ? pow(x, 1.0 / 3.0) : ((x * iss3) + (4.0 / 29.0));
                half fy = (y > sss) ? pow(y, 1.0 / 3.0) : ((y * iss3) + (4.0 / 29.0));
                half fz = (z > sss) ? pow(z, 1.0 / 3.0) : ((z * iss3) + (4.0 / 29.0));
                half3 lab = half3((116.0 * fy) - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
                return lab;
            }

            half gamma2linear(half y)
            {
#if _TRANSFER_FUNCTION_10
                half l = y;
#elif _TRANSFER_FUNCTION_18
                half l = pow(y, 1.8);
#elif _TRANSFER_FUNCTION_20
                half l = pow(y, 2.0);
#elif _TRANSFER_FUNCTION_22
                half l = pow(y, 2.2);
#elif _TRANSFER_FUNCTION_240M
                half l = (y < 0.0913) ? y * (1 / 4) :  pow(y * (1.0 / 1.1115) + (0.1115 / 1.1115), 1 / 0.45);
#elif _TRANSFER_FUNCTION_SRGB
                half l = (y <= 0.04045) ? y * (1.0 / 12.92) :  pow(y * (1.0 / 1.055) + (0.055 / 1.055), 2.4);
#else //_TRANSFER_FUNCTION_709
                half l = (y < 0.0812) ? y * (1 / 4.5) :  pow(y * (1.0 / 1.099) + (0.099 / 1.099), 1 / 0.45);
#endif
                return l;
            }

            half3 gamma2linear(half3 rgb)
            {
#if _TRANSFER_FUNCTION_10
                rgb = rgb;
#elif _TRANSFER_FUNCTION_18
                rgb = pow(rgb, 1.8);
#elif _TRANSFER_FUNCTION_20
                rgb = pow(rgb, 2.0);
#elif _TRANSFER_FUNCTION_22
                rgb = pow(rgb, 2.2);
#elif _TRANSFER_FUNCTION_240M
                rgb = (rgb < 0.0913) ? rgb * (1 / 4) :  pow(rgb * (1.0 / 1.1115) + (0.1115 / 1.1115), 1 / 0.45);
#elif _TRANSFER_FUNCTION_SRGB
                rgb = (rgb <= 0.04045) ? rgb * (1.0 / 12.92) :  pow(rgb * (1.0 / 1.055) + (0.055 / 1.055), 2.4);
#else //_TRANSFER_FUNCTION_709
                rgb = (rgb < 0.0812) ? rgb * (1 / 4.5) :  pow(rgb * (1.0 / 1.099) + (0.099 / 1.099), 1 / 0.45);
#endif
                return rgb;
            }

            half y2l(half y)
            {
#if _NOMINAL_RANGE_0_255
                y = y;
#else // _NOMINAL_RANGE_16_235
                y = (y * (255.0 / 219.0)) - (16.0 / 219.0);
#endif
                return gamma2linear(y);
            }

            half3 yuv2rgb(half3 ycbcr)
            {
#if _NOMINAL_RANGE_0_255
                half3 yuv = ycbcr - half3(0.0, 0.5, 0.5);
#else //_NOMINAL_RANGE_16_235
                half3 yuv = ycbcr * half3(255.0 / 219.0, 128.0 / 112.0, 128.0 / 112.0) - half3(16.0 / 219.0, 128.0 / 224.0, 128.0 / 224.0);
#endif
                float4 mx;
#if _YUV_MATRIX_BT601
                mx = float4(1.402000, -0.344136, -0.714136, 1.772000);
#elif _YUV_MATRIX_SMPTE
                mx = float4(1.576000, -0.227000, -0.477000, 1.826000);
#else // _YUV_MATRIX_BT709
                mx = float4(1.574800, -0.187324, -0.468124, 1.855600);
#endif
                half r = yuv.x + yuv.z * mx.x;
                half g = yuv.x + yuv.y * mx.y + yuv.z * mx.z;
                half b = yuv.x + yuv.y * mx.w;
                half3 rgb = saturate(half3(r, g, b));

                return gamma2linear(rgb);
            }


            half4 sampleNV12(Texture2D tex, SamplerState state, float2 uv, float4 texelSize)
            {
                float2 y_uv = float2(uv.x, uv.y * (2.0 / 3.0));
                float uvu = floor(uv.x * texelSize.z * 0.5) * texelSize.x * 2.0 + texelSize.x * 0.5;
                float uvv = (2.0 / 3.0) + uv.y * (1.0 / 3.0);
                float2 u_uv = float2(uvu, uvv);
                float2 v_uv = float2(uvu + texelSize.x, uvv);
                half3 yuv;
                yuv.x = tex.SampleLevel(state, y_uv, 0).r;
                yuv.y = tex.SampleLevel(state, u_uv, 0).r;
                yuv.z = tex.SampleLevel(state, v_uv, 0).r;
                half4 rgba;
                rgba.rgb = yuv2rgb(yuv);
#if _ALPHA_SOURCE_ALPHA
                rgba.a = y2l(yuv.r);
                if (rgba.a != 0)
                {
                    rgba.rgb = saturate(rgba.rgb / rgba.a);
                }
#else
                rgba.a = 1;
#endif
                return rgba;
            }
            half4 sampleYUY2(Texture2D tex, SamplerState state, float2 uv, float4 texelSize)
            {
                half3 yuv;
                float fx = frac(uv.x * texelSize.z * 0.5);
                if (fx < 0.5)
                {
                    yuv.xy = tex.SampleLevel(state, uv, 0).rg;
                    yuv.z = tex.SampleLevel(state, float2(uv.x + texelSize.x, uv.y), 0).g;
                }
                else
                {
                    yuv.xz = tex.SampleLevel(state, uv, 0).rg;
                    yuv.y = tex.SampleLevel(state, float2(uv.x - texelSize.x, uv.y), 0).g;
                }
                half4 rgba;
                rgba.rgb = yuv2rgb(yuv);
#if _ALPHA_SOURCE_ALPHA
                rgba.a = y2l(yuv.r);
                if (rgba.a != 0)
                {
                    rgba.rgb = saturate(rgba.rgb / rgba.a);
                }
#else
                rgba.a = 1;
#endif
                return rgba;
            }
            half4 sampleY(Texture2D tex, SamplerState state, float2 uv)
            {
                half y = tex.SampleLevel(state, uv, 0).r;
                half4 rgba;
#if _ALPHA_SOURCE_ALPHA
                rgba.rgb = 1;
                rgba.a = y2l(y);
#else
                rgba.rgb = y2l(y).xxx;
                rgba.a = 1;
#endif
                return rgba;
            }
            half4 sampleARGB(Texture2D tex, SamplerState state, float2 uv)
            {
                half4 rgba = tex.SampleLevel(state, uv, 0).rgba;
                rgba.rgb = gamma2linear(rgba.rgb);
                return rgba;
            }
            half4 sampleAYUV(Texture2D tex, SamplerState state, float2 uv)
            {
                half4 ayuv = tex.SampleLevel(state, uv, 0).rgba;
                half4 rgba;
                rgba.rgb = yuv2rgb(ayuv.rgb);
                rgba.a = ayuv.a;
                return rgba;
            }


            half4 sampleNV12Luminance(Texture2D tex, SamplerState state, float2 uv, float4 texelSize)
            {
                float2 y_uv = float2(uv.x, uv.y * (2.0 / 3.0));
                half y = tex.SampleLevel(state, y_uv, 0).r;
                return y2l(y);
            }
            half4 sampleYUY2Luminance(Texture2D tex, SamplerState state, float2 uv, float4 texelSize)
            {
                half y = tex.SampleLevel(state, uv, 0).r;
                return y2l(y);
            }
            half4 sampleYLuminance(Texture2D tex, SamplerState state, float2 uv)
            {
                half y = tex.SampleLevel(state, uv, 0).r;
                return y2l(y);
            }
            half4 sampleARGBLuminance(Texture2D tex, SamplerState state, float2 uv)
            {
                half3 rgb = tex.SampleLevel(state, uv, 0).rgb;
                return gamma2linear(max(max(rgb.r, rgb.g), rgb.b));
            }
            half4 sampleAYUVLuminance(Texture2D tex, SamplerState state, float2 uv)
            {
                half y = tex.SampleLevel(state, uv, 0).r;
                return y2l(y);
            }

            half4 sampleColor(Texture2D tex, SamplerState state, float2 uv, float4 texelSize)
            {
#if 0
                uv  = (floor(uv * texelSize.zw) + 0.5) * texelSize.xy;
#endif
#if _BLITFORMAT_Y
                half4 col = sampleY(tex, state, uv);
#elif _BLITFORMAT_YUY2
                half4 col = sampleYUY2(tex, state, uv, texelSize);
#elif _BLITFORMAT_ARGB
                half4 col = sampleARGB(tex, state, uv);
#elif _BLITFORMAT_AYUV
                half4 col = sampleAYUV(tex, state, uv);
#else //_BLITFORMAT_NV12
                half4 col = sampleNV12(tex, state, uv, texelSize);
#endif
                return col;
            }
            half sampleLuminance(Texture2D tex, SamplerState state, float2 uv, float4 texelSize)
            {
#if 0
                uv  = (floor(uv * texelSize.zw) + 0.5) * texelSize.xy;
#endif
#if _BLITFORMAT_Y
                half l = sampleYLuminance(tex, state, uv);
#elif _BLITFORMAT_YUY2
                half l = sampleYUY2Luminance(tex, state, uv, texelSize);
#elif _BLITFORMAT_ARGB
                half l = sampleARGBLuminance(tex, state, uv);
#elif _BLITFORMAT_AYUV
                half l = sampleAYUVLuminance(tex, state, uv);
#else //_BLITFORMAT_NV12
                half l = sampleNV12Luminance(tex, state, uv, texelSize);
#endif
                return l;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 col = sampleColor(_MainTex, sampler_MainTex, i.uv.xy, _MainTex_TexelSize);

#if _ALPHA_SOURCE_BOTTOM// || _ALPHA_SOURCE_BOTTOM_HALF
                col.a = sampleLuminance(_MainTex, sampler_MainTex, float2(i.uv.x, i.uv.z), _MainTex_TexelSize);
#elif _ALPHA_SOURCE_RIGHT// || _ALPHA_SOURCE_RIGHT_HALF
                col.a = sampleLuminance(_MainTex, sampler_MainTex, float2(i.uv.z, i.uv.y), _MainTex_TexelSize);
#elif _ALPHA_SOURCE_ALPHA
                col.a = col.a;
#elif _ALPHA_SOURCE_CHROMAKEY
                half3 lab = rgb2lab(col.rgb);
                half dc = length(_ChromakeyLab - lab);
                half a = saturate((dc - _ChromakeyParam.x) * _ChromakeyParam.y);
                col.rgb = (a == 0.0) ? 1.0 : (saturate(col.rgb - (_Chromakey.rgb * (1.0 - a))) * (1.0 / a));
                col.a = a;
#elif _ALPHA_SOURCE_TRACK
                col.a = sampleLuminance(_Track, sampler_MainTex, i.uv1, _Track_TexelSize);
#elif _ALPHA_SOURCE_ZERO
                col.a = 0.0;
#else //_ALPHA_SOURCE_ONE
                col.a = 1.0;
#endif
#if UNITY_COLORSPACE_GAMMA
                col.rgb = (col.rgb <= 0.0031308) ? (col.rgb * 12.92) : (pow(col.rgb, 1.0 / 2.4) * 1.055 - 0.055);
#endif
                return col * _Color;
            }

            ENDCG
        }
    }
}
