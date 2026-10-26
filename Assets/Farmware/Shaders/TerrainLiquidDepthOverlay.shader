// 製品Gameビューの淡水・海水Depth Overlay用Shaderです。
// Tile Tint Alphaを16px段の開始値として読み、3pxクリップ後の45pxまたは93px勾配を段境界で濃く戻さず描きます。
Shader "Farmware/Terrain/Liquid Depth Overlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Renderer Tint", Color) = (1, 1, 1, 1)
        _PixelsPerCell ("Pixels Per Cell", Float) = 16
        _ClipPixels ("Top Clip Pixels", Float) = 3
        _FadePixels ("Fade Pixels After Clip", Float) = 45
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
            Name "TerrainLiquidDepthOverlay"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half4 color : COLOR;
                float2 uv : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _PixelsPerCell;
                float _ClipPixels;
                float _FadePixels;
                float _MaximumAlpha;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color * _Color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float depthInsideCell = 1.0 - frac(input.positionWS.y);
                float depthPixels = depthInsideCell * max(_PixelsPerCell, 1.0);
                bool isFirstStage = input.color.a < (0.5 / 255.0);
                float effectivePixels = isFirstStage
                    ? depthPixels - _ClipPixels
                    : depthPixels;
                clip(effectivePixels);

                // 段開始Alphaへ1px当たりの増分を足し、16px境界でも同じ直線勾配を継続します。
                half alpha = saturate(
                    input.color.a
                    + (effectivePixels * _MaximumAlpha / max(_FadePixels, 1.0)));
                alpha = min(alpha, _MaximumAlpha);
                half4 sampled = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return half4(sampled.rgb * input.color.rgb, sampled.a * alpha);
            }
            ENDHLSL
        }
    }
}
