// 水の基底色と水中断面が読むセルデータです。隣列の距離補間は行いません。
#ifndef FARMWARE_WATER_CELL_DATA_INCLUDED
#define FARMWARE_WATER_CELL_DATA_INCLUDED
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

// RGBの色変換を戻し、段数・床・海水・奥岸を復元します。
float4 DecodeWaterCell(half4 color)
{
    float3 data = color.rgb;
    #ifndef UNITY_COLORSPACE_GAMMA
        data = LinearToSRGB(data);
    #endif
    return float4(round(data.r * 6.0), step(0.5, data.g), step(0.5, data.b), color.a < 0.75 ? 1.0 : 0.0);
}

// 16pxセル内の素材画素中心で評価し、拡大表示でも1pxの中で濃度を滑らかに変えません。
float ProjectedWaterDistance(float stage, float worldY)
{
    float pixelCenter = floor(frac(worldY) * 16.0) + 0.5;
    return stage * 16.0 + 16.0 - pixelCenter;
}

// 既存の床あり45px／50%、床なし93px／100%を光学的な被覆率として維持します。
// 描画全体にこのAlphaを掛けず、実断面の寄与を1-被覆率として基底色の上に合成します。
float WaterCoverOpacity(float depth, float hasFloor)
{
    float fadePixels = hasFloor > 0.5 ? 45.0 : 93.0;
    float maximum = hasFloor > 0.5 ? 0.5 : 1.0;
    return saturate((depth - 3.0) / fadePixels) * maximum;
}

// 上端3pxを切るのは実際の奥岸の水色だけです。断面の地面の縁は切りません。
void ClipWaterNorthBank(float northBank, float worldY)
{
    clip(northBank > 0.5 ? (1.0 - frac(worldY)) * 16.0 - 3.0 : 1.0);
}
#endif
