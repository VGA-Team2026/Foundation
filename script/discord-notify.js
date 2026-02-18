#!/usr/bin/env node

/**
 * Discord通知スクリプト
 * Discord Webhookを使用して各種通知を送信
 *
 * 使用方法:
 *   node discord-notify.js <type> [file_path]
 *
 * タイプ:
 *   success      - ビルド成功通知
 *   failure      - ビルド失敗通知（file_path: エラーログファイル）
 *   daily-review - 日次レビュー通知（file_path: サマリーファイル）
 *
 * 環境変数:
 *   DISCORD_WEBHOOK_URL  - Discord Webhook URL（必須）
 *   GITHUB_REPOSITORY    - GitHubリポジトリ名（例: owner/repo）
 *   GITHUB_RUN_ID        - GitHub Actions実行ID
 *   GITHUB_RUN_NUMBER    - GitHub Actions実行番号
 *   BUILD_PROFILE        - ビルドプロファイル名（ビルド通知用）
 *   BUILD_EXIT_CODE      - ビルド終了コード（failure用）
 */

const https = require('https');
const fs = require('fs');

/**
 * Discord Webhookにメッセージを送信
 * @param {string} webhookUrl - Discord Webhook URL
 * @param {object} payload - Discordメッセージペイロード（embeds等）
 * @returns {Promise<void>}
 */
function sendDiscordWebhook(webhookUrl, payload) {
    return new Promise((resolve, reject) => {
        const url = new URL(webhookUrl);
        const data = JSON.stringify(payload);

        const options = {
            hostname: url.hostname,
            port: 443,
            path: url.pathname + url.search,
            method: 'POST',
            headers: {
                'Content-Type': 'application/json; charset=utf-8',
                'Content-Length': Buffer.byteLength(data)
            }
        };

        const req = https.request(options, (res) => {
            let responseData = '';
            res.on('data', (chunk) => {
                responseData += chunk;
            });
            res.on('end', () => {
                // Discord returns 204 No Content on success
                if (res.statusCode >= 200 && res.statusCode < 300) {
                    resolve();
                } else {
                    reject(new Error(`Discord Webhook error (${res.statusCode}): ${responseData}`));
                }
            });
        });

        req.on('error', (error) => {
            reject(error);
        });

        req.write(data);
        req.end();
    });
}

/**
 * 成功時のDiscordメッセージを作成
 * @returns {object} Discordペイロード
 */
function createSuccessMessage() {
    const repository = process.env.GITHUB_REPOSITORY || 'unknown/repo';
    const runId = process.env.GITHUB_RUN_ID || '';
    const runNumber = process.env.GITHUB_RUN_NUMBER || '';
    const buildProfile = process.env.BUILD_PROFILE || 'Development';

    const actionsUrl = `https://github.com/${repository}/actions/runs/${runId}`;

    return {
        embeds: [
            {
                title: 'Unity ビルド成功',
                color: 0x36a64f,
                fields: [
                    { name: 'リポジトリ', value: repository, inline: true },
                    { name: 'ビルド番号', value: `#${runNumber}`, inline: true },
                    { name: 'プロファイル', value: buildProfile, inline: true }
                ],
                url: actionsUrl,
                timestamp: new Date().toISOString()
            }
        ]
    };
}

/**
 * 失敗時のDiscordメッセージを作成
 * @param {string} errorContent - エラーログの内容
 * @returns {object} Discordペイロード
 */
function createFailureMessage(errorContent) {
    const repository = process.env.GITHUB_REPOSITORY || 'unknown/repo';
    const runId = process.env.GITHUB_RUN_ID || '';
    const runNumber = process.env.GITHUB_RUN_NUMBER || '';
    const buildProfile = process.env.BUILD_PROFILE || 'Development';
    const exitCode = process.env.BUILD_EXIT_CODE || 'unknown';

    const actionsUrl = `https://github.com/${repository}/actions/runs/${runId}`;

    // エラー内容を最大1500文字に制限（Discord Embedの制限対策）
    const truncatedError = errorContent.length > 1500
        ? errorContent.substring(0, 1500) + '\n... (省略)'
        : errorContent;

    return {
        embeds: [
            {
                title: 'Unity ビルド失敗',
                color: 0xdc3545,
                fields: [
                    { name: 'リポジトリ', value: repository, inline: true },
                    { name: 'ビルド番号', value: `#${runNumber}`, inline: true },
                    { name: 'プロファイル', value: buildProfile, inline: true },
                    { name: '終了コード', value: `${exitCode}`, inline: true }
                ],
                description: `**エラー詳細:**\n\`\`\`\n${truncatedError}\n\`\`\``,
                url: actionsUrl,
                timestamp: new Date().toISOString()
            }
        ]
    };
}

/**
 * 日次レビュー通知のDiscordメッセージを作成
 * @param {string} summaryContent - サマリーの内容
 * @returns {object} Discordペイロード
 */
function createDailyReviewMessage(summaryContent) {
    const repository = process.env.GITHUB_REPOSITORY || 'unknown/repo';
    const today = new Date().toLocaleDateString('ja-JP', {
        year: 'numeric',
        month: 'long',
        day: 'numeric'
    });

    const repoUrl = `https://github.com/${repository}`;

    // サマリー内容を最大2500文字に制限
    const truncatedSummary = summaryContent.length > 2500
        ? summaryContent.substring(0, 2500) + '\n... (省略)'
        : summaryContent;

    return {
        embeds: [
            {
                title: '日次実装レビュー',
                color: 0x0066cc,
                description: truncatedSummary,
                fields: [
                    { name: 'リポジトリ', value: `[${repository}](${repoUrl})`, inline: true },
                    { name: 'PR一覧', value: `[確認する](${repoUrl}/pulls)`, inline: true }
                ],
                footer: { text: today },
                timestamp: new Date().toISOString()
            }
        ]
    };
}

/**
 * メイン処理
 */
async function main() {
    const args = process.argv.slice(2);

    if (args.length < 1) {
        console.error('使用方法: node discord-notify.js <type> [file_path]');
        console.error('  type: success, failure, daily-review');
        process.exit(1);
    }

    const notifyType = args[0].toLowerCase();
    const filePath = args[1];

    // Webhook URLを取得
    const webhookUrl = process.env.DISCORD_WEBHOOK_URL;

    if (!webhookUrl) {
        console.error('DISCORD_WEBHOOK_URL 環境変数が設定されていません');
        process.exit(1);
    }

    let payload;

    if (notifyType === 'success') {
        payload = createSuccessMessage();
        console.log('ビルド成功通知を送信中...');
    } else if (notifyType === 'failure') {
        let errorContent = 'エラー詳細は Actions ログを確認してください。';
        if (filePath && fs.existsSync(filePath)) {
            try {
                errorContent = fs.readFileSync(filePath, 'utf8');
            } catch (e) {
                console.warn(`エラーファイルの読み込みに失敗: ${e.message}`);
            }
        }
        payload = createFailureMessage(errorContent);
        console.log('ビルド失敗通知を送信中...');
    } else if (notifyType === 'daily-review') {
        let summaryContent = '変更内容はリポジトリを確認してください。';
        if (filePath && fs.existsSync(filePath)) {
            try {
                summaryContent = fs.readFileSync(filePath, 'utf8');
            } catch (e) {
                console.warn(`サマリーファイルの読み込みに失敗: ${e.message}`);
            }
        }
        payload = createDailyReviewMessage(summaryContent);
        console.log('日次レビュー通知を送信中...');
    } else {
        console.error(`無効な通知タイプ: ${notifyType}`);
        console.error('  有効な値: success, failure, daily-review');
        process.exit(1);
    }

    try {
        await sendDiscordWebhook(webhookUrl, payload);
        console.log('Discord通知を送信しました');
    } catch (error) {
        console.error(`Discord通知の送信に失敗: ${error.message}`);
        process.exit(1);
    }
}

if (require.main === module) {
    main();
}

module.exports = { sendDiscordWebhook, createSuccessMessage, createFailureMessage, createDailyReviewMessage };
