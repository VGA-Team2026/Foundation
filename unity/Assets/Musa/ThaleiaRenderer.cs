#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Thaleia — マークダウンをグラフィカルに描画するレンダラー
/// NOTE: 見出し（色付き背景・フォントサイズ変更・トグル開閉）とリストに対応
/// </summary>
public class ThaleiaRenderer
{
    // =====================================================================
    // ブロック定義
    // =====================================================================

    private enum BlockType
    {
        Heading,
        ListItem,
        Paragraph,
        CodeBlock,
    }

    private class DocBlock
    {
        public BlockType Type;
        public int Level;       // NOTE: Heading: 1-6, ListItem: ネスト深度(0〜)
        public string Text;
        public bool IsExpanded;
    }

    // =====================================================================
    // フィールド
    // =====================================================================

    private List<DocBlock> blocks = new List<DocBlock>();
    private bool stylesInitialized;
    private GUIStyle h1Style, h2Style, h3Style;
    private GUIStyle paragraphStyle;
    private GUIStyle listItemStyle;
    private GUIStyle codeBlockStyle;

    // NOTE: 見出しレベルごとの背景色
    private static readonly Color H1Color = new Color(0.20f, 0.40f, 0.65f, 0.85f);
    private static readonly Color H2Color = new Color(0.25f, 0.50f, 0.45f, 0.70f);
    private static readonly Color H3Color = new Color(0.40f, 0.35f, 0.55f, 0.55f);

    // NOTE: インラインマークダウン変換用Regexキャッシュ
    private static readonly Regex BoldRegex = new Regex(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex CodeRegex = new Regex(@"`(.+?)`", RegexOptions.Compiled);

    // =====================================================================
    // パース
    // =====================================================================

    /// <summary>
    /// マークダウンテキストをパースしてブロックリストに変換
    /// </summary>
    public void Parse(string markdown)
    {
        blocks.Clear();
        if (string.IsNullOrEmpty(markdown)) return;

        var lines = markdown.Split('\n');
        bool inCodeBlock = false;
        var codeBuffer = new System.Text.StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            // NOTE: コードブロックの開始/終了
            if (line.TrimStart().StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    // NOTE: コードブロック終了
                    blocks.Add(new DocBlock
                    {
                        Type = BlockType.CodeBlock,
                        Text = codeBuffer.ToString(),
                    });
                    codeBuffer.Clear();
                    inCodeBlock = false;
                }
                else
                {
                    inCodeBlock = true;
                }
                continue;
            }

            if (inCodeBlock)
            {
                if (codeBuffer.Length > 0) codeBuffer.Append('\n');
                codeBuffer.Append(line);
                continue;
            }

            // NOTE: 空行はスキップ
            if (string.IsNullOrWhiteSpace(line)) continue;

            // NOTE: 見出し
            if (line.StartsWith("#"))
            {
                int level = 0;
                while (level < line.Length && line[level] == '#') level++;
                string text = line.Substring(level).Trim();
                blocks.Add(new DocBlock
                {
                    Type = BlockType.Heading,
                    Level = level,
                    Text = text,
                    IsExpanded = true,
                });
                continue;
            }

            // NOTE: リストアイテム（- または * で始まる行）
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                int indent = line.Length - line.TrimStart().Length;
                int nestLevel = indent / 2;
                string text = trimmed.Substring(2);
                blocks.Add(new DocBlock
                {
                    Type = BlockType.ListItem,
                    Level = nestLevel,
                    Text = text,
                });
                continue;
            }

            // NOTE: テーブル行（| で始まる）はそのままパラグラフとして表示
            // NOTE: それ以外の通常テキスト
            blocks.Add(new DocBlock
            {
                Type = BlockType.Paragraph,
                Text = line,
            });
        }

        // NOTE: コードブロックが閉じられなかった場合
        if (inCodeBlock && codeBuffer.Length > 0)
        {
            blocks.Add(new DocBlock
            {
                Type = BlockType.CodeBlock,
                Text = codeBuffer.ToString(),
            });
        }
    }

    // =====================================================================
    // 描画
    // =====================================================================

    /// <summary>
    /// パース済みブロックを描画
    /// </summary>
    public void Draw()
    {
        InitStyles();

        // NOTE: 現在折りたたまれている見出しレベル（0=折りたたみなし）
        int collapsedLevel = 0;

        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];

            // NOTE: 見出しの場合 — 折りたたみ判定をリセットまたは継続
            if (block.Type == BlockType.Heading)
            {
                // NOTE: 折りたたみ中でも同レベル以上の見出しは表示する
                if (collapsedLevel > 0 && block.Level >= collapsedLevel)
                {
                    // NOTE: まだ折りたたみ範囲内 → スキップ
                    continue;
                }

                // NOTE: 折りたたみリセット
                collapsedLevel = 0;

                DrawHeading(block);

                // NOTE: 見出しが閉じられていたら、それ以下のレベルをスキップ
                if (!block.IsExpanded)
                {
                    collapsedLevel = block.Level + 1;
                }
                continue;
            }

            // NOTE: 折りたたみ中はスキップ
            if (collapsedLevel > 0) continue;

            switch (block.Type)
            {
                case BlockType.ListItem:
                    DrawListItem(block);
                    break;
                case BlockType.CodeBlock:
                    DrawCodeBlock(block);
                    break;
                case BlockType.Paragraph:
                    DrawParagraph(block);
                    break;
            }
        }
    }

    // =====================================================================
    // 各ブロックの描画
    // =====================================================================

    private void DrawHeading(DocBlock block)
    {
        var style = block.Level <= 1 ? h1Style : block.Level == 2 ? h2Style : h3Style;
        var bgColor = block.Level <= 1 ? H1Color : block.Level == 2 ? H2Color : H3Color;

        EditorGUILayout.Space(block.Level <= 1 ? 8 : 4);

        // NOTE: 背景付きの見出し描画
        var rect = EditorGUILayout.GetControlRect(false, style.fixedHeight > 0 ? style.fixedHeight : style.fontSize + 12);
        EditorGUI.DrawRect(rect, bgColor);

        // NOTE: トグル三角 + テキスト
        var foldoutRect = new Rect(rect.x + 4, rect.y, 16, rect.height);
        var labelRect = new Rect(rect.x + 20, rect.y, rect.width - 24, rect.height);

        // NOTE: Foldoutの三角アイコンを描画
        block.IsExpanded = EditorGUI.Foldout(foldoutRect, block.IsExpanded, GUIContent.none, true);
        EditorGUI.LabelField(labelRect, block.Text, style);

        EditorGUILayout.Space(2);
    }

    private void DrawListItem(DocBlock block)
    {
        EditorGUILayout.BeginHorizontal();
        float indent = 16f + block.Level * 16f;
        GUILayout.Space(indent);

        // NOTE: ビュレットマーカーの描画
        var bulletRect = EditorGUILayout.GetControlRect(false, GUILayout.Width(8), GUILayout.Height(EditorGUIUtility.singleLineHeight));
        var dotRect = new Rect(bulletRect.x, bulletRect.y + bulletRect.height * 0.5f - 2.5f, 5, 5);
        if (block.Level == 0)
        {
            EditorGUI.DrawRect(dotRect, EditorGUIUtility.isProSkin ? Color.white : Color.black);
        }
        else
        {
            // NOTE: ネストされたリストは丸（中空風に小さい四角）
            EditorGUI.DrawRect(dotRect, EditorGUIUtility.isProSkin ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.4f, 0.4f, 0.4f));
        }

        // NOTE: テキスト（**bold** を richText で表現）
        string richText = ConvertInlineMarkdown(block.Text);
        EditorGUILayout.LabelField(richText, listItemStyle);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawCodeBlock(DocBlock block)
    {
        EditorGUILayout.Space(2);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.TextArea(block.Text, codeBlockStyle, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2);
    }

    private void DrawParagraph(DocBlock block)
    {
        string richText = ConvertInlineMarkdown(block.Text);
        EditorGUILayout.LabelField(richText, paragraphStyle);
    }

    // =====================================================================
    // インラインマークダウン変換
    // =====================================================================

    /// <summary>
    /// **bold** と `code` をリッチテキストに変換
    /// </summary>
    private static string ConvertInlineMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        // NOTE: **bold** → <b>bold</b>
        text = BoldRegex.Replace(text, "<b>$1</b>");

        // NOTE: `code` → <color=#88ccff>code</color>
        text = CodeRegex.Replace(text, "<color=#88ccff>$1</color>");

        return text;
    }

    // =====================================================================
    // スタイル初期化
    // =====================================================================

    private void InitStyles()
    {
        if (stylesInitialized) return;

        h1Style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 16,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(4, 4, 2, 2),
            fixedHeight = 28,
        };
        h1Style.normal.textColor = Color.white;

        h2Style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(4, 4, 2, 2),
            fixedHeight = 24,
        };
        h2Style.normal.textColor = Color.white;

        h3Style = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(4, 4, 2, 2),
            fixedHeight = 22,
        };
        h3Style.normal.textColor = Color.white;

        paragraphStyle = new GUIStyle(EditorStyles.label)
        {
            wordWrap = true,
            richText = true,
            fontSize = 12,
            padding = new RectOffset(8, 4, 1, 1),
        };

        listItemStyle = new GUIStyle(EditorStyles.label)
        {
            wordWrap = true,
            richText = true,
            fontSize = 12,
            padding = new RectOffset(2, 4, 1, 1),
        };

        codeBlockStyle = new GUIStyle(EditorStyles.textArea)
        {
            wordWrap = false,
            richText = false,
            fontSize = 11,
            padding = new RectOffset(8, 8, 4, 4),
        };

        stylesInitialized = true;
    }
}
#endif
