using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static UnityEditor.Experimental.AssetDatabaseExperimental.AssetDatabaseCounters;

namespace GapperGames
{
    public class EDEN_ShaderToTerrain : MonoBehaviour
    {
        public Shader shader;
        private CustomRenderTexture rt;
        private Material material;
        private Terrain terrain;
        private Shader defaultShader;

        public void Generate()
        {
            if(rt == null)
            {
                rt = (CustomRenderTexture)Resources.Load("ShaderGraphToTerrain");
            }

            if (defaultShader == null)
            {
                defaultShader = (Shader)Resources.Load("DefaultShaderGraphToTerrain");
            }

            if (shader == null) { return; }

            material = new Material(shader);

            terrain = GetComponent<Terrain>();

            rt.material = material;
            rt.initializationMaterial = material;
            rt.Initialize();
            rt.Update();

            int heightSize = terrain.terrainData.heightmapResolution;
            float[,] heightMap = new float[heightSize, heightSize];

            Rect rectReadPicture = new Rect(0, 0, rt.width, rt.height);

            RenderTexture.active = rt;

            Texture2D rtTex2d = new Texture2D(rt.width, rt.height);
            rtTex2d.ReadPixels(rectReadPicture, 0, 0);
            rtTex2d.Apply();

            RenderTexture.active = null;

            for (int x = 0; x < heightSize; x++)
            {
                for (int y = 0; y < heightSize; y++)
                {
                    heightMap[x, y] = rtTex2d.GetPixel((int)(((float)x / heightSize) * rtTex2d.width), (int)(((float)y / heightSize) * rtTex2d.height)).r;
                }
            }


            terrain.terrainData.SetHeights(0, 0, heightMap);
            Smooth();
        }

        public void CreateShader()
        {
            Type projectWindowUtilType = typeof(ProjectWindowUtil);
            MethodInfo getActiveFolderPath = projectWindowUtilType.GetMethod("GetActiveFolderPath", BindingFlags.Static | BindingFlags.NonPublic);
            object obj = getActiveFolderPath.Invoke(null, new object[0]);
            string pathToCurrentFolder = obj.ToString();
            //AssetDatabase.CopyAsset("Assets/EDEN/Resources/DefaultShaderGraphToTerrain.shadergraph", pathToCurrentFolder + "/new Template Shader.shadergraph");
            AssetDatabase.CopyAsset("Assets/EDEN_ShadergraphToTerrain/Resources/DefaultShaderGraphToTerrain.shadergraph", "Assets/new Template Shader.shadergraph");
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath("Assets/new Template Shader.shadergraph");
        }

        public void Smooth()
        {
            //Initialize Variables
            terrain = GetComponent<Terrain>();
            int heightSize = terrain.terrainData.heightmapResolution;
            float[,] _heightMap = terrain.terrainData.GetHeights(0, 0, heightSize, heightSize);
            float[,] smoothedHeightmap = terrain.terrainData.GetHeights(0, 0, heightSize, heightSize);

            //Loop Over Heightmap
            for (int x = 0; x < heightSize; x++)
            {
                for (int z = 0; z < heightSize; z++)
                {
                    //Get Average Height
                    float total = 0;
                    for (int a = -2; a <= 2; a++)
                    {
                        for (int b = -2; b <= 2; b++)
                        {
                            int xPos = Mathf.Clamp(x + a, 0, heightSize - 1);
                            int zPos = Mathf.Clamp(z + b, 0, heightSize - 1);
                            total += _heightMap[xPos, zPos];
                        }
                    }
                    total /= ((2 * 2) + 1) * ((2 * 2) + 1);

                    smoothedHeightmap[x, z] = total;

                    if (x == 0 || x == heightSize - 1 || z == 0 || z == heightSize - 1)
                    {
                        smoothedHeightmap[x, z] = 0;
                    }
                }
            }

            //Apply Changes
            terrain.terrainData.SetHeights(0, 0, smoothedHeightmap);
        }
    }
}
