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
        "Restores stamina, +5 HP",
        "Restores 25 HP",
        "Speed boost for 30 seconds",
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
            p.Health = Math.Min(p.MaxHealth, p.Health + 5);
            if (!inVeh)
                PlayAnim(p, "mp_player_intdrink", "loop_bottle", 1800);
            Msg("Water: stamina restored");
        }
        else if (id == Inv.Burger)
        {
            if (p.Health >= p.MaxHealth)
            {
                Msg("Health is full");
                return;
            }
            Inv.Take(id);
            p.Health = Math.Min(p.MaxHealth, p.Health + 25);
            if (!inVeh)
                PlayAnim(p, "mp_player_inteat@burger",
                    "mp_player_int_eat_burger", 2500);
            Msg("Burger: +25 health");
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
            Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER,
                Game.Player.Handle, 1.3f);
            boostEnd = Game.GameTime + 30000;
            if (!inVeh)
                PlayAnim(p, "mp_player_intdrink", "loop_bottle", 1800);
            Msg("Energy Drink: speed boost 30s");
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
    //  PinkUI - drawn with native rects/text (NO PNG files needed)
    //  Design space 1280x720. Replaces DrawUiOld + the old sprite UI.
    // =====================================================================
    const int BCols = 3;
    const int BRows = 2;
    const float DW = 1280f, DH = 720f;

    static void PR(float x, float y, float w, float h,
        int r, int g, int b, int a)
    {
        Rect((x + w / 2f) / DW, (y + h / 2f) / DH, w / DW, h / DH,
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
        float nx = x / DW;
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

    static void PPanel(float x, float y, float w, float h, float hh)
    {
        PR(x - 3f, y - 3f, w + 6f, h + 6f, 255, 63, 180, 70);   // glow
        PR(x - 1f, y - 1f, w + 2f, h + 2f, 255, 63, 180, 230);  // border
        PR(x, y, w, h, 20, 10, 24, 240);                         // body
        PR(x, y, w, hh, 44, 14, 48, 255);                        // header
        PR(x, y + hh, w, 2f, 255, 63, 180, 255);                 // line
        // pink slanted-look stripes (bottom right)
        PR(x + w - 70f, y + h - 6f, 50f, 3f, 255, 63, 180, 200);
        PR(x + w - 40f, y + h - 12f, 30f, 3f, 255, 63, 180, 120);
    }

    static void PSidebar(float x, float y, int catSel, int focus)
    {
        for (int i = 0; i < CatNames.Length; i++)
        {
            float sy = y + 80f + i * 44f;
            bool on = (i == catSel);
            if (on && focus == 0) PR(x + 10f, sy, 116f, 38f, 255, 63, 180, 240);
            else if (on) PR(x + 10f, sy, 116f, 38f, 150, 40, 110, 230);
            else PR(x + 10f, sy, 116f, 38f, 38, 22, 44, 220);
            PT(CatNames[i], x + 24f, sy + 8f, 0.34f, 255, 255, 255, 0);
        }
    }

    void DrawUi(Ped p)
    {
        BuildVisible();
        bool shop = (mode == 2);
        int q = QtyOptions[qtyIdx];

        int selRow = sel / BCols;
        if (selRow < scrollRow) scrollRow = selRow;
        if (selRow >= scrollRow + BRows) scrollRow = selRow - BRows + 1;
        if (scrollRow < 0) scrollRow = 0;

        PR(0f, 0f, DW, DH, 0, 0, 0, 120);   // dim background

        // ---------------- BAG ----------------
        float bx = 30f, by = 30f, bw = 560f, bh = 430f;
        List<int> bagList = new List<int>();
        if (!shop) bagList = vis;
        else
        {
            for (int id = 0; id < Names.Length; id++)
            {
                if (Inv.Qty[id] <= 0) continue;
                if (catSel != 0 && Cat[id] != catSel) continue;
                bagList.Add(id);
            }
        }
        PPanel(bx, by, bw, bh, 64f);
        PT("BAG", bx + 20f, by + 8f, 0.6f, 255, 255, 255, 0);
        PT("Check the items in your bag", bx + 20f, by + 40f, 0.3f,
            255, 150, 215, 0);
        PR(bx + bw - 180f, by + 16f, 160f, 32f, 70, 22, 76, 255);
        PT(string.Format("{0:0.0} / {1:0} KG", TotalWeight(), MaxWeight),
            bx + bw - 30f, by + 20f, 0.36f, 255, 255, 255, 2);
        PSidebar(bx, by, catSel, focus);

        int bStart = shop ? 0 : scrollRow * BCols;
        int bEnd = Math.Min(bagList.Count, bStart + BCols * BRows);
        if (bagList.Count == 0)
            PT("EMPTY", bx + 340f, by + 200f, 0.5f, 200, 170, 195, 1);
        for (int i = bStart; i < bEnd; i++)
        {
            int id = bagList[i];
            int rr = (i - bStart) / BCols, cc = (i - bStart) % BCols;
            float tx = bx + 140f + cc * 136f, ty = by + 80f + rr * 148f;
            if (!shop && i == sel && focus == 1)
                PR(tx - 3f, ty - 3f, 134f, 146f, 255, 63, 180, 255);
            PR(tx, ty, 128f, 140f, 36, 22, 42, 255);
            DrawIcon(id, (tx + 64f) / DW, (ty + 52f) / DH, 0.095f);
            PT("x" + Inv.Qty[id], tx + 120f, ty + 80f, 0.36f,
                255, 255, 255, 2);
            PT(Names[id], tx + 64f, ty + 110f, 0.34f, 240, 240, 245, 1);
        }
        PT(shop ? "BACKSPACE Close" : "ENTER Use    TAB Category    I Close",
            bx + 20f, by + bh - 30f, 0.3f, 200, 200, 205, 0);

        // ---------------- SHOP ----------------
        if (shop)
        {
            float sx = 610f, sy = 30f, sw = 640f, sh = 430f;
            PPanel(sx, sy, sw, sh, 64f);
            PT(sellMode ? "SELL" : "SHOP", sx + 20f, sy + 8f, 0.6f,
                255, 255, 255, 0);
            PT((sellMode ? "Selling" : "Buying") + "   Qty x" + q,
                sx + 20f, sy + 40f, 0.3f, 255, 150, 215, 0);
            PR(sx + sw - 180f, sy + 16f, 160f, 32f, 70, 22, 76, 255);
            PT("$" + Game.Player.Money.ToString("N0"),
                sx + sw - 30f, sy + 20f, 0.36f, 255, 200, 60, 2);
            PSidebar(sx, sy, catSel, focus);

            int sStart = scrollRow * BCols;
            int sEnd = Math.Min(vis.Count, sStart + BCols * BRows);
            if (vis.Count == 0)
                PT("NOTHING TO SHOW", sx + 380f, sy + 200f, 0.5f,
                    200, 170, 195, 1);
            for (int i = sStart; i < sEnd; i++)
            {
                int id = vis[i];
                int rr = (i - sStart) / BCols, cc = (i - sStart) % BCols;
                float tx = sx + 140f + cc * 160f;
                float ty = sy + 80f + rr * 156f;
                bool on = (i == sel && focus == 1);
                if (on) PR(tx - 3f, ty - 3f, 158f, 154f, 255, 63, 180, 255);
                PR(tx, ty, 152f, 148f, 36, 22, 42, 255);
                DrawIcon(id, (tx + 76f) / DW, (ty + 38f) / DH, 0.085f);
                PT("x" + Inv.Qty[id], tx + 146f, ty + 4f, 0.3f,
                    255, 123, 208, 2);
                PT(Names[id], tx + 76f, ty + 76f, 0.34f, 240, 240, 245, 1);
                int price = (sellMode ? SellPrice[id] : BuyPrice[id]) * q;
                PT("$" + price, tx + 76f, ty + 98f, 0.34f, 255, 200, 60, 1);
                PR(tx + 12f, ty + 122f, 128f, 20f, 255, 63, 180,
                    on ? 255 : 160);
                PT(sellMode ? "SELL" : "BUY", tx + 76f, ty + 122f, 0.3f,
                    255, 255, 255, 1);
            }
            PT("T Buy/Sell   Q Qty   TAB Category   ENTER Confirm",
                sx + 20f, sy + sh - 30f, 0.3f, 200, 200, 205, 0);
        }

        // ---------------- CATALOG ----------------
        float cx = 30f, cy = 475f, cw = 1220f, ch = 225f;
        PPanel(cx, cy, cw, ch, 48f);
        PT("ALL ITEMS", cx + 20f, cy + 6f, 0.5f, 255, 255, 255, 0);
        PT("HP " + p.Health + "    ARMOR " + p.Armor,
            cx + cw - 20f, cy + 12f, 0.36f, 255, 255, 255, 2);
        int curId = vis.Count > 0 ? vis[sel] : -1;
        for (int id = 0; id < Names.Length; id++)
        {
            float ix = cx + 10f + id * 172f, iy = cy + 58f;
            if (id == curId)
                PR(ix - 2f, iy - 2f, 168f, 124f, 255, 63, 180, 255);
            PR(ix, iy, 164f, 120f, 36, 22, 42, 255);
            DrawIcon(id, (ix + 82f) / DW, (iy + 44f) / DH, 0.09f);
            PT(Names[id], ix + 82f, iy + 86f, 0.34f, 240, 240, 245, 1);
        }
        if (curId >= 0)
            PT(Names[curId] + " - " + Desc[curId],
                cx + 20f, cy + 190f, 0.34f, 255, 150, 215, 0);
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

        if (p == null || !p.Exists() || p.IsDead)
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
        int cols = BCols;
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
