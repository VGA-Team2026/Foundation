using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Content;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// ビルド前に実行する処理
/// </summary>
public class PreprocessBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 0; // ビルド前処理の中での処理優先順位 (0で最高)

    public void OnPreprocessBuild(BuildReport report)
    {
        Debug.Log($"IPreprocessBuildWithReport.OnPreprocessBuild for {report.summary.platform} at {report.summary.outputPath}");

        //アプリ名がFoundation
        if (PlayerSettings.productName == "Foundation")
        {
            throw new BuildFailedException("アプリ名(Foundation)を変更してください");
        }

        //IL2CPP
        var buildTarget = UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
        if(PlayerSettings.GetScriptingBackend(buildTarget).ToString() != "IL2CPP")
        {
            //書き換える
            PlayerSettings.SetScriptingBackend(buildTarget, ScriptingImplementation.IL2CPP);
            Debug.LogWarning("ScriptingBackendをIL2CPPに変更しました");
        }

        //ビルドハッシュの更新
        BuildScript.BuildStateBuild(BuildState.TeamID);
    }

    void AddressableCheck(string path)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            throw new BuildFailedException($"そもそもAddressablesの設定がされていません。");
        }

        var guid = AssetDatabase.AssetPathToGUID(path);
        if (guid == null)
        {
            throw new BuildFailedException($"対象のアセットがありませんでした。[{path}]");
        }

        var find = settings.FindAssetEntry(guid);
        if (find == null)
        {
            throw new BuildFailedException($"Addressablesに登録されていません。[{path}]");
        }
    }
}