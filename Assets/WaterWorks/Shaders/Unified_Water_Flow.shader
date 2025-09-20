Shader "WaterWorks/Unified_Water_Flow"
{
	Properties
	{
		_BaseColor("Base Color", Color) = (0.1,0.3,0.5,1)
		_Transparency("Transparency", Range(0,1)) = 0.4
		_WorldTiling("World Tiling (1/m)", Float) = 0.3
		_FlowSpeed("Flow Speed", Float) = 0.15
		_GlobalFlowDir("Global Flow Dir (xyz)", Vector) = (0,0,1,0)
		_FlowMode("Flow Mode (0=River 1=Gravity 2=Global)", Int) = 0
		_UseUV2Dir("Use UV2 Direction (river)", Int) = 1
		_SteepnessThreshold("Steepness Threshold", Range(0,1)) = 0.35
		_UseTriForSteep("Use Triplanar On Steep", Int) = 1
		_NormalMap("Normal Map", 2D) = "bump" {}
		_NormalStrength("Normal Strength", Range(0,2)) = 1
		_FoamTex("Foam Tex", 2D) = "white" {}
		_FoamStrength("Foam Strength", Range(0,2)) = 0
	}
	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off
		Cull Off

		Pass
		{
			Name "ForwardUnlit"
			Tags { "LightMode"="UniversalForward" }
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseColor;
				float _Transparency;
				float _WorldTiling;
				float _FlowSpeed;
				float4 _GlobalFlowDir;
				int _FlowMode;
				int _UseUV2Dir;
				float _SteepnessThreshold;
				int _UseTriForSteep;
				float _NormalStrength;
				float _FoamStrength;
			CBUFFER_END

			TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
			TEXTURE2D(_FoamTex);  SAMPLER(sampler_FoamTex);

			struct Attributes
			{
				float3 positionOS : POSITION;
				float3 normalOS   : NORMAL;
				float4 tangentOS  : TANGENT;
				float2 uv0        : TEXCOORD0;
				float2 uv1        : TEXCOORD1; // UV2 for river dir
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 worldPos    : TEXCOORD0;
				float3 worldNormal : TEXCOORD1;
				float2 uv0         : TEXCOORD2;
				float2 uv1         : TEXCOORD3;
			};

			Varyings vert(Attributes v)
			{
				Varyings o;
				o.worldPos = TransformObjectToWorld(v.positionOS);
				o.positionCS = TransformWorldToHClip(o.worldPos);
				o.worldNormal = TransformObjectToWorldNormal(v.normalOS);
				o.uv0 = v.uv0;
				o.uv1 = v.uv1;
				return o;
			}

			float3 TriplanarSampleNormal(TEXTURE2D_PARAM(tex, samp), float3 worldPos, float3 worldNormal, float tiling)
			{
				float3 n = normalize(worldNormal);
				float3 w = abs(n);
				w /= (w.x + w.y + w.z + 1e-5);
				float2 uvX = worldPos.zy * tiling;
				float2 uvY = worldPos.xz * tiling;
				float2 uvZ = worldPos.xy * tiling;
				float3 nx = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvX));
				float3 ny = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvY));
				float3 nz = UnpackNormal(SAMPLE_TEXTURE2D(tex, samp, uvZ));
				float3 nWorld = normalize(nx * w.x + ny * w.y + nz * w.z);
				return nWorld;
			}

			float4 frag(Varyings i) : SV_Target
			{
				float3 N = normalize(i.worldNormal);
				float3 G = float3(0,-1,0);
				float3 dirGlob = normalize(_GlobalFlowDir.xyz);
				float3 dirRiver = normalize(float3(i.uv1 * 2.0 - 1.0, 0.0));
				float3 dirGrav  = normalize(G - N * dot(G, N));
				float3 dirSel = dirGlob;
				if (_FlowMode == 0) dirSel = (_UseUV2Dir != 0) ? dirRiver : dirGlob; // River
				if (_FlowMode == 1) dirSel = dirGrav;                                 // Gravity
				if (_FlowMode == 2) dirSel = dirGlob;                                 // Global

				float2 worldUV = i.worldPos.xz * _WorldTiling;
				float2 flowUV  = worldUV + dirSel.xz * (_FlowSpeed * _Time.y);

				float3 nSample;
				if (_UseTriForSteep != 0 && abs(N.y) < _SteepnessThreshold)
				{
					nSample = TriplanarSampleNormal(TEXTURE2D_ARGS(_NormalMap, sampler_NormalMap), i.worldPos, N, _WorldTiling);
				}
				else
				{
					nSample = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, flowUV));
				}
				nSample = normalize(lerp(float3(0,0,1), nSample, saturate(_NormalStrength)));

				float foam = _FoamStrength > 0 ? SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, flowUV).r * _FoamStrength : 0;
				float3 col = _BaseColor.rgb + foam.xxx;
				float alpha = saturate(1.0 - _Transparency);
				return float4(col, alpha);
			}
			ENDHLSL
		}
	}
	Fallback Off
}

