using Unity.Collections;
using Unity.Jobs;

namespace Foundation.Threading
{
    /// <summary>
    /// IJobParallelForを使った並列バッチ処理のヘルパー
    /// 大量のデータを複数ワーカースレッドで分割処理する
    ///
    /// 使い方:
    /// 1. IJobParallelFor実装の構造体を定義
    /// 2. ParallelJobProcessor.Schedule()でジョブを実行
    /// </summary>
    public static class ParallelJobProcessor
    {
        /// <summary>
        /// デフォルトのバッチサイズ（キャッシュライン最適化）
        /// </summary>
        public const int DefaultBatchSize = 64;

        /// <summary>
        /// IJobParallelForをスケジュールして即座に完了を待つ
        /// </summary>
        /// <typeparam name="TJob">IJobParallelFor実装型</typeparam>
        /// <param name="job">ジョブインスタンス</param>
        /// <param name="arrayLength">処理する配列長</param>
        /// <param name="batchSize">バッチサイズ（小さいほど分散度が高い）</param>
        public static void Run<TJob>(TJob job, int arrayLength, int batchSize = DefaultBatchSize)
            where TJob : struct, IJobParallelFor
        {
            var handle = job.Schedule(arrayLength, batchSize);
            handle.Complete();
        }

        /// <summary>
        /// IJobParallelForをスケジュールする（非同期、手動でComplete()が必要）
        /// </summary>
        /// <typeparam name="TJob">IJobParallelFor実装型</typeparam>
        /// <param name="job">ジョブインスタンス</param>
        /// <param name="arrayLength">処理する配列長</param>
        /// <param name="batchSize">バッチサイズ</param>
        /// <param name="dependency">依存するJobHandle</param>
        /// <returns>スケジュールされたJobHandle</returns>
        public static JobHandle Schedule<TJob>(TJob job, int arrayLength, int batchSize = DefaultBatchSize, JobHandle dependency = default)
            where TJob : struct, IJobParallelFor
        {
            return job.Schedule(arrayLength, batchSize, dependency);
        }

        /// <summary>
        /// IJobをスケジュールして即座に完了を待つ（単一ワーカー）
        /// </summary>
        public static void RunSingle<TJob>(TJob job)
            where TJob : struct, IJob
        {
            var handle = job.Schedule();
            handle.Complete();
        }

        /// <summary>
        /// IJobをスケジュールする（非同期、手動でComplete()が必要）
        /// </summary>
        public static JobHandle ScheduleSingle<TJob>(TJob job, JobHandle dependency = default)
            where TJob : struct, IJob
        {
            return job.Schedule(dependency);
        }

        /// <summary>
        /// 複数のJobHandleを結合する
        /// </summary>
        public static JobHandle CombineHandles(NativeArray<JobHandle> handles)
        {
            return JobHandle.CombineDependencies(handles);
        }

        /// <summary>
        /// 2つのJobHandleを結合する
        /// </summary>
        public static JobHandle CombineHandles(JobHandle a, JobHandle b)
        {
            return JobHandle.CombineDependencies(a, b);
        }
    }
}
