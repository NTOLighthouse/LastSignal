using System.Text;
using UnityEditor;
using UnityEngine;

public static class InspectMatProps
{
    public static string Run()
    {
        var sb = new StringBuilder();
        var guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/FullScene" });
        sb.AppendLine($"Found {guids.Length} materials:");
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) continue;
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.magenta;
            float met = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : -1;
            float sm = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : -1;
            float surf = m.HasProperty("_Surface") ? m.GetFloat("_Surface") : -1;
            Color em = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            bool emOn = m.IsKeywordEnabled("_EMISSION");
            string bm = m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "-";
            string nm = m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") ? m.GetTexture("_BumpMap").name : "-";
            string mg = m.HasProperty("_MetallicGlossMap") && m.GetTexture("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap").name : "-";
            string emt = m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") ? m.GetTexture("_EmissionMap").name : "-";
            sb.AppendLine($"{m.name,-28} | col=({c.r:F2},{c.g:F2},{c.b:F2},{c.a:F2}) | met={met:F2} sm={sm:F2} surf={surf:F0} | emOn={emOn} em=({em.r:F2},{em.g:F2},{em.b:F2}) | B={bm} N={nm} MG={mg} E={emt}");
        }
        return sb.ToString();
    }
}
