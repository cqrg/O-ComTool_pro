using System;
using System.Collections.Generic;
using System.Threading;

namespace O_ComTool_Pro.Modbus
{
    /// <summary>
    /// 把串口 DataReceived 多次到达的字节拼装为完整 Modbus RTU 帧。
    /// 策略：达到期望响应长度(ModbusMaster.ExpectedResponseLength)立即成帧；
    /// 否则空闲 ~50ms 后强制成帧(兜底，避免半帧卡死)。线程安全。
    /// OnFrame 在线程池线程上触发，订阅者需自行 Invoke 回 UI 线程。
    /// </summary>
    public sealed class ModbusReceiveAggregator : IDisposable
    {
        private readonly object _lock = new object();
        private readonly List<byte> _buf = new List<byte>();
        private DateTime _lastFeed;
        private byte _requestFc;
        private readonly Timer _timer;
        private const int IdleMs = 50;

        public event Action<byte[], int> OnFrame;

        public ModbusReceiveAggregator()
        {
            _lastFeed = DateTime.MinValue;
            _timer = new Timer(OnTick, null, 30, 30);
        }

        /// <summary>主机发出请求后调用，告知聚合器期望响应的功能码。</summary>
        public void SetExpectedFunctionCode(byte fc)
        {
            lock (_lock)
            {
                _requestFc = fc;
            }
        }

        /// <summary>投喂一批新到达的字节。</summary>
        public void Feed(byte[] chunk, int length)
        {
            if (chunk == null || length <= 0) return;
            lock (_lock)
            {
                for (int i = 0; i < length; i++) _buf.Add(chunk[i]);
                _lastFeed = DateTime.Now;
                TryEmitLocked();
            }
        }

        public void Reset()
        {
            lock (_lock) { _buf.Clear(); }
        }

        private void TryEmitLocked()
        {
            // _requestFc == 0 表示尚未设定，跳过期望长度判定，仅靠超时成帧
            if (_buf.Count >= 2 && _requestFc != 0)
            {
                byte[] partial = _buf.ToArray();
                int expected = ModbusMaster.ExpectedResponseLength(partial, partial.Length, _requestFc);
                if (expected > 0 && _buf.Count >= expected)
                {
                    EmitLocked(expected);
                    return;
                }
            }
        }

        private void OnTick(object state)
        {
            byte[] frame = null;
            lock (_lock)
            {
                if (_buf.Count == 0) return;
                if (_lastFeed != DateTime.MinValue && (DateTime.Now - _lastFeed).TotalMilliseconds >= IdleMs)
                {
                    // 空闲超时：把缓冲当作一帧吐出
                    byte[] f = _buf.ToArray();
                    _buf.Clear();
                    frame = f;
                }
            }
            if (frame != null)
            {
                Action<byte[], int> h = OnFrame;
                if (h != null) h(frame, frame.Length);
            }
        }

        private void EmitLocked(int count)
        {
            byte[] frame = new byte[count];
            for (int i = 0; i < count; i++) frame[i] = _buf[i];
            // 移除已消费字节
            int remain = _buf.Count - count;
            for (int i = 0; i < remain; i++) _buf[i] = _buf[count + i];
            int removeAt = remain;
            _buf.RemoveRange(removeAt, count);
            // 在锁外触发事件
            System.Threading.Tasks.Task.Run(() =>
            {
                Action<byte[], int> h = OnFrame;
                if (h != null) h(frame, frame.Length);
            });
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
