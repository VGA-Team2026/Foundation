#!/usr/bin/env node
/**
 * Unityコンパイルログ解析スクリプト
 *
 * compile.logからコンパイルエラーを抽出し、
 * サマリをコンソールに出力し、compile-errors.mdとして保存します。
 *
 * 使用方法:
 *   node parse-compile-log.js <compile.log path> [output path]
 */

const fs = require('fs');
const path = require('path');

// エラーパターン定義
const ERROR_PATTERNS = [
  // C#コンパイルエラー (CS0000形式)
  /^(.+\.cs)\((\d+),(\d+)\):\s*error\s+(CS\d+):\s*(.+)$/,
  // Unityコンパイルエラー
  /^Assets[\\\/].+\.cs\(\d+,\d+\):\s*error\s+.+$/,
  // 一般的なエラー
  /error\s+CS\d+:/i,
  // CompilerError
  /CompilerError/i,
];

// 警告パターン（参考用）
const WARNING_PATTERNS = [
  /^(.+\.cs)\((\d+),(\d+)\):\s*warning\s+(CS\d+):\s*(.+)$/,
];

// 無視するパターン（ライセンスエラーなど）
const IGNORE_PATTERNS = [
  /\[Licensing::Module\]/i,
  /Access token is unavailable/i,
  /LogAssemblyErrors \(\d+ms\)/i,
];

/**
 * ログファイルを解析してエラーを抽出
 */
function parseCompileLog(logPath) {
  if (!fs.existsSync(logPath)) {
    console.error(`❌ ログファイルが見つかりません: ${logPath}`);
    process.exit(1);
  }

  const content = fs.readFileSync(logPath, 'utf-8');
  const lines = content.split(/\r?\n/);

  const errors = [];
  const warnings = [];
  const errorsByFile = new Map();

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();
    if (!line) continue;

    // 無視するパターンをスキップ
    if (IGNORE_PATTERNS.some(pattern => pattern.test(line))) {
      continue;
    }

    // エラーパターンをチェック
    let isError = false;
    for (const pattern of ERROR_PATTERNS) {
      if (pattern.test(line)) {
        isError = true;
        break;
      }
    }

    if (isError) {
      // CS形式のエラーを詳細解析
      const csMatch = line.match(/^(.+\.cs)\((\d+),(\d+)\):\s*error\s+(CS\d+):\s*(.+)$/);
      if (csMatch) {
        const errorInfo = {
          file: csMatch[1],
          line: parseInt(csMatch[2]),
          column: parseInt(csMatch[3]),
          code: csMatch[4],
          message: csMatch[5],
          raw: line,
        };
        errors.push(errorInfo);

        // ファイル別にグループ化
        const fileName = path.basename(errorInfo.file);
        if (!errorsByFile.has(fileName)) {
          errorsByFile.set(fileName, []);
        }
        errorsByFile.get(fileName).push(errorInfo);
      } else {
        // その他のエラー
        errors.push({
          file: 'unknown',
          line: 0,
          column: 0,
          code: 'UNKNOWN',
          message: line,
          raw: line,
        });
      }
    }
  }

  return { errors, warnings, errorsByFile };
}

/**
 * エラーサマリを生成
 */
function generateSummary(errors, errorsByFile) {
  const lines = [];

  if (errors.length === 0) {
    lines.push('# ✅ コンパイル成功');
    lines.push('');
    lines.push('コンパイルエラーは検出されませんでした。');
    return lines.join('\n');
  }

  lines.push('# ❌ コンパイルエラー検出');
  lines.push('');
  lines.push(`**エラー数: ${errors.length}件**`);
  lines.push('');

  // ファイル別サマリ
  lines.push('## ファイル別エラー');
  lines.push('');
  lines.push('| ファイル | エラー数 |');
  lines.push('|----------|----------|');

  for (const [fileName, fileErrors] of errorsByFile) {
    lines.push(`| ${fileName} | ${fileErrors.length} |`);
  }
  lines.push('');

  // エラー詳細
  lines.push('## エラー詳細');
  lines.push('');

  for (const [fileName, fileErrors] of errorsByFile) {
    lines.push(`### ${fileName}`);
    lines.push('');
    lines.push('```');
    for (const error of fileErrors) {
      lines.push(`${error.file}(${error.line},${error.column}): error ${error.code}: ${error.message}`);
    }
    lines.push('```');
    lines.push('');
  }

  // 対応方法
  lines.push('## 対応方法');
  lines.push('');
  lines.push('1. 上記のエラーを確認してください');
  lines.push('2. エラー箇所を修正してください');
  lines.push('3. 修正後、再度プッシュしてください');
  lines.push('');

  return lines.join('\n');
}

/**
 * コンソールにエラーを表示
 */
function displayErrors(errors, errorsByFile) {
  console.log('');
  console.log('═'.repeat(60));
  console.log('  UNITY COMPILE ERROR REPORT');
  console.log('═'.repeat(60));
  console.log('');

  if (errors.length === 0) {
    console.log('✅ コンパイルエラーはありません');
    console.log('');
    return;
  }

  console.log(`❌ ${errors.length} 件のコンパイルエラーが検出されました`);
  console.log('');

  // ファイル別に表示
  for (const [fileName, fileErrors] of errorsByFile) {
    console.log(`📁 ${fileName} (${fileErrors.length} errors)`);
    console.log('─'.repeat(50));

    for (const error of fileErrors) {
      console.log(`  Line ${error.line}: [${error.code}] ${error.message}`);
    }
    console.log('');
  }

  console.log('═'.repeat(60));
  console.log('');
}

/**
 * メイン処理
 */
function main() {
  const args = process.argv.slice(2);

  if (args.length < 1) {
    console.error('使用方法: node parse-compile-log.js <compile.log path> [output path]');
    process.exit(1);
  }

  const logPath = args[0];
  const outputPath = args[1] || 'compile-errors.md';
  const outputDir = path.dirname(outputPath);

  console.log(`📄 ログファイル: ${logPath}`);
  console.log(`📝 出力先: ${outputPath}`);

  // ログ解析
  const { errors, warnings, errorsByFile } = parseCompileLog(logPath);

  // コンソール表示
  displayErrors(errors, errorsByFile);

  // サマリ生成
  const summary = generateSummary(errors, errorsByFile);

  // ファイル出力
  fs.writeFileSync(outputPath, summary, 'utf-8');
  console.log(`✅ エラーサマリを出力しました: ${outputPath}`);

  // エラーフラグファイル出力（GitHub Actionsでの判定用）
  const hasErrorsPath = path.join(outputDir, '.has_compile_errors');
  if (errors.length > 0) {
    fs.writeFileSync(hasErrorsPath, `${errors.length}`, 'utf-8');
    console.log(`⚠️ エラーフラグファイルを作成: ${hasErrorsPath}`);
    process.exit(1);
  } else {
    // エラーがない場合はフラグファイルを削除
    if (fs.existsSync(hasErrorsPath)) {
      fs.unlinkSync(hasErrorsPath);
    }
  }
}

main();
