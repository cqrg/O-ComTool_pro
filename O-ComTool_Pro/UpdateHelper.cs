using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace O_ComTool_Pro
{

    public class UpdateHelper
    {
        public struct check_value
        {
            public bool valid;
            public string version;
            public string link;
            public string feature;
        }

        /// <summary>
        /// 校验更新链接后启动默认处理器。
        /// 仅允许 http/https 方案，防止被篡改的 XML 把 link 设成可执行文件路径 / file:// / UNC 等造成命令执行。
        /// 返回是否成功启动。
        /// </summary>
        public static bool StartUpdateLink(string link)
        {
            if (!IsSafeUrl(link))
            {
                MessageBox.Show("更新链接不合法，已拒绝打开：\r\n" + (link ?? "(空)"),
                    "O-ComTool 安全提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                Process.Start(link);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开更新链接失败：" + ex.Message,
                    "O-ComTool 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        /// 仅接受 http/https 绝对 URI。Process.Start(string) 会按系统默认处理器解析，
        /// 因此必须拒绝一切非 web 方案，避免本地文件/可执行路径被当作命令启动。
        /// </summary>
        static bool IsSafeUrl(string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return false;
            Uri uri;
            if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out uri)) return false;
            string scheme = uri.Scheme.ToLowerInvariant();
            return scheme == "http" || scheme == "https";
        }

        public static check_value check_update(string url)
        {
            check_value ret_value;
            ret_value.version = "";
            ret_value.link = "";
            ret_value.feature = "";
            ret_value.valid = false;
            
            try
            {
                // 关闭 DTD/外部实体解析，防止 XXE 与外部引用（本地文件泄露 / SSRF）
                XmlDocument ver_xml = new XmlDocument { XmlResolver = null };
                XmlReaderSettings readerSettings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (XmlReader reader = XmlReader.Create(url + "/check_version.xml", readerSettings))
                {
                    ver_xml.Load(reader);
                }
                XmlNode ver = ver_xml.SelectSingleNode("checkupdate");
                foreach (XmlNode node in ver)
                {
                    XmlNode verid = ver_xml.SelectSingleNode("checkupdate/version");
                    if (node.Name == "version")
                    {
                        ret_value.version = node.InnerText;
                    }
                    if (node.Name == "link")
                    {
                        ret_value.link = node.InnerText;
                    }
                    if (verid.InnerText == Application.ProductVersion.Substring(0, 5))
                    {
                        ret_value.feature = "已经是最新版本啦！\r\n";
                    }
                    if (node.Name == "feature")
                    {
                        ret_value.feature += node.InnerText;
                    }
                }
                ret_value.valid = true;
                return ret_value;
            }
            catch
            {
                return ret_value;
            }
        }
    }
}
