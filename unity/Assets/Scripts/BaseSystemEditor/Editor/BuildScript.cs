using UnityEditor;
using UnityEngine;

/// <summary>
/// CI用ビルドスクリプト
/// pr-compile-check.yml から -executeMethod BuildScript.CompilePlayerScripts で呼び出される
/// </summary>
public class BuildScript
{
    /// <summary>
    /// プレイヤースクリプトのコンパイルチェック
    /// バッチモードでのプロジェクト起動時にスクリプトコンパイルが自動的に行われ、
    /// コンパイルエラーがある場合はこのメソッドに到達する前にUnityが非ゼロで終了する
    /// </summary>
    public static void CompilePlayerScripts()
    {
        Debug.Log("=== TestCompile: Compilation successful ===");
    }
}
