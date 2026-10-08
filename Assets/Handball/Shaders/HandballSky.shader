// Pastel three-colour gradient skybox with a soft sun glow.
Shader "Handball/GradientSky"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.36, 0.62, 0.98, 1)
        _HorizonColor ("Horizon", Color) = (0.86, 0.93, 1.0, 1)
        _BottomColor ("Bottom", Color) = (0.72, 0.86, 0.66, 1)
        _Exponent ("Gradient Exponent", Range(0.1, 4)) = 0.75
        _SunColor ("Sun Glow", Color) = (1, 0.93, 0.75, 1)
        _SunSize ("Sun Size", Range(0.9, 0.9999)) = 0.9985
        _SunGlow ("Sun Glow Strength", Range(0, 2)) = 0.45
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _BottomColor;
                half _Exponent;
                half4 _SunColor;
                half _SunSize;
                half _SunGlow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 dir = normalize(input.direction);
                half up = saturate(dir.y);
                half down = saturate(-dir.y);
                half3 color = lerp(_HorizonColor.rgb, _TopColor.rgb, pow(up, _Exponent));
                color = lerp(color, _BottomColor.rgb, pow(down, 0.6h));

                Light sun = GetMainLight();
                half sunDot = dot(dir, sun.direction);
                half disc = smoothstep(_SunSize, _SunSize + 0.0008h, sunDot);
                half glow = pow(saturate(sunDot), 24.0h) * _SunGlow;
                color += _SunColor.rgb * (disc + glow);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
