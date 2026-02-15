# metaファイル取り扱いルール

## 基本ルール

**AIはmetaファイルを生成しない。**

Unityのmetaファイルは必ずUnityエディタに生成させる。

## 手順

1. `.cs`ファイルや`.asset`ファイルを作成・編集
2. Unityリコンパイルを実行してmetaファイルを生成させる
   ```bash
   node scripts/commands.js unity-recompile
   ```
3. metaファイルが生成されたことを確認
   ```bash
   ls path/to/file.cs.meta
   ```
4. 元ファイルとmetaファイルを一緒にコミット

## metaファイルチェックスクリプト

AI生成の可能性があるmetaファイルやGUID重複を検出するスクリプト:

```bash
# 未追跡metaファイルのAI生成チェック（デフォルト）
node scripts/check-meta-files.js

# ステージング済みmetaファイルのチェック
node scripts/check-meta-files.js --staged

# GUID重複チェック
node scripts/check-meta-files.js --guid-check

# 全metaファイルのAI生成チェック
node scripts/check-meta-files.js --all
```

## AI生成metaファイルの特徴

- `mainObjectFileID: 11400000`（Unity 6000.0+では通常`0`）
- 不自然な`executionOrder`値

## 禁止事項

- metaファイルを手動で作成しない
- metaファイルのGUIDを手動で設定しない
- PowerShellの`NewGuid()`でGUIDを生成しない

## Unityが起動していない場合

Unityエディタが起動していない場合は、ユーザーに以下を依頼:
- Unityエディタを起動してプロジェクトを開く
- または、metaファイル生成を後回しにする
