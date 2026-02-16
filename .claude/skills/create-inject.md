# テスト用InjectParamList作成スキル

`TemplateParam.asset` をベースに、テスト用のInjectParamListアセットを新規作成する。
与えられた指示に従いデバッグコマンド・自動走行・無敵モード等を設定する。

## 概要

1. TemplateParam.asset をコピーして新規アセットを作成
2. 指示に従いパラメータとデバッグコマンドを設定
3. 存在しないデバッグコマンドは新規作成
4. 作成後にデバッグコマンドリスト（InjectParamListMenu）を更新

## ベースファイル

```
unity/Assets/DataAsset/Params/TemplateParam.asset
```

## Step 1: ベースファイルの読み込みとコピー

1. `TemplateParam.asset` をReadツールで読み込む
2. 内容をベースに新規assetファイルをWriteツールで作成
3. 配置先: `unity/Assets/DataAsset/Params/` 配下（用途に応じてサブフォルダ可）

### 変更必須フィールド

| フィールド | 説明 | 例 |
|---|---|---|
| `m_Name` | アセット名（ファイル名と一致させる） | `RemoteTestParam` |
| `_listName` | 表示名 | `遠隔テスト` |
| `_description` | 説明 | `遠隔テスト用パラメータ` |

## Step 2: パラメータ設定

指示に応じて以下のフィールドを設定する。指示がないフィールドはテンプレートの値をそのまま使う。

### 基本パラメータ

| フィールド | 型 | 説明 | テンプレート値 |
|---|---|---|---|
| `_isInvincible` | 0/1 | 無敵モード | 1 |
| `_isEnabled` | 0/1 | 自動走行（AutoPlayer） | 1 |
| `_startWithWhiteKenzoku` | 0/1 | 白ケンゾク付き開始 | 0 |
| `_storyInitialSpeed` | double | ストーリー初期速度 | 20 |
| `_storyMaxSpeed` | double | ストーリー最大速度 | 40 |
| `_maxRetryCount` | int | 最大リトライ回数 | 9 |
| `_isEndlessDebug` | 0/1 | エンドレスデバッグ | 0 |
| `_endlessDebugLevel` | int | エンドレスレベル | 0 |
| `_endlessInitialSpeed` | double | エンドレス初期速度 | 38 |
| `_endlessMaxSpeed` | double | エンドレス最大速度 | 250 |
| `_randomSeed` | int | ランダムシード（0=ランダム） | 0 |
| `_initPropNum` | int | 初期置物生成数 | 10 |

### シナリオ設定

シナリオアセットを指定する場合、対応する `.meta` ファイルからguidを取得して設定する。

```yaml
_scenario: {fileID: 11400000, guid: {シナリオのguid}, type: 2}
```

指定なしの場合はテンプレートの値を使用。

## Step 3: デバッグコマンドの設定

### コマンドのYAML構造

`_commands` リストと `references.RefIds` セクションで管理される。

```yaml
_commands:
- rid: {rid1}
- rid: {rid2}
...
references:
  version: 2
  RefIds:
  - rid: {rid1}
    type: {class: コマンドクラス名, ns: , asm: InGame}
    data:
      フィールド1: 値1
  - rid: {rid2}
    type: {class: コマンドクラス名, ns: , asm: InGame}
    data:
      フィールド1: 値1
```

### rid（参照ID）の生成

新規ridは19桁の一意な数値。既存ridと重複しないように生成する。
例: `1000000000000000001`, `1000000000000000002`, ...

新規作成時は `1000000000000000001` から連番で割り当てればよい。

### 利用可能なデバッグコマンド

| クラス名 | 説明 | SerializeFieldフィールド |
|---|---|---|
| `DebugInvincibleCommand` | 無敵トグル | なし |
| `DebugClearCommand` | 強制ゲームクリア | なし |
| `DebugSkillCommand` | スキル発動 | `skillLevel: int` |
| `DebugSkillLevelUpCommand` | スキルレベルアップ | なし |
| `DebugAddSkillPointCommand` | スキルポイント加算 | なし |
| `DebugWarpToGoalCommand` | ゴール手前にワープ | なし |
| `DebugMagnetTestCommand` | マグネット延長テスト | `magnetDuration: float`, `autoExecuteEnabled: 0/1`, `autoExecuteDelay: float` |
| `DebugForceDeathCommand` | 無敵貫通して強制死亡 | `autoExecuteEnabled: 0/1`, `autoExecuteDelay: float` |
| `DebugStopPlayModeCommand` | PlayMode終了 | `autoExecuteEnabled: 0/1`, `autoExecuteDelay: float` |

### IAutoExecuteCommand対応コマンド

`autoExecuteEnabled: 1` にすると、ゲーム開始後 `autoExecuteDelay` 秒で自動実行される。

```yaml
- rid: 1000000000000000001
  type: {class: DebugForceDeathCommand, ns: , asm: InGame}
  data:
    autoExecuteEnabled: 1
    autoExecuteDelay: 30
```

### dataフィールドがないコマンド

SerializeFieldを持たないコマンドは `data:` の後にスペース1つで終わる（空データ）。

```yaml
- rid: 1000000000000000001
  type: {class: DebugClearCommand, ns: , asm: InGame}
  data:
```

## Step 4: 存在しないデバッグコマンドの新規作成

指示されたデバッグコマンドが存在しない場合、新規C#ファイルを作成する。

### 作成先

```
unity/Assets/Scripts/InGame/Debug/DebugCommand/{コマンドクラス名}.cs
```

### テンプレート

```csharp
#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>
/// {説明}
/// NOTE: IAutoExecuteCommand実装により自動実行可能
/// </summary>
public class {クラス名} : DebugCommand, IAutoExecuteCommand
{
    [Header("Auto Execute Settings")]
    [SerializeField, Tooltip("自動実行を有効にする")]
    private bool autoExecuteEnabled = false;

    [SerializeField, Tooltip("自動実行までの遅延時間（秒）")]
    private float autoExecuteDelay = 3.0f;

    // 必要に応じて追加フィールド

    public override string Name => "{コマンド名}";
    public override string Description => "{説明}";

    // IAutoExecuteCommand実装
    public bool IsAutoExecuteEnabled => autoExecuteEnabled;
    public float AutoExecuteDelay => autoExecuteDelay;

    public override void Execute()
    {
        // 実装
    }
}
#endif
```

### 自動実行が不要な場合

`IAutoExecuteCommand` を実装せず、`DebugCommand` のみ継承する。
`#if UNITY_EDITOR` はエディタ専用の場合のみ使用。

## Step 5: metaファイルの生成

新規 `.cs` ファイルや `.asset` ファイルを作成した場合:

1. Unityにフォーカスしてmetaファイルを自動生成させる
```bash
powershell -Command "Add-Type -AssemblyName Microsoft.VisualBasic; [Microsoft.VisualBasic.Interaction]::AppActivate('Unity')"
```

2. metaファイルが生成されたことを確認
```bash
ls unity/Assets/DataAsset/Params/{ファイル名}.asset.meta
```

## Step 6: デバッグコマンドリストの更新

新規アセット作成後、InjectParamListMenuを更新する必要がある。

### 方法A: Unityエディタ経由（推奨）

Unityにフォーカスすると `[InitializeOnLoad]` により自動でメニューが再生成される。
ただし確実に更新するには:

```bash
node scripts/commands.js unity-recompile
```

### 方法B: 手動でメニュースクリプトを編集

`unity/Assets/Scripts/Editor/Inject/Generated/InjectParamListMenu.cs` に新しいメニュー項目を追加する。

既存エントリのパターンに従い、新しいアセットのGUID（metaファイルから取得）とlistNameでメニュー項目を追加する。

## Step 7: Inject設定の切り替え

作成したアセットを使用するには、`/inject` コマンドで切り替える。
または `.claude/skills/inject.md` の手順に従い `ParamInjectSettings.asset` を直接編集する。

## 完成例

### 遠隔テスト用パラメータ（自動走行 + 30秒後強制死亡 + 60秒後PlayMode終了）

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 16a185dbf0cccc841ae27404f4841e88, type: 3}
  m_Name: RemoteTestParam
  m_EditorClassIdentifier:
  _commands:
  - rid: 1000000000000000001
  - rid: 1000000000000000002
  _curveInputFailDistance: 15
  _curveInputSuccessDistance: 15
  _description: 遠隔テスト用パラメータ
  _endlessDebugLevel: 0
  _endlessDebugScenario: {fileID: 0}
  _endlessInitialSpeed: 38
  _endlessMaxSpeed: 250
  _initPropNum: 10
  _isEnabled: 1
  _isEndlessDebug: 0
  _isInvincible: 0
  _listName: 遠隔テスト
  _maxRetryCount: 9
  _monsterDexUnlockDistance: 0
  _propBoxSheet: {fileID: 11400000, guid: a9fa0816bb8ad0f4cb6737320ce176c4, type: 2}
  _randomSeed: 0
  _scenario: {fileID: 11400000, guid: 997830c79ec0c374ba67ab21294d0271, type: 2}
  _startWithWhiteKenzoku: 0
  _storyInitialSpeed: 20
  _storyMaxSpeed: 40
  references:
    version: 2
    RefIds:
    - rid: 1000000000000000001
      type: {class: DebugForceDeathCommand, ns: , asm: InGame}
      data:
        autoExecuteEnabled: 1
        autoExecuteDelay: 30
    - rid: 1000000000000000002
      type: {class: DebugStopPlayModeCommand, ns: , asm: InGame}
      data:
        autoExecuteEnabled: 1
        autoExecuteDelay: 60
```

## 関連ファイル

- テンプレート: `unity/Assets/DataAsset/Params/TemplateParam.asset`
- デバッグコマンド: `unity/Assets/Scripts/InGame/Debug/DebugCommand/`
- メニュー生成: `unity/Assets/Scripts/Editor/Inject/InjectMenuGenerator.cs`
- 生成済みメニュー: `unity/Assets/Scripts/Editor/Inject/Generated/InjectParamListMenu.cs`
- Inject設定: `unity/Assets/DataAsset/Params/ParamInjectSettings.asset`
- Injectスキル: `.claude/skills/inject.md`
