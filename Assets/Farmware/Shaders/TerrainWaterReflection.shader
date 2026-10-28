// 接地点から投影し、水面で切り抜いた反射Meshを描きます。
// 絶対画素位置の固定中心ごとに周期を変え、低FPS・整数pxの横揺れだけを適用します。
Shader "Farmware/Terrain/Water Reflection Proxy"
{
    Properties
    {
        [PerRendererData] _MainTex ("共有Atlas (自動設定)", 2D) = "white" {}
        _Color ("元画像の色補正", Color) = (1,1,1,1)
        _ReflectionTint ("反射の色補正", Color) = (0.56,0.82,0.91,1)
        _MaximumAlpha ("反射の最大濃度", Range(0,1)) = 0.22
        [IntRange] _WaveAmplitudePixels ("横揺れの最大幅 (px)", Range(0,2)) = 1
        _WaveSpacingPixels ("横方向のまとまり (px)", Range(16,128)) = 32
        _WaveBandPixels ("縦方向のまとまり (px)", Range(2,12)) = 4
        _OscillationPeriodSeconds ("揺れの基準周期 (秒)", Range(1,12)) = 4
        _PeriodVariation ("周期のばらつき (割合)", Range(0,0.5)) = 0.35
        [IntRange] _AnimationStepsPerSecond ("更新回数 (回/秒)", Range(1,12)) = 2
        _AnchorPixels ("付け根を安定させる長さ (px)", Range(0,8)) = 2
        [HideInInspector] _AnimationTimeSeconds ("共通時計 (秒)", Float) = 0
        [HideInInspector] _WorldPixelOffset ("絶対画素原点", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "TerrainWaterReflectionProxy"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Meshは元画像の矩形と接地点からの距離を明示します。Tileの段数やAtlas配置から推測しません。
            struct Attributes
            {
                float4 positionOS : POSITION; // チャンク内の描画位置（タイル単位）です。
                float2 uv : TEXCOORD0; // 水域で切り抜いた元SpriteのUVです。
                float4 bounds : TEXCOORD1; // 元Sprite全体のAtlas UV矩形です。
                float3 depth : TEXCOORD2; // 元画像の深度・減衰終端・受け手水面からの深度（px）です。
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION; // GPU描画位置です。
                float2 positionWS : TEXCOORD0; // 表示原点相対のワールド座標です。
                float2 uv : TEXCOORD1; // 元画像を読むUVです。
                float4 bounds : TEXCOORD2; // 隣Spriteを読ませない境界です。
                float3 depth : TEXCOORD3; // 元画像の減衰と、受け手ごとに共通の揺れ深度です。
            };
            TEXTURE2D(_MainTex); // バッチが借用するAtlasです。
            float4 _MainTex_TexelSize; // 借用Atlasの逆寸法・寸法です。
            CBUFFER_START(UnityPerMaterial)
                float4 _Color; // 元画像の色補正です。
                float4 _ReflectionTint; // 水色へ寄せる補正です。
                float _MaximumAlpha; // 静止した最大濃度です。時間では変えません。
                float _WaveAmplitudePixels; // 整数pxの最大横変位です。
                float _WaveSpacingPixels; // 固定中心の横間隔の目安（px）です。
                float _WaveBandPixels; // 固定中心の縦間隔の目安（px）です。
                float _OscillationPeriodSeconds; // 中心ごとの基準周期（秒）です。
                float _PeriodVariation; // 基準周期からのばらつき率です。
                float _AnimationStepsPerSecond; // 秒あたりのコマ更新数です。
                float _AnchorPixels; // 揺らさない付け根の長さ（px）です。
                float _AnimationTimeSeconds; // Presenterが渡す共通時計です。
                float4 _WorldPixelOffset; // 4096px周期内の絶対原点です。
            CBUFFER_END
            // 位置はCPUで水域と交差済みです。頂点を揺らさず、画像の読取位置だけを横へ変えます。
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(world);
                output.positionWS = world.xy;
                output.uv = input.uv; output.bounds = input.bounds; output.depth = input.depth;
                return output;
            }
            // 固定中心の整数IDを混ぜ、各中心の位相・周期・強さを決めます。
            uint HashNode(uint2 node)
            {
                uint value = node.x * 0x9e3779b9u ^ node.y * 0x85ebca6bu ^ 0x68bc21ebu;
                value ^= value >> 16; value *= 0x7feb352du;
                value ^= value >> 15; value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
            // 毎コマの乱数ではなく、その場で往復する連続値を返します。最終的な変位だけ整数へ丸めます。
            float NodeDisplacement(uint2 node, float time)
            {
                uint hash = HashNode(node);
                float phase = (hash & 65535u) / 65536.0;
                float choice = (hash >> 16) / 65535.0;
                float variation = clamp(_PeriodVariation,0.0,0.5);
                float period = max(_OscillationPeriodSeconds,1.0) * lerp(1.0-variation,1.0+variation,choice);
                return cos(6.2831853 * (time / period + phase)) * lerp(0.65,1.0,choice);
            }
            // 周囲4中心を滑らかに混ぜ、格子の端やタイル境界で乱数の塊が切れないようにします。
            float EvaluateDisplacement(float2 pixel, float depth)
            {
                uint2 count = (uint2)max(round(4096.0 / max(float2(_WaveSpacingPixels,_WaveBandPixels),1.0)),1.0);
                float2 location = frac(pixel / 4096.0) * count;
                uint2 node = (uint2)floor(location) % count;
                float2 f = frac(location); f = f*f*(3.0-2.0*f);
                float fps = max(round(_AnimationStepsPerSecond),1.0);
                float time = floor(_AnimationTimeSeconds * fps) / fps;
                float lower = lerp(NodeDisplacement(node,time),
                    NodeDisplacement((node+uint2(1,0))%count,time),f.x);
                float upper = lerp(NodeDisplacement((node+uint2(0,1))%count,time),
                    NodeDisplacement((node+uint2(1,1))%count,time),f.x);
                // 付け根は0、奥は最大幅へ近づけます。水位や反射の位置自体は動かしません。
                float envelope = saturate((depth-_AnchorPixels) / max(12.0-_AnchorPixels,1.0));
                // CPUで確保した両側2pxの余白を越えないよう、Materialの範囲外入力も制限します。
                return round(lerp(lower,upper,f.y) * clamp(round(_WaveAmplitudePixels),0.0,2.0) * envelope);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float visibility = saturate(1.0-input.depth.x / max(input.depth.y,0.001));
                float2 pixel = floor(input.positionWS*16.0)+_WorldPixelOffset.xy;
                float nativeDepth = floor(input.depth.z)+0.5;
                float offset = EvaluateDisplacement(pixel,nativeDepth);
                float2 shiftedUv = input.uv-float2(offset*_MainTex_TexelSize.x,0);
                // 元位置のAlphaを掛けず、画像と透明部分を一緒に動かします。
                // 変位後に元Spriteの外へ出た画素は透明にし、輪郭を固定したり端の色を引き伸ばしたりしません。
                // 水域はCPUの受け手矩形で既に切り抜いているため、動いた像が陸地へ出ることはありません。
                clip(min(min(shiftedUv.x-input.bounds.x,input.bounds.z-shiftedUv.x),
                    min(shiftedUv.y-input.bounds.y,input.bounds.w-shiftedUv.y)));
                float2 safeUv = clamp(shiftedUv,input.bounds.xy+0.5*_MainTex_TexelSize.xy,
                    input.bounds.zw-0.5*_MainTex_TexelSize.xy);
                half4 source = SAMPLE_TEXTURE2D(_MainTex,sampler_PointClamp,safeUv);
                half3 color = source.rgb*_Color.rgb;
                color = lerp(color,color*_ReflectionTint.rgb,0.65);
                return half4(color,source.a*_Color.a
                    *_ReflectionTint.a*_MaximumAlpha*visibility);
            }
            ENDHLSL
        }
    }
}
