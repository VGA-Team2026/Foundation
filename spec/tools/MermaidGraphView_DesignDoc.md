# MermaidGraphView 実装設計書

> Unity Editor 拡張 — Mermaid テキストを GraphView でグラフィカル表示する汎用ビューア

**バージョン:** 0.1.0-draft  
**対象 Unity:** 2021.3 LTS 以降（GraphView API: `UnityEditor.Experimental.GraphView`）  
**対象ダイアグラム:** Flowchart / StateDiagram / ClassDiagram / SequenceDiagram  
**用途:** コードアーキテクチャのドキュメント表示、汎用 Mermaid ビューア（独立 EditorWindow）

---

## 1. 概要

### 1.1 目的

Mermaid 記法のテキストを Unity Editor 内でグラフィカルに表示する独立 EditorWindow を提供する。
`.mmd` / `.md` ファイルからの読み込み、テキスト直接入力の双方に対応し、
Unity の `GraphView` API を使ったネイティブなノード＆エッジ描画で、パン・ズーム・選択などの
Editor 標準操作を自然にサポートする。

### 1.2 スコープ

| IN                                         | OUT                                              |
| ------------------------------------------ | ------------------------------------------------ |
| Flowchart（flowchart / graph）の表示        | Mermaid テキストの編集・生成（出力は読み取り専用）|
| StateDiagram-v2 の表示                      | Gantt / Pie / ER / Git Graph 等の対応            |
| ClassDiagram の表示                         | ランタイム実行（Editor 専用）                    |
| SequenceDiagram の表示                      | ノードのドラッグによるレイアウト手動変更          |
| `.mmd` / `.md`（コードブロック）からの読み込み | Mermaid → C# コード生成                        |
| テキスト入力エリアからのライブ解析          |                                                  |
| Sugiyama ベースの自動レイアウト              |                                                  |

### 1.3 全体アーキテクチャ

```
┌─────────────────────────────────────────────────────┐
│                  MermaidEditorWindow                 │
│  ┌──────────────┐  ┌────────────────────────────┐   │
│  │ InputPanel   │  │  MermaidGraphView           │   │
│  │ (TextField)  │  │  (GraphView subclass)       │   │
│  │              │→ │  ┌─────┐  ┌─────┐          │   │
│  │ FileDropZone │  │  │Node │──│Node │          │   │
│  │              │  │  └─────┘  └─────┘          │   │
│  │ DiagramType  │  │       ↕ Edge               │   │
│  │ Indicator    │  │  ┌─────┐                   │   │
│  └──────────────┘  │  │Node │                   │   │
│                    │  └─────┘                   │   │
│                    └────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
        ↑ テキスト
  ┌─────┴──────────────────────────────────┐
  │            Parser Pipeline             │
  │  ┌──────────┐  ┌───────────┐  ┌─────┐ │
  │  │Tokenizer │→ │ASTBuilder │→ │ AST │ │
  │  └──────────┘  └───────────┘  └─────┘ │
  └────────────────────────────────────────┘
        ↓ AST
  ┌─────────────────────────────────────────┐
  │          Layout Engine                  │
  │  SugiyamaLayoutEngine                   │
  │  (CycleRemoval → LayerAssign →          │
  │   CrossingMinimize → CoordAssign)       │
  │                                         │
  │  SequenceLayoutEngine（縦型専用）        │
  └─────────────────────────────────────────┘
        ↓ 位置付き Graph Model
  ┌─────────────────────────────────────────┐
  │       GraphView Renderer                │
  │  MermaidNode / MermaidEdge /             │
  │  MermaidGroup / SequenceLane             │
  └─────────────────────────────────────────┘
```

---

## 2. ディレクトリ構成

```
Packages/com.ars.mermaid-graphview/
├── package.json
├── Editor/
│   ├── MermaidGraphView.asmdef          # Editor-only Assembly Definition
│   ├── Window/
│   │   ├── MermaidEditorWindow.cs       # EditorWindow 本体
│   │   ├── MermaidGraphViewPanel.cs     # GraphView サブクラス
│   │   └── InputPanel.cs               # 左ペイン：テキスト入力 & ファイル D&D
│   ├── Parser/
│   │   ├── MermaidTokenizer.cs          # 字句解析（共通トークナイザ）
│   │   ├── MermaidToken.cs              # トークン定義
│   │   ├── Ast/
│   │   │   ├── MermaidDocument.cs       # AST ルート
│   │   │   ├── FlowchartAst.cs          # Flowchart 用 AST ノード群
│   │   │   ├── StateDiagramAst.cs       # StateDiagram 用 AST ノード群
│   │   │   ├── ClassDiagramAst.cs       # ClassDiagram 用 AST ノード群
│   │   │   └── SequenceDiagramAst.cs    # SequenceDiagram 用 AST ノード群
│   │   ├── FlowchartParser.cs           # flowchart 構文パーサ
│   │   ├── StateDiagramParser.cs        # stateDiagram 構文パーサ
│   │   ├── ClassDiagramParser.cs        # classDiagram 構文パーサ
│   │   ├── SequenceDiagramParser.cs     # sequenceDiagram 構文パーサ
│   │   └── ParserFactory.cs             # ダイアグラムタイプ判別 & パーサ選択
│   ├── Layout/
│   │   ├── ILayoutEngine.cs             # レイアウトエンジン共通インタフェース
│   │   ├── LayoutGraph.cs              # レイアウト計算用の中間グラフ表現
│   │   ├── SugiyamaLayoutEngine.cs      # 階層型レイアウト（Flowchart/State/Class）
│   │   ├── SequenceLayoutEngine.cs      # シーケンス図専用レイアウト
│   │   └── LayoutConfig.cs             # レイアウトパラメータ
│   ├── Renderer/
│   │   ├── MermaidNode.cs               # GraphView Node サブクラス
│   │   ├── MermaidEdge.cs               # GraphView Edge サブクラス
│   │   ├── MermaidGroup.cs              # subgraph / composite state 用 Group
│   │   ├── SequenceLaneElement.cs       # シーケンス図のライフライン描画
│   │   ├── NodeStyleResolver.cs         # ノード形状・色の解決
│   │   └── EdgeStyleResolver.cs         # エッジスタイル（実線/点線/太線）の解決
│   ├── Styles/
│   │   ├── MermaidGraphView.uss         # UI Toolkit スタイルシート
│   │   └── MermaidNodeStyles.uss        # ノード形状別スタイル
│   └── Utility/
│       ├── MermaidFileImporter.cs        # .mmd ファイルの ScriptedImporter
│       └── MermaidAsset.cs              # .mmd ファイルを表す ScriptableObject
└── Tests/
    └── Editor/
        ├── TokenizerTests.cs
        ├── FlowchartParserTests.cs
        ├── StateDiagramParserTests.cs
        ├── ClassDiagramParserTests.cs
        ├── SequenceDiagramParserTests.cs
        └── SugiyamaLayoutTests.cs
```

---

## 3. パーサ設計

### 3.1 ダイアグラムタイプ判定

最初の非空・非コメント行から判定する。

| 先頭キーワード                        | DiagramType          |
| ------------------------------------- | -------------------- |
| `flowchart` / `graph`                 | `Flowchart`          |
| `stateDiagram` / `stateDiagram-v2`    | `StateDiagram`       |
| `classDiagram`                        | `ClassDiagram`       |
| `sequenceDiagram`                     | `SequenceDiagram`    |

```csharp
// ParserFactory.cs
public static IMermaidParser Create(string source)
{
    var firstLine = GetFirstMeaningfulLine(source);
    return firstLine switch
    {
        var l when l.StartsWith("flowchart") || l.StartsWith("graph")
            => new FlowchartParser(),
        var l when l.StartsWith("stateDiagram")
            => new StateDiagramParser(),
        var l when l.StartsWith("classDiagram")
            => new ClassDiagramParser(),
        var l when l.StartsWith("sequenceDiagram")
            => new SequenceDiagramParser(),
        _ => throw new MermaidParseException($"Unknown diagram type: {firstLine}")
    };
}
```

### 3.2 共通トークナイザ

行指向の字句解析を行い、各パーサに `List<MermaidToken>` を渡す。

```csharp
public enum TokenKind
{
    // 共通
    Keyword,        // flowchart, stateDiagram, classDiagram, sequenceDiagram, subgraph, end, state, class, participant, ...
    Identifier,     // ノードID / 状態名 / クラス名
    StringLiteral,  // "..." / 「...」 内のテキスト
    Arrow,          // -->, --->, -.->,-## ->> など
    Colon,          // :
    OpenBrace,      // {
    CloseBrace,     // }
    OpenBracket,    // [ ( (( { {{ [/ [\ など（ノード形状）
    CloseBracket,   // ] ) )) } }} /] \]
    Pipe,           // |
    Comment,        // %% ...
    Direction,      // TB, TD, BT, RL, LR
    Newline,
    EOF,

    // Sequence 固有
    Activate,       // activate
    Deactivate,     // deactivate
    Loop,           // loop
    Alt,            // alt
    Else,           // else
    Opt,            // opt
    Par,            // par
    Note,           // Note left of / Note right of / Note over
    End,            // end

    // ClassDiagram 固有
    Visibility,     // +, -, #, ~
    ReturnType,     // メソッド戻り値
    Relationship,   // <|-- , *-- , o-- , --> , -- , ..> , ..|> , ..
}
```

### 3.3 Flowchart パーサ

サポートする構文:

```
flowchart TD
    A[Rectangle] --> B(Rounded)
    B --> C{Diamond}
    C -->|Yes| D[Result 1]
    C -->|No| E[Result 2]
    subgraph sub1 [Title]
        F --> G
    end
```

AST 構造:

```csharp
public class FlowchartDocument
{
    public FlowDirection Direction { get; set; }  // TB, LR, BT, RL
    public List<FlowNode> Nodes { get; }
    public List<FlowEdge> Edges { get; }
    public List<FlowSubgraph> Subgraphs { get; }
}

public class FlowNode
{
    public string Id { get; set; }
    public string Label { get; set; }
    public NodeShape Shape { get; set; }  // Rectangle, Rounded, Diamond, Circle, ...
    public string CssClass { get; set; }
}

public enum NodeShape
{
    Rectangle,      // [text]
    Rounded,        // (text)
    Stadium,        // ([text])
    Cylinder,       // [(text)]
    Circle,         // ((text))
    Diamond,        // {text}
    Hexagon,        // {{text}}
    Parallelogram,  // [/text/]
    Subroutine,     // [[text]]
    Trapezoid,      // [/text\]
}

public class FlowEdge
{
    public string SourceId { get; set; }
    public string TargetId { get; set; }
    public string Label { get; set; }
    public EdgeStyle Style { get; set; }  // Solid, Dotted, Thick
    public ArrowType Arrow { get; set; }  // Normal, Circle, Cross, Open
}
```

### 3.4 StateDiagram パーサ

サポートする構文:

```
stateDiagram-v2
    [*] --> Still
    Still --> Moving
    Moving --> Still
    Moving --> Crash
    state Moving {
        Slow --> Fast
        Fast --> Slow
    }
    state fork_state <<fork>>
```

AST 構造:

```csharp
public class StateDiagramDocument
{
    public List<StateNode> States { get; }
    public List<StateTransition> Transitions { get; }
}

public class StateNode
{
    public string Id { get; set; }
    public string Label { get; set; }
    public StateKind Kind { get; set; }       // Normal, Start, End, Fork, Join, Choice
    public List<StateNode> Children { get; }  // composite state
    public List<StateTransition> InternalTransitions { get; }
}

public class StateTransition
{
    public string FromId { get; set; }
    public string ToId { get; set; }
    public string Label { get; set; }
}
```

### 3.5 ClassDiagram パーサ

サポートする構文:

```
classDiagram
    class Animal {
        +String name
        +int age
        +makeSound() void
    }
    Animal <|-- Dog
    Animal <|-- Cat
    class Dog {
        +fetch() void
    }
```

AST 構造:

```csharp
public class ClassDiagramDocument
{
    public List<ClassNode> Classes { get; }
    public List<ClassRelation> Relations { get; }
}

public class ClassNode
{
    public string Name { get; set; }
    public string Stereotype { get; set; }           // <<interface>>, <<abstract>>, ...
    public List<ClassMember> Fields { get; }
    public List<ClassMember> Methods { get; }
}

public class ClassMember
{
    public Visibility Visibility { get; set; }  // Public, Private, Protected, Internal
    public string Name { get; set; }
    public string Type { get; set; }
    public bool IsMethod { get; set; }
}

public class ClassRelation
{
    public string FromClass { get; set; }
    public string ToClass { get; set; }
    public RelationType Type { get; set; }  // Inheritance, Composition, Aggregation, Association, ...
    public string Label { get; set; }
    public string FromCardinality { get; set; }
    public string ToCardinality { get; set; }
}
```

### 3.6 SequenceDiagram パーサ

サポートする構文:

```
sequenceDiagram
    participant Alice
    participant Bob
    Alice->>Bob: Hello Bob
    Bob-->>Alice: Hello Alice
    Alice->>Bob: How are you?
    loop Every minute
        Bob-->>Alice: Great!
    end
    Note right of Bob: Thinking...
```

AST 構造:

```csharp
public class SequenceDiagramDocument
{
    public List<Participant> Participants { get; }
    public List<SequenceElement> Elements { get; }  // Message / Block / Note
}

public class Participant
{
    public string Id { get; set; }
    public string Alias { get; set; }
    public bool IsActor { get; set; }
}

public abstract class SequenceElement { }

public class SequenceMessage : SequenceElement
{
    public string FromId { get; set; }
    public string ToId { get; set; }
    public string Text { get; set; }
    public MessageStyle Style { get; set; }  // Solid, Dotted
    public ArrowHead ArrowHead { get; set; } // Filled, Open, Cross
    public bool Activate { get; set; }
}

public class SequenceBlock : SequenceElement
{
    public BlockKind Kind { get; set; }  // Loop, Alt, Opt, Par, Critical
    public string Label { get; set; }
    public List<SequenceSection> Sections { get; }  // alt の else 分岐など
}

public class SequenceNote : SequenceElement
{
    public NotePosition Position { get; set; }  // LeftOf, RightOf, Over
    public List<string> OverParticipants { get; set; }
    public string Text { get; set; }
}
```

---

## 4. レイアウトエンジン設計

### 4.1 共通インタフェース

```csharp
public interface ILayoutEngine
{
    LayoutResult Calculate(LayoutGraph graph, LayoutConfig config);
}

public class LayoutGraph
{
    public List<LayoutNode> Nodes { get; }
    public List<LayoutEdge> Edges { get; }
    public List<LayoutGroup> Groups { get; }  // subgraph / composite state
}

public class LayoutNode
{
    public string Id { get; set; }
    public Vector2 Size { get; set; }       // ノードの推定サイズ
    public string GroupId { get; set; }      // 所属グループ（nullable）
}

public class LayoutResult
{
    public Dictionary<string, Vector2> NodePositions { get; }
    public Dictionary<string, Rect> GroupRects { get; }
    public Dictionary<string, List<Vector2>> EdgeWaypoints { get; }
}

public class LayoutConfig
{
    public float NodeSpacingH { get; set; } = 60f;   // ノード間の水平間隔
    public float NodeSpacingV { get; set; } = 80f;   // ノード間の垂直間隔
    public float GroupPadding { get; set; } = 30f;    // Group 内パディング
    public FlowDirection Direction { get; set; } = FlowDirection.TB;
}
```

### 4.2 Sugiyama レイアウトエンジン（Flowchart / StateDiagram / ClassDiagram）

Sugiyama の4フェーズアルゴリズムを簡易実装する。
完全な最適化は NP 困難なため、実用的なヒューリスティクスで十分な品質を目指す。

#### Phase 1: Cycle Removal（閉路除去）

- DFS ベースの Back Edge 検出
- Back Edge を一時的に反転して DAG 化
- 最終描画時に反転エッジのみ矢印方向を元に戻す

```csharp
// 擬似コード
public class CycleRemover
{
    public HashSet<LayoutEdge> RemovedEdges { get; }

    public void RemoveCycles(LayoutGraph graph)
    {
        var visited = new HashSet<string>();
        var onStack = new HashSet<string>();

        foreach (var node in graph.Nodes)
        {
            if (!visited.Contains(node.Id))
                DFS(node.Id, graph, visited, onStack);
        }
    }

    private void DFS(string nodeId, LayoutGraph graph,
                     HashSet<string> visited, HashSet<string> onStack)
    {
        visited.Add(nodeId);
        onStack.Add(nodeId);

        foreach (var edge in graph.GetOutEdges(nodeId))
        {
            if (onStack.Contains(edge.TargetId))
            {
                // Back edge detected → reverse
                edge.Reverse();
                RemovedEdges.Add(edge);
            }
            else if (!visited.Contains(edge.TargetId))
            {
                DFS(edge.TargetId, graph, visited, onStack);
            }
        }
        onStack.Remove(nodeId);
    }
}
```

#### Phase 2: Layer Assignment（レイヤー割り当て）

- Longest Path アルゴリズム（最長パスに基づくレイヤー決定）
- レイヤー間を跨ぐ長いエッジにはダミーノードを挿入

```csharp
public class LayerAssigner
{
    // ノードID → レイヤー番号
    public Dictionary<string, int> Assign(LayoutGraph dag)
    {
        var layers = new Dictionary<string, int>();
        var sorted = TopologicalSort(dag);

        // 各ノードのレイヤー = 入力ノードのレイヤー最大値 + 1
        foreach (var nodeId in sorted)
        {
            int maxParentLayer = -1;
            foreach (var inEdge in dag.GetInEdges(nodeId))
            {
                if (layers.TryGetValue(inEdge.SourceId, out int parentLayer))
                    maxParentLayer = Math.Max(maxParentLayer, parentLayer);
            }
            layers[nodeId] = maxParentLayer + 1;
        }

        // 長いエッジにダミーノード挿入
        InsertDummyNodes(dag, layers);

        return layers;
    }
}
```

#### Phase 3: Crossing Minimization（交差削減）

- Barycenter Heuristic（重心法）
  - 各ノードの位置を隣接レイヤーの接続先の重心位置に設定
  - レイヤーペアを上下交互にスイープし収束するまで繰り返す（最大 24 回）

```csharp
public class CrossingMinimizer
{
    public void Minimize(List<List<string>> layers, LayoutGraph graph, int maxIterations = 24)
    {
        for (int i = 0; i < maxIterations; i++)
        {
            bool improved = false;

            // 下方スイープ
            for (int l = 1; l < layers.Count; l++)
                improved |= OrderByBarycenter(layers[l], layers[l - 1], graph);

            // 上方スイープ
            for (int l = layers.Count - 2; l >= 0; l--)
                improved |= OrderByBarycenter(layers[l], layers[l + 1], graph);

            if (!improved) break;
        }
    }

    private bool OrderByBarycenter(List<string> layer, List<string> fixedLayer, LayoutGraph graph)
    {
        // 各ノードの重心 = 接続先ノードの fixedLayer 内での位置の平均
        var barycenters = new Dictionary<string, float>();
        foreach (var nodeId in layer)
        {
            var positions = GetNeighborPositions(nodeId, fixedLayer, graph);
            barycenters[nodeId] = positions.Count > 0
                ? positions.Average()
                : layer.IndexOf(nodeId);
        }

        var newOrder = layer.OrderBy(n => barycenters[n]).ToList();
        bool changed = !layer.SequenceEqual(newOrder);
        layer.Clear();
        layer.AddRange(newOrder);
        return changed;
    }
}
```

#### Phase 4: Coordinate Assignment（座標割り当て）

- 各レイヤー内のノードを等間隔に配置
- ダミーノードを直線上に配置してエッジの折れを最小化
- Group (subgraph) のバウンディングボックスを計算

```csharp
public class CoordinateAssigner
{
    public LayoutResult Assign(
        List<List<string>> layers,
        LayoutGraph graph,
        LayoutConfig config)
    {
        var positions = new Dictionary<string, Vector2>();

        for (int layerIdx = 0; layerIdx < layers.Count; layerIdx++)
        {
            var layer = layers[layerIdx];
            float totalWidth = layer.Sum(id => graph.GetNode(id).Size.x)
                             + (layer.Count - 1) * config.NodeSpacingH;
            float startX = -totalWidth / 2f;
            float currentX = startX;

            for (int i = 0; i < layer.Count; i++)
            {
                var node = graph.GetNode(layer[i]);
                float x = currentX + node.Size.x / 2f;
                float y = layerIdx * config.NodeSpacingV;

                // Direction 変換: LR なら x,y を入れ替え
                positions[layer[i]] = config.Direction switch
                {
                    FlowDirection.LR => new Vector2(y, x),
                    FlowDirection.RL => new Vector2(-y, x),
                    FlowDirection.BT => new Vector2(x, -y),
                    _                => new Vector2(x, y),   // TB (default)
                };
                currentX += node.Size.x + config.NodeSpacingH;
            }
        }

        return new LayoutResult
        {
            NodePositions = positions,
            GroupRects = CalculateGroupRects(positions, graph, config),
            EdgeWaypoints = CalculateEdgeWaypoints(positions, graph),
        };
    }
}
```

### 4.3 SequenceLayoutEngine（シーケンス図専用）

シーケンス図は Sugiyama とは異なる専用レイアウトを使用する。

```
┌────────┐         ┌────────┐
│ Alice  │         │  Bob   │
└───┬────┘         └───┬────┘
    │  Hello Bob       │       ← Message 1 (y=0)
    │─────────────────>│
    │  Hello Alice     │       ← Message 2 (y=1)
    │<─ ─ ─ ─ ─ ─ ─ ─ │
    │                  │
    │  ┌─loop──────────┤       ← Block (y=2..3)
    │  │ Great!        │
    │  │<─ ─ ─ ─ ─ ─ ─│
    │  └───────────────┤
    │                  │
```

レイアウトロジック:

1. **Participant 配列:** 宣言順に水平方向等間隔配置（x 座標）
2. **メッセージ行:** 上から順に y 座標を `messageIndex * rowHeight` で割り当て
3. **ブロック (loop/alt/opt):** ブロック開始〜end の y 範囲を矩形として描画
4. **ライフライン:** 各 Participant の x 座標に垂直線を描画（GraphView の VisualElement で実装）
5. **Activation bar:** activate/deactivate 間を塗りつぶした矩形として描画

```csharp
public class SequenceLayoutEngine : ILayoutEngine
{
    private const float ParticipantSpacing = 200f;
    private const float RowHeight = 50f;
    private const float ParticipantBoxHeight = 40f;

    public LayoutResult Calculate(LayoutGraph graph, LayoutConfig config)
    {
        var result = new LayoutResult();
        var seqGraph = (SequenceLayoutGraph)graph;

        // 1. Participant の x 座標を決定
        for (int i = 0; i < seqGraph.Participants.Count; i++)
        {
            float x = i * ParticipantSpacing;
            result.NodePositions[seqGraph.Participants[i].Id] =
                new Vector2(x, 0);
        }

        // 2. 各要素の y 座標を順次割り当て
        float currentY = ParticipantBoxHeight + 20f;
        foreach (var element in seqGraph.Elements)
        {
            switch (element)
            {
                case SequenceMessageLayout msg:
                    msg.Y = currentY;
                    currentY += RowHeight;
                    break;
                case SequenceBlockLayout block:
                    block.StartY = currentY;
                    currentY += block.InnerElements.Count * RowHeight + 20f;
                    block.EndY = currentY;
                    currentY += 10f;
                    break;
                case SequenceNoteLayout note:
                    note.Y = currentY;
                    currentY += RowHeight;
                    break;
            }
        }

        return result;
    }
}
```

---

## 5. GraphView レンダラー設計

### 5.1 MermaidGraphViewPanel

```csharp
public class MermaidGraphViewPanel : GraphView
{
    public MermaidGraphViewPanel()
    {
        // 標準操作の設定
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());
        this.AddManipulator(new ContentZoomer());

        // スタイルシート読み込み
        var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
            "Packages/com.ars.mermaid-graphview/Editor/Styles/MermaidGraphView.uss");
        styleSheets.Add(styleSheet);

        // グリッド背景
        var grid = new GridBackground();
        Insert(0, grid);
    }

    /// <summary>
    /// AST + レイアウト結果から GraphView の要素を構築する
    /// </summary>
    public void Render(MermaidDocument document, LayoutResult layout)
    {
        // 既存要素のクリア
        graphElements.ForEach(e => RemoveElement(e));

        var renderer = RendererFactory.Create(document.DiagramType);
        renderer.Render(this, document, layout);
    }
}
```

### 5.2 MermaidNode（Flowchart / StateDiagram / ClassDiagram 用）

```csharp
public class MermaidNode : Node
{
    public string MermaidId { get; private set; }

    public MermaidNode(string id, string label, NodeVisualStyle style)
    {
        MermaidId = id;
        title = label;

        // ノード形状に応じたスタイルクラスを追加
        AddToClassList($"mermaid-node-{style.ShapeClass}");

        // ポート追加（入力・出力は非表示だが接続用に必要）
        var inputPort = Port.Create<Edge>(Orientation.Vertical, Direction.Input, Port.Capacity.Multi, typeof(bool));
        inputPort.portName = "";
        inputPort.style.display = DisplayStyle.None;
        inputContainer.Add(inputPort);

        var outputPort = Port.Create<Edge>(Orientation.Vertical, Direction.Output, Port.Capacity.Multi, typeof(bool));
        outputPort.portName = "";
        outputPort.style.display = DisplayStyle.None;
        outputContainer.Add(outputPort);

        // 位置設定はレンダリング後に外部から
        capabilities &= ~Capabilities.Movable;  // ドラッグ無効（読み取り専用ビューア）
        capabilities &= ~Capabilities.Deletable;
    }
}
```

### 5.3 ClassDiagram 用 MermaidClassNode

ClassDiagram のノードはフィールドとメソッドの一覧を持つ、特殊な構造。

```csharp
public class MermaidClassNode : MermaidNode
{
    public MermaidClassNode(ClassNode classData, NodeVisualStyle style)
        : base(classData.Name, classData.Name, style)
    {
        // ステレオタイプ表示
        if (!string.IsNullOrEmpty(classData.Stereotype))
        {
            var stereotypeLabel = new Label($"«{classData.Stereotype}»");
            stereotypeLabel.AddToClassList("mermaid-stereotype");
            mainContainer.Insert(0, stereotypeLabel);
        }

        // フィールド一覧
        var fieldsContainer = new VisualElement();
        fieldsContainer.AddToClassList("mermaid-class-section");
        foreach (var field in classData.Fields)
        {
            var label = new Label($"{VisibilitySymbol(field.Visibility)} {field.Name}: {field.Type}");
            label.AddToClassList("mermaid-class-member");
            fieldsContainer.Add(label);
        }
        extensionContainer.Add(fieldsContainer);

        // メソッド一覧
        var methodsContainer = new VisualElement();
        methodsContainer.AddToClassList("mermaid-class-section");
        foreach (var method in classData.Methods)
        {
            var label = new Label($"{VisibilitySymbol(method.Visibility)} {method.Name}: {method.Type}");
            label.AddToClassList("mermaid-class-member");
            methodsContainer.Add(label);
        }
        extensionContainer.Add(methodsContainer);

        RefreshExpandedState();
    }

    private string VisibilitySymbol(Visibility v) => v switch
    {
        Visibility.Public    => "+",
        Visibility.Private   => "-",
        Visibility.Protected => "#",
        Visibility.Internal  => "~",
        _ => ""
    };
}
```

### 5.4 MermaidEdge

```csharp
public class MermaidEdge : Edge
{
    public MermaidEdge(EdgeVisualStyle style)
    {
        // エッジスタイルの適用
        AddToClassList($"mermaid-edge-{style.LineStyle}");  // solid, dotted, thick

        // ラベル
        if (!string.IsNullOrEmpty(style.Label))
        {
            var edgeLabel = new Label(style.Label);
            edgeLabel.AddToClassList("mermaid-edge-label");
            Add(edgeLabel);
        }

        capabilities &= ~Capabilities.Deletable;
    }
}
```

### 5.5 SequenceDiagram 用カスタム描画

シーケンス図は GraphView の Node/Edge モデルにうまく乗らないため、
VisualElement ベースのカスタム描画を使用する。

```csharp
public class SequenceDiagramRenderer : IMermaidRenderer
{
    public void Render(MermaidGraphViewPanel view, MermaidDocument doc, LayoutResult layout)
    {
        var seqDoc = (SequenceDiagramDocument)doc.Content;

        // ライフライン（垂直線）
        foreach (var participant in seqDoc.Participants)
        {
            var pos = layout.NodePositions[participant.Id];

            // Participant ボックス（上部）
            var box = new SequenceParticipantElement(participant);
            box.SetPosition(new Rect(pos.x - 60, pos.y, 120, 40));
            view.AddElement(box);

            // ライフライン（点線の垂直線）
            var lifeline = new SequenceLifelineElement(participant.Id);
            lifeline.SetPosition(new Rect(pos.x, 40, 2, layout.TotalHeight));
            view.contentContainer.Add(lifeline);
        }

        // メッセージ（水平矢印）
        foreach (var msg in layout.Messages)
        {
            var arrow = new SequenceArrowElement(msg);
            view.contentContainer.Add(arrow);
        }

        // ブロック (loop / alt / opt)
        foreach (var block in layout.Blocks)
        {
            var blockRect = new SequenceBlockElement(block);
            view.contentContainer.Add(blockRect);
        }
    }
}
```

---

## 6. EditorWindow 設計

### 6.1 MermaidEditorWindow

```csharp
public class MermaidEditorWindow : EditorWindow
{
    private MermaidGraphViewPanel _graphView;
    private TextField _inputField;
    private Label _statusLabel;
    private Label _diagramTypeLabel;

    [MenuItem("Window/Mermaid Viewer")]
    public static void Open()
    {
        var window = GetWindow<MermaidEditorWindow>();
        window.titleContent = new GUIContent("Mermaid Viewer");
        window.minSize = new Vector2(800, 500);
    }

    private void CreateGUI()
    {
        // SplitView: 左ペイン（入力） | 右ペイン（グラフ）
        var splitView = new TwoPaneSplitView(0, 300, TwoPaneSplitViewOrientation.Horizontal);

        // 左ペイン
        var leftPane = new VisualElement();
        leftPane.style.minWidth = 200;

        _diagramTypeLabel = new Label("(no diagram)");
        _diagramTypeLabel.AddToClassList("mermaid-diagram-type");
        leftPane.Add(_diagramTypeLabel);

        _inputField = new TextField
        {
            multiline = true,
            label = "",
            value = "flowchart TD\n    A[Start] --> B[End]"
        };
        _inputField.style.flexGrow = 1;
        _inputField.RegisterValueChangedCallback(OnInputChanged);
        leftPane.Add(_inputField);

        // ファイルドロップ対応
        leftPane.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
        leftPane.RegisterCallback<DragPerformEvent>(OnDragPerform);

        // ステータス
        _statusLabel = new Label("Ready");
        _statusLabel.AddToClassList("mermaid-status");
        leftPane.Add(_statusLabel);

        // 右ペイン
        _graphView = new MermaidGraphViewPanel();
        _graphView.style.flexGrow = 1;

        splitView.Add(leftPane);
        splitView.Add(_graphView);
        rootVisualElement.Add(splitView);

        // 初回レンダリング
        RenderMermaid(_inputField.value);
    }

    private void OnInputChanged(ChangeEvent<string> evt)
    {
        // デバウンス: 300ms 待ってからパース実行
        _pendingText = evt.newValue;
        _lastEditTime = EditorApplication.timeSinceStartup;
        EditorApplication.delayCall += CheckDebounce;
    }

    private double _lastEditTime;
    private string _pendingText;
    private const double DebounceDelay = 0.3;

    private void CheckDebounce()
    {
        if (EditorApplication.timeSinceStartup - _lastEditTime >= DebounceDelay)
            RenderMermaid(_pendingText);
    }

    private void RenderMermaid(string source)
    {
        try
        {
            // 1. パース
            var parser = ParserFactory.Create(source);
            var document = parser.Parse(source);
            _diagramTypeLabel.text = document.DiagramType.ToString();

            // 2. AST → LayoutGraph 変換
            var converter = LayoutGraphConverter.Create(document.DiagramType);
            var layoutGraph = converter.Convert(document);

            // 3. レイアウト計算
            var layoutEngine = LayoutEngineFactory.Create(document.DiagramType);
            var layoutResult = layoutEngine.Calculate(layoutGraph, new LayoutConfig());

            // 4. GraphView レンダリング
            _graphView.Render(document, layoutResult);

            _statusLabel.text = $"OK — {document.DiagramType} ({layoutGraph.Nodes.Count} nodes)";
            _statusLabel.RemoveFromClassList("mermaid-status-error");
        }
        catch (MermaidParseException ex)
        {
            _statusLabel.text = $"Parse Error: {ex.Message}";
            _statusLabel.AddToClassList("mermaid-status-error");
        }
        catch (Exception ex)
        {
            _statusLabel.text = $"Error: {ex.Message}";
            _statusLabel.AddToClassList("mermaid-status-error");
        }
    }

    // .mmd / .md ファイルのドラッグ＆ドロップ対応
    private void OnDragUpdated(DragUpdatedEvent evt)
    {
        if (DragAndDrop.paths.Any(p => p.EndsWith(".mmd") || p.EndsWith(".md")))
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
    }

    private void OnDragPerform(DragPerformEvent evt)
    {
        var path = DragAndDrop.paths.FirstOrDefault(p => p.EndsWith(".mmd") || p.EndsWith(".md"));
        if (path == null) return;

        var content = File.ReadAllText(path);

        // .md の場合は mermaid コードブロックを抽出
        if (path.EndsWith(".md"))
            content = ExtractMermaidBlock(content);

        _inputField.value = content;
    }

    private string ExtractMermaidBlock(string markdown)
    {
        var match = Regex.Match(markdown, @"```mermaid\s*\n([\s\S]*?)```", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : markdown;
    }
}
```

---

## 7. USS スタイル定義

```css
/* MermaidGraphView.uss */

.mermaid-diagram-type {
    font-size: 14px;
    -unity-font-style: bold;
    padding: 8px;
    background-color: rgba(40, 40, 40, 0.8);
    color: #4FC3F7;
}

.mermaid-status {
    font-size: 11px;
    padding: 4px 8px;
    background-color: rgba(30, 30, 30, 0.9);
    color: #A5D6A7;
}

.mermaid-status-error {
    color: #EF5350;
}

/* Flowchart ノード形状 */
.mermaid-node-rectangle #node-border {
    border-radius: 0;
}

.mermaid-node-rounded #node-border {
    border-radius: 8px;
}

.mermaid-node-diamond #node-border {
    /* ダイヤモンドは USS だけでは難しいため、
       VisualElement.generateVisualContent で Mesh 描画を使用 */
}

.mermaid-node-stadium #node-border {
    border-radius: 20px;
}

.mermaid-node-circle #node-border {
    border-radius: 50%;
}

/* ClassDiagram ノード */
.mermaid-class-section {
    border-top-width: 1px;
    border-top-color: rgba(255, 255, 255, 0.2);
    padding: 4px 8px;
}

.mermaid-class-member {
    font-size: 11px;
    -unity-font-style: normal;
    color: #E0E0E0;
    padding: 1px 0;
}

.mermaid-stereotype {
    -unity-text-align: middle-center;
    font-size: 10px;
    color: #FFD54F;
    -unity-font-style: italic;
}

/* Edge スタイル */
.mermaid-edge-solid .edge {
    --edge-width: 2;
}

.mermaid-edge-dotted .edge {
    --edge-width: 2;
    /* USS では dash パターン未サポートのため、
       MermaidEdge 側で generateVisualContent を使って点線描画 */
}

.mermaid-edge-thick .edge {
    --edge-width: 4;
}

.mermaid-edge-label {
    font-size: 11px;
    background-color: rgba(30, 30, 30, 0.9);
    color: #FFF;
    padding: 2px 6px;
    border-radius: 3px;
}

/* Sequence 図専用 */
.sequence-participant-box {
    background-color: #1565C0;
    border-radius: 4px;
    padding: 6px 12px;
    color: #FFF;
    font-size: 12px;
    -unity-text-align: middle-center;
}

.sequence-lifeline {
    border-left-width: 1px;
    border-left-color: rgba(255, 255, 255, 0.3);
}

.sequence-block-rect {
    border-width: 1px;
    border-color: rgba(255, 255, 255, 0.4);
    background-color: rgba(255, 255, 255, 0.05);
    border-radius: 4px;
}
```

---

## 8. データフローまとめ

```
User Input (text / .mmd file)
        │
        ▼
  ┌─────────────┐
  │  Tokenizer   │   文字列 → List<MermaidToken>
  └──────┬──────┘
         ▼
  ┌─────────────────┐
  │  Parser (per type)│   List<MermaidToken> → MermaidDocument (AST)
  └──────┬──────────┘
         ▼
  ┌───────────────────┐
  │LayoutGraphConverter│   AST → LayoutGraph (ノード・エッジ・グループ)
  └──────┬────────────┘
         ▼
  ┌─────────────────┐
  │  Layout Engine    │   LayoutGraph → LayoutResult (位置情報)
  └──────┬──────────┘
         ▼
  ┌──────────────────┐
  │  Renderer          │   AST + LayoutResult → GraphView 要素
  └──────────────────┘
         ▼
   MermaidGraphViewPanel (GraphView 上に表示)
```

---

## 9. 実装優先度とフェーズ計画

### Phase 1（MVP — 1〜2 週間）

- Flowchart パーサ（基本構文: ノード定義、エッジ定義、サブグラフ）
- Sugiyama レイアウト（4 フェーズ簡易実装）
- MermaidNode / MermaidEdge の基本描画
- EditorWindow（テキスト入力 → グラフ表示）

### Phase 2（+1 週間）

- StateDiagram パーサ（composite state 含む）
- ClassDiagram パーサ（フィールド・メソッド・リレーション）
- ノード形状の USS スタイリング拡充
- `.mmd` ファイルの Drag & Drop 対応

### Phase 3（+1 週間）

- SequenceDiagram パーサ
- SequenceLayoutEngine（専用レイアウト）
- SequenceDiagram 用カスタム VisualElement 群
- `.md` ファイル内 mermaid ブロック抽出

### Phase 4（改善・安定化）

- レイアウト品質改善（Coordinate Assignment の Brandes-Köpf アルゴリズム導入検討）
- エッジルーティングの改善（スプライン曲線化）
- パース時のエラーリカバリ（エラー行のハイライト）
- テスト拡充

---

## 10. 既知の制約と検討事項

| 項目                              | 制約・リスク                                              | 対応方針                                                 |
| --------------------------------- | --------------------------------------------------------- | -------------------------------------------------------- |
| GraphView は Experimental API    | 将来の Unity バージョンで破壊的変更の可能性               | 抽象レイヤーを挟んで GraphView 依存を Renderer 層に限定  |
| ダイヤモンド・六角形ノード形状    | USS の border-radius では表現不可                          | `generateVisualContent` + Mesh 描画で対応                |
| エッジの点線描画                  | GraphView Edge は点線未サポート                            | カスタム Edge クラスで `generateVisualContent` オーバーライド |
| Sugiyama の最適化                 | 完全実装は複雑、大規模グラフでパフォーマンス劣化の可能性  | 100 ノード以下をターゲットとし、ヒューリスティクスで十分  |
| Mermaid 構文の網羅率              | 全構文対応は現実的でない（click イベント、style 等）      | 表示に関わるコア構文のみ対応、非対応構文は無視           |
| シーケンス図と GraphView の相性   | Node/Edge モデルに乗らない                                | VisualElement ベースのカスタム描画で対応                  |
| ノードサイズの事前計算            | テキスト量でサイズが変わるが描画前に正確なサイズが不明    | フォントメトリクスから推定 or 固定幅 + 行数で概算         |

---

## 11. テスト方針

### Unit テスト

- **Tokenizer:** 各ダイアグラムタイプの入力に対し正しいトークン列を生成するか
- **Parser:** 標準的な Mermaid テキストに対し正しい AST を構築するか
- **Layout:** 既知の小規模グラフで期待されるレイヤー割り当て・交差数を検証

### 統合テスト

- Mermaid 公式ドキュメントのサンプルコード（各ダイアグラム 5〜10 例）を入力し、
  例外なくレンダリング完了するかを検証
- パースエラー時に EditorWindow がクラッシュせず、エラーメッセージが表示されるか

### 手動テスト

- 大規模グラフ（50〜100 ノード）のレイアウト品質を目視確認
- パン・ズーム・選択の操作感を確認
