using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;


#if UNITY_EDITOR
internal static class SetupStreamingAssets
{
    private const string DataFolder = "/../StreamingAssets~";

    [UnityEditor.InitializeOnLoadMethod]
    private static void Setup()
    {
        var dataFolder = GetProjectRelativePath(Path.GetFullPath(GetScriptFolder() + DataFolder));
        if (!Directory.Exists(dataFolder))
        {
            return;
        }
        foreach (var from in Directory.EnumerateFiles(dataFolder))
        {
            var to = Application.streamingAssetsPath + '/' + Path.GetFileName(from);
            if (File.Exists(to))
            {
                continue;
            }
            Debug.Log($"Copy file: {from} => {to}");
            try
            {
                File.Copy(from, to);
            }
            catch
            {
                // nop
            }
        }
    }
    private static string GetProjectRelativePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Application.dataPath, "Assets");
    }
    private static string GetScriptFolder()
    {
        var scriptPath = GetCallerFilePath();
        return Path.GetDirectoryName(scriptPath);
    }
    private static string GetCallerFilePath([System.Runtime.CompilerServices.CallerFilePath] string filePath = null)
    {
        return filePath;
    }
}
#endif
