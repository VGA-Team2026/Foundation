using System;

namespace Foundation.Threading
{
    /// <summary>
    /// コンポーネントのスレッディングモデルを定義する列挙型
    /// </summary>
    public enum ThreadingType
    {
        /// <summary>
        /// メインスレッドのみで動作（Unity APIを直接使用）
        /// </summary>
        MainThreadOnly,

        /// <summary>
        /// 非同期処理可能（UniTask等でawait可能だがメインスレッドに戻る）
        /// </summary>
        AsyncCapable,

        /// <summary>
        /// マルチスレッド対応（Job System/スレッドプールで並列実行可能）
        /// </summary>
        MultiThreaded
    }

    /// <summary>
    /// MonoBehaviourやクラスにスレッディングモデルを宣言するアトリビュート
    /// Inspectorやエディタウィンドウで可視化される
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class ThreadingModelAttribute : Attribute
    {
        public ThreadingType Type { get; }
        public string Description { get; }

        /// <param name="type">スレッディングモデルの種類</param>
        /// <param name="description">補足説明（任意）</param>
        public ThreadingModelAttribute(ThreadingType type, string description = "")
        {
            Type = type;
            Description = description;
        }
    }
}
