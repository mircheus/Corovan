Shader "Custom/ScrollingGround"
{
    Properties
    {
        _MainTex      ("Texture",      2D)          = "white" {}
        _NormalMap    ("Normal Map",   2D)          = "bump"  {}
        _NormalScale  ("Normal Scale", Range(0,2))  = 1.0
        _ScrollSpeedX ("Speed X",      Float)       = 0.0
        _ScrollSpeedY ("Speed Y",      Float)       = 0.2
        _Tint         ("Tint Color",   Color)       = (1,1,1,1)
        _Smoothness   ("Smoothness",   Range(0,1))  = 0.2
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _NormalMap_ST;
                float4 _Tint;
                float  _ScrollSpeedX;
                float  _ScrollSpeedY;
                float  _NormalScale;
                float  _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
                float3 tangentWS   : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                float  fogFactor   : TEXCOORD5;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   norInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS  = posInputs.positionCS;
                OUT.positionWS   = posInputs.positionWS;
                OUT.normalWS     = norInputs.normalWS;
                OUT.tangentWS    = norInputs.tangentWS;
                OUT.bitangentWS  = norInputs.bitangentWS;
                OUT.fogFactor    = ComputeFogFactor(posInputs.positionCS.z);

                // Прокрутка UV — одинаковая для обеих текстур
                float2 scroll = float2(_ScrollSpeedX, _ScrollSpeedY) * _Time.y;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex) + scroll;

                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Основная текстура
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Tint;

                // Normal map (у него может быть свой Tiling/Offset через _NormalMap_ST)
                float2 uvNormal    = TRANSFORM_TEX(IN.uv, _NormalMap);
                half4  normalSample = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvNormal);
                half3  normalTS     = UnpackNormalScale(normalSample, _NormalScale);

                // Tangent Space → World Space
                float3x3 TBN      = float3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS);
                float3   normalWS = normalize(mul(normalTS, TBN));

                InputData inputData       = (InputData)0;
                inputData.positionWS      = IN.positionWS;
                inputData.normalWS        = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord     = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord        = IN.fogFactor;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo      = albedo.rgb;
                surfaceData.alpha       = 1.0;
                surfaceData.smoothness  = _Smoothness;
                surfaceData.normalTS    = normalTS;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb   = MixFog(color.rgb, IN.fogFactor);

                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
