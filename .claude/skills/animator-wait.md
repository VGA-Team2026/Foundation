# Animatorアニメーション待機スキル

UnityのAnimatorでアニメーション再生完了を待機する際の注意点と推奨パターン。

## 問題: SetTrigger + normalizedTimeで即戻りする

`SetTrigger` → 1フレーム待ち → `normalizedTime < 1.0f` のwhileループは**即抜け**する。

### 原因

`SetTrigger("Play")` を呼んでも、Animatorは次フレームでまだ **Idle ステート** にいることがある。
Idleの `normalizedTime` は `>= 1.0` なので、whileの `< 1.0f` 条件が最初からfalseになる。

```csharp
// NG: 即戻りする
animator.SetTrigger("Play");
await UniTask.Yield();
while (animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f)
{
    await UniTask.Yield();
}
```

## 推奨パターン: CrossFadeで即座にステート遷移

`CrossFade` はトリガー+トランジション条件に依存せず、指定ステートへ直接遷移を開始する。

```csharp
// OK: CrossFadeで即座に再生ステートへ遷移
animator.CrossFade("Result", 0f, 0, 0f);  // (stateName, transitionDuration, layer, normalizedOffset)
await UniTask.Yield();  // CrossFade適用のため1フレーム待機
while (animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f)
{
    await UniTask.Yield();
}
```

### CrossFadeのパラメータ

```csharp
CrossFade(
    string stateName,    // 遷移先ステート名
    float transitionDuration,  // 遷移時間（0で即座に切り替え）
    int layer,           // レイヤー（通常0）
    float normalizedTime // 再生開始位置（0で先頭から）
)
```

## ステート名の確認方法

AnimatorControllerファイル（.controller）から `m_Name:` を検索して再生ステート名を確認する。

```
grep "m_Name:" path/to/Foo.controller
```

## クリックスキップとの併用パターン

```csharp
animator.CrossFade("Result", 0f, 0, 0f);
await UniTask.Yield();

while (animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f)
{
    if (UnityEngine.Input.GetMouseButtonDown(0))
    {
        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        animator.Play(stateInfo.fullPathHash, 0, 1.0f);
        animator.Update(0f);
        break;
    }
    await UniTask.Yield();
}
```
