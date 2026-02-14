using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

/// <summary>
/// Package Updater - 全Unityパッケージの更新確認・一括更新を行うEditorWindow
/// 依存関係解析とバージョン制約機能付き
/// </summary>
public class PackageUpdaterWindow : EditorWindow
{
    // NOTE: タブ
    private int selectedTab;
    private readonly string[] tabNames = { "All", "Unity", "OpenUPM", "Git" };

    // NOTE: フィルタ
    private bool showUpdatableOnly;
    private bool showUnusedOnly;
    private string searchFilter = "";

    // NOTE: パッケージデータ
    private List<PackageEntry> packages = new List<PackageEntry>();
    private Vector2 packageScrollPosition;

    // NOTE: 非同期リクエスト
    private ListRequest listRequest;
    private SearchRequest currentSearchRequest;
    private AddRequest currentAddRequest;
    private RemoveRequest currentRemoveRequest;
    private Queue<PackageEntry> searchQueue = new Queue<PackageEntry>();
    private int totalSearchCount;
    private int completedSearchCount;

    // NOTE: 状態
    private enum WindowState { Idle, Listing, Checking, Updating, Analyzing, Removing }
    private WindowState state = WindowState.Idle;
    private string statusMessage = "";
    private float progress;

    // NOTE: ログ
    private List<string> logs = new List<string>();
    private Vector2 logScrollPosition;
    private bool showLog;

    // NOTE: GUIStyleキャッシュ
    private GUIStyle headerStyle;
    private GUIStyle updateAvailableStyle;
    private GUIStyle upToDateStyle;
    private GUIStyle gitWarningStyle;
    private GUIStyle usedStyle;
    private GUIStyle transitiveStyle;
    private GUIStyle unusedStyle;
    private bool stylesInitialized;

    // NOTE: scopedRegistriesのスコープキャッシュ
    private HashSet<string> openUpmScopes;

    // NOTE: バージョン制約
    private Dictionary<string, VersionConstraint> versionConstraints = new Dictionary<string, VersionConstraint>();

    // NOTE: Remove対象
    private PackageEntry removingPackage;

    [MenuItem("Musa/Package Updater %#p")]
    public static void ShowWindow()
    {
        var window = GetWindow<PackageUpdaterWindow>("Package Updater");
        window.minSize = new Vector2(500, 400);
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        LoadOpenUpmScopes();
        LoadManifestConfig();
        RefreshPackageList();
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void LoadOpenUpmScopes()
    {
        openUpmScopes = new HashSet<string>();
        try
        {
            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath)) return;
            string json = File.ReadAllText(manifestPath);
            // NOTE: scopedRegistriesからスコープを抽出（全てのscopedRegistriesを処理）
            int idx = json.IndexOf("\"scopedRegistries\"", StringComparison.Ordinal);
            if (idx < 0) return;

            // NOTE: Regex.Matchesで全ての"scopes"配列を収集
            var scopesMatches = Regex.Matches(json.Substring(idx), @"""scopes""\s*:\s*\[(.*?)\]", RegexOptions.Singleline);
            foreach (Match match in scopesMatches)
            {
                string scopesBlock = match.Groups[1].Value;
                foreach (string part in scopesBlock.Split('"'))
                {
                    string trimmed = part.Trim().Trim(',').Trim();
                    if (!string.IsNullOrEmpty(trimmed) && trimmed.Contains("."))
                        openUpmScopes.Add(trimmed);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PackageUpdater] Failed to parse scoped registries: {e.Message}");
        }
    }

    private void InitStyles()
    {
        if (stylesInitialized) return;

        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13,
            padding = new RectOffset(4, 4, 4, 4)
        };

        updateAvailableStyle = new GUIStyle(EditorStyles.label) { fontSize = 11 };
        updateAvailableStyle.normal.textColor = new Color(0.2f, 0.8f, 1f);

        upToDateStyle = new GUIStyle(EditorStyles.label) { fontSize = 11 };
        upToDateStyle.normal.textColor = new Color(0.4f, 0.8f, 0.4f);

        gitWarningStyle = new GUIStyle(EditorStyles.label) { fontSize = 11 };
        gitWarningStyle.normal.textColor = new Color(1f, 0.7f, 0.2f);

        usedStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
        usedStyle.normal.textColor = new Color(0.4f, 0.8f, 0.4f);

        transitiveStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
        transitiveStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);

        unusedStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 10 };
        unusedStyle.normal.textColor = new Color(1f, 0.5f, 0.3f);

        stylesInitialized = true;
    }

    private void OnEditorUpdate()
    {
        // NOTE: List リクエスト処理
        if (listRequest != null && listRequest.IsCompleted)
        {
            HandleListCompleted();
            listRequest = null;
            Repaint();
        }

        // NOTE: Search リクエスト処理
        if (currentSearchRequest != null && currentSearchRequest.IsCompleted)
        {
            HandleSearchCompleted();
            currentSearchRequest = null;
            ProcessNextSearch();
            Repaint();
        }

        // NOTE: Add リクエスト処理
        if (currentAddRequest != null && currentAddRequest.IsCompleted)
        {
            HandleAddCompleted();
            currentAddRequest = null;
            Repaint();
        }

        // NOTE: Remove リクエスト処理
        if (currentRemoveRequest != null && currentRemoveRequest.IsCompleted)
        {
            HandleRemoveCompleted();
            currentRemoveRequest = null;
            Repaint();
        }
    }

    private void OnGUI()
    {
        InitStyles();

        // NOTE: タブ
        selectedTab = GUILayout.Toolbar(selectedTab, tabNames, GUILayout.Height(25));
        EditorGUILayout.Space(4);

        // NOTE: ツールバー
        DrawToolbar();

        // NOTE: 進捗バー
        if (state == WindowState.Checking || state == WindowState.Listing || state == WindowState.Analyzing)
        {
            DrawProgressBar();
        }

        EditorGUILayout.Space(2);

        // NOTE: パッケージ一覧
        DrawPackageList();

        // NOTE: ログ
        DrawLogSection();

        // NOTE: ステータスバー
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label(statusMessage);
        GUILayout.FlexibleSpace();
        GUILayout.Label($"{packages.Count} packages");
        EditorGUILayout.EndHorizontal();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal();

        GUI.enabled = state == WindowState.Idle;
        if (GUILayout.Button("Refresh", GUILayout.Width(70), GUILayout.Height(24)))
        {
            RefreshPackageList();
        }
        if (GUILayout.Button("Check Updates", GUILayout.Width(100), GUILayout.Height(24)))
        {
            CheckAllUpdates();
        }
        if (GUILayout.Button("Analyze", GUILayout.Width(70), GUILayout.Height(24)))
        {
            AnalyzeDependencies();
        }

        int selectedCount = GetFilteredPackages().Count(p => p.selected && p.hasUpdate);
        GUI.enabled = state == WindowState.Idle && selectedCount > 0;
        if (GUILayout.Button($"Update ({selectedCount})", GUILayout.Width(90), GUILayout.Height(24)))
        {
            UpdateSelectedPackages();
        }
        GUI.enabled = true;

        GUILayout.FlexibleSpace();

        showUpdatableOnly = GUILayout.Toggle(showUpdatableOnly, "\u66f4\u65b0\u53ef\u80fd\u306e\u307f", GUILayout.Width(90));
        showUnusedOnly = GUILayout.Toggle(showUnusedOnly, "\u672a\u4f7f\u7528\u306e\u307f", GUILayout.Width(75));

        GUILayout.Label("\u691c\u7d22:", GUILayout.Width(35));
        searchFilter = EditorGUILayout.TextField(searchFilter, GUILayout.Width(150), GUILayout.Height(18));

        EditorGUILayout.EndHorizontal();
    }

    private void DrawProgressBar()
    {
        Rect r = EditorGUILayout.GetControlRect(false, 20);
        EditorGUI.ProgressBar(r, progress, $"{(int)(progress * 100)}% {statusMessage}");
    }

    private void DrawPackageList()
    {
        var filtered = GetFilteredPackages();

        // NOTE: 全選択/全解除
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All", EditorStyles.miniButtonLeft, GUILayout.Width(70)))
        {
            foreach (var p in filtered)
                if (p.hasUpdate) p.selected = true;
        }
        if (GUILayout.Button("Deselect All", EditorStyles.miniButtonRight, GUILayout.Width(80)))
        {
            foreach (var p in filtered)
                p.selected = false;
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        packageScrollPosition = EditorGUILayout.BeginScrollView(packageScrollPosition, GUILayout.ExpandHeight(true));

        foreach (var pkg in filtered)
        {
            DrawPackageEntry(pkg);
        }

        if (filtered.Count == 0)
        {
            EditorGUILayout.HelpBox("\u8868\u793a\u3059\u308b\u30d1\u30c3\u30b1\u30fc\u30b8\u304c\u3042\u308a\u307e\u305b\u3093", MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawPackageEntry(PackageEntry pkg)
    {
        EditorGUILayout.BeginHorizontal("box");

        // NOTE: チェックボックス（更新可能なもののみ）
        if (pkg.hasUpdate)
        {
            pkg.selected = EditorGUILayout.Toggle(pkg.selected, GUILayout.Width(20));
        }
        else
        {
            GUILayout.Space(24);
        }

        // NOTE: パッケージ名
        EditorGUILayout.LabelField(pkg.name, EditorStyles.boldLabel, GUILayout.MinWidth(200));

        // NOTE: 使用状況ラベル
        if (pkg.usageStatus != UsageStatus.Unknown)
        {
            switch (pkg.usageStatus)
            {
                case UsageStatus.DirectlyUsed:
                    GUILayout.Label("\u4f7f\u7528\u4e2d", usedStyle, GUILayout.Width(40));
                    break;
                case UsageStatus.Transitive:
                    GUILayout.Label("\u4f9d\u5b58\u5148", transitiveStyle, GUILayout.Width(40));
                    break;
                case UsageStatus.Unused:
                    GUILayout.Label("\u672a\u4f7f\u7528", unusedStyle, GUILayout.Width(40));
                    break;
            }
        }

        // NOTE: バージョン制約インジケータ
        if (pkg.constraint != null)
        {
            if (pkg.constraint.pinned)
            {
                GUILayout.Label("\ud83d\udd12\u56fa\u5b9a", EditorStyles.miniLabel, GUILayout.Width(45));
            }
            else if (!string.IsNullOrEmpty(pkg.constraint.maxVersion))
            {
                GUILayout.Label($"\u2b06max:{pkg.constraint.maxVersion}", EditorStyles.miniLabel, GUILayout.Width(90));
            }
        }

        // NOTE: タイプラベル
        string typeLabel = pkg.packageType switch
        {
            PackageType.Unity => "",
            PackageType.OpenUpm => "[OpenUPM]",
            PackageType.Git => "[Git]",
            _ => ""
        };
        if (!string.IsNullOrEmpty(typeLabel))
        {
            GUILayout.Label(typeLabel, EditorStyles.miniLabel, GUILayout.Width(60));
        }

        GUILayout.FlexibleSpace();

        // NOTE: バージョン情報
        if (pkg.packageType == PackageType.Git)
        {
            string hash = pkg.currentVersion;
            if (hash.Length > 7) hash = hash.Substring(0, 7);
            EditorGUILayout.LabelField($"{hash} \u26a0 \u624b\u52d5\u66f4\u65b0", gitWarningStyle, GUILayout.Width(160));
        }
        else if (pkg.constraint != null && pkg.constraint.pinned)
        {
            EditorGUILayout.LabelField($"{pkg.currentVersion} (\u56fa\u5b9a)", upToDateStyle, GUILayout.Width(160));
        }
        else if (pkg.hasUpdate)
        {
            EditorGUILayout.LabelField($"{pkg.currentVersion} \u2192 {pkg.latestVersion}", updateAvailableStyle, GUILayout.Width(200));
        }
        else if (pkg.checkedForUpdate)
        {
            EditorGUILayout.LabelField($"{pkg.currentVersion} \u2713 \u6700\u65b0", upToDateStyle, GUILayout.Width(160));
        }
        else
        {
            EditorGUILayout.LabelField(pkg.currentVersion, GUILayout.Width(100));
        }

        // NOTE: 制約メニューボタン
        if (pkg.packageType != PackageType.Git)
        {
            if (GUILayout.Button("\u2699", EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(18)))
            {
                ShowConstraintMenu(pkg);
            }
        }

        // NOTE: Removeボタン（未使用パッケージのみ）
        GUI.enabled = state == WindowState.Idle && pkg.usageStatus == UsageStatus.Unused;
        if (pkg.usageStatus == UsageStatus.Unused)
        {
            if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(55), GUILayout.Height(18)))
            {
                RemovePackage(pkg);
            }
        }
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();
    }

    private void DrawLogSection()
    {
        showLog = EditorGUILayout.Foldout(showLog, $"\u66f4\u65b0\u30ed\u30b0 ({logs.Count})");
        if (showLog && logs.Count > 0)
        {
            logScrollPosition = EditorGUILayout.BeginScrollView(logScrollPosition, GUILayout.MaxHeight(120));
            foreach (var log in logs)
            {
                EditorGUILayout.LabelField(log, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }
    }

    // =====================================================================
    // パッケージ操作
    // =====================================================================

    private void RefreshPackageList()
    {
        state = WindowState.Listing;
        statusMessage = "\u30d1\u30c3\u30b1\u30fc\u30b8\u4e00\u89a7\u3092\u53d6\u5f97\u4e2d...";
        progress = 0f;
        packages.Clear();
        listRequest = Client.List(false); // NOTE: offlineMode=false で全パッケージ取得
    }

    private void HandleListCompleted()
    {
        if (listRequest.Status == StatusCode.Failure)
        {
            state = WindowState.Idle;
            statusMessage = $"\u53d6\u5f97\u5931\u6557: {listRequest.Error?.message ?? "Unknown error"}";
            AddLog($"[ERROR] \u30d1\u30c3\u30b1\u30fc\u30b8\u4e00\u89a7\u53d6\u5f97\u5931\u6557: {listRequest.Error?.message}");
            return;
        }

        packages.Clear();
        foreach (var info in listRequest.Result)
        {
            // NOTE: Builtinモジュールは除外
            if (info.name.StartsWith("com.unity.modules.")) continue;

            var entry = new PackageEntry
            {
                name = info.name,
                currentVersion = info.source == PackageSource.Git ? (info.git?.hash ?? info.version) : info.version,
                packageType = ClassifyPackage(info),
                source = info.source
            };

            // NOTE: バージョン制約を適用
            if (versionConstraints.TryGetValue(entry.name, out var constraint))
            {
                entry.constraint = constraint;
            }

            packages.Add(entry);
        }

        // NOTE: 名前順ソート
        packages.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

        state = WindowState.Idle;
        statusMessage = $"{packages.Count} \u30d1\u30c3\u30b1\u30fc\u30b8\u3092\u53d6\u5f97";
        AddLog($"[INFO] {packages.Count} \u30d1\u30c3\u30b1\u30fc\u30b8\u3092\u53d6\u5f97");
    }

    private PackageType ClassifyPackage(UnityEditor.PackageManager.PackageInfo info)
    {
        if (info.source == PackageSource.Git)
            return PackageType.Git;

        // NOTE: scopedRegistriesのスコープに一致するか
        if (openUpmScopes != null)
        {
            foreach (var scope in openUpmScopes)
            {
                if (info.name.StartsWith(scope))
                    return PackageType.OpenUpm;
            }
        }

        return PackageType.Unity;
    }

    private void CheckAllUpdates()
    {
        searchQueue.Clear();
        completedSearchCount = 0;

        var targets = packages.Where(p => p.packageType != PackageType.Git).ToList();

        // NOTE: pinnedパッケージはスキップ
        targets = targets.Where(p => p.constraint == null || !p.constraint.pinned).ToList();

        totalSearchCount = targets.Count;

        foreach (var pkg in targets)
        {
            pkg.checkedForUpdate = false;
            pkg.hasUpdate = false;
            pkg.latestVersion = null;
            searchQueue.Enqueue(pkg);
        }

        // NOTE: pinnedパッケージは更新なしとしてマーク
        foreach (var pkg in packages.Where(p => p.constraint != null && p.constraint.pinned && p.packageType != PackageType.Git))
        {
            pkg.checkedForUpdate = true;
            pkg.hasUpdate = false;
        }

        // NOTE: Gitパッケージは手動更新マーク
        foreach (var pkg in packages.Where(p => p.packageType == PackageType.Git))
        {
            pkg.checkedForUpdate = true;
        }

        state = WindowState.Checking;
        statusMessage = "\u66f4\u65b0\u3092\u78ba\u8a8d\u4e2d...";
        progress = 0f;

        ProcessNextSearch();
    }

    private void ProcessNextSearch()
    {
        if (searchQueue.Count == 0)
        {
            state = WindowState.Idle;
            int updateCount = packages.Count(p => p.hasUpdate);
            statusMessage = $"\u78ba\u8a8d\u5b8c\u4e86 - {updateCount} \u4ef6\u306e\u66f4\u65b0\u3042\u308a";
            AddLog($"[INFO] \u66f4\u65b0\u78ba\u8a8d\u5b8c\u4e86: {updateCount} \u4ef6\u306e\u66f4\u65b0\u3042\u308a");
            return;
        }

        var pkg = searchQueue.Peek();
        currentSearchRequest = Client.Search(pkg.name);
        progress = totalSearchCount > 0 ? (float)completedSearchCount / totalSearchCount : 0f;
        statusMessage = $"\u78ba\u8a8d\u4e2d ({completedSearchCount}/{totalSearchCount})...";
    }

    private void HandleSearchCompleted()
    {
        if (searchQueue.Count == 0) return;
        var pkg = searchQueue.Dequeue();
        completedSearchCount++;

        if (currentSearchRequest.Status == StatusCode.Failure)
        {
            AddLog($"[WARN] {pkg.name}: \u78ba\u8a8d\u5931\u6557 - {currentSearchRequest.Error?.message}");
            pkg.checkedForUpdate = true;
            return;
        }

        var result = currentSearchRequest.Result;
        if (result != null && result.Length > 0)
        {
            // NOTE: 検索結果の最初のパッケージの最新バージョンを取得
            var latest = result[0];
            pkg.latestVersion = latest.versions.latest;

            // NOTE: maxVersionによるキャップ
            if (pkg.constraint != null && !string.IsNullOrEmpty(pkg.constraint.maxVersion))
            {
                if (!string.IsNullOrEmpty(pkg.latestVersion) && IsNewerVersion(pkg.latestVersion, pkg.constraint.maxVersion))
                {
                    pkg.latestVersion = pkg.constraint.maxVersion;
                }
            }

            if (!string.IsNullOrEmpty(pkg.latestVersion) && pkg.latestVersion != pkg.currentVersion)
            {
                // NOTE: SemVer比較
                if (IsNewerVersion(pkg.latestVersion, pkg.currentVersion))
                {
                    pkg.hasUpdate = true;
                    AddLog($"[UPDATE] {pkg.name}: {pkg.currentVersion} \u2192 {pkg.latestVersion}");
                }
            }
        }

        pkg.checkedForUpdate = true;
    }

    private void UpdateSelectedPackages()
    {
        var toUpdate = packages.Where(p => p.selected && p.hasUpdate && p.packageType != PackageType.Git).ToList();
        if (toUpdate.Count == 0) return;

        state = WindowState.Updating;
        searchQueue.Clear();
        foreach (var pkg in toUpdate)
            searchQueue.Enqueue(pkg);

        totalSearchCount = toUpdate.Count;
        completedSearchCount = 0;
        AddLog($"[INFO] {toUpdate.Count} \u30d1\u30c3\u30b1\u30fc\u30b8\u306e\u66f4\u65b0\u3092\u958b\u59cb");

        ProcessNextUpdate();
    }

    private void ProcessNextUpdate()
    {
        if (searchQueue.Count == 0)
        {
            state = WindowState.Idle;
            statusMessage = "\u66f4\u65b0\u5b8c\u4e86";
            AddLog("[INFO] \u5168\u30d1\u30c3\u30b1\u30fc\u30b8\u306e\u66f4\u65b0\u304c\u5b8c\u4e86");
            // NOTE: 更新後にリストをリフレッシュ
            RefreshPackageList();
            return;
        }

        var pkg = searchQueue.Peek();
        currentAddRequest = Client.Add($"{pkg.name}@{pkg.latestVersion}");
        progress = totalSearchCount > 0 ? (float)completedSearchCount / totalSearchCount : 0f;
        statusMessage = $"\u66f4\u65b0\u4e2d: {pkg.name} ({completedSearchCount + 1}/{totalSearchCount})";
    }

    private void HandleAddCompleted()
    {
        if (searchQueue.Count == 0) return;
        var pkg = searchQueue.Dequeue();
        completedSearchCount++;

        if (currentAddRequest.Status == StatusCode.Failure)
        {
            AddLog($"[ERROR] {pkg.name}: \u66f4\u65b0\u5931\u6557 - {currentAddRequest.Error?.message}");
        }
        else
        {
            AddLog($"[OK] {pkg.name}: {pkg.currentVersion} \u2192 {pkg.latestVersion} \u66f4\u65b0\u6210\u529f");
            pkg.selected = false;
        }

        ProcessNextUpdate();
    }

    // =====================================================================
    // 依存関係解析
    // =====================================================================

    private void AnalyzeDependencies()
    {
        state = WindowState.Analyzing;
        statusMessage = "\u4f9d\u5b58\u95a2\u4fc2\u3092\u89e3\u6790\u4e2d...";
        progress = 0f;

        try
        {
            // NOTE: 1. packages-lock.jsonから依存情報を取得
            var lockInfo = ParsePackagesLock();
            progress = 0.2f;
            Repaint();

            // NOTE: 2. 各パッケージのアセンブリGUIDを収集
            CollectPackageAssemblyGuids(lockInfo);
            progress = 0.5f;
            Repaint();

            // NOTE: 3. プロジェクトの.asmdef参照GUIDを収集
            var projectRefGuids = CollectProjectReferences();
            progress = 0.7f;
            Repaint();

            // NOTE: 4. 使用中パッケージをマーク
            MarkDirectlyUsedPackages(lockInfo, projectRefGuids);

            // NOTE: 5. 依存チェーンで依存先マーク
            MarkTransitivePackages(lockInfo);

            // NOTE: 6. 残りを未使用マーク
            MarkUnusedPackages();

            progress = 1f;
            state = WindowState.Idle;

            int usedCount = packages.Count(p => p.usageStatus == UsageStatus.DirectlyUsed);
            int transitiveCount = packages.Count(p => p.usageStatus == UsageStatus.Transitive);
            int unusedCount = packages.Count(p => p.usageStatus == UsageStatus.Unused);
            statusMessage = $"\u89e3\u6790\u5b8c\u4e86 - \u4f7f\u7528\u4e2d:{usedCount} \u4f9d\u5b58\u5148:{transitiveCount} \u672a\u4f7f\u7528:{unusedCount}";
            AddLog($"[INFO] \u4f9d\u5b58\u95a2\u4fc2\u89e3\u6790\u5b8c\u4e86: \u4f7f\u7528\u4e2d={usedCount}, \u4f9d\u5b58\u5148={transitiveCount}, \u672a\u4f7f\u7528={unusedCount}");
        }
        catch (Exception e)
        {
            state = WindowState.Idle;
            statusMessage = $"\u89e3\u6790\u5931\u6557: {e.Message}";
            AddLog($"[ERROR] \u4f9d\u5b58\u95a2\u4fc2\u89e3\u6790\u5931\u6557: {e.Message}");
            Debug.LogException(e);
        }
    }

    private Dictionary<string, LockPackageInfo> ParsePackagesLock()
    {
        var result = new Dictionary<string, LockPackageInfo>();
        string lockPath = Path.Combine(Application.dataPath, "..", "Packages", "packages-lock.json");
        if (!File.Exists(lockPath)) return result;

        string json = File.ReadAllText(lockPath);

        // NOTE: "dependencies"オブジェクトを探す
        int depsIdx = json.IndexOf("\"dependencies\"", StringComparison.Ordinal);
        if (depsIdx < 0) return result;
        int depsObjStart = json.IndexOf('{', depsIdx);
        if (depsObjStart < 0) return result;
        int depsObjEnd = FindMatchingBrace(json, depsObjStart);
        if (depsObjEnd < 0) return result;

        string depsBlock = json.Substring(depsObjStart + 1, depsObjEnd - depsObjStart - 1);

        // NOTE: 各パッケージエントリを簡易パース
        int pos = 0;
        while (pos < depsBlock.Length)
        {
            int nameStart = depsBlock.IndexOf('"', pos);
            if (nameStart < 0) break;
            int nameEnd = depsBlock.IndexOf('"', nameStart + 1);
            if (nameEnd < 0) break;
            string pkgName = depsBlock.Substring(nameStart + 1, nameEnd - nameStart - 1);

            int objStart = depsBlock.IndexOf('{', nameEnd);
            if (objStart < 0) break;
            int objEnd = FindMatchingBrace(depsBlock, objStart);
            if (objEnd < 0) break;

            string objBlock = depsBlock.Substring(objStart, objEnd - objStart + 1);

            var info = new LockPackageInfo { name = pkgName };

            // NOTE: depthを取得
            var depthMatch = Regex.Match(objBlock, "\"depth\"\\s*:\\s*(\\d+)");
            if (depthMatch.Success) info.depth = int.Parse(depthMatch.Groups[1].Value);

            // NOTE: dependenciesを取得
            int subDepsIdx = objBlock.IndexOf("\"dependencies\"", StringComparison.Ordinal);
            if (subDepsIdx >= 0)
            {
                int subObjStart = objBlock.IndexOf('{', subDepsIdx);
                if (subObjStart >= 0)
                {
                    int subObjEnd = FindMatchingBrace(objBlock, subObjStart);
                    if (subObjEnd >= 0)
                    {
                        string subDepsBlock = objBlock.Substring(subObjStart + 1, subObjEnd - subObjStart - 1);
                        foreach (Match m in Regex.Matches(subDepsBlock, "\"([^\"]+)\"\\s*:"))
                        {
                            info.dependencies.Add(m.Groups[1].Value);
                        }
                    }
                }
            }

            result[pkgName] = info;
            pos = objEnd + 1;
        }

        return result;
    }

    private void CollectPackageAssemblyGuids(Dictionary<string, LockPackageInfo> lockInfo)
    {
        foreach (var pkg in packages)
        {
            pkg.assemblyGuids = new HashSet<string>();
            pkg.dependencyPackages = new HashSet<string>();

            // NOTE: lockInfoから依存パッケージ名を取得
            if (lockInfo.TryGetValue(pkg.name, out var lockPkg))
            {
                pkg.dependencyPackages = lockPkg.dependencies;
            }

            // NOTE: PackageInfo.resolvedPathからasmdefを探す
            string resolvedPath = GetPackageResolvedPath(pkg.name);
            if (string.IsNullOrEmpty(resolvedPath) || !Directory.Exists(resolvedPath)) continue;

            try
            {
                var asmdefFiles = Directory.GetFiles(resolvedPath, "*.asmdef", SearchOption.AllDirectories);
                foreach (var asmdefFile in asmdefFiles)
                {
                    string metaPath = asmdefFile + ".meta";
                    if (!File.Exists(metaPath)) continue;

                    string guid = ExtractGuidFromMeta(metaPath);
                    if (!string.IsNullOrEmpty(guid))
                    {
                        pkg.assemblyGuids.Add(guid);
                    }
                }
            }
            catch (Exception)
            {
                // NOTE: アクセス権限エラーなどは無視
            }
        }
    }

    private string GetPackageResolvedPath(string packageName)
    {
        // NOTE: PackageManagerのAPIでresolvedPathを取得
        var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{packageName}");
        return info?.resolvedPath;
    }

    private HashSet<string> CollectProjectReferences()
    {
        var refGuids = new HashSet<string>();
        string assetsPath = Application.dataPath;

        try
        {
            var asmdefFiles = Directory.GetFiles(assetsPath, "*.asmdef", SearchOption.AllDirectories);
            foreach (var asmdefFile in asmdefFiles)
            {
                string content = File.ReadAllText(asmdefFile);
                // NOTE: GUID:形式の参照を抽出
                foreach (Match m in Regex.Matches(content, "GUID:([a-fA-F0-9]+)"))
                {
                    refGuids.Add(m.Groups[1].Value);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PackageUpdater] Failed to collect project references: {e.Message}");
        }

        return refGuids;
    }

    private void MarkDirectlyUsedPackages(Dictionary<string, LockPackageInfo> lockInfo, HashSet<string> projectRefGuids)
    {
        foreach (var pkg in packages)
        {
            pkg.usageStatus = UsageStatus.Unknown;

            if (pkg.assemblyGuids == null || pkg.assemblyGuids.Count == 0) continue;

            foreach (var guid in pkg.assemblyGuids)
            {
                if (projectRefGuids.Contains(guid))
                {
                    pkg.usageStatus = UsageStatus.DirectlyUsed;
                    break;
                }
            }
        }
    }

    private void MarkTransitivePackages(Dictionary<string, LockPackageInfo> lockInfo)
    {
        // NOTE: 使用中パッケージの依存先を反復的にマーク
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var pkg in packages)
            {
                if (pkg.usageStatus != UsageStatus.DirectlyUsed && pkg.usageStatus != UsageStatus.Transitive)
                    continue;

                if (pkg.dependencyPackages == null) continue;

                foreach (var depName in pkg.dependencyPackages)
                {
                    var depPkg = packages.FirstOrDefault(p => p.name == depName);
                    if (depPkg != null && depPkg.usageStatus == UsageStatus.Unknown)
                    {
                        depPkg.usageStatus = UsageStatus.Transitive;
                        changed = true;
                    }
                }
            }
        }
    }

    private void MarkUnusedPackages()
    {
        foreach (var pkg in packages)
        {
            if (pkg.usageStatus == UsageStatus.Unknown)
            {
                pkg.usageStatus = UsageStatus.Unused;
            }
        }
    }

    private static string ExtractGuidFromMeta(string metaPath)
    {
        try
        {
            string content = File.ReadAllText(metaPath);
            var match = Regex.Match(content, @"guid:\s*([a-fA-F0-9]+)");
            if (match.Success)
                return match.Groups[1].Value;
        }
        catch (Exception)
        {
            // NOTE: 読み取りエラーは無視
        }
        return null;
    }

    // =====================================================================
    // パッケージ削除
    // =====================================================================

    private void RemovePackage(PackageEntry pkg)
    {
        if (!EditorUtility.DisplayDialog(
            "\u30d1\u30c3\u30b1\u30fc\u30b8\u306e\u524a\u9664",
            $"{pkg.name} \u3092\u524a\u9664\u3057\u307e\u3059\u304b\uff1f\n\n\u3053\u306e\u64cd\u4f5c\u306f\u5143\u306b\u623b\u305b\u307e\u305b\u3093\u3002",
            "\u524a\u9664",
            "\u30ad\u30e3\u30f3\u30bb\u30eb"))
        {
            return;
        }

        state = WindowState.Removing;
        statusMessage = $"\u524a\u9664\u4e2d: {pkg.name}...";
        removingPackage = pkg;
        currentRemoveRequest = Client.Remove(pkg.name);
        AddLog($"[INFO] {pkg.name} \u306e\u524a\u9664\u3092\u958b\u59cb");
    }

    private void HandleRemoveCompleted()
    {
        if (removingPackage == null) return;

        if (currentRemoveRequest.Status == StatusCode.Failure)
        {
            AddLog($"[ERROR] {removingPackage.name}: \u524a\u9664\u5931\u6557 - {currentRemoveRequest.Error?.message}");
            statusMessage = $"\u524a\u9664\u5931\u6557: {removingPackage.name}";
        }
        else
        {
            AddLog($"[OK] {removingPackage.name}: \u524a\u9664\u6210\u529f");
            statusMessage = $"\u524a\u9664\u6210\u529f: {removingPackage.name}";
            // NOTE: 制約も削除
            RemovePackageConstraint(removingPackage.name);
        }

        removingPackage = null;
        state = WindowState.Idle;

        // NOTE: リストをリフレッシュ
        RefreshPackageList();
    }

    // =====================================================================
    // バージョン制約
    // =====================================================================

    private void LoadManifestConfig()
    {
        versionConstraints.Clear();
        try
        {
            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath)) return;
            string json = File.ReadAllText(manifestPath);

            int configIdx = json.IndexOf("\"packageUpdaterConfig\"", StringComparison.Ordinal);
            if (configIdx < 0) return;

            int configObjStart = json.IndexOf('{', configIdx);
            if (configObjStart < 0) return;
            int configObjEnd = FindMatchingBrace(json, configObjStart);
            if (configObjEnd < 0) return;

            string configBlock = json.Substring(configObjStart, configObjEnd - configObjStart + 1);

            // NOTE: versionConstraintsセクションをパース
            int vcIdx = configBlock.IndexOf("\"versionConstraints\"", StringComparison.Ordinal);
            if (vcIdx < 0) return;

            int vcObjStart = configBlock.IndexOf('{', vcIdx);
            if (vcObjStart < 0) return;
            int vcObjEnd = FindMatchingBrace(configBlock, vcObjStart);
            if (vcObjEnd < 0) return;

            string vcBlock = configBlock.Substring(vcObjStart + 1, vcObjEnd - vcObjStart - 1);

            // NOTE: 各パッケージ制約をパース
            int pos = 0;
            while (pos < vcBlock.Length)
            {
                int nameStart = vcBlock.IndexOf('"', pos);
                if (nameStart < 0) break;
                int nameEnd = vcBlock.IndexOf('"', nameStart + 1);
                if (nameEnd < 0) break;
                string pkgName = vcBlock.Substring(nameStart + 1, nameEnd - nameStart - 1);

                int objStart = vcBlock.IndexOf('{', nameEnd);
                if (objStart < 0) break;
                int objEnd = FindMatchingBrace(vcBlock, objStart);
                if (objEnd < 0) break;

                string objBlock = vcBlock.Substring(objStart, objEnd - objStart + 1);

                var constraint = new VersionConstraint();

                if (Regex.IsMatch(objBlock, @"""pin""\s*:\s*true\b"))
                {
                    constraint.pinned = true;
                }

                var maxMatch = Regex.Match(objBlock, "\"maxVersion\"\\s*:\\s*\"([^\"]+)\"");
                if (maxMatch.Success)
                {
                    constraint.maxVersion = maxMatch.Groups[1].Value;
                }

                versionConstraints[pkgName] = constraint;

                pos = objEnd + 1;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PackageUpdater] Failed to load manifest config: {e.Message}");
        }
    }

    private void SaveManifestConfig()
    {
        try
        {
            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath)) return;
            string json = File.ReadAllText(manifestPath);

            // NOTE: 既存のpackageUpdaterConfigセクションを除去
            int configIdx = json.IndexOf("\"packageUpdaterConfig\"", StringComparison.Ordinal);
            if (configIdx >= 0)
            {
                int configObjStart = json.IndexOf('{', configIdx);
                if (configObjStart >= 0)
                {
                    int configObjEnd = FindMatchingBrace(json, configObjStart);
                    if (configObjEnd >= 0)
                    {
                        // NOTE: 前のカンマまたは後のカンマを含めて削除
                        int removeStart = configIdx;
                        int removeEnd = configObjEnd + 1;

                        // NOTE: 前方のカンマを探す
                        int beforeComma = removeStart - 1;
                        while (beforeComma >= 0 && char.IsWhiteSpace(json[beforeComma])) beforeComma--;
                        if (beforeComma >= 0 && json[beforeComma] == ',')
                        {
                            removeStart = beforeComma;
                        }
                        else
                        {
                            // NOTE: 後方のカンマを探す
                            int afterComma = removeEnd;
                            while (afterComma < json.Length && char.IsWhiteSpace(json[afterComma])) afterComma++;
                            if (afterComma < json.Length && json[afterComma] == ',')
                            {
                                removeEnd = afterComma + 1;
                            }
                        }

                        // NOTE: 前後の改行も除去
                        while (removeStart > 0 && (json[removeStart - 1] == '\n' || json[removeStart - 1] == '\r'))
                            removeStart--;
                        // NOTE: ただし少なくとも1つの改行は残す
                        if (removeStart > 0 && json[removeStart] != '\n')
                            removeStart++;

                        json = json.Substring(0, removeStart) + json.Substring(removeEnd);
                    }
                }
            }

            // NOTE: 制約がある場合のみセクションを追加
            if (versionConstraints.Count > 0)
            {
                // NOTE: scopedRegistriesの直前に挿入
                int insertIdx = json.IndexOf("\"scopedRegistries\"", StringComparison.Ordinal);
                if (insertIdx < 0)
                    insertIdx = json.IndexOf("\"dependencies\"", StringComparison.Ordinal);

                if (insertIdx >= 0)
                {
                    // NOTE: 挿入するJSON文字列を構築
                    string indent = "  ";
                    string configJson = $"{indent}\"packageUpdaterConfig\": {{\n";
                    configJson += $"{indent}{indent}\"versionConstraints\": {{\n";

                    var entries = versionConstraints.ToList();
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var kvp = entries[i];
                        configJson += $"{indent}{indent}{indent}\"{kvp.Key}\": {{ ";

                        var parts = new List<string>();
                        if (kvp.Value.pinned)
                            parts.Add("\"pin\": true");
                        if (!string.IsNullOrEmpty(kvp.Value.maxVersion))
                            parts.Add($"\"maxVersion\": \"{kvp.Value.maxVersion}\"");

                        configJson += string.Join(", ", parts);
                        configJson += " }";
                        if (i < entries.Count - 1) configJson += ",";
                        configJson += "\n";
                    }

                    configJson += $"{indent}{indent}}}\n";
                    configJson += $"{indent}}},\n{indent}";

                    json = json.Substring(0, insertIdx) + configJson + json.Substring(insertIdx);
                }
            }

            File.WriteAllText(manifestPath, json);
            AddLog("[INFO] manifest.json \u306b\u30d0\u30fc\u30b8\u30e7\u30f3\u5236\u7d04\u3092\u4fdd\u5b58");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PackageUpdater] Failed to save manifest config: {e.Message}");
            AddLog($"[ERROR] manifest.json \u3078\u306e\u4fdd\u5b58\u5931\u6557: {e.Message}");
        }
    }

    private void SetPackageConstraint(string packageName, bool pin, string maxVersion)
    {
        var constraint = new VersionConstraint { pinned = pin, maxVersion = maxVersion };
        versionConstraints[packageName] = constraint;

        // NOTE: PackageEntryにも反映
        var pkg = packages.FirstOrDefault(p => p.name == packageName);
        if (pkg != null) pkg.constraint = constraint;

        SaveManifestConfig();
    }

    private void RemovePackageConstraint(string packageName)
    {
        versionConstraints.Remove(packageName);

        var pkg = packages.FirstOrDefault(p => p.name == packageName);
        if (pkg != null) pkg.constraint = null;

        SaveManifestConfig();
    }

    private void ShowConstraintMenu(PackageEntry pkg)
    {
        var menu = new GenericMenu();

        // NOTE: 固定/固定解除
        if (pkg.constraint != null && pkg.constraint.pinned)
        {
            menu.AddItem(new GUIContent("\u56fa\u5b9a\u89e3\u9664"), false, () => RemovePackageConstraint(pkg.name));
        }
        else
        {
            menu.AddItem(new GUIContent($"\u56fa\u5b9a (Pin) - {pkg.currentVersion}"), false, () => SetPackageConstraint(pkg.name, true, null));
        }

        menu.AddSeparator("");

        // NOTE: 最大バージョン設定
        menu.AddItem(new GUIContent($"\u6700\u5927\u30d0\u30fc\u30b8\u30e7\u30f3\u3092\u73fe\u5728\u306b\u8a2d\u5b9a ({pkg.currentVersion})"), false, () => SetPackageConstraint(pkg.name, false, pkg.currentVersion));

        if (!string.IsNullOrEmpty(pkg.latestVersion) && pkg.latestVersion != pkg.currentVersion)
        {
            menu.AddItem(new GUIContent($"\u6700\u5927\u30d0\u30fc\u30b8\u30e7\u30f3\u3092\u6700\u65b0\u306b\u8a2d\u5b9a ({pkg.latestVersion})"), false, () => SetPackageConstraint(pkg.name, false, pkg.latestVersion));
        }

        menu.AddSeparator("");

        // NOTE: 制約解除
        bool hasConstraint = pkg.constraint != null;
        if (hasConstraint)
        {
            menu.AddItem(new GUIContent("\u5236\u7d04\u3092\u89e3\u9664"), false, () => RemovePackageConstraint(pkg.name));
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("\u5236\u7d04\u3092\u89e3\u9664"));
        }

        menu.ShowAsContext();
    }

    // =====================================================================
    // ユーティリティ
    // =====================================================================

    private static int FindMatchingBrace(string text, int openBracePos)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = openBracePos; i < text.Length; i++)
        {
            char c = text[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    private List<PackageEntry> GetFilteredPackages()
    {
        IEnumerable<PackageEntry> filtered = packages;

        // NOTE: タブフィルタ
        switch (selectedTab)
        {
            case 1: filtered = filtered.Where(p => p.packageType == PackageType.Unity); break;
            case 2: filtered = filtered.Where(p => p.packageType == PackageType.OpenUpm); break;
            case 3: filtered = filtered.Where(p => p.packageType == PackageType.Git); break;
        }

        // NOTE: 更新可能のみ
        if (showUpdatableOnly)
            filtered = filtered.Where(p => p.hasUpdate);

        // NOTE: 未使用のみ
        if (showUnusedOnly)
            filtered = filtered.Where(p => p.usageStatus == UsageStatus.Unused);

        // NOTE: 検索フィルタ
        if (!string.IsNullOrEmpty(searchFilter))
            filtered = filtered.Where(p => p.name.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);

        return filtered.ToList();
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        // NOTE: プレリリース版のサフィックスを考慮したSemVer比較
        var latestParts = SplitPreRelease(latest);
        var currentParts = SplitPreRelease(current);

        if (TryParseSemVer(latestParts.version, out var latestVer) && TryParseSemVer(currentParts.version, out var currentVer))
        {
            // NOTE: メジャー.マイナー.パッチの比較
            if (latestVer != currentVer)
                return latestVer > currentVer;

            // NOTE: バージョンが同じ場合、プレリリースタグの比較
            // 安定版 > プレリリース版
            if (string.IsNullOrEmpty(latestParts.preRelease) && !string.IsNullOrEmpty(currentParts.preRelease))
                return true;
            if (!string.IsNullOrEmpty(latestParts.preRelease) && string.IsNullOrEmpty(currentParts.preRelease))
                return false;

            // NOTE: 両方プレリリース版の場合は文字列比較
            if (!string.IsNullOrEmpty(latestParts.preRelease) && !string.IsNullOrEmpty(currentParts.preRelease))
                return string.Compare(latestParts.preRelease, currentParts.preRelease, StringComparison.Ordinal) > 0;
        }

        // NOTE: パースできない場合は文字列比較
        return string.Compare(latest, current, StringComparison.Ordinal) > 0;
    }

    private static (string version, string preRelease) SplitPreRelease(string versionStr)
    {
        if (string.IsNullOrEmpty(versionStr))
            return (versionStr, null);

        int dashIndex = versionStr.IndexOf('-');
        if (dashIndex >= 0)
        {
            return (versionStr.Substring(0, dashIndex), versionStr.Substring(dashIndex + 1));
        }

        return (versionStr, null);
    }

    private static bool TryParseSemVer(string versionStr, out Version version)
    {
        version = null;
        if (string.IsNullOrEmpty(versionStr)) return false;
        return Version.TryParse(versionStr, out version);
    }

    private void AddLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        logs.Add($"[{timestamp}] {message}");
        // NOTE: ログ上限
        if (logs.Count > 200) logs.RemoveAt(0);
    }

    // =====================================================================
    // データクラス
    // =====================================================================

    private enum PackageType { Unity, OpenUpm, Git }
    private enum UsageStatus { Unknown, DirectlyUsed, Transitive, Unused }

    private class VersionConstraint
    {
        public bool pinned;
        public string maxVersion;
    }

    private class LockPackageInfo
    {
        public string name;
        public int depth;
        public HashSet<string> dependencies = new HashSet<string>();
    }

    private class PackageEntry
    {
        public string name;
        public string currentVersion;
        public string latestVersion;
        public PackageType packageType;
        public PackageSource source;
        public bool selected;
        public bool hasUpdate;
        public bool checkedForUpdate;
        public UsageStatus usageStatus;
        public HashSet<string> assemblyGuids;
        public HashSet<string> dependencyPackages;
        public VersionConstraint constraint;
    }
}
