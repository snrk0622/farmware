// 製品Gameビューの淡水・海水Depth Overlay用Shaderです。
// Tile Tint RGBから左右・中央の段開始値を読み、横方向にも補間して16px単位の矩形帯を出さずに描きます。
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

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
                half4 _FreshWaterColor;
                half4 _SaltWaterColor;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                // Tile Colorは描画色ではなく、RGBへ左・中央・右の開始Alpha、Aへ液体種を保持します。
                output.color = input.color;
                // Linear設定のTilemapRendererは頂点RGBを色として変換します。
                // ここにはAlphaの数値データを詰めているため、元の0〜1へ戻してから補間します。
                #ifndef UNITY_COLORSPACE_GAMMA
                    output.color.rgb = LinearToSRGB(input.color.rgb);
                #endif
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float depthInsideCell = 1.0 - frac(input.positionWS.y);
                float depthPixels = depthInsideCell * max(_PixelsPerCell, 1.0);
                float horizontalPosition = saturate(input.uv.x);
                bool isLeftHalf = horizontalPosition < 0.5;
                float interpolation = isLeftHalf
                    ? horizontalPosition + 0.5
                    : horizontalPosition - 0.5;
                float horizontalStartAlpha = isLeftHalf ? input.color.r : input.color.g;
                float horizontalEndAlpha = isLeftHalf ? input.color.g : input.color.b;
                float stageStartAlpha = lerp(
                    horizontalStartAlpha,
                    horizontalEndAlpha,
                    interpolation);
                bool isFirstStage = input.color.g < (0.5 / 255.0);
                float effectivePixels = isFirstStage
                    ? depthPixels - _ClipPixels
                    : depthPixels;
                clip(effectivePixels);

                // 横補間済みの段開始Alphaへ縦1px当たりの増分を足し、どちらのセル境界でも濃度を戻しません。
                half alpha = saturate(
                    stageStartAlpha
                    + (effectivePixels * _MaximumAlpha / max(_FadePixels, 1.0)));
                alpha = min(alpha, _MaximumAlpha);
                half4 sampled = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 depthColor = input.color.a < 0.5h
                    ? _FreshWaterColor
                    : _SaltWaterColor;
                return half4(sampled.rgb * depthColor.rgb * _Color.rgb, sampled.a * alpha);
            }
            ENDHLSL
        }
    }
}
