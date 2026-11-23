using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class InspectMayak
{
    public static string Run()
    {
        var sb = new StringBuilder();
        string fbxPath = "Assets/FullScene/Mayak_FullScene.fbx";
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) return "FBX importer not found at " + fbxPath;

        sb.AppendLine("=== MODEL IMPORTER ===");
        sb.AppendLine($"materialImportMode={importer.materialImportMode}, materialLocation={importer.materialLocation}, importBlendShapes={importer.importBlendShapes}, importAnimation={importer.importAnimation}, animationType={importer.animationType}");

        var externalMap = importer.GetExternalObjectMap();
        sb.AppendLine($"\n=== EXTERNAL OBJECT MAP ({externalMap.Count}) ===");
        foreach (var kv in externalMap)
        {
            sb.AppendLine($"  [{kv.Key.type.Name}] {kv.Key.name} -> {(kv.Value != null ? AssetDatabase.GetAssetPath(kv.Value) : "NULL")}");
        }

        var assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        sb.AppendLine("\n=== EMBEDDED MATERIALS ===");
        var embeddedMats = new List<Material>();
        foreach (var a in assets)
        {
            if (a is Material m)
            {
                embeddedMats.Add(m);
                Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : (m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white);
                float met = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : -1f;
                float sm = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : (m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : -1f);
                string baseTex = m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null ? m.GetTexture("_BaseMap").name : "none";
                sb.AppendLine($"  {m.name} | sh={(m.shader ? m.shader.name : "null")} | col=({c.r:F2},{c.g:F2},{c.b:F2},{c.a:F2}) | met={met:F2} | sm={sm:F2} | baseTex={baseTex}");
            }
        }
        sb.AppendLine($"Total embedded materials: {embeddedMats.Count}");

        // Also inspect unique materials used by renderers in Mayak_FullScene in the open scene
        var root = GameObject.Find("Mayak_FullScene");
        sb.AppendLine($"\n=== SCENE INSTANCE Mayak_FullScene (found={root != null}) ===");
        if (root != null)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var matMap = new Dictionary<string, string>();
            int overrideCount = 0;
            foreach (var r in renderers)
            {
                if (PrefabUtility.HasPrefabInstanceAnyOverrides(r.gameObject, false))
                {
                    var mods = PrefabUtility.GetPropertyModifications(r);
                    if (mods != null && mods.Length > 0) overrideCount++;
                }
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) { matMap["NULL"] = "NULL"; continue; }
                    string path = AssetDatabase.GetAssetPath(m);
                    matMap[m.name] = $"{path} | sh={(m.shader ? m.shader.name : "null")}";
                }
            }
            sb.AppendLine($"Renderers: {renderers.Length}, unique mats: {matMap.Count}, renderer prefab overrides: {overrideCount}");
            var keys = new List<string>(matMap.Keys);
            keys.Sort();
            foreach (var k in keys)
                sb.AppendLine($"  {k} -> {matMap[k]}");
        }

        // List all clips / blendshapes
        sb.AppendLine("\n=== ANIMATION CLIPS ===");
        foreach (var a in assets)
        {
            if (a is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                sb.AppendLine($"  Clip: {clip.name} (len={clip.length:F2}s)");
        }

        return sb.ToString();
    }
}
