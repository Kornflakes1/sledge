// Built-in render pipeline shader for Hammer meshes: painted vertex colour tint plus up to three
// extra layers blended by the painted blend weights (stored in UV3, R/G/B = layers 1-3).
Shader "Hammer/Vertex Blend"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Base", 2D) = "white" {}
        _Layer1 ("Layer 1 (R)", 2D) = "white" {}
        _Layer2 ("Layer 2 (G)", 2D) = "white" {}
        _Layer3 ("Layer 3 (B)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _Layer1;
        sampler2D _Layer2;
        sampler2D _Layer3;
        fixed4 _Color;
        half _Glossiness;
        half _Metallic;

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
            float4 blend;
        };

        void vert( inout appdata_full v, out Input o )
        {
            UNITY_INITIALIZE_OUTPUT( Input, o );
            o.blend = v.texcoord3;
        }

        void surf( Input IN, inout SurfaceOutputStandard o )
        {
            float2 uv = IN.uv_MainTex;
            fixed4 c = tex2D( _MainTex, uv );
            c = lerp( c, tex2D( _Layer1, uv ), saturate( IN.blend.r ) );
            c = lerp( c, tex2D( _Layer2, uv ), saturate( IN.blend.g ) );
            c = lerp( c, tex2D( _Layer3, uv ), saturate( IN.blend.b ) );
            // Unpainted corners have a colour of (0,0,0,0); only tint where paint has been applied,
            // the same way s&box treats mesh vertex colours
            c.rgb *= lerp( 1, IN.color.rgb, IN.color.a );
            c *= _Color;

            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
