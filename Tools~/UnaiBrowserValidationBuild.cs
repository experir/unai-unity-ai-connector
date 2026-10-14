// Copy into Assets/Editor of an isolated project, and copy the runner/jslib into Assets.
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UnaiBrowserValidationBuild
{
    public static void Build()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("UNAI Browser Validation").AddComponent<UnaiBrowserValidation>();
        const string scene = "Assets/UnaiBrowserValidation.unity";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scene);
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.initialMemorySize = 64;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scene }, target = BuildTarget.WebGL,
            locationPathName = "Builds/WebGL", options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("UNAI browser validation player failed to build.");
        Debug.Log("[UNAI browser] WebGL validation player built successfully.");
    }
}
