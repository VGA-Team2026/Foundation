# Melpomene通知システム仕様

## 概要
毎朝9時に未完了のチケットをSlack/Discordに通知するシステム

## 通知フロー
1. 朝9時にトリガー
2. `@here` でチケットお知らせをポスト
3. 昨日発行されたチケットかつ未完了のIssuesを取得
4. 各個人ごとにグループ化
5. スレッドで各個人にメンションをつけてポスト

## 設定ファイル (secrets)

### ファイル名
`melpomene_notify_config.json`

### フォーマット

```json
{
  "webhook_url": "https://hooks.slack.com/services/xxx または Discord Webhook URL",
  "platform": "slack",
  "channel_id": "#melpomene-notify",
  "notify_time": "09:00",
  "timezone": "Asia/Tokyo",
  "users": [
    {
      "github_username": "github_user1",
      "display_name": "表示名1",
      "mention_id": "<@U01234567>",
      "email": "user1@example.com"
    },
    {
      "github_username": "github_user2",
      "display_name": "表示名2",
      "mention_id": "<@U98765432>",
      "email": "user2@example.com"
    }
  ],
  "default_mention": "<!here>",
  "message_template": {
    "header": ":ticket: 本日のチケットお知らせ",
    "no_tickets": "未完了のチケットはありません :tada:",
    "user_section": "{mention} さんの未完了チケット ({count}件)"
  }
}
```

### フィールド説明

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| webhook_url | string | ○ | Slack/Discord Webhook URL |
| platform | string | ○ | `slack` または `discord` |
| channel_id | string | △ | 通知先チャンネル（Webhook使用時は不要） |
| notify_time | string | ○ | 通知時刻 (HH:MM形式) |
| timezone | string | ○ | タイムゾーン |
| users | array | ○ | ユーザーマッピング配列 |
| default_mention | string | ○ | デフォルトのメンション（@here等） |
| message_template | object | △ | メッセージテンプレート |

### users配列のフィールド

| フィールド | 型 | 必須 | 説明 |
|-----------|-----|------|------|
| github_username | string | ○ | GitHubユーザー名（Issue作成者と照合） |
| display_name | string | ○ | 表示用の名前 |
| mention_id | string | ○ | メンション用ID |
| email | string | △ | メールアドレス（バックアップ用） |

### メンションIDフォーマット

#### Slack
- ユーザー: `<@U01234567>`
- チャンネル: `<#C01234567>`
- グループ: `<!subteam^S01234567>`
- @here: `<!here>`
- @channel: `<!channel>`

#### Discord
- ユーザー: `<@123456789012345678>`
- ロール: `<@&123456789012345678>`
- チャンネル: `<#123456789012345678>`
- @here: `@here`
- @everyone: `@everyone`

## サンプル設定ファイル

### Slack用
```json
{
  "webhook_url": "<YOUR_SLACK_WEBHOOK_URL>",
  "platform": "slack",
  "notify_time": "09:00",
  "timezone": "Asia/Tokyo",
  "users": [
    {
      "github_username": "tanaka",
      "display_name": "田中",
      "mention_id": "<@U01ABCDEF>"
    },
    {
      "github_username": "suzuki",
      "display_name": "鈴木",
      "mention_id": "<@U02GHIJKL>"
    }
  ],
  "default_mention": "<!here>",
  "message_template": {
    "header": ":ticket: 本日のチケットお知らせ",
    "no_tickets": "未完了のチケットはありません :tada:",
    "user_section": "{mention} さんの未完了チケット ({count}件)"
  }
}
```

### Discord用
```json
{
  "webhook_url": "https://discord.com/api/webhooks/123456789/XXXXXXXXXXXX",
  "platform": "discord",
  "notify_time": "09:00",
  "timezone": "Asia/Tokyo",
  "users": [
    {
      "github_username": "tanaka",
      "display_name": "田中",
      "mention_id": "<@123456789012345678>"
    },
    {
      "github_username": "suzuki",
      "display_name": "鈴木",
      "mention_id": "<@234567890123456789>"
    }
  ],
  "default_mention": "@here",
  "message_template": {
    "header": "🎫 本日のチケットお知らせ",
    "no_tickets": "未完了のチケットはありません 🎉",
    "user_section": "{mention} さんの未完了チケット ({count}件)"
  }
}
```

## 通知メッセージ例

### メインポスト
```
@here
🎫 本日のチケットお知らせ

昨日発行された未完了チケット: 5件
- 田中: 2件
- 鈴木: 3件

詳細はスレッドをご確認ください。
```

### スレッド（田中さん用）
```
<@U01ABCDEF> さんの未完了チケット (2件)

#123 [Bug] UIが表示されない
https://github.com/owner/repo/issues/123

#125 [Feature] 新機能追加
https://github.com/owner/repo/issues/125
```

## セキュリティ考慮事項
- `melpomene_notify_config.json` は `.gitignore` に追加すること
- Webhook URLは外部に公開しないこと
- GitHub Actions等で使用する場合はSecretsに保存
