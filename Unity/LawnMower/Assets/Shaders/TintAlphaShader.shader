Shader "Custom/TintAlphaShader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo(rgb)/Paintable(a)", 2D) = "white" {}
        _EAOTex("Emissive(rgb)/AO(a)", 2D) = "black" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _LightColor("Light Color", Color) = (1.09, .82, .65, 1)
        _BrakeColor("Brake Color", Color) = (0.7265, 0.095, 0, 1)
        _MowerOnColor("Mower On Color", Color) = (0.095, 0.7265, 0, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM

        // Create two shader variants to toggle emission
        // Serves as an example how this feature works
        // As textures are merged, not much is saved
        #pragma multi_compile_local __ EMISSION_ON

        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Standard fullforwardshadows

        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _EAOTex;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_EAOTex;
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;
        fixed3 _LightColor;
        fixed3 _BrakeColor;
        fixed3 _MowerOnColor;

        // Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
        // See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
        // #pragma instancing_options assumeuniformscaling
        UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        UNITY_INSTANCING_BUFFER_END(Props)

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Albedo comes from a texture tinted by color
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
            fixed4 eao = tex2D(_EAOTex, IN.uv_EAOTex.xy);
            o.Occlusion = eao.a;
            c.rgb *= lerp(half4 (1, 1, 1, 1), _Color, 1 - c.a);
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
            o.Emission = eao.r * _BrakeColor
                       + eao.b * _MowerOnColor;
            #ifdef EMISSION_ON
                o.Emission += eao.g * _LightColor;
            #endif
        }
        ENDCG
    }
    FallBack "Diffuse"
}
