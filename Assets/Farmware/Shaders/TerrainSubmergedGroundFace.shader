// 実際の奥岸の真下だけに置く断面です。水では深さで減衰し、マグマでは液面より上の3pxだけを残します。
Shader "Farmware/Terrain/Submerged Ground Face"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Renderer Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TerrainWaterCellData.hlsl"
            // Sprite UVとセルの表示データを分け、Atlasへ詰めた素材も実際の投影段数で合成します。
            struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionHCS:SV_POSITION; float3 positionWS:TEXCOORD0; float2 uv:TEXCOORD1; float4 cellData:TEXCOORD2; half magmaBank:TEXCOORD3; };
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            // セルの段数・条件は全頂点で等しく、素材1px中心の深度はFragだけで計算します。
            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                o.uv = input.uv;
                o.cellData = DecodeWaterCell(input.color);
                // Alpha 0はマグマ岸専用印です。水の奥岸印は128/255なので判定が重なりません。
                o.magmaBank = input.color.a < 0.25h ? 1.0h : 0.0h;
                return o;
            }
            // 他列を平均せず、この断面の実Sourceからの距離だけで、水の被覆率を可視率へ変換します。
            half4 Frag(Varyings input):SV_Target
            {
                // 上端3pxを切るのは水面と青Overlayだけです。この断面は残して水面より上の地面の縁を見せます。
                float depth = ProjectedWaterDistance(input.cellData.x, input.positionWS.y);
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color;
                if (input.magmaBank > 0.5h)
                {
                    // マグマの不透明面より上の縁だけを描き、模様Rendererの有無にかかわらず内部断面を透視しません。
                    clip(3.0 - depth);
                    return source;
                }
                source.a *= 1.0 - WaterCoverOpacity(depth, input.cellData.y);
                return source;
            }
            ENDHLSL
        }
    }
}
