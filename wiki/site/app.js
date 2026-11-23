(function(){
"use strict";
var W = window.WIKI;
var $ = function(s,r){return (r||document).querySelector(s)};
var $$ = function(s,r){return Array.prototype.slice.call((r||document).querySelectorAll(s))};
var esc = function(s){return String(s==null?"":s).replace(/[&<>"]/g,function(c){return {"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;"}[c]})};
var byId = {}, byCat = {}, catById = {};
var IC = W.icons || {};
W.pages.forEach(function(p){if(p.m&&p.m.indexOf("data:")!==0)p.m=IC[p.m]||null});
W.pages.forEach(function(p){byId[p.id]=p;(byCat[p.c]=byCat[p.c]||[]).push(p)});
W.cats.forEach(function(c){catById[c.id]=c});
var content = $("#content"), side = $("#side");

/* ---------- theme ---------- */
try{var th=localStorage.getItem("wiki-theme");if(th)document.documentElement.setAttribute("data-theme",th)}catch(e){}
$("#theme").onclick=function(){
  var cur=document.documentElement.getAttribute("data-theme");
  var dark=cur?cur==="dark":window.matchMedia("(prefers-color-scheme: dark)").matches;
  var nx=dark?"light":"dark";document.documentElement.setAttribute("data-theme",nx);
  try{localStorage.setItem("wiki-theme",nx)}catch(e){}
};
$("#menu").onclick=function(){document.body.classList.toggle("menu")};

/* ---------- helpers ---------- */
function sortRu(a,b){return a.t.localeCompare(b.t,"ru")}
function catPages(id){return (byCat[id]||[]).slice().sort(sortRu)}
function href(id){return "#/p/"+id}
function fillIcons(root){$$("img[data-i]",root).forEach(function(i){var u=IC[i.getAttribute("data-i")];if(u)i.src=u;else i.style.display="none"})}
function parseHash(){
  var h=location.hash.replace(/^#/,"")||"/";
  var q="",i=h.indexOf("?");var params={};
  if(i>=0){q=h.slice(i+1);h=h.slice(0,i);q.split("&").forEach(function(kv){var s=kv.split("=");if(s[0])params[decodeURIComponent(s[0])]=decodeURIComponent((s[1]||"").replace(/\+/g," "))})}
  return {path:h,params:params};
}
function setHash(path,params){
  var q=Object.keys(params||{}).filter(function(k){return params[k]}).map(function(k){return encodeURIComponent(k)+"="+encodeURIComponent(params[k])}).join("&");
  history.replaceState(null,"","#"+path+(q?"?"+q:""));
}

/* ---------- sidebar ---------- */
function buildSide(cur){
  var h="";
  W.cats.slice().sort(function(a,b){return a.o-b.o}).forEach(function(c){
    var list=catPages(c.id);var open=(cur&&cur.cat===c.id);
    h+='<div class="grp'+(open?' open':'')+'"><a class="gh'+(open?' on':'')+'" href="#/c/'+c.id+'"><span>'+c.i+'</span><span>'+esc(c.t)+'</span><span class="n">'+list.length+'</span></a>';
    if(c.id==="guide"||c.id==="trinkets"||c.id==="raft"||c.id==="epicloot"||c.id==="tools"||c.id==="classes"){
      h+='<div class="sub">';
      list.slice(0,40).forEach(function(p){h+='<a href="'+href(p.id)+'"'+(cur&&cur.id===p.id?' class="cur"':'')+' title="'+esc(p.t)+'">'+esc(p.t)+'</a>'});
      h+='</div>';
    }
    h+='</div>';
  });
  h+='<hr><div class="grp"><a class="gh" href="#/hotkeys"><span>⌨️</span><span>Горячие клавиши</span></a></div>';
  h+='<div class="grp"><a class="gh" href="#/settings"><span>🧩</span><span>Все настройки</span><span class="n">'+W.settings.length+'</span></a></div>';
  side.innerHTML=h;
  $$(".gh",side).forEach(function(a){a.addEventListener("click",function(){if(window.innerWidth<=980)document.body.classList.remove("menu")})});
  $$(".sub a",side).forEach(function(a){a.addEventListener("click",function(){document.body.classList.remove("menu")})});
}

/* ---------- auto-link (энциклопедия) ---------- */
var termMap = null, termRx = null;
function buildTerms(){
  termMap = {};var arr=[];
  (byCat.pov||[]).forEach(function(p){
    var names=[p.t].concat(p.a||[]);
    names.forEach(function(n){
      n=(n||"").trim();if(n.length<4)return;var k=n.toLowerCase();
      if(!termMap[k]){termMap[k]=p.id;arr.push(n)}
    });
  });
  arr.sort(function(a,b){return b.length-a.length});
  var rx=arr.map(function(s){return s.replace(/[.*+?^${}()|[\]\\]/g,"\\$&")}).join("|");
  termRx=new RegExp("(^|[^А-Яа-яЁёA-Za-z0-9])("+rx+")(?=$|[^А-Яа-яЁёA-Za-z0-9])","gi");
}
function autolink(root,selfId){
  if(!termMap)buildTerms();
  var done={},walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT,{acceptNode:function(n){
    var p=n.parentNode;while(p&&p!==root){var t=p.nodeName;if(t==="A"||t==="CODE"||t==="H1"||t==="H2"||t==="H3"||t==="TH"||t==="KBD")return NodeFilter.FILTER_REJECT;p=p.parentNode}
    return n.nodeValue.length>3?NodeFilter.FILTER_ACCEPT:NodeFilter.FILTER_REJECT}});
  var nodes=[],n;while((n=walker.nextNode()))nodes.push(n);
  nodes.forEach(function(node){
    var s=node.nodeValue,last=0,out=null,m;termRx.lastIndex=0;
    while((m=termRx.exec(s))){
      var id=termMap[m[2].toLowerCase()];
      if(!id||id===selfId||done[id])continue;
      done[id]=1;out=out||document.createDocumentFragment();
      var st=m.index+m[1].length;
      out.appendChild(document.createTextNode(s.slice(last,st)));
      var a=document.createElement("a");a.href=href(id);a.className="auto";a.title="Статья: "+byId[id].t;a.textContent=m[2];out.appendChild(a);
      last=st+m[2].length;
    }
    if(out){out.appendChild(document.createTextNode(s.slice(last)));node.parentNode.replaceChild(out,node)}
  });
}

/* ---------- views ---------- */
function home(){
  var m=W.meta,h='<div class="hero"><h1>Вики сборки Valheim</h1><p>Справочник по вашей сборке: Path of Valheiman, Epic Loot, Item Drawers, патчи и ValheimRAFT. Всё в одном месте, с поиском по статьям, предметам и настройкам.</p><div class="stats"><span>'+m.pages+' страниц</span><span>'+(m.icons||0)+' иконок</span><span>'+(m.recipes||0)+' рецептов</span><span>'+(m.relations||0)+' связей</span><span>'+m.settings+' настроек</span><span>Valheim '+m.valheim+'</span><span>PoV '+m.pov+'</span><span>Epic Loot '+m.epicloot+'</span></div></div>';
  h+='<h2>Разделы</h2><div class="cards">';
  W.cats.slice().sort(function(a,b){return a.o-b.o}).forEach(function(c){
    h+='<a class="card" href="#/c/'+c.id+'"><span class="ic">'+c.i+'</span><b>'+esc(c.t)+'</b><small>'+esc(c.d)+'</small><br><small>'+(byCat[c.id]||[]).length+' страниц</small></a>'});
  h+='<a class="card" href="#/hotkeys"><span class="ic">⌨️</span><b>Горячие клавиши</b><small>Все клавиши модов в одной таблице.</small></a>';
  h+='<a class="card" href="#/settings"><span class="ic">🧩</span><b>Все настройки</b><small>'+W.settings.length+' параметров с описанием, значением и умолчанием.</small></a></div>';
  h+='<h2>С чего начать</h2><ul><li><a href="#/p/guide:start">Что установлено и как всё устроено</a></li><li><a href="#/p/guide:equipment">Слоты, пояса и колчаны</a></li><li><a href="#/p/guide:drawers">Раскладка по ящикам (L)</a></li><li><a href="#/p/guide:xp-penalty">Как убрать штраф боевого опыта</a></li><li><a href="#/p/trinkets:all">Таблица всех тринкетов</a></li><li><a href="#/p/guide:friend">Что дать другу</a></li><li><a href="#/p/raft:status">Статус ValheimRAFT</a></li></ul>';
  h+='<h2>Случайная статья</h2><p><a href="#" id="rnd">🎲 Открыть случайную статью энциклопедии</a></p>';
  content.innerHTML=h;
  $("#rnd").onclick=function(e){e.preventDefault();var l=byCat.pov;location.hash=href(l[Math.floor(Math.random()*l.length)].id)};
  buildSide(null);document.title="Вики сборки Valheim";
}

function category(id,params){
  var c=catById[id];if(!c)return notFound();
  var list=catPages(id);var subs={};
  list.forEach(function(p){var s=(p.s||"Без раздела").split(" › ")[0];(subs[s]=subs[s]||[]).push(p)});
  var keys=Object.keys(subs).sort(function(a,b){return a.localeCompare(b,"ru")});
  var h='<div class="crumbs"><a href="#/">Главная</a> › '+esc(c.t)+'</div><h1>'+c.i+' '+esc(c.t)+'</h1><p class="muted">'+esc(c.d)+' Страниц: '+list.length+'.</p>';
  h+='<div class="toolbar"><input id="flt" placeholder="Фильтр внутри раздела…" value="'+esc(params.f||"")+'"></div>';
  if(keys.length>1){h+='<div class="chips" id="chips"><span class="chip on" data-s="">Все</span>'+keys.map(function(k){return '<span class="chip" data-s="'+esc(k)+'">'+esc(k)+' ('+subs[k].length+')</span>'}).join("")+'</div>'}
  h+='<div id="lst"></div>';
  content.innerHTML=h;buildSide({cat:id});document.title=c.t+" — Вики";
  var curSub="";
  function draw(){
    var f=($("#flt").value||"").toLowerCase().trim(),o="";
    keys.forEach(function(k){
      if(curSub&&k!==curSub)return;
      var arr=subs[k].filter(function(p){return !f||p.x.indexOf(f)>=0});
      if(!arr.length)return;
      o+=(keys.length>1?'<h3>'+esc(k)+' <small class="muted">'+arr.length+'</small></h3>':'')+'<div class="list">'+arr.map(function(p){return '<a href="'+href(p.id)+'"'+(p.m?' class="hasimg"':'')+'>'+(p.m?'<img class="th" loading="lazy" src="'+p.m+'" alt="">':'')+'<span>'+esc(p.t)+'</span></a>'}).join("")+'</div>';
    });
    $("#lst").innerHTML=o||'<p class="muted">Ничего не найдено.</p>';
  }
  $("#flt").oninput=function(){draw();setHash("/c/"+id,{f:$("#flt").value})};
  $$(".chip",content).forEach(function(ch){ch.onclick=function(){$$(".chip",content).forEach(function(x){x.classList.remove("on")});ch.classList.add("on");curSub=ch.getAttribute("data-s");draw()}});
  draw();
}

function pageView(id){
  var p=byId[id];if(!p)return notFound();
  var c=catById[p.c];
  var h='<div class="crumbs"><a href="#/">Главная</a> › <a href="#/c/'+p.c+'">'+esc(c.t)+'</a>'+(p.s?' › '+esc(p.s):'')+'</div>';
  h+='<h1>'+esc(p.t)+'</h1><div class="layout2"><article class="article" id="art">'+(p.b||(p.bl?'':'<p class="muted">Подробного описания нет.</p>'))+(p.bl?p.bl.map(function(b,i){return '<section class="blk blk'+i+'"><h2>'+esc(b[0])+'</h2>'+b[1]+'</section>'}).join(""):'')+'</article><aside class="side2">';
  if((p.f&&p.f.length)||p.m){h+='<div class="infobox"><h4>'+esc(p.t)+'</h4>'+(p.m?'<img class="pic" src="'+p.m+'" alt="'+esc(p.t)+'">':'')+'<dl>'+(p.f||[]).map(function(r){return '<dt>'+esc(r[0])+'</dt><dd>'+esc(r[1])+'</dd>'}).join("")+'</dl></div>'}
  h+='<div class="toc" id="toc" hidden></div>';
  if(p.r&&p.r.length){var grp={},seen={};p.r.forEach(function(x){if(!byId[x[1]]||seen[x[0]+x[1]])return;seen[x[0]+x[1]]=1;(grp[x[0]]=grp[x[0]]||[]).push(byId[x[1]])});
    var gk=Object.keys(grp);if(gk.length){h+='<div class="rel rl"><b>Связи</b>'+gk.map(function(k){var l=grp[k];return '<div class="rg"><span class="rk">'+esc(k)+' ('+l.length+')</span>'+l.slice(0,10).map(function(r){return '<a href="'+href(r.id)+'">'+(r.m?'<img class="ci" src="'+r.m+'" alt="">':'')+esc(r.t)+'</a>'}).join("")+(l.length>10?'<span class="muted">… ещё '+(l.length-10)+'</span>':'')+'</div>'}).join("")+'</div>'}}
  var rel=[];(p.l||[]).forEach(function(i){if(byId[i])rel.push(byId[i])});
  if(rel.length){h+='<div class="rel"><b>Ссылаются сюда ('+rel.length+')</b>'+rel.slice(0,14).map(function(r){return '<a href="'+href(r.id)+'">'+esc(r.t)+'</a>'}).join("")+'</div>'}
  var sib=(byCat[p.c]||[]).filter(function(x){return x.s===p.s&&x.id!==p.id}).slice(0,8);
  if(sib.length){h+='<div class="rel"><b>Из того же раздела</b>'+sib.map(function(r){return '<a href="'+href(r.id)+'">'+esc(r.t)+'</a>'}).join("")+'</div>'}
  h+='</aside></div>';
  content.innerHTML=h;buildSide({cat:p.c,id:id});document.title=p.t+" — Вики";
  var art=$("#art");fillIcons(content);
  $$("table.t",art).forEach(function(t){var w=document.createElement("div");w.className="tw";t.parentNode.insertBefore(w,t);w.appendChild(t)});
  var heads=$$("h2,h3",art);
  if(heads.length>2){
    heads.forEach(function(x,i){x.id="h"+i});
    $("#toc").hidden=false;$("#toc").innerHTML="<b>Содержание</b>"+heads.map(function(x,i){return '<a class="'+(x.tagName==="H3"?"l3":"")+'" href="#" data-t="h'+i+'">'+esc(x.textContent)+'</a>'}).join("");
    $$("#toc a").forEach(function(a){a.onclick=function(e){e.preventDefault();var t=document.getElementById(a.getAttribute("data-t"));if(t)t.scrollIntoView({behavior:"smooth",block:"start"})}});
  }
  if(p.c==="pov")autolink(art,p.id);
  window.scrollTo(0,0);
}

function notFound(){content.innerHTML='<h1>Не найдено</h1><p>Такой страницы нет. <a href="#/">На главную</a></p>';buildSide(null)}

/* ---------- настройки ---------- */
function settings(params,hotOnly){
  var mods={};W.settings.forEach(function(s){mods[s.m]=s.mn});
  var base=hotOnly?W.settings.filter(function(s){return s.t==="KeyboardShortcut"||/^(Hotkeys)$/i.test(s.s)}):W.settings;
  var h='<div class="crumbs"><a href="#/">Главная</a> › '+(hotOnly?'Горячие клавиши':'Все настройки')+'</div><h1>'+(hotOnly?'⌨️ Горячие клавиши':'🧩 Все настройки')+'</h1>';
  h+='<p class="muted">'+(hotOnly?'Клавиши из конфигов модов (значение — как стоит у вас сейчас). ':'Из файлов <code>BepInEx\\config</code>. ')+'Описания в конфигах английские. Правьте файлы только при закрытой игре. Всего: '+base.length+'.</p>';
  h+='<div class="toolbar"><input id="sq" placeholder="Поиск по названию и описанию…" value="'+esc(params.q||"")+'"><select id="sm"><option value="">Все моды</option>'+Object.keys(mods).map(function(k){return '<option value="'+k+'"'+(params.mod===k?' selected':'')+'>'+esc(mods[k])+'</option>'}).join("")+'</select><select id="ss"><option value="">Все разделы</option></select>';
  if(!hotOnly)h+='<label><input type="checkbox" id="sc"'+(params.ch?' checked':'')+'> только изменённые</label>';
  h+='</div><div id="cnt" class="muted"></div><div id="sl"></div>';
  content.innerHTML=h;buildSide(null);document.title=(hotOnly?"Горячие клавиши":"Настройки")+" — Вики";
  var shown=150;
  function fillSections(){
    var m=$("#sm").value,secs={};base.forEach(function(s){if(!m||s.m===m)secs[s.s]=1});
    var cur=params.sec||"";$("#ss").innerHTML='<option value="">Все разделы</option>'+Object.keys(secs).sort().map(function(k){return '<option'+(k===cur?' selected':'')+'>'+esc(k)+'</option>'}).join("");
  }
  function draw(){
    var q=($("#sq").value||"").toLowerCase().trim(),m=$("#sm").value,sec=$("#ss").value,ch=$("#sc")&&$("#sc").checked;
    var arr=base.filter(function(s){
      if(m&&s.m!==m)return false;if(sec&&s.s!==sec)return false;
      if(ch&&s.v===s.d)return false;
      if(q&&(s.k+" "+s.h+" "+s.s).toLowerCase().indexOf(q)<0)return false;return true});
    $("#cnt").textContent="Найдено: "+arr.length;
    $("#sl").innerHTML=arr.slice(0,shown).map(function(s){
      var diff=s.d&&s.v!==s.d;
      return '<div class="set"><div><span class="k">'+esc(s.k)+'</span> <span class="m">· '+esc(s.mn)+' · <span class="sec">'+esc(s.s)+'</span>'+(s.t?' · '+esc(s.t):'')+'</span></div>'+
        (s.h?'<div class="d">'+esc(s.h)+'</div>':'')+
        '<div class="vals">Сейчас: <code class="'+(diff?'changed':'')+'">'+esc(s.v)+'</code>'+(s.d?' &nbsp; По умолчанию: <code>'+esc(s.d)+'</code>':'')+(s.r?' &nbsp; <span class="muted">'+esc(s.r)+'</span>':'')+'</div></div>'}).join("")+
      (arr.length>shown?'<p><a href="#" id="more">Показать ещё ('+(arr.length-shown)+')</a></p>':'');
    var mb=$("#more");if(mb)mb.onclick=function(e){e.preventDefault();shown+=200;draw()};
    setHash(hotOnly?"/hotkeys":"/settings",{q:$("#sq").value,mod:m,sec:sec,ch:ch?"1":""});
  }
  fillSections();
  $("#sq").oninput=function(){shown=150;draw()};
  $("#sm").onchange=function(){params.sec="";fillSections();shown=150;draw()};
  $("#ss").onchange=function(){params.sec=$("#ss").value;shown=150;draw()};
  if($("#sc"))$("#sc").onchange=function(){shown=150;draw()};
  draw();
}

/* ---------- поиск ---------- */
var qEl=$("#q"),resEl=$("#results"),sel=-1,cur=[];
function snippet(text,q){
  var i=text.indexOf(q);if(i<0)return"";var a=Math.max(0,i-40),b=Math.min(text.length,i+q.length+80);
  return (a>0?"…":"")+esc(text.slice(a,i))+"<mark>"+esc(text.slice(i,i+q.length))+"</mark>"+esc(text.slice(i+q.length,b))+(b<text.length?"…":"");
}
function search(q){
  q=q.toLowerCase().trim();if(!q)return[];
  var words=q.split(/\s+/),out=[];
  W.pages.forEach(function(p){
    var t=p.t.toLowerCase(),sc=0;
    if(t===q)sc=1000;else if(t.indexOf(q)===0)sc=600;else if(t.indexOf(q)>=0)sc=400;
    else if((p.a||[]).some(function(a){return a.toLowerCase()===q}))sc=500;
    else if(words.every(function(w){return t.indexOf(w)>=0}))sc=300;
    else if(words.every(function(w){return p.x.indexOf(w)>=0}))sc=100;
    if(sc){out.push({sc:sc-(p.c==="items"?0:0),p:p,sn:sc<300?snippet(p.x,words[0]):""})}
  });
  W.settings.forEach(function(s){
    var k=s.k.toLowerCase(),sc=0;
    if(k===q)sc=450;else if(k.indexOf(q)>=0)sc=250;else if(words.every(function(w){return (k+" "+s.h.toLowerCase()).indexOf(w)>=0}))sc=60;
    if(sc)out.push({sc:sc,s:s});
  });
  out.sort(function(a,b){return b.sc-a.sc||(a.p&&b.p?a.p.t.localeCompare(b.p.t,"ru"):0)});
  return out.slice(0,14);
}
function showRes(){
  var q=qEl.value;if(!q.trim()){resEl.hidden=true;return}
  cur=search(q);sel=cur.length?0:-1;
  resEl.innerHTML=cur.length?cur.map(function(r,i){
    if(r.p){var c=catById[r.p.c];return '<a class="res'+(i===0?' sel':'')+'" href="'+href(r.p.id)+'">'+(r.p.m?'<img class="th" src="'+r.p.m+'" alt="">':'')+'<b>'+esc(r.p.t)+'</b> <small>'+c.i+' '+esc(c.t)+(r.p.s?' › '+esc(r.p.s):'')+'</small>'+(r.sn?'<small>'+r.sn+'</small>':'')+'</a>'}
    return '<a class="res'+(i===0?' sel':'')+'" href="#/settings?q='+encodeURIComponent(r.s.k)+'&mod='+r.s.m+'"><b>'+esc(r.s.k)+'</b> <small>🧩 Настройка · '+esc(r.s.mn)+' · '+esc(r.s.s)+'</small><small>'+esc(r.s.h.slice(0,110))+'</small></a>'
  }).join(""):'<div class="res muted">Ничего не найдено</div>';
  resEl.hidden=false;
}
qEl.addEventListener("input",showRes);
qEl.addEventListener("focus",showRes);
qEl.addEventListener("keydown",function(e){
  var items=$$(".res",resEl);
  if(e.key==="ArrowDown"||e.key==="ArrowUp"){e.preventDefault();if(!items.length)return;items[sel]&&items[sel].classList.remove("sel");sel=(sel+(e.key==="ArrowDown"?1:-1)+items.length)%items.length;items[sel].classList.add("sel");items[sel].scrollIntoView({block:"nearest"})}
  else if(e.key==="Enter"){if(items[sel]){location.hash=items[sel].getAttribute("href").replace(/^#/,"");qEl.blur();resEl.hidden=true}}
  else if(e.key==="Escape"){resEl.hidden=true;qEl.blur()}
});
document.addEventListener("click",function(e){if(!e.target.closest("#searchbox"))resEl.hidden=true;else if(e.target.closest(".res"))resEl.hidden=true});
document.addEventListener("keydown",function(e){if(e.key==="/"&&document.activeElement.tagName!=="INPUT"&&document.activeElement.tagName!=="SELECT"){e.preventDefault();qEl.focus();qEl.select()}});

/* ---------- роутер ---------- */
function route(){
  var r=parseHash(),p=r.path;
  if(p==="/"||p==="")home();
  else if(p.indexOf("/p/")===0)pageView(decodeURIComponent(p.slice(3)));
  else if(p.indexOf("/c/")===0)category(p.slice(3),r.params);
  else if(p==="/settings")settings(r.params,false);
  else if(p==="/hotkeys")settings(r.params,true);
  else notFound();
  document.body.classList.remove("menu");
}
window.addEventListener("hashchange",route);
$("#foot").innerHTML="Собрано "+esc(W.meta.built)+" · данные: локализация и конфиги сборки, код модов, гайды. Не официальная вики.";
route();
})();
