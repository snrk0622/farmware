// Wall前面2段へ材質共通の32px折れ目影を重ねる製品用Shaderです。
// 上下段は別Materialから開始行0／16を受け取り、段境界でAlphaをリセットせず連続減衰させます。
Shader "Farmware/Terrain/Wall Fold Shadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _ShadowColor ("Shadow Color", Color) = (0, 0, 0, 1)
        _TopAlpha ("Top Alpha", Range(0, 1)) = 0.35
        _FadePixelCount ("Fade Pixel Count", Float) = 32
        _SpritePixelHeight ("Sprite Pixel Height", Float) = 16
        _StartPixelRow ("Start Pixel Row", Float) = 0
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
            Name "TerrainWallFoldShadow"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float objectY : TEXCOORD1;
                half4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                float _TopAlpha;
                float _FadePixelCount;
                float _SpritePixelHeight;
                float _StartPixelRow;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.objectY = input.positionOS.y;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half sourceAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;

                // Tilemapの各セルは整数境界へ配置されます。ローカルYの小数部をセル内0〜1へ戻し、Atlas UV範囲に依存せず画素行を求めます。
                float cellY = frac(input.objectY + 0.00001);
                float pixelRowFromTop = floor((1.0 - cellY) * max(_SpritePixelHeight, 1.0));
                pixelRowFromTop = min(pixelRowFromTop, max(_SpritePixelHeight - 1.0, 0.0));
                float globalPixelRow = _StartPixelRow + pixelRowFromTop;

                clip((_FadePixelCount - 0.5) - globalPixelRow);
                float fadeDenominator = max(_FadePixelCount - 1.0, 1.0);
                float fade = 1.0 - saturate(globalPixelRow / fadeDenominator);
                half alpha = sourceAlpha * input.color.a * _ShadowColor.a * _TopAlpha * fade;
                clip(alpha - (1.0h / 255.0h));
                return half4(_ShadowColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
