using UnityEditor;
using UnityEngine;

public class WebGLBuildScript
{
    [MenuItem("Build/Build WebGL")]
    public static void BuildWebGL()
    {
        string outputPath = "../../web/public/unity-build";
        
        BuildPlayerOptions opts = new BuildPlayerOptions
        {
            scenes = new[]
            {
                "Assets/Scenes/StartScene.unity", "Assets/Scenes/LobbyScene.unity", "Assets/Scenes/RoomScene.unity"
                , "Assets/Scenes/GameScene.unity"
            },
            locationPathName = outputPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };
        
        BuildPipeline.BuildPlayer(opts);
        Debug.Log($"WebGL build complete → {outputPath}");
    }
}