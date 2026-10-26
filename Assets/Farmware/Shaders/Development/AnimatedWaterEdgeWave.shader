// 水際検品用の1px波線Shaderです。
// 2色のPixel帯を整数画素単位で流し、飛沫ではなく静かな水面の接触線として見せます。
Shader "Farmware/Development/Animated Water Edge Wave"
{
    Properties
    {
        _FirstColor ("First Color", Color) = (0.34, 0.71, 0.84, 1)
        _SecondColor ("Second Color", Color) = (0.67, 0.90, 0.93, 1)
        _PixelLength ("Pixel Length", Float) = 48
        _PixelsPerSecond ("Pixels Per Second", Float) = 8
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
            Name "AnimatedWaterEdgeWave"
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
                half4 _FirstColor;
                half4 _SecondColor;
                float _PixelLength;
                float _PixelsPerSecond;
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
                // 時間を整数化して1pxずつ進めることで、Pixel間を補間する滑らかな移動を避けます。
                float animationOffset = floor(_Time.y * _PixelsPerSecond);
                float pixelPosition = floor(input.uv.x * max(_PixelLength, 1.0));

                // 2px幅の帯を2色で交互にし、連続線の中に読み取れる流れを作ります。
                float colorIndex = fmod(floor((pixelPosition + animationOffset) * 0.5), 2.0);
                return lerp(_FirstColor, _SecondColor, colorIndex);
            }
            ENDHLSL
        }
    }
}
