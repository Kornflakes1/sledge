// Hammer's "Fullbright" view: textures with no scene lighting, each face shaded a little by which
// way it points so shapes still read. Used as a replacement shader by the Hammer window.
Shader "Hammer/Fullbright"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Base", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert( appdata v )
            {
                v2f o;
                o.pos = UnityObjectToClipPos( v.vertex );
                o.uv = TRANSFORM_TEX( v.uv, _MainTex );
                o.normal = UnityObjectToWorldNormal( v.normal );
                o.color = v.color;
                return o;
            }

            fixed4 frag( v2f i ) : SV_Target
            {
                fixed4 c = tex2D( _MainTex, i.uv ) * _Color;
                // Unpainted Hammer meshes have (0,0,0,0) vertex colours: only tint where painted
                c.rgb *= lerp( 1, i.color.rgb, i.color.a );

                // Fixed "lighting": tops brightest, then the two side directions, bottoms darkest
                float3 n = normalize( i.normal );
                float shade = 0.72 + 0.28 * n.y - 0.08 * abs( n.x ) + 0.04 * abs( n.z );
                c.rgb *= saturate( shade );
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }

    // Alpha-tested things (foliage, fences)
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                fixed4 color : COLOR;
            };

            v2f vert( appdata v )
            {
                v2f o;
                o.pos = UnityObjectToClipPos( v.vertex );
                o.uv = TRANSFORM_TEX( v.uv, _MainTex );
                o.normal = UnityObjectToWorldNormal( v.normal );
                o.color = v.color;
                return o;
            }

            fixed4 frag( v2f i ) : SV_Target
            {
                fixed4 c = tex2D( _MainTex, i.uv ) * _Color;
                clip( c.a - 0.5 );
                // Unpainted Hammer meshes have (0,0,0,0) vertex colours: only tint where painted
                c.rgb *= lerp( 1, i.color.rgb, i.color.a );

                // Fixed "lighting": tops brightest, then the two side directions, bottoms darkest
                float3 n = normalize( i.normal );
                float shade = 0.72 + 0.28 * n.y - 0.08 * abs( n.x ) + 0.04 * abs( n.z );
                c.rgb *= saturate( shade );
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }

    // Transparent things (text, decals...) keep their blending, unlit
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert( appdata v )
            {
                v2f o;
                o.pos = UnityObjectToClipPos( v.vertex );
                o.uv = TRANSFORM_TEX( v.uv, _MainTex );
                o.color = v.color;
                return o;
            }

            fixed4 frag( v2f i ) : SV_Target
            {
                fixed4 t = tex2D( _MainTex, i.uv );
                // Alpha-only textures (fonts) have no colour: treat them as white
                t.rgb = dot( t.rgb, 1 ) < 0.001 ? 1 : t.rgb;
                return t * _Color * i.color;
            }
            ENDCG
        }
    }

    // Hammer signs: one-sided text hidden behind walls, as in the other views
    SubShader
    {
        Tags { "RenderType"="HammerSign" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert( appdata v )
            {
                v2f o;
                o.pos = UnityObjectToClipPos( v.vertex );
                o.uv = TRANSFORM_TEX( v.uv, _MainTex );
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag( v2f i ) : SV_Target
            {
                fixed4 c = i.color;
                c.a *= tex2D( _MainTex, i.uv ).a;
                return c;
            }
            ENDCG
        }
    }
}
