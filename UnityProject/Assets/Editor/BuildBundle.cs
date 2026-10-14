using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

public static class BuildBundle
{
    private const string BundleName = "xsplitscreenbundle";

    public static void Run()
    {
        try
        {
            RunInner();
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"BuildBundle failed: {e}");
            EditorApplication.Exit(1);
        }
    }

    private static void RunInner()
    {
        // Assign every real prefab and its texture/material dependencies to the bundle,
        // matching the original bundle's structure (prefabs.md.wait -- see AssetBundle listing).
        string[] prefabPaths = Directory.GetFiles("Assets/XSplitScreen/RealPrefabs", "*.prefab");
        string[] texturePaths = Directory.GetFiles("Assets/XSplitScreen/Textures", "*.png", SearchOption.AllDirectories);
        string[] materialPaths = Directory.Exists("Assets/XSplitScreen/Materials")
            ? Directory.GetFiles("Assets/XSplitScreen/Materials", "*.mat", SearchOption.AllDirectories)
            : new string[0];

        foreach (var path in prefabPaths.Concat(texturePaths).Concat(materialPaths))
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null) { Debug.LogWarning($"No importer for {path}"); continue; }
            importer.SetAssetBundleNameAndVariant(BundleName, "");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string outDir = "AssetBundleOutput";
        Directory.CreateDirectory(outDir);

        var manifest = BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        if (manifest == null)
        {
            Debug.LogError("BuildAssetBundles returned null - build failed");
            return;
        }

        Debug.Log($"Built bundles: {string.Join(", ", manifest.GetAllAssetBundles())}");
        Debug.Log($"BUILD_SUCCESS: {Path.GetFullPath(Path.Combine(outDir, BundleName))}");
    }
}
