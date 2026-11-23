// 적 회색 실루엣 (수정사항 2026-11-10).
// 플레이어 시야각 안이지만 빛이 닿지 않는 적은 모습 대신 회색 그림자로만 보인다.
// 조명을 받지 않는다 - 어둠 속에서도 같은 회색이다. 그림의 명암만 살짝 남겨 몸 윤곽이 읽히게 한다.
Shader "Capstone/EnemySilhouette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Silhouette", Color) = (0.45, 0.46, 0.5, 0.85)
        _Detail ("Detail", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings  { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Detail;
            CBUFFER_END

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half lum = dot(tex.rgb, half3(0.299, 0.587, 0.114));
                half3 rgb = _Color.rgb * lerp(1.0, 0.7 + lum * 0.8, _Detail);
                return half4(rgb, tex.a * _Color.a * i.color.a);
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings  { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Detail;
            CBUFFER_END
            Varyings vert (Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS); o.uv = v.uv; o.color = v.color; return o; }
            half4 frag (Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half lum = dot(tex.rgb, half3(0.299, 0.587, 0.114));
                return half4(_Color.rgb * lerp(1.0, 0.7 + lum * 0.8, _Detail), tex.a * _Color.a * i.color.a);
            }
            ENDHLSL
        }
    }
}
