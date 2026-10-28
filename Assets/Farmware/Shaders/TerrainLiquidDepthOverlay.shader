// 製品Gameビューの淡水・海水Depth Overlay用Shaderです。
// 水域の基底色を不透明に描きます。岸の投影距離で水色全体を薄くせず、断面は別レイヤーで合成します。
Shader "Farmware/Terrain/Liquid Depth Overlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Renderer Tint", Color) = (1, 1, 1, 1)

        _FreshWaterColor ("Fresh Water Depth Color", Color) = (0.094118, 0.282353, 0.376471, 1)
        _SaltWaterColor ("Salt Water Depth Color", Color) = (0.062745, 0.164706, 0.282353, 1)
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
            #include "TerrainWaterCellData.hlsl"

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
                float4 cellData : TEXCOORD2;
                float2 uv : TEXCOORD1;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;

                half4 _FreshWaterColor;
                half4 _SaltWaterColor;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                output.cellData = DecodeWaterCell(input.color);

                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                ClipWaterNorthBank(input.cellData.w, input.positionWS.y);
                half4 sampled = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 depthColor = input.cellData.z < 0.5
                    ? _FreshWaterColor
                    : _SaltWaterColor;
                return half4(sampled.rgb * depthColor.rgb * _Color.rgb, sampled.a * _Color.a);
            }
            ENDHLSL
        }
    }
}
