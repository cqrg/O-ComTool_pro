using System;

namespace O_ComTool_Pro.Modbus
{
    /// <summary>
    /// Modbus RTU 帧构造与解析（主机侧）。算法依据 Modbus over Serial Line 标准；
    /// CRC 复用 CrcUtil.Crc16Modbus（poly 0xA001, init 0xFFFF，低字节在前）。
    /// </summary>
    public static class ModbusMaster
    {
        // 功能码
        public const byte FC_READ_COILS = 0x01;
        public const byte FC_READ_DISCRETE = 0x02;
        public const byte FC_READ_HOLDING = 0x03;
        public const byte FC_READ_INPUT = 0x04;
        public const byte FC_WRITE_SINGLE_COIL = 0x05;
        public const byte FC_WRITE_SINGLE_REG = 0x06;
        public const byte FC_WRITE_MULTI_COILS = 0x0F;
        public const byte FC_WRITE_MULTI_REGS = 0x10;

        /// <summary>读线圈/离散输入/保持寄存器/输入寄存器（FC 01/02/03/04）。</summary>
        public static byte[] BuildRead(byte slave, byte fc, ushort addr, ushort qty)
        {
            byte[] pdu = new byte[] { slave, fc, Hi(addr), Lo(addr), Hi(qty), Lo(qty) };
            return AppendCrc(pdu);
        }

        /// <summary>写单个线圈(FC05, value=true→0xFF00) 或单个寄存器(FC06)。</summary>
        public static byte[] BuildWriteSingle(byte slave, byte fc, ushort addr, ushort value)
        {
            ushort v = (fc == FC_WRITE_SINGLE_COIL) ? (value != 0 ? (ushort)0xFF00 : (ushort)0x0000) : value;
            byte[] pdu = new byte[] { slave, fc, Hi(addr), Lo(addr), Hi(v), Lo(v) };
            return AppendCrc(pdu);
        }

        /// <summary>写多个保持寄存器（FC10）。</summary>
        public static byte[] BuildWriteMultiRegs(byte slave, ushort addr, ushort[] values)
        {
            int n = values == null ? 0 : values.Length;
            byte[] pdu = new byte[6 + 1 + n * 2];
            pdu[0] = slave;
            pdu[1] = FC_WRITE_MULTI_REGS;
            pdu[2] = Hi(addr); pdu[3] = Lo(addr);
            ushort qty = (ushort)n;
            pdu[4] = Hi(qty); pdu[5] = Lo(qty);
            pdu[6] = (byte)(n * 2);
            for (int i = 0; i < n; i++)
            {
                pdu[7 + i * 2] = Hi(values[i]);
                pdu[8 + i * 2] = Lo(values[i]);
            }
            return AppendCrc(pdu);
        }

        /// <summary>写多个线圈（FC0F）。coils[i]=true→1。</summary>
        public static byte[] BuildWriteMultiCoils(byte slave, ushort addr, bool[] coils)
        {
            int n = coils == null ? 0 : coils.Length;
            int bytes = (n + 7) / 8;
            byte[] pdu = new byte[6 + 1 + bytes];
            pdu[0] = slave;
            pdu[1] = FC_WRITE_MULTI_COILS;
            pdu[2] = Hi(addr); pdu[3] = Lo(addr);
            ushort qty = (ushort)n;
            pdu[4] = Hi(qty); pdu[5] = Lo(qty);
            pdu[6] = (byte)bytes;
            for (int i = 0; i < n; i++)
            {
                if (coils[i]) pdu[7 + i / 8] |= (byte)(1 << (i % 8));
            }
            return AppendCrc(pdu);
        }

        /// <summary>解析响应帧。expectedFc 为请求时使用的功能码，用于识别异常(0x80)。</summary>
        public static ModbusResponse ParseResponse(byte[] frame, int length, byte expectedFc)
        {
            ModbusResponse r = new ModbusResponse();
            if (frame == null || length < 5)
            {
                r.Error = "帧过短";
                return r;
            }
            // 保留原始帧字节，供回显
            r.RawFrame = new byte[length];
            Array.Copy(frame, 0, r.RawFrame, 0, length);
            // 校验 CRC（最后两字节，小端）
            if (!CheckCrc(frame, length))
            {
                r.Error = "CRC 错误";
                return r;
            }
            r.Slave = frame[0];
            r.Fc = frame[1];
            int dataLen = length - 4; // 去除 slave + fc + 2字节CRC
            if (r.Fc == (expectedFc | 0x80))
            {
                r.IsException = true;
                r.ExceptionCode = frame[2];
                r.Error = "异常码 0x" + r.ExceptionCode.ToString("X2");
                return r;
            }
            switch (r.Fc)
            {
                case FC_READ_COILS:
                case FC_READ_DISCRETE:
                    {
                        int bc = frame[2];
                        r.Data = new byte[bc];
                        Array.Copy(frame, 3, r.Data, 0, bc);
                        r.Coils = new bool[bc * 8];
                        for (int i = 0; i < bc * 8; i++)
                            r.Coils[i] = (frame[3 + i / 8] & (1 << (i % 8))) != 0;
                        break;
                    }
                case FC_READ_HOLDING:
                case FC_READ_INPUT:
                    {
                        int bc = frame[2];
                        int regs = bc / 2;
                        r.Data = new byte[bc];
                        Array.Copy(frame, 3, r.Data, 0, bc);
                        r.RegisterValues = new ushort[regs];
                        for (int i = 0; i < regs; i++)
                            r.RegisterValues[i] = (ushort)((frame[3 + i * 2] << 8) | frame[4 + i * 2]);
                        break;
                    }
                case FC_WRITE_SINGLE_COIL:
                case FC_WRITE_SINGLE_REG:
                case FC_WRITE_MULTI_COILS:
                case FC_WRITE_MULTI_REGS:
                    // 写响应为回显/地址+数量，无数据载荷
                    r.Data = new byte[0];
                    break;
                default:
                    r.Error = "未知功能码 0x" + r.Fc.ToString("X2");
                    break;
            }
            return r;
        }

        /// <summary>根据已发功能码与(读响应)字节计数推算期望响应长度；不足以判定返回 -1。</summary>
        public static int ExpectedResponseLength(byte[] partial, int have, byte requestFc)
        {
            if (have < 2) return -1;
            byte fc = partial[1];
            if ((fc & 0x80) != 0) return 5;             // 异常响应：slave+fc+exc+CRC = 5
            switch (fc)
            {
                case FC_READ_COILS:
                case FC_READ_DISCRETE:
                case FC_READ_HOLDING:
                case FC_READ_INPUT:
                    if (have >= 3) return 3 + partial[2] + 2;  // slave+fc+byteCount+data+CRC
                    return -1;
                case FC_WRITE_SINGLE_COIL:
                case FC_WRITE_SINGLE_REG:
                case FC_WRITE_MULTI_COILS:
                case FC_WRITE_MULTI_REGS:
                    return 8;                            // 写响应固定 8 字节
                default:
                    return -1;
            }
        }

        // ---- 工具 ----
        static byte[] AppendCrc(byte[] data)
        {
            byte[] crc = CrcUtil.Crc16ModbusBytes(data, data.Length);
            byte[] result = new byte[data.Length + 2];
            Array.Copy(data, 0, result, 0, data.Length);
            result[data.Length] = crc[0];      // 低字节
            result[data.Length + 1] = crc[1];  // 高字节
            return result;
        }

        static bool CheckCrc(byte[] frame, int length)
        {
            if (length < 4) return false;
            ushort calc = CrcUtil.Crc16Modbus(frame, length - 2);
            ushort recv = (ushort)((frame[length - 1] << 8) | frame[length - 2]);
            return calc == recv;
        }

        static byte Hi(ushort v) { return (byte)((v >> 8) & 0xFF); }
        static byte Lo(ushort v) { return (byte)(v & 0xFF); }
    }

    public class ModbusResponse
    {
        public byte Slave;
        public byte Fc;
        public bool IsException;
        public byte ExceptionCode;
        public byte[] RawFrame;       // 原始响应帧字节(含 CRC)，供回显
        public byte[] Data;            // 数据载荷（读响应的原始字节）
        public ushort[] RegisterValues; // FC03/04 解析出的寄存器值（大端）
        public bool[] Coils;            // FC01/02 解析出的线圈/离散量
        public string Error;            // 非空表示解析失败/异常
    }
}
