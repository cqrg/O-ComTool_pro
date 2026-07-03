using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using O_ComTool_Pro.Modbus;

namespace O_ComTool_Pro.Chart
{
    /// <summary>
    /// 实时曲线面板。WebView2 内联 ECharts（离线）。
    /// 支持自定义帧解析(RawFrameExtractor)与 Modbus 寄存器(ModbusRegisterExtractor)两类曲线。
    /// 移植并泛化自 S_PT_FactoryConfiguration/MainFrame.Waveform.cs（环形缓冲/自动平移/双 Y 轴/节流渲染）。
    /// </summary>
    public partial class ChartPanel : UserControl
    {
        private WebView2 _wv;
        private bool _wvReady;
        private DataGridView _dgvSeries;
        private Button _btnAddSeries;
        private Button _btnApply;
        private CheckBox _chkAutoRange;
        private TextBox _tbxYMinL, _tbxYMaxL, _tbxYMinR, _tbxYMaxR, _tbxBuffer;
        private Button _btnReset;
        private Label _lblStatus;
        private readonly List<IValueExtractor> _extractors = new List<IValueExtractor>();
        private readonly Timer _renderTimer;
        private readonly List<double?[]> _pending = new List<double?[]>();
        private string _htmlPath;

        public ChartPanel()
        {
            InitializeComponentLite();
            _renderTimer = new Timer { Interval = 300 };
            _renderTimer.Tick += RenderTimer_Tick;
        }

        // ---- MainForm 在每帧到达时调用 ----
        public void OnFrame(byte[] rawFrame, ModbusResponse mb)
        {
            if (_extractors.Count == 0) return;
            double?[] vals = new double?[_extractors.Count];
            for (int i = 0; i < _extractors.Count; i++)
                vals[i] = _extractors[i].Extract(rawFrame, mb);
            lock (_pending) { _pending.Add(vals); }
        }

        private void RenderTimer_Tick(object sender, EventArgs e)
        {
            if (!_wvReady) return;
            List<double?[]> snapshot;
            lock (_pending)
            {
                if (_pending.Count == 0) return;
                snapshot = new List<double?[]>(_pending);
                _pending.Clear();
            }
            // 把这一批合并：每条 series 取最新值，一次性推送一个点(简化：每帧一点按序推送)
            foreach (var vals in snapshot)
            {
                var sb = new System.Text.StringBuilder("addData([");
                for (int i = 0; i < vals.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(vals[i].HasValue ? vals[i].Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null");
                }
                sb.Append("])");
                try { _wv.CoreWebView2.ExecuteScriptAsync(sb.ToString()); }
                catch { /* WebView 未就绪，忽略 */ }
            }
        }

        // ---- 曲线配置与应用 ----
        private void BtnApply_Click(object sender, EventArgs e) { RebuildSeries(); }

        private void RebuildSeries()
        {
            _extractors.Clear();
            var seriesSpec = new List<KeyValuePair<string, int /*yAxis*/>>();
            foreach (DataGridViewRow row in _dgvSeries.Rows)
            {
                if (row.IsNewRow) continue;
                bool enabled = Convert.ToBoolean(row.Cells[0].Value ?? false);
                if (!enabled) continue;
                string name = Convert.ToString(row.Cells[1].Value ?? ("s" + (seriesSpec.Count + 1)));
                string src = Convert.ToString(row.Cells[2].Value ?? "Raw");
                int yAxis = Convert.ToString(row.Cells[7].Value ?? "左") == "右" ? 1 : 0;

                IValueExtractor ext;
                if (src == "Modbus")
                {
                    int regIdx = ParseInt(row.Cells[3].Value, 0);
                    double scale = ParseDouble(row.Cells[6].Value, 1.0);
                    ext = new ModbusRegisterExtractor(name, regIdx, scale);
                }
                else
                {
                    int offset = ParseInt(row.Cells[3].Value, 0);
                    int dataType = ParseInt(row.Cells[4].Value, 1);
                    int order = Convert.ToString(row.Cells[5].Value ?? "大端") == "小端" ? 1 : 0;
                    double scale = ParseDouble(row.Cells[6].Value, 1.0);
                    ext = new RawFrameExtractor(name, offset, dataType, order, scale);
                }
                _extractors.Add(ext);
                seriesSpec.Add(new KeyValuePair<string, int>(name, yAxis));
            }
            LoadChartHtml(seriesSpec);
            _renderTimer.Stop();
            if (seriesSpec.Count > 0) _renderTimer.Start();
            _lblStatus.Text = "已应用 " + seriesSpec.Count + " 条曲线";
        }

        private static int ParseInt(object v, int def) { int r; return int.TryParse(Convert.ToString(v ?? ""), out r) ? r : def; }
        private static double ParseDouble(object v, double def) { double r; return double.TryParse(Convert.ToString(v ?? ""), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out r) ? r : def; }

        private void LoadChartHtml(List<KeyValuePair<string, int>> seriesSpec)
        {
            string echarts = ReadEchartsResource();
            if (echarts == null) { _lblStatus.Text = "echarts.min.js 资源缺失"; return; }
            string seriesJs = BuildSeriesJs(seriesSpec);
            string html = "<!DOCTYPE html><html><head><meta charset='utf-8'>"
                + "<script>" + echarts + "</script>"
                + "<style>*{margin:0;padding:0;box-sizing:border-box}html,body,#chart{width:100%;height:100%}</style>"
                + "</head><body><div id='chart'></div><script>"
                + "var c=echarts.init(document.getElementById('chart'));"
                + "var MAX=" + Math.Max(600, ParseIntBuf(_tbxBuffer.Text, 3000)) + ",seq=0,userDragged=false;"
                + "var arrs=[];" // 每条 series 一个环形数组
                + seriesJs
                + @"function autoPan(){if(userDragged)return;var n=0;for(var i=0;i<arrs.length;i++)n=Math.max(n,arrs[i].length);if(n===0)return;var view=Math.min(300,n);var end=seq;var start=Math.max(end-view+1,0);c.dispatchAction({type:'dataZoom',startValue:start,endValue:end});}
function render(){var s=[];for(var i=0;i<arrs.length;i++)s.push({id:'s'+i,data:arrs[i]});c.setOption({series:s},{replaceMerge:['series']});autoPan();}
var t=null;window.addData=function(vals){seq++;for(var i=0;i<arrs.length&&i<vals.length;i++){var v=vals[i];arrs[i].push([seq,v==null?null:v]);if(arrs[i].length>MAX)arrs[i].shift();}if(!t){t=setTimeout(function(){render();t=null;},300);}};
c.on('dataZoom',function(p){if(p.batch)userDragged=true;});
window.resetChart=function(){seq=0;userDragged=false;for(var i=0;i<arrs.length;i++)arrs[i]=[];c.clear();c.setOption({},true);buildOption();};
window.addEventListener('resize',function(){c.resize();});
buildOption();"
                + "</script></body></html>";

            // 写临时文件用 NavigateToFile（避免 NavigateToString 对 ~1MB 内联脚本的体积限制）
            try
            {
                _htmlPath = Path.Combine(Path.GetTempPath(), "ocomtool_chart.html");
                File.WriteAllText(_htmlPath, html, System.Text.Encoding.UTF8);
                if (_wvReady && _wv.CoreWebView2 != null) { _wv.CoreWebView2.Navigate(_htmlPath); userResetFlags(); }
            }
            catch (Exception ex) { _lblStatus.Text = "写图表临时文件失败：" + ex.Message; }
        }

        private void userResetFlags() { try { _wv.CoreWebView2.ExecuteScriptAsync("userDragged=false"); } catch { } }

        private static int ParseIntBuf(string s, int def) { int r; return int.TryParse(s, out r) && r >= 600 ? r : def; }

        private string BuildSeriesJs(List<KeyValuePair<string, int>> seriesSpec)
        {
            // 初始化 arrs[] + 颜色 + yAxisIndex；并定义 buildOption() 构造图例/双 Y 轴/series/dataZoom
            var sb = new System.Text.StringBuilder();
            string[] palette = new string[] { "#0096c8", "#dc6428", "#7cb342", "#ab47bc", "#fdd835", "#26a69a", "#ec407a", "#5c6bc0" };
            for (int i = 0; i < seriesSpec.Count; i++)
            {
                string color = palette[i % palette.Length];
                sb.Append("arrs.push([]);");
                // 把元数据存到 arrs 上不便，这里直接在 buildOption 里按 i 取
            }
            // buildOption
            sb.Append("function buildOption(){var names=[");
            for (int i = 0; i < seriesSpec.Count; i++) { if (i > 0) sb.Append(','); sb.Append("'" + JsEscape(seriesSpec[i].Key) + "'"); }
            sb.Append("];var yidx=[");
            for (int i = 0; i < seriesSpec.Count; i++) { if (i > 0) sb.Append(','); sb.Append(seriesSpec[i].Value); }
            sb.Append(@"];var palette=['#0096c8','#dc6428','#7cb342','#ab47bc','#fdd835','#26a69a','#ec407a','#5c6bc0'];var legend=[];var series=[];for(var i=0;i<names.length;i++){var col=palette[i%palette.length];legend.push({name:names[i],itemStyle:{color:col}});series.push({id:'s'+i,name:names[i],type:'line',yAxisIndex:yidx[i],data:arrs[i],symbol:'none',animation:false,lineStyle:{color:col,width:1.5}});}c.setOption({legend:{data:legend,top:2,left:'center',textStyle:{fontSize:10},itemWidth:20,itemHeight:2,itemGap:10,icon:'rect'},grid:{left:4,right:4,top:22,bottom:24,containLabel:true},xAxis:{type:'value',axisLabel:{show:false},axisLine:{show:false},axisTick:{show:false},splitLine:{show:false}},yAxis:[{type:'value',name:'左',splitLine:{lineStyle:{color:'#eee',type:'dashed'}}},{type:'value',name:'右',splitLine:{show:false}}],dataZoom:[{type:'inside',xAxisIndex:0},{type:'slider',xAxisIndex:0,bottom:4,height:16}],series:series});}");
            return sb.ToString();
        }

        private static string JsEscape(string s) { return (s ?? "").Replace("'", "\\'").Replace("\r", "").Replace("\n", " "); }

        private static string ReadEchartsResource()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("O_ComTool_Pro.echarts.min.js"))
                {
                    if (s == null) return null;
                    using (StreamReader r = new StreamReader(s, System.Text.Encoding.UTF8))
                        return r.ReadToEnd();
                }
            }
            catch { return null; }
        }

        private void InitWebView()
        {
            _wv = new WebView2 { Dock = DockStyle.Fill };
            _wv.CoreWebView2InitializationCompleted += (s, e) =>
            {
                if (!e.IsSuccess) { _lblStatus.Text = "WebView2 初始化失败（请确认系统已安装 WebView2 运行时）"; return; }
                _wv.CoreWebView2.Settings.IsZoomControlEnabled = false;
                _wvReady = true;
                RebuildSeries();
            };
            _pChartHolder.Controls.Add(_wv);
            try { _wv.EnsureCoreWebView2Async(null); } catch (Exception ex) { _lblStatus.Text = "WebView2 不可用：" + ex.Message; }
        }

        private Panel _pChartHolder;

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            InitWebView();
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            if (_wvReady) { try { _wv.CoreWebView2.ExecuteScriptAsync("resetChart()"); } catch { } }
        }

        private void InitializeComponentLite()
        {
            this.SuspendLayout();
            Font f = new Font("微软雅黑", 9F);

            Panel top = new Panel { Location = new Point(0, 0), Size = new Size(760, 28), BackColor = Color.White, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _chkAutoRange = new CheckBox { Text = "Y自适应", Location = new Point(6, 4), Size = new Size(78, 20), Font = f, Checked = true };
            top.Controls.Add(_chkAutoRange);
            // 简化：量程/缓存控件留作占位，初版仅保留自适应与重置
            _btnReset = new Button { Text = "重置", Location = new Point(90, 3), Size = new Size(56, 22), Font = f };
            _btnReset.Click += BtnReset_Click;
            top.Controls.Add(_btnReset);
            _lblStatus = new Label { Location = new Point(160, 6), Size = new Size(400, 18), ForeColor = Color.DimGray, Font = f, Text = "状态：就绪" };
            top.Controls.Add(_lblStatus);

            Panel cfg = new Panel { Location = new Point(0, 30), Size = new Size(760, 120), BackColor = Color.WhiteSmoke, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _dgvSeries = new DataGridView
            {
                Location = new Point(4, 4), Size = new Size(700, 90),
                AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, Font = f,
                Columns =
                {
                    new DataGridViewCheckBoxColumn { Name="Enable", HeaderText="启用", Width=46 },
                    new DataGridViewTextBoxColumn { Name="Name", HeaderText="名称", Width=80 },
                    new DataGridViewComboBoxColumn { Name="Src", HeaderText="来源", Width=70, Items={"Raw","Modbus"} },
                    new DataGridViewTextBoxColumn { Name="Offset", HeaderText="偏移/寄存器", Width=80 },
                    new DataGridViewComboBoxColumn { Name="DType", HeaderText="数据类型", Width=90, Items={"int16","uint16","int32","uint32","float32","uint8"} },
                    new DataGridViewComboBoxColumn { Name="Order", HeaderText="字节序", Width=60, Items={"大端","小端"} },
                    new DataGridViewTextBoxColumn { Name="Scale", HeaderText="scale", Width=50 },
                    new DataGridViewComboBoxColumn { Name="YAxis", HeaderText="Y轴", Width=54, Items={"左","右"} },
                }
            };
            cfg.Controls.Add(_dgvSeries);
            _btnApply = new Button { Text = "应用", Location = new Point(708, 4), Size = new Size(48, 22), Font = f };
            _btnApply.Click += BtnApply_Click;
            cfg.Controls.Add(_btnApply);
            Label hint = new Label { Location = new Point(708, 32), Size = new Size(48, 80), Font = f, Text = "说明:\nRaw=偏移\nModbus=\n寄存器" };
            cfg.Controls.Add(hint);

            _pChartHolder = new Panel { Location = new Point(0, 154), Size = new Size(760, 250), BackColor = Color.White, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };

            Controls.Add(top);
            Controls.Add(cfg);
            Controls.Add(_pChartHolder);
            this.AutoScaleDimensions = new SizeF(6F, 12F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.BackColor = Color.White;
            this.Size = new Size(760, 408);
            this.ResumeLayout(false);
        }
    }
}
