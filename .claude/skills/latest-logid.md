# 最新ログID取得スキル

最新のUnityゲームログのIDと日付を取得するスキル。

## 手順

1. `log/unitylog/` ディレクトリから最新のログファイルを特定する

```bash
powershell -Command "Get-ChildItem 'log\unitylog' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | Format-List Name, LastWriteTime"
```

2. ファイル名 `gamelog_{ログID}.txt` からログIDの英数字部分を切り出す
3. ログIDと日付を表示する

## 出力形式

```
{ログID} - {yyyy-MM-dd HH:mm:ss}
```

例: `6pzvejhq` - 2026-01-29 09:16:21
