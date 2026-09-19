// FPV camera look: barrel (fisheye) distortion, chromatic fringe, sensor grain,
// analog line tearing/snow when the video link is weak, scanlines and vignette.
Shader "DroneTrainer/FpvAnalog"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Distortion ("Barrel", Float) = 0.35
        _Zoom ("Zoom", Float) = 1.3
        _Noise ("Noise", Range(0, 1)) = 0.06
        _Breakup ("Breakup", Range(0, 1)) = 0
        _Vignette ("Vignette", Range(0, 1)) = 0.35
        _Chroma ("Chromatic", Float) = 0.004
        _Scanline ("Scanline", Range(0, 1)) = 0.12
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Distortion, _Zoom, _Noise, _Breakup, _Vignette, _Chroma, _Scanline, _Seed;

            float hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float2 distort(float2 uv, float k)
            {
                float aspect = _MainTex_TexelSize.z / _MainTex_TexelSize.w;
                float2 c = uv - 0.5;
                c.x *= aspect;
                float r2 = dot(c, c);
                float f = 1.0 + k * r2 + k * 0.5 * r2 * r2;
                c *= f / _Zoom;
                c.x /= aspect;
                return c + 0.5;
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;

                // Analog line tearing when the link is weak
                float row = floor(uv.y * 240.0);
                float lineNoise = hash(float2(row, floor(_Seed * 60.0)));
                if (lineNoise < _Breakup * 0.5)
                    uv.x += (hash(float2(row * 3.1, _Seed + 1.0)) - 0.5) * 0.10 * _Breakup;

                float2 uvR = distort(uv, _Distortion + _Chroma);
                float2 uvG = distort(uv, _Distortion);
                float2 uvB = distort(uv, _Distortion - _Chroma);
                float inside = step(0.0, uvG.x) * step(uvG.x, 1.0) * step(0.0, uvG.y) * step(uvG.y, 1.0);

                fixed4 col;
                col.r = tex2D(_MainTex, uvR).r;
                col.g = tex2D(_MainTex, uvG).g;
                col.b = tex2D(_MainTex, uvB).b;
                col.a = 1;
                col.rgb *= inside;

                // Sensor grain
                float g = hash(uv * 1000.0 + _Seed) - 0.5;
                col.rgb += g * _Noise;

                // Snow rows when breakup is strong
                float snow = step(lineNoise, _Breakup * _Breakup * 0.7);
                col.rgb = lerp(col.rgb, (g + 0.5).xxx, snow * 0.85);

                // Scanlines
                col.rgb *= 1.0 - _Scanline * 0.5 * (0.5 + 0.5 * sin(uv.y * 480.0 * 3.14159));

                // Vignette
                float2 v = uv - 0.5;
                col.rgb *= 1.0 - _Vignette * dot(v, v) * 2.0;

                // Slight analog desaturation
                float lum = dot(col.rgb, float3(0.3, 0.59, 0.11));
                col.rgb = lerp(col.rgb, lum.xxx, 0.12);
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}

