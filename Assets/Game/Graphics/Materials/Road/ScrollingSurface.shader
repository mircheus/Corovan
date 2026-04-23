Shader "Custom/ScrollingSurface"
{
    Properties
    {
        _MainTex        ("Main Texture",        2D)     = "white" {}
        _NormalMap      ("Normal Map",          2D)     = "bump"  {}
        _ScrollSpeedX   ("Scroll Speed X",      Float)  = 0.2
        _ScrollSpeedY   ("Scroll Speed Y",      Float)  = 0.0

        // Второй слой для глубины (опционально)
        _SecondTex      ("Second Texture",      2D)     = "white" {}
        _SecondSpeedX   ("Second Speed X",      Float)  = -0.1
        _SecondSpeedY   ("Second Speed Y",      Float)  = 0.05
        _SecondOpacity  ("Second Layer Opacity",Range(0,1)) = 0.4

        _Tint           ("Tint Color",          Color)  = (1,1,1,1)
        _Smoothness     ("Smoothness",          Range(0,1)) = 0.5
        _Metallic       ("Metallic",            Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // URP keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── Свойства ──────────────────────────────────────────────
            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);  SAMPLER(sampler_NormalMap);
            TEXTURE2D(_SecondTex);  SAMPLER(sampler_SecondTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _SecondTex_ST;
                float4 _Tint;
                float  _ScrollSpeedX;
                float  _ScrollSpeedY;
                float  _SecondSpeedX;
                float  _SecondSpeedY;
                float  _SecondOpacity;
                float  _Smoothness;
                float  _Metallic;
            CBUFFER_END

            // ── Структуры ─────────────────────────────────────────────
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

            // ── Вершинный шейдер ──────────────────────────────────────
            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   norInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS  = posInputs.positionCS;
                OUT.positionWS   = posInputs.positionWS;
                OUT.uv           = IN.uv;
                OUT.normalWS     = norInputs.normalWS;
                OUT.tangentWS    = norInputs.tangentWS;
                OUT.bitangentWS  = norInputs.bitangentWS;
                OUT.fogFactor    = ComputeFogFactor(posInputs.positionCS.z);

                return OUT;
            }

            // ── Фрагментный шейдер ────────────────────────────────────
            half4 frag(Varyings IN) : SV_Target
            {
                // ── Прокрутка UV ──────────────────────────────────────
                float2 scroll1 = float2(_ScrollSpeedX, _ScrollSpeedY) * _Time.y;
                float2 scroll2 = float2(_SecondSpeedX, _SecondSpeedY) * _Time.y;

                float2 uv1 = TRANSFORM_TEX(IN.uv, _MainTex)  + scroll1;
                float2 uv2 = TRANSFORM_TEX(IN.uv, _SecondTex) + scroll2;

                // ── Сэмплирование текстур ─────────────────────────────
                half4 col1 = SAMPLE_TEXTURE2D(_MainTex,  sampler_MainTex,  uv1);
                half4 col2 = SAMPLE_TEXTURE2D(_SecondTex, sampler_SecondTex, uv2);

                // Смешиваем два слоя
                half4 albedo = lerp(col1, col2, _SecondOpacity * col2.a) * _Tint;

                // ── Normal map ────────────────────────────────────────
                half4 normalSample = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv1);
                half3 normalTS     = UnpackNormal(normalSample);

                float3x3 TBN = float3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS);
                float3 normalWS = normalize(mul(normalTS, TBN));

                // ── PBR освещение (URP) ───────────────────────────────
                InputData inputData = (InputData)0;
                inputData.positionWS     = IN.positionWS;
                inputData.normalWS       = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.fogCoord       = IN.fogFactor;
                inputData.shadowCoord    = TransformWorldToShadowCoord(IN.positionWS);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo      = albedo.rgb;
                surfaceData.alpha       = albedo.a;
                surfaceData.smoothness  = _Smoothness;
                surfaceData.metallic    = _Metallic;
                surfaceData.normalTS    = normalTS;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb   = MixFog(color.rgb, IN.fogFactor);

                return color;
            }
            ENDHLSL
        }

        // Shadow caster pass (нужен для корректных теней)
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
