// 製品Gameビューの淡水・海水・マグマSurface用Shaderです。
// 論理Tilemapを移動せず、岸へ接する先頭セルだけ画面奥側3pxを破棄して全液体の水面位置を揃えます。
Shader "Farmware/Terrain/Liquid Surface"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Renderer Tint", Color) = (1, 1, 1, 1)
        _PixelsPerCell ("Pixels Per Cell", Float) = 16
        _ClipPixels ("Top Clip Pixels", Float) = 3
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "TerrainLiquidSurface"
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
                half materialAlpha : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _PixelsPerCell;
                float _ClipPixels;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                // Tile頂点Alphaは通常セル=1、岸側セル=0というクリップ印に使い、見た目のAlphaとは分離します。
                output.color = half4(input.color.rgb * _Color.rgb, input.color.a);
                output.materialAlpha = _Color.a;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 岸側セルだけセル上端からの深さを評価します。内部セルを切らないため16pxごとの横縞が生じません。
                if (input.color.a < 0.5h)
                {
                    float depthInsideCell = 1.0 - frac(input.positionWS.y);
                    float depthPixels = depthInsideCell * max(_PixelsPerCell, 1.0);
                    clip(depthPixels - _ClipPixels);
                }

                half4 sampled = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return half4(
                    sampled.rgb * input.color.rgb,
                    sampled.a * input.materialAlpha);
            }
            ENDHLSL
        }
    }
}
