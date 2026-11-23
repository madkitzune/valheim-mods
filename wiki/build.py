# -*- coding: utf-8 -*-
"""Строит data.js для вики сборки Valheim (Path of Valheiman + Epic Loot + Item Drawers + RAFT).
Запуск: python build.py  ->  site/data.js
"""
import json, os, re, sys, html, collections, datetime, base64, io
from PIL import Image

VAL = r"D:\SteamLibrary\steamapps\common\Valheim"
LOC = VAL + r"\BepInEx\plugins\PathOfValheiman\Localization\russian.txt"
CFG = VAL + r"\BepInEx\config"
EL = CFG + r"\EpicLoot\baseconfig"
SC = os.path.join(HERE, "assets", "pov")
RAFT = r"valheim-raft-4.3.5\pkg"
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "site", "data.js")

E = html.escape

PIMG = os.path.dirname(SC)  # папка с извлечёнными картинками PoV
VIMG = os.path.join(HERE, "assets", "vicons")
sys.path.insert(0, HERE)
_img_cache = {}


def img(path, size=96):
    """файл -> data URI (webp); None если файла нет"""
    if not path or not os.path.exists(path):
        return None
    k = (path, size)
    if k not in _img_cache:
        try:
            im = Image.open(path).convert("RGBA")
            im.thumbnail((size, size), Image.LANCZOS)
            buf = io.BytesIO()
            im.save(buf, "WEBP", quality=86, method=6)
            _img_cache[k] = "data:image/webp;base64," + base64.b64encode(buf.getvalue()).decode()
        except Exception as e:
            print("img fail", path, e)
            _img_cache[k] = None
    return _img_cache[k]


def pimg(name, size=96):
    return img(os.path.join(PIMG, "PassiveTree." + name + ".png"), size)

# ---------------------------------------------------------------- localization
loc = {}
english_ru = {}
for line in open(LOC, encoding="utf-8"):
    line = line.rstrip("\n")
    if not line or line.startswith("#"):
        continue
    if "\t" not in line:
        continue
    k, v = line.split("\t", 1)
    if k.startswith("@"):
        loc[k[1:]] = v.replace("\\n", "\n")
    else:
        english_ru[k] = v

# ---------------------------------------------------------------- knobs (for {placeholders})
knobs = {}
try:
    t = open(os.path.join(SC, "Tunables.cs"), encoding="utf-8").read()
    for m in re.finditer(r'new Knob\("([^"]+)",\s*Stat\.\w+,\s*"((?:[^"\\]|\\.)*)",\s*(-?[\d.]+)f,\s*pct:\s*(true|false)', t):
        knobs[m.group(1)] = (float(m.group(3)), m.group(4) == "true")
except Exception as e:
    print("knobs:", e)


for _b, _l in (("meadows", 1), ("blackforest", 19), ("swamp", 43), ("mountain", 77), ("mountains", 77), ("plains", 97),
               ("mistlands", 130), ("ashlands", 181), ("deepnorth", 246), ("ocean", 130)):
    knobs["biome." + _b] = (float(_l), False)


def num(v):
    s = ("%.4f" % v).rstrip("0").rstrip(".")
    return s.replace(".", ",")


PH = re.compile(r"\{([A-Za-z0-9_.\-]+)(?:\|([a-z]+))?\}")


def fill(text):
    def rep(m):
        key, fmt = m.group(1), m.group(2)
        if key in knobs:
            v, pct = knobs[key]
            if fmt == "pct" or pct:
                return num(v * 100) + "%"
            return num(v)
        return '<span class="ph" title="значение подставляется модом из настроек: %s">…</span>' % E(key)
    return PH.sub(rep, text)


LINK = re.compile(r'<link="([^"]+)">(.*?)</link>', re.S)


def inline(text):
    """безопасная инлайн-обработка: link -> ссылка, b/i/color оставить, прочее экранировать"""
    text = text.replace("\r", "")
    keep = []

    def stash(m):
        keep.append(m.group(0))
        return "\x00%d\x00" % (len(keep) - 1)
    text = LINK.sub(lambda m: '\x01%s\x02%s\x03' % (m.group(1), m.group(2)), text)
    text = re.sub(r"<(/?)(b|i)>", lambda m: "\x04%s%s\x05" % (m.group(1), m.group(2)), text)
    text = re.sub(r"<color=[^>]*>", "\x06", text)
    text = text.replace("</color>", "\x07")
    text = E(text)
    text = re.sub(r"\x01([^\x02]+)\x02(.*?)\x03", lambda m: '<a href="#/p/wiki:%s">%s</a>' % (m.group(1), m.group(2)), text, flags=re.S)
    text = re.sub(r"\x04(/?)(b|i)\x05", lambda m: "<%s%s>" % (m.group(1), m.group(2)), text)
    text = text.replace("\x06", '<span class="hl">').replace("\x07", "</span>")
    # ph-spans were already html; fill() runs before E so do it carefully
    return text


def rich(text):
    """текст статьи (с плейсхолдерами) -> html"""
    text = text.replace("\r", "")
    # плейсхолдеры подставляем до экранирования через маркеры
    marks = []

    def ph(m):
        key, fmt = m.group(1), m.group(2)
        if key in knobs:
            v, pct = knobs[key]
            res = (num(v * 100) + "%") if (fmt == "pct" or pct) else num(v)
            marks.append('<b class="val">%s</b>' % res)
        else:
            marks.append('<span class="ph" title="значение задаётся настройками мода: %s">…</span>' % E(key))
        return "\x08%d\x08" % (len(marks) - 1)
    text = PH.sub(ph, text)
    out = []
    in_list = None
    for raw in text.split("\n"):
        line = raw.rstrip()
        if not line.strip():
            if in_list:
                out.append("</%s>" % in_list); in_list = None
            continue
        if line.startswith("## "):
            if in_list: out.append("</%s>" % in_list); in_list = None
            out.append("<h3>%s</h3>" % inline(line[3:])); continue
        if line.startswith("# "):
            if in_list: out.append("</%s>" % in_list); in_list = None
            out.append("<h2>%s</h2>" % inline(line[2:])); continue
        m = re.match(r"^(\d+)\.\s+(.*)$", line)
        if m:
            if in_list != "ol":
                if in_list: out.append("</%s>" % in_list)
                out.append("<ol>"); in_list = "ol"
            out.append("<li>%s</li>" % inline(m.group(2))); continue
        m = re.match(r"^[-•]\s+(.*)$", line)
        if m:
            if in_list != "ul":
                if in_list: out.append("</%s>" % in_list)
                out.append("<ul>"); in_list = "ul"
            out.append("<li>%s</li>" % inline(m.group(1))); continue
        if in_list: out.append("</%s>" % in_list); in_list = None
        out.append("<p>%s</p>" % inline(line))
    if in_list: out.append("</%s>" % in_list)
    res = "\n".join(out)
    return re.sub(r"\x08(\d+)\x08", lambda m: marks[int(m.group(1))], res)


def plain(h):
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", h))).strip()


def human(s):
    s = re.sub(r"([a-z0-9])([A-Z])", r"\1 \2", s)
    return s.replace("_", " ")


# ---------------------------------------------------------------- container
cats = []
pages = {}
order = []


def cat(id, title, parent=None, icon="", desc="", sort=0):
    cats.append(dict(id=id, t=title, p=parent, i=icon, d=desc, o=sort))


def page(id, title, c, body="", fields=None, sub="", kind="article", tags=None, forms=None, extra_text="", im=None):
    pages[id] = dict(id=id, t=title, c=c, s=sub, k=kind, b=body, f=fields or [], g=tags or [], a=forms or [], m=im,
                     x=(title + " " + plain(body) + " " + " ".join(v for _, v in (fields or [])) + " " + extra_text).lower())
    order.append(id)


# ================================================================ КАТЕГОРИИ
cat("guide", "О сборке и гайды", icon="📘", desc="Что установлено, как ставить, как пользоваться и как чинить.", sort=1)
cat("pov", "Энциклопедия Path of Valheiman", icon="📚", desc="Механики боя, оружия, союзников, персонажа, предметов и мира.", sort=2)
cat("items", "Предметы мода", icon="🎒", desc="Пояса, колчаны, посохи, руны, самоцветы, стамески и прочее.", sort=3)
cat("trinkets", "Тринкеты", icon="📿", desc="15 тринкетов Valheim 1.0 и как их использует мод.", sort=4)
cat("classes", "Классы и дерево", icon="🌳", desc="Пути, вознесения, ремесленные мастерства и свершения.", sort=5)
cat("gems", "Самоцветы и эффекты", icon="💎", desc="Самоцветы, статусы, редкость и рунический крафт.", sort=6)
cat("world", "Мир, существа и постройки", icon="🌍", desc="Существа, постройки, сложность и события.", sort=7)
cat("epicloot", "Epic Loot", icon="✨", desc="Волшебные эффекты, легендарные и мифические предметы.", sort=8)
cat("raft", "ValheimRAFT", icon="⛵", desc="Корабли, плоты и управляемые базы (форк 4.3.5).", sort=9)
cat("tools", "Справочники", icon="⚙️", desc="Все настройки, горячие клавиши и словарь.", sort=10)

# ================================================================ ГАЙДЫ ПО СБОРКЕ
G = "guide"

page("guide:start", "Начало: что это за сборка", G, """
<p>Сборка для <b>Valheim 1.0.21</b> на BepInEx 5.4.23.5 (BepInExPack 5.4.2351). Мир — «LovelyPoV», хост — вы, игра для двоих.</p>
<h2>Установлено и загружено</h2>
<ul>
<li><a href="#/p/mod:pov">Path of Valheiman 6.0.6</a> — главный мод: дерево пассивных умений, новая боёвка, лут, рунический крафт, система снаряжения.</li>
<li><a href="#/p/mod:epicloot">Epic Loot 0.15.6</a> — редкость предметов, зачарование, легендарные.</li>
<li><a href="#/p/mod:drawers">Item Drawers 0.5.7</a> — ящики на одну позицию предмета (до 9999).</li>
<li><a href="#/p/mod:guard">Drawers Save Guard 1.1.0</a> — ваш патч: клавиша L кладёт предметы в ящики.</li>
<li><a href="#/p/mod:small">Jotunn 2.30.2, Newtonsoft.Json, Epic Loot ItemDrawers Patch, Display BepInEx Info</a> — вспомогательные.</li>
</ul>
<h2>Выключено намеренно</h2>
<ul>
<li><b>Equipment and Quick Slots 3.1.3</b> — отключён 18.11.2026. Он заставлял Path of Valheiman выключить свою систему снаряжения (пояса, колчаны, ячейки еды переставали работать). Подробности: <a href="#/p/guide:eaqs">конфликт EAQS</a>.</li>
<li><b>ValheimRAFT</b> — убран из игры 26.10.2026. Обновлённый форк 4.3.5 разобран, <a href="#/p/raft:status">установка ждёт</a>.</li>
</ul>
<h2>Куда смотреть дальше</h2>
<ul>
<li><a href="#/p/guide:equipment">Система снаряжения и ячейки</a></li>
<li><a href="#/p/guide:drawers">Раскладка по ящикам (клавиша L)</a></li>
<li><a href="#/p/guide:xp-penalty">Штраф боевого опыта</a></li>
<li><a href="#/c/trinkets">Тринкеты</a></li>
<li><a href="#/p/guide:friend">Что дать другу</a></li>
<li><a href="#/hotkeys">Горячие клавиши</a> и <a href="#/settings">все настройки</a></li>
</ul>
""", tags=["сборка", "старт"])

page("mod:pov", "Path of Valheiman 6.0.6", G, """
<p>Огромный мод-переработка (2420 патчей, 4137 узлов дерева, ~1700 аффиксов лута). Основные части:</p>
<ul>
<li><b>Дерево пассивных умений</b> с классами и вознесениями (<a href="#/c/classes">Классы и дерево</a>).</li>
<li><b>Боёвка</b>: недуги, оглушение, рунический щит, конструкты, баллисты (<a href="#/c/pov">Энциклопедия</a>).</li>
<li><b>Лут и рунический крафт</b>, самоцветы, азурит (<a href="#/c/gems">Самоцветы и эффекты</a>).</li>
<li><b>Система снаряжения</b>: окно рядом с крафтом, портрет, ячейки еды/зелий/стрел/инструментов, колчаны и пояса (<a href="#/p/guide:equipment">гайд</a>).</li>
<li><b>Штраф опыта по биомам</b> (<a href="#/p/guide:xp-penalty">как отключить</a>).</li>
<li>Синхронизация настроек с хоста (<code>Server sync = true</code>).</li>
</ul>
<p>Конфиг: <code>BepInEx\\config\\treextr.pathofvalheiman.cfg</code> (~3400 строк, <a href="#/settings?mod=pov">все настройки</a>). Локализация: <code>plugins\\PathOfValheiman\\Localization\\russian.txt</code> (26 тыс. строк).</p>
""", tags=["мод"])

page("mod:epicloot", "Epic Loot 0.15.6", G, """
<p>Добавляет редкость предметов (Magic → Rare → Epic → Legendary → Mythic), зачарования, авторские легендарные предметы и сеты. PoV читает редкость Epic Loot в подсказках предметов.</p>
<p>Данные сборки: <a href="#/c/epicloot">Epic Loot</a> (эффекты, легендарные, мифические, сеты).</p>
<p>Конфиг: <code>randyknapp.mods.epicloot.cfg</code> и JSON в <code>config\\EpicLoot\\baseconfig</code>.</p>
""", tags=["мод"])

page("mod:drawers", "Item Drawers 0.5.7 (mkz)", G, """
<p>Ящик хранит один вид предмета, до <b>9999</b> штук. Взаимодействие:</p>
<ul>
<li><b>E</b> — взять стак; <b>Alt + E</b> — взять один; <b>Shift + E</b> — положить всё из рюкзака.</li>
<li>Ящик сам подбирает выпавшие предметы того же вида в радиусе 5 м.</li>
</ul>
<p>Настройки синхронизируются с хоста (<code>ServerIsAuthoritative = true</code>). Это <b>версия mkz</b>, не kg: поэтому Path of Valheiman её не знал, пока не появился <a href="#/p/mod:guard">Drawers Save Guard</a>.</p>
""", tags=["мод"])

page("mod:guard", "Drawers Save Guard 1.1.0 (ваш патч)", G, """
<p>Маленький плагин, написанный для этой сборки. Исходники: <code>Dev\\valheim-drawers-patch</code>.</p>
<h2>Что чинит</h2>
<ol>
<li>Ящики mkz — это <code>Container</code>, но они регистрируют только свои сетевые запросы. Когда PoV на клавише <kbd>L</kbd> звал стандартную «разложить по контейнеру», игра писала <code>Failed to find rpc method 1299871265</code> (это <code>RPC_RequestStack</code>). Теперь предметы кладутся в ящик напрямую.</li>
<li>PoV после раскладки вызывал стандартное <code>Container.Save</code>, а у ящика свой формат данных — содержимое могло быть перезаписано. Вызов перенаправляется на сохранение самого ящика.</li>
</ol>
<h2>Как проверить</h2>
<p>В логе после нажатия L: <code>[Drawers] Honey: 50 of 50 put into the drawer</code>.</p>
<h2>Ограничения</h2>
<ul>
<li>Привязан к mkz Item Drawers 0.5.7 и PoV 6.0.6.</li>
<li>Крафт из ящиков через PoV отдельно не проверялся.</li>
<li>Если ящик принадлежит другому игроку, плагин забирает права на ящик при раскладке.</li>
<li>Нужен и вам, и другу.</li>
</ul>
""", tags=["мод", "патч"])

page("mod:small", "Вспомогательные плагины", G, """
<ul>
<li><b>Jotunn 2.30.2</b> — библиотека для модов (предметы, постройки, сетевые запросы). Нужен RAFT и Epic Loot.</li>
<li><b>Newtonsoft.Json Detector</b> — проверка наличия библиотеки JSON.</li>
<li><b>Epic Loot ItemDrawers Patch 1.0.0</b> — ищет другие моды ящиков (kg), с mkz в логе пишет предупреждения «type not found». Безвредно.</li>
<li><b>Display BepInEx Info In-Game 2.0.2</b> — показывает список модов в игре.</li>
</ul>
""", tags=["мод"])

page("guide:equipment", "Система снаряжения: слоты, пояса, колчаны", G, """
<p>Окно снаряжения рядом с окном крафта — часть <b>Path of Valheiman</b> (<code>Equipment system = true</code>). Работает, пока не установлен другой мод слотов (<a href="#/p/guide:eaqs">EAQS</a>, ExtraSlots и т.п.).</p>
<h2>Какие слоты есть</h2>
<ul>
<li><b>Броня и вещи:</b> шлем, нагрудник, поножи, плащ, пояс, колчан, тринкет(и), утилити, кольца, кошелёк, ключи.</li>
<li><b>Ячейки еды</b> (3): Shift + Z / X / C — съесть.</li>
<li><b>Ячейки зелий</b> (3): Shift + V / B / N.</li>
<li><b>Ячейки стрел</b> (до 3, открывает колчан): Shift + Q — следующие стрелы.</li>
<li><b>Ячейки инструментов</b> (8) под хотбаром: Alt + 1…8.</li>
</ul>
<h2>Пояса</h2>
<p>11 поясов по стадиям игры (<a href="#/c/items">Предметы мода</a>): от «Пояса первой тропы» (Луга) до «Пояса безмолвной стужи» (Глубокий Север), плюс «Дань Хель» и «Кровь Квасира». Пояс пассивный: его надо надеть в слот «Пояс», открывать нечего. Бонус — в подсказке (например, у «Пояса лесного дозора»: <b>+2 к ячейкам еды</b>).</p>
<h2>Колчаны</h2>
<p>Колчан носится за спиной и открывает 1–3 ячейки боеприпасов (стрелы и болты), а также даёт бонус к урону стрел. Бонусы определяются, когда колчан попадает в сумку.</p>
<table class="t"><tr><th>Колчан</th><th>Ячеек стрел</th><th>Бонус</th></tr>
<tr><td>Колчан первой охоты</td><td>1</td><td>+колющий урон стрел и болтов</td></tr>
<tr><td>Колчан угольного следа</td><td>1</td><td>+урон огнём стрел и болтов</td></tr>
<tr><td>Колчан снежного дозора</td><td>2</td><td>+эффективная дальность стрел</td></tr>
<tr><td>Колчан двергского расчёта</td><td>2</td><td>+скорость полёта стрел</td></tr>
<tr><td>Колчан пепельного пути</td><td>3</td><td>+накопление разрушения брони</td></tr></table>
<h2>Если слоты не появились</h2>
<p>Смотрите в <code>LogOutput.log</code>: должна быть строка <code>[Equipment] Equipment system on.</code>. Строка <code>Equipment system off: … is installed</code> означает конфликтующий мод слотов.</p>
""", tags=["снаряжение", "пояс", "колчан"])

page("guide:eaqs", "Конфликт: Equipment and Quick Slots", G, """
<p>Path of Valheiman при старте ищет в списке плагинов <code>ExtraSlots</code>, <code>AzuExtendedPlayerInventory</code>, <code>ExtendedPlayerInventory</code>, <code>equipmentandquickslots</code>, <code>ComfyQuickSlots</code>. Если находит — <b>выключает свою систему снаряжения</b>: пояса теряют бонусы к ячейкам, колчаны не получают слот.</p>
<p>В логе это выглядело так: <code>Equipment system off: Equipment and Quick Slots is installed and keeps the equipment slots.</code></p>
<h2>Что сделано</h2>
<ul>
<li>18.11.2026 EAQS отключён: файл переименован в <code>EquipmentAndQuickSlots.dll.disabled</code>.</li>
<li>Бэкап сейва, конфига и DLL: <code>Valheim\\_backup\\2026-11-18</code>.</li>
<li>Слоты PoV (Head, Chest, Legs, Cape, Utility, Trinket…) и ячейки еды/зелий/стрел/инструментов заменили слоты и быстрые слоты EAQS.</li>
</ul>
<h2>Как вернуть EAQS</h2>
<p>Переименуйте файл обратно в <code>.dll</code> (и снова получите отключённую систему PoV).</p>
<p class="note">Плюс EAQS — настраиваемое число быстрых слотов (до 6) и утилити (до 3); PoV даёт второй слот утилити умением «Полная сбруя».</p>
""", tags=["конфликт", "eaqs"])

page("guide:drawers", "Раскладка по ящикам (клавиша L)", G, """
<p>Клавиша <kbd>L</kbd> (кнопка «Stack nearby» в окне инвентаря) кладёт предметы из рюкзака в сундуки поблизости, где такие предметы уже лежат. Нужно умение <b>Tidy Pack</b>. Радиус — 10 м (<code>Stack nearby range</code>). Сортировка рюкзака — <kbd>K</kbd>.</p>
<h2>С ящиками mkz Item Drawers</h2>
<p>Работает благодаря <a href="#/p/mod:guard">Drawers Save Guard</a>: в ящик уходят одинаковые предметы из рядов рюкзака (не хотбар и не слоты снаряжения). В логе: <code>[Drawers] Honey: 50 of 50 put into the drawer</code>.</p>
<h2>Если не работает</h2>
<ul>
<li>В ящике должен лежать хотя бы один такой предмет — пустой ящик не принимает.</li>
<li>Ящик, чей владелец не вы, может не обновиться.</li>
<li>Нет строки <code>Drawer guard active</code> в логе → плагин не загрузился (проверьте <code>BepInEx\\plugins\\DrawersSaveGuard</code>).</li>
</ul>
<h2>Крафт из ближних сундуков</h2>
<p>Радиус — <code>Chest reach at a workbench</code> (по умолчанию = радиус верстака). Для ящиков отдельно не проверялось.</p>
""", tags=["ящики", "L"])

page("guide:xp-penalty", "Штраф боевого опыта по биомам", G, """
<p>Когда вы намного выше рекомендуемого уровня биома, опыт за бой падает (на экране: «Штраф боевого опыта −93%»). Пол — 5% от обычного.</p>
<table class="t"><tr><th>Биом</th><th>Рекомендуемый уровень</th></tr>
<tr><td>Луга</td><td>1</td></tr><tr><td>Чёрный лес</td><td>19</td></tr><tr><td>Болото</td><td>43</td></tr><tr><td>Горы</td><td>77</td></tr>
<tr><td>Равнины</td><td>97</td></tr><tr><td>Туманные земли</td><td>130</td></tr><tr><td>Пепельные земли</td><td>181</td></tr><tr><td>Глубокий Север</td><td>246</td></tr></table>
<p>Полный опыт — до уровня следующего биома, дальше плавно падает до 5% через 10 уровней.</p>
<h2>Как убрать (cfg PoV, строки ~960–981)</h2>
<ul>
<li><code>Biome experience floor = 1</code> — штраф выключен совсем.</li>
<li><code>Biome experience floor = 0.3</code> — минимум 30% вместо 5%.</li>
<li><code>Biome levels forgiven = 20</code> — 20 лишних уровней полного опыта.</li>
<li><code>Biome gap at the floor</code> — растянуть спад (до 100).</li>
<li><code>Recommended player levels</code> — пороги по биомам.</li>
</ul>
<p>Правьте файл при закрытой игре. У вас хост, настройки синхронизируются другу при входе.</p>
""", tags=["опыт", "настройки"])

page("guide:friend", "Что дать другу и куда класть", G, """
<p>Готовый архив: <code>Dev\\valheim-drawers-patch\\ValheimFriendPack.zip</code> (235 МБ). Внутри — <code>README.txt</code>.</p>
<h2>Требования</h2>
<ol>
<li>Valheim <b>1.0.21</b>.</li>
<li>BepInExPack Valheim <b>5.4.2351</b> (Thunderstore).</li>
<li>Выключены: Equipment and Quick Slots, ExtraSlots, ExtendedPlayerInventory, ComfyQuickSlots.</li>
</ol>
<h2>Установка</h2>
<p>Распаковать прямо в папку игры (рядом с <code>valheim.exe</code>), согласившись на слияние и замену. Получится <code>Valheim\\BepInEx\\plugins\\PathOfValheiman\\…</code>.</p>
<h2>Что внутри архива</h2>
<p>Path of Valheiman, Epic Loot, Item Drawers, Drawers Save Guard, Epic Loot ItemDrawers Patch, Jotunn, Newtonsoft.Json Detector, Display BepInEx Info.</p>
<h2>Настройки</h2>
<p>Конфиги слать не нужно: PoV, Epic Loot и Item Drawers синхронизируют баланс с хоста при входе.</p>
<h2>Проверка</h2>
<p>В <code>LogOutput.log</code>: <code>Chainloader startup complete (8 loaded, 0 skipped, 0 failed)</code> и после входа в мир <code>[Equipment] Equipment system on.</code></p>
<p class="note">Архив для личного обмена. Это сторонние моды — публично не выкладывайте.</p>
""", tags=["друг", "установка"])

page("guide:paths", "Где что лежит и бэкапы", G, """
<table class="t">
<tr><th>Что</th><th>Путь</th></tr>
<tr><td>Игра</td><td><code>D:\\SteamLibrary\\steamapps\\common\\Valheim</code></td></tr>
<tr><td>Плагины / конфиги / лог</td><td><code>BepInEx\\plugins</code> · <code>BepInEx\\config</code> · <code>BepInEx\\LogOutput.log</code></td></tr>
<tr><td>Vortex (staging)</td><td><code>D:\\Vortex Mods\\valheim</code></td></tr>
<tr><td>Персонаж</td><td><code>C:\\Program Files (x86)\\Steam\\userdata\\1317666527\\892970\\remote\\characters\\lihtern.fch</code></td></tr>
<tr><td>Мир</td><td><code>…\\remote\\worlds\\LovelyPoV</code></td></tr>
<tr><td>Бэкапы правок</td><td><code>Valheim\\_backup\\&lt;дата&gt;</code></td></tr>
<tr><td>Патч для ящиков (исходники)</td><td><code>drawers-save-guard</code></td></tr>
<tr><td>Разбор RAFT 4.3.5</td><td><code>valheim-raft-4.3.5</code></td></tr>
</table>
<p class="note">Правила: конфиги править только при закрытой игре (BepInEx перезаписывает cfg при выходе); перед правкой делать бэкап.</p>
""", tags=["пути"])

page("guide:troubleshoot", "Типичные проблемы", G, """
<table class="t">
<tr><th>Симптом</th><th>Причина и решение</th></tr>
<tr><td>Пояс/колчан «висит», нет слотов еды или стрел</td><td>Система снаряжения PoV выключена другим модом слотов. Проверьте лог на <code>Equipment system off</code>. <a href="#/p/guide:eaqs">EAQS</a></td></tr>
<tr><td>Клавиша L ничего не кладёт в ящик</td><td>Нет Drawers Save Guard или в ящике нет такого предмета. <a href="#/p/guide:drawers">Гайд</a></td></tr>
<tr><td>В логе <code>Failed to find rpc method 1299871265</code></td><td>Ящик mkz без патча. Ставьте Drawers Save Guard 1.1.0.</td></tr>
<tr><td>«Штраф боевого опыта −93%»</td><td>Вы переросли биом. <a href="#/p/guide:xp-penalty">Как убрать</a></td></tr>
<tr><td>Друг видит другие значения</td><td>Настройки идут с хоста. Проверьте, что он перезашёл, и что у него те же версии модов.</td></tr>
<tr><td>Предупреждения <code>Could not find type … kg_ItemDrawers / QuickStackStore / BestAutoSort</code></td><td>Безвредно: моды ищут чужие дополнения, которых нет.</td></tr>
<tr><td>Мод в Vortex «сам включился»</td><td>Vortex при деплое восстанавливает отключённые файлы — отключайте в самом Vortex.</td></tr>
</table>
""", tags=["проблемы", "faq"])

# ================================================================ ТРИНКЕТЫ
TRINKETS = [
    ("TrinketBronzeHealth", "Heart of the Forest", "Чёрный лес", "5 бронзы, 5 древних семян, трофей шамана", "Кузница", "50", "60 с", "+25% регена здоровья"),
    ("TrinketBronzeStamina", "Bronze Pendant", "Чёрный лес", "5 бронзы, трофей медведя, 3 рубина", "Кузница", "50", "60 с", "+25% регена выносливости"),
    ("TrinketIronHealth", "Iron Brooch", "Болото", "5 железа, 5 древней коры, трофей пиявки", "Кузница", "65", "30 с", "+50 здоровья, +20 брони"),
    ("TrinketIronStamina", "Nimble Anklet", "Болото", "5 железа, 5 иссохших костей, трофей Суртлинга", "Кузница", "60", "30 с", "+50 выносливости, +15% скорости"),
    ("TrinketChitinSwim", "Fins of Destiny", "Океан", "5 хитина, 5 чешуи змея, трофей змея", "Кузница", "10", "120 с", "+50% скорость плавания, −80% расход выносливости"),
    ("TrinketSilverDamage", "Wolf Sight", "Горы", "5 серебра, 5 клыков волка, трофей Фенринга", "Кузница", "55", "30 с", "+20 лук и копьё, +10% колющий урон"),
    ("TrinketSilverResist", "Crystal Heart", "Горы", "5 серебра, 5 кристаллов, трофей каменного голема", "Кузница", "80", "50 с", "−25% физического урона"),
    ("TrinketBlackDamageHealth", "Bracelets of the Brave", "Равнины", "5 чёрного металла, 5 шкур локса, трофей локса", "Кузница", "85", "60 с", "+100 здоровья, +20 дубины, +10% дробящий урон"),
    ("TrinketBlackStamina", "Evasion Mantle", "Равнины", "5 чёрного металла, 10 льна, трофей берсерка Фулингов", "Кузница", "60", "120 с", "−50% расход на блок, +50% бонус парирования, +20 уклонение"),
    ("TrinketCarapaceEitr", "Pulsating Earrings", "Туманные земли", "5 панцирей, 5 очищенного эйтра, трофей Гьялла", "Чёрная кузница", "65", "60 с", "+25% регена эйтра"),
    ("TrinketScaleStaminaDamage", "Resounding Shackle", "Туманные земли", "5 очищенного эйтра, 5 чешуйчатой шкуры, жвалы, трофей искателя", "Чёрная кузница", "75", "60 с", "+100 выносливости, +10% рубящий урон"),
    ("TrinketFlametalEitr", "Jörmundling", "Пепельные земли", "5 огнемета, 5 очищенного эйтра, кровавый камень, трофей павшей валькирии", "Чёрная кузница", "70", "60 с", "+100 эйтра, +20 магия стихий, +20 магия крови"),
    ("TrinketFlametalStaminaHealth", "Brimstone", "Пепельные земли", "5 огнемета, 5 серы, расплавленное ядро, трофей Моргена", "Чёрная кузница", "100", "мгновенно", "+100 здоровья и выносливости"),
    ("TrinketBloodGoldHealth", "(не установлено)", "не установлено", "—", "—", "—", "—", "не установлено"),
    ("TrinketBloodGoldStamina", "(не установлено)", "не установлено", "—", "—", "—", "—", "не установлено"),
]
_V = json.load(open(os.path.join(HERE, "data", "vanilla.json"), encoding="utf-8"))
_ST = {"forge": "Кузница", "blackforge": "Чёрная кузница"}
_T2 = []
for pid, name, stage, ing, st, adr, dur, eff in TRINKETS:
    _it = _V["items"].get(pid)
    _r = [r for r in _V["recipes"] if r["out"] == pid]
    if _it:
        name = "%s (%s)" % (_it["ru"].strip(), _it["en"])
    if _r:
        ing = ", ".join("%s ×%s" % ((_V["items"].get(a[0]) or {}).get("ru", a[0]).strip(), a[1]) for a in _r[0]["ins"])
        st = _ST.get(_r[0]["st"], _r[0]["st"])
        if stage == "не установлено":
            stage = "Туманные земли и дальше" if _r[0]["st"] == "blackforge" else "—"
    _T2.append((pid, name, stage, ing, st, adr, dur, eff))
TRINKETS = _T2
rows = ""
for pid, name, stage, ing, st, adr, dur, eff in TRINKETS:
    _ic = img(os.path.join(VIMG, pid + ".png"), 64)
    rows += "<tr><td>%s<b>%s</b><br><code>%s</code></td><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td></tr>" % (('<img class="ic" src="%s" alt="">' % _ic) if _ic else "", E(name), pid, E(stage), E(ing + " · " + st), adr, dur, E(eff))
page("trinkets:all", "Все 15 тринкетов (таблица)", "trinkets", """
<p>Тринкеты — предметы самой игры (Valheim 1.0, система адреналина). Path of Valheiman их не добавляет, но меняет: даёт несколько слотов через дерево умений, добавляет собственные базовые бонусы (в коде значения 6–10) и учитывает «слот руки» тринкета (в щитовую руку нельзя взять щит/факел).</p>
<table class="t wide"><tr><th>Тринкет</th><th>Стадия</th><th>Рецепт · станция</th><th>Адреналин</th><th>Длит.</th><th>Эффект при активации</th></tr>%s</table>
<p class="note"><b>Достоверность:</b> имена из кода мода сопоставлены с названиями из гайдов ([holy.gg], [gamesfuze]) по смыслу. Стадия выведена из ингредиентов — сайты её не называют. Два предмета «BloodGold» с источниками не совпали (возможно, это новые тринкеты Valheim 1.0: Neckstabber и Witch Crown). Числа эффектов из общих гайдов; PoV добавляет к ним свои бонусы.</p>
""" % rows, tags=["тринкеты"])
for pid, name, stage, ing, st, adr, dur, eff in TRINKETS:
    page("trinket:" + pid, name if not name.startswith("(") else pid, "trinkets", "<p>%s</p>" % E(eff),
         im=img(os.path.join(VIMG, pid + ".png"), 96),
         fields=[("Прототип в коде", pid), ("Стадия", stage), ("Рецепт", ing), ("Станция", st), ("Адреналин", adr), ("Длительность", dur), ("Эффект", eff)],
         sub="Тринкеты")

# ================================================================ ПРЕДМЕТЫ PoV
item_ids = collections.OrderedDict()
for k in loc:
    m = re.match(r"^item\.([^.]+)\.(.+)$", k)
    if m:
        item_ids.setdefault(m.group(1), {})[m.group(2)] = loc[k]


def item_group(i):
    if i.startswith("PT_Belt"): return "Пояса"
    if "Quiver" in i: return "Колчаны"
    if "Staff" in i or "Sceptre" in i or "Scepter" in i: return "Посохи и скипетры"
    if "Chisel" in i: return "Стамески"
    if "Jewel" in i: return "Уникальные самоцветы"
    if "Rune" in i or "Runestone" in i or "Fragment" in i: return "Руны"
    if "Azurite" in i or "Visage" in i: return "Азурит и лики"
    if "Ring" in i: return "Кольца"
    if i.startswith("packed"): return "Упакованные постройки"
    if "Bomb" in i or "Oil" in i or "Essence" in i: return "Бомбы, масла, сущности"
    if "Gem" in i: return "Камни эффектов"
    return "Прочее"


cat_items = {}
for iid, d in item_ids.items():
    name = d.get("name")
    if not name:
        continue
    g = item_group(iid)
    f = [("Код предмета", iid), ("Группа", g)]
    body = ""
    for key, label in (("flavor", "Описание"), ("desc", "Эффект"), ("description", "Эффект"), ("effect", "Эффект"), ("text", "Текст"), ("quote", "Цитата"), ("about", "О предмете")):
        if key in d:
            body += "<p><b>%s.</b> %s</p>" % (label, inline(fill(d[key]) if False else d[key]))
    if not body:
        body = "<p class=\"muted\">У предмета нет собственного описания в локализации (бонусы — в подсказке в игре).</p>"
    body = rich_body = re.sub(r"\{[^}]+\}", "…", body) if False else body
    page("item:" + iid, name, "items", body, fields=f, sub=g, im=pimg("item." + iid, 96))

# ================================================================ КЛАССЫ, ПУТИ, ПРОФЕССИИ, СВЕРШЕНИЯ
paths = collections.OrderedDict()
for k, v in loc.items():
    m = re.match(r"^path\.([^.]+)\.(name|attr|of)$", k)
    if m:
        paths.setdefault(m.group(1), {})[m.group(2)] = v
body = "<table class='t'><tr><th>Путь</th><th>Атрибут</th></tr>" + "".join("<tr><td>%s</td><td>%s</td></tr>" % (E(d.get("name", i)), E(d.get("attr", ""))) for i, d in paths.items()) + "</table>"
page("classes:paths", "Пути (стартовые классы)", "classes", "<p>Выбор пути определяет стартовую область дерева и ключевой атрибут.</p>" + body, tags=["классы"])

classes = collections.OrderedDict()
for k, v in loc.items():
    m = re.match(r"^class\.([^.]+)\.(name|sub|goal)$", k)
    if m:
        classes.setdefault(m.group(1), {})[m.group(2)] = v
asc_nodes = collections.OrderedDict()
for _k, _v in loc.items():
    _m = re.match(r"^class\.([a-z0-9_\-]+)\.([a-z0-9_\-]+)$", _k)
    if _m and _m.group(2) not in ("name", "sub", "goal"):
        asc_nodes.setdefault(_m.group(1), collections.OrderedDict())[_m.group(2)] = _v
ART_SLUGS = sorted((re.sub(r"-512$", "", f[len("PassiveTree.classart."):-4]) for f in os.listdir(PIMG) if f.startswith("PassiveTree.classart.") and f.endswith("-512.png")), key=len, reverse=True)
fam = collections.OrderedDict()
for i, d in classes.items():
    parts = i.split("-")
    key = None
    for sl in ART_SLUGS:
        if i == sl or i.startswith(sl + "-"):
            key = sl
            break
    if key is None:
        key = "-".join(parts[:2]) if parts[0] == "asc" and len(parts) > 1 else parts[0]
    fam.setdefault(key, []).append((i, d))
for f_, lst in fam.items():
    rows = ""
    for i, d in lst:
        rows += "<tr><td>%s</td><td>%s</td><td>%s</td></tr>" % (E(d.get("name", i)), E(d.get("sub", "")), E(d.get("goal", "")))
    # имя: русское из class.<fam>.name, иначе по ключу вознесения
    ru = None
    for i, d in lst:
        if i == f_ and d.get("name"):
            ru = d["name"]
    if f_.startswith("asc-"):
        title = "Вознесение: " + f_[4:].replace("-", " ").title()
        sub = "Вознесения"
    else:
        title = ru or f_.replace("_", " ").title()
        sub = "Классовые деревья"
    art = pimg("classart." + f_ + "-512", 280) or pimg("badge." + f_, 160) or pimg("class." + f_, 160)
    nd = asc_nodes.get(f_, {})
    big = [(k, v) for k, v in nd.items() if not k.endswith("-small")]
    small = [(k, v) for k, v in nd.items() if k.endswith("-small")]
    body = ""
    if f_.startswith("asc-"):
        body += "<p>Вознесение — финальная ветка дерева класса. Ключевых узлов: <b>%d</b>, малых: <b>%d</b>.</p>" % (len(big), len(small))
    else:
        body += "<p>Записей в локализации: <b>%d</b>.</p>" % len(lst)
    if big:
        body += "<h2>Ключевые узлы</h2><table class='t'><tr><th>Узел</th><th>Код</th></tr>" + "".join("<tr><td><b>%s</b></td><td><code>%s</code></td></tr>" % (E(v), k) for k, v in big) + "</table>"
    if small:
        body += "<h2>Малые узлы</h2><table class='t'><tr><th>Бонус</th><th>Код</th></tr>" + "".join("<tr><td>%s</td><td><code>%s</code></td></tr>" % (E(v), k) for k, v in small) + "</table>"
    if rows:
        body += "<h2>Подписи</h2><table class='t'><tr><th>Узел</th><th>Подпись</th><th>Цель</th></tr>%s</table>" % rows
    page("class:" + f_, title, "classes", body, fields=[("Ключ", f_), ("Ключевых узлов", str(len(big))), ("Малых узлов", str(len(small)))], sub=sub, im=art)

profs = collections.OrderedDict()
for k, v in loc.items():
    m = re.match(r"^prof\.([^.]+)\.(.+)$", k)
    if m:
        profs.setdefault(m.group(1), {})[m.group(2)] = v
rows = ""
for i, d in profs.items():
    rows += "<tr><td>%s</td><td>%s</td><td>%s</td><td><code>%s</code></td></tr>" % (E(d.get("name", i)), E(d.get("a.name", "")), E(d.get("b.name", "")), i)
page("classes:profs", "Ремесленные мастерства", "classes", "<p>Профессии открываются по мере работы (добыча, стройка, плавка, охота…). У каждой — две ветви.</p><table class='t'><tr><th>Мастерство</th><th>Ветвь A</th><th>Ветвь B</th><th>Код</th></tr>%s</table>" % rows, tags=["ремесло"])

deeds = collections.OrderedDict()
for k, v in loc.items():
    m = re.match(r"^deed\.([^.]+)\.(t\d+)\.([a-z])\.(name|hint)$", k)
    if m:
        deeds.setdefault(m.group(1), collections.OrderedDict()).setdefault(m.group(2) + m.group(3), {})[m.group(4)] = v
rows = ""
for dname, tiers in deeds.items():
    for tk, d in tiers.items():
        rows += "<tr><td><code>%s</code></td><td>%s</td><td><b>%s</b></td><td>%s</td></tr>" % (dname, tk, E(d.get("name", "")), E(re.sub(r"\{[^}]+\}", "N", d.get("hint", ""))))
page("classes:deeds", "Свершения (deeds)", "classes", "<p>Свершения дают очки и открывают узлы. Числа в «N» подставляются модом.</p><table class='t wide'><tr><th>Серия</th><th>Ступень</th><th>Название</th><th>Задача</th></tr>%s</table>" % rows, tags=["свершения"])

# ================================================================ САМОЦВЕТЫ, СТАТУСЫ, РЕДКОСТЬ
gems = collections.OrderedDict()
for k, v in loc.items():
    m = re.match(r"^gem\.([^.]+)\.(name|text)$", k)
    if m:
        gems.setdefault(m.group(1), {})[m.group(2)] = v
for gid, d in gems.items():
    page("gem:" + gid, d.get("name", gid), "gems", rich(d.get("text", "")), fields=[("Код", gid)], sub="Самоцветы", im=pimg("gemhex." + gid, 96))

stat = collections.OrderedDict()
for k, v in loc.items():
    m = re.match(r"^status\.([^.]+)\.(name|tip)$", k)
    if m:
        stat.setdefault(m.group(1), {})[m.group(2)] = v
for sid, d in stat.items():
    page("status:" + sid, d.get("name", sid), "gems", "<p>%s</p>" % E(d.get("tip", "")), fields=[("Код", sid)], sub="Статусы")

rar = collections.OrderedDict()
for k, v in loc.items():
    m = re.match(r"^rarity\.([^.]+)$", k)
    if m: rar[m.group(1)] = v
page("gems:rarity", "Редкость предметов", "gems", "<ul>" + "".join("<li><b>%s</b> <code>%s</code></li>" % (E(v), k) for k, v in rar.items()) + "</ul>", tags=["редкость"])

runic = {k: v for k, v in loc.items() if k.startswith("runic.")}
page("gems:runic", "Рунический крафт (подсказки)", "gems", "<ul>" + "".join("<li><code>%s</code>: %s</li>" % (k, inline(re.sub(r'\{[^}]+\}', '…', v))) for k, v in sorted(runic.items())[:120]) + "</ul>", tags=["руны"])

# ================================================================ МИР
cr = collections.OrderedDict()
for k, v in loc.items():
    m = re.match(r"^creature\.([^.]+)\.name$", k)
    if m: page("creature:" + m.group(1), v, "world", "", fields=[("Код", m.group(1))], sub="Существа")
for k, v in loc.items():
    m = re.match(r"^piece\.([^.]+)\.name$", k)
    if m: page("piece:" + m.group(1), v, "world", "", fields=[("Код", m.group(1))], sub="Постройки")
diff = [(k, v) for k, v in loc.items() if k.startswith("difficulty.") and k.count(".") == 1]
tips = {k: v for k, v in loc.items() if k.startswith("difficulty.") and k.endswith(".tip")}
page("world:difficulty", "Уровни сложности", "world", "<ul>" + "".join("<li><b>%s</b>: %s</li>" % (E(v), E(tips.get(k + ".tip", ""))) for k, v in diff) + "</ul>", tags=["сложность"])
power = {k: v for k, v in loc.items() if k.startswith("power.")}
page("world:power", "Алтарь силы монстров", "world", "<ul>" + "".join("<li>%s</li>" % E(re.sub(r"\{[^}]+\}", "N", v)) for v in power.values()) + "</ul>", tags=["сила"])

# ================================================================ ЭНЦИКЛОПЕДИЯ (wiki.*)
groups = collections.OrderedDict()
src = open(os.path.join(SC, "Wiki.cs"), encoding="utf-8").read()
term_group = {}
for m in re.finditer(r'Group\("([a-z\-]+)", \(\) => L\.Id\("wiki\.group\.[a-z\-]+", "([^"]+)"\), (.*?)\)\),?\s*$', src, re.M):
    gid, gtitle, rest = m.group(1), m.group(2), m.group(3)
    gname = loc.get("wiki.group." + gid, gtitle)
    for sm in re.finditer(r'Sub\("([a-z\-]+)", \(\) => L\.Id\("wiki\.group\.[a-z\-]+", "[^"]+"\), ((?:"[a-z0-9\-]+"(?:, )?)+)\)', rest):
        sname = loc.get("wiki.group." + sm.group(1), sm.group(1))
        for t in re.findall(r'"([a-z0-9\-]+)"', sm.group(2)):
            term_group[t] = (gname, sname)
terms = collections.OrderedDict()
for k, v in loc.items():
    m = re.match(r"^wiki\.([a-z0-9\-]+)\.(title|text|forms|long)$", k)
    if m:
        terms.setdefault(m.group(1), {})[m.group(2)] = v
for tk, d in terms.items():
    if "title" not in d:
        continue
    gname, sname = term_group.get(tk, ("Прочие термины", ""))
    body = ""
    if d.get("text"): body += rich(d["text"])
    if d.get("long"): body += rich(d["long"])
    forms = [x.strip() for x in d.get("forms", "").split(",") if x.strip()]
    page("wiki:" + tk, d["title"], "pov", body, fields=[], sub=gname + ((" › " + sname) if sname else ""), forms=forms, tags=[gname, sname])

# ================================================================ EPIC LOOT
def jload(p):
    return json.load(open(p, encoding="utf-8-sig"))


try:
    me = jload(EL + r"\magiceffects.json")["MagicItemEffects"]
    rows = ""
    for e in me:
        req = e.get("Requirements", {}) or {}
        types = ", ".join(req.get("AllowedItemTypes", []) or [])
        skills = ", ".join(req.get("AllowedSkillTypes", []) or [])
        rar_ = ", ".join(req.get("AllowedRarity", []) or [])
        vals = e.get("ValuesPerRarity", {}) or {}
        vtxt = "; ".join("%s: %s–%s" % (r, v.get("MinValue"), v.get("MaxValue")) for r, v in vals.items() if isinstance(v, dict))
        rows += "<tr><td><b>%s</b></td><td>%s</td><td>%s</td><td>%s</td></tr>" % (E(human(e["Type"])), E(types or ("не выпадает" if req.get("NoRoll") else "любые")), E(skills), E(vtxt or "—"))
    page("el:effects", "Волшебные эффекты (%d)" % len(me), "epicloot", "<p>Из <code>magiceffects.json</code>. Названия — по коду эффекта (игровые строки лежат в DLL).</p><table class='t wide'><tr><th>Эффект</th><th>Типы предметов</th><th>Навыки</th><th>Значения по редкости</th></tr>%s</table>" % rows, tags=["epic loot", "эффекты"])
    lg = jload(EL + r"\legendaries.json")
    for sect, title in (("LegendaryItems", "Легендарные предметы"), ("MythicItems", "Мифические предметы"), ("LegendarySets", "Легендарные сеты"), ("MythicSets", "Мифические сеты")):
        lst = lg.get(sect, [])
        rows = ""
        for e in lst:
            req = e.get("Requirements", {}) or {}
            ge = ", ".join(human(x.get("Type", "")) for x in e.get("GuaranteedMagicEffects", []) or [])
            if sect.endswith("Items"):
                rows += "<tr><td><b>%s</b></td><td>%s</td><td>%s</td><td>%s</td></tr>" % (E(human(e.get("ID", ""))), E(", ".join(req.get("AllowedItemTypes", []) or [])), E(", ".join(req.get("AllowedSkillTypes", []) or [])), E(ge))
            else:
                rows += "<tr><td><b>%s</b></td><td colspan=3>%s</td></tr>" % (E(human(e.get("ID", ""))), E(", ".join(human(x) for x in e.get("Items", []) or [e.get("Name", "")])))
        head = "<tr><th>ID</th><th>Тип</th><th>Навык</th><th>Гарантированные эффекты</th></tr>" if sect.endswith("Items") else "<tr><th>Сет</th><th>Состав</th></tr>"
        page("el:" + sect, "%s (%d)" % (title, len(lst)), "epicloot", "<table class='t wide'>%s%s</table>" % (head, rows), tags=["epic loot"])
except Exception as e:
    print("epicloot:", e)

# ================================================================ RAFT
try:
    tr = json.load(open(RAFT + r"\Assets\Translations\English\valheimraft.json", encoding="utf-8-sig"))
    pcs = collections.OrderedDict()
    for k, v in tr.items():
        base = k[:-5] if k.endswith("_desc") else k
        pcs.setdefault(base, {})["desc" if k.endswith("_desc") else "name"] = v
    rows = "".join("<tr><td><b>%s</b></td><td>%s</td><td><code>%s</code></td></tr>" % (E(d.get("name", b)), E(d.get("desc", "")), b) for b, d in pcs.items() if "name" in d)
    page("raft:pieces", "Постройки и предметы RAFT (%d)" % len(pcs), "raft", "<table class='t wide'><tr><th>Название</th><th>Описание</th><th>Код</th></tr>%s</table>" % rows, tags=["raft", "постройки"])
except Exception as e:
    print("raft tr:", e)

page("raft:status", "ValheimRAFT: статус в сборке", "raft", """
<p>Сейчас RAFT <b>не установлен</b> (убран 26.10.2026). Разобран форк <b>team0/ValheimRAFT 4.3.5</b> (JNDEV0, ответвление Zolantris), собранный под Valheim 1.0.12 / Unity 6. Лицензия в пакете — GPLv3.</p>
<h2>Состав пакета</h2>
<p>ValheimRAFT, ValheimVehicles (основной), ZdoWatcher, DynamicLocations, Zolantris.Shared — 5 DLL, всего 40 МБ. Зависимости: BepInExPack ≥5.4.2350, Jotunn ≥2.30.0, JsonDotNET ≥13.0.4 (у вас всё есть).</p>
<h2>Совпадения патчей с вашими модами</h2>
<table class="t"><tr><th>Мод</th><th>Общих методов с RAFT</th></tr>
<tr><td>Path of Valheiman</td><td>38</td></tr><tr><td>Epic Loot</td><td>26</td></tr><tr><td>Item Drawers</td><td>4</td></tr></table>
<p>Большинство патчей RAFT пассивные (Postfix). Блокирующих всего 7: <code>PlanPiece.CalculateSupported</code>, <code>Player.UpdateTeleport</code> и пять патчей «рога судна» (<code>StartAttack/BlockAttack/IsBlocking/UpdateBlock/UpdateActionProgress</code>), срабатывающих только с рогом в руке. Самые чувствительные места: <code>Humanoid.EquipItem/UnequipItem</code> (RAFT, Epic Loot и PoV все патчат), <code>Player.UpdateTeleport</code>.</p>
<h2>Риски</h2>
<ul>
<li>Форк помечен как <b>устаревший (Deprecated)</b> на Thunderstore.</li>
<li>Собран под 1.0.12, у вас 1.0.21 — совместимость узнаем только в игре.</li>
<li>Совместимость миров с плотами из 1.4.9 не описана.</li>
</ul>
<h2>План установки</h2>
<ol><li>Закрыть игру, бэкап.</li><li>Положить плагины в <code>BepInEx\\plugins</code> отдельной папкой.</li><li>Запуск, чтение лога; если что-то ломается — маленький патч-плагин.</li></ol>
""", tags=["raft", "статус"])

try:
    md = open(RAFT + r"\README.md", encoding="utf-8").read()
    secs = re.split(r"^## ", md, flags=re.M)[1:]
    def md2html(s):
        out = []; inl = False
        for line in s.split("\n"):
            if line.startswith("### "):
                if inl: out.append("</ul>"); inl = False
                out.append("<h3>%s</h3>" % E(line[4:])); continue
            if re.match(r"^\s*[-*] ", line):
                if not inl: out.append("<ul>"); inl = True
                t = E(re.sub(r"^\s*[-*] ", "", line)); t = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", t); t = re.sub(r"`(.+?)`", r"<code>\1</code>", t)
                out.append("<li>%s</li>" % t); continue
            if inl: out.append("</ul>"); inl = False
            if line.strip() and not line.startswith("---") and not line.startswith(">"):
                t = E(line); t = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", t); t = re.sub(r"`(.+?)`", r"<code>\1</code>", t)
                t = re.sub(r"\[([^\]]+)\]\((https?://[^)]+)\)", r'<a href="\2" target="_blank" rel="noopener">\1</a>', E(line) and t)
                out.append("<p>%s</p>" % t)
        if inl: out.append("</ul>")
        return "\n".join(out)
    for s in secs:
        title, _, rest = s.partition("\n")
        title = re.sub(r"^[^\w]+", "", title).strip()
        if not title or "Support" in title: continue
        page("raft:doc:" + re.sub(r"\W+", "-", title.lower()), "RAFT: " + title, "raft", md2html(rest), sub="Документация 4.3.5", tags=["raft"])
except Exception as e:
    print("raft readme:", e)

# ================================================================ НАСТРОЙКИ
CFGS = [
    ("pov", "Path of Valheiman", "treextr.pathofvalheiman.cfg"),
    ("epicloot", "Epic Loot", "randyknapp.mods.epicloot.cfg"),
    ("drawers", "Item Drawers", "mkz.itemdrawers.cfg"),
    ("hse", "HSE Manager", "valesthor.hsemanager.cfg"),
    ("eaqs", "Equipment and Quick Slots (выключен)", "randyknapp.mods.equipmentandquickslots.cfg"),
    ("info", "Display BepInEx Info", "org.bepinex.valheim.displayinfo.cfg"),
]
settings = []
for mid, mname, fn in CFGS:
    p = os.path.join(CFG, fn)
    if not os.path.exists(p):
        continue
    section = ""
    desc, typ, dflt, rng = [], "", "", ""
    for line in open(p, encoding="utf-8-sig", errors="replace"):
        line = line.rstrip("\n")
        if line.startswith("[") and line.endswith("]"):
            section = line[1:-1]; desc, typ, dflt, rng = [], "", "", ""; continue
        if line.startswith("## "):
            desc.append(line[3:]); continue
        if line.startswith("# Setting type:"): typ = line.split(":", 1)[1].strip(); continue
        if line.startswith("# Default value:"): dflt = line.split(":", 1)[1].strip(); continue
        if line.startswith("# Acceptable value"): rng = line.split(":", 1)[1].strip(); continue
        if line.startswith("#") or not line.strip():
            continue
        if " = " in line:
            key, val = line.split(" = ", 1)
            settings.append(dict(m=mid, mn=mname, s=section, k=key, v=val, d=dflt, t=typ, r=rng, h=" ".join(desc)))
            desc, typ, dflt, rng = [], "", "", ""
print("settings:", len(settings))

hot = [s for s in settings if s["t"] == "KeyboardShortcut"]

# ================================================================ СЛОВАРЬ
page("tools:attrs", "Атрибуты и параметры (словарь)", "tools",
     "<table class='t'><tr><th>Код</th><th>Название</th><th>Род. падеж</th></tr>" + "".join(
         "<tr><td><code>%s</code></td><td>%s</td><td>%s</td></tr>" % (k.split(".")[1], E(v), E(loc.get(k.replace(".name", ".gen"), "")))
         for k, v in loc.items() if re.match(r"^attr\.[^.]+\.name$", k)) + "</table>", tags=["словарь"])

# ================================================================ СВЯЗИ (рецепты, дроп, торговцы, обратные ссылки)
import links
links.build(globals())
cat_order = {c["id"]: c for c in cats}

# ================================================================ ОБРАТНЫЕ ССЫЛКИ
back = collections.defaultdict(set)
for pid, p in pages.items():
    srcs = p["b"] + "".join(b for _, b in p.get("bl", []))
    for m in re.finditer(r'href="#/p/([^"]+)"', srcs):
        t = m.group(1)
        if t in pages and t != pid:
            back[t].add(pid)
    for _, t in p.get("r", []):
        if t in pages and t != pid:
            back[t].add(pid)
for pid, p in pages.items():
    p["l"] = sorted(back.get(pid, []))[:60]

# ================================================================ ВЫВОД
used = collections.Counter(p["c"] for p in pages.values())
meta = dict(built=datetime.datetime.now().strftime("%d.%m.%Y %H:%M"), pages=len(pages), settings=len(settings),
            valheim="1.0.21", pov="6.0.6", epicloot="0.15.6", drawers="0.5.7", counts=dict(used))
meta["recipes"] = RECIPE_COUNT; meta["relations"] = REL_COUNT; meta["icons"] = len(ICONS)
data = dict(meta=meta, cats=cats, pages=[pages[i] for i in order], settings=settings, icons=ICONS)
os.makedirs(os.path.dirname(OUT), exist_ok=True)
js = "window.WIKI=" + json.dumps(data, ensure_ascii=False, separators=(",", ":")) + ";"
open(OUT, "w", encoding="utf-8").write(js)
print("pages:", len(pages), dict(used), "size KB:", len(js) // 1024)

# ================================================================ единый файл
def bundle():
    s = os.path.join(HERE, "site")
    idx = open(os.path.join(s, "index.html"), encoding="utf-8").read()
    css = open(os.path.join(s, "style.css"), encoding="utf-8").read()
    dat = open(os.path.join(s, "data.js"), encoding="utf-8").read().replace("</script", "<\/script")
    app = open(os.path.join(s, "app.js"), encoding="utf-8").read().replace("</script", "<\/script")
    idx = idx.replace('<link rel="stylesheet" href="style.css">', "<style>" + css + "</style>")
    idx = idx.replace('<script src="data.js"></script>', "<script>" + dat + "</script>")
    idx = idx.replace('<script src="app.js"></script>', "<script>" + app + "</script>")
    open(os.path.join(HERE, "wiki.html"), "w", encoding="utf-8").write(idx)
    print("wiki.html KB:", len(idx) // 1024)


bundle()
