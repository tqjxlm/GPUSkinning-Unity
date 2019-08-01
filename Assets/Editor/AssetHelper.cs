using UnityEngine;
using UnityEditor;
using System.IO;

public class AssetHelper
{
    public static string GetStoragePath(string subPath)
    {
        string assetDir = "Assets/Meshes";
        if (!Directory.Exists(assetDir + '/' + subPath))
        {
            AssetDatabase.CreateFolder(assetDir, subPath);
        }
        return assetDir + '/' + subPath;
    }

    public static void SaveAsset(Object asset, string path, bool overwrite=false)
    {
        if (overwrite)
        {
            AssetDatabase.DeleteAsset(path);
        }
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
    }
}
