// Ironfront fog of war (Milestone 007). Drawn by a URP Full Screen Pass renderer feature before
// post-processing: reconstructs each pixel's world position from the depth buffer, looks up the player's
// visibility texture (0 unexplored, 0.5 explored, 1 visible) and darkens the pixel accordingly. Works on
// every opaque surface (ground, obstacles, buildings) so tall objects cannot poke through the fog.
// Disabled (no effect) unless _IronfrontFogParams.x is 1, i.e. outside matches, in the editor and in tools.
Shader "Hidden/Ironfront/FogOfWar"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "FogOfWar"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_IronfrontFogTex);
            SAMPLER(sampler_IronfrontFogTex);
            float4 _IronfrontFogRect;   // minX, minZ, sizeX, sizeZ
            float4 _IronfrontFogParams; // enabled, unexplored darkness, explored darkness, outside-map darkness

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_IronfrontFogParams.x < 0.5) return half4(0, 0, 0, 0);

                float2 uv = input.texcoord;
                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(uv);
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float2 f = (world.xz - _IronfrontFogRect.xy) / max(_IronfrontFogRect.zw, 0.001);

                float darkness;
                if (f.x < 0 || f.y < 0 || f.x > 1 || f.y > 1)
                {
                    darkness = _IronfrontFogParams.w;
                }
                else
                {
                    // 5-tap soften (about one cell) so the 1 m grid does not read as stair steps.
                    float2 o = 0.9 / max(_IronfrontFogRect.zw, 0.001);
                    float v = SAMPLE_TEXTURE2D(_IronfrontFogTex, sampler_IronfrontFogTex, f).r * 0.4
                        + SAMPLE_TEXTURE2D(_IronfrontFogTex, sampler_IronfrontFogTex, f + float2(o.x, o.y)).r * 0.15
                        + SAMPLE_TEXTURE2D(_IronfrontFogTex, sampler_IronfrontFogTex, f + float2(-o.x, o.y)).r * 0.15
                        + SAMPLE_TEXTURE2D(_IronfrontFogTex, sampler_IronfrontFogTex, f + float2(o.x, -o.y)).r * 0.15
                        + SAMPLE_TEXTURE2D(_IronfrontFogTex, sampler_IronfrontFogTex, f + float2(-o.x, -o.y)).r * 0.15;
                    darkness = v < 0.5
                        ? lerp(_IronfrontFogParams.y, _IronfrontFogParams.z, v * 2)
                        : lerp(_IronfrontFogParams.z, 0, (v - 0.5) * 2);
                }
                return half4(0.015, 0.02, 0.028, saturate(darkness));
            }
            ENDHLSL
        }
    }
}
