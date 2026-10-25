// 検品用の連続深度Overlayです。
// 乾燥崖の黒化と液体の青化を1枚の矩形内で連続変化させ、タイル境界のAlpha段差を防ぎます。
Shader "Farmware/Development/Continuous Depth Overlay"
{
    Properties
    {
        _OverlayColor ("Overlay Color", Color) = (0.18, 0.50, 0.72, 1)
        _ClippedTopRatio ("Clipped Top Ratio", Range(0, 1)) = 0.01875
        _FadeRatio ("Fade Ratio", Range(0.0001, 1)) = 0.28125
        _MaximumAlpha ("Maximum Alpha", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ContinuousDepthOverlay"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _OverlayColor;
                float _ClippedTopRatio;
                float _FadeRatio;
                float _MaximumAlpha;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // SpriteのUV上端を深度0として、下方向へ0～1の深度を作ります。
                float depthFromTop = 1.0 - input.uv.y;

                // 液体では水面を低く見せる上端3px、乾燥崖では0pxをMaterialから受け取ります。
                // 矩形自体を移動しないため、縦に並ぶタイル同士が重なってAlphaだけ濃くなる問題を避けます。
                clip(depthFromTop - _ClippedTopRatio);

                // 切り欠き後の水深を指定距離で正規化し、タイル境界に依存しない連続勾配を作ります。
                float fadeProgress = saturate(
                    (depthFromTop - _ClippedTopRatio) / max(_FadeRatio, 0.0001));
                half alpha = _OverlayColor.a * _MaximumAlpha * fadeProgress;
                return half4(_OverlayColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
