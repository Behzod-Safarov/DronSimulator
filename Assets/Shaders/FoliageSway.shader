// Wind-driven foliage. Standard lighting with alpha cutout, plus a vertex program that
// pushes each vertex downwind by a weight baked into UV2 by TreeGenerator:
//   uv2.x = sway weight  (0 at the trunk, 1 at the outermost crown tips)
//   uv2.y = phase offset (0..1, per leaf cluster, so branches never move in lockstep)
//
// Driven entirely by the global vectors WindVisuals publishes, so one material serves every
// tree and the whole stand reacts to the same wind the drone is flying in.
//
// The offset is built in world space and then pulled back into object space, which keeps it
// correct both for normally-transformed renderers and for statically batched ones (where
// unity_WorldToObject is identity and the vertices already live in world space).
Shader "DroneTrainer/FoliageSway"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB) Cutout (A)", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.45
        _Glossiness ("Smoothness", Range(0,1)) = 0.18
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _SwayScale ("Sway scale (per material)", Range(0,4)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard vertex:vert addshadow alphatest:_Cutoff
        #pragma target 3.0
        #pragma multi_compile_instancing

        sampler2D _MainTex;
        half _Glossiness;
        half _Metallic;
        half _SwayScale;
        fixed4 _Color;

        // Published by WindVisuals every frame.
        float4 _DtWindDir;      // xyz = unit wind direction (world), w = 0..1 strength
        float4 _DtWindParams;   // x = bend metres at full strength, y = sway Hz, z = flutter Hz, w = gust 0..1

        struct Input
        {
            float2 uv_MainTex;
        };

        void vert(inout appdata_full v)
        {
            float weight = v.texcoord1.x;
            if (weight <= 0.0001) return;

            float strength = _DtWindDir.w * _SwayScale;
            float bend     = _DtWindParams.x;
            float swayHz   = _DtWindParams.y;
            float flutHz   = _DtWindParams.z;
            float gust     = _DtWindParams.w;

            float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            // Phase from the baked per-cluster offset plus a long spatial wave, so gusts visibly
            // travel across a stand instead of every tree pulsing together.
            float phase = v.texcoord1.y * 6.2831853
                        + (worldPos.x + worldPos.z) * 0.07;

            float sway    = sin(_Time.y * swayHz * 6.2831853 + phase);
            float flutter = sin(_Time.y * flutHz * 6.2831853 + phase * 2.37);

            float3 dir = _DtWindDir.xyz;
            // Perpendicular (in the ground plane) for the shimmer that makes leaves read as leaves.
            float3 side = normalize(float3(-dir.z, 0, dir.x) + 1e-5);

            // A steady lean downwind that grows with the gust, an along-wind oscillation on top,
            // and a small cross-wind/vertical flutter.
            float amp = weight * bend * strength;
            float3 offset = dir  * amp * (0.55 + 0.30 * gust + 0.35 * sway)
                          + side * amp * 0.22 * flutter
                          + float3(0, amp * 0.16 * flutter, 0);

            v.vertex.xyz += mul(unity_WorldToObject, float4(offset, 0)).xyz;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }

    FallBack "Standard"
}
