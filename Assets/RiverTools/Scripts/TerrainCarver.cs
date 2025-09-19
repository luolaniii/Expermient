using UnityEngine;
#if UNITY_2022_2_OR_NEWER
using UnityEngine.Splines;
using Unity.Mathematics;
#endif

namespace RiverTools
{
	/// Deforms a Unity Terrain along a spline to carve a riverbed.
	/// WARNING: This modifies TerrainData heights destructively. Duplicate your TerrainData before carving.
	[ExecuteAlways]
	public class TerrainCarver : MonoBehaviour
	{
		public Terrain terrain;
		#if UNITY_2022_2_OR_NEWER
		public SplineContainer spline;
		#endif

		[Header("Carve Shape")]
		[Min(0.1f)] public float baseHalfWidth = 2f;    // meters, each side
		[Min(0.0f)] public float maxDepthMeters = 0.3f; // meters at center line (safer default)
		public AnimationCurve widthOverU = AnimationCurve.Linear(0, 1, 1, 1);
		public AnimationCurve depthOverU = AnimationCurve.Linear(0, 1, 1, 1);
		[Tooltip("Softness of the river cross section edges (0=hard, 1=soft)")]
		[Range(0f,1f)] public float edgeSoftness = 0.5f;

		[Header("Spline Mode Options")]
		[Tooltip("Project spline sample to terrain height before carving (so it always sits on ground).")]
		public bool projectCenterToTerrain = true;
		[Tooltip("Vertical offset applied after projecting center to terrain (negative makes the spline slightly below ground).")]
		public float centerYOffset = -0.05f;

		[Header("Width Source")]
		[Tooltip("Use RiverFromSpline width as base and scale it for carving.")]
		public bool useRiverWidth = false;
		public RiverFromSpline riverSource;
		[Range(0.1f, 1.0f)] public float carveWidthScale = 0.7f; // narrower than river
		[Min(0.1f)] public float overrideHalfWidthMeters = 2.5f; // used when not using river width

		[Header("Sampling")]
		[Min(8)] public int samplesAlong = 512;
		[Tooltip("Inflate edit bounds in meters around the path to limit SetHeights region")]
		public float editBoundsPadding = 5f;

		[Header("Baseline/Reapply")]
		[Tooltip("If set, carving starts from this TerrainData's heights each time.")]
		public TerrainData sourceTerrainData;
		[Tooltip("Remember current heights in memory as the baseline to re-carve from.")]
		public bool rememberBaselineInMemory = false;
		[SerializeField] int _baselineRes = 0;
		[SerializeField] float[] _baselineHeights1D; // row-major (z,x)

		[Header("Automation")]
		[Tooltip("Automatically re-carve in editor when parameters change.")]
		public bool autoCarveOnValidate = false;

		[Header("Direct Trace Mode (no spline)")]
		[Tooltip("If enabled, carve by tracing downhill from a start point along the terrain surface, without requiring a spline.")]
		public bool carveFromStart = false;
		public Transform traceStart;
		[Min(0.1f)] public float traceStepMeters = 2f;
		[Range(1,20000)] public int traceMaxSteps = 5000;
		[Tooltip("Minimum slope (m per m) to continue tracing. Below threshold for several steps will stop.")]
		public float traceMinSlope = 0.001f;
		public int traceLowSlopeHysteresis = 10;
		[Tooltip("Stop when reaching this target height (Y). If null and stopAtWorldHeight false, ignored.")]
		public Transform stopAtTargetHeight;
		public bool stopAtWorldHeight = false;
		public float stopWorldHeightY = 0f;
		[Tooltip("Stop when total traced length exceeds this value (meters). 0 disables.")]
		public float traceMaxLengthMeters = 0f;

		[Header("Smoothing")]
		[Tooltip("Optional box-blur passes over carved corridor to smooth pits.")]
		[Range(0,4)] public int postSmoothPasses = 0;
		[Tooltip("Box blur radius (in pixels) for post-smoothing.")]
		[Range(1,3)] public int postSmoothRadiusPx = 1;

		// Debug fields removed in slim version

		[ContextMenu("Carve Terrain")]
		public void Carve()
		{
			if (terrain == null || terrain.terrainData == null)
			{
				Debug.LogError("TerrainCarver: Assign a valid Terrain with TerrainData.");
				return;
			}
			#if UNITY_2022_2_OR_NEWER
			if (spline == null || spline.Spline == null || spline.Spline.Count < 2)
			{
				Debug.LogError("TerrainCarver: Assign a valid SplineContainer.");
				return;
			}
			#endif

			var td = terrain.terrainData;
			int res = td.heightmapResolution;
			float sizeX = td.size.x;
			float sizeZ = td.size.z;
			float sizeY = td.size.y;
			Vector3 origin = terrain.transform.position;

			// Initialize heights from source/baseline/current
			float[,] heights;
			if (sourceTerrainData != null && sourceTerrainData.heightmapResolution == res)
			{
				heights = sourceTerrainData.GetHeights(0, 0, res, res);
			}
			else if (rememberBaselineInMemory && _baselineHeights1D != null && _baselineRes == res)
			{
				heights = new float[res, res];
				for (int z = 0; z < res; z++)
				{
					for (int x = 0; x < res; x++)
					{
						int idx = z * res + x;
						heights[z, x] = _baselineHeights1D[idx];
					}
				}
			}
			else
			{
				heights = td.GetHeights(0, 0, res, res);
			}

			// Track edited region to minimize SetHeights area
			int minX = res - 1, minZ = res - 1, maxX = 0, maxZ = 0;

			{
				// Iterate along spline
				#if UNITY_2022_2_OR_NEWER
				float length = SplineUtility.CalculateLength(spline.Spline, (float4x4)spline.transform.localToWorldMatrix);
				if (length <= 1e-4f) return;
				#endif

				for (int i = 0; i < samplesAlong; i++)
				{
					float t = (float)i / (samplesPerSegmentClamp(samplesAlong) - 1);
					#if UNITY_2022_2_OR_NEWER
					float3 posF = SplineUtility.EvaluatePosition(spline.Spline, t);
					float3 tanF = SplineUtility.EvaluateTangent(spline.Spline, t);
					Vector3 center = spline.transform.TransformPoint((Vector3)posF);
					Vector3 forward = (spline.transform.TransformVector((Vector3)math.normalize(tanF))).normalized;
					#else
					continue;
					#endif

					if (projectCenterToTerrain)
					{
						float y = terrain.SampleHeight(center) + origin.y + centerYOffset;
						center.y = y;
					}

					float uNorm = t;
					ApplyTrenchAtCenter(ref heights, res, origin, sizeX, sizeZ, sizeY,
						center, forward, uNorm,
						useRiverWidth && riverSource != null ? riverSource.baseHalfWidth * carveWidthScale : Mathf.Max(0.0001f, overrideHalfWidthMeters));
				}

			{
				// Apply full heightmap in spline mode to ensure complete corridor is written
				if (postSmoothPasses > 0)
				{
					RunBoxBlur(ref heights, postSmoothPasses, postSmoothRadiusPx);
				}
				terrain.terrainData.SetHeights(0, 0, heights);
			}
		}

		int samplesPerSegmentClamp(int s) { return Mathf.Max(2, s); }

		int ApplyTrenchAtCenter(ref float[,] heights, int res, Vector3 origin, float sizeX, float sizeZ, float sizeY,
			Vector3 center, Vector3 forward, float uNorm, float baseHalfWidthMeters)
		{
			float halfWidth = baseHalfWidthMeters * Mathf.Max(0.0001f, widthOverU.Evaluate(uNorm));
			float depthMeters = maxDepthMeters * Mathf.Max(0.0f, depthOverU.Evaluate(uNorm));

			Vector3 left = Vector3.Cross(Vector3.up, forward);
			if (left.sqrMagnitude < 1e-6f) left = Vector3.left;
			left.Normalize();

			float radiusX = halfWidth + editBoundsPadding;
			float radiusZ = halfWidth + editBoundsPadding;
			int pxRadiusX = Mathf.CeilToInt(radiusX / sizeX * (res - 1));
			int pxRadiusZ = Mathf.CeilToInt(radiusZ / sizeZ * (res - 1));

			Vector2Int centerPX = WorldToPixel(center, origin, sizeX, sizeZ, res);
			int x0 = Mathf.Clamp(centerPX.x - pxRadiusX, 0, res - 1);
			int x1 = Mathf.Clamp(centerPX.x + pxRadiusX, 0, res - 1);
			int z0 = Mathf.Clamp(centerPX.y - pxRadiusZ, 0, res - 1);
			int z1 = Mathf.Clamp(centerPX.y + pxRadiusZ, 0, res - 1);

			// Track edited region bounds
			// Note: We cannot update minX/minZ/maxX/maxZ here because they are local variables in Carve();
			// we intentionally left region minimization as a whole SetHeights for simplicity in trace mode.

			int edited = 0;
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					Vector3 wp = PixelToWorld(x, z, origin, sizeX, sizeZ, res);
					wp.y = center.y;
					Vector3 d = wp - center;
					float distAcross = Mathf.Abs(Vector3.Dot(d, left));
					float a = distAcross / Mathf.Max(0.0001f, halfWidth);
					if (a > 1.0f) continue;

					float s = 1.0f - a;
					s = Smooth01(s, edgeSoftness);

					float deltaMeters = depthMeters * s;
					float deltaH = deltaMeters / sizeY;
					float h = heights[z, x];
					h = Mathf.Max(0f, h - deltaH);
					heights[z, x] = h;
					edited++;
				}
			}
			return edited;
		}


		void RunBoxBlur(ref float[,] h, int passes, int radius)
		{
			int res = h.GetLength(0);
			radius = Mathf.Clamp(radius, 1, 3);
			float[,] tmp = new float[res, res];
			for (int p = 0; p < passes; p++)
			{
				for (int z = 0; z < res; z++)
				{
					for (int x = 0; x < res; x++)
					{
						float sum = 0f; int cnt = 0;
						for (int dz = -radius; dz <= radius; dz++)
						for (int dx = -radius; dx <= radius; dx++)
						{
							int xx = x + dx, zz = z + dz;
							if (xx < 0 || xx >= res || zz < 0 || zz >= res) continue;
							sum += h[zz, xx]; cnt++;
						}
						tmp[z, x] = sum / Mathf.Max(1, cnt);
					}
				}
				// swap
				var t = h; h = tmp; tmp = t;
			}
		}

		[ContextMenu("Capture Baseline From Current Terrain")]
		public void CaptureBaseline()
		{
			if (terrain == null || terrain.terrainData == null) return;
			var td = terrain.terrainData;
			int res = td.heightmapResolution;
			var heights = td.GetHeights(0, 0, res, res);
			_baselineRes = res;
			_baselineHeights1D = new float[res * res];
			for (int z = 0; z < res; z++)
			{
				for (int x = 0; x < res; x++)
				{
					_baselineHeights1D[z * res + x] = heights[z, x];
				}
			}
		}

		[ContextMenu("Clear Baseline")]
		public void ClearBaseline()
		{
			_baselineRes = 0;
			_baselineHeights1D = null;
		}

		void OnValidate()
		{
			if (autoCarveOnValidate)
			{
				Carve();
			}
		}

		// Gizmos/debug removed in slim version

		static Vector2Int WorldToPixel(Vector3 world, Vector3 origin, float sizeX, float sizeZ, int res)
		{
			float u = Mathf.InverseLerp(origin.x, origin.x + sizeX, world.x);
			float v = Mathf.InverseLerp(origin.z, origin.z + sizeZ, world.z);
			int x = Mathf.RoundToInt(u * (res - 1));
			int z = Mathf.RoundToInt(v * (res - 1));
			return new Vector2Int(x, z);
		}

		static Vector3 PixelToWorld(int x, int z, Vector3 origin, float sizeX, float sizeZ, int res)
		{
			float u = (float)x / (res - 1);
			float v = (float)z / (res - 1);
			float wx = Mathf.Lerp(origin.x, origin.x + sizeX, u);
			float wz = Mathf.Lerp(origin.z, origin.z + sizeZ, v);
			return new Vector3(wx, 0f, wz);
		}

		static float Smooth01(float x, float softness)
		{
			// Smoothstep-like profile with adjustable softness
			x = Mathf.Clamp01(x);
			float s = Mathf.Lerp(x, x * x * (3 - 2 * x), Mathf.Clamp01(softness));
			return s;
		}
	}
}

