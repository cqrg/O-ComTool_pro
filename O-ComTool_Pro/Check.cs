using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace O_ComTool_Pro
{
    public partial class Check : Form
    {
        public Check()
        {
            InitializeComponent();
            cmbWaySelect.SelectedIndex = 0;
        }

        byte CheckSum(byte[] buffer, uint length) { return CrcUtil.Sum(buffer, (int)length); }

        byte XOR(byte[] buffer, uint length) { return CrcUtil.Xor(buffer, (int)length); }

        public uint crc16_modbus(byte[] modbusdata, uint length) { return CrcUtil.Crc16Modbus(modbusdata, (int)length); }

        ulong[] Crc32Table = new ulong[256];
        public void GetCRC32Table() { /* 保留空实现以兼容旧调用；CRC32 表已由 CrcUtil 内部懒加载 */ }

        ulong crc32_calc(byte[] data, uint len) { return CrcUtil.Crc32(data, (int)len); }

        private void btnCheck_Click(object sender, EventArgs e)
        {
            uint i = 0;
            MatchCollection mc = Regex.Matches(txtData.Text, @"(?i)[\da-f]{2}");
            byte[] bytesToCheck = new byte[mc.Count];
            foreach (Match m in mc)//遍历所有mc，并将其转换成十六进制
            {
                bytesToCheck[i++] = byte.Parse(m.Value, System.Globalization.NumberStyles.HexNumber);//赋值并累加
            }
            switch (cmbWaySelect.SelectedIndex)
            {
                case 0:
                    txtResault.Text = "0x" + CheckSum(bytesToCheck, i).ToString("X2");
                    break;
                case 1:
                    txtResault.Text = "0x" + XOR(bytesToCheck, i).ToString("X2");
                    break;
                case 2:
                    txtResault.Text = "0x" + crc16_modbus(bytesToCheck, i).ToString("X4");
                    break;
                case 3:
                    txtResault.Text = "0x" + crc32_calc(bytesToCheck, i).ToString("X8");
                    break;

            }
        }

    }
}
