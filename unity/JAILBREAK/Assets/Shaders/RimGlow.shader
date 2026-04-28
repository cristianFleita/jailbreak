Shader "Custom/RimGlow"
{
    Properties
    {
        [Header(Rim)]
        _RimColor           ("Rim Color",            Color)          = (0, 0.6, 1, 1)
        _RimPower           ("Rim Power",            Range(0.1, 16)) = 3.0
        _RimIntensity       ("Rim Intensity",        Range(0, 10))   = 2.0
        _RimOffset          ("Rim Offset",           Range(-1, 1))   = 0.0
        _IntensityScale     ("Intensity Scale",      Range(0, 1))    = 1.0

        [Header(Flicker)]
        [Toggle(_FLICKER_ON)] _FlickerEnabled ("Flicker Enabled",   Float) = 0
        _FlickerSpeed       ("Flicker Speed",        Range(0.1, 20)) = 6.0
        _FlickerMin         ("Flicker Min",          Range(0, 1))    = 0.2
        _FlickerMax         ("Flicker Max",          Range(0, 2))    = 1.0
        _FlickerSharpness   ("Flicker Sharpness",    Range(1, 16))   = 4.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent+1"
        }

        Pass
        {
            Name "RimGlowAdditive"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest  LEqual
            Cull   Back
            Blend  One One

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #pragma shader_feature_local _FLICKER_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RimColor;
                float  _RimPower;
                float  _RimIntensity;
                float  _RimOffset;
                float  _IntensityScale;
                float  _FlickerSpeed;
                float  _FlickerMin;
                float  _FlickerMax;
                float  _FlickerSharpness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewDirWS   : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float ComputeRim(float3 normalWS, float3 viewDirWS)
            {
                float ndotv = saturate(dot(normalWS, viewDirWS));
                return pow(saturate(1.0 - ndotv + _RimOffset), _RimPower);
            }

            float ComputeFlickerFactor()
            {
                #ifdef _FLICKER_ON
                    float wave  = sin(_Time.y * _FlickerSpeed);
                    float sharp = saturate(wave * _FlickerSharpness * 0.5 + 0.5);
                    return lerp(_FlickerMin, _FlickerMax, sharp);
                #else
                    return 1.0;
                #endif
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   norInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.normalWS    = norInputs.normalWS;
                OUT.viewDirWS   = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);

                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 normalWS  = normalize(IN.normalWS);
                float3 viewDirWS = normalize(IN.viewDirWS);

                float  rim      = ComputeRim(normalWS, viewDirWS);
                float  flicker  = ComputeFlickerFactor();
                float3 rimColor = _RimColor.rgb * rim * _RimIntensity * _IntensityScale * flicker;

                return half4(rimColor, 1.0);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
