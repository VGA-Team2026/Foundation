# Inject設定変更コマンド

`ParamInjectSettings.asset` の `_selectedParamList` に対して、対応するInjectParamListアセットをアサインする。
対応するInjectParamListは自動でリスト化されるため、一番合致するものを選択する。

## 引数

- `$ARGUMENTS`: 設定名やパラメータ変更指示（省略時は一覧表示と選択）

## 処理フロー

1. `.claude/skills/inject.md` を読み込んで手順に従う
2. 現在の設定を確認（`ParamInjectSettings.asset` の `_selectedParamList` guid）
3. InjectParamList一覧を自動取得（Grepツールでスクリプトguid検索）
4. 引数が指定されている場合:
   - 設定名（例: `Test_Area03`, `FeatureTestParam`）→ 自動マッチングで最も合致するInjectに切り替え
   - パラメータ変更指示（例: `無敵ON`, `自動走行OFF`）→ 現在のInjectのパラメータを変更
   - 両方の場合は切り替え後にパラメータ変更
5. 引数が未指定の場合:
   - InjectParamList一覧を表示（`_listName` と `_description`）
   - ユーザーに選択させる
6. 変更後にUnityにフォーカスを当てて反映

## 自動マッチング

引数で設定名が指定された場合、`_listName` とファイル名に対して以下の優先順で一番合致するものを自動選択する:
1. 完全一致 → 2. 前方一致 → 3. 部分一致

一致が1つなら確認なしで切り替え。複数あればユーザーに確認。

## 使用例

```
/inject Test_Area03           → テスト_エリア3に切り替え
/inject Feature               → 機能テスト（FeatureTestParam）に自動マッチ
/inject 無敵ON                → 現在のInjectで_isInvincible=1に変更
/inject Development 自動走行ON → Developmentに切り替え後_isEnabled=1
/inject                       → 一覧表示して選択
```
