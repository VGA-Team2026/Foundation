using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Concurrent;

/// <summary>
/// ランタイムコマンドブリッジ
/// NOTE: UnityCommandServer（HTTPスレッド）からのコマンドをメインスレッドで実行する
/// NOTE: ConcurrentQueueでスレッド安全にコマンドを受け渡す
/// NOTE: ゲーム固有のコマンド実装はプロジェクト側でオーバーライドすること
/// </summary>
[InitializeOnLoad]
public static class RuntimeCommandBridge
{
    /// <summary>
    /// コマンド実行リクエスト
    /// </summary>
    public class CommandRequest
    {
        public string commandType;
        public string parametersJson;
        public Action<CommandResult> callback;
    }

    /// <summary>
    /// コマンド実行結果
    /// </summary>
    public class CommandResult
    {
        public bool success;
        public string message;
    }

    /// <summary>
    /// ゲーム状態情報
    /// </summary>
    public class GameStatusInfo
    {
        public string gameState;
        public bool isPlaying;
        public float totalDistance;
        public string currentInjectList;
    }

    // NOTE: スレッドセーフなコマンドキュー
    private static readonly ConcurrentQueue<CommandRequest> commandQueue = new ConcurrentQueue<CommandRequest>();

    // NOTE: プロジェクト側でコマンド実行ロジックを差し替え可能にするデリゲート
    public static Func<string, string, CommandResult> CommandHandler;
    public static Func<GameStatusInfo> GameStatusHandler;
    public static Func<string, CommandResult> InjectHandler;

    static RuntimeCommandBridge()
    {
        EditorApplication.update += ProcessQueue;
    }

    /// <summary>
    /// コマンドをキューに追加（HTTPスレッドから呼び出し可能）
    /// </summary>
    public static void EnqueueCommand(CommandRequest request)
    {
        commandQueue.Enqueue(request);
    }

    /// <summary>
    /// ゲーム状態を取得（メインスレッドから呼び出す必要あり）
    /// NOTE: UnityCommandServerからはdelayCall経由で呼び出す
    /// </summary>
    public static GameStatusInfo GetGameStatus()
    {
        if (GameStatusHandler != null)
        {
            return GameStatusHandler();
        }

        return new GameStatusInfo
        {
            gameState = "NotRunning",
            isPlaying = false,
            totalDistance = 0f,
            currentInjectList = "None"
        };
    }

    /// <summary>
    /// InjectParamListを名前で切り替え
    /// NOTE: メインスレッドから呼び出す
    /// </summary>
    public static CommandResult ChangeInjectParamList(string paramListName)
    {
        if (InjectHandler != null)
        {
            return InjectHandler(paramListName);
        }

        return new CommandResult
        {
            success = false,
            message = "InjectHandler is not registered"
        };
    }

    /// <summary>
    /// 毎フレームキューを処理
    /// </summary>
    private static void ProcessQueue()
    {
        // NOTE: 再生中でなければスキップ
        if (!EditorApplication.isPlaying) return;

        while (commandQueue.TryDequeue(out var request))
        {
            var result = ExecuteCommand(request.commandType, request.parametersJson);
            request.callback?.Invoke(result);
        }
    }

    /// <summary>
    /// コマンドを実行
    /// NOTE: プロジェクト側のCommandHandlerに委譲
    /// </summary>
    private static CommandResult ExecuteCommand(string commandType, string parametersJson)
    {
        if (CommandHandler != null)
        {
            return CommandHandler(commandType, parametersJson);
        }

        return new CommandResult
        {
            success = false,
            message = $"CommandHandler is not registered. Cannot execute: {commandType}"
        };
    }
}
