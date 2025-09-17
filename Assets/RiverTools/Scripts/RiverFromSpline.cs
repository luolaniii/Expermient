using System.Collections.Generic;
using UnityEngine;
#if UNITY_2022_2_OR_NEWER
using UnityEngine.Splines;
using Unity.Mathematics;
#endif

namespace RiverTools
{
	[ExecuteAlways]
	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public class RiverFromSpline : MonoBehaviour
	{
		#if UNITY_2022_2_OR_NEWER
		public SplineContainer spline;
		#endif

		[Header("Shape")]
		[Min(0.1f)] public float baseHalfWidth = 2f;
		public AnimationCurve widthOverU = AnimationCurve.Linear(0, 1, 1, 1);
		[Min(2)] public int samplesPerSegment = 16;
		public bool buildCollider = false;

		[Header("Weights/Colors")]
		[Range(0f, 1f)] public float gravityBlendAtMaxSlope = 1f;
		[Tooltip("Slope in m per m that maps to gravityBlendAtMaxSlope")]
		public float slopeForMaxBlend = 0.5f;
		[Tooltip("Foam increases with slope. This scales the response.")]
		public float foamSlopeScale = 2.0f;

		[Header("Shading/Stability")]
		[Tooltip("When enabled, vertex colors encode path/gravity/foam weights; when off, vertex colors are white to avoid unintended tinting in shaders.")]
		public bool writeVertexColors = true;
		[Tooltip("Force normals to (0,1,0) after build to get stable lighting for water.")]
		public bool forceUpNormals = false;
		[Tooltip("Lift the surface slightly to prevent z-fighting with terrain.")]
		public float surfaceYOffset = 0.02f;

		Mesh _mesh;
		MeshFilter _mf;
		MeshCollider _mc;

		void OnEnable()
		{
			EnsureMesh();
			Build();
		}

		void OnValidate()
		{
			EnsureMesh();
			Build();
		}

		void EnsureMesh()
		{
			if (_mf == null) _mf = GetComponent<MeshFilter>();
			if (buildCollider && _mc == null) _mc = GetComponent<MeshCollider>();
			if (_mesh == null)
			{
				_mesh = new Mesh { name = "RiverMesh" };
				_mesh.MarkDynamic();
				_mf.sharedMesh = _mesh;
			}
		}

		public void Build()
		{
			#if UNITY_2022_2_OR_NEWER
			if (spline == null || spline.Spline == null || spline.Spline.Count < 2) return;

			float length = SplineUtility.CalculateLength(spline.Spline, (float4x4)spline.transform.localToWorldMatrix);
			if (length <= 0.001f) return;

			int segments = math.max(1, spline.Spline.Count - 1);
			int sampleCount = segments * samplesPerSegment + 1;

			var vertices = new List<Vector3>(sampleCount * 2);
			var uvs = new List<Vector2>(sampleCount * 2);
			var uv2 = new List<Vector2>(sampleCount * 2); // pack flow dir in UV space (optional)
			var colors = new List<Color>(sampleCount * 2);
			var indices = new List<int>((sampleCount - 1) * 6);

			Vector3 prevPos = default;
			bool hasPrev = false;
			float accumLen = 0f;

			for (int i = 0; i < sampleCount; i++)
			{
				float t = (float)i / (sampleCount - 1);
				float3 posF = SplineUtility.EvaluatePosition(spline.Spline, t);
				float3 tanF = SplineUtility.EvaluateTangent(spline.Spline, t);
				Vector3 pos = spline.transform.TransformPoint((Vector3)posF);
				Vector3 tan = math.normalize((Vector3)tanF);

				if (hasPrev)
				{
					accumLen += Vector3.Distance(pos, prevPos);
				}
				prevPos = pos; hasPrev = true;

				float uNorm = length > 1e-3f ? accumLen / length : 0f;
				float widthMul = Mathf.Max(0.0001f, widthOverU.Evaluate(uNorm));
				float halfWidth = baseHalfWidth * widthMul;

				Vector3 forward = (spline.transform.TransformVector(tan)).normalized;
				// Stable left: use cross with up, fallback to previous when nearly parallel
				if (!hasPrev) { }
				Vector3 left = Vector3.Cross(Vector3.up, forward);
				if (left.sqrMagnitude < 1e-6f)
				{
					left = hasPrev ? Vector3.Normalize(Vector3.Cross(Vector3.up, (prevPos - pos).normalized)) : Vector3.left;
				}
				else
				{
					left.Normalize();
				}

				Vector3 pL = pos + left * halfWidth + Vector3.up * surfaceYOffset;
				Vector3 pR = pos - left * halfWidth + Vector3.up * surfaceYOffset;

				// approximate slope from forward.y
				float dy = Mathf.Abs(forward.y);
				float slope = dy; // unitless ~ sin(theta)
				float gravityW = Mathf.Clamp01(slope / Mathf.Max(1e-4f, slopeForMaxBlend)) * gravityBlendAtMaxSlope;
				float pathW = 1f - gravityW;
				float foam = Mathf.Clamp01(slope * foamSlopeScale);

				// vertices in local space
				Vector3 lLocal = transform.InverseTransformPoint(pL);
				Vector3 rLocal = transform.InverseTransformPoint(pR);
				vertices.Add(lLocal);
				vertices.Add(rLocal);

				// UVs: U along length (0..1), V across (0..1)
				uvs.Add(new Vector2(uNorm, 0f));
				uvs.Add(new Vector2(uNorm, 1f));

				// UV2: pack world-space projected flow dir on XZ for debug/bake (optional)
				Vector2 flowXZ = new Vector2(forward.x, forward.z).normalized;
				uv2.Add(flowXZ * 0.5f + new Vector2(0.5f, 0.5f));
				uv2.Add(flowXZ * 0.5f + new Vector2(0.5f, 0.5f));

				// Colors: R=path weight, G=gravity weight, B=foam, A=1 (optional)
				Color c = writeVertexColors ? new Color(pathW, gravityW, foam, 1f) : Color.white;
				colors.Add(c);
				colors.Add(c);
			}

			for (int i = 0; i < sampleCount - 1; i++)
			{
				int vi = i * 2;
				indices.Add(vi + 0); indices.Add(vi + 1); indices.Add(vi + 2);
				indices.Add(vi + 1); indices.Add(vi + 3); indices.Add(vi + 2);
			}

			_mesh.Clear();
			_mesh.SetVertices(vertices);
			_mesh.SetUVs(0, uvs);
			_mesh.SetUVs(1, uv2);
			_mesh.SetColors(colors);
			_mesh.SetTriangles(indices, 0);
			_mesh.RecalculateNormals();
			if (forceUpNormals)
			{
				var norms = _mesh.normals;
				for (int i = 0; i < norms.Length; i++) norms[i] = Vector3.up;
				_mesh.normals = norms;
			}
			_mesh.RecalculateBounds();

			_mf.sharedMesh = _mesh;
			if (buildCollider)
			{
				if (_mc == null) _mc = gameObject.GetComponent<MeshCollider>();
				if (_mc == null) _mc = gameObject.AddComponent<MeshCollider>();
				_mc.sharedMesh = _mesh;
			}
			#endif
		}
	}
}

