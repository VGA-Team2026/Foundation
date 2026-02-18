# テスト用InjectParamList作成コマンド

TemplateParam.assetをベースに、テスト用InjectParamListアセットを新規作成する。

## 引数

- `$ARGUMENTS`: 作成するInjectの名前と設定指示

## 処理フロー

1. `.claude/skills/create-inject.md` を読み込んで手順に従う
2. 引数を解析:
   - アセット名（例: `RemoteTest`, `FeatureTest_NewSkill`）
   - 設定指示（例: `自動走行ON 無敵OFF 30秒後強制死亡 60秒後PlayMode終了`）
3. `TemplateParam.asset` を読み込み、ベースにして新規アセットを作成
4. 指示に従いパラメータとデバッグコマンドを設定
5. 存在しないデバッグコマンドが必要な場合は新規C#ファイルを作成
6. Unityにフォーカスしてmetaファイル生成・メニュー更新
7. 作成完了を報告

## 設定指示の解釈

| 指示例 | 対応するフィールド/操作 |
|---|---|
| `自動走行ON` / `AutoPlayer ON` | `_isEnabled: 1` |
| `自動走行OFF` | `_isEnabled: 0` |
| `無敵ON` | `_isInvincible: 1` |
| `無敵OFF` | `_isInvincible: 0` |
| `白ケンゾクON` | `_startWithWhiteKenzoku: 1` |
| `N秒後強制死亡` | DebugForceDeathCommand (autoExecuteEnabled:1, autoExecuteDelay:N) |
| `N秒後PlayMode終了` | DebugStopPlayModeCommand (autoExecuteEnabled:1, autoExecuteDelay:N) |
| `マグネットテスト` | DebugMagnetTestCommand (autoExecuteEnabled:1) |
| `スキルN発動` | DebugSkillCommand (skillLevel:N) |
| `ゴールワープ` | DebugWarpToGoalCommand |
| `速度N` / `初期速度N` | `_storyInitialSpeed: N` |
| `最大速度N` | `_storyMaxSpeed: N` |
| `リトライN回` | `_maxRetryCount: N` |
| `シードN` | `_randomSeed: N` |
| `エンドレスモード` | `_isEndlessDebug: 1` |

## 使用例

```
/create-inject RemoteTest 自動走行ON 無敵OFF 30秒後強制死亡 60秒後PlayMode終了
/create-inject SkillTest スキル3発動 マグネットテスト
/create-inject SpeedTest 速度50 最大速度100
/create-inject （引数なし → 対話形式で設定）
```
