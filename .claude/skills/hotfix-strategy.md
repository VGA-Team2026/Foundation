# Hotfix戦略スキル

QA不具合修正で既存実装を壊さないための判断基準と実装パターン。

## 修正レベルの判断基準

### Level 1: ガード句追加（最も安全）
既存処理の前に条件チェックを追加して、不正な状態での実行を防ぐ。

```csharp
// NOTE: hotfix - null参照回避 (#123)
if (target == null) return;
```

**適用条件:**
- 特定条件でnull参照やindex out of rangeが発生する
- 特定の状態遷移が不正に行われる
- 入力が想定外のタイミングで来る

### Level 2: 条件分岐追加（安全）
既存のif-else構造に条件を追加し、不具合パスを回避する。

```csharp
// NOTE: hotfix - カーブ中はスライド入力を無視 (#456)
if (isCurving && action == ActionType.Slide)
{
    return false;
}
// 既存処理はそのまま
```

**適用条件:**
- 特定の状態組み合わせで不具合が発生する
- 既存のフロー自体は正しく、特定ケースのみ問題

### Level 3: 値の補正・クランプ（安全）
計算結果や状態値を安全な範囲に補正する。

```csharp
// NOTE: hotfix - 速度が負にならないよう補正 (#789)
speed = Mathf.Max(0f, speed);
```

**適用条件:**
- 計算結果が想定範囲外になる
- 浮動小数点の誤差で境界値を超える

### Level 4: フラグ追加（注意が必要）
新しいboolフラグで状態を制御する。

```csharp
// NOTE: hotfix - 二重実行防止フラグ (#101)
private bool isProcessing;

public void Execute()
{
    if (isProcessing) return;
    isProcessing = true;
    // ...
    isProcessing = false;
}
```

**適用条件:**
- 処理の二重実行や競合が原因
- タイミング依存の不具合

### Level 5: メソッド追加（要相談）
既存メソッドは変更せず、新しいメソッドを追加して呼び出し側で使い分ける。

**適用条件:**
- 既存メソッドの修正では副作用が大きい
- 新しい振る舞いを安全に追加したい

## 禁止事項

- 既存のpublic メソッドシグネチャの変更
- 既存のクラス継承構造の変更
- 既存のイベント購読/発行パターンの変更
- 複数ファイルにまたがる大規模リファクタリング
- パフォーマンスに影響する処理の追加（Update内のGCAlloc等）

## コメント規約

修正箇所には必ず以下のフォーマットでコメントを付与:

```csharp
// NOTE: hotfix - {不具合の簡潔な説明} (#{Issue番号})
```

Issue番号がない場合:
```csharp
// NOTE: hotfix - {不具合の簡潔な説明}
```

## 根本対応が必要な場合

hotfixでは対応しきれないと判断した場合:
1. PR本文に `TODO: 根本対応が必要` と明記
2. 根本対応用のIssueを別途作成
3. hotfixはあくまで暫定対応として実装

```bash
gh issue create --title "refactor: {根本対応の説明}" --body "hotfix PR: #{PR番号} の根本対応" --label "enhancement"
```
