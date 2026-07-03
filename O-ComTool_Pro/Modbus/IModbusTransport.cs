using System;

namespace O_ComTool_Pro.Modbus
{
    /// <summary>
    /// MainForm 实现该接口，把 ModbusPanel 与底层串口解耦。
    /// Send 由 UI 线程调用；ResponseReceived/StatusChanged 可能在后台线程触发，
    /// 订阅者需自行 Invoke 回 UI 线程。
    /// </summary>
    public interface IModbusTransport
    {
        /// <summary>发送一帧并记录其功能码(用于响应帧边界与解析)。</summary>
        void Send(byte[] frame, byte requestFc);

        /// <summary>收到并成功拼装/解析的响应。Error 非空表示异常/CRC 错。</summary>
        event Action<ModbusResponse> ResponseReceived;

        /// <summary>状态变化，如超时、模式切换。文本可直接展示。</summary>
        event Action<string> StatusChanged;

        /// <summary>开启/关闭 Modbus 解析模式(开启后接收路径会把字节喂给聚合器)。</summary>
        void SetModbusMode(bool on);

        /// <summary>串口当前是否打开。</summary>
        bool IsOpen { get; }
    }
}
