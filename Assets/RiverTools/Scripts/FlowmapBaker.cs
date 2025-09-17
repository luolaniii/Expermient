using UnityEngine;

namespace RiverTools
{
	/// Bakes a flowmap in UV space (0..1) for the target mesh. Output RG encodes UV flow direction (packed 0..1),
	/// B encodes foam weight, A encodes speed (normalized).
	[ExecuteAlways]
	public class FlowmapBaker : MonoBehaviour
	{
		public MeshFilter targetMeshFilter;
		public int textureSize = 1024;
		public float baseSpeed = 1.0f;
		public float slopeToSpeedScale = 2.0f;
		public float slopeToFoamScale = 2.0f;

		Material _bakeMat;

		[ContextMenu("Bake Flowmap (PNG)")]
		public void Bake()
		{
			if (targetMeshFilter == null || targetMeshFilter.sharedMesh == null)
			{
				Debug.LogError("FlowmapBaker: Assign a MeshFilter with a UV0-unwrapped river mesh.");
				return;
			}

			if (_bakeMat == null)
			{
				_bakeMat = new Material(Shader.Find("Hidden/RiverTools/FlowmapBake"));
			}

			RenderTexture rt = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
			rt.wrapMode = TextureWrapMode.Clamp;
			rt.filterMode = FilterMode.Bilinear;
			var prev = RenderTexture.active;

			var cb = new UnityEngine.Rendering.CommandBuffer();
			cb.SetRenderTarget(rt);
			cb.ClearRenderTarget(true, true, Color.black);
			_bakeMat.SetFloat("_BaseSpeed", baseSpeed);
			_bakeMat.SetFloat("_SlopeToSpeed", slopeToSpeedScale);
			_bakeMat.SetFloat("_SlopeToFoam", slopeToFoamScale);
			cb.DrawMesh(targetMeshFilter.sharedMesh, Matrix4x4.identity, _bakeMat, 0, 0);
			UnityEngine.Graphics.ExecuteCommandBuffer(cb);
			cb.Release();

			RenderTexture.active = rt;
			Texture2D tex = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false, true);
			ex.ReadPixels(new Rect(0, 0, textureSize, textureSize), 0, 0, false);
			ex.Apply(false, false);
			RenderTexture.active = prev;

			#if UNITY_EDITOR
			string path = UnityEditor.EditorUtility.SaveFilePanelInProject("Save Flowmap PNG", "RiverFlowmap", "png", "Choose a location to save the baked flowmap.");
			if (!string.IsNullOrEmpty(path))
			{
				var bytes = tex.EncodeToPNG();
				System.IO.File.WriteAllBytes(path, bytes);
				UnityEditor.AssetDatabase.Refresh();
				Debug.Log($"Flowmap saved: {path}");
			}
			#endif

			// cleanup
			DestroyImmediate(tex);
			rt.Release();
		}
	}
}

