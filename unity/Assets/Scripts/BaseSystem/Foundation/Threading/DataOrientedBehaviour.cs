using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Foundation.Threading
{
    /// <summary>
    /// データ志向設計のMonoBehaviour基底クラス
    /// NativeArrayによる連続メモリ配置とJob Systemによる並列処理を提供する
    ///
    /// 使い方:
    /// 1. データ構造体Tを定義（structのみ、参照型フィールド不可）
    /// 2. このクラスを継承し、InitialCapacityとScheduleJobを実装
    /// 3. ProcessData()をUpdate/FixedUpdateから呼び出す
    /// </summary>
    /// <typeparam name="T">処理対象のデータ型（struct制約）</typeparam>
    [ThreadingModel(ThreadingType.MultiThreaded, "Job Systemによる並列データ処理")]
    public abstract class DataOrientedBehaviour<T> : MonoBehaviour where T : struct
    {
        NativeDataContainer<T> _dataContainer;

        /// <summary>
        /// データコンテナへのアクセス
        /// </summary>
        protected NativeDataContainer<T> DataContainer => _dataContainer;

        /// <summary>
        /// 初期容量を返す（サブクラスで定義）
        /// </summary>
        protected abstract int InitialCapacity { get; }

        /// <summary>
        /// Job Systemジョブをスケジュールする（サブクラスで実装）
        /// </summary>
        /// <param name="data">処理対象のNativeArray</param>
        /// <param name="dependency">前段のJobHandle</param>
        /// <returns>スケジュールされたJobHandle</returns>
        protected abstract JobHandle ScheduleJob(NativeArray<T> data, JobHandle dependency);

        protected virtual void OnEnable()
        {
            _dataContainer = new NativeDataContainer<T>(InitialCapacity);
        }

        protected virtual void OnDisable()
        {
            _dataContainer?.Dispose();
            _dataContainer = null;
        }

        /// <summary>
        /// データ処理を実行する（同期完了）
        /// Update()やFixedUpdate()から呼び出す
        /// </summary>
        protected void ProcessData()
        {
            if (_dataContainer == null || !_dataContainer.IsCreated) return;

            var handle = ScheduleJob(_dataContainer.Data, default);
            handle.Complete();
        }

        /// <summary>
        /// データ処理をスケジュールする（非同期）
        /// JobHandleを返すので、後で手動でComplete()を呼ぶ必要がある
        /// LateUpdateで結果を使う場合などに利用
        /// </summary>
        protected JobHandle ScheduleProcessData(JobHandle dependency = default)
        {
            if (_dataContainer == null || !_dataContainer.IsCreated) return dependency;

            return ScheduleJob(_dataContainer.Data, dependency);
        }

        /// <summary>
        /// マネージド配列からデータを設定する
        /// </summary>
        protected void SetData(T[] source)
        {
            if (_dataContainer == null) return;
            _dataContainer.CopyFrom(source);
        }

        /// <summary>
        /// 処理結果をマネージド配列として取得する
        /// </summary>
        protected T[] GetResults()
        {
            if (_dataContainer == null) return System.Array.Empty<T>();
            return _dataContainer.ToArray();
        }
    }
}
