Shader "ValheimNewUI/LocationNegative"
{
    Properties
    {
        _MainTex ("Font Atlas", 2D) = "white" {}
        _FaceColor ("Text Color", Color) = (1,1,1,1)
        _FaceDilate ("Dilate", Float) = -0.075
        _WeightNormal ("Weight", Float) = 0
        _WeightBold ("Bold Weight", Float) = 0.5
        _GradientScale ("SDF Range", Float) = 5
        _ScaleRatioA ("Scale Ratio", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend OneMinusDstColor OneMinusSrcAlpha
        ColorMask RGB

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Vertex
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                half opacity : TEXCOORD1;
            };
            sampler2D _MainTex;
            half _FaceDilate;
            half4 _FaceColor;

            Varyings vert(Vertex v)
            {
                Varyings o;
                o.position = UnityObjectToClipPos(v.position);
                o.uv = v.uv;
                o.opacity = v.color.a * _FaceColor.a;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half edge = tex2D(_MainTex, i.uv).a - (0.5 - 0.5 * _FaceDilate);
                half coverage = saturate(edge / max(fwidth(edge), 0.001) + 0.5) * i.opacity;
                return coverage.xxxx;
            }
            ENDCG
        }
    }
}
