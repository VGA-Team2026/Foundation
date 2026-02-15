#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;

namespace Melpomene
{
    /// <summary>
    /// SceneView上にMelpomeneチケットを表示するUI Toolkitベースのオーバーレイ
    /// NOTE: 各チケットをボタンとして表示し、クリックでIssueを開く
    /// NOTE: 同じ位置のチケットは円周上に分散表示
    /// NOTE: 10件以上の場合は最新9件+「more」ボタンを表示
    /// </summary>
    [InitializeOnLoad]
    public static class MelpomeneSceneViewOverlay
    {
        private static readonly Dictionary<SceneView, VisualElement> sceneViewContainers = new();
        private static readonly Dictionary<int, MelpomeneTicketElement> ticketElements = new();
        private static readonly Dictionary<string, MelpomeneMoreElement> moreElements = new();
        private static StyleSheet styleSheet;

        // クラスタリング設定
        private const float CLUSTER_DISTANCE = 0.5f;  // 同一位置とみなす距離（ワールド座標）
        private const float SPREAD_RADIUS = 40f;      // 円周の半径（スクリーン座標）
        private const int MAX_VISIBLE_IN_CLUSTER = 9; // クラスタ内の最大表示数

        // スクリーン空間での重なり判定設定
        private const float SCREEN_OVERLAP_MARGIN = 5f;  // 重なり判定のマージン
        private const float SCREEN_SPREAD_DISTANCE = 60f; // 散らす距離
        private const int MAX_SPREAD_ITERATIONS = 10;    // 散らす処理の最大イテレーション
        private const float DEFAULT_ELEMENT_SIZE = 30f;  // デフォルトの要素サイズ

        /// <summary>
        /// スクリーン空間でのBoundingBox
        /// </summary>
        private struct ScreenBoundingBox
        {
            public float minX;
            public float minY;
            public float maxX;
            public float maxY;
            public VisualElement element;
            public Vector2 screenCenter;

            public bool Overlaps(ScreenBoundingBox other, float margin)
            {
                return !(maxX + margin < other.minX ||
                         minX - margin > other.maxX ||
                         maxY + margin < other.minY ||
                         minY - margin > other.maxY);
            }

            public void ApplyOffset(Vector2 offset)
            {
                minX += offset.x;
                maxX += offset.x;
                minY += offset.y;
                maxY += offset.y;
                screenCenter += offset;
            }
        }

        static MelpomeneSceneViewOverlay()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            // 定期的にチケット位置を更新
            foreach (var kvp in sceneViewContainers)
            {
                if (kvp.Key != null)
                {
                    UpdateTicketPositions(kvp.Key, kvp.Value);
                }
            }
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!MelpomeneManager.Instance.IsInitialized)
                return;

            // チケット表示が無効の場合は処理しない
            if (MelpomeneManager.Instance.Config == null || !MelpomeneManager.Instance.Config.enableTicketDisplay)
            {
                // 既存の要素を非表示にする
                if (sceneViewContainers.TryGetValue(sceneView, out var existingContainer))
                {
                    existingContainer.style.display = DisplayStyle.None;
                }
                return;
            }

            // SceneViewごとにコンテナを管理
            if (!sceneViewContainers.TryGetValue(sceneView, out var container))
            {
                container = CreateContainer(sceneView);
                sceneViewContainers[sceneView] = container;
            }

            // コンテナを表示状態に
            container.style.display = DisplayStyle.Flex;

            // チケットを更新
            UpdateTickets(sceneView, container);
        }

        /// <summary>
        /// コンテナを作成
        /// </summary>
        private static VisualElement CreateContainer(SceneView sceneView)
        {
            var container = new VisualElement
            {
                name = "melpomene-ticket-container",
                pickingMode = PickingMode.Ignore
            };

            container.style.position = Position.Absolute;
            container.style.left = 0;
            container.style.top = 0;
            container.style.right = 0;
            container.style.bottom = 0;

            // スタイルシートを読み込み
            if (styleSheet == null)
            {
                styleSheet = LoadStyleSheet();
            }
            if (styleSheet != null)
            {
                container.styleSheets.Add(styleSheet);
            }

            sceneView.rootVisualElement.Add(container);
            return container;
        }

        /// <summary>
        /// スタイルシートを読み込み
        /// </summary>
        private static StyleSheet LoadStyleSheet()
        {
            var guids = AssetDatabase.FindAssets("MelpomeneSceneView t:StyleSheet");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            }
            return null;
        }

        /// <summary>
        /// チケットを更新
        /// NOTE: クラスタリングを行い、同じ位置のチケットは分散表示
        /// </summary>
        private static void UpdateTickets(SceneView sceneView, VisualElement container)
        {
            var tickets = MelpomeneManager.Instance.GetTicketsForCurrentScene();

            // クラスタリング処理
            var clusters = ClusterTickets(tickets);

            // 既存の要素をマーク
            var existingNumbers = new HashSet<int>(ticketElements.Keys);
            var existingMoreKeys = new HashSet<string>(moreElements.Keys);

            foreach (var cluster in clusters)
            {
                var clusterKey = GetClusterKey(cluster.centerPosition);
                var clusterTickets = cluster.tickets;
                var totalCount = clusterTickets.Count;

                // 表示するチケット（最新順でソートして最大数まで）
                var sortedTickets = clusterTickets
                    .OrderByDescending(t => t.timestamp)
                    .ToList();

                var visibleTickets = sortedTickets.Take(MAX_VISIBLE_IN_CLUSTER).ToList();
                var hiddenCount = totalCount - visibleTickets.Count;

                // 各チケット要素を作成/更新
                for (int i = 0; i < visibleTickets.Count; i++)
                {
                    var ticket = visibleTickets[i];
                    var offset = CalculateSpreadOffset(i, visibleTickets.Count + (hiddenCount > 0 ? 1 : 0));

                    if (!ticketElements.TryGetValue(ticket.issueNumber, out var element))
                    {
                        element = new MelpomeneTicketElement(ticket);
                        container.Add(element);
                        ticketElements[ticket.issueNumber] = element;
                    }
                    else
                    {
                        element.UpdateTicket(ticket);
                        existingNumbers.Remove(ticket.issueNumber);
                    }

                    // オフセットを設定（複数チケットの場合のみ）
                    element.SetSpreadOffset(totalCount > 1 ? offset : Vector2.zero);
                }

                // 10件以上の場合は「more」ボタンを表示
                if (hiddenCount > 0)
                {
                    var hiddenTickets = sortedTickets.Skip(MAX_VISIBLE_IN_CLUSTER).ToList();
                    var moreOffset = CalculateSpreadOffset(visibleTickets.Count, visibleTickets.Count + 1);

                    if (!moreElements.TryGetValue(clusterKey, out var moreElement))
                    {
                        moreElement = new MelpomeneMoreElement(cluster.centerPosition, hiddenTickets, hiddenCount);
                        container.Add(moreElement);
                        moreElements[clusterKey] = moreElement;
                    }
                    else
                    {
                        moreElement.UpdateHiddenTickets(hiddenTickets, hiddenCount);
                        existingMoreKeys.Remove(clusterKey);
                    }

                    moreElement.SetSpreadOffset(moreOffset);
                }
            }

            // 不要な要素を削除
            foreach (var number in existingNumbers)
            {
                if (ticketElements.TryGetValue(number, out var element))
                {
                    element.RemoveFromHierarchy();
                    ticketElements.Remove(number);
                }
            }

            // 不要なmore要素を削除
            foreach (var key in existingMoreKeys)
            {
                if (moreElements.TryGetValue(key, out var element))
                {
                    element.RemoveFromHierarchy();
                    moreElements.Remove(key);
                }
            }
        }

        /// <summary>
        /// チケットを位置でクラスタリング
        /// </summary>
        private static List<TicketCluster> ClusterTickets(List<MelpomeneTicket> tickets)
        {
            var clusters = new List<TicketCluster>();

            foreach (var ticket in tickets)
            {
                var pos = GetTicketWorldPosition(ticket);
                var foundCluster = false;

                foreach (var cluster in clusters)
                {
                    if (Vector3.Distance(cluster.centerPosition, pos) < CLUSTER_DISTANCE)
                    {
                        cluster.tickets.Add(ticket);
                        foundCluster = true;
                        break;
                    }
                }

                if (!foundCluster)
                {
                    clusters.Add(new TicketCluster
                    {
                        centerPosition = pos,
                        tickets = new List<MelpomeneTicket> { ticket }
                    });
                }
            }

            return clusters;
        }

        /// <summary>
        /// チケットのワールド座標を取得
        /// </summary>
        private static Vector3 GetTicketWorldPosition(MelpomeneTicket ticket)
        {
            if (!string.IsNullOrEmpty(ticket.targetObjectPath))
            {
                var obj = GameObject.Find(ticket.targetObjectPath);
                if (obj != null)
                {
                    return obj.transform.position;
                }
            }
            return ticket.worldPosition;
        }

        /// <summary>
        /// クラスタキーを生成（位置を丸めて文字列化）
        /// </summary>
        private static string GetClusterKey(Vector3 pos)
        {
            return $"{Mathf.Round(pos.x)}_{Mathf.Round(pos.y)}_{Mathf.Round(pos.z)}";
        }

        /// <summary>
        /// 円周上のオフセットを計算
        /// </summary>
        private static Vector2 CalculateSpreadOffset(int index, int totalCount)
        {
            if (totalCount <= 1) return Vector2.zero;

            float angle = (2f * Mathf.PI * index) / totalCount - Mathf.PI / 2f;
            return new Vector2(
                Mathf.Cos(angle) * SPREAD_RADIUS,
                Mathf.Sin(angle) * SPREAD_RADIUS
            );
        }

        /// <summary>
        /// チケットクラスタ情報
        /// </summary>
        private class TicketCluster
        {
            public Vector3 centerPosition;
            public List<MelpomeneTicket> tickets;
        }

        /// <summary>
        /// チケット位置を更新
        /// NOTE: スプレッドオフセットを適用し、スクリーン空間で重なりを散らす
        /// </summary>
        private static void UpdateTicketPositions(SceneView sceneView, VisualElement container)
        {
            if (sceneView.camera == null)
                return;

            var boundingBoxes = new List<ScreenBoundingBox>();

            // チケット要素のBoundingBoxを収集
            foreach (var kvp in ticketElements)
            {
                var element = kvp.Value;
                var bbox = CalculateScreenBoundingBox(sceneView, element, element.GetWorldPosition(), element.GetSpreadOffset());
                if (bbox.HasValue)
                {
                    boundingBoxes.Add(bbox.Value);
                }
            }

            // more要素のBoundingBoxを収集
            foreach (var kvp in moreElements)
            {
                var element = kvp.Value;
                var bbox = CalculateScreenBoundingBox(sceneView, element, element.GetWorldPosition(), element.GetSpreadOffset());
                if (bbox.HasValue)
                {
                    boundingBoxes.Add(bbox.Value);
                }
            }

            // スクリーン空間で重なりを散らす
            SpreadOverlappingElements(boundingBoxes);

            // 散らした後の位置を適用
            foreach (var bbox in boundingBoxes)
            {
                ApplyScreenPosition(bbox.element, bbox.screenCenter);
            }
        }

        /// <summary>
        /// スクリーン空間のBoundingBoxを計算
        /// NOTE: テキストラベルも含めた全体のサイズで計算
        /// </summary>
        private static ScreenBoundingBox? CalculateScreenBoundingBox(SceneView sceneView, VisualElement element, Vector3 worldPos, Vector2 spreadOffset)
        {
            // ワールド座標をスクリーン座標に変換
            var viewportPos = sceneView.camera.WorldToViewportPoint(worldPos);

            // カメラの後ろにある場合は非表示
            if (viewportPos.z < 0)
            {
                element.style.display = DisplayStyle.None;
                return null;
            }

            // ビューポート外の場合は非表示
            if (viewportPos.x < 0 || viewportPos.x > 1 || viewportPos.y < 0 || viewportPos.y > 1)
            {
                element.style.display = DisplayStyle.None;
                return null;
            }

            element.style.display = DisplayStyle.Flex;

            // スクリーン座標に変換（Y軸反転）
            var screenPos = new Vector2(
                viewportPos.x * sceneView.position.width,
                (1 - viewportPos.y) * sceneView.position.height
            );

            // オフセットを適用
            screenPos += spreadOffset;

            // テキストラベルを含めた全体のサイズを取得
            Vector2 totalSize;
            if (element is MelpomeneTicketElement ticketElement)
            {
                totalSize = ticketElement.GetTotalSize();
            }
            else if (element is MelpomeneMoreElement moreElement)
            {
                totalSize = moreElement.GetTotalSize();
            }
            else
            {
                // フォールバック
                totalSize = new Vector2(
                    element.resolvedStyle.width > 0 ? element.resolvedStyle.width : DEFAULT_ELEMENT_SIZE,
                    element.resolvedStyle.height > 0 ? element.resolvedStyle.height : DEFAULT_ELEMENT_SIZE
                );
            }

            return new ScreenBoundingBox
            {
                minX = screenPos.x - totalSize.x / 2,
                maxX = screenPos.x + totalSize.x / 2,
                minY = screenPos.y - totalSize.y / 2,
                maxY = screenPos.y + totalSize.y / 2,
                element = element,
                screenCenter = screenPos
            };
        }

        /// <summary>
        /// 重なっている要素を散らす
        /// </summary>
        private static void SpreadOverlappingElements(List<ScreenBoundingBox> boxes)
        {
            if (boxes.Count < 2) return;

            for (int iteration = 0; iteration < MAX_SPREAD_ITERATIONS; iteration++)
            {
                bool hasOverlap = false;

                for (int i = 0; i < boxes.Count; i++)
                {
                    for (int j = i + 1; j < boxes.Count; j++)
                    {
                        var boxA = boxes[i];
                        var boxB = boxes[j];

                        if (boxA.Overlaps(boxB, SCREEN_OVERLAP_MARGIN))
                        {
                            hasOverlap = true;

                            // 重心から離れる方向にオフセットを計算
                            Vector2 direction = boxB.screenCenter - boxA.screenCenter;
                            if (direction.sqrMagnitude < 0.01f)
                            {
                                // 同じ位置の場合はランダム方向
                                float angle = (i + j) * 0.5f;
                                direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                            }
                            direction = direction.normalized;

                            // 両方を反対方向に移動
                            float spreadAmount = SCREEN_SPREAD_DISTANCE / MAX_SPREAD_ITERATIONS;
                            var offsetA = -direction * spreadAmount * 0.5f;
                            var offsetB = direction * spreadAmount * 0.5f;

                            boxA.ApplyOffset(offsetA);
                            boxB.ApplyOffset(offsetB);

                            boxes[i] = boxA;
                            boxes[j] = boxB;
                        }
                    }
                }

                if (!hasOverlap) break;
            }
        }

        /// <summary>
        /// スクリーン位置を適用
        /// </summary>
        private static void ApplyScreenPosition(VisualElement element, Vector2 screenCenter)
        {
            float width = element.resolvedStyle.width > 0 ? element.resolvedStyle.width : DEFAULT_ELEMENT_SIZE;
            float height = element.resolvedStyle.height > 0 ? element.resolvedStyle.height : DEFAULT_ELEMENT_SIZE;

            element.style.left = screenCenter.x - width / 2;
            element.style.top = screenCenter.y - height / 2;
        }

        /// <summary>
        /// キャッシュをクリア（シーン変更時などに呼び出し）
        /// </summary>
        public static void ClearCache()
        {
            foreach (var element in ticketElements.Values)
            {
                element.RemoveFromHierarchy();
            }
            ticketElements.Clear();

            foreach (var element in moreElements.Values)
            {
                element.RemoveFromHierarchy();
            }
            moreElements.Clear();
        }
    }

    /// <summary>
    /// 個別のチケット表示要素
    /// NOTE: SpreadOffsetで円周上に分散表示可能
    /// </summary>
    public class MelpomeneTicketElement : VisualElement
    {
        private MelpomeneTicket ticket;
        private Vector2 spreadOffset;
        private readonly Button button;
        private readonly VisualElement categoryIndicator;
        private readonly Label numberLabel;
        private readonly Label titleLabel;

        private static readonly Color BugColor = new Color(1f, 0.3f, 0.3f, 0.9f);
        private static readonly Color FeatureColor = new Color(0.3f, 0.7f, 1f, 0.9f);
        private static readonly Color ImprovementColor = new Color(0.3f, 1f, 0.5f, 0.9f);
        private static readonly Color QuestionColor = new Color(1f, 0.8f, 0.3f, 0.9f);

        private static readonly Color CriticalColor = new Color(0.9f, 0.1f, 0.1f, 0.95f);
        private static readonly Color HighColor = new Color(1f, 0.5f, 0f, 0.95f);
        private static readonly Color MediumColor = new Color(0.3f, 0.7f, 0.3f, 0.95f);
        private static readonly Color LowColor = new Color(0.4f, 0.4f, 0.5f, 0.95f);

        public MelpomeneTicketElement(MelpomeneTicket ticket)
        {
            this.ticket = ticket;

            // 基本設定
            AddToClassList("melpomene-ticket");
            style.position = Position.Absolute;
            style.flexDirection = FlexDirection.Row;
            style.alignItems = Align.Center;

            // ボタン作成
            button = new Button(() => OnClick())
            {
                name = "ticket-button"
            };
            button.AddToClassList("melpomene-ticket-button");
            Add(button);

            // カテゴリインジケーター（右上の小さな丸）
            categoryIndicator = new VisualElement
            {
                name = "category-indicator"
            };
            categoryIndicator.AddToClassList("melpomene-category-indicator");
            button.Add(categoryIndicator);

            // チケット番号ラベル
            numberLabel = new Label
            {
                name = "ticket-number"
            };
            numberLabel.AddToClassList("melpomene-ticket-number");
            button.Add(numberLabel);

            // タイトルラベル（ボタンの横）
            titleLabel = new Label
            {
                name = "ticket-title"
            };
            titleLabel.AddToClassList("melpomene-ticket-title");
            Add(titleLabel);

            // ツールチップ
            button.tooltip = "";

            // 右クリックメニュー登録
            button.AddManipulator(new ContextualMenuManipulator(OnRightClick));

            // 初期更新
            UpdateVisuals();
        }

        public void UpdateTicket(MelpomeneTicket newTicket)
        {
            ticket = newTicket;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            // 番号
            numberLabel.text = $"#{ticket.issueNumber}";

            // タイトル
            titleLabel.text = ticket.title ?? "";

            // 優先度色（ボタンの背景色）
            var priorityColor = GetPriorityColor(ticket.priority);
            button.style.backgroundColor = priorityColor;

            // カテゴリ色（右上インジケーター）
            var categoryColor = GetCategoryColor(ticket.category);
            categoryIndicator.style.backgroundColor = categoryColor;

            // ツールチップ
            button.tooltip = $"#{ticket.issueNumber}: {ticket.title}\n{ticket.category} - {ticket.priority}";
        }

        /// <summary>
        /// ワールド座標を取得
        /// NOTE: Hierarchyパスからオブジェクトを復元できれば現在位置を使用
        /// </summary>
        public Vector3 GetWorldPosition()
        {
            if (!string.IsNullOrEmpty(ticket.targetObjectPath))
            {
                var obj = GameObject.Find(ticket.targetObjectPath);
                if (obj != null)
                {
                    return obj.transform.position;
                }
            }
            return ticket.worldPosition;
        }

        /// <summary>
        /// スプレッドオフセットを設定
        /// NOTE: 同じ位置のチケットを円周上に分散するため
        /// </summary>
        public void SetSpreadOffset(Vector2 offset)
        {
            spreadOffset = offset;
        }

        /// <summary>
        /// スプレッドオフセットを取得
        /// </summary>
        public Vector2 GetSpreadOffset()
        {
            return spreadOffset;
        }

        /// <summary>
        /// テキストラベルを含めた全体のサイズを取得
        /// </summary>
        public Vector2 GetTotalSize()
        {
            // ボタンのサイズ
            float buttonWidth = button.resolvedStyle.width > 0 ? button.resolvedStyle.width : 30f;
            float buttonHeight = button.resolvedStyle.height > 0 ? button.resolvedStyle.height : 20f;

            // タイトルラベルのサイズ
            float titleWidth = titleLabel.resolvedStyle.width > 0 ? titleLabel.resolvedStyle.width : 0f;
            float titleHeight = titleLabel.resolvedStyle.height > 0 ? titleLabel.resolvedStyle.height : 0f;

            // 全体のサイズ（横並びなので幅は合計、高さは最大値）
            float totalWidth = buttonWidth + titleWidth + 4f; // 4pxはマージン
            float totalHeight = Mathf.Max(buttonHeight, titleHeight);

            return new Vector2(totalWidth, totalHeight);
        }

        private void OnClick()
        {
            // チケット詳細ウィンドウを開く（TicketListと同じ挙動）
            MelpomeneTicketDetailWindow.ShowWindow(ticket);
        }

        private void OnRightClick(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction($"#{ticket.issueNumber}: {ticket.title}", null, DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Open in Browser", _ =>
            {
                if (!string.IsNullOrEmpty(ticket.issueUrl))
                {
                    Application.OpenURL(ticket.issueUrl);
                }
            });
            evt.menu.AppendAction("Copy URL", _ =>
            {
                GUIUtility.systemCopyBuffer = ticket.issueUrl;
            });
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("View Details", _ =>
            {
                MelpomeneTicketDetailWindow.ShowWindow(ticket);
            });

            // 自分が作成したチケットのみクローズ可能
            if (ticket.state == "open" && IsOwnTicket(ticket))
            {
                evt.menu.AppendAction("Close Issue", _ =>
                {
                    if (EditorUtility.DisplayDialog(
                        "チケットをクローズ",
                        $"チケット #{ticket.issueNumber} をクローズしますか？\n\n「{ticket.title}」",
                        "クローズする",
                        "キャンセル"))
                    {
                        MelpomeneManager.Instance.CloseTicketAsync(ticket.issueNumber).Forget();
                    }
                });
            }
        }

        /// <summary>
        /// 自分が作成したチケットかどうかを判定
        /// </summary>
        private bool IsOwnTicket(MelpomeneTicket ticket)
        {
            if (ticket == null) return false;
            var config = MelpomeneManager.Instance.Config;
            return config != null &&
                   !string.IsNullOrEmpty(config.defaultUserName) &&
                   ticket.userName == config.defaultUserName;
        }

        private Color GetCategoryColor(MelpomeneCategory category)
        {
            return category switch
            {
                MelpomeneCategory.Bug => BugColor,
                MelpomeneCategory.Feature => FeatureColor,
                MelpomeneCategory.Improvement => ImprovementColor,
                MelpomeneCategory.Question => QuestionColor,
                _ => Color.gray
            };
        }

        private Color GetPriorityColor(MelpomenePriority priority)
        {
            return priority switch
            {
                MelpomenePriority.Critical => CriticalColor,
                MelpomenePriority.High => HighColor,
                MelpomenePriority.Medium => MediumColor,
                MelpomenePriority.Low => LowColor,
                _ => Color.gray
            };
        }
    }

    /// <summary>
    /// 「more」ボタン要素
    /// NOTE: 10件以上のチケットがある場合に表示
    /// NOTE: クリックでチケットリストウィンドウを開く
    /// </summary>
    public class MelpomeneMoreElement : VisualElement
    {
        private Vector3 worldPosition;
        private Vector2 spreadOffset;
        private List<MelpomeneTicket> hiddenTickets;
        private int hiddenCount;
        private readonly Button button;

        public MelpomeneMoreElement(Vector3 position, List<MelpomeneTicket> tickets, int count)
        {
            worldPosition = position;
            hiddenTickets = tickets;
            hiddenCount = count;

            // 基本設定
            AddToClassList("melpomene-more");
            style.position = Position.Absolute;

            // ボタン作成
            button = new Button(() => OnClick())
            {
                name = "more-button",
                text = $"+{count} more"
            };
            button.AddToClassList("melpomene-more-button");
            button.style.backgroundColor = new Color(0.2f, 0.2f, 0.3f, 0.9f);
            button.style.color = Color.white;
            button.style.borderTopLeftRadius = 10;
            button.style.borderTopRightRadius = 10;
            button.style.borderBottomLeftRadius = 10;
            button.style.borderBottomRightRadius = 10;
            button.style.paddingLeft = 8;
            button.style.paddingRight = 8;
            button.style.paddingTop = 4;
            button.style.paddingBottom = 4;
            button.style.fontSize = 11;
            Add(button);

            // ツールチップ
            button.tooltip = $"Click to view {count} more tickets at this location";

            // 右クリックメニュー
            button.AddManipulator(new ContextualMenuManipulator(OnRightClick));
        }

        public void UpdateHiddenTickets(List<MelpomeneTicket> tickets, int count)
        {
            hiddenTickets = tickets;
            hiddenCount = count;
            button.text = $"+{count} more";
            button.tooltip = $"Click to view {count} more tickets at this location";
        }

        public Vector3 GetWorldPosition()
        {
            return worldPosition;
        }

        public void SetSpreadOffset(Vector2 offset)
        {
            spreadOffset = offset;
        }

        public Vector2 GetSpreadOffset()
        {
            return spreadOffset;
        }

        /// <summary>
        /// 全体のサイズを取得
        /// </summary>
        public Vector2 GetTotalSize()
        {
            float width = button.resolvedStyle.width > 0 ? button.resolvedStyle.width : 60f;
            float height = button.resolvedStyle.height > 0 ? button.resolvedStyle.height : 20f;
            return new Vector2(width, height);
        }

        private void OnClick()
        {
            // チケットリストをポップアップで表示
            ShowHiddenTicketsPopup();
        }

        private void OnRightClick(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction($"{hiddenCount} hidden tickets", null, DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Show all tickets", _ => ShowHiddenTicketsPopup());
        }

        /// <summary>
        /// 非表示チケットのリストをポップアップで表示
        /// </summary>
        private void ShowHiddenTicketsPopup()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent($"--- {hiddenCount} more tickets ---"), false, null);
            menu.AddSeparator("");

            foreach (var ticket in hiddenTickets)
            {
                var t = ticket; // クロージャ用
                menu.AddItem(new GUIContent($"#{t.issueNumber}: {t.title}"), false, () =>
                {
                    MelpomeneTicketDetailWindow.ShowWindow(t);
                });
            }

            menu.ShowAsContext();
        }
    }
}
#endif
