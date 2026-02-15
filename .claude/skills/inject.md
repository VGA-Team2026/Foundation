# Inject設定変更スキル

`ParamInjectSettings.asset` の `_selectedParamList` に対して、対応するInjectParamListアセットをアサインする。
対応するInjectParamListは自動でリスト化されるため、一番合致するものを選択する。

## 前提知識

- `unity/Assets/DataAsset/Params/ParamInjectSettings.asset` がInjectシステムの設定ファイル
- `_selectedParamList` フィールドのguidで、使用するInjectParamListアセットを指定
- InjectParamListアセットは `unity/Assets/DataAsset/Params/` 配下に存在（サブフォルダ含む）
- 各アセットの `_listName` が表示名、`_description` が説明
- InjectParamListのスクリプトguid: `16a185dbf0cccc841ae27404f4841e88`

## Inject一覧の取得

### Step 1: assetファイルの検索

Grepツールで `unity/Assets/DataAsset/Params/` 配下のInjectParamListアセットを検索する。

```
Grepツール:
  pattern: "guid: 16a185dbf0cccc841ae27404f4841e88"
  path: unity/Assets/DataAsset/Params
  glob: "*.asset"
  output_mode: files_with_matches
```

### Step 2: listNameと説明の取得

各assetファイルをReadツールで読み込み、`_listName` と `_description` を抽出する。

### Step 3: guidの取得

選択されたassetファイルに対応する `.meta` ファイルをReadツールで読み込み、`guid:` 行を取得する。

## 自動マッチング

引数が指定された場合、以下の優先順位で最も合致するInjectParamListを自動選択する:

1. **完全一致**: `_listName` またはファイル名が引数と完全一致
2. **前方一致**: `_listName` またはファイル名が引数で始まる
3. **部分一致**: `_listName`、ファイル名、`_description` に引数が含まれる

一致するものが1つなら自動選択。複数あればユーザーに確認する。

## Inject設定の切り替え手順

1. **一覧取得**: 上記Step 1-2でInjectParamList一覧を取得
2. **選択**: 引数がある場合は自動マッチング、ない場合はユーザーに選択肢を提示
3. **guid取得**: 選択されたassetの `.meta` ファイルからguidを取得（Step 3）
4. **ParamInjectSettings.asset を更新**: Editツールで `_selectedParamList` のguidを書き換え

```yaml
# 変更対象行（Editツールで置換）
_selectedParamList: {fileID: 11400000, guid: {取得したguid}, type: 2}
```

5. **Unityにフォーカス**: アセット変更を反映

```bash
powershell -Command "Add-Type -AssemblyName Microsoft.VisualBasic; [Microsoft.VisualBasic.Interaction]::AppActivate('Unity')"
```

## 現在の設定確認

`ParamInjectSettings.asset` をReadツールで読み込み、`_selectedParamList` のguidを確認する。
そのguidで `.meta` ファイルをGrepして現在選択中のアセットを特定する。

```
Grepツール:
  pattern: "{現在のguid}"
  path: unity/Assets/DataAsset/Params
  glob: "*.meta"
  output_mode: files_with_matches
```

## InjectParamの変更手順

ユーザーがパラメータ変更を要求した場合、選択中のInjectParamListアセットを直接編集する。

### 変更可能なパラメータ一覧

| フィールド名 | 型 | 説明 |
|---|---|---|
| `_isInvincible` | bool (0/1) | 無敵モード |
| `_isEnabled` | bool (0/1) | オートプレイ |
| `_startWithWhiteKenzoku` | bool (0/1) | 白ケンゾク付きで開始 |
| `_storyInitialSpeed` | double | ストーリー初期速度 |
| `_storyMaxSpeed` | double | ストーリー最大速度 |
| `_maxRetryCount` | int | 最大リトライ回数 |
| `_curveInputSuccessDistance` | float | カーブ入力受付距離 |
| `_curveInputFailDistance` | float | カーブ入力失敗距離 |
| `_isEndlessDebug` | bool (0/1) | エンドレスデバッグモード |
| `_endlessDebugLevel` | int | エンドレスデバッグレベル |
| `_endlessInitialSpeed` | double | エンドレス初期速度 |
| `_endlessMaxSpeed` | double | エンドレス最大速度 |
| `_randomSeed` | int | ランダムシード |

### 編集方法

1. 対象のassetファイルをReadツールで読み込む
2. Editツールで該当フィールドの値を変更する
3. Unityにフォーカスして反映

### フィールドが存在しない場合

assetにフィールドが記載されていない場合は追加する。
YAMLのフィールド順序は厳密でないが、既存フィールドの近くに追加すること。

## 関連ファイル

- 設定ファイル: `unity/Assets/DataAsset/Params/ParamInjectSettings.asset`
- パラメータ定義: `unity/Assets/Scripts/InGame/Generated/Inject/InjectParamListParams.cs`
- InjectSystem: `unity/Assets/Scripts/InGame/Inject/InjectSystem.cs`
- アセット格納先: `unity/Assets/DataAsset/Params/` 配下
