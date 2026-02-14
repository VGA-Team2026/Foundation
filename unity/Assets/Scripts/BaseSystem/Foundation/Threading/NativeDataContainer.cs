using System;
using Unity.Collections;

namespace Foundation.Threading
{
    /// <summary>
    /// NativeArrayのライフサイクルを管理するコンテナ
    /// データ志向設計で連続メモリ配置を保証する
    /// </summary>
    /// <typeparam name="T">unmanaged制約を持つデータ型（struct、参照型フィールドなし）</typeparam>
    public class NativeDataContainer<T> : IDisposable where T : struct
    {
        NativeArray<T> _data;
        Allocator _allocator;
        bool _disposed;

        /// <summary>
        /// NativeArrayへの直接アクセス（Job Systemへの受け渡し用）
        /// </summary>
        public NativeArray<T> Data => _data;

        /// <summary>
        /// 現在の要素数
        /// </summary>
        public int Length => _data.Length;

        /// <summary>
        /// データが有効かどうか
        /// </summary>
        public bool IsCreated => _data.IsCreated;

        /// <param name="capacity">初期容量</param>
        /// <param name="allocator">メモリアロケータ（デフォルト: Persistent）</param>
        public NativeDataContainer(int capacity, Allocator allocator = Allocator.Persistent)
        {
            _allocator = allocator;
            _data = new NativeArray<T>(capacity, allocator);
            _disposed = false;
        }

        /// <summary>
        /// 配列をリサイズする（既存データはコピーされる）
        /// </summary>
        /// <param name="newCapacity">新しい容量</param>
        public void Resize(int newCapacity)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativeDataContainer<T>));
            if (newCapacity == _data.Length) return;

            var newData = new NativeArray<T>(newCapacity, _allocator);
            int copyLength = Math.Min(_data.Length, newCapacity);
            if (copyLength > 0)
            {
                NativeArray<T>.Copy(_data, newData, copyLength);
            }
            _data.Dispose();
            _data = newData;
        }

        /// <summary>
        /// 全要素を指定値で初期化する
        /// </summary>
        public void Fill(T value)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativeDataContainer<T>));
            for (int i = 0; i < _data.Length; i++)
            {
                _data[i] = value;
            }
        }

        /// <summary>
        /// マネージド配列からデータをコピーする
        /// </summary>
        public void CopyFrom(T[] source)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativeDataContainer<T>));
            if (source.Length != _data.Length)
            {
                Resize(source.Length);
            }
            _data.CopyFrom(source);
        }

        /// <summary>
        /// マネージド配列へデータをコピーする
        /// </summary>
        public T[] ToArray()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativeDataContainer<T>));
            return _data.ToArray();
        }

        public void Dispose()
        {
            if (!_disposed && _data.IsCreated)
            {
                _data.Dispose();
                _disposed = true;
            }
        }
    }
}
