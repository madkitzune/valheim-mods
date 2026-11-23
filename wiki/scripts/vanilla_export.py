# -*- coding: utf-8 -*-
"""Из dump.json (UnityPy-выгрузка бандлов Valheim) -> data/vanilla.json + assets/vicons/*.png
Запуск: python -I scripts/vanilla_export.py <папка со dump.json, tx_localization.txt и s/>"""
import json, os, sys, csv, io, collections
from PIL import Image

SRC = sys.argv[1]
HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = json.load(open(os.path.join(SRC, "dump.json"), encoding="utf-8"))
gos, monos, sprites = D["gos"], D["monos"], D["sprites"]
bykey = {"%s|%d" % (m["cab"], m["pid"]): m for m in monos}


def goname(r):
    return gos.get(r[1:]) if r else None


def go_of(m):
    g = m.get("go")
    return g[1:] if g else None


# --- локализация (английский + русский) из resources.assets (tx_localization.txt)
loc = {}
rd = csv.reader(io.StringIO(open(os.path.join(SRC, "tx_localization.txt"), encoding="utf-8", errors="replace").read()))
hdr = next(rd)
iru, ien = hdr.index("Russian"), hdr.index("English")
for row in rd:
    if len(row) > iru and row[0]:
        loc[row[0]] = (row[ien], row[iru])


def tr(tok):
    if not tok:
        return ("", "")
    k = tok.lstrip("$")
    return loc.get(k, (k, k))


comp = collections.defaultdict(dict)
for m in monos:
    g = go_of(m)
    tt = m["tt"]
    if g is None:
        continue
    if "m_itemData" in tt: comp[g]["item"] = tt
    if "m_resources" in tt and "m_category" in tt: comp[g]["piece"] = tt
    if isinstance(tt.get("m_drops"), list): comp[g]["cdrop"] = tt
    if "m_health" in tt and "m_faction" in tt: comp[g]["char"] = tt
    if "m_discoverRange" in tt: comp[g]["station"] = tt
    if "m_conversion" in tt: comp[g]["conv"] = tt
    if "m_itemPrefab" in tt: comp[g]["pick"] = tt
    if "m_dropWhenDestroyed" in tt: comp[g]["dod"] = tt
    if "m_dropItems" in tt: comp[g]["mine"] = tt
    if "m_defaultItems" in tt: comp[g]["cont"] = tt
    if isinstance(tt.get("m_items"), list) and tt["m_items"] and isinstance(tt["m_items"][0], dict) and "m_prefab" in tt["m_items"][0]: comp[g]["trader"] = tt
    if "m_spawners" in tt: comp[g]["spawn"] = tt


def prefab_of_item(ref):
    if not ref: return None
    m = bykey.get(ref[1:])
    if m and m.get("go"): return goname(m["go"])
    return None


def prefab_of_go(ref):
    return goname(ref) if ref else None


def dmgsum(d):
    if not isinstance(d, dict): return None
    return {k[2:]: v for k, v in d.items() if k.startswith("m_") and isinstance(v, (int, float)) and v}


items = {}
for g, c in comp.items():
    if "item" not in c: continue
    nm = gos.get(g)
    sh = c["item"]["m_itemData"]["m_shared"]
    icon = None
    for r in sh.get("m_icons") or []:
        if r and r[1:] in sprites:
            icon = r[1:]; break
    en, ru = tr(sh.get("m_name"))
    items[nm] = dict(id=nm, tok=sh.get("m_name"), en=en, ru=ru, desc=tr(sh.get("m_description")), type=sh.get("m_itemType"), stack=sh.get("m_maxStackSize"),
                     weight=sh.get("m_weight"), maxq=sh.get("m_maxQuality"), icon=icon, food=sh.get("m_food"), foodst=sh.get("m_foodStamina"),
                     foodeitr=sh.get("m_foodEitr"), foodburn=sh.get("m_foodBurnTime"), value=sh.get("m_value"),
                     dmg=dmgsum(sh.get("m_damages")), armor=sh.get("m_armor"), block=sh.get("m_blockPower"), dura=sh.get("m_maxDurability"),
                     skill=sh.get("m_skillType"), ammo=sh.get("m_ammoType"), setname=sh.get("m_setName"), setsize=sh.get("m_setSize"))

stations = {}
for g, c in comp.items():
    if "station" in c:
        en, ru = tr(c["station"].get("m_name"))
        stations[gos.get(g)] = dict(en=en, ru=ru, id=gos.get(g))

recipes = []
for m in monos:
    tt = m["tt"]
    if not (m.get("go") is None and "m_item" in tt and "m_resources" in tt and "m_craftingStation" in tt): continue
    out = prefab_of_item(tt["m_item"])
    if not out: continue
    st = None
    if tt.get("m_craftingStation"):
        sm = bykey.get(tt["m_craftingStation"][1:])
        st = goname(sm["go"]) if sm and sm.get("go") else None
    ins = []
    for r in tt["m_resources"]:
        p = prefab_of_item(r.get("m_resItem"))
        if p: ins.append([p, r.get("m_amount", 1), r.get("m_amountPerLevel", 0)])
    recipes.append(dict(out=out, n=tt.get("m_amount", 1), st=st, lvl=tt.get("m_minStationLevel", 1), ins=ins, en=bool(tt.get("m_enabled", 1))))

pieces = {}
for g, c in comp.items():
    if "piece" not in c: continue
    t = c["piece"]
    st = None
    if t.get("m_craftingStation"):
        sm = bykey.get(t["m_craftingStation"][1:])
        st = goname(sm["go"]) if sm and sm.get("go") else None
    ins = []
    for r in t["m_resources"]:
        p = prefab_of_item(r.get("m_resItem"))
        if p: ins.append([p, r.get("m_amount", 1), r.get("m_amountPerLevel", 0)])
    en, ru = tr(t.get("m_name"))
    ic = t.get("m_icon")
    icon = ic[1:] if ic and ic[1:] in sprites else None
    pieces[gos.get(g)] = dict(id=gos.get(g), en=en, ru=ru, cat=t.get("m_category"), st=st, ins=ins, icon=icon, on=bool(t.get("m_enabled", 1)), comfort=t.get("m_comfort"))

chars = {}
for g, c in comp.items():
    if "char" not in c: continue
    t = c["char"]
    en, ru = tr(t.get("m_name"))
    drops = []
    cd = c.get("cdrop")
    if cd:
        for d in cd["m_drops"]:
            p = prefab_of_go(d.get("m_prefab"))
            if p: drops.append([p, d.get("m_amountMin"), d.get("m_amountMax"), d.get("m_chance"), d.get("m_levelMultiplier")])
    chars[gos.get(g)] = dict(id=gos.get(g), en=en, ru=ru, hp=t.get("m_health"), faction=t.get("m_faction"), boss=t.get("m_boss"), drops=drops)

convs = []
for g, c in comp.items():
    if "conv" not in c: continue
    nm = gos.get(g)
    for cv in c["conv"]["m_conversion"]:
        a = prefab_of_item(cv.get("m_from")); b = prefab_of_item(cv.get("m_to"))
        if a and b: convs.append([nm, a, b, cv.get("m_cookTime")])


def dropt(tbl):
    out = []
    lst = tbl if isinstance(tbl, list) else (tbl.get("m_drops", []) if isinstance(tbl, dict) else [])
    for d in lst or []:
        if isinstance(d, str):
            q = goname(d)
            if q: out.append([q, 1, 1])
            continue
        if not isinstance(d, dict): continue
        p = prefab_of_go(d.get("m_item")) or prefab_of_go(d.get("m_prefab"))
        if p: out.append([p, d.get("m_stackMin"), d.get("m_stackMax")])
    return out


world = []
for g, c in comp.items():
    nm = gos.get(g)
    if "pick" in c and c["pick"].get("m_itemPrefab"):
        p = prefab_of_go(c["pick"]["m_itemPrefab"])
        if p: world.append(["pick", nm, p, str(c["pick"].get("m_amount"))])
    if "dod" in c:
        for p, a, b in dropt(c["dod"]["m_dropWhenDestroyed"]): world.append(["destroy", nm, p, "%s-%s" % (a, b)])
    if "mine" in c:
        for p, a, b in dropt(c["mine"]["m_dropItems"]): world.append(["mine", nm, p, "%s-%s" % (a, b)])
    if "cont" in c:
        for p, a, b in dropt(c["cont"]["m_defaultItems"]): world.append(["chest", nm, p, "%s-%s" % (a, b)])

spawns = []
for g, c in comp.items():
    if "spawn" in c:
        for sp in c["spawn"]["m_spawners"]:
            q = prefab_of_go(sp.get("m_prefab"))
            if q and sp.get("m_enabled", 1):
                spawns.append([q, sp.get("m_biome"), sp.get("m_groupSizeMin"), sp.get("m_groupSizeMax"), sp.get("m_spawnAtDay"), sp.get("m_spawnAtNight"), sp.get("m_requiredGlobalKey")])

trade = []
for g, c in comp.items():
    if "trader" in c:
        for it in c["trader"]["m_items"]:
            p = prefab_of_item(it.get("m_prefab"))
            if p: trade.append([gos.get(g), p, it.get("m_stack"), it.get("m_price"), it.get("m_requiredGlobalKey")])

json.dump(dict(items=items, stations=stations, recipes=recipes, pieces=pieces, chars=chars, convs=convs, world=world, trade=trade, spawns=spawns,
               loc={k: v for k, v in loc.items() if k.startswith(("item_", "piece_", "enemy_", "se_", "skill_", "biome_"))}),
          open(os.path.join(HERE, "data", "vanilla.json"), "w", encoding="utf-8"), ensure_ascii=False)
print({k: len(v) for k, v in dict(items=items, stations=stations, recipes=recipes, pieces=pieces, chars=chars, convs=convs, world=world, trade=trade, spawns=spawns).items()})

OUTI = os.path.join(HERE, "assets", "vicons")
os.makedirs(OUTI, exist_ok=True)
n = 0
for nm, it in list(items.items()) + list(pieces.items()):
    k = it.get("icon")
    if not k: continue
    src = os.path.join(SRC, sprites[k][1])
    if not os.path.exists(src): continue
    im = Image.open(src).convert("RGBA"); im.thumbnail((128, 128), Image.LANCZOS)
    im.save(os.path.join(OUTI, nm + ".png"), optimize=True); n += 1
print("icons", n)
