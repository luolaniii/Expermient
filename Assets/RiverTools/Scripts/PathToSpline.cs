using System.Collections.Generic;
using UnityEngine;
#if UNITY_2022_2_OR_NEWER
using UnityEngine.Splines;
#endif

namespace RiverTools
{
	[ExecuteAlways]
	public class PathToSpline : MonoBehaviour
	{
		public TerrainPathTracer tracer;
		#if UNITY_2022_2_OR_NEWER
		public SplineContainer splineContainer;
		#endif
		[Tooltip("Meters between knots after resampling the traced path")]
		[Min(0.1f)] public float knotSpacingMeters = 3f;

		[ContextMenu("Build Spline From Traced Path")]
		public void BuildSpline()
		{
			if (tracer == null || tracer.pathPoints == null || tracer.pathPoints.Count < 2)
			{
				Debug.LogError("PathToSpline: No traced path. Run TerrainPathTracer first.");
				return;
			}

			List<Vector3> pts = Resample(tracer.pathPoints, knotSpacingMeters);

			#if UNITY_2022_2_OR_NEWER
			if (splineContainer == null)
			{
				var go = new GameObject("RiverSpline");
				go.transform.SetParent(transform, false);
				splineContainer = go.AddComponent<SplineContainer>();
			}

			var spline = new Spline();
			for (int i = 0; i < pts.Count; i++)
			{
				Vector3 local = splineContainer.transform.InverseTransformPoint(pts[i]);
				var knot = new BezierKnot(local);
				spline.Add(knot);
			}
			spline.Closed = false;
			splineContainer.Spline = spline;
			#else
			Debug.LogWarning("Unity Splines requires 2022.2+ and the com.unity.splines package.");
			#endif
		}

		static List<Vector3> Resample(List<Vector3> src, float spacing)
		{
			List<Vector3> outPts = new List<Vector3>();
			if (src.Count == 0) return outPts;
			outPts.Add(src[0]);
			float acc = 0f;
			for (int i = 1; i < src.Count; i++)
			{
				float d = Vector3.Distance(src[i - 1], src[i]);
				acc += d;
				if (acc >= spacing)
				{
					outPts.Add(src[i]);
					acc = 0f;
				}
			}
			if (outPts[outPts.Count - 1] != src[src.Count - 1]) outPts.Add(src[src.Count - 1]);
			return outPts;
		}
	}
}

