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

    public static int[] Qty = new int[6];
    public static bool Dirty;
    public static bool WantBandage;
    public static bool WantArmor;

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
        "Water", "Burger", "Energy Drink"
    };
    static readonly string[] Desc = {
        "Heals 60 HP over time (L3 / H)",
        "Full body armor (RB / B)",
        "Instantly restores full health",
        "Restores stamina, +5 HP",
        "Restores 25 HP",
        "Speed boost for 30 seconds"
    };
    static readonly int[] Cat = { 1, 2, 1, 3, 3, 3 };
    static readonly string[] CatNames = {
        "ALL", "MEDICAL", "ARMOR", "FOOD"
    };
    static readonly int[] BuyPrice = { 150, 500, 300, 20, 40, 60 };
    static readonly int[] SellPrice = { 50, 150, 100, 5, 10, 20 };
    static readonly int[] QtyOptions = { 1, 5, 10 };

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
        if (p.IsInVehicle())
        {
            Msg("Can't use in a vehicle");
            return;
        }

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
            PlayAnim(p, "mp_player_inteat@burger",
                "mp_player_int_eat_burger", 2500);
            Msg("Burger: +25 health");
        }
        else if (id == Inv.Energy)
        {
            Inv.Take(id);
            Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER,
                Game.Player.Handle, 1.3f);
            boostEnd = Game.GameTime + 30000;
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

    void DrawUi(Ped p)
    {
        // panel
        float pcx = 0.5f, pcy = 0.5f, pw = 0.64f, ph = 0.64f;
        Rect(pcx, pcy, pw, ph, 18, 18, 22, 235);
        // sidebar
        Rect(0.245f, pcy, 0.13f, ph, 26, 26, 31, 245);

        string title = mode == 1 ? "INVENTORY" : "SHOP";
        DrawText(title, 0.195f, 0.20f, 0.45f,
            230, 230, 235, 255, false);
        if (mode == 2)
        {
            string ms = sellMode ? "SELL" : "BUY";
            DrawText(ms, 0.195f, 0.235f, 0.38f,
                230, 100, 130, 255, false);
        }

        // categories
        for (int i = 0; i < CatNames.Length; i++)
        {
            float cy = 0.30f + i * 0.05f;
            bool on = (i == catSel);
            if (on && focus == 0)
                Rect(0.245f, cy + 0.012f, 0.12f, 0.040f,
                    225, 100, 130, 255);
            else if (on)
                Rect(0.245f, cy + 0.012f, 0.12f, 0.040f,
                    120, 50, 70, 200);
            DrawText(CatNames[i], 0.200f, cy, 0.36f,
                on ? 255 : 190, on ? 255 : 190, on ? 255 : 195,
                255, false);
        }

        BuildVisible();

        // grid
        float x0 = 0.335f, y0 = 0.215f;
        float tw = 0.068f, th = 0.125f;
        float gx = 0.075f, gy = 0.135f;
        int cols = 6;
        int rowsShown = 3;

        int selRow = sel / cols;
        if (selRow < scrollRow) scrollRow = selRow;
        if (selRow >= scrollRow + rowsShown)
            scrollRow = selRow - rowsShown + 1;
        if (scrollRow < 0) scrollRow = 0;

        if (vis.Count == 0)
        {
            string empty = mode == 1
                ? "Your bag is empty"
                : "Nothing to show";
            DrawText(empty, 0.57f, 0.38f, 0.45f,
                170, 170, 175, 255, true);
        }

        int start = scrollRow * cols;
        int end = Math.Min(vis.Count, start + cols * rowsShown);
        for (int i = start; i < end; i++)
        {
            int id = vis[i];
            int rr = (i - start) / cols;
            int cc = (i - start) % cols;
            float tx = x0 + cc * gx;
            float ty = y0 + rr * gy;
            float tcx = tx + tw / 2f;
            float tcy = ty + th / 2f;

            if (i == sel && focus == 1)
                Rect(tcx, tcy, tw + 0.004f, th + 0.007f,
                    225, 100, 130, 255);
            Rect(tcx, tcy, tw, th, 36, 36, 42, 255);

            string top;
            if (mode == 1) top = "x" + Inv.Qty[id];
            else if (sellMode) top = "$" + SellPrice[id];
            else top = "$" + BuyPrice[id];
            DrawText(top, tx + 0.005f, ty + 0.004f, 0.30f,
                255, 255, 255, 255, false);

            DrawIcon(id, tcx, ty + 0.058f, 0.060f);

            DrawText(Names[id], tcx, ty + th - 0.032f, 0.28f,
                235, 235, 240, 255, true);
        }

        // detail strip
        Rect(0.57f, 0.717f, 0.49f, 0.085f, 28, 28, 34, 255);
        if (vis.Count > 0)
        {
            int id = vis[sel];
            int q = QtyOptions[qtyIdx];
            DrawText(Names[id] + " - " + Desc[id],
                0.335f, 0.680f, 0.34f, 255, 255, 255, 255, false);
            string line;
            if (mode == 1)
                line = "Owned: " + Inv.Qty[id] + "     Enter = use";
            else if (sellMode)
                line = "Sell x" + q + " = $" + (SellPrice[id] * q)
                    + "     Owned: " + Inv.Qty[id]
                    + "     Enter = sell";
            else
                line = "Buy x" + q + " = $" + (BuyPrice[id] * q)
                    + "     Owned: " + Inv.Qty[id]
                    + "     Enter = buy";
            DrawText(line, 0.335f, 0.710f, 0.32f,
                230, 100, 130, 255, false);
        }

        // stats bar
        string stats = "$" + Game.Player.Money
            + "      HP " + p.Health
            + "      ARMOR " + p.Armor;
        DrawText(stats, 0.335f, 0.762f, 0.34f,
            255, 255, 255, 255, false);

        // hints
        string hint = mode == 1
            ? "Arrows move   Left = categories   Enter use"
            : "Arrows move   Left = categories   T buy/sell   "
                + "Q qty   Enter confirm";
        DrawText(hint, 0.5f, 0.833f, 0.30f,
            200, 200, 205, 255, true);
    }

    // ---------- Main loop ----------
    void OnTick(object sender, EventArgs e)
    {
        try { Tick2(); }
        catch (Exception) { }
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
            else if (!p.IsInVehicle()) OpenMenu(1);
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
        int cols = 6;
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
