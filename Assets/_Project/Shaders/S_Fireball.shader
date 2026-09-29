// Unlit fire shader for the low-poly VFX. Unlit on purpose: KayKit's look comes from flat colour
// fields, and a fireball is a light source anyway, so there is nothing for a lit shader to add
// beyond cost. No textures — the gradient is three colours driven by a fresnel term, which costs a
// dot product and keeps the whole effect down to one material with no sampler.
//
// Blend mode, cull and ZWrite are material properties rather than separate shaders: the solid core
// wants alpha blending with depth, the impact flash and shards want additive with none.
Shader "Soulvail/Fireball"
{
    Properties
    {
        [HDR] _CoreColor ("Core Colour", Color) = (1, 0.953, 0.690, 1)
        [HDR] _MidColor ("Mid Colour", Color) = (1, 0.604, 0.235, 1)
        [HDR] _RimColor ("Rim Colour", Color) = (0.882, 0.294, 0.133, 1)
        // Low on purpose. The mesh is flat shaded, so the fresnel term is constant across each
        // triangle and the ramp is what makes the facets visible at all - there is no lighting here
        // to do it. A steep power pushes almost every facet to the core colour and the ball reads
        // smooth, which is the one thing this art style must not do.
        _FresnelPower ("Fresnel Power", Range(0.25, 8)) = 1
        _Alpha ("Alpha", Range(0, 1)) = 1

        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 10
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(Off, 0, On, 1)] _ZWrite ("Depth Write", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FireballUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _MidColor;
                float4 _RimColor;
                float _FresnelPower;
                float _Alpha;
                float _SrcBlend;
                float _DstBlend;
                float _Cull;
                float _ZWrite;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionWS);
                output.color = input.color;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);

                // Facing the camera is the hot core, grazing angles are the cooler rim. Trails and
                // billboards can hand us a degenerate normal; saturate keeps that at the core
                // colour instead of letting it wander.
                float fresnel = saturate(1.0 - saturate(dot(normalWS, viewDirWS)));
                fresnel = pow(fresnel, _FresnelPower);

                float3 color = lerp(_CoreColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.5, fresnel));
                color = lerp(color, _RimColor.rgb, smoothstep(0.5, 1.0, fresnel));
                color *= input.color.rgb;

                // Straight alpha, not premultiplied: the core material blends SrcAlpha/OneMinusSrcAlpha
                // and the impact material blends SrcAlpha/One, so both want alpha left in the w.
                float alpha = _Alpha * _CoreColor.a * input.color.a;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
