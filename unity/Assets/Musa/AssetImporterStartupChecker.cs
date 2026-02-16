using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// アセットインポーター起動時チェッカー
/// NOTE: Unity起動時にローカルキャッシュを参照し、不足アセットがあれば通知する (#680)
/// NOTE: ネットワーク通信は行わず、ローカルキャッシュのGUID判定のみ
/// </summary>
[InitializeOnLoad]
public static class AssetImporterStartupChecker
{
    private const string SESSION_KEY = "AssetImporterStartupChecker_Checked";

    static AssetImporterStartupChecker()
    {
        // NOTE: 同セッション内で重複チェックしない
        if (SessionState.GetBool(SESSION_KEY, false)) return;

        // NOTE: delayCallでエディタ初期化完了後に実行
        EditorApplication.delayCall += CheckMissingAssets;
    }

    private static void CheckMissingAssets()
    {
        SessionState.SetBool(SESSION_KEY, true);

        // NOTE: カタログキャッシュを読み込み
        var cachePath = Path.Combine(Application.dataPath, "..", GoogleDriveAssetImporter.CATALOG_CACHE_FILE);
        if (!File.Exists(cachePath))
        {
            // NOTE: キャッシュ未存在時はスキップ（初回起動など）
            return;
        }

        GoogleDriveAssetImporter.AssetCatalog catalog;
        try
        {
            var json = File.ReadAllText(cachePath);
            catalog = JsonUtility.FromJson<GoogleDriveAssetImporter.AssetCatalog>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AssetImporter] カタログキャッシュ読み込み失敗: {ex.Message}");
            return;
        }

        if (catalog?.assets == null || catalog.assets.Length == 0) return;

        // NOTE: GUIDベースで不足アセットをチェック
        var missingAssets = new List<string>();
        foreach (var asset in catalog.assets)
        {
            if (asset.guids == null || asset.guids.Length == 0) continue;

            int found = 0;
            foreach (var guid in asset.guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(assetPath) && File.Exists(Path.Combine(Application.dataPath, "..", assetPath)))
                {
                    found++;
                }
            }

            if (found < asset.guids.Length)
            {
                int missing = asset.guids.Length - found;
                missingAssets.Add($"  - {asset.name}: {missing}/{asset.guids.Length} ファイル不足");
            }
        }

        if (missingAssets.Count == 0) return;

        // NOTE: 不足アセット通知ダイアログ
        var message = $"以下のアセットが不足しています:\n\n{string.Join("\n", missingAssets)}\n\nAsset Importerを開いてダウンロードしますか?";

        if (EditorUtility.DisplayDialog(
            "Asset Importer - 不足アセット検出",
            message,
            "Asset Importerを開く",
            "後で"))
        {
            GoogleDriveAssetImporter.ShowWindow();
        }
    }
}
