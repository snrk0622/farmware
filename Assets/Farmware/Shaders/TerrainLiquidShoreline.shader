// 静止した水際の接触形状と、共通時間で変化する明暗を分離する製品Shaderです。
// 頂点RGBは水際色、Alphaは奥・左右・内角・裏側終端の7bitです。論理的な地形や液体は動かしません。
Shader "Farmware/Terrain/Liquid Shoreline"
{
    Properties
    {
        [PerRendererData] _MainTex ("共有Sprite", 2D) = "white" {}
        _BaseOpacity ("接触線の最低濃度 (0で透明な部分あり)", Range(0, 1)) = 0
        _HighlightOpacity ("明るい部分の最大濃度", Range(0, 1)) = 0.9
        _HighlightLengthPixels ("明暗模様の長さの目安 (px)", Range(8, 128)) = 36
        [IntRange] _BrightnessLevels ("濃度の階調数 (段階)", Range(2, 8)) = 4
        _OscillationPeriodSeconds ("寄せ引きの基準周期 (秒)", Range(1, 12)) = 3
        _PeriodVariation ("周期のばらつき (基準に対する割合)", Range(0, 0.5)) = 0.25
        [IntRange] _AnimationStepsPerSecond ("明暗を更新する回数 (回/秒)", Range(1, 12)) = 4
        [HideInInspector] _AnimationTimeSeconds ("共有する表示時間 (秒)", Float) = 0
        [HideInInspector] _WorldPixelOffset ("表示原点の絶対画素オフセット", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "LiquidShoreline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Gridの全面Spriteから受け取る頂点位置と、色・形状のデータです。
            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };
            // 形状は時間で変更せず、現在の画素位置と共通時間から明暗だけを計算します。
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 positionWS : TEXCOORD0;
                half4 color : COLOR;
            };
            CBUFFER_START(UnityPerMaterial)
                float _BaseOpacity;
                float _HighlightOpacity;
                float _HighlightLengthPixels;
                float _BrightnessLevels;
                float _OscillationPeriodSeconds;
                float _PeriodVariation;
                float _AnimationStepsPerSecond;
                float _AnimationTimeSeconds;
                float4 _WorldPixelOffset;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = world.xy;
                output.positionHCS = TransformWorldToHClip(world);
                // RGBは実際の色なのでLinear変換を維持します。Alphaの形状bitには色変換は掛かりません。
                output.color = input.color;
                return output;
            }

            // 周期内の波の中心IDを混ぜ、場所ごとに変わらない位相・周期を作る整数ハッシュです。
            // IDは呼出側で4096pxの周期内へ戻すため、チャンク境界や原点変更で再抽選しません。
            uint HashWaveNode(uint2 node)
            {
                uint value = node.x * 0x9e3779b9u ^ node.y * 0x85ebca6bu ^ 0x68bc21ebu;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }

            // 1つの固定中心の強さを返します。引数は周期内の中心IDと、コマ保持済みの表示秒です。
            // 中心全体で同じ位相を使うため、その波は流れず、同じ場所で広がって戻ります。
            float EvaluateWaveStrength(uint2 node, float time)
            {
                uint hash = HashWaveNode(node);
                float phaseOffset = (hash & 65535u) / 65536.0;
                float periodChoice = (hash >> 16) / 65535.0;
                float variation = clamp(_PeriodVariation, 0.0, 0.5);
                float localPeriod = max(_OscillationPeriodSeconds, 1.0)
                    * lerp(1.0 - variation, 1.0 + variation, periodChoice);
                float phase = frac(time / localPeriod + phaseOffset);
                // 往路・復路を同じ範囲へ畳み、各中心の同じ濃度へ戻る往復を計算します。
                float foldedPhase = min(phase, 1.0 - phase);
                return 0.5 + 0.5 * cos(6.2831853 * foldedPhase);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 論理セルの16pxを基準に整数画素を選び、輪郭を半端な座標へ動かしません。
                const int tileSize = 16;
                const int clipPixels = 3;
                const int visibleTop = tileSize - clipPixels - 1;
                int2 pixel = (int2)floor(frac(input.positionWS) * tileSize);
                uint mask = (uint)round(input.color.a * 255.0);
                bool back = (mask & 1u) != 0u;
                // 裏側へ回り込む終端の上端3pxだけを地面で隠します。
                // 左右を分けることで、片側だけ終端の細い水路でも反対側の線を途切れさせません。
                bool leftRow = pixel.y <= visibleTop || (!back && (mask & 32u) == 0u);
                bool rightRow = pixel.y <= visibleTop || (!back && (mask & 64u) == 0u);
                bool onLine = (back && pixel.y == visibleTop)
                    || (pixel.x == 0 && leftRow && (mask & 2u) != 0u)
                    || (pixel.x == tileSize - 1 && rightRow && (mask & 4u) != 0u)
                    || (!back && pixel.x == 0 && pixel.y >= visibleTop && (mask & 8u) != 0u)
                    || (!back && pixel.x == tileSize - 1 && pixel.y >= visibleTop && (mask & 16u) != 0u);
                clip(onLine ? 1.0 : -1.0);

                // CPUのShorelinePatternPeriodPixelsと同じ周期です。整数周期数を使うため、
                // 負座標・周期の端・Floating Originでも同じ絶対地点の模様を維持できます。
                const float period = 4096.0;
                float2 absolutePixel = floor(input.positionWS * tileSize) + _WorldPixelOffset.xy;
                float2 phasePosition = frac(absolutePixel / period);
                float steps = max(round(_AnimationStepsPerSecond), 1.0);
                // Presenterが全チャンク共通の表示時間を渡します。コマの間は同じ値を使い、補間しません。
                // 時刻をMaterialの入力にすることで、GPUテストでも任意のコマを再現できます。
                float time = floor(_AnimationTimeSeconds * steps) / steps;

                // 全岸共通の2周期では強弱が揃うため、模様長さを目安に固定中心を格子へ置きます。
                // 4096pxに整数個の中心を置き、隣接中心のIDも循環させて周期端の継ぎ目を防ぎます。
                uint nodeCount = max(1u, (uint)round(period / max(_HighlightLengthPixels, 1.0)));
                float2 nodePosition = phasePosition * nodeCount;
                uint2 lowerNode = (uint2)floor(nodePosition) % nodeCount;
                float2 fraction = frac(nodePosition);
                float2 upperWeight = 0.5 - 0.5 * cos(3.14159265 * fraction);
                float2 lowerWeight = 1.0 - upperWeight;

                // 各中心の空間形状は固定し、範囲端で0へ滑らかに戻してから最終4階調へ丸めます。
                // 1画素ごとの乱数やタイル別再生にはせず、隣の4中心の波を同じ絶対座標で重ねます。
                float lowerLeft = lowerWeight.x * lowerWeight.y * EvaluateWaveStrength(lowerNode, time);
                float lowerRight = upperWeight.x * lowerWeight.y
                    * EvaluateWaveStrength((lowerNode + uint2(1u, 0u)) % nodeCount, time);
                float upperLeft = lowerWeight.x * upperWeight.y
                    * EvaluateWaveStrength((lowerNode + uint2(0u, 1u)) % nodeCount, time);
                float upperRight = upperWeight.x * upperWeight.y
                    * EvaluateWaveStrength((lowerNode + uint2(1u, 1u)) % nodeCount, time);
                float highlight = saturate(max(max(lowerLeft, lowerRight), max(upperLeft, upperRight)));
                float intervals = max(round(_BrightnessLevels) - 1.0, 1.0);
                highlight = round(highlight * intervals) / intervals;
                // 最低濃度0なら最も弱い階調は完全透明です。形状を欠落させず、明暗の寄せ引きで現れて消えます。
                half alpha = lerp(_BaseOpacity, _HighlightOpacity, highlight);
                return half4(input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
