using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace GapperGames
{
    [CustomEditor(typeof(EDEN_ShaderToTerrain))]
    public class EDEN_ShaderToTerrain_Editor : Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EDEN_ShaderToTerrain terrain = (EDEN_ShaderToTerrain)target;

            if (GUILayout.Button("Generate Heightmap (Press Twice)"))
            {
                terrain.Generate();
            }

            if (GUILayout.Button("Create New Template Shader"))
            {
                terrain.CreateShader();
            }
        }
    }
}
