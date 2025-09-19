using UnityEngine;
#if UNITY_2022_2_OR_NEWER
using UnityEngine.Splines;
using Unity.Mathematics;
#endif

namespace RiverTools
{
	/// Blends an eroded TerrainData into a baseline TerrainData only inside a spline corridor.
	/// Baseline and Eroded must have the same heightmap resolution and size.
	[ExecuteAlways]
	public class TerrainBlendHeights : MonoBehaviour
	{
		public Terrain targetTerrain;
		public TerrainData baseline;
		public TerrainData eroded;
		#if UNITY_2022_2_OR_NEWER
		public SplineContainer spline;
		#endif

		[Header("Corridor")]
		[Min(0.1f)] public float corridorHalfWidth = 3f;
		[Min(0f)] public float falloffMeters = 2f; // additional fade-out beyond corridor
		[Min(8)] public int samplesAlong = 256;
		[Tooltip("Extra meters around the corridor to include in the edit region")]
		public float editBoundsPadding = 4f;

		[Header("Apply")]
		[Tooltip("Apply to full heightmap if true; otherwise only the union of local windows per sample.")]
		public bool applyFull = false;

		[ContextMenu("Blend Corridor From Eroded Into Baseline")]
		public void Blend()
		{
			if (targetTerrain == null || targetTerrain.terrainData == null)
			{
				Debug.LogError("TerrainBlendHeights: Assign targetTerrain.");
				return;
			}
			if (baseline == null || eroded == null)
			{
				Debug.LogError("TerrainBlendHeights: Assign baseline and eroded TerrainData.");
				return;
			}
			var td = targetTerrain.terrainData;
			if (baseline.heightmapResolution != eroded.heightmapResolution || baseline.heightmapResolution != td.heightmapResolution)
			{
				Debug.LogError("TerrainBlendHeights: Heightmap resolutions must match between baseline, eroded, and target terrain.");
				return;
			}
			if (baseline.size != eroded.size || baseline.size != td.size)
			{
				Debug.LogError("TerrainBlendHeights: Terrain sizes must match between baseline, eroded, and target terrain.");
				return;
			}
			#if UNITY_2022_2_OR_NEWER
			if (spline == null || spline.Spline == null || spline.Spline.Count < 2)
			{
				Debug.LogError("TerrainBlendHeights: Assign a valid SplineContainer.");
				return;
			}
			#endif

			int res = td.heightmapResolution;
			float sizeX = td.size.x;
			float sizeZ = td.size.z;
			Vector3 origin = targetTerrain.transform.position;

			float[,] baseH = baseline.GetHeights(0, 0, res, res);
			float[,] eroH = eroded.GetHeights(0, 0, res, res);
			float[,] outH = new float[res, res];
			float[,] mask = new float[res, res];

			// Track region edited for partial apply
			int minX = res - 1, minZ = res - 1, maxX = 0, maxZ = 0;

			#if UNITY_2022_2_OR_NEWER
			for (int i = 0; i < samplesAlong; i++)
			{
				float t = (float)i / (samplesAlong - 1);
				float3 posF = SplineUtility.EvaluatePosition(spline.Spline, t);
				float3 tanF = SplineUtility.EvaluateTangent(spline.Spline, t);
				Vector3 center = spline.transform.TransformPoint((Vector3)posF);
				Vector3 forward = (spline.transform.TransformVector((Vector3)math.normalize(tanF))).normalized;
				Vector3 left = Vector3.Cross(Vector3.up, forward);
				if (left.sqrMagnitude < 1e-6f) left = Vector3.left;
				left.Normalize();

				float radius = corridorHalfWidth + falloffMeters + editBoundsPadding;
				int pxRadiusX = Mathf.CeilToInt(radius / sizeX * (res - 1));
				int pxRadiusZ = Mathf.CeilToInt(radius / sizeZ * (res - 1));
				Vector2Int cpx = WorldToPixel(center, origin, sizeX, sizeZ, res);
				int x0 = Mathf.Clamp(cpx.x - pxRadiusX, 0, res - 1);
				int x1 = Mathf.Clamp(cpx.x + pxRadiusX, 0, res - 1);
				int z0 = Mathf.Clamp(cpx.y - pxRadiusZ, 0, res - 1);
				int z1 = Mathf.Clamp(cpx.y + pxRadiusZ, 0, res - 1);

				minX = Mathf.Min(minX, x0); minZ = Mathf.Min(minZ, z0);
				maxX = Mathf.Max(maxX, x1); maxZ = Mathf.Max(maxZ, z1);

				for (int z = z0; z <= z1; z++)
				{
					for (int x = x0; x <= x1; x++)
					{
						Vector3 wp = PixelToWorld(x, z, origin, sizeX, sizeZ, res);
						wp.y = center.y;
						Vector3 d = wp - center;
						float distAcross = Mathf.Abs(Vector3.Dot(d, left));
						float w = 0f;
						if (distAcross <= corridorHalfWidth)
						{
							w = 1f;
						}
						else if (falloffMeters > 0f && distAcross <= corridorHalfWidth + falloffMeters)
						{
							float a = (distAcross - corridorHalfWidth) / Mathf.Max(1e-4f, falloffMeters);
							w = 1f - Mathf.SmoothStep(0f, 1f, a);
						}
						if (w > mask[z, x]) mask[z, x] = w;
					}
				}
			}
			#endif

			// Compose output heights
			for (int z = 0; z < res; z++)
			{
				for (int x = 0; x < res; x++)
				{
					float m = mask[z, x];
					outH[z, x] = Mathf.Lerp(baseH[z, x], eroH[z, x], m);
				}
			}

			if (applyFull || minX > maxX || minZ > maxZ)
			{
				targetTerrain.terrainData.SetHeights(0, 0, outH);
			}
			else
			{
				int width = maxX - minX + 1;
				int height = maxZ - minZ + 1;
				float[,] region = new float[height, width];
				for (int rz = 0; rz < height; rz++)
				{
					for (int rx = 0; rx < width; rx++)
					{
						region[rz, rx] = outH[minZ + rz, minX + rx];
					}
				}
				targetTerrain.terrainData.SetHeights(minX, minZ, region);
			}
		}

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
	}
}

