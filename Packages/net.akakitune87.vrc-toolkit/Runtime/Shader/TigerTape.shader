Shader "aki_lua87/Tiger Tape"
{
    Properties
    {
        [Header(Colors)]
        _YellowColor ("Yellow Color", Color) = (1.0, 0.64, 0.015, 1.0)
        _BlackColor ("Black Color", Color) = (0.018, 0.014, 0.01, 1.0)
        _Brightness ("Brightness", Range(0.1, 2.0)) = 1.0

        [Header(Pattern)]
        _StripeCount ("Stripe Density", Range(0.25, 32.0)) = 6.0
        _StripeAngle ("Stripe Angle", Range(-90.0, 90.0)) = 45.0
        _BlackWidth ("Black Stripe Width", Range(0.05, 0.95)) = 0.5
        _StripeOffset ("Stripe Offset", Range(-1.0, 1.0)) = 0.0
        _PatternScale ("Pattern Scale (X, Y)", Vector) = (1.0, 1.0, 0.0, 0.0)
        _EdgeSoftness ("Stripe Edge Softness", Range(0.0, 0.15)) = 0.01

        [Header(Finish)]
        _EdgeDarkening ("Tape Edge Darkening", Range(0.0, 0.5)) = 0.08
        _EdgeWidth ("Tape Edge Width", Range(0.001, 0.25)) = 0.06
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "IgnoreProjector" = "True"
            "VRCFallback" = "Unlit"
        }

        LOD 100
        Cull Off
        ZWrite On

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            fixed4 _YellowColor;
            fixed4 _BlackColor;
            half _Brightness;
            half _StripeCount;
            half _StripeAngle;
            half _BlackWidth;
            half _StripeOffset;
            float4 _PatternScale;
            half _EdgeSoftness;
            half _EdgeDarkening;
            half _EdgeWidth;

            v2f vert(appdata input)
            {
                v2f output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                UNITY_TRANSFER_FOG(output, output.position);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 centeredUv = (input.uv - 0.5) * _PatternScale.xy;
                float angle = radians(_StripeAngle);
                float2 stripeDirection = float2(cos(angle), sin(angle));
                float stripeCoordinate = dot(centeredUv, stripeDirection) * _StripeCount + _StripeOffset;

                // Distance from the center of the repeating black band.
                float bandDistance = abs(frac(stripeCoordinate) - 0.5);
                float halfBlackWidth = _BlackWidth * 0.5;
                float antialiasWidth = max(fwidth(stripeCoordinate) * 0.5, (float)_EdgeSoftness);
                float blackMask = 1.0 - smoothstep(
                    halfBlackWidth - antialiasWidth,
                    halfBlackWidth + antialiasWidth,
                    bandDistance
                );

                fixed3 color = lerp(_YellowColor.rgb, _BlackColor.rgb, blackMask);

                float distanceToTapeEdge = min(input.uv.y, 1.0 - input.uv.y);
                float edgeMask = 1.0 - smoothstep(0.0, _EdgeWidth, distanceToTapeEdge);
                color *= 1.0 - edgeMask * _EdgeDarkening;
                color *= _Brightness;

                fixed4 finalColor = fixed4(color, 1.0);
                UNITY_APPLY_FOG(input.fogCoord, finalColor);
                return finalColor;
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
