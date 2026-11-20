using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

// Shared inventory (other scripts in the same DLL can use it)
public static class Inv
{
    public const int Bandage = 0;
    public const int Armor = 1;
    public const int Medkit = 2;
    public const int Water = 3;
    public const int Burger = 4;
    public const int Energy = 5;
    public const int Revive = 6;

    public static int[] Qty = new int[7];
    public static bool Dirty;
    public static bool WantBandage;
    public static bool WantArmor;
    public static bool WantRevive;
    public static bool Busy;      // carrying / reviving someone

    public static bool Take(int id)
    {
        if (id < 0 || id >= Qty.Length) return false;
        if (Qty[id] <= 0) return false;
        Qty[id]--;
        Dirty = true;
        return true;
    }
}

public class ItemSystem : Script
{
    // ===== Keys =====
    int BagKey = 0x49;        // I
    int ShopKey = 0x45;       // E (near a shop)
    int AnywhereKey = 0x4F;   // O (only if ShopAnywhere = true)
    int KUp = 0x26, KDown = 0x28, KLeft = 0x25, KRight = 0x27;
    int KEnter = 0x0D, KBack = 0x08;
    int KTab = 0x09;          // next category
    int KBuySell = 0x54;      // T  buy / sell
    int KQty = 0x51;          // Q  quantity 1 / 5 / 10

    // true = open the shop from anywhere with the O key
    const bool ShopAnywhere = false;
    // ================

    static readonly string[] Names = {
        "Bandage", "Body Armor", "Medkit",
        "Water", "Burger", "Energy Drink", "Revive Kit"
    };
    static readonly string[] Desc = {
        "Heals 60 HP over time (L3 / H)",
        "Full body armor (RB / B)",
        "Instantly restores full health",
        "Restores stamina and a little food, +5 HP",
        "Restores 35 food and 25 HP",
        "Adds 30% to the stamina bar",
        "Revives a dead person (CPR 10s)"
    };
    static readonly int[] Cat = { 1, 2, 1, 3, 3, 3, 1 };
    static readonly string[] CatNames = {
        "ALL", "MEDICAL", "ARMOR", "FOOD"
    };
    static readonly int[] BuyPrice = { 150, 500, 300, 20, 40, 60, 1000 };
    static readonly int[] SellPrice = { 50, 150, 100, 5, 10, 20, 300 };
    static readonly int[] QtyOptions = { 1, 5, 10 };
    static readonly float[] Weight = { 0.5f, 3.0f, 1.5f, 0.5f, 0.4f, 0.4f, 1.0f };
    const float MaxWeight = 50f;

    static float TotalWeight()
    {
        float w = 0f;
        for (int i = 0; i < Weight.Length; i++) w += Weight[i] * Inv.Qty[i];
        return w;
    }

    // Pixel icons 10x10 ('.' = empty)
    static readonly string[][] Icons = {
        new string[] {   // bandage
            "..........",
            "..........",
            ".YYYYYYYY.",
            "YYYYYYYYYY",
            "YYY.WW.YYY",
            "YYY.WW.YYY",
            "YYYYYYYYYY",
            ".YYYYYYYY.",
            "..........",
            ".........."
        },
        new string[] {   // body armor
            "..BB..BB..",
            ".BBB..BBB.",
            "BBBBBBBBBB",
            "BLBBBBBBLB",
            "BLBDBBDBLB",
            "BBBBBBBBBB",
            "BBBBBBBBBB",
            ".BBBBBBBB.",
            ".BBBBBBBB.",
            "..BBBBBB.."
        },
        new string[] {   // medkit
            "...GGGG...",
            "..G....G..",
            "WWWWWWWWWW",
            "WWWWRRWWWW",
            "WWWWRRWWWW",
            "WWRRRRRRWW",
            "WWRRRRRRWW",
            "WWWWRRWWWW",
            "WWWWRRWWWW",
            "WWWWWWWWWW"
        },
        new string[] {   // water
            "...BBBB...",
            "...BBBB...",
            "...LLLL...",
            "..LLLLLL..",
            "..LLWWLL..",
            "..LLLLLL..",
            "..LLLLLL..",
            "..LLLLLL..",
            "..LLLLLL..",
            "..BBBBBB.."
        },
        new string[] {   // burger
            "..OOOOOO..",
            ".OOWOOOOO.",
            "OOOOOOWOOO",
            "..EEEEEE..",
            ".RRRRRRRR.",
            "..NNNNNN..",
            ".NNNNNNNN.",
            "..OOOOOO..",
            ".OOOOOOOO.",
            ".........."
        },
        new string[] {   // energy drink
            "...GGGG...",
            "..RRRRRR..",
            "..RYYYYR..",
            "..RYRRYR..",
            "..RYYYYR..",
            "..RRRRRR..",
            "..RRYYRR..",
            "..RRRRRR..",
            "..RRRRRR..",
            "...GGGG..."
        },
        new string[] {   // revive kit (heart)
            "..RR..RR..",
            ".RRRRRRRR.",
            "RRRWRRRRRR",
            "RRWRRRRRRR",
            "RRRRRRRRRR",
            ".RRRRRRRR.",
            "..RRRRRR..",
            "...RRRR...",
            "....RR....",
            ".........."
        },
    };

    static readonly Vector3[] Shops = {
        new Vector3(-48.5f, -1757.5f, 29.4f),
        new Vector3(25.7f, -1347.3f, 29.5f),
        new Vector3(-707.5f, -914.3f, 19.2f),
        new Vector3(373.9f, 326.9f, 103.6f),
        new Vector3(1163.4f, -323.8f, 69.2f),
        new Vector3(-1820.5f, 792.5f, 138.1f),
        new Vector3(2557.5f, 382.3f, 108.6f),
        new Vector3(1961.1f, 3740.7f, 32.3f),
        new Vector3(1698.4f, 4924.4f, 42.1f),
        new Vector3(1729.2f, 6414.1f, 35.0f),
        new Vector3(-3243.0f, 1001.4f, 12.8f),
    };

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    const string CFG = "scripts\\ItemSystem.cfg";

    bool[] prev = new bool[256];
    int mode;          // 0 closed, 1 bag, 2 shop
    bool sellMode;
    int catSel;        // 0..3
    int focus = 1;     // 0 = category list, 1 = item grid
    int sel;           // index in visible list
    int scrollRow;
    int qtyIdx;
    bool blipsMade;
    int boostEnd;
    int nextSave;
    List<int> vis = new List<int>();

    public ItemSystem()
    {
        Load();
        Tick += OnTick;
    }

    // ---------- Save / Load ----------
    void Save()
    {
        try
        {
            string s = "";
            for (int i = 0; i < Inv.Qty.Length; i++)
            {
                if (i > 0) s += ",";
                s += Inv.Qty[i];
            }
            File.WriteAllText(CFG, s);
        }
        catch (Exception) { }
    }

    void Load()
    {
        try
        {
            if (!File.Exists(CFG)) return;
            string[] a = File.ReadAllText(CFG).Trim().Split(',');
            for (int i = 0; i < Inv.Qty.Length && i < a.Length; i++)
            {
                int v = int.Parse(a[i]);
                Inv.Qty[i] = v < 0 ? 0 : v;
            }
        }
        catch (Exception) { }
    }

    // ---------- Helpers ----------
    bool Pressed(int vk)
    {
        bool d = (GetAsyncKeyState(vk) & 0x8000) != 0;
        bool r = d && !prev[vk];
        prev[vk] = d;
        return r;
    }

    static bool PadJustDis(int c)
    {
        return Function.Call<bool>(
            Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, c);
    }

    static bool PadJust(int c)
    {
        return Function.Call<bool>(
            Hash.IS_CONTROL_JUST_PRESSED, 0, c);
    }

    static void Msg(string t)
    {
        GTA.UI.Screen.ShowSubtitle(t, 1800);
    }

    static void Rect(float cx, float cy, float w, float h,
        int r, int g, int b, int a)
    {
        Function.Call(Hash.DRAW_RECT, cx, cy, w, h, r, g, b, a);
    }

    static void DrawText(string s, float x, float y, float scale,
        int r, int g, int b, int a, bool center)
    {
        Function.Call(Hash.SET_TEXT_FONT, 0);
        Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, a);
        Function.Call(Hash.SET_TEXT_CENTRE, center);
        Function.Call(Hash.SET_TEXT_DROP_SHADOW);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(
            Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s);
        Function.Call(
            Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y, 0);
    }

    static void PlayAnim(Ped p, string dict, string clip, int ms)
    {
        Function.Call(Hash.REQUEST_ANIM_DICT, dict);
        int tries = 0;
        while (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)
            && tries++ < 50)
            Script.Yield();
        if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict))
            return;
        Function.Call(Hash.TASK_PLAY_ANIM, p, dict, clip,
            8f, -8f, ms, 49, 0f, false, false, false);
    }

    void MakeBlips()
    {
        blipsMade = true;
        for (int i = 0; i < Shops.Length; i++)
        {
            Vector3 s = Shops[i];
            int b = Function.Call<int>(
                Hash.ADD_BLIP_FOR_COORD, s.X, s.Y, s.Z);
            Function.Call(Hash.SET_BLIP_SPRITE, b, 52);
            Function.Call(Hash.SET_BLIP_COLOUR, b, 2);
            Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, b, true);
            Function.Call(
                Hash.BEGIN_TEXT_COMMAND_SET_BLIP_NAME, "STRING");
            Function.Call(
                Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,
                "Item Shop");
            Function.Call(Hash.END_TEXT_COMMAND_SET_BLIP_NAME, b);
        }
    }

    // ---------- Using items ----------
    void UseItem(Ped p, int id)
    {
        if (Inv.Qty[id] <= 0)
        {
            Msg("You don't have this item");
            return;
        }
        bool inVeh = p.IsInVehicle();

        if (id == Inv.Bandage)
        {
            Inv.WantBandage = true;
            mode = 0;
        }
        else if (id == Inv.Armor)
        {
            Inv.WantArmor = true;
            mode = 0;
        }
        else if (id == Inv.Medkit)
        {
            if (p.Health >= p.MaxHealth)
            {
                Msg("Health is full");
                return;
            }
            Inv.Take(id);
            p.Health = p.MaxHealth;
            Msg("Medkit used");
        }
        else if (id == Inv.Water)
        {
            Inv.Take(id);
            Function.Call(Hash.RESTORE_PLAYER_STAMINA,
                Game.Player.Handle, 1.0f);
            Needs.AddStamina(25f);
            Needs.AddFood(8f);
            Needs.AddStress(-5f);
            p.Health = Math.Min(p.MaxHealth, p.Health + 5);
            if (!inVeh)
                PlayAnim(p, "mp_player_intdrink", "loop_bottle", 1800);
            Msg("Water: stamina restored");
        }
        else if (id == Inv.Burger)
        {
            if (p.Health >= p.MaxHealth && Needs.Food >= 99f)
            {
                Msg("You are full");
                return;
            }
            Inv.Take(id);
            p.Health = Math.Min(p.MaxHealth, p.Health + 25);
            Needs.AddFood(35f);
            Needs.AddStress(-5f);
            if (!inVeh)
                PlayAnim(p, "mp_player_inteat@burger",
                    "mp_player_int_eat_burger", 2500);
            Msg("Burger: +35 food");
        }
        else if (id == Inv.Revive)
        {
            if (inVeh)
            {
                Msg("Get out of the vehicle to revive");
                return;
            }
            Inv.WantRevive = true;
            mode = 0;
        }
        else if (id == Inv.Energy)
        {
            Inv.Take(id);
            Needs.AddStamina(30f);     // +30% of the stamina bar
            if (!inVeh)
                PlayAnim(p, "mp_player_intdrink", "loop_bottle", 1800);
            Msg("Energy Drink: +30% stamina");
        }
    }

    // ---------- Shop ----------
    void BuyOrSell(int id)
    {
        Player pl = Game.Player;
        int q = QtyOptions[qtyIdx];
        if (!sellMode)
        {
            int total = BuyPrice[id] * q;
            if (pl.Money < total)
            {
                Msg("Not enough money");
                return;
            }
            if (TotalWeight() + Weight[id] * q > MaxWeight)
            {
                Msg("Bag is full (too heavy)");
                return;
            }
            pl.Money = pl.Money - total;
            Inv.Qty[id] += q;
            Inv.Dirty = true;
            Msg("Bought " + q + " x " + Names[id]);
        }
        else
        {
            if (Inv.Qty[id] <= 0)
            {
                Msg("You don't have this item");
                return;
            }
            if (q > Inv.Qty[id]) q = Inv.Qty[id];
            Inv.Qty[id] -= q;
            pl.Money = pl.Money + SellPrice[id] * q;
            Inv.Dirty = true;
            Msg("Sold " + q + " x " + Names[id]);
        }
    }

    // ---------- Visible list ----------
    void BuildVisible()
    {
        vis.Clear();
        for (int id = 0; id < Names.Length; id++)
        {
            if (catSel != 0 && Cat[id] != catSel) continue;
            bool needOwned = (mode == 1) || (mode == 2 && sellMode);
            if (needOwned && Inv.Qty[id] <= 0) continue;
            vis.Add(id);
        }
        if (sel >= vis.Count) sel = vis.Count - 1;
        if (sel < 0) sel = 0;
    }

    // ---------- Drawing ----------
    static void PixelColor(char ch, out int r, out int g, out int b)
    {
        r = 255; g = 255; b = 255;
        switch (ch)
        {
            case 'W': r = 235; g = 235; b = 235; break;
            case 'R': r = 210; g = 50; b = 50; break;
            case 'G': r = 150; g = 150; b = 155; break;
            case 'D': r = 60; g = 60; b = 66; break;
            case 'B': r = 70; g = 140; b = 220; break;
            case 'L': r = 150; g = 200; b = 240; break;
            case 'Y': r = 235; g = 190; b = 110; break;
            case 'O': r = 200; g = 130; b = 55; break;
            case 'N': r = 120; g = 70; b = 35; break;
            case 'E': r = 90; g = 170; b = 70; break;
        }
    }

    static void DrawIcon(int id, float cx, float cy, float size)
    {
        string[] rows = Icons[id];
        float ph = size / 10f;
        float pw = ph * 9f / 16f;
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                char ch = rows[y][x];
                if (ch == '.') continue;
                int r, g, b;
                PixelColor(ch, out r, out g, out b);
                Rect(cx + (x - 4.5f) * pw,
                    cy + (y - 4.5f) * ph,
                    pw + 0.0003f, ph + 0.0004f, r, g, b, 255);
            }
        }
    }

    // =====================================================================
    //  PinkUI - PNG sprites (scripts\PinkUI\*.png) with rect fallback.
    //  Design space 1280x720, kept un-stretched on any screen aspect.
    // =====================================================================
    const int BCols = 3;
    const int BRows = 2;
    const float DW = 1280f, DH = 720f;
    const float LOGO_A = 0.7382f;   // logo.png height / width
    const string UIDIR = "scripts\\PinkUI\\";
    const string ICONDIR = "scripts\\PinkUI\\items\\";

    static float SW { get { return GTA.UI.Screen.ScaledWidth; } }
    static float NXd(float x) { return ((SW - DW) / 2f + x) / SW; }

    static void PR(float x, float y, float w, float h,
        int r, int g, int b, int a)
    {
        Rect(NXd(x + w / 2f), (y + h / 2f) / DH, w / SW, h / DH,
            r, g, b, a);
    }

    // align: 0 left, 1 center, 2 right
    static void PT(string s, float x, float y, float scale,
        int r, int g, int b, int align)
    {
        Function.Call(Hash.SET_TEXT_FONT, 4);
        Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, 255);
        Function.Call(Hash.SET_TEXT_DROP_SHADOW);
        float nx = NXd(x);
        if (align == 1) Function.Call(Hash.SET_TEXT_CENTRE, true);
        else if (align == 2)
        {
            Function.Call(Hash.SET_TEXT_JUSTIFICATION, 2);
            Function.Call(Hash.SET_TEXT_WRAP, 0f, nx);
            nx = 0f;
        }
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(
            Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT,
            nx, y / DH, 0);
    }

    Dictionary<string, GTA.UI.CustomSprite> sprs =
        new Dictionary<string, GTA.UI.CustomSprite>();
    bool sprOff;

    // draws a PNG at design-pixel x,y,w,h. false = not available
    bool Sprite(string path, float x, float y, float w, float h)
    {
        if (sprOff) return false;
        try
        {
            GTA.UI.CustomSprite s;
            if (!sprs.TryGetValue(path, out s))
            {
                s = File.Exists(path)
                    ? new GTA.UI.CustomSprite(path,
                        new System.Drawing.SizeF(w, h),
                        new System.Drawing.PointF(0f, 0f))
                    : null;
                sprs[path] = s;
            }
            if (s == null) return false;
            s.Size = new System.Drawing.SizeF(w * DW / SW, h);
            s.Position = new System.Drawing.PointF(NXd(x) * DW, y);
            s.Draw();
            return true;
        }
        catch (Exception) { sprOff = true; return false; }
    }

    bool Spr(string name, float x, float y, float w, float h)
    {
        return Sprite(UIDIR + name, x, y, w, h);
    }

    void Icon(int id, float cx, float cy, float size)
    {
        if (Sprite(ICONDIR + "item_" + id + ".png",
            cx - size / 2f, cy - size / 2f, size, size)) return;
        DrawIcon(id, NXd(cx), cy / DH, size / DH * 0.9f);
    }

    // native fill (drawn under sprites) + PNG frame on top.
    // band = frame thickness baked into the PNG, m = PNG glow margin
    void Frame(string spr, float x, float y, float w, float h,
        float band, float m, int r, int g, int b)
    {
        PR(x + band, y + band, w - 2f * band, h - 2f * band, r, g, b, 255);
        if (!Spr(spr, x - m, y - m, w + 2f * m, h + 2f * m))
            PR(x, y, w, h, r, g, b, 255);
    }

    void Panel(string spr, float x, float y, float w, float h)
    {
        PR(x + 8f, y + 8f, w - 16f, 56f, 14, 52, 22, 255);
        PR(x + 8f, y + 66f, w - 16f, h - 74f, 7, 20, 11, 255);
        if (!Spr(spr, x - 10f, y - 10f, w + 20f, h + 20f))
        {
            PR(x - 1f, y - 1f, w + 2f, h + 2f, 50, 230, 70, 230);
            PR(x, y, w, h, 7, 20, 11, 255);
            PR(x, y, w, 64f, 14, 52, 22, 255);
            PR(x, y + 64f, w, 2f, 50, 230, 70, 255);
        }
    }

    void Sidebar(float x, float y)
    {
        for (int i = 0; i < CatNames.Length; i++)
        {
            float sy = y + 80f + i * 44f;
            bool on = (i == catSel);
            if (on && focus == 0)
                Frame("cat_on.png", x + 10f, sy, 116f, 38f, 4f, 0f, 70, 235, 90);
            else if (on)
                Frame("cat_dim.png", x + 10f, sy, 116f, 38f, 4f, 0f, 30, 110, 50);
            else
                Frame("cat_off.png", x + 10f, sy, 116f, 38f, 4f, 0f, 16, 34, 20);
            PT(CatNames[i], x + 24f, sy + 8f, 0.34f, 255, 255, 255, 0);
        }
    }

    void DrawUi(Ped p)
    {
        BuildVisible();
        bool shop = (mode == 2);
        int cols = shop ? 1 : BCols;
        int rowsShown = shop ? 5 : BRows;

        int selRow = sel / cols;
        if (selRow < scrollRow) scrollRow = selRow;
        if (selRow >= scrollRow + rowsShown)
            scrollRow = selRow - rowsShown + 1;
        if (scrollRow < 0) scrollRow = 0;

        Rect(0.5f, 0.5f, 1f, 1f, 0, 0, 0, 120);   // dim background

        if (shop) DrawShop(p, cols, rowsShown);
        else DrawBag(p, cols, rowsShown);
    }

    // ---------------- BAG: icon grid ----------------
    void DrawBag(Ped p, int cols, int rowsShown)
    {
        float bx = 320f, by = 110f, bw = 640f, bh = 500f;
        Panel("panel_bag.png", bx, by, bw, bh);
        PT("BAG", bx + 76f, by + 6f, 0.6f, 255, 255, 255, 0);
        PT("Check the items in your bag", bx + 76f, by + 38f, 0.3f,
            160, 255, 175, 0);
        Frame("pill.png", bx + bw - 190f, by + 16f, 170f, 32f,
            3f, 0f, 16, 56, 26);
        PT(string.Format("{0:0.0} / {1:0} KG", TotalWeight(), MaxWeight),
            bx + bw - 30f, by + 20f, 0.36f, 255, 255, 255, 2);
        Sidebar(bx, by);
        Spr("logo.png", bx + 377f - 170f, by + 258f - 170f * LOGO_A, 340f, 340f * LOGO_A);

        int start = scrollRow * cols;
        int end = Math.Min(vis.Count, start + cols * rowsShown);
        if (vis.Count == 0)
            PT("EMPTY", bx + 380f, by + 220f, 0.5f, 170, 210, 175, 1);

        for (int i = start; i < end; i++)
        {
            int id = vis[i];
            int rr = (i - start) / cols, cc = (i - start) % cols;
            float tx = bx + 140f + cc * 158f;
            float ty = by + 80f + rr * 178f;
            bool on = (i == sel && focus == 1);

            if (on) Frame("tile_sel.png", tx, ty, 150f, 170f, 4f, 8f, 22, 66, 30);
            else Frame("tile.png", tx, ty, 150f, 170f, 4f, 8f, 14, 32, 18);

            Frame("qty.png", tx + 98f, ty + 8f, 44f, 20f, 3f, 0f, 20, 90, 36);
            PT("x" + Inv.Qty[id], tx + 138f, ty + 9f, 0.3f, 255, 255, 255, 2);

            Spr("pad.png", tx + 14f, ty + 94f, 122f, 28f);
            Icon(id, tx + 75f, ty + 64f, 100f);
            PT(Names[id], tx + 75f, ty + 130f, 0.36f, 240, 240, 245, 1);
        }

        if (vis.Count > 0)
        {
            int id = vis[sel];
            PT(Names[id] + " - " + Desc[id],
                bx + 140f, by + bh - 62f, 0.34f, 160, 255, 175, 0);
        }
        PT("ENTER Use    TAB Category    I Close",
            bx + 20f, by + bh - 30f, 0.3f, 200, 200, 205, 0);
        PT("HP " + p.Health + "   ARMOR " + p.Armor,
            bx + bw - 100f, by + bh - 30f, 0.3f, 255, 255, 255, 2);
    }

    // ---------------- SHOP: list with buy buttons ----------------
    void DrawShop(Ped p, int cols, int rowsShown)
    {
        float bx = 260f, by = 95f, bw = 760f, bh = 530f;
        int q = QtyOptions[qtyIdx];
        Panel("panel_shop.png", bx, by, bw, bh);
        PT("SHOP", bx + 76f, by + 6f, 0.6f, 255, 255, 255, 0);
        PT("Buy and sell items", bx + 76f, by + 38f, 0.3f, 160, 255, 175, 0);
        Frame("pill.png", bx + bw - 190f, by + 16f, 170f, 32f,
            3f, 0f, 16, 56, 26);
        PT("$" + Game.Player.Money.ToString("N0"),
            bx + bw - 30f, by + 20f, 0.36f, 255, 200, 60, 2);

        // BUY / SELL tabs + quantity
        float ty0 = by + 78f;
        if (!sellMode) Frame("seg_on.png", bx + 140f, ty0, 90f, 30f, 3f, 0f, 70, 235, 90);
        else Frame("seg_off.png", bx + 140f, ty0, 90f, 30f, 3f, 0f, 16, 34, 20);
        PT("BUY", bx + 185f, ty0 + 5f, 0.36f, 255, 255, 255, 1);
        if (sellMode) Frame("seg_on.png", bx + 236f, ty0, 90f, 30f, 3f, 0f, 70, 235, 90);
        else Frame("seg_off.png", bx + 236f, ty0, 90f, 30f, 3f, 0f, 16, 34, 20);
        PT("SELL", bx + 281f, ty0 + 5f, 0.36f, 255, 255, 255, 1);
        PT("QTY", bx + bw - 232f, ty0 + 6f, 0.32f, 160, 255, 175, 2);
        for (int k = 0; k < QtyOptions.Length; k++)
        {
            float qx = bx + bw - 220f + k * 62f;
            if (k == qtyIdx) Frame("seg_s_on.png", qx, ty0, 56f, 30f, 3f, 0f, 70, 235, 90);
            else Frame("seg_s_off.png", qx, ty0, 56f, 30f, 3f, 0f, 16, 34, 20);
            PT("x" + QtyOptions[k], qx + 28f, ty0 + 5f, 0.34f, 255, 255, 255, 1);
        }

        Sidebar(bx, by + 44f);
        Spr("logo.png", bx + 440f - 210f, by + 289f - 210f * LOGO_A, 420f, 420f * LOGO_A);

        int start = scrollRow;
        int end = Math.Min(vis.Count, start + rowsShown);
        if (vis.Count == 0)
            PT("NOTHING TO SHOW", bx + 440f, by + 260f, 0.5f, 170, 210, 175, 1);

        for (int i = start; i < end; i++)
        {
            int id = vis[i];
            float rx = bx + 140f, ry = by + 124f + (i - start) * 66f;
            bool on = (i == sel && focus == 1);

            if (on) Frame("row_sel.png", rx, ry, 600f, 60f, 4f, 8f, 22, 66, 30);
            else Frame("row.png", rx, ry, 600f, 60f, 4f, 8f, 14, 32, 18);

            Icon(id, rx + 38f, ry + 30f, 52f);
            PT(Names[id], rx + 76f, ry + 6f, 0.4f, 255, 255, 255, 0);
            PT(Desc[id], rx + 76f, ry + 33f, 0.26f, 140, 200, 150, 0);
            PT("Owned x" + Inv.Qty[id], rx + 400f, ry + 20f, 0.32f,
                160, 255, 175, 2);
            int price = (sellMode ? SellPrice[id] : BuyPrice[id]) * q;
            PT("$" + price, rx + 495f, ry + 17f, 0.4f, 255, 200, 60, 2);
            if (on) Frame("btn_s.png", rx + 506f, ry + 16f, 80f, 28f, 3f, 0f, 70, 235, 90);
            else Frame("btn_s_dim.png", rx + 506f, ry + 16f, 80f, 28f, 3f, 0f, 30, 120, 52);
            PT(sellMode ? "SELL" : "BUY", rx + 546f, ry + 19f, 0.34f,
                255, 255, 255, 1);
        }

        PT("T Buy/Sell   Q Qty   TAB Category   ENTER Confirm   BACKSPACE Close",
            bx + 20f, by + bh - 30f, 0.3f, 200, 200, 205, 0);
        PT("HP " + p.Health + "   ARMOR " + p.Armor,
            bx + bw - 100f, by + bh - 30f, 0.3f, 255, 255, 255, 2);
    }

    // ---------- Main loop ----------
    void OnTick(object sender, EventArgs e)
    {
        try { Tick2(); }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText("ItemSystem.log",
                    DateTime.Now + "  " + ex + "\r\n");
            }
            catch (Exception) { }
        }
    }

    void OpenMenu(int m)
    {
        mode = m;
        sel = 0;
        scrollRow = 0;
        catSel = 0;
        focus = 1;
        sellMode = false;
        qtyIdx = 0;
    }

    void Tick2()
    {
        Ped p = Game.Player.Character;

        bool kBag = Pressed(BagKey);
        bool kShop = Pressed(ShopKey);
        bool kAny = Pressed(AnywhereKey);
        bool kUp = Pressed(KUp);
        bool kDown = Pressed(KDown);
        bool kLeft = Pressed(KLeft);
        bool kRight = Pressed(KRight);
        bool kEnter = Pressed(KEnter);
        bool kBack = Pressed(KBack);
        bool kTab = Pressed(KTab);
        bool kBS = Pressed(KBuySell);
        bool kQty = Pressed(KQty);

        if (p == null || !p.Exists() || p.IsDead || DeathScreen.Downed)
        {
            mode = 0;
            return;
        }

        int now = Game.GameTime;

        if (!blipsMade) MakeBlips();

        // energy drink ends
        if (boostEnd != 0 && now > boostEnd)
        {
            boostEnd = 0;
            Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER,
                Game.Player.Handle, 1.0f);
        }

        // save when something changed
        if (Inv.Dirty && now >= nextSave)
        {
            nextSave = now + 500;
            Inv.Dirty = false;
            Save();
        }

        // ---------- shops in the world ----------
        if (mode == 0 && !p.IsInVehicle())
        {
            Vector3 pos = p.Position;
            for (int i = 0; i < Shops.Length; i++)
            {
                Vector3 s = Shops[i];
                float dx = pos.X - s.X;
                float dy = pos.Y - s.Y;
                float d2 = dx * dx + dy * dy;
                if (d2 > 900f) continue;     // 30 m

                Function.Call(Hash.DRAW_MARKER, 1,
                    s.X, s.Y, s.Z - 1.0f,
                    0f, 0f, 0f, 0f, 0f, 0f,
                    1.2f, 1.2f, 0.6f,
                    80, 220, 80, 120,
                    false, false, 2, false, 0, 0, false);

                float dz = Math.Abs(pos.Z - s.Z);
                if (d2 < 4f && dz < 3f)
                {
                    DrawText("Press E - Item Shop",
                        0.5f, 0.88f, 0.5f, 255, 255, 255, 255, true);
                    if (kShop || PadJust(51)) OpenMenu(2);
                }
            }

            if (ShopAnywhere && kAny) OpenMenu(2);
        }

        // ---------- bag key ----------
        if (kBag)
        {
            if (mode != 0) mode = 0;
            else OpenMenu(1);
        }

        // ---------- menu ----------
        if (mode == 0) return;

        Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

        bool up = kUp || PadJustDis(172);
        bool down = kDown || PadJustDis(173);
        bool left = kLeft || PadJustDis(174);
        bool right = kRight || PadJustDis(175);
        bool enter = kEnter || PadJustDis(201);
        bool back = kBack || PadJustDis(202);
        bool tab = kTab || PadJustDis(206);
        bool buySell = kBS || PadJustDis(204);
        bool qty = kQty || PadJustDis(203);

        BuildVisible();
        int cols = (mode == 2) ? 1 : BCols;
        bool enterUse = enter;
        if (focus == 0)
        {
            // category list: Up/Down changes category
            int nc = CatNames.Length;
            if (up)
            {
                catSel = (catSel + nc - 1) % nc;
                sel = 0;
                scrollRow = 0;
            }
            if (down)
            {
                catSel = (catSel + 1) % nc;
                sel = 0;
                scrollRow = 0;
            }
            if (right || enter)
            {
                focus = 1;
                enterUse = false;
            }
        }
        else
        {
            // item grid: Left on the first column goes to categories
            if (up && sel - cols >= 0) sel -= cols;
            if (down && sel + cols < vis.Count) sel += cols;
            if (left)
            {
                if (sel % cols == 0) focus = 0;
                else sel--;
            }
            if (right && sel + 1 < vis.Count) sel++;
        }

        if (tab)
        {
            catSel = (catSel + 1) % CatNames.Length;
            sel = 0;
            scrollRow = 0;
        }
        if (mode == 2 && buySell)
        {
            sellMode = !sellMode;
            sel = 0;
            scrollRow = 0;
        }
        if (mode == 2 && qty)
            qtyIdx = (qtyIdx + 1) % QtyOptions.Length;

        if (back) mode = 0;

        if (enterUse && focus == 1 && mode != 0)
        {
            BuildVisible();
            if (vis.Count > 0)
            {
                int id = vis[sel];
                if (mode == 1) UseItem(p, id);
                else BuyOrSell(id);
            }
        }

        if (mode != 0) DrawUi(p);
    }
}
