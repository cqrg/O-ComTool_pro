using System;
using System.Drawing;
using System.Windows.Forms;

namespace O_ComTool_Pro.Modbus
{
    /// <summary>
    /// 主机侧 Modbus RTU 调试面板。在 tbpModbus 中宿主。
    /// 通过 IModbusTransport 发送组好的帧；订阅 ResponseReceived 刷新表格/异常。
    /// </summary>
    public partial class ModbusPanel : UserControl
    {
        private NumericUpDown nudSlave;
        private ComboBox cmbFunction;
        private NumericUpDown nudAddr;
        private NumericUpDown nudQty;
        private TextBox txbWriteValues;
        private CheckBox chkPoll;
        private NumericUpDown nudPollMs;
        private Button btnSend;
        private CheckBox chkModbusMode;
        private DataGridView dgvResult;
        private Label lblStatus;
        private Label lblWriteHint;
        private readonly Timer _pollTimer = new Timer();

        private IModbusTransport _transport;

        // 功能码下拉项文本
        private static readonly string[] _fcItems = new string[] {
            "01 读线圈",
            "02 读离散输入",
            "03 读保持寄存器",
            "04 读输入寄存器",
            "05 写单个线圈",
            "06 写单个寄存器",
            "0F 写多个线圈",
            "10 写多个寄存器",
        };

        public ModbusPanel()
        {
            InitializeComponentLite();
            _pollTimer.Tick += PollTimer_Tick;
        }

        /// <summary>绑定传输层并订阅响应。MainForm 在创建面板后调用。</summary>
        public void Bind(IModbusTransport transport)
        {
            _transport = transport;
            if (_transport != null)
            {
                _transport.ResponseReceived += Transport_ResponseReceived;
                _transport.StatusChanged += Transport_StatusChanged;
            }
        }

        private void Transport_StatusChanged(string text)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { this.BeginInvoke((Action)(() => { lblStatus.Text = text; lblStatus.ForeColor = Color.DarkOrange; })); }
            catch (InvalidOperationException) { }
        }

        private void Transport_ResponseReceived(ModbusResponse r)
        {
            // 后台线程：切回 UI 线程刷新
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                this.BeginInvoke((Action)(() => ShowResponse(r)));
            }
            catch (InvalidOperationException) { }
        }

        private void ShowResponse(ModbusResponse r)
        {
            if (r == null) return;
            if (!string.IsNullOrEmpty(r.Error))
            {
                lblStatus.Text = "状态：" + r.Error;
                lblStatus.ForeColor = Color.Red;
                return;
            }
            lblStatus.Text = "状态：OK  从机 " + r.Slave + "  FC 0x" + r.Fc.ToString("X2");
            lblStatus.ForeColor = Color.Green;

            // 读响应：填充表格
            if (r.RegisterValues != null)
            {
                dgvResult.Rows.Clear();
                for (int i = 0; i < r.RegisterValues.Length; i++)
                    dgvResult.Rows.Add(i, "0x" + r.RegisterValues[i].ToString("X4"), r.RegisterValues[i].ToString());
            }
            else if (r.Coils != null)
            {
                dgvResult.Rows.Clear();
                for (int i = 0; i < r.Coils.Length; i++)
                    dgvResult.Rows.Add(i, r.Coils[i] ? "1" : "0", r.Coils[i] ? "ON" : "OFF");
            }
        }

        private byte SelectedFc()
        {
            int idx = cmbFunction.SelectedIndex;
            byte[] fcs = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x0F, 0x10 };
            return fcs[idx < 0 || idx >= fcs.Length ? 2 : idx];
        }

        private bool IsReadFc(byte fc) { return fc == 0x01 || fc == 0x02 || fc == 0x03 || fc == 0x04; }

        private void btnSend_Click(object sender, EventArgs e) { SendOnce(); }

        private void SendOnce()
        {
            if (_transport == null) return;
            byte slave = (byte)nudSlave.Value;
            ushort addr = (ushort)nudAddr.Value;
            byte fc = SelectedFc();

            // 进入 Modbus 模式（首次发送时勾选）
            if (!chkModbusMode.Checked) chkModbusMode.Checked = true;

            byte[] frame;
            if (fc == 0x03 || fc == 0x04 || fc == 0x01 || fc == 0x02)
            {
                frame = ModbusMaster.BuildRead(slave, fc, addr, (ushort)nudQty.Value);
            }
            else if (fc == 0x05)
            {
                ushort v = ParseSingleWriteValue();
                frame = ModbusMaster.BuildWriteSingle(slave, fc, addr, v);
            }
            else if (fc == 0x06)
            {
                ushort v = ParseSingleWriteValue();
                frame = ModbusMaster.BuildWriteSingle(slave, fc, addr, v);
            }
            else if (fc == 0x10)
            {
                ushort[] vals = ParseMultiWriteValues();
                frame = ModbusMaster.BuildWriteMultiRegs(slave, addr, vals);
            }
            else // 0x0F
            {
                bool[] coils = ParseMultiCoils();
                frame = ModbusMaster.BuildWriteMultiCoils(slave, addr, coils);
            }

            lblStatus.Text = "状态：发送 " + frame.Length + " 字节...";
            lblStatus.ForeColor = Color.DimGray;
            _transport.Send(frame, fc);
        }

        private ushort ParseSingleWriteValue()
        {
            ushort v;
            ushort.TryParse(txbWriteValues.Text.Trim(), System.Globalization.NumberStyles.HexNumber, null, out v);
            return v;
        }

        private ushort[] ParseMultiWriteValues()
        {
            // 接受 "0x1234, 0xABCD ; 0010" 等分隔
            string raw = txbWriteValues.Text.Replace("0x", "").Replace("0X", "");
            string[] parts = raw.Split(new char[] { ',', ';', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            System.Collections.Generic.List<ushort> list = new System.Collections.Generic.List<ushort>();
            foreach (string p in parts)
            {
                ushort v;
                if (ushort.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out v)) list.Add(v);
            }
            return list.ToArray();
        }

        private bool[] ParseMultiCoils()
        {
            string raw = txbWriteValues.Text;
            System.Collections.Generic.List<bool> list = new System.Collections.Generic.List<bool>();
            foreach (char c in raw)
            {
                if (c == '1') list.Add(true);
                else if (c == '0') list.Add(false);
            }
            return list.ToArray();
        }

        private void chkPoll_CheckedChanged(object sender, EventArgs e)
        {
            if (chkPoll.Checked)
            {
                int ms = (int)Math.Max(50, nudPollMs.Value);
                _pollTimer.Interval = ms;
                _pollTimer.Start();
            }
            else
            {
                _pollTimer.Stop();
            }
        }

        private void PollTimer_Tick(object sender, EventArgs e)
        {
            if (_transport == null || !_transport.IsOpen) return;
            SendOnce();
        }

        private void cmbFunction_SelectedIndexChanged(object sender, EventArgs e)
        {
            byte fc = SelectedFc();
            bool isRead = IsReadFc(fc);
            nudQty.Enabled = isRead;
            txbWriteValues.Enabled = !isRead;
            lblWriteHint.Text = isRead
                ? "读操作：设置起始地址与数量"
                : (fc == 0x05 || fc == 0x06
                    ? "写单个：1 个值(HEX，如 0001 或 FF00)"
                    : "写多个：以 空格/逗号/分号 分隔(HEX)；线圈用 0/1 序列");
        }

        private void chkModbusMode_CheckedChanged(object sender, EventArgs e)
        {
            if (_transport != null) _transport.SetModbusMode(chkModbusMode.Checked);
            lblStatus.Text = chkModbusMode.Checked ? "状态：Modbus 模式已开启" : "状态：Modbus 模式已关闭";
        }

        private void InitializeComponentLite()
        {
            this.SuspendLayout();
            Font f = new Font("微软雅黑", 9F);

            // 用委托局部(非本地函数，C#5 兼容)批量造标签
            Func<string, int, int, int, Label> L = (t, x, y, w) => new Label { Text = t, Location = new Point(x, y), Size = new Size(w, 20), Font = f };

            Controls.Add(L("从机", 8, 8, 36));
            nudSlave = new NumericUpDown { Location = new Point(46, 6), Size = new Size(48, 22), Minimum = 1, Maximum = 247, Value = 1, Font = f };
            Controls.Add(nudSlave);

            Controls.Add(L("功能码", 104, 8, 50));
            cmbFunction = new ComboBox { Location = new Point(156, 6), Size = new Size(150, 22), DropDownStyle = ComboBoxStyle.DropDownList, Font = f };
            cmbFunction.Items.AddRange(_fcItems);
            cmbFunction.SelectedIndex = 2;
            Controls.Add(cmbFunction);

            Controls.Add(L("起始地址", 316, 8, 60));
            nudAddr = new NumericUpDown { Location = new Point(378, 6), Size = new Size(64, 22), Maximum = 65535, Value = 0, Font = f, Hexadecimal = true };
            Controls.Add(nudAddr);

            Controls.Add(L("数量", 450, 8, 36));
            nudQty = new NumericUpDown { Location = new Point(486, 6), Size = new Size(50, 22), Minimum = 1, Maximum = 125, Value = 1, Font = f };
            Controls.Add(nudQty);

            btnSend = new Button { Text = "发送请求", Location = new Point(546, 5), Size = new Size(80, 24), Font = f };
            Controls.Add(btnSend);

            Controls.Add(L("写值/值序列", 8, 40, 80));
            txbWriteValues = new TextBox { Location = new Point(90, 38), Size = new Size(300, 22), Font = f, Enabled = false };
            Controls.Add(txbWriteValues);
            lblWriteHint = new Label { Location = new Point(398, 40), Size = new Size(360, 20), ForeColor = Color.Gray, Font = f, Text = "" };
            Controls.Add(lblWriteHint);

            chkPoll = new CheckBox { Text = "轮询", Location = new Point(8, 70), Size = new Size(56, 20), Font = f };
            Controls.Add(chkPoll);
            Controls.Add(L("间隔ms", 70, 72, 48));
            nudPollMs = new NumericUpDown { Location = new Point(118, 70), Size = new Size(60, 22), Minimum = 50, Maximum = 60000, Value = 1000, Font = f };
            Controls.Add(nudPollMs);
            chkModbusMode = new CheckBox { Text = "Modbus 解析模式", Location = new Point(200, 70), Size = new Size(140, 20), Font = f };
            Controls.Add(chkModbusMode);

            dgvResult = new DataGridView
            {
                Location = new Point(8, 100),
                Size = new Size(740, 180),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Font = f,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            dgvResult.Columns.Add("cIdx", "序号");
            dgvResult.Columns.Add("cHex", "HEX");
            dgvResult.Columns.Add("cDec", "DEC/状态");
            Controls.Add(dgvResult);

            lblStatus = new Label { Location = new Point(8, 286), Size = new Size(740, 20), Font = f, Text = "状态：就绪", Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            Controls.Add(lblStatus);

            // 事件
            btnSend.Click += btnSend_Click;
            chkPoll.CheckedChanged += chkPoll_CheckedChanged;
            cmbFunction.SelectedIndexChanged += cmbFunction_SelectedIndexChanged;
            chkModbusMode.CheckedChanged += chkModbusMode_CheckedChanged;
            cmbFunction_SelectedIndexChanged(null, null);

            this.AutoScaleDimensions = new SizeF(6F, 12F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = Color.White;
            this.Size = new Size(756, 312);
            this.ResumeLayout(false);
        }
    }
}
