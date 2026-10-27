// 製品Gameビューの水面反射Proxy用Shaderです。
// 垂直面SpriteだけをPoint Sampleし、画素行ごとに位相をずらした-1/0/+1pxの横揺れと深度減衰を適用します。
Shader "Farmware/Terrain/Water Reflection Proxy"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Renderer Tint", Color) = (1, 1, 1, 1)
        _ReflectionTint ("Reflection Tint", Color) = (0.56, 0.82, 0.91, 1)
        _MaximumAlpha ("Maximum Alpha", Range(0, 1)) = 0.22
        _PixelsPerUnit ("Pixels Per Unit", Float) = 16
        _WavePixelsPerSecond ("Wave Pixels Per Second", Float) = 4
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
            Name "TerrainWaterReflectionProxy"
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
                float reflectionDepthPixels : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _ReflectionTint;
                float _MaximumAlpha;
                float _PixelsPerUnit;
                float _WavePixelsPerSecond;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                // TileのAlphaは1〜2/255の段数データなので、実際の透明度とは分離します。
                output.color = half4(input.color.rgb * _Color.rgb, _Color.a);
                output.uv = input.uv;
                float stage = round(input.color.a * 255.0) - 1.0;
                // Runtime Tileと同じ0.75圧縮・中心pivot・3px水面位置です。段ごとの補正を戻すと
                // 各Quadの全頂点から同じセル原点が得られ、Atlas UVに依存せず実水面を復元できます。
                const float compression = 0.75;
                const float surfaceOffset = (1.0 - compression) * 0.5 - 3.0 / 16.0;
                float cellBottom = floor(output.positionWS.y - stage * (1.0 - compression) - surfaceOffset);
                float surfaceY = cellBottom + stage + 1.0 - 3.0 / 16.0;
                output.reflectionDepthPixels = stage >= 0.0 && stage <= 1.0
                    ? (surfaceY - output.positionWS.y) * 16.0 : -1.0;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 接触線より上へ反射を出さず、2枚を通した24px全体で連続的に消します。
                clip(input.reflectionDepthPixels);
                float visibility = saturate(1.0 - input.reflectionDepthPixels / 24.0);
                // 画素行を時間位相へ加え、隣接行が同時に一枚板として移動しない水面らしい揺れにします。
                float pixelRow = floor(input.positionWS.y * max(_PixelsPerUnit, 1.0));
                float animationStep = floor(_Time.y * _WavePixelsPerSecond);
                float phase = fmod(fmod(animationStep + pixelRow, 4.0) + 4.0, 4.0);
                float horizontalPixelOffset = phase < 1.0
                    ? 0.0
                    : (phase < 2.0 ? 1.0 : (phase < 3.0 ? 0.0 : -1.0));
                float2 shiftedUv = input.uv
                    - float2(horizontalPixelOffset * _MainTex_TexelSize.x, 0.0);

                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_PointClamp, shiftedUv);
                half originalSilhouette = SAMPLE_TEXTURE2D(
                    _MainTex,
                    sampler_PointClamp,
                    input.uv).a;
                source *= input.color;

                // 元シルエット外へずれた画素を透明にし、反射の輪郭が横揺れで膨張することを防ぎます。
                half3 tintedColor = lerp(
                    source.rgb,
                    source.rgb * _ReflectionTint.rgb,
                    0.65h);
                half alpha = source.a
                    * originalSilhouette
                    * _ReflectionTint.a
                    * _MaximumAlpha
                    * visibility;
                return half4(tintedColor, alpha);
            }
            ENDHLSL
        }
    }
}
