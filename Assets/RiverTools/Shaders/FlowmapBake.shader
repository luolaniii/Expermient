Shader "Hidden/RiverTools/FlowmapBake"
{
	Properties{}
	SubShader
	{
		Tags{ "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
		Pass
		{
			ZTest Always ZWrite Off Cull Off
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
				float _BaseSpeed;
				float _SlopeToSpeed;
				float _SlopeToFoam;
			CBUFFER_END

			struct Attributes
			{
				float3 positionOS : POSITION;
				float3 normalOS   : NORMAL;
				float4 tangentOS  : TANGENT;
				float2 uv0        : TEXCOORD0; // unwrap
				float2 uv2        : TEXCOORD2; // optional: path dir packed (0..1)
				float4 color      : COLOR;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 worldPos    : TEXCOORD0;
				float3 worldNormal : TEXCOORD1;
				float2 uv0         : TEXCOORD2;
				float2 uv2         : TEXCOORD3;
				float4 color       : COLOR;
			};

			Varyings vert(Attributes v)
			{
				Varyings o;
				float3 worldPos = TransformObjectToWorld(v.positionOS);
				float3 worldNormal = TransformObjectToWorldNormal(v.normalOS);
				o.worldPos = worldPos;
				o.worldNormal = worldNormal;
				o.uv0 = v.uv0;
				o.uv2 = v.uv2;
				o.color = v.color;
				// Position directly by UV (0..1) into clip space (-1..1)
				float2 uv = saturate(v.uv0);
				o.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
				return o;
			}

			float4 frag(Varyings i) : SV_Target
			{
				// Derive gravity-projected flow in UV space from height gradient in UV space
				float dYdu = ddx(i.worldPos.y);
				float dYdv = ddy(i.worldPos.y);
				float2 flowGravityUV = normalize(float2(-dYdu, -dYdv) + 1e-6);

				// Path flow along U axis (1,0). Optionally blend with provided uv2 dir
				float2 flowPathUV = float2(1.0, 0.0);
				if (any(i.uv2))
				{
					float2 dirXZ = i.uv2 * 2.0 - 1.0; // unpack
					// Project world XZ flow into UV assuming U aligns with river length; keep as (1,0)
				}

				// Weights from vertex color
				float pathW = saturate(i.color.r);
				float gravW = saturate(i.color.g);
				float foamBias = saturate(i.color.b);
				float wSum = max(1e-5, pathW + gravW);
				pathW /= wSum; gravW /= wSum;

				float2 flowUV = normalize(flowPathUV * pathW + flowGravityUV * gravW);
				float slopeMag = length(float2(dYdu, dYdv));
				float speed = saturate(_BaseSpeed * (1.0 + slopeMag * _SlopeToSpeed));
				float foam = saturate(foamBias + slopeMag * _SlopeToFoam);

				float2 packed = flowUV * 0.5 + 0.5;
				return float4(packed, foam, speed);
			}
			ENDHLSL
		}
	}
}

