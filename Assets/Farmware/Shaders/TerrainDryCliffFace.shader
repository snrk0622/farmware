// 製品Gameビューの乾燥崖Face用Shaderです。
// 頂点Alphaが正なら乾燥崖の48px減衰、0なら水中断面の不透明下地として扱い、製品深度Overlayへ渡します。
Shader "Farmware/Terrain/Dry Cliff Face"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Renderer Tint", Color) = (1, 1, 1, 1)
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
            Name "TerrainDryCliffFace"
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
                half4 sampled = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // Tileの上端から下端へ0～1を作り、48pxのうち現在16px段が占める1/3だけAlphaを減らします。
                // 開始Alphaは上から1、2/3、1/3なので、段境界で濃さが戻らず3段目下端で0になります。
                float depthInsideCell = 1.0 - frac(input.positionWS.y);
                // Alpha 0は水中断面の識別値です。乾燥減衰を外し、水の濃さは前面のLiquid深度Overlayだけで決めます。
                bool isSubmergedFace = input.color.a < (0.5h / 255.0h);
                half visibleAlpha = isSubmergedFace
                    ? 1.0h
                    : saturate(input.color.a - (depthInsideCell / 3.0));
                return half4(sampled.rgb * input.color.rgb, sampled.a * visibleAlpha);
            }
            ENDHLSL
        }
    }
}
