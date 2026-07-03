using System;
using System.Text;

namespace O_ComTool_Pro
{
    /// <summary>
    /// 统一的校验/CRC 工具集。所有方法读取 buf[0..len-1]。
    /// 算法来源：累加和/XOR/CRC16-Modbus/CRC32 来自原 Check.cs / MainForm.cs；
    /// LRC / FCS 移植自 BYSerial (MIT, https://gitee.com/LvYiWuHen/byserial) Util/StringCheck.cs。
    /// </summary>
    public static class CrcUtil
    {
        // ---------- 1 字节算法 ----------

        /// <summary>累加和（按字节求和取低 8 位）。</summary>
        public static byte Sum(byte[] buf, int len)
        {
            int sum = 0;
            for (int i = 0; i < len; i++) sum += buf[i];
            return (byte)(sum & 0xFF);
        }

        /// <summary>异或校验。</summary>
        public static byte Xor(byte[] buf, int len)
        {
            byte x = 0;
            for (int i = 0; i < len; i++) x ^= buf[i];
            return x;
        }

        /// <summary>Modbus ASCII 纵向冗余校验 LRC：字节求和后取两补码的低 8 位。</summary>
        public static byte Lrc(byte[] buf, int len)
        {
            byte lrc = 0;
            for (int i = 0; i < len; i++) lrc += buf[i];
            return (byte)((lrc ^ 0xFF) + 1);
        }

        // ---------- 2 字节算法 ----------

        /// <summary>CRC-16/Modbus（poly 0xA001 反射，init 0xFFFF），返回 16 位整数。</summary>
        public static ushort Crc16Modbus(byte[] buf, int len)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < len; i++)
            {
                crc ^= buf[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc = (ushort)(crc >> 1);
                }
            }
            return crc;
        }

        /// <summary>CRC-16/Modbus 的小端字节序列（Modbus RTU 帧尾约定：低字节在前）。</summary>
        public static byte[] Crc16ModbusBytes(byte[] buf, int len)
        {
            ushort crc = Crc16Modbus(buf, len);
            return new byte[] { (byte)(crc & 0xFF), (byte)((crc >> 8) & 0xFF) };
        }

        // ---------- 4 字节算法 ----------

        private static uint[] _crc32Table;
        private static uint[] Crc32Table
        {
            get
            {
                if (_crc32Table == null)
                {
                    uint[] t = new uint[256];
                    for (uint i = 0; i < 256; i++)
                    {
                        uint c = i;
                        for (int j = 0; j < 8; j++)
                            c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1;
                        t[i] = c;
                    }
                    _crc32Table = t;
                }
                return _crc32Table;
            }
        }

        /// <summary>CRC-32（poly 0xEDB88320 反射，init/final 0xFFFFFFFF），返回 32 位整数。</summary>
        public static uint Crc32(byte[] buf, int len)
        {
            uint value = 0xFFFFFFFF;
            uint[] t = Crc32Table;
            for (int i = 0; i < len; i++)
                value = (value >> 8) ^ t[(value & 0xFF) ^ buf[i]];
            return value ^ 0xFFFFFFFF;
        }

        // ---------- BYSerial FCS ----------
        // BYSerial 的 FCS 作用于"字符串的 ASCII 字符"：去掉空格/换行后逐字符求和取低 8 位，1 字节。
        // 与作用于解码字节的 Sum 不同，这里是字符级校验，常见于按文本帧逐字符累加的场景。
        /// <summary>BYSerial 风格 FCS：对字符串（自动去除空格/换行）的 ASCII 字符码求和取低 8 位。</summary>
        public static byte FcsOfAsciiString(string s)
        {
            if (s == null) s = "";
            s = s.Replace(" ", "").Replace("\r", "").Replace("\n", "");
            int result = 0;
            for (int i = 0; i < s.Length; i++)
                result += (int)s[i];
            return (byte)(result & 0xFF);
        }
    }
}
