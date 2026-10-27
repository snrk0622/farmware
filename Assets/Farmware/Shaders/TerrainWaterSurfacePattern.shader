// 水中の断面・水際線とは独立した、素材1pxの短い水面模様を描きます。
// 位置は絶対画素格子に固定し、低FPSの長さ・濃度だけを変えます。論理地形は変更しません。
Shader "Farmware/Terrain/Water Surface Pattern"
{
    Properties
    {
        [PerRendererData] _MainTex ("共有Sprite", 2D) = "white" {}
        _FreshColor ("淡水の模様色 (RGB)", Color) = (0.32, 0.58, 0.65, 1)
        _SaltColor ("海水の模様色 (RGB)", Color) = (0.26, 0.45, 0.61, 1)
        _MaximumOpacity ("模様の最大濃度", Range(0, 1)) = 0.18
        _Density ("候補位置の採用率 (0〜1)", Range(0, 1)) = 0.65
        [IntRange] _MinimumLengthPixels ("最短の長さ (px)", Range(1, 16)) = 3
        [IntRange] _MaximumLengthPixels ("最長の長さ (px)", Range(1, 16)) = 8
        _ActiveSeconds ("出現から消失までの基準時間 (秒)", Range(1, 20)) = 4
        _RestSeconds ("消失後の基準休止時間 (秒)", Range(0, 20)) = 3
        _PeriodVariation ("出現・休止時間のばらつき (割合)", Range(0, 0.5)) = 0.35
        [IntRange] _AnimationStepsPerSecond ("形状と濃度を更新する回数 (回/秒)", Range(1, 12)) = 2
        [IntRange] _BrightnessLevels ("透明を含む濃度の階調数", Range(2, 8)) = 4
        [ToggleUI] _Animate ("アニメーションを有効にする (無効で静止形状を確認)", Float) = 1
        [HideInInspector] _AnimationTimeSeconds ("共有する表示時間 (秒)", Float) = 0
        [HideInInspector] _WorldPixelOffset ("表示原点の絶対画素オフセット", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "WaterSurfacePattern"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TerrainWaterCellData.hlsl"

            // 全面Tileの色は実色ではなく、液体種と奥岸クリップを渡す符号化データです。
            struct Attributes
            {
                // Tilemapが渡すローカル頂点座標です。Shaderで形状自体は動かしません。
                float4 positionOS : POSITION;
                // RGBは投影段・床・海水の符号、Alphaは奥岸クリップ印です。
                half4 color : COLOR;
            };
            // ワールド座標を渡し、Tile・チャンクごとに模様を開始し直さないようにします。
            struct Varyings
            {
                float4 positionHCS : SV_POSITION; // GPUの描画位置です。
                float2 positionWS : TEXCOORD0; // 原点相対のワールド位置（タイル単位）です。
                half4 color : COLOR; // セルの符号化データを補間して受け渡します。
            };
            CBUFFER_START(UnityPerMaterial)
                // Inspectorで調整する色です。Alphaは色の設定ではなく最大濃度で統一します。
                float4 _FreshColor;
                float4 _SaltColor;
                float _MaximumOpacity; // 最大濃度（0〜1）です。
                float _Density; // 固定候補の採用率（0〜1）です。
                float _MinimumLengthPixels; // 成長時の全長の下限（素材px）です。
                float _MaximumLengthPixels; // 成長時の全長の上限（素材px）です。
                float _ActiveSeconds; // 出現から消失までの基準秒です。
                float _RestSeconds; // 消失後に完全透明で待つ基準秒です。
                float _PeriodVariation; // 各候補の出現・休止時間に掛けるばらつき率です。
                float _AnimationStepsPerSecond; // 1秒のコマ更新数です。
                float _BrightnessLevels; // 透明を含む濃度の段階数です。
                float _Animate; // Inspectorの静止確認切替です。位置や密度は変えません。
                float _AnimationTimeSeconds; // Presenterが実行時コピーへ渡す共通時計（秒）です。
                float4 _WorldPixelOffset; // 原点変更時に渡す絶対原点の4096px周期内オフセットです。
            CBUFFER_END

            // 頂点を移動せず、全面Tileの描画範囲と既存の水面位置を維持します。
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(world);
                output.positionWS = world.xy;
                output.color = input.color;
                return output;
            }

            // 128×128候補格子は4096pxで循環します。原点変更前後で同じ整数IDを使います。
            uint HashNode(int2 node, uint salt)
            {
                // 128は2の冪なので下位7bitが床方向剰余と一致します。負IDにも同じ周期を適用できます。
                uint2 wrapped = (uint2)node & 127u;
                uint value = wrapped.x * 0x9e3779b9u ^ wrapped.y * 0x85ebca6bu ^ salt;
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }

            // 下位16bitだけを0〜1未満へ変換します。用途別Saltで密度・位置・周期を独立させます。
            float Choice(int2 node, uint salt)
            {
                return (HashNode(node, salt) & 65535u) / 65536.0;
            }

            // 固定中心1個の画素濃度です。引数は絶対画素・候補ID・コマ保持済み時刻です。
            // 隣候補も評価するので、中心が32px格子や16pxタイルの端を跨いでも線を切りません。
            float EvaluateMark(float2 pixel, int2 node, float time)
            {
                if (Choice(node, 0x68bc21ebu) >= saturate(_Density)) return 0.0;
                float2 center = node * 32.0 + float2(
                    floor(Choice(node, 0x02e5be93u) * 32.0),
                    floor(Choice(node, 0x967a889bu) * 32.0));
                float2 delta = pixel - center;
                // 形は厚さ1pxの連続した水平線だけに揃えます。長さや再生のばらつきは後段で維持します。
                if (abs(delta.x) > 8.0 || delta.y != 0.0) return 0.0;

                float variation = clamp(_PeriodVariation, 0.0, 0.5);
                float active = max(_ActiveSeconds, 1.0) * lerp(1.0 - variation, 1.0 + variation,
                    Choice(node, 0x9f6abc1du));
                float rest = max(_RestSeconds, 0.0) * lerp(1.0 - variation, 1.0 + variation,
                    Choice(node, 0x972f31a7u));
                float cycle = active + rest;
                float elapsed = frac(time / cycle + Choice(node, 0xa511e9b3u)) * cycle;
                float strength = elapsed < active ? saturate(1.0 - abs(elapsed / active * 2.0 - 1.0)) : 0.0;
                if (_Animate < 0.5) strength = 1.0;
                float intervals = max(round(_BrightnessLevels) - 1.0, 1.0);
                strength = round(strength * intervals) / intervals;
                if (strength <= 0.0) return 0.0;

                // 長さも整数画素で伸縮させ、中心位置は動かしません。最短・最長の逆入力も正規化します。
                float minimum = clamp(round(min(_MinimumLengthPixels, _MaximumLengthPixels)), 1.0, 16.0);
                float maximum = clamp(round(max(_MinimumLengthPixels, _MaximumLengthPixels)), minimum, 16.0);
                float fullLength = minimum + floor(Choice(node, 0x13a5ba1du) * (maximum - minimum + 1.0));
                float length = max(1.0, round(fullLength * lerp(0.5, 1.0, strength)));
                float left = -floor(length * 0.5);
                return delta.x >= left && delta.x < left + length ? strength : 0.0;
            }

            // 水域Tile内だけを描き、実際の奥岸先頭セルの上端3pxだけは水面と同じように隠します。
            half4 Frag(Varyings input) : SV_Target
            {
                float4 cell = DecodeWaterCell(input.color);
                ClipWaterNorthBank(cell.w, input.positionWS.y);
                float2 pixel = floor(input.positionWS * 16.0) + _WorldPixelOffset.xy;
                // CPUと同じ4096px周期へ戻し、負座標でも床方向の格子を維持します。
                pixel = frac(pixel / 4096.0) * 4096.0;
                int2 node = (int2)floor(pixel / 32.0);
                float fps = max(round(_AnimationStepsPerSecond), 1.0);
                float time = floor(_AnimationTimeSeconds * fps) / fps;
                float strength = 0.0;
                // 最大16px・高さ1pxなので隣接3×3候補までで十分です。水域全体をCPUで毎フレーム走査しません。
                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        strength = max(strength, EvaluateMark(pixel, node + int2(x, y), time));
                    }
                }
                clip(strength - 0.0001);
                return half4(lerp(_FreshColor.rgb, _SaltColor.rgb, cell.z), strength * _MaximumOpacity);
            }
            ENDHLSL
        }
    }
}
