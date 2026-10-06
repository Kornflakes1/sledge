// Selection tints drawn over the Hammer views: a flat colour, depth tested against the scene and
// pulled a little towards the camera so it doesn't fight with the surface it lies on.
Shader "Hidden/Hammer/Overlay"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Back
            Offset -2, -2

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            float4 vert( float4 vertex : POSITION ) : SV_POSITION
            {
                return UnityObjectToClipPos( vertex );
            }

            fixed4 frag() : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
