// Shader for Hammer meshes (Built-in and URP): painted vertex colour tint plus up to three
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

    // URP: the same look through URP's own lighting. Only compiled when URP is installed.
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D( _MainTex ); SAMPLER( sampler_MainTex );
        TEXTURE2D( _Layer1 ); SAMPLER( sampler_Layer1 );
        TEXTURE2D( _Layer2 ); SAMPLER( sampler_Layer2 );
        TEXTURE2D( _Layer3 ); SAMPLER( sampler_Layer3 );

        CBUFFER_START( UnityPerMaterial )
            float4 _MainTex_ST;
            half4 _Color;
            half _Glossiness;
            half _Metallic;
        CBUFFER_END

        float _HammerFullbright;
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #if UNITY_VERSION >= 600010
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #else
            #pragma multi_compile _ _FORWARD_PLUS
            #endif
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                float4 blend : TEXCOORD3;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 blend : TEXCOORD3;
                half4 color : COLOR;
                DECLARE_LIGHTMAP_OR_SH( lightmapUV, vertexSH, 4 );
                half fogFactor : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert( Attributes v )
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID( v );
                UNITY_TRANSFER_INSTANCE_ID( v, o );
                VertexPositionInputs p = GetVertexPositionInputs( v.positionOS.xyz );
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal( v.normalOS );
                o.uv = TRANSFORM_TEX( v.uv, _MainTex );
                o.blend = v.blend;
                o.color = v.color;
                OUTPUT_LIGHTMAP_UV( v.lightmapUV, unity_LightmapST, o.lightmapUV );
                OUTPUT_SH( o.normalWS, o.vertexSH );
                o.fogFactor = ComputeFogFactor( p.positionCS.z );
                return o;
            }

            half4 frag( Varyings i ) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID( i );
                float2 uv = i.uv;
                half4 c = SAMPLE_TEXTURE2D( _MainTex, sampler_MainTex, uv );
                c = lerp( c, SAMPLE_TEXTURE2D( _Layer1, sampler_Layer1, uv ), saturate( i.blend.r ) );
                c = lerp( c, SAMPLE_TEXTURE2D( _Layer2, sampler_Layer2, uv ), saturate( i.blend.g ) );
                c = lerp( c, SAMPLE_TEXTURE2D( _Layer3, sampler_Layer3, uv ), saturate( i.blend.b ) );
                // Unpainted corners have a colour of (0,0,0,0): only tint where paint has been applied
                c.rgb *= lerp( 1, i.color.rgb, i.color.a );
                c *= _Color;

                // The Hammer window's Fullbright view (URP has no replacement shaders): no scene
                // lighting, each face shaded a little by which way it points so shapes still read
                if ( _HammerFullbright > 0.5 )
                {
                    float3 n = normalize( i.normalWS );
                    return half4( c.rgb * saturate( 0.72 + 0.28 * n.y - 0.08 * abs( n.x ) + 0.04 * abs( n.z ) ), 1 );
                }

                InputData input = (InputData)0;
                input.positionWS = i.positionWS;
                input.positionCS = i.positionCS;
                input.normalWS = NormalizeNormalPerPixel( i.normalWS );
                input.viewDirectionWS = GetWorldSpaceNormalizeViewDir( i.positionWS );
                input.shadowCoord = TransformWorldToShadowCoord( i.positionWS );
                input.fogCoord = i.fogFactor;
                input.bakedGI = SAMPLE_GI( i.lightmapUV, i.vertexSH, input.normalWS );
                input.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV( i.positionCS );
                input.shadowMask = SAMPLE_SHADOWMASK( i.lightmapUV );

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = c.rgb;
                surface.alpha = 1;
                surface.metallic = _Metallic;
                surface.smoothness = _Glossiness;
                surface.occlusion = 1;
                surface.normalTS = half3( 0, 0, 1 );

                half4 color = UniversalFragmentPBR( input, surface );
                color.rgb = MixFog( color.rgb, input.fogCoord );
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 vert( Attributes v ) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID( v );
                float3 positionWS = TransformObjectToWorld( v.positionOS.xyz );
                float3 normalWS = TransformObjectToWorldNormal( v.normalOS );
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize( _LightPosition - positionWS );
            #else
                float3 lightDirection = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip( ApplyShadowBias( positionWS, normalWS, lightDirection ) );
            #if UNITY_REVERSED_Z
                positionCS.z = min( positionCS.z, UNITY_NEAR_CLIP_VALUE );
            #else
                positionCS.z = max( positionCS.z, UNITY_NEAR_CLIP_VALUE );
            #endif
                return positionCS;
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 vert( Attributes v ) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID( v );
                return TransformObjectToHClip( v.positionOS.xyz );
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings vert( Attributes v )
            {
                UNITY_SETUP_INSTANCE_ID( v );
                Varyings o;
                o.positionCS = TransformObjectToHClip( v.positionOS.xyz );
                o.normalWS = TransformObjectToWorldNormal( v.normalOS );
                return o;
            }

            half4 frag( Varyings i ) : SV_Target { return half4( NormalizeNormalPerPixel( i.normalWS ), 0 ); }
            ENDHLSL
        }
    }

    // Built-in pipeline
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
