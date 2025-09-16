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
    [CreateAssetMenu(fileName = "Template Shader", menuName = "EDEN/Template Shader")]
    public class CreateShader
    {
        private Shader defaultShader;

        public CreateShader()
        {
            if (defaultShader == null)
            {
                defaultShader = (Shader)Resources.Load("DefaultShaderGraphToTerrain");
            }

            Type projectWindowUtilType = typeof(ProjectWindowUtil);
            MethodInfo getActiveFolderPath = projectWindowUtilType.GetMethod("GetActiveFolderPath", BindingFlags.Static | BindingFlags.NonPublic);
            object obj = getActiveFolderPath.Invoke(null, new object[0]);
            string pathToCurrentFolder = obj.ToString();
            AssetDatabase.CopyAsset("Assets/EDEN/Resources/DefaultShaderGraphToTerrain.shadergraph", pathToCurrentFolder + "/new Template Shader.shadergraph");
        }
    }
}
