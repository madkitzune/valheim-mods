# -*- coding: utf-8 -*-
"""Единая модель связей вики: сущности, рецепты, отношения -> блоки страниц и «Связи».
Вызывается из build.py: links.build(G) где G = globals() build.py."""
import json, os, re, io, base64, html, collections
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
E = html.escape

BIOMES = [(1, "Луга"), (8, "Чёрный лес"), (2, "Болото"), (4, "Горы"), (16, "Равнины"), (512, "Туманные земли"), (32, "Пепельные земли"), (64, "Глубокий Север"), (256, "Океан")]
TYPE_GROUP = {1: "Материалы", 2: "Зелья и расходники", 3: "Оружие", 14: "Оружие", 22: "Оружие", 4: "Оружие", 5: "Щиты", 6: "Броня", 7: "Броня", 11: "Броня",
              12: "Броня", 17: "Броня", 9: "Боеприпасы", 23: "Боеприпасы", 13: "Трофеи", 15: "Инструменты", 19: "Инструменты", 18: "Утилити (пояса, ключи)",
              21: "Рыба", 10: "Внешний вид", 16: "Прочее", 24: "Тринкеты"}
TYPE_SLOT = {3: "в руку (одноручное оружие)", 14: "в руки (двуручное оружие)", 22: "в руки (двуручное оружие)", 4: "в руки (лук/арбалет)", 5: "в левую руку (щит)", 6: "на голову (шлем)",
             7: "на тело (нагрудник)", 11: "на ноги (поножи)", 12: "на руки", 17: "на плечи (плащ)", 18: "в слот утилити (пояс/амулет)", 15: "в руку (факел)", 19: "в руку (инструмент)", 24: "в слот тринкета"}
BOSS_KEYS = {"defeated_eikthyr": "Эйктюр", "defeated_gdking": "Древний", "defeated_bonemass": "Костяная масса", "defeated_dragon": "Модер", "defeated_goblinking": "Ягл", "defeated_queen": "Королева", "defeated_fader": "Фейдер"}
TRADERS = {"Haldor": "Хальдор", "Hildir": "Хильдир", "BogWitch": "Болотная ведьма"}
STATION_RU = {"piece_workbench": "Верстак", "forge": "Кузница", "blackforge": "Чёрная кузница", "piece_cauldron": "Котёл", "piece_MeadCauldron": "Медовый котёл",
              "piece_stonecutter": "Каменотёс", "piece_artisanstation": "Стол ремесленника", "piece_magetable": "Стол галдра", "piece_preptable": "Стол приготовления",
              "UpgradeStation": "Кузница потенциала", "PT_RuneCarverTable": "Стол рунореза", "PT_RunicAnvil": "Рунная наковальня", "EL:table": "Стол зачарования (Epic Loot)",
              "smelter": "Плавильня", "blastfurnace": "Доменная печь", "piece_cookingstation": "Очаг для готовки", "piece_oven": "Печь", "piece_fermenter": "Ферментер",
              "charcoal_kiln": "Угольная печь", "eitrrefinery": "Очиститель эйтра", "windmill": "Мельница", "piece_spinningwheel": "Прялка", "piece_beehive": "Улей",
              "bonfire": "Костёр", "fire_pit": "Очаг", "piece_sapcollector": "Сборщик сока", "piece_cooker": "Очаг"}
SPECIAL_NAMES = {"EL:table": "Стол зачарования"}
IDS_SPECIAL_STATION = {"piece_workbench", "forge", "blackforge", "piece_cauldron", "piece_MeadCauldron", "piece_stonecutter", "piece_artisanstation", "piece_magetable", "piece_preptable", "UpgradeStation"}


def jl(p):
    return json.load(open(p, encoding="utf-8-sig"))


def humanize(s):
    s = re.sub(r"^(PT_|PoV_)", "", s)
    s = re.sub(r"([a-z0-9])([A-Z])", r"\1 \2", s).replace("_", " ")
    return s.strip()


def build(G):
    pages, page, cat, loc, PIMG = G["pages"], G["page"], G["cat"], G["loc"], G["PIMG"]
    TRINKETS = G["TRINKETS"]
    icons = G.setdefault("ICONS", {})

    # ------------------------------------------------------------ данные
    V = jl(os.path.join(HERE, "data", "vanilla.json"))
    povr = {"items": {}, "stations": {}}
    pp = os.path.join(HERE, "data", "pov_recipes.json")
    if os.path.exists(pp):
        povr = jl(pp)
    vit, vpc, vch = V["items"], V["pieces"], V["chars"]
    vloc = V["loc"]

    # ------------------------------------------------------------ иконки
    def icon_file(eid):
        c = [os.path.join(HERE, "assets", "modicons", eid + ".png"), os.path.join(HERE, "assets", "vicons", eid + ".png"), os.path.join(PIMG, "PassiveTree.item." + eid + ".png"),
             os.path.join(PIMG, "PassiveTree.gemhex." + eid + ".png"), os.path.join(PIMG, "PassiveTree.jewel." + eid + ".png")]
        for p in c:
            if os.path.exists(p):
                return p
        return None

    def reg_icon(eid, size=72, path=None):
        if eid in icons:
            return True
        p = path or icon_file(eid)
        if not p:
            return False
        try:
            im = Image.open(p).convert("RGBA")
            bb = im.getbbox()
            if bb:
                im = im.crop(bb)
            im.thumbnail((size, size), Image.LANCZOS)
            buf = io.BytesIO()
            im.save(buf, "WEBP", quality=80, method=6)
            icons[eid] = "data:image/webp;base64," + base64.b64encode(buf.getvalue()).decode()
            return True
        except Exception:
            return False

    add_recipe_pending = []
    # ------------------------------------------------------------ реестр сущностей
    ENT = {}     # id -> dict(name, pid, grp)

    def ru(v):
        return v.get("ru") or v.get("en")

    # станции -> страницы построек
    # PoV предметы
    pov_ids = {m.group(1) for k in loc for m in [re.match(r"^item\.([^.]+)\.name$", k)] if m}
    for i in pov_ids:
        if "item:" + i in pages:
            ENT[i] = dict(name=pages["item:" + i]["t"], pid="item:" + i)
    # тринкеты (существующие страницы trinket:<id>)
    for tk in vit:
        if vit[tk]["type"] == 24:
            ENT[tk] = dict(name=ru(vit[tk]), pid="trinket:" + tk, trinket=True)

    # свои моды (AxePlates, ArcaneArmory): data/mods_items.json
    MODS = []
    mp = os.path.join(HERE, "data", "mods_items.json")
    if os.path.exists(mp):
        MODS = jl(mp)["items"]
        cat("mods", "Свои моды", icon="🛠️", desc="AxePlates (обухи для Древних топоров) и ArcaneArmory (новое оружие).", sort=4)
        KIND = {"plate": "Обухи", "material": "Материалы", "weapon": "Оружие", "plan": "Чертежи"}
        mid = {x["id"] for x in MODS}
        for x in MODS:
            pid_ = "mod:" + x["id"]
            f = [("Код", x["id"]), ("Мод", x["mod"]), ("Название (EN)", x["en"])]
            if x.get("stage"): f.append(("Стадия", x["stage"]))
            if x.get("base"): f.append(("Ванильная основа", x["base"]))
            if x.get("dmgMulVsBase"): f.append(("Урон к основе", "×%s" % x["dmgMulVsBase"]))
            if x.get("effect"): f.append(("Эффект", x["effect"]))
            page(pid_, x["ru"], "mods", "<p>%s</p>" % E(x.get("desc_ru") or x.get("effect") or ""), fields=f, sub="%s: %s" % (x["mod"], KIND.get(x["kind"], x["kind"])))
            ENT[x["id"]] = dict(name=x["ru"], pid=pid_)
        for x in MODS:
            ob, us, cb = [], [], []
            if x.get("acquire"): ob.append("Как получить: %s." % x["acquire"])
            dr = x.get("drops")
            if dr:
                ob.append("Выпадает: мобы %s; сундуки %s; шанс %s." % (dr.get("mobs"), dr.get("chests"), dr.get("chance")))
            if x.get("usage"): us.append(x["usage"])
            if x.get("effect"): us.append("Эффект: %s." % x["effect"])
            if x.get("base"): us.append("Расходует ванильную основу: %s (нужна в инвентаре при крафте)." % x["base"])
            if x["kind"] == "plan" and x.get("unlocks"):
                cb.append("Изучение открывает рецепт: %s." % next((y["ru"] for y in MODS if y["id"] == x["unlocks"]), x["unlocks"]))
            if x["kind"] == "plate": cb.append("Устанавливается на Древние топоры (AxeEarly), даёт бонус урона и уровня инструмента.")
            povr["items"][x["id"]] = dict(obtain=ob, use=us, consumed_by=cb, recipes=[])
        for x in MODS:
            r = x.get("recipe")
            if r:
                add_recipe_pending.append((x["id"], r.get("amount", 1), r.get("station"), r.get("level") or 1, [(i["item"], i["n"], 0) for i in r["ingredients"]], "мод", r.get("upgrade", "")))

    # PoV предметы без строки в локализации, но с иконкой/рецептом
    extra = set()
    for f in os.listdir(PIMG):
        m_ = re.match(r"^PassiveTree\.item\.(.+)\.png$", f)
        if m_:
            extra.add(m_.group(1))
    extra |= set(povr["items"])
    for d_ in povr["items"].values():
        for r_ in d_.get("recipes", []) or []:
            extra.update(a for a, _ in r_.get("in", []))
    for i in sorted(extra):
        if i in ENT or i in vit or i in vpc or not re.match(r"^(PT_|PoV_)", i):
            continue
        page("item:" + i, humanize(i), "items", "", fields=[("Код предмета", i), ("Название", "по коду (нет строки в локализации)")], sub="Прочее (по коду)")
        ENT[i] = dict(name=humanize(i), pid="item:" + i)

    # ванильные предметы с иконкой и участием в данных
    used = set()
    for r in V["recipes"]:
        used.add(r["out"]); used.update(i[0] for i in r["ins"])
    for pc in vpc.values():
        used.update(i[0] for i in pc["ins"])
    for c in vch.values():
        used.update(d[0] for d in c["drops"])
    used.update(t[1] for t in V["trade"]); used.update(c[1] for c in V["convs"]); used.update(c[2] for c in V["convs"])
    used.update(w[2] for w in V["world"] if w[0] in ("pick", "mine"))
    vpages = []
    for iid, it in vit.items():
        if iid in ENT or not it["icon"]:
            continue
        if iid not in used and it["type"] != 13:
            continue
        ENT[iid] = dict(name=ru(it), pid="v:" + iid, vanilla=True)
        vpages.append(iid)
    # станции и постройки с рецептами
    vpieces = []
    for pid_, pc in vpc.items():
        if not pc["icon"] or not pc["ins"] or not pc["on"] or " " in pid_:
            continue
        if re.search(r"\(\d+\)|DELETE|_old|test", pid_, re.I):
            continue
        ENT[pid_] = dict(name=ru(pc), pid="vp:" + pid_, piece=True)
        vpieces.append(pid_)
    for sid in IDS_SPECIAL_STATION | {"PT_RuneCarverTable", "PT_RunicAnvil", "smelter", "blastfurnace", "piece_cookingstation", "piece_oven", "piece_fermenter", "charcoal_kiln", "eitrrefinery", "windmill", "piece_spinningwheel", "piece_beehive", "piece_cauldron"}:
        if sid not in ENT:
            nm = STATION_RU.get(sid) or (ru(vpc[sid]) if sid in vpc else humanize(sid))
            if sid in vpc and vpc[sid]["icon"]:
                ENT[sid] = dict(name=ru(vpc[sid]), pid="vp:" + sid, piece=True)
                vpieces.append(sid)
            elif sid.startswith("PT_"):
                pid_ = "piece:" + sid
                ENT[sid] = dict(name=pages[pid_]["t"] if pid_ in pages else nm, pid=pid_ if pid_ in pages else None, piece=True)
            else:
                ENT[sid] = dict(name=nm, pid=None, piece=True)
    ENT["EL:table"] = dict(name="Стол зачарования (Epic Loot)", pid=None, piece=True)
    # существа
    vcreat = []
    for cid, c in vch.items():
        if re.search(r"\(\d+\)", cid) or cid in ("Player", "TrainingDummy") or not (c["drops"] or c["boss"]):
            continue
        ENT["c:" + cid] = dict(name=re.sub(r"<[^>]+>", "", ru(c)), pid="creature:" + cid, creature=True)
        vcreat.append(cid)

    def nm(eid):
        e = ENT.get(eid)
        if e:
            return e["name"]
        if eid in vit:
            return ru(vit[eid])
        if eid in vpc:
            return ru(vpc[eid])
        return humanize(eid)

    def thumb(eid, n=None, extra=""):
        e = ENT.get(eid)
        has = reg_icon(eid)
        img = '<img class="ci" data-i="%s" alt="">' % E(eid) if has else ""
        txt = "%s<span>%s</span>%s%s" % (img, E(nm(eid)), (' <b class="q">×%s</b>' % n) if n not in (None, "") else "", extra)
        if e and e.get("pid"):
            return '<a class="chip2" href="#/p/%s">%s</a>' % (E(e["pid"]), txt)
        return '<span class="chip2 nolink">%s</span>' % txt

    # ------------------------------------------------------------ рецепты
    RECIPES = []

    def add_recipe(out, n, st, lvl, ins, src, note="", upg=False):
        RECIPES.append(dict(out=out, n=n, st=st, lvl=lvl, ins=[(a, b, c) if len(i) > 2 else (a, b, 0) for i in ins for a, b, c in [tuple(i) + (0,) * (3 - len(i))]], src=src, note=note))

    for a_ in add_recipe_pending:
        add_recipe(*a_)
    seen = set()
    for r in V["recipes"]:
        if not r["en"]:
            continue
        key = (r["out"], r["n"], r["st"], r["lvl"], tuple((i[0], i[1]) for i in r["ins"]))
        if key in seen:
            continue
        seen.add(key)
        add_recipe(r["out"], r["n"], r["st"], r["lvl"], r["ins"], "игра")
    # PoV: из пакета агента
    for iid, d in povr["items"].items():
        for r in d.get("recipes", []) or []:
            ins = [(a, b, 0) for a, b in r.get("in", [])]
            if not ins:
                continue
            add_recipe(iid, r.get("out") or 1, r.get("station"), r.get("level") or 1, ins, "PoV", r.get("note") or "")
    # Epic Loot
    EL = os.path.join(G["EL"])
    try:
        for f, k in (("materialconversions.json", "MaterialConversions"), ("shardstoneconversions.json", "MaterialConversions")):
            for c in jl(os.path.join(EL, f))[k]:
                add_recipe("EL:" + c["Product"], c.get("Amount", 1), "EL:table", 1, [(("EL:" + x["Item"]) if x["Item"].startswith(("Shard", "Dust", "Essence", "Reagent", "Runestone", "Red_", "Blue_", "Green_", "Yellow_", "Purple_", "White_", "Black_")) or "ShardStone" in x["Item"] else x["Item"], x["Amount"], 0) for x in c["Resources"]], "Epic Loot", c.get("Type", ""))
    except Exception as e:
        print("EL conv:", e)
    # RAFT
    raft_pieces = []
    try:
        raft_pieces = parse_raft()
    except Exception as e:
        print("raft parse:", e)
    for rp in raft_pieces:
        add_recipe("R:" + rp["id"], 1, None, 1, [(a, b, 0) for a, b in rp["ins"]], "RAFT 4.3.5", rp.get("src", ""))
        ENT["R:" + rp["id"]] = dict(name=rp["name"], pid=None, raft=True)

    # покупки/дроп/плавка
    drops = collections.defaultdict(list)      # item -> [(creature id, min,max,chance)]
    for cid in vcreat:
        for d in vch[cid]["drops"]:
            drops[d[0]].append((cid, d[1], d[2], d[3]))
    trade = collections.defaultdict(list)
    for t in V["trade"]:
        trade[t[1]].append(t)
    conv_to = collections.defaultdict(list); conv_from = collections.defaultdict(list)
    seenc = set()
    for c in V["convs"]:
        k = tuple(c[:3])
        if k in seenc:
            continue
        seenc.add(k)
        conv_to[c[2]].append(c); conv_from[c[1]].append(c)
    world = collections.defaultdict(lambda: collections.defaultdict(set))
    for kind, src, item, amt in V["world"]:
        world[item][kind].add(re.sub(r"\s*\(\d+\)$|\(Clone\)", "", src).strip())
    by_out = collections.defaultdict(list); by_in = collections.defaultdict(list)
    for r in RECIPES:
        by_out[r["out"]].append(r)
        for a, b, c in r["ins"]:
            by_in[a].append(r)

    def st_name(st):
        if not st:
            return "руками (без станции)"
        return ENT[st]["name"] if st in ENT else STATION_RU.get(st) or humanize(st)

    def st_link(st):
        if st and st in ENT and ENT[st].get("pid"):
            return '<a href="#/p/%s">%s</a>' % (E(ENT[st]["pid"]), E(st_name(st)))
        return E(st_name(st))

    def recipe_html(r, show_out=False):
        ins = "".join(thumb(a, b, (' <small class="muted">(+%s/ур.)</small>' % c) if c else "") for a, b, c in r["ins"])
        head = ""
        if show_out:
            head = thumb(r["out"], r["n"] if r["n"] != 1 else None) + '<span class="arr">←</span>'
        lv = (" ур. %s" % r["lvl"]) if r["st"] and r["lvl"] and r["lvl"] > 1 else ""
        cap = '<div class="rc-st">%s%s%s</div>' % (st_link(r["st"]), E(lv), (' · <small class="muted">%s</small>' % E(r["note"])) if r["note"] else "")
        return '<div class="rc">%s<div class="rc-in">%s</div>%s</div>' % (head, ins, cap)

    def biomes(mask):
        return ", ".join(n for b, n in BIOMES if mask & b) or "любые"

    spawn_by = collections.defaultdict(set)
    for q, mask, a, b, d, n, key in V["spawns"]:
        spawn_by[q].add(mask)

    # ------------------------------------------------------------ создание страниц
    cat("vanilla", "Предметы игры", icon="⚔️", desc="Предметы Valheim: рецепты, дроп, торговцы, где пригодятся.", sort=4)
    vrel = {}

    def mk_page(pid, title, c, sub, eid, fields):
        if pid in pages:
            return
        page(pid, title, c, "", fields=fields, sub=sub, im=None)
        pages[pid]["m"] = eid if reg_icon(eid, 80) else None

    for iid in vpages:
        it = vit[iid]
        grp = TYPE_GROUP.get(it["type"], "Прочее")
        if it["type"] == 2 and (it["food"] or it["foodst"] or it["foodeitr"]):
            grp = "Еда"
        f = [("Код предмета", iid), ("Название (EN)", it["en"]), ("Группа", grp)]
        if it["weight"] is not None: f.append(("Вес", str(round(it["weight"], 2))))
        if (it["stack"] or 0) > 1: f.append(("Стак", str(it["stack"])))
        if (it["maxq"] or 1) > 1: f.append(("Макс. уровень улучшения", str(it["maxq"])))
        if it["value"]: f.append(("Цена продажи", "%s монет" % it["value"]))
        if it["dmg"]: f.append(("Урон", ", ".join("%s %s" % (k, int(v) if v == int(v) else round(v, 1)) for k, v in it["dmg"].items())))
        if it["armor"]: f.append(("Броня", str(int(it["armor"]))))
        if it["dura"] and it["dura"] > 1: f.append(("Прочность", str(int(it["dura"]))))
        if it["food"]: f.append(("Еда", "здоровье %s, выносливость %s%s, %s с" % (int(it["food"]), int(it["foodst"] or 0), (", эйтр %s" % int(it["foodeitr"])) if it["foodeitr"] else "", int(it["foodburn"] or 0))))
        mk_page("v:" + iid, ru(it), "vanilla", grp, iid, f)
        pages["v:" + iid]["b"] = ""
    for pid_ in vpieces:
        pc = vpc[pid_]
        f = [("Код", pid_), ("Название (EN)", pc["en"])]
        if pc["comfort"]: f.append(("Комфорт", str(pc["comfort"])))
        if pc["st"]: f.append(("Станция", st_name(pc["st"])))
        isst = pid_ in IDS_SPECIAL_STATION or pid_ in ("piece_cauldron", "smelter", "blastfurnace", "piece_oven", "piece_fermenter", "charcoal_kiln", "eitrrefinery", "windmill", "piece_spinningwheel", "piece_cookingstation", "piece_beehive")
        mk_page("vp:" + pid_, ru(pc), "world", "Станции" if isst else "Постройки (игра)", pid_, f)
    for cid in vcreat:
        c = vch[cid]
        f = [("Код", cid), ("Название (EN)", re.sub(r"<[^>]+>", "", c["en"])), ("Здоровье", str(int(c["hp"] or 0)))]
        if c["boss"]: f.append(("Тип", "босс"))
        if cid in spawn_by: f.append(("Где обитает", "; ".join(sorted({biomes(m) for m in spawn_by[cid]}))))
        mk_page("creature:" + cid, re.sub(r"<[^>]+>", "", ru(c)), "world", "Существа (игра)" if not c["boss"] else "Боссы", "c:" + cid, f)
        if "c:" + cid in icons or True:
            pages["creature:" + cid]["m"] = None
    # иконки существ: нет спрайтов-портретов; для боссов берём трофей
    BOSS_TROPHY = {}
    for cid in vcreat:
        for d in vch[cid]["drops"]:
            if d[0].startswith("Trophy") and d[0] in ENT:
                BOSS_TROPHY[cid] = d[0]
                break
        if cid in BOSS_TROPHY and reg_icon(BOSS_TROPHY[cid], 80):
            icons["c:" + cid] = icons[BOSS_TROPHY[cid]]
            pages["creature:" + cid]["m"] = "c:" + cid

    # иконки для всех сущностей-ссылок
    for eid, e in ENT.items():
        if e.get("pid") and e["pid"] in pages and not pages[e["pid"]].get("m"):
            if reg_icon(eid, 80):
                pages[e["pid"]]["m"] = eid
    for eid in list(ENT):
        reg_icon(eid)
    # иконки PoV страниц (item:/gem:) -> ключи
    for pid_, p in pages.items():
        m = p.get("m")
        if m and m.startswith("data:"):
            icons[pid_] = m
            p["m"] = pid_

    # ------------------------------------------------------------ блоки
    REL = collections.defaultdict(list)      # pid -> [(label, pid2)]

    def rel(a_eid, label, b_eid):
        ea, eb = ENT.get(a_eid), ENT.get(b_eid)
        if ea and eb and ea.get("pid") and eb.get("pid") and ea["pid"] != eb["pid"]:
            REL[ea["pid"]].append((label, eb["pid"]))

    def cap(lst, n, fmt=lambda x: x):
        s = "".join(fmt(x) for x in lst[:n])
        return s + ('<span class="muted"> … и ещё %d</span>' % (len(lst) - n) if len(lst) > n else "")

    BLK = {}

    def blocks(eid):
        g, u, d, m = [], [], [], []
        # ---- как получить
        outs = by_out.get(eid, [])
        for r in outs[:6]:
            g.append("<div class='bl-h'>Рецепт%s</div>%s" % ("" if len(outs) == 1 else " (вариант)", recipe_html(r)))
            for a, b, c in r["ins"]:
                rel(eid, "← ингредиент", a)
            if r["st"]:
                rel(eid, "← станция", r["st"])
        if len(outs) > 6:
            g.append("<p class='muted'>…и ещё %d рецептов</p>" % (len(outs) - 6))
        pv = povr["items"].get(eid, {})
        for t in pv.get("obtain", []) or []:
            g.append("<p>%s</p>" % E(t))
        dr = drops.get(eid, [])
        if dr:
            rows = ""
            for cid, a, b, ch in sorted(dr, key=lambda x: -(x[3] or 0))[:14]:
                rows += "<li>%s — %s%s</li>" % (thumb("c:" + cid) if ("c:" + cid) in ENT else E(cid), ("%s–%s шт." % (a, b)) if a != b else "%s шт." % a, (", шанс %d%%" % round((ch or 1) * 100)) if ch is not None and ch < 1 else "")
                rel(eid, "← выпадает с", "c:" + cid)
            g.append("<div class='bl-h'>Выпадает с существ</div><ul>%s</ul>" % rows)
        tr_ = trade.get(eid, [])
        if tr_:
            rows = ""
            for tn, it_, st, price, key in tr_:
                need = (" (после победы над боссом «%s»)" % BOSS_KEYS[key]) if key in BOSS_KEYS else ((" (условие: %s)" % key) if key else "")
                rows += "<li>%s: %s шт. за %s монет%s</li>" % (E(TRADERS.get(tn, tn)), st, price, E(need))
            g.append("<div class='bl-h'>Продаётся у торговцев</div><ul>%s</ul>" % rows)
        for c in conv_to.get(eid, [])[:6]:
            g.append("<p>Получается в «%s» из %s%s.</p>" % (st_link(c[0]) if c[0] in ENT else E(humanize(c[0])), thumb(c[1]), (" (%s с)" % int(c[3])) if c[3] else ""))
            rel(eid, "← переработка", c[1]); rel(eid, "← станция", c[0])
        w = world.get(eid)
        if w:
            lab = (("pick", "Собирается в мире (с растений и объектов)"), ("mine", "Добывается из залежей"), ("destroy", "Выпадает при разрушении"), ("chest", "Лежит в сундуках и находках"))
            for kind, title in lab:
                names = sorted(w.get(kind, []))
                if names:
                    g.append("<div class='bl-h'>%s</div><p class='tags'>%s</p>" % (title, cap(names, 8, lambda x: "<span class='tag'>%s</span>" % E(humanize(x)))))
        if eid.startswith("EL:") or eid in ("IronBountyToken",):
            pass
        if not g:
            g.append("<p class='muted'>Источник получения в данных не найден (возможно, выдаётся скриптом, событием или модом).</p>")
        # ---- где использовать
        uses = [r for r in by_in.get(eid, []) if r["out"] != eid]
        if uses:
            # показать итоговые рецепты
            u.append("<div class='bl-h'>Ингредиент в рецептах (%d)</div>%s" % (len(uses), cap(uses, 10, lambda r: recipe_html(r, True))))
            for r in uses:
                rel(eid, "→ рецепт", r["out"]);
        cs = conv_from.get(eid, [])
        for c in cs[:6]:
            u.append("<p>Перерабатывается в «%s»: %s%s.</p>" % (st_link(c[0]) if c[0] in ENT else E(humanize(c[0])), thumb(c[2]), (" (%s с)" % int(c[3])) if c[3] else ""))
            rel(eid, "→ переработка", c[2])
        pcs = [(pid_, pc) for pid_, pc in vpc.items() if pid_ in ENT and ENT[pid_].get("piece") and any(i[0] == eid for i in pc["ins"])]
        if pcs:
            u.append("<div class='bl-h'>Нужен для построек (%d)</div><p class='tags'>%s</p>" % (len(pcs), cap(pcs, 24, lambda x: thumb(x[0], next(i[1] for i in x[1]["ins"] if i[0] == eid)))))
            for pid_, pc in pcs:
                rel(eid, "→ постройка", pid_)
        if ENT.get(eid, {}).get("piece"):
            made = [r for r in RECIPES if r["st"] == eid]
            if made:
                u.append("<div class='bl-h'>Станция для %d рецептов</div><p class='tags'>%s</p>" % (len(made), cap(made, 40, lambda r: thumb(r["out"]))))
                for r in made:
                    rel(eid, "→ изготавливает", r["out"])
        for t in pv.get("consumed_by", []) or []:
            u.append("<p>%s</p>" % E(t))
        if not u:
            u.append("<p class='muted'>В рецептах и постройках не используется (по найденным данным).</p>")
        # ---- что делать
        it = vit.get(eid)
        if pv.get("use"):
            for t in pv["use"]:
                d.append("<p>%s</p>" % E(t))
        if it and ENT.get(eid, {}).get("vanilla") or (it and ENT.get(eid, {}).get("trinket")):
            t = it["type"]
            if t in TYPE_SLOT:
                d.append("<p>Надевается/берётся %s. Для экипировки откройте инвентарь и перетащите предмет в слот или используйте его в хотбаре.</p>" % TYPE_SLOT[t])
            if t == 2:
                if it["food"] or it["foodst"] or it["foodeitr"]:
                    d.append("<p>Съешьте (E в хотбаре или Shift+Z/X/C с ячейками еды PoV): даёт здоровье %s, выносливость %s%s на %s с. Одновременно действуют 3 вида еды.</p>" % (int(it["food"] or 0), int(it["foodst"] or 0), (", эйтр %s" % int(it["foodeitr"])) if it["foodeitr"] else "", int(it["foodburn"] or 0)))
                else:
                    d.append("<p>Выпейте/используйте из хотбара (зелья PoV — Shift+V/B/N). Срок действия и эффект — в подсказке предмета.</p>")
            if t == 13:
                d.append("<p>Трофей: хранится в сумке или вешается на стену. Применение — в рецептах (см. «Где использовать»); на алтаре босса — призыв.</p>")
            if t == 1:
                d.append("<p>Материал: лежит в инвентаре/сундуке, расходуется при создании. Стопка до %s, вес %s.</p>" % (it["stack"], round(it["weight"] or 0, 2)))
            if t == 9:
                d.append("<p>Боеприпас: выбирается в ячейке стрел (Shift+Q — следующие стрелы) и тратится при выстреле из подходящего оружия.</p>")
            if (it["maxq"] or 1) > 1 and outs:
                d.append("<p>Можно улучшать у станции до уровня %s (удвоенная цена и более высокий уровень станции на каждый шаг; у PoV — предел зависит от настроек).</p>" % it["maxq"])
            if it["desc"] and it["desc"][1] and not it["desc"][1].startswith("item_"):
                d.append("<p class='muted'>Описание игры: %s</p>" % E(it["desc"][1]))
        if not d:
            d.append("<p class='muted'>Отдельных указаний нет (см. подсказку предмета в игре).</p>")
        # ---- что можно сделать
        res = []
        for r in uses:
            if r["out"] not in res:
                res.append(r["out"])
        for c in cs:
            if c[2] not in res: res.append(c[2])
        if ENT.get(eid, {}).get("piece"):
            for r in RECIPES:
                if r["st"] == eid and r["out"] not in res:
                    res.append(r["out"])
        if res:
            m.append("<p class='tags'>%s</p>" % cap(res, 36, lambda x: thumb(x)))
        for t in pv.get("consumed_by", []) or []:
            m.append("<p>%s</p>" % E(t))
        if it and (it["maxq"] or 1) > 1:
            m.append("<p>Улучшение до %s уровня (у станции рецепта).</p>" % it["maxq"])
        if not m:
            m.append("<p class='muted'>Ни во что не превращается и ничего не открывает (по найденным данным).</p>")
        return g, u, d, m

    # создатель блоков для страниц
    def stamp(pid, eid):
        g, u, d, m = blocks(eid)
        p = pages[pid]
        p["bl"] = [["Как получить", "".join(g)], ["Где использовать", "".join(u)], ["Что делать с ним", "".join(d)], ["Что можно сделать", "".join(m)]]
        txt = " ".join(re.sub(r"<[^>]+>", " ", "".join(g + u + d + m)).split())
        p["x"] += " " + txt.lower()

    for eid, e in ENT.items():
        if e.get("pid") and e["pid"] in pages and not e.get("creature"):
            stamp(e["pid"], eid)

    # существа: блоки
    for cid in vcreat:
        c = vch[cid]
        pid = "creature:" + cid
        g = []
        if cid in spawn_by:
            g.append("<p>Обитает: %s.</p>" % E("; ".join(sorted({biomes(m_) for m_ in spawn_by[cid]}))))
        else:
            g.append("<p class='muted'>Биом не определён (появляется из событий, подземелий или по призыву).</p>")
        d_ = c["drops"]
        rows = "".join("<li>%s — %s%s</li>" % (thumb(x[0]), ("%s–%s" % (x[1], x[2])) if x[1] != x[2] else x[1], (" · шанс %d%%" % round((x[3] or 1) * 100)) if (x[3] is not None and x[3] < 1) else "") for x in d_)
        for x in d_:
            rel("c:" + cid, "→ дроп", x[0])
        u = ["<ul>%s</ul>" % rows] if rows else ["<p class='muted'>Ничего не роняет.</p>"]
        dm = []
        if c["boss"]:
            dm.append("<p>Босс: призывается на алтаре трофеями; побеждённый босс открывает новые рецепты и торговлю.</p>")
        tr_ = [x[0] for x in d_ if x[0].startswith("Trophy")]
        mk = []
        for t in tr_:
            tr_out = [r for r in by_in.get(t, [])]
            if tr_out:
                mk.append("<p>Трофей %s — ингредиент: %s</p>" % (thumb(t), cap(tr_out, 8, lambda r: thumb(r["out"]))))
                for r in tr_out:
                    rel("c:" + cid, "→ через трофей", r["out"])
        pages[pid]["bl"] = [["Где обитает", "".join(g)], ["Что роняет", "".join(u)], ["Особенности", "".join(dm) or "<p class='muted'>Особых заметок нет.</p>"], ["Что из этого можно сделать", "".join(mk) or "<p class='muted'>Дроп не используется в рецептах.</p>"]]

    # RAFT страница рецептов
    if raft_pieces:
        rows = "".join("<tr><td><b>%s</b><br><code>%s</code></td><td>%s</td></tr>" % (E(rp["name"]), E(rp["id"]), "".join(thumb(a, b) for a, b in rp["ins"])) for rp in raft_pieces)
        page("raft:recipes", "Рецепты построек RAFT", "raft", "<p>Из кода форка 4.3.5 (Jotunn <code>RequirementConfig</code> и <code>PrefabRecipeConfig</code>). Значения могут меняться настройками. Названия построек — по коду.</p><table class='t wide'><tr><th>Постройка</th><th>Стоимость</th></tr>%s</table>" % rows, tags=["raft", "рецепты"])
        for rp in raft_pieces:
            for a, b in rp["ins"]:
                if a in ENT and ENT[a].get("pid"):
                    REL[ENT[a]["pid"]].append(("→ постройка RAFT", "raft:recipes"))
        # PoV предметы в рецептах RAFT тоже получат блок «используется» через by_in (повторно)

    for x in MODS:
        if x["kind"] == "plan" and x.get("unlocks"):
            rel(x["id"], "→ открывает рецепт", x["unlocks"]); rel(x["unlocks"], "← чертёж", x["id"])
        if x.get("base"):
            rel(x["id"], "← расходует основу", x["base"]); rel(x["base"], "→ основа для", x["id"])
        if x["kind"] == "plate" and "AxeEarly" in ENT:
            rel(x["id"], "→ ставится на", "AxeEarly"); rel("AxeEarly", "← обухи", x["id"])
    # итоговая запись связей
    for pid, lst in REL.items():
        if pid in pages:
            pages[pid]["r"] = lst
    # поисковый текст: связи
    for pid, lst in REL.items():
        if pid in pages:
            names = " ".join(pages[b]["t"] for _, b in lst[:80] if b in pages)
            pages[pid]["x"] += " " + names.lower()
    print("links: ent", len(ENT), "recipes", len(RECIPES), "rel", sum(len(v) for v in REL.values()), "icons", len(icons))
    G["REL_COUNT"] = sum(len(v) for v in REL.values())
    G["RECIPE_COUNT"] = len(RECIPES)


def parse_raft():
    base = r"valheim-raft-4.3.5\decomp\ValheimVehicles"
    names = {}
    pn = open(os.path.join(base, "ValheimVehicles.SharedScripts", "PrefabNames.cs"), encoding="utf-8").read()
    for m in re.finditer(r'public static readonly string (\w+) = (?:ValheimVehiclesPrefix \+ )?"([^"]+)"', pn):
        names[m.group(1)] = ("ValheimVehicles" + m.group(2)) if m.group(0).find("Prefix +") > 0 else m.group(2)
    out = collections.OrderedDict()
    pat_item = re.compile(r'new RequirementConfig\s*\{(.*?)\}', re.S)

    def reqs(block):
        res = []
        for m in pat_item.finditer(block):
            body = m.group(1)
            it = re.search(r'Item = "([^"]+)"', body); am = re.search(r'Amount = (\d+)', body)
            if it:
                res.append((it.group(1), int(am.group(1)) if am else 1))
        return res
    cfg = open(os.path.join(base, "ValheimVehicles.BepInExConfig", "PrefabRecipeConfig.cs"), encoding="utf-8").read()
    for m in re.finditer(r'\{\s*PrefabNames\.(\w+),\s*new RequirementConfig\[\d+\]\s*\{(.*?)\}\s*\}\s*,?\s*(?=\{|\};)', cfg, re.S):
        r = reqs(m.group(2))
        if r:
            out[m.group(1)] = dict(id=names.get(m.group(1), m.group(1)), ins=r, src="PrefabRecipeConfig.cs")
    for root, _, fs in os.walk(base):
        for f in fs:
            if not f.endswith(".cs") or f == "PrefabRecipeConfig.cs":
                continue
            t = open(os.path.join(root, f), encoding="utf-8", errors="replace").read()
            for m in re.finditer(r'Requirements = new RequirementConfig\[\d+\]\s*\{(.*?)\n\t*\}\s*\n', t, re.S):
                pre = t[max(0, m.start() - 2500):m.start()]
                nmatch = re.findall(r'PrefabNames\.(\w+)', pre)
                if not nmatch:
                    continue
                key = nmatch[-1]
                r = reqs(m.group(1))
                if r and key not in out:
                    out[key] = dict(id=names.get(key, key), ins=r, src=f)
    tr = jl(r"valheim-raft-4.3.5\pkg\Assets\Translations\English\valheimraft.json")
    res = []
    for k, v in out.items():
        pid = v["id"]
        nm_ = humanize(re.sub(r"^ValheimVehicles", "", k))
        res.append(dict(id=pid, name=nm_, ins=v["ins"], src=v["src"]))
    return res
