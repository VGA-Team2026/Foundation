# マグネットテスト設定スキル

マグネット機能をテストするためのInject設定を行うスキル。

## 概要

- **MagnetTestParam**: 無敵＋自動走行でマグネット機能をテスト
- **DebugMagnetTestCommand**: マグネット継続時間を300秒に延長してスキル発動

## 設定ファイル

| ファイル | 説明 |
|---------|------|
| `unity/Assets/DataAsset/Params/MagnetTestParam.asset` | テスト用パラメータ |
| `unity/Assets/Scripts/InGame/Debug/DebugCommand/DebugMagnetTestCommand.cs` | デバッグコマンド |

## Inject設定変更

ParamInjectSettingsの`_selectedParamList`を以下に変更:

```yaml
_selectedParamList: {fileID: 11400000, guid: <MagnetTestParamのGUID>, type: 2}
```

## テスト手順

1. Inject設定をMagnetTestParamに変更
2. Unityエディタでリコンパイル
3. PlayModeで開始（自動走行 + 無敵）
4. デバッグメニューから「Magnet Test」ボタンを押す
5. マグネット継続時間が300秒に延長された状態でスキル発動

## MagnetTestParamの設定内容

- `_isInvincible: 1` - 無敵モード有効
- `_isEnabled: 1` - 自動走行モード有効
- `_commands`: DebugMagnetTestCommand（magnetDuration: 300秒）

## DebugMagnetTestCommandの動作

1. PlayerSkillを検索
2. リフレクションでskillActions[0]（SkillMagnet）にアクセス
3. activateTimeを指定時間（デフォルト300秒）に変更
4. ExecuteSkill(1)でマグネットスキルを発動
