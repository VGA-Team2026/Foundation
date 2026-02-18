# Unity 6 MainToolbar API リファレンス

Unity 6 (6000.x) で追加されたメインツールバー拡張API。
Play/Pause/Stopボタンがあるメインツールバーにカスタム要素を追加できる。

公式ドキュメント: https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Toolbars.MainToolbar.html

## 名前空間

```csharp
using UnityEditor.Toolbars;
```

## 基本パターン

`[MainToolbarElement]` を付けた **static メソッド** から `MainToolbarElement` を返す。

```csharp
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

public static class MyToolbarExtension
{
    [MainToolbarElement("MyTool/Button", defaultDockPosition = MainToolbarDockPosition.Right)]
    public static MainToolbarElement CreateButton()
    {
        var icon = EditorGUIUtility.IconContent("SettingsIcon").image as Texture2D;
        var content = new MainToolbarContent(icon, "Settings", "Open Project Settings");
        return new MainToolbarButton(content, () =>
        {
            SettingsService.OpenProjectSettings();
        });
    }
}
```

## MainToolbarElementAttribute

static メソッドを登録する属性。

```csharp
[MainToolbarElement(path, defaultDockPosition, defaultDockIndex, menuPriority)]
```

| パラメータ | 型 | 説明 |
|---|---|---|
| `path` | `string` | 要素の一意識別子（例: `"Git/Branch"`） |
| `defaultDockPosition` | `MainToolbarDockPosition` | 初期配置: `Left`, `Middle`, `Right` |
| `defaultDockIndex` | `int` (省略可) | 同一ドック内の順序 |
| `menuPriority` | `int` (省略可) | ツールバーメニュー内の表示順序 |

## MainToolbarDockPosition (enum)

| 値 | 説明 |
|---|---|
| `Left` | ツールバー左側 |
| `Middle` | ツールバー中央（Play/Pause付近） |
| `Right` | ツールバー右側 |

## MainToolbarContent (struct)

ツールバー要素の表示内容を定義。`GUIContent` に似た役割。

### プロパティ

| プロパティ | 型 | 説明 |
|---|---|---|
| `image` | `Texture2D` | アイコン画像 |
| `text` | `string` | 表示テキスト |
| `tooltip` | `string` | ホバー時ツールチップ |

### コンストラクタ例

```csharp
// テキストのみ
new MainToolbarContent("Label Text")

// アイコンのみ
new MainToolbarContent(iconTexture)

// アイコン + テキスト
new MainToolbarContent(iconTexture, "Label Text")

// アイコン + テキスト + ツールチップ
new MainToolbarContent(iconTexture, "Label Text", "Tooltip here")
```

## MainToolbarElement (基底クラス)

全ツールバー要素の基底。直接使わず、派生クラスを使う。

### 共通プロパティ

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `content` | `MainToolbarContent` | - | 表示内容（書き換えて更新可能） |
| `displayed` | `bool` | `true` | 表示/非表示 |
| `enabled` | `bool` | `true` | 入力イベント受付 |
| `populateContextMenu` | `Action<GenericMenu>` | `null` | 右クリックメニューのカスタマイズ |

## 要素タイプ一覧

### MainToolbarButton

クリックで処理を実行するボタン。

```csharp
[MainToolbarElement("MyTool/Action", defaultDockPosition = MainToolbarDockPosition.Middle)]
public static MainToolbarElement CreateActionButton()
{
    var content = new MainToolbarContent("Do Something");
    return new MainToolbarButton(content, () =>
    {
        Debug.Log("Button clicked!");
    });
}
```

### MainToolbarLabel

情報表示用の読み取り専用ラベル。

```csharp
[MainToolbarElement("MyTool/Info", defaultDockPosition = MainToolbarDockPosition.Right)]
public static MainToolbarElement CreateInfoLabel()
{
    var content = new MainToolbarContent("Info Text");
    return new MainToolbarLabel(content);
}
```

### MainToolbarToggle

ON/OFF切り替えトグル。**自身で状態を保持しない**ため、コールバック内で状態管理が必要。

```csharp
private static bool _isEnabled = false;

[MainToolbarElement("MyTool/Toggle", defaultDockPosition = MainToolbarDockPosition.Middle)]
public static MainToolbarElement CreateToggle()
{
    var content = new MainToolbarContent("Feature");
    return new MainToolbarToggle(content, _isEnabled, (value) =>
    {
        _isEnabled = value;
        MainToolbar.Refresh("MyTool/Toggle");
    });
}
```

### MainToolbarDropdown

ドロップダウンメニュー。

```csharp
private static string _selected = "Option A";

[MainToolbarElement("MyTool/Dropdown", defaultDockPosition = MainToolbarDockPosition.Middle)]
public static MainToolbarElement CreateDropdown()
{
    var content = new MainToolbarContent(_selected);
    return new MainToolbarDropdown(content, (rect) =>
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Option A"), _selected == "Option A", () =>
        {
            _selected = "Option A";
            MainToolbar.Refresh("MyTool/Dropdown");
        });
        menu.AddItem(new GUIContent("Option B"), _selected == "Option B", () =>
        {
            _selected = "Option B";
            MainToolbar.Refresh("MyTool/Dropdown");
        });
        menu.DropDown(rect);
    });
}
```

### MainToolbarSlider

スライダーコントロール。

```csharp
[MainToolbarElement("MyTool/Slider", defaultDockPosition = MainToolbarDockPosition.Middle)]
public static MainToolbarElement CreateSlider()
{
    var content = new MainToolbarContent("Time Scale");
    return new MainToolbarSlider(content, Time.timeScale, 0f, 2f, (value) =>
    {
        Time.timeScale = value;
    });
}
```

## 動的更新

### MainToolbar.Refresh(path)

要素の内容を更新した後に呼び出し、ツールバーに再描画を通知する。

```csharp
// content を更新
label.content = new MainToolbarContent("Updated Text");
// ツールバーに通知
MainToolbar.Refresh("MyTool/Info");
```

### IEnumerable<MainToolbarElement> の返却

1つのメソッドから複数要素をまとめて返すことも可能。

```csharp
[MainToolbarElement("MyTool/Group", defaultDockPosition = MainToolbarDockPosition.Right)]
public static IEnumerable<MainToolbarElement> CreateGroup()
{
    yield return new MainToolbarButton(new MainToolbarContent("A"), () => { });
    yield return new MainToolbarButton(new MainToolbarContent("B"), () => { });
}
```

## 右クリックコンテキストメニュー

```csharp
var element = new MainToolbarButton(content, onClick);
element.populateContextMenu = (menu) =>
{
    menu.AddItem(new GUIContent("Reset"), false, () => { /* reset logic */ });
};
```

## 注意事項

- **Unity 6 (6000.0) 以降専用** - Unity 2022.x/2023.x では使用不可
- `#if UNITY_6000_0_OR_NEWER` で旧バージョンとの互換性を確保
- メソッドは **static** である必要がある
- `path` はプロジェクト内で一意にする（`"Company/Tool/Element"` 形式推奨）
- `MainToolbarToggle` は内部状態を持たない - コールバックで自前管理
- 表示更新後は必ず `MainToolbar.Refresh(path)` を呼ぶ
