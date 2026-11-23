# -*- coding: utf-8 -*-
import os
H = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
p = os.path.join(H, "site", "app.js")
s = open(p, encoding="utf-8").read()


def rep(a, b):
    global s
    assert a in s, a[:60]
    s = s.replace(a, b, 1)


rep('var byId = {}, byCat = {}, catById = {};', '''var byId = {}, byCat = {}, catById = {};
var IC = W.icons || {};
W.pages.forEach(function(p){if(p.m&&p.m.indexOf("data:")!==0)p.m=IC[p.m]||null});''')
rep('function href(id){return "#/p/"+id}', '''function href(id){return "#/p/"+id}
function fillIcons(root){$$("img[data-i]",root).forEach(function(i){var u=IC[i.getAttribute("data-i")];if(u)i.src=u;else i.style.display="none"})}''')
rep('''  var rel=[];(p.l||[]).forEach(function(i){if(byId[i])rel.push(byId[i])});
  if(rel.length){h+='<div class="rel"><b>Ссылаются сюда</b>'+rel.slice(0,12).map(function(r){return '<a href="'+href(r.id)+'">'+esc(r.t)+'</a>'}).join("")+'</div>'}''', '''  if(p.r&&p.r.length){var grp={},seen={};p.r.forEach(function(x){if(!byId[x[1]]||seen[x[0]+x[1]])return;seen[x[0]+x[1]]=1;(grp[x[0]]=grp[x[0]]||[]).push(byId[x[1]])});
    var gk=Object.keys(grp);if(gk.length){h+='<div class="rel rl"><b>Связи</b>'+gk.map(function(k){var l=grp[k];return '<div class="rg"><span class="rk">'+esc(k)+' ('+l.length+')</span>'+l.slice(0,10).map(function(r){return '<a href="'+href(r.id)+'">'+(r.m?'<img class="ci" src="'+r.m+'" alt="">':'')+esc(r.t)+'</a>'}).join("")+(l.length>10?'<span class="muted">… ещё '+(l.length-10)+'</span>':'')+'</div>'}).join("")+'</div>'}}
  var rel=[];(p.l||[]).forEach(function(i){if(byId[i])rel.push(byId[i])});
  if(rel.length){h+='<div class="rel"><b>Ссылаются сюда ('+rel.length+')</b>'+rel.slice(0,14).map(function(r){return '<a href="'+href(r.id)+'">'+esc(r.t)+'</a>'}).join("")+'</div>'}''')
rep('''<article class="article" id="art">'+(p.b||'<p class="muted">Подробного описания нет.</p>')+'</article>''', '''<article class="article" id="art">'+(p.b||(p.bl?'':'<p class="muted">Подробного описания нет.</p>'))+(p.bl?p.bl.map(function(b,i){return '<section class="blk blk'+i+'"><h2>'+esc(b[0])+'</h2>'+b[1]+'</section>'}).join(""):'')+'</article>''')
rep('  var art=$("#art");', '  var art=$("#art");fillIcons(content);')
rep("""'+m.pages+' страниц</span><span>'+m.settings+' настроек</span>""", """'+m.pages+' страниц</span><span>'+(m.icons||0)+' иконок</span><span>'+(m.recipes||0)+' рецептов</span><span>'+(m.relations||0)+' связей</span><span>'+m.settings+' настроек</span>""")
open(p, "w", encoding="utf-8").write(s)

c = os.path.join(H, "site", "style.css")
css = open(c, encoding="utf-8").read()
if "/* --- рецепты" not in css:
    css += """
/* --- рецепты, связи, блоки --- */
.blk{margin:18px 0;padding:12px 16px;border:1px solid rgba(127,127,127,.35);border-radius:10px;background:rgba(127,127,127,.06)}
.blk h2{margin:0 0 8px;font-size:1.05rem}
.blk0{border-left:4px solid #5b9bd5}.blk1{border-left:4px solid #8bb85b}.blk2{border-left:4px solid #d9a441}.blk3{border-left:4px solid #b36fd0}
.bl-h{font-weight:600;margin:10px 0 4px;font-size:.92rem;opacity:.9}
.rc{display:flex;flex-wrap:wrap;align-items:center;gap:6px;margin:6px 0;padding:6px 8px;border-radius:8px;background:rgba(127,127,127,.08)}
.rc-in{display:flex;flex-wrap:wrap;gap:6px}.rc-st{width:100%;font-size:.82rem;opacity:.85}
.arr{opacity:.6;margin:0 4px}
.chip2{display:inline-flex;align-items:center;gap:5px;padding:2px 8px 2px 3px;border-radius:16px;background:rgba(127,127,127,.14);text-decoration:none;color:inherit;font-size:.88rem;line-height:1.2}
a.chip2:hover{background:rgba(91,155,213,.3)}
.chip2.nolink{opacity:.8}
.chip2 .q{color:#d9a441}
img.ci{width:28px;height:28px;object-fit:contain;flex:none;vertical-align:middle}
.tags{display:flex;flex-wrap:wrap;gap:6px;align-items:center}
.tag{padding:1px 8px;border-radius:10px;background:rgba(127,127,127,.15);font-size:.82rem}
.rl .rg{margin:6px 0}.rl .rk{display:block;font-size:.75rem;opacity:.7;text-transform:uppercase;letter-spacing:.03em}
.rl a{display:flex;align-items:center;gap:6px}
.blk ul{margin:4px 0 4px 18px;padding:0}
"""
    open(c, "w", encoding="utf-8").write(css)
print("patched")
