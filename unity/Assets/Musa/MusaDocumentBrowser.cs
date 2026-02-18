#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// spec/ ディレクトリのスキャンとファイルマッピングを担う共通ユーティリティ
/// NOTE: MusaDocumentTab / MusaDocumentWindow から利用
/// </summary>
public static class MusaDocumentBrowser
{
    public class DocTreeNode
    {
        public string Name;
        public string FullPath;
        public bool IsDirectory;
        public List<DocTreeNode> Children = new List<DocTreeNode>();
        public bool IsExpanded;
    }

    // NOTE: スクリプト名 → .md フルパス のマッピングキャッシュ
    private static Dictionary<string, string> scriptToDocMap;

    /// <summary>
    /// spec/ ディレクトリの絶対パスを取得
    /// NOTE: Application.dataPath (= unity/Assets) から ../../spec で到達
    /// </summary>
    public static string GetSpecRootPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "spec"));
    }

    /// <summary>
    /// 指定ディレクトリ配下を再帰的にスキャンしてツリー構築
    /// </summary>
    public static List<DocTreeNode> BuildFileTree(string rootPath)
    {
        var nodes = new List<DocTreeNode>();
        if (!Directory.Exists(rootPath)) return nodes;

        // NOTE: ディレクトリ
        foreach (var dir in Directory.GetDirectories(rootPath))
        {
            var dirNode = new DocTreeNode
            {
                Name = Path.GetFileName(dir),
                FullPath = dir,
                IsDirectory = true,
                Children = BuildFileTree(dir)
            };
            // NOTE: 空ディレクトリはスキップ
            if (dirNode.Children.Count > 0)
                nodes.Add(dirNode);
        }

        // NOTE: .md ファイル
        foreach (var file in Directory.GetFiles(rootPath, "*.md"))
        {
            nodes.Add(new DocTreeNode
            {
                Name = Path.GetFileName(file),
                FullPath = file,
                IsDirectory = false
            });
        }

        return nodes;
    }

    /// <summary>
    /// スクリプト名に対応する仕様書パスを検索
    /// NOTE: 複数マッチ時は spec/code/ 配下を優先
    /// </summary>
    public static string FindDocForScript(string scriptName)
    {
        if (string.IsNullOrEmpty(scriptName)) return null;
        if (scriptToDocMap == null) RefreshCache();
        if (scriptToDocMap == null) return null;

        scriptToDocMap.TryGetValue(scriptName, out var path);
        return path;
    }

    /// <summary>
    /// .md ファイルの内容を読み込み
    /// </summary>
    public static string ReadDocContent(string mdPath)
    {
        if (string.IsNullOrEmpty(mdPath) || !File.Exists(mdPath)) return null;
        return File.ReadAllText(mdPath);
    }

    /// <summary>
    /// spec/ を再スキャンしてマッピング辞書を再構築
    /// </summary>
    public static void RefreshCache()
    {
        scriptToDocMap = new Dictionary<string, string>();
        var specRoot = GetSpecRootPath();
        if (!Directory.Exists(specRoot)) return;

        var codePath = Path.Combine(specRoot, "code");
        var allMdFiles = Directory.GetFiles(specRoot, "*.md", SearchOption.AllDirectories);

        foreach (var mdFile in allMdFiles)
        {
            var key = Path.GetFileNameWithoutExtension(mdFile);
            if (scriptToDocMap.ContainsKey(key))
            {
                // NOTE: spec/code/ 配下を優先
                if (mdFile.Replace('\\', '/').Contains("/code/"))
                    scriptToDocMap[key] = mdFile;
            }
            else
            {
                scriptToDocMap[key] = mdFile;
            }
        }
    }

    /// <summary>
    /// spec/ 直下のカテゴリ（ディレクトリ名）リストを返却
    /// </summary>
    public static string[] GetCategories()
    {
        var specRoot = GetSpecRootPath();
        if (!Directory.Exists(specRoot)) return new string[0];

        var dirs = Directory.GetDirectories(specRoot);
        var categories = new List<string>();
        foreach (var dir in dirs)
        {
            categories.Add(Path.GetFileName(dir));
        }
        categories.Sort();
        return categories.ToArray();
    }
}
#endif
