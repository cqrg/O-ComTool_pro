using O_ComTool_Pro.Modbus;

namespace O_ComTool_Pro.Chart
{
    /// <summary>
    /// 从一帧数据中抽取一个双精度数值用于绘制曲线。
    /// rawFrame 为接收到的原始字节；mb 为(若处于 Modbus 模式)解析出的响应，可能为 null。
    /// 返回 null 表示本帧不产出数据点。
    /// </summary>
    public interface IValueExtractor
    {
        string Name { get; }
        double? Extract(byte[] rawFrame, ModbusResponse mb);
    }

    /// <summary>原始帧按偏移取整数/浮点，乘 scale。照 BYSerial 曲线设置 UX。</summary>
    public class RawFrameExtractor : IValueExtractor
    {
        public string Name { get; set; }
        public int Offset;          // 字节偏移(从 0 开始)
        public int DataType;        // 0=int16 1=uint16 2=int32 3=uint32 4=float32 5=uint8
        public int ByteOrder;       // 0=大端(BigEndian) 1=小端(LittleEndian)
        public double Scale;        // 乘以该系数

        public RawFrameExtractor(string name, int offset, int dataType, int byteOrder, double scale)
        {
            Name = name; Offset = offset; DataType = dataType; ByteOrder = byteOrder; Scale = scale;
        }

        public double? Extract(byte[] rawFrame, ModbusResponse mb)
        {
            if (rawFrame == null) return null;
            int size = SizeOf(DataType);
            if (rawFrame.Length < Offset + size) return null;
            byte[] p = new byte[size];
            for (int i = 0; i < size; i++) p[i] = rawFrame[Offset + i];
            if (ByteOrder == 0)
            {
                // 大端→翻转为小端便于 BitConverter
                System.Array.Reverse(p);
            }
            double v;
            switch (DataType)
            {
                case 0: v = (short)System.BitConverter.ToInt16(p, 0); break;
                case 1: v = System.BitConverter.ToUInt16(p, 0); break;
                case 2: v = System.BitConverter.ToInt32(p, 0); break;
                case 3: v = System.BitConverter.ToUInt32(p, 0); break;
                case 4: v = System.BitConverter.ToSingle(p, 0); break;
                case 5: v = p[0]; break;
                default: return null;
            }
            return v * Scale;
        }

        public static int SizeOf(int dataType)
        {
            switch (dataType)
            {
                case 0: case 1: return 2;
                case 2: case 3: return 4;
                case 4: return 4;
                case 5: return 1;
                default: return 2;
            }
        }
    }

    /// <summary>从 Modbus 响应中取指定寄存器索引的值。</summary>
    public class ModbusRegisterExtractor : IValueExtractor
    {
        public string Name { get; set; }
        public int RegisterIndex;
        public double Scale;

        public ModbusRegisterExtractor(string name, int registerIndex, double scale)
        {
            Name = name; RegisterIndex = registerIndex; Scale = scale;
        }

        public double? Extract(byte[] rawFrame, ModbusResponse mb)
        {
            if (mb == null || mb.RegisterValues == null) return null;
            if (RegisterIndex < 0 || RegisterIndex >= mb.RegisterValues.Length) return null;
            return mb.RegisterValues[RegisterIndex] * Scale;
        }
    }
}
