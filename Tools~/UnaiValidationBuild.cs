using System;
using System.IO;
using UnAI.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UnaiValidationBuild
{
    public static void Build()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("UNAI build smoke test").AddComponent<UnaiManager>();
        const string scene = "Assets/UnaiBuildSmoke.unity";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scene);
        var target = EditorUserBuildSettings.activeBuildTarget;
        string output = target == BuildTarget.WebGL ? "Builds/WebGL" : "Builds/Windows/UNAI.exe";
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scene }, target = target, locationPathName = output
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception($"UNAI validation build failed: {report.summary.result}, {report.summary.totalErrors} errors.");
        Debug.Log($"[UNAI validation] {Application.unityVersion}: {target} build passed.");
    }
}
