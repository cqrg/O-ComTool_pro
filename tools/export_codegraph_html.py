#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""从 CodeGraph 的 SQLite 库导出自包含 HTML 交互式图谱。"""
import sqlite3, json, html, os

DB = ".codegraph/codegraph.db"
OUT = "codegraph.html"

c = sqlite3.connect(DB)
nodes_raw = c.execute(
    "SELECT id, kind, name, qualified_name, file_path, start_line, signature, docstring "
    "FROM nodes"
).fetchall()
edges_raw = c.execute("SELECT source, target, kind FROM edges").fetchall()
c.close()

node_ids = {r[0] for r in nodes_raw}
nodes = []
for nid, kind, name, qn, fp, line, sig, doc in nodes_raw:
    nodes.append({
        "id": nid, "kind": kind, "name": name,
        "label": name, "file": fp, "line": line or 0,
        "sig": sig or "", "doc": (doc or "")[:200],
    })
edges = []
seen = set()
for s, t, k in edges_raw:
    if s not in node_ids or t not in node_ids:
        continue
    key = (s, t, k)
    if key in seen:
        continue
    seen.add(key)
    edges.append({"from": s, "to": t, "kind": k})

data = {"nodes": nodes, "edges": edges}
json_str = json.dumps(data, ensure_ascii=False)

# 节点类型 → 颜色
KIND_COLOR = {
    "class":     "#3b82f6",
    "method":    "#10b981",
    "function":  "#10b981",
    "constructor":"#22c55e",
    "interface": "#a855f7",
    "struct":    "#ec4899",
    "property":  "#f59e0b",
    "field":     "#94a3b8",
    "import":    "#64748b",
    "file":      "#cbd5e1",
}
EDGE_COLOR = {
    "calls":         "#ef4444",
    "implements":    "#a855f7",
    "instantiates":  "#f97316",
    "contains":      "#cbd5e1",
    "imports":       "#94a3b8",
}

HTML = """<!DOCTYPE html>
<html lang="zh-CN"><head><meta charset="utf-8">
<title>O-ComTool CodeGraph 图谱</title>
__VISLIB__
<style>
*{box-sizing:border-box}
html,body{height:100%;margin:0;font-family:"Microsoft YaHei",sans-serif;background:#0f172a;color:#e2e8f0}
#toolbar{position:fixed;top:8px;left:8px;z-index:10;background:#1e293b;padding:8px 10px;border-radius:8px;display:flex;gap:8px;align-items:center;flex-wrap:wrap;box-shadow:0 2px 8px rgba(0,0,0,.4)}
#toolbar select,#toolbar input{background:#0f172a;color:#e2e8f0;border:1px solid #334155;border-radius:4px;padding:3px 6px}
#toolbar button{background:#3b82f6;color:#fff;border:0;border-radius:4px;padding:4px 10px;cursor:pointer}
#net{width:100%;height:100%}
#detail{position:fixed;top:8px;right:8px;z-index:10;width:320px;max-height:90vh;overflow:auto;background:#1e293b;padding:10px 12px;border-radius:8px;display:none;font-size:13px;line-height:1.5;box-shadow:0 2px 8px rgba(0,0,0,.4)}
#detail h3{margin:0 0 6px;color:#60a5fa}
#detail .k{color:#94a3b8}
.legend{display:inline-flex;gap:6px;align-items:center;margin-left:6px}
.legend i{width:10px;height:10px;border-radius:50%;display:inline-block}
#stats{position:fixed;bottom:8px;left:8px;z-index:10;background:#1e293b;padding:4px 10px;border-radius:6px;font-size:12px;color:#94a3b8}
</style></head><body>
<div id="toolbar">
  <label>范围
    <select id="scope">
      <option value="core">类 / 方法 / 接口</option>
      <option value="all">全部符号</option>
      <option value="class">仅类与结构</option>
    </select>
  </label>
  <label>边
    <select id="ekind">
      <option value="call">调用/实现/实例化</option>
      <option value="all">全部(含 contains)</option>
    </select>
  </label>
  <input id="search" placeholder="搜索符号名..." style="width:160px">
  <button onclick="resetView()">重置</button>
  <span class="legend">
    <span class="legend"><i style="background:#3b82f6"></i>类</span>
    <span class="legend"><i style="background:#10b981"></i>方法</span>
    <span class="legend"><i style="background:#a855f7"></i>接口</span>
    <span class="legend"><i style="background:#ef4444"></i>调用</span>
  </span>
</div>
<div id="detail"></div>
<div id="net"></div>
<div id="stats"></div>
<script>
const DATA = __DATA__;
const KIND_COLOR = __KIND_COLOR__;
const EDGE_COLOR = __EDGE_COLOR__;
const SCOPE_KINDS = { core:new Set(["class","method","function","constructor","interface","struct"]),
                      all:new Set(["class","method","function","constructor","interface","struct","property","field","import","file"]),
                      class:new Set(["class","struct","interface"]) };
let network=null, allNodes=null, allEdges=null;

function build(){
  const scope = document.getElementById('scope').value;
  const ek = document.getElementById('ekind').value;
  const q = document.getElementById('search').value.trim().toLowerCase();
  const keep = SCOPE_KINDS[scope];
  const edgeKinds = (ek==='all') ? null : new Set(['calls','implements','instantiates']);
  // 节点过滤
  let nodeIds = new Set();
  let nodes = DATA.nodes.filter(n=>{
    if(!keep.has(n.kind)) return false;
    if(q && !(n.name.toLowerCase().includes(q)|| (n.file||'').toLowerCase().includes(q))) return false;
    nodeIds.add(n.id); return true;
  });
  // 边过滤
  let edges = DATA.edges.filter(e=>{
    if(!nodeIds.has(e.from)||!nodeIds.has(e.to)) return false;
    if(edgeKinds && !edgeKinds.has(e.kind)) return false;
    return true;
  });
  // 仅保留有边相连的节点(减少孤立点)，除非在搜索
  if(!q){
    const used=new Set();
    edges.forEach(e=>{used.add(e.from);used.add(e.to)});
    nodes=nodes.filter(n=>used.has(n.id));
  }
  const visNodes = nodes.map(n=>({
    id:n.id, label:n.name, title:n.kind+'  '+n.name+'\\n'+(n.file||'')+':'+n.line,
    group:n.kind, color:{background:KIND_COLOR[n.kind]||'#888',border:'#0f172a'},
    font:{color:'#e2e8f0',size:13}, shape:'dot',
    size: n.kind==='class'||n.kind==='interface'?14: (n.kind==='method'?9:7),
    _n:n
  }));
  const visEdges = edges.map(e=>({
    from:e.from,to:e.to, color:{color:EDGE_COLOR[e.kind]||'#475569',opacity:0.5},
    arrows: e.kind==='contains'?undefined:{to:{enabled:true,scaleFactor:0.4}},
    width: e.kind==='calls'?1.5:0.6, hidden:false
  }));
  allNodes=new vis.DataSet(visNodes); allEdges=new vis.DataSet(visEdges);
  if(network){network.destroy();network=null;}
  network=new vis.Network(document.getElementById('net'),{nodes:allNodes,edges:allEdges},{
    nodes:{borderWidth:1}, edges:{smooth:{type:'continuous'}},
    physics:{solver:'barnesHut',barnesHut:{gravitationalConstant:-8000,springLength:120,damping:0.4},stabilization:{iterations:120}},
    interaction:{hover:true,zoomView:true}
  });
  network.on('click',p=>{
    const d=document.getElementById('detail');
    if(!p.nodes.length){d.style.display='none';return;}
    const nd=allNodes.get(p.nodes[0])._n;
    d.style.display='block';
    d.innerHTML='<h3>'+nd.name+'</h3>'+
      '<div><span class="k">类型:</span> '+nd.kind+'</div>'+
      '<div><span class="k">文件:</span> '+nd.file+'</div>'+
      '<div><span class="k">行:</span> '+nd.line+'</div>'+
      (nd.sig?'<div style="margin-top:6px"><pre style="white-space:pre-wrap;background:#0f172a;padding:6px;border-radius:4px;margin:0">'+escapeHtml(nd.sig)+'</pre></div>':'')+
      (nd.doc?'<div style="margin-top:6px;color:#94a3b8">'+escapeHtml(nd.doc)+'</div>':'');
  });
  document.getElementById('stats').textContent='节点 '+nodes.length+' / 边 '+edges.length+' (库共 '+DATA.nodes.length+' 节点, '+DATA.edges.length+' 边)';
}
function escapeHtml(s){return (s||'').replace(/[&<>]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));}
function resetView(){
  document.getElementById('scope').value='core';
  document.getElementById('ekind').value='call';
  document.getElementById('search').value='';
  build();
}
document.getElementById('scope').onchange=build;
document.getElementById('ekind').onchange=build;
let t=null; document.getElementById('search').oninput=()=>{clearTimeout(t);t=setTimeout(build,250)};
build();
</script></body></html>
"""

html_str = (HTML
            .replace("__DATA__", json_str)
            .replace("__KIND_COLOR__", json.dumps(KIND_COLOR))
            .replace("__EDGE_COLOR__", json.dumps(EDGE_COLOR)))

# 内联 vis-network 库（自包含、离线可用、无 CDN/SRI 风险）
lib_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "vis-network.min.js")
with open(lib_path, "r", encoding="utf-8") as f:
    vislib = f.read()
html_str = html_str.replace("__VISLIB__", "<script>" + vislib + "</script>")

with open(OUT, "w", encoding="utf-8") as f:
    f.write(html_str)
print("written", OUT, os.path.getsize(OUT), "bytes;",
      len(nodes), "nodes,", len(edges), "edges")
