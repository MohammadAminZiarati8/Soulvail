// The Chameleon stalker's cloak (JG-22c). One transparent pass: the atlas-lit body fades towards a
// floor alpha while a fresnel rim, broken into scrolling bands, stays readable. _Cloak is driven per
// renderer by StalkerView; at 0 the body is the opaque look it had before, so the swap is invisible.
//
// Hand-written HLSL rather than a Shader Graph, on S_Fireball's precedent: a graph cannot be authored
// from a script, and the effect is a fresnel, a sine and a lerp.
Shader "Soulvail/Cloak"
{
    Properties
    {
        _BaseMap ("Atlas", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 0)
        _RimColor ("Rim Colour", Color) = (0.78, 0.92, 0.62, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2
        // JG-22c rule 2: the floors a full cloak never goes under.
        _RimAlpha ("Rim Alpha Floor", Range(0, 1)) = 0.35
        _BodyAlpha ("Body Alpha Floor", Range(0, 1)) = 0.1
        _ShimmerScale ("Shimmer Bands per Metre", Float) = 6
        _ShimmerSpeed ("Shimmer Scroll Speed", Float) = 1.5
        _Cloak ("Cloak", Range(0, 1)) = 1
        _Flash ("White Flash", Range(0, 1)) = 0
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
            Name "CloakForward"
            Tags { "LightMode" = "UniversalForward" }

            // Depth on, so a near-clear body hides its own far side instead of stacking it.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float4 _EmissionColor;
                float4 _RimColor;
                float _RimPower;
                float _RimAlpha;
                float _BodyAlpha;
                float _ShimmerScale;
                float _ShimmerSpeed;
                float _Cloak;
                float _Flash;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));

                // Half-lambert plus ambient: close enough to the Lit body at _Cloak 0 that the swap
                // does not pop, and cheap enough for a phone.
                Light light = GetMainLight();
                float wrap = saturate(dot(normal, light.direction)) * 0.5 + 0.5;
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                half3 lit = albedo * (light.color * wrap + SampleSH(normal)) + _EmissionColor.rgb;

                float fresnel = pow(1.0 - saturate(dot(normal, view)), _RimPower);
                // Rule 2: bands climb the body in world space, so a still stalker still moves.
                float shimmer = 0.5 + 0.5 * sin((input.positionWS.y + input.positionWS.x * 0.35) * _ShimmerScale * 6.2831853 - _Time.y * _ShimmerSpeed * 6.2831853);
                float rim = fresnel * lerp(_RimAlpha, 1.0, shimmer);

                half3 cloaked = lerp(lit * 0.6, _RimColor.rgb, saturate(fresnel * (0.6 + 0.4 * shimmer)));
                half3 colour = lerp(lit, cloaked, _Cloak);
                colour = lerp(colour, half3(1, 1, 1), _Flash);

                // Never under the body floor anywhere, and never under the rim floor at the silhouette.
                float cloakedAlpha = max(_BodyAlpha, max(rim, fresnel * _RimAlpha));
                float alpha = lerp(1.0, cloakedAlpha, _Cloak) * _BaseColor.a;
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
