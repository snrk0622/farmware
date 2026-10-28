// マグマ全面へ、画像を反復せずに連続した網状の流れを描く製品用Shaderです。
// ChunkTilemapPresenterから共通時刻と絶対画素原点を受け取り、地形TileやSimulation状態は変更しません。
Shader "Farmware/Terrain/Magma Surface Pattern"
{
    Properties
    {
        [PerRendererData] _MainTex ("共有Sprite", 2D) = "white" {}
        // HDR色の1を超える輝度をBloomへ渡します。地面や壁を直接照らすLightとは別の表面発光です。
        [HDR] _DarkColor ("セル中心の暗色", Color) = (0.5, 0.06, 0.008, 1)
        [HDR] _BodyColor ("溶岩面の基調色", Color) = (1.0, 0.26, 0.018, 1)
        [HDR] _HaloColor ("網の外側色", Color) = (2.0, 0.8, 0.045, 1)
        [HDR] _CoreColor ("網の中心色", Color) = (3.2, 2.1, 0.18, 1)
        _CellSizePixels ("網目の基準寸法 (px、1タイル=16px)", Range(4, 128)) = 6
        _PointMotion ("網目中心の移動量 (セル比)", Range(0, 0.25)) = 0.22
        _CoreWidthPixels ("明るい網線の半幅 (px)", Range(0.25, 4)) = 0.25
        _HaloWidthPixels ("橙色の発光帯半幅 (px)", Range(0.25, 12)) = 0.75
        _FlowPeriodSeconds ("網目が一巡する基準時間 (秒)", Range(2, 30)) = 12
        [IntRange] _AnimationStepsPerSecond ("形状を更新する回数 (回/秒)", Range(1, 12)) = 4
        [IntRange] _BodyBrightnessLevels ("内部明度の階調数", Range(2, 8)) = 4
        _PulseAmount ("発光の明滅量", Range(0, 0.5)) = 0.16
        [ToggleUI] _Animate ("アニメーションを有効にする", Float) = 1
        [HideInInspector] _AnimationTimeSeconds ("共有する表示時間 (秒)", Float) = 0
        [HideInInspector] _WorldPixelOffset ("表示原点の絶対画素オフセット", Vector) = (0, 0, 0, 0)
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
            Name "MagmaSurfacePattern"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // TilemapRendererがSprite形状から渡す頂点情報です。模様自体は画像を読まず絶対座標から作ります。
            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            // 頂点を動かさず、画素ごとに同じ世界座標と岸側クリップ印を渡します。
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 positionWS : TEXCOORD0;
                half clipMarker : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _DarkColor;
                float4 _BodyColor;
                float4 _HaloColor;
                float4 _CoreColor;
                float _CellSizePixels;
                float _PointMotion;
                float _CoreWidthPixels;
                float _HaloWidthPixels;
                float _FlowPeriodSeconds;
                float _AnimationStepsPerSecond;
                float _BodyBrightnessLevels;
                float _PulseAmount;
                float _Animate;
                float _AnimationTimeSeconds;
                float4 _WorldPixelOffset;
            CBUFFER_END

            // Tilemapの論理座標は維持し、Shaderが見た目だけを変化させます。
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(world);
                output.positionWS = world.xy;
                // 基礎液面と同じく、Alpha 0は実際の画面奥岸だけ上端3pxを隠す印です。
                output.clipMarker = input.color.a;
                return output;
            }

            // 4096px周期内の格子IDから、実行順に依存しない乱数の種を作ります。
            uint HashCell(int2 cell, int periodInCells, uint salt)
            {
                // 入力は周期内のセルと隣接1セル（-1〜periodInCells）なので、1周期足すと必ず0以上です。
                // 符号なし剰余で周期端を循環させ、符号付き整数の剰余に対するShader警告も避けます。
                uint2 wrapped = (uint2)(cell + periodInCells) % (uint)periodInCells;
                uint value = wrapped.x * 0x9e3779b9u ^ wrapped.y * 0x85ebca6bu ^ salt;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }

            // 整数Hashの下位16bitを0以上1未満へ変換します。用途別Saltで位置と位相を分離します。
            float Random01(int2 cell, int periodInCells, uint salt)
            {
                return (HashCell(cell, periodInCells, salt) & 65535u) / 65536.0;
            }

            // 1格子に1個置くVoronoi特徴点です。点をセル内で往復させると、境界の網そのものが変形します。
            float2 FeaturePoint(int2 cell, int periodInCells, float time)
            {
                float2 basePoint = float2(
                    Random01(cell, periodInCells, 0x68bc21ebu),
                    Random01(cell, periodInCells, 0x02e5be93u));
                // 点が隣セルへ飛び出すと網が瞬間的に組み替わるため、中央寄りの範囲へ制限します。
                basePoint = lerp(0.28, 0.72, basePoint);
                float phase = 6.2831853 * Random01(cell, periodInCells, 0x967a889bu);
                float speed = lerp(0.82, 1.18, Random01(cell, periodInCells, 0x9f6abc1du));
                float angle = (time / max(_FlowPeriodSeconds, 2.0)) * 6.2831853 * speed + phase;
                float2 motion = float2(sin(angle), cos(angle * 0.87 + phase))
                    * clamp(_PointMotion, 0.0, 0.25);
                return basePoint + motion;
            }

            // 現在画素に最も近い特徴点と2番目に近い特徴点の距離を求めます。
            // F2-F1が小さい場所は2つの領域が競合する境界なので、添付例の明るい網として利用できます。
            void EvaluateCellularField(float2 pixel, float time, out float nearest, out float secondNearest,
                out float cellVariation)
            {
                // 1タイル16px内に複数の網目を収められるよう、4pxまで許可します。
                // 4096pxを整数個のセルで分割できる近似寸法へ揃え、周期端にも継ぎ目を作りません。
                int periodInCells = max((int)round(4096.0 / clamp(_CellSizePixels, 4.0, 128.0)), 1);
                float cellSize = 4096.0 / periodInCells;
                float2 fieldPosition = pixel / cellSize;
                int2 baseCell = (int2)floor(fieldPosition);
                float2 localPosition = frac(fieldPosition);
                nearest = 1000.0;
                secondNearest = 1000.0;
                cellVariation = 0.5;

                // 特徴点は自セル内に留まるため、周囲3×3だけで最近傍2点を確定できます。
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        int2 candidateCell = baseCell + int2(x, y);
                        float2 candidate = float2(x, y) + FeaturePoint(candidateCell, periodInCells, time);
                        float distanceToPoint = length(candidate - localPosition) * cellSize;
                        if (distanceToPoint < nearest)
                        {
                            secondNearest = nearest;
                            nearest = distanceToPoint;
                            cellVariation = Random01(candidateCell, periodInCells, 0x972f31a7u);
                        }
                        else if (distanceToPoint < secondNearest)
                        {
                            secondNearest = distanceToPoint;
                        }
                    }
                }
            }

            // マグマセル全面を不透明に描き、岸の位置は基礎液面と同じ規則で固定します。
            half4 Frag(Varyings input) : SV_Target
            {
                const float pixelsPerCell = 16.0;
                const float clipPixels = 3.0;
                if (input.clipMarker < 0.5h)
                {
                    float depthInsideCell = 1.0 - frac(input.positionWS.y);
                    clip(depthInsideCell * pixelsPerCell - clipPixels);
                }

                // 画像UVではなく絶対素材画素を使うため、16px Tileや64px Chunkの境界で模様を開始し直しません。
                float2 pixel = floor(input.positionWS * pixelsPerCell) + _WorldPixelOffset.xy;
                pixel = frac(pixel / 4096.0) * 4096.0;
                float fps = max(round(_AnimationStepsPerSecond), 1.0);
                float steppedTime = _Animate >= 0.5
                    ? floor(_AnimationTimeSeconds * fps) / fps
                    : 0.0;

                float nearest;
                float secondNearest;
                float cellVariation;
                EvaluateCellularField(pixel, steppedTime, nearest, secondNearest, cellVariation);

                // F2-F1を境界からの近似距離へ変換し、細い芯と広い橙色帯を別々に作ります。
                float edgeDistance = max((secondNearest - nearest) * 0.5, 0.0);
                float core = 1.0 - step(max(_CoreWidthPixels, 0.25), edgeDistance);
                float halo = 1.0 - step(max(_HaloWidthPixels, _CoreWidthPixels), edgeDistance);

                // セル中心ほど暗く、境界へ近づくほど熱くします。階調化して細かなちらつきを抑えます。
                // 特徴点の探索と同じ寸法を使い、縮小時も暗い中心と明るい境界の比率を保ちます。
                int periodInCells = max((int)round(4096.0 / clamp(_CellSizePixels, 4.0, 128.0)), 1);
                float cellSize = 4096.0 / periodInCells;
                float bodyStrength = saturate(nearest / (cellSize * 0.52) + (cellVariation - 0.5) * 0.24);
                float intervals = max(round(_BodyBrightnessLevels) - 1.0, 1.0);
                bodyStrength = round(bodyStrength * intervals) / intervals;
                float3 color = lerp(_DarkColor.rgb, _BodyColor.rgb, bodyStrength);
                color = lerp(color, _HaloColor.rgb, halo * 0.78);

                // 明滅は全画面一斉にせず、絶対座標由来の位相を混ぜて局所的に呼吸させます。
                float pulsePhase = steppedTime / max(_FlowPeriodSeconds * 0.5, 1.0)
                    + dot(pixel, float2(0.013, 0.009));
                float pulse = 1.0 - clamp(_PulseAmount, 0.0, 0.5)
                    + clamp(_PulseAmount, 0.0, 0.5) * (0.5 + 0.5 * sin(pulsePhase * 6.2831853));
                color = lerp(color, _CoreColor.rgb * pulse, core);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
