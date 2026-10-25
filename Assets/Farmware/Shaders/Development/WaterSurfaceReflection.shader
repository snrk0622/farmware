// このShaderは、水面より上に立つWall前面や直立オブジェクトの反射Proxyだけを描画します。
// 水平なGroundTop／WallTopにはProxyを作らないため、斜め見下ろしで不自然な上面反射を仕組み上除外できます。
Shader "Farmware/Development/Water Surface Reflection Proxy"
{
    Properties
    {
        // SpriteRendererがSprite Atlas内の対象領域を自動設定する、Sprite用の元Textureです。
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        // SpriteRenderer.colorと乗算する標準Sprite色です。通常は白のまま使用します。
        _Color ("Renderer Color", Color) = (1, 1, 1, 1)
        _ReflectionTint ("Reflection Tint", Color) = (0.56, 0.82, 0.91, 1)
        _MaximumAlpha ("Maximum Alpha", Range(0, 1)) = 0.22
        _ReflectionAxisWorldY ("Reflection Axis World Y", Float) = 0.5
        _MaximumDepthWorldUnits ("Maximum Depth World Units", Float) = 2
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
            Name "WaterSurfaceReflectionProxy"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            /// SpriteRendererから受け取る頂点位置、Sprite Atlas UV、頂点色です。
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            /// Fragment処理へ渡す画面位置、World位置、Sprite UV、乗算色です。
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                half4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _ReflectionTint;
                float _MaximumAlpha;
                float _ReflectionAxisWorldY;
                float _MaximumDepthWorldUnits;
                float _PixelsPerUnit;
                float _WavePixelsPerSecond;
            CBUFFER_END

            /// 反射ProxyをWorld空間へ変換し、左右だけを整数Pixel単位で揺らします。
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                // 0、+1、0、-1pxを段階的に繰り返し、サブPixel補間によるドットのぼけを避けます。
                float waveStep = fmod(floor(_Time.y * _WavePixelsPerSecond), 4.0);
                float wavePixelOffset = waveStep < 1.0
                    ? 0.0
                    : (waveStep < 2.0 ? 1.0 : (waveStep < 3.0 ? 0.0 : -1.0));
                positionWS.x += wavePixelOffset / max(_PixelsPerUnit, 1.0);

                output.positionWS = positionWS;
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            /// 元の垂直面SpriteをPoint Sampleし、水際から手前へ連続的に消える青い反射として返します。
            half4 Frag(Varyings input) : SV_Target
            {
                float depthFromAxis = _ReflectionAxisWorldY - input.positionWS.y;
                float maximumDepth = max(_MaximumDepthWorldUnits, 0.0001);

                // 反射軸より奥側、または検証上限より手前側は描かず、水面内だけへ反射を限定します。
                clip(depthFromAxis);
                clip(maximumDepth - depthFromAxis);

                // Sprite自身を読む方式なので、Camera画面に見えるGroundTop、床、影、UIは反射へ混入しません。
                half4 sourceColor = SAMPLE_TEXTURE2D(_MainTex, sampler_PointClamp, input.uv);
                sourceColor *= input.color;

                // 水際を最大強度、上限深度を透明として連続的に弱め、水中床の視認性を残します。
                half depthFade = 1.0h - saturate(depthFromAxis / maximumDepth);
                half3 tintedColor = lerp(
                    sourceColor.rgb,
                    sourceColor.rgb * _ReflectionTint.rgb,
                    0.65h);
                half alpha = sourceColor.a
                    * _ReflectionTint.a
                    * _MaximumAlpha
                    * depthFade;
                return half4(tintedColor, alpha);
            }
            ENDHLSL
        }
    }
}
