// URP conversion of LowPolyWater_Pack/Shaders/WaterShaded.shader (Built-in RP), with
// the wave animation moved off the CPU. LowPolyWater.cs used to rewrite every vertex
// and call RecalculateNormals() each frame; both jobs now happen on the GPU, so that
// component should be removed from the water object.
Shader "LowPolyWater/WaterShaded URP"
{
    Properties
    {
        // Look settings, edited here on the material.
        // Keep Water colour well below white. It gets multiplied by sun + ambient, so a channel
        // near 1.0 clips to flat white and loses every bit of lighting variation it had.
        [Header(Surface)] [Space(4)]
        _BaseColor ("Water colour (looking down)", Color) = (0.05, 0.32, 0.42, 1)
        _HorizonColor ("Horizon colour (grazing)", Color) = (0.42, 0.72, 0.82, 1)
        _FresnelPower ("Horizon blend", Range(0.5, 8)) = 4
        _NormalStrength ("Facet sharpness", Range(1, 8)) = 3
        _SpecColor ("Sun glint colour", Color) = (1,1,1,1)
        _Shininess ("Sun glint tightness", Float) = 200
        _ShoreTex ("Foam texture", 2D) = "black" {}

        _InvFadeParemeter ("Shore blend (Edge, Shore, Distance scale)", Vector) = (0.2 ,0.39, 0.5, 1.0)

        [MaterialToggle] _isInnerAlphaBlendOrColor ("Fade inner to color or alpha?", Float) = 0

        // Everything below is written by World_InfiniteOcean, which owns the mesh and therefore
        // is the only thing that knows the triangle size these have to agree with. Hidden so
        // there is exactly one place to edit each value - change them on the component.
        [HideInInspector] _WaveHeight ("Wave height", Float) = 1.5
        [HideInInspector] _WaveLength ("Wave length", Float) = 40
        [HideInInspector] _WaveSpeed ("Wave speed", Float) = 3
        [HideInInspector] _WaveDirection ("Wave direction", Float) = 45
        [HideInInspector] _WaveChaos ("Chaos", Float) = 0.6
        [HideInInspector] _WaveFadeDistance ("Wave fade distance", Float) = 0

        [HideInInspector] _BumpTiling ("Foam tiling", Vector) = (0.02, 0.02, 0.012, 0.012)
        [HideInInspector] _BumpDirection ("Foam movement", Vector) = (20, 20, 20, -33)
        [HideInInspector] _Foam ("Foam (intensity, crest cutoff)", Vector) = (0.6, 0.5, 0, 0)
        [HideInInspector] _FoamFadeDistance ("Foam fade distance", Float) = 0

        [HideInInspector] [Toggle(_INFINITE_OCEAN)] _InfiniteOcean ("Infinite ocean", Float) = 0
        [HideInInspector] _MeshExtent ("Mesh half size", Float) = 1000
        [HideInInspector] _FollowSnap ("Grid cell size", Float) = 10
        [HideInInspector] _HorizonReach ("Horizon reach", Float) = 1
        [HideInInspector] _HorizonFalloff ("Horizon falloff", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        LOD 500

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZTest LEqual
            ZWrite Off
            Cull Off
            ColorMask RGB

            HLSLPROGRAM
            #pragma target 3.0

            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            // Edge blending needs the camera depth texture. Enable "Depth Texture" on the
            // URP Asset, or switch the material to WATER_EDGEBLEND_OFF.
            #pragma multi_compile WATER_EDGEBLEND_ON WATER_EDGEBLEND_OFF

            #pragma shader_feature_local_vertex _INFINITE_OCEAN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #ifdef WATER_EDGEBLEND_ON
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif

            TEXTURE2D(_ShoreTex);
            SAMPLER(sampler_ShoreTex);

            // Global, pushed by World_InfiniteOcean from World_Origin's accumulated rebase offset.
            // Waves and foam are sampled from scene space, which the floating origin yanks out from
            // under them every time it slides the world back toward zero; adding the offset back
            // puts both in true world space so a rebase is invisible. Stays zero in scenes that have
            // no floating origin. Deliberately outside UnityPerMaterial - it is a global, and putting
            // it in there would break SRP Batcher compatibility.
            float4 _WorldOriginOffset;

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                half4  _HorizonColor;
                half4  _SpecColor;
                half   _FresnelPower;
                half   _NormalStrength;
                half   _Shininess;
                float4 _InvFadeParemeter;
                float4 _BumpTiling;
                float4 _BumpDirection;
                float4 _Foam;
                half   _isInnerAlphaBlendOrColor;

                float  _WaveHeight;
                float  _WaveLength;
                float  _WaveSpeed;
                float  _WaveDirection;
                float  _WaveChaos;
                float  _WaveFadeDistance;

                float  _InfiniteOcean;
                float  _MeshExtent;
                float  _FollowSnap;
                float  _HorizonReach;
                float  _HorizonFalloff;
                float  _FoamFadeDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS    : SV_POSITION;
                float4 bumpCoords    : TEXCOORD0;
                float4 screenPos     : TEXCOORD1;
                float3 positionRelWS : TEXCOORD2; // camera-relative: keeps ddx/ddy precise far from the origin
                half3  planeNormalWS : TEXCOORD3; // undisplaced plane normal, used only to orient the face normal
                half   waveOffset    : TEXCOORD4;
                half   fogFactor     : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Wrapped to a fixed cell period so the pattern keeps full float precision however many
            // kilometres out the vertex sits. Wrapping the cell rather than the sample position is
            // what makes it seamless - neighbouring cells wrap to neighbouring values, so there is
            // no join to see.
            float OceanHash(float2 cell)
            {
                cell -= floor(cell / 4096.0) * 4096.0;
                return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
            }

            float OceanNoise(float2 position)
            {
                float2 cell = floor(position);
                float2 f = frac(position);

                // Smoothstep the interpolation, otherwise the surface creases along every cell edge.
                f = f * f * (3.0 - 2.0 * f);

                float a = OceanHash(cell);
                float b = OceanHash(cell + float2(1, 0));
                float c = OceanHash(cell + float2(0, 1));
                float d = OceanHash(cell + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y) * 2.0 - 1.0;
            }

            // Two ways of being a wave, blended by _WaveChaos. Directional rather than the pack's
            // radial ripple, because a radial wave needs an origin to ripple out of and an infinite
            // ocean has no such point.
            float WaveHeightAt(float2 worldXZ)
            {
                float angle = radians(_WaveDirection);
                float2 heading = float2(cos(angle), sin(angle));
                float wavelength = max(_WaveLength, 0.01);
                float travel = _Time.y * _WaveSpeed;

                // Clean rolling swell: two sines crossing at an angle.
                float swell = 0.0;
                float swellWeight = 0.0;
                float amplitude = 1.0;
                float octaveLength = wavelength;
                float2 direction = heading;

                [unroll]
                for (int i = 0; i < 2; i++)
                {
                    // frac() before the sine keeps the phase inside [0,1) however far out the vertex
                    // is and however long the game has been running. Without it the phase grows
                    // without bound and 32-bit floats quietly lose the wave shape.
                    float phase = (dot(worldXZ, direction) - travel) / octaveLength;

                    swell += sin(TWO_PI * frac(phase)) * amplitude;
                    swellWeight += amplitude;

                    octaveLength *= 0.55;
                    amplitude *= 0.6;
                    direction = mul(float2x2(0.77, -0.64, 0.64, 0.77), direction); // turn ~40 degrees
                }

                swell /= max(swellWeight, 1e-4);

                // Broken chop: layered value noise drifting along with the swell. This is the part
                // that stops the ocean reading as a corrugated roof - stacking pure sines never
                // loses the stripe, however many of them go in.
                float chop = 0.0;
                float chopWeight = 0.0;
                float2 samplePos = (worldXZ - heading * travel) / wavelength;

                amplitude = 1.0;

                [unroll]
                for (int j = 0; j < 3; j++)
                {
                    chop += OceanNoise(samplePos) * amplitude;
                    chopWeight += amplitude;

                    samplePos *= 2.03; // off exactly 2 so octaves never line up on the same grid
                    amplitude *= 0.5;
                }

                chop /= max(chopWeight, 1e-4);

                return _WaveHeight * lerp(swell, chop, saturate(_WaveChaos));
            }

            // Damps detail toward the horizon, where waves and foam are smaller than a pixel
            // and only ever read as shimmer. Damping starts at half of fadeDistance; 0 = off.
            half DistanceFade(float distanceToCamera, float fadeDistance)
            {
                return (fadeDistance > 0.0)
                    ? 1.0 - smoothstep(fadeDistance * 0.5, fadeDistance, distanceToCamera)
                    : 1.0;
            }

            half4 SampleFoam(float4 coords)
            {
                half4 foam = (SAMPLE_TEXTURE2D(_ShoreTex, sampler_ShoreTex, coords.xy)
                            * SAMPLE_TEXTURE2D(_ShoreTex, sampler_ShoreTex, coords.zw)) - 0.125;
                return foam;
            }

            // Driven by the URP main light instead of _WorldSpaceLightPos0 / _LightColor0.
            half4 CalculateBaseColor(float3 positionWS, float3 normalWS)
            {
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);

                Light mainLight = GetMainLight();
                half3 lightColor = mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation);

                // Water is barely a diffuse surface. Most of what reads as "the sea" is how much
                // sky it bounces at you, and that depends on how edge-on you are looking: straight
                // down you see into the water, out towards the horizon you see reflected sky.
                // Shading it as plain Lambert is what made this a flat sheet that only ever got
                // brighter or darker as a whole, no matter what the light was doing.
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDirWS)), max(_FresnelPower, 0.01));
                half3 waterColor = lerp(_BaseColor.rgb, _HorizonColor.rgb, fresnel);

                // UNITY_LIGHTMODEL_AMBIENT does not exist in URP; the ambient probe is the equivalent.
                half3 ambient = SampleSH(normalWS);
                half NdotL = saturate(dot(normalWS, mainLight.direction));

                half3 lit = waterColor * (ambient + lightColor * NdotL);

                // Blinn-Phong keeps the sun glint tight and lets it smear along a wave face the way
                // a real sun streak does, which Phong's reflect() lobe does not.
                half3 halfVector = normalize(mainLight.direction + viewDirWS);
                half glint = pow(saturate(dot(normalWS, halfVector)), max(_Shininess, 1.0));

                return half4(lit + lightColor * _SpecColor.rgb * glint, 1.0);
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionOS = input.positionOS.xyz;

            #ifdef _INFINITE_OCEAN
                // ONE predicate for "the grid is no longer evenly spaced". The stretch here and the
                // follow further down must agree on it: snapping in whole cells is only valid on an
                // even grid, and these two conditions disagreeing is precisely what re-rolls the
                // whole surface every time the camera moves. Keep it as a single flag - do not
                // re-derive the same idea twice from _HorizonReach and _HorizonFalloff separately.
                bool stretched = _HorizonReach > 1.0;

                if (stretched)
                {
                    // Throw the outer rings toward the horizon while keeping triangle density near
                    // the camera. Scaling the radius rather than each axis independently matters:
                    // per-axis compression squeezes only one axis for vertices near the centre
                    // lines, slivering them into a visible cross directly under the camera.
                    // Assumes the plane is centred on its own origin, which the generated one is.
                    float extent  = max(_MeshExtent, 1e-3);
                    float2 unitXZ = positionOS.xz / extent;
                    float radius  = max(length(unitXZ), 1e-5);

                    positionOS.xz = (unitXZ / radius) * pow(radius, max(_HorizonFalloff, 1.0))
                                  * extent * _HorizonReach;
                }
            #endif

                float3 positionWS = TransformObjectToWorld(positionOS);

            #ifdef _INFINITE_OCEAN
                // Slide the sheet under the camera. Unity still culls against the mesh's original
                // bounds, which is what World_InfiniteOcean.cs is there to fix.
                float2 originWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0)).xz;
                float2 followOffset = _WorldSpaceCameraPos.xz - originWS;

                // Moving in whole grid cells is free of charge when the grid is even: every vertex
                // lands exactly where another vertex just was, so the world-space wave field under
                // it does not shift at all. That only holds while spacing is uniform - the horizon
                // stretch deliberately makes it uneven, and then a snapped jump drops every vertex
                // on a brand new spot and visibly re-rolls the whole surface. So stretch and snap
                // are mutually exclusive: a stretched sheet slides smoothly instead.
                if (_FollowSnap > 0.0 && !stretched)
                    followOffset = floor(followOffset / _FollowSnap) * _FollowSnap;

                positionWS.xz += followOffset;
            #endif

                // Everything the surface is sampled from lives in true world space, so the water
                // pattern survives a floating-origin rebase without shifting.
                float2 sampleXZ = positionWS.xz + _WorldOriginOffset.xz;

                float waveHeight = WaveHeightAt(sampleXZ)
                                 * DistanceFade(distance(positionWS.xz, _WorldSpaceCameraPos.xz), _WaveFadeDistance);
                positionWS.y += waveHeight;

                output.positionCS = TransformWorldToHClip(positionWS);

                // Equivalent to the built-in ComputeScreenPos / URP's positionNDC.
                float4 ndc = output.positionCS * 0.5;
                output.screenPos.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
                output.screenPos.zw = output.positionCS.zw;

                output.bumpCoords = (sampleXZ.xyxy + _Time.xxxx * _BumpDirection.xyzw) * _BumpTiling.xyzw;

                output.positionRelWS = positionWS - _WorldSpaceCameraPos;
                output.planeNormalWS = TransformObjectToWorldNormal(input.normalOS);
                // Was always 0 in the original because the vertex offset hook was never filled in.
                // Now that there is real displacement it drives the crest foam term below. Scaled
                // by wave height so the Foam cutoff stays a plain 0-1 "how high up the crest",
                // instead of meaning something different every time wave height is touched.
                output.waveOffset    = saturate(waveHeight / max(_WaveHeight, 1e-4));
                output.fogFactor     = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Flat per-triangle normals from screen-space derivatives. This replaces the
                // split-mesh + RecalculateNormals() pass LowPolyWater.cs did every frame, and
                // unlike interpolated vertex normals it follows the GPU displacement.
                float3 normalWS = normalize(cross(ddx(input.positionRelWS), ddy(input.positionRelWS)));
                normalWS = (dot(normalWS, input.planeNormalWS) < 0.0) ? -normalWS : normalWS;

                // Tilt each facet further away from flat before lighting it. A wave 1.5m tall and
                // 40m long only leans about 13 degrees, which is nowhere near enough tilt for the
                // sun to pick out one facet from the next. The geometry is untouched - this only
                // steepens what the light sees, which is the whole trick behind low-poly water.
                normalWS = normalize(input.planeNormalWS + (normalWS - input.planeNormalWS) * _NormalStrength);

                float3 positionWS = input.positionRelWS + _WorldSpaceCameraPos;

                half4 edgeBlendFactors = half4(1.0, 0.0, 0.0, 0.0);

                #ifdef WATER_EDGEBLEND_ON
                    float2 screenUV = input.screenPos.xy / input.screenPos.w;
                    float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    edgeBlendFactors = saturate(_InvFadeParemeter * (sceneDepth - input.screenPos.w));
                    edgeBlendFactors.y = 1.0 - edgeBlendFactors.y;
                #endif

                half4 baseColor = CalculateBaseColor(positionWS, normalWS);

                half foamFade = DistanceFade(length(input.positionRelWS.xz), _FoamFadeDistance);
                half4 foam = SampleFoam(input.bumpCoords * 2.0);
                baseColor.rgb += foam.rgb * _Foam.x * foamFade
                               * (edgeBlendFactors.y + saturate(input.waveOffset - _Foam.y));

                if (_isInnerAlphaBlendOrColor == 0)
                    baseColor.rgb += 1.0 - edgeBlendFactors.x;
                if (_isInnerAlphaBlendOrColor == 1.0)
                    baseColor.a = edgeBlendFactors.x;

                baseColor.rgb = MixFog(baseColor.rgb, input.fogFactor);
                return baseColor;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
