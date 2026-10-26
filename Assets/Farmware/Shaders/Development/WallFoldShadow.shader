// Wall上面と前面の折れ目を示す、材質共通の検品用陰影Shaderです。
// 16×16 pxのWall前面へ段ごとに重ね、複数段をまたぐ場合も上端から指定px数で黒のAlphaを連続的に完全透過まで下げます。
Shader "Farmware/Development/Wall Fold Shadow"
{
    Properties
    {
        _ShadowColor ("Shadow Color", Color) = (0, 0, 0, 1)
        _TopAlpha ("Top Alpha", Range(0, 1)) = 0.35
        _FadePixelCount ("Fade Pixel Count", Float) = 8
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
            Name "WallFoldShadow"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float objectY : TEXCOORD0;
            };

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
                output.objectY = input.positionOS.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 対象Spriteは16 PPU・中央Pivotの16×16 pxです。
                // ローカル上端+0.5からの距離を画素行へ戻し、Texture内の切り出し位置には依存しないようにします。
                float pixelRowFromTop = floor(
                    (0.5 - input.objectY) * max(_SpritePixelHeight, 1.0));

                // 2段目以降は開始行を16pxずつ進め、32px指定でも段の境目で濃度がリセットされないようにします。
                float globalPixelRow = _StartPixelRow + pixelRowFromTop;

                // 8px指定では0～7行目、16px指定では0～15行目だけを対象とし、指定範囲より下へ余分な黒を描きません。
                clip((_FadePixelCount - 0.5) - globalPixelRow);

                // 最後の対象行をAlpha 0にするため、行数ではなく「行数-1」で正規化します。
                // 8pxなら上から100%から0%まで7等分され、下側へ緩やかに消えます。
                float fadeDenominator = max(_FadePixelCount - 1.0, 1.0);
                float fade = 1.0 - saturate(globalPixelRow / fadeDenominator);
                half alpha = _ShadowColor.a * _TopAlpha * fade;
                return half4(_ShadowColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
