using System.Collections.Generic;
using UnityEngine;

namespace RiverTools
{
	/// Extracts a steepest-descent path over a Unity Terrain by following gravity
	/// projected on the terrain surface. Produces a list of world-space points.
	[ExecuteAlways]
	public class TerrainPathTracer : MonoBehaviour
	{
		[Header("Inputs")]
		public Terrain terrain;
		public Transform startPoint;

		[Header("Tracing Settings")]
		[Min(0.1f)] public float stepSizeMeters = 2f;
		[Range(1, 20000)] public int maxSteps = 5000;
		[Tooltip("Minimum slope (m per m) to continue tracing. Below this for several steps will stop.")]
		public float minSlope = 0.001f;
		[Tooltip("Number of consecutive low-slope steps before stopping")]
		public int lowSlopeHysteresis = 10;
		[Tooltip("Keep at least this many meters away from terrain edges")]
		public float edgePaddingMeters = 1f;
		[Tooltip("Smooth the path by applying a moving average with this window size (meters)")]
		public float smoothWindowMeters = 4f;

		[Header("Stop Conditions (optional)")]
		[Tooltip("If assigned, tracing will stop when reaching at or below this target's world Y height.")]
		public Transform stopAtTargetHeight;
		[Tooltip("If true and no target is provided, stop when reaching this world Y height.")]
		public bool stopAtWorldHeight = false;
		public float stopWorldHeightY = 0f;
		[Tooltip("Stop when total traced length exceeds this value (meters). 0 disables.")]
		public float maxLengthMeters = 0f;

		[Header("Debug/Output")]
		public Color gizmoColor = new Color(0.2f, 0.8f, 1f, 1f);
		public bool drawGizmos = true;
		[Tooltip("Resulting world-space path points (read-only)")]
		public List<Vector3> pathPoints = new List<Vector3>();

		readonly List<Vector3> _rawPoints = new List<Vector3>();

		[ContextMenu("Trace From Start")]
		public void TraceFromStart()
		{
			if (terrain == null || startPoint == null)
			{
				Debug.LogError("TerrainPathTracer: Assign terrain and startPoint.");
				return;
			}

			TraceFrom(startPoint.position);
		}

		public void TraceFrom(Vector3 worldStart)
		{
			pathPoints.Clear();
			_rawPoints.Clear();

			if (!GetTerrainBounds(out var terrainBounds))
			{
				Debug.LogError("TerrainPathTracer: Terrain has no valid TerrainData.");
				return;
			}

			Vector3 pos = ClampInsideBounds(worldStart, terrainBounds, edgePaddingMeters);
			int lowSlopeCount = 0;
			float traveled = 0f;
			float stopY = float.NegativeInfinity;
			if (stopAtTargetHeight != null) stopY = stopAtTargetHeight.position.y;
			else if (stopAtWorldHeight) stopY = stopWorldHeightY;

			for (int i = 0; i < maxSteps; i++)
			{
				if (!IsInsideBounds(pos, terrainBounds, edgePaddingMeters)) break;

				Vector3 downhill = ComputeDownhillDirection(pos);
				float slope = downhill.magnitude; // already normalized vs zero slope

				if (slope < minSlope)
				{
					lowSlopeCount++;
					if (lowSlopeCount >= lowSlopeHysteresis) break;
				}
				else
				{
					lowSlopeCount = 0;
				}

				// Move along the surface: step in XZ along downhill, then resample height at new XZ
				Vector3 moveXZ = new Vector3(downhill.x, 0f, downhill.z);
				if (moveXZ.sqrMagnitude < 1e-10f) break;

				_rawPoints.Add(pos);
				Vector3 step = moveXZ.normalized * stepSizeMeters;
				Vector3 next = pos + step;
				traveled += step.magnitude;
				if (maxLengthMeters > 0f && traveled >= maxLengthMeters) { pos = next; break; }
				// keep within bounds and glue to terrain height
				next.x = Mathf.Clamp(next.x, terrainBounds.min.x + edgePaddingMeters, terrainBounds.max.x - edgePaddingMeters);
				next.z = Mathf.Clamp(next.z, terrainBounds.min.z + edgePaddingMeters, terrainBounds.max.z - edgePaddingMeters);
				next.y = SampleHeight(next);
				// stop if reached height threshold
				if (!float.IsNegativeInfinity(stopY) && next.y <= stopY) { pos = next; break; }
				pos = next;
			}

			// add last position
			if (_rawPoints.Count == 0) _rawPoints.Add(pos);

			// Smooth
			SmoothRawIntoPath();
		}

		bool GetTerrainBounds(out Bounds worldBounds)
		{
			worldBounds = default;
			if (terrain == null || terrain.terrainData == null) return false;
			var size = terrain.terrainData.size;
			var origin = terrain.transform.position;
			worldBounds = new Bounds(origin + size * 0.5f, size);
			return true;
		}

		Vector3 ClampInsideBounds(Vector3 p, Bounds b, float pad)
		{
			p.x = Mathf.Clamp(p.x, b.min.x + pad, b.max.x - pad);
			p.z = Mathf.Clamp(p.z, b.min.z + pad, b.max.z - pad);
			p.y = SampleHeight(p);
			return p;
		}

		bool IsInsideBounds(Vector3 p, Bounds b, float pad)
		{
			return p.x > b.min.x + pad && p.x < b.max.x - pad && p.z > b.min.z + pad && p.z < b.max.z - pad;
		}

		float SampleHeight(Vector3 world)
		{
			if (terrain == null || terrain.terrainData == null) return world.y;
			return terrain.SampleHeight(world) + terrain.transform.position.y;
		}

		Vector3 ComputeDownhillDirection(Vector3 worldPos)
		{
			// Use terrain normal to get the steepest descent direction by projecting gravity onto the tangent plane
			if (terrain == null || terrain.terrainData == null) return Vector3.zero;
			Vector3 terrainOrigin = terrain.transform.position;
			Vector3 local = worldPos - terrainOrigin;
			Vector3 size = terrain.terrainData.size;
			float u = Mathf.Clamp01(local.x / size.x);
			float v = Mathf.Clamp01(local.z / size.z);
			Vector3 n = terrain.terrainData.GetInterpolatedNormal(u, v);
			n = n.sqrMagnitude > 0f ? n.normalized : Vector3.up;
			Vector3 g = Vector3.down;
			Vector3 tangentGravity = g - n * Vector3.Dot(g, n);
			return tangentGravity; // magnitude ~ sin(slope)
		}

		void SmoothRawIntoPath()
		{
			pathPoints.Clear();
			if (_rawPoints.Count == 0)
			{
				return;
			}

			if (smoothWindowMeters <= 0.01f)
			{
				pathPoints.AddRange(_rawPoints);
				return;
			}

			// moving average by distance window
			float window = Mathf.Max(0.01f, smoothWindowMeters);
			int head = 0;
			Vector3 acc = Vector3.zero;
			float accLen = 0f;
			Vector3 last = _rawPoints[0];
			acc += last;
			accLen = 0f;

			pathPoints.Add(last);
			for (int i = 1; i < _rawPoints.Count; i++)
			{
				Vector3 p = _rawPoints[i];
				acc += p;
				accLen += Vector3.Distance(_rawPoints[i - 1], p);

				while (accLen > window && head < i)
				{
					acc -= _rawPoints[head];
					accLen -= Vector3.Distance(_rawPoints[head], _rawPoints[head + 1]);
					head++;
				}

				int count = i - head + 1;
				if (count > 0)
				{
					Vector3 avg = acc / count;
					pathPoints.Add(avg);
				}
			}
		}

		void OnDrawGizmos()
		{
			if (!drawGizmos || pathPoints == null || pathPoints.Count < 2) return;
			Gizmos.color = gizmoColor;
			for (int i = 1; i < pathPoints.Count; i++)
			{
				Gizmos.DrawLine(pathPoints[i - 1], pathPoints[i]);
			}
		}
	}
}

