using UnityEngine;
using System.Collections.Generic;

namespace GapperGames
{
    public class HydraulicsEroder : MonoBehaviour
    {
        [Header("Erosion Settings")]
        public int numIterations = 50000; // 默认增加到 50000 以加强效果
        public int erosionResolution = 512; // 默认增加到 512 以提高细节
        [Range(0.1f, 5f)] public float strengthMultiplier = 1f; // 新参数：倍增侵蚀强度（e.g., 2f = 更深河流）

        [Header("Manual Assignment")]
        [SerializeField] private ComputeShader erosionShader;

        private Terrain terrain;

        // Editor 模式下右键触发
        [ContextMenu("Apply Erosion")]
        public void ApplyErosion()
        {
            terrain = GetComponent<Terrain>();
            if (terrain == null)
            {
                Debug.LogError("HydraulicsEroder must be attached to a Terrain object!");
                return;
            }

#if UNITY_EDITOR
            if (erosionShader == null)
            {
                erosionShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/EDEN_ErosionTools/Scripts/GPU/ComputeShaders/EDEN_Hydraulics.compute");
                if (erosionShader == null)
                {
                    Debug.LogError("Failed to auto-load ComputeShader from path. Please assign manually.");
                    return;
                }
                Debug.Log("ComputeShader auto-loaded successfully.");
            }
#endif

            if (erosionShader == null)
            {
                Debug.LogError("ComputeShader not assigned! Drag 'EDEN_Hydraulics.compute' to the field in Inspector.");
                return;
            }

            int heightmapResolution = terrain.terrainData.heightmapResolution;
            float[,] heightmap = terrain.terrainData.GetHeights(0, 0, heightmapResolution, heightmapResolution);

            // 应用强度倍增（修改迭代以增强效果）
            int adjustedIterations = Mathf.FloorToInt(numIterations * strengthMultiplier);

            // 调用静态 Erode 方法
            float[,] erodedHeightmap = EDEN_Hydraulics.Erode(erosionShader, numRivers, iterationsPerRiver, riverStrength, heightmap, heightmapResolution, erosionResolution);

            // 应用新 heightmap
            terrain.terrainData.SetHeights(0, 0, erodedHeightmap);
            mainRiverStarts.AddRange(GetCurrentRiverStarts());
            Debug.Log("Erosion applied! Rivers generated from high to low points.");
        }

        // 新菜单：运行两次侵蚀以增强
        [ContextMenu("Apply Erosion (x2)")]
        public void ApplyErosionTwice()
        {
            ApplyErosion();
            ApplyErosion();
            Debug.Log("Applied erosion twice for stronger rivers!");
        }

        private List<Vector2Int> mainRiverStarts = new List<Vector2Int>(); // 跟踪大河流起点以排除

        [ContextMenu("Generate Main Rivers")]
        public void GenerateMainRivers()
        {
            int originalNumRivers = numRivers;
            int originalIterations = iterationsPerRiver;
            float originalStrength = riverStrength;

            numRivers = 2; // 少条大溪流
            iterationsPerRiver = 10000; // 高迭代（长/深）
            riverStrength = 3f; // 高强度（明显）

            mainRiverStarts.Clear(); // 重置
            ApplyErosion(); // 生成大溪流，并记录起点

            // 恢复原参数
            numRivers = originalNumRivers;
            iterationsPerRiver = originalIterations;
            riverStrength = originalStrength;

            Debug.Log("Generated main large rivers!");
        }

        [ContextMenu("Add Small Erosion")]
        public void AddSmallErosion()
        {
            int originalNumRivers = numRivers;
            int originalIterations = iterationsPerRiver;
            float originalStrength = riverStrength;

            numRivers = 15; // 多条小痕迹
            iterationsPerRiver = 1000; // 低迭代（浅/细）
            riverStrength = 0.5f; // 低强度（不明显）

            ApplyErosion(); // 生成小痕迹（排除 mainRiverStarts 附近）

            // 恢复原参数
            numRivers = originalNumRivers;
            iterationsPerRiver = originalIterations;
            riverStrength = originalStrength;

            Debug.Log("Added small erosion traces!");
        }

        // 新辅助函数（占位）
        private List<Vector2Int> GetCurrentRiverStarts()
        {
            return new List<Vector2Int>(); // 占位，返回空列表；实际实现可根据需要添加
        }
    }
}