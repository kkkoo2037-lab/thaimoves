using System;
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

    // true = open the shop from anywhere with the O key
    const bool ShopAnywhere = false;
    // ================

    static readonly string[] Names = {
        "Bandage", "Body Armor", "Medkit",
        "Water", "Burger", "Energy Drink"
    };
    static readonly int[] BuyPrice = { 150, 500, 300, 20, 40, 60 };
    static readonly int[] SellPrice = { 50, 150, 100, 5, 10, 20 };

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
    int sel;
    bool blipsMade;
    int boostEnd;
    int nextSave;

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
        if (!sellMode)
        {
            if (pl.Money < BuyPrice[id])
            {
                Msg("Not enough money");
                return;
            }
            pl.Money = pl.Money - BuyPrice[id];
            Inv.Qty[id]++;
            Inv.Dirty = true;
            Msg("Bought " + Names[id]);
        }
        else
        {
            if (Inv.Qty[id] <= 0)
            {
                Msg("You don't have this item");
                return;
            }
            Inv.Qty[id]--;
            pl.Money = pl.Money + SellPrice[id];
            Inv.Dirty = true;
            Msg("Sold " + Names[id]);
        }
    }

    // ---------- Menu drawing ----------
    int RowCount()
    {
        int n = Names.Length;
        return mode == 1 ? n + 1 : n + 2;
    }

    string RowText(int i)
    {
        int n = Names.Length;
        if (mode == 1)
        {
            if (i == n) return "Close";
            return Names[i] + "   x" + Inv.Qty[i];
        }
        if (i == 0)
            return sellMode ? "Mode:  < SELL >" : "Mode:  < BUY >";
        if (i == n + 1) return "Close";
        int id = i - 1;
        int price = sellMode ? SellPrice[id] : BuyPrice[id];
        return Names[id] + "   $" + price + "   (own " + Inv.Qty[id] + ")";
    }

    void DrawMenu()
    {
        int rows = RowCount();
        float h = 0.10f + rows * 0.036f;
        float cy = 0.20f + h / 2f;
        Function.Call(Hash.DRAW_RECT,
            0.50f, cy, 0.34f, h, 0, 0, 0, 190);

        string title = mode == 1 ? "BAG" : "ITEM SHOP";
        DrawText(title + "     $" + Game.Player.Money,
            0.34f, 0.21f, 0.5f, 255, 255, 255, 255, false);

        for (int i = 0; i < rows; i++)
        {
            bool on = (i == sel);
            int g = on ? 220 : 255;
            int b = on ? 0 : 255;
            DrawText(RowText(i), 0.34f, 0.255f + i * 0.036f, 0.4f,
                255, g, b, 255, false);
        }
    }

    // ---------- Main loop ----------
    void OnTick(object sender, EventArgs e)
    {
        try { Tick2(); }
        catch (Exception) { }
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
                    if (kShop || PadJust(51))
                    {
                        mode = 2;
                        sel = 0;
                        sellMode = false;
                    }
                }
            }

            if (ShopAnywhere && kAny)
            {
                mode = 2;
                sel = 0;
                sellMode = false;
            }
        }

        // ---------- bag key ----------
        if (kBag)
        {
            if (mode != 0) mode = 0;
            else if (!p.IsInVehicle())
            {
                mode = 1;
                sel = 0;
            }
        }

        // ---------- menu ----------
        if (mode == 0) return;

        Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

        int rows = RowCount();
        int n = Names.Length;
        if (kUp || PadJustDis(172)) sel = (sel + rows - 1) % rows;
        if (kDown || PadJustDis(173)) sel = (sel + 1) % rows;

        bool left = kLeft || PadJustDis(174);
        bool right = kRight || PadJustDis(175);
        if (mode == 2 && sel == 0 && (left || right))
            sellMode = !sellMode;

        if (kBack || PadJustDis(202)) mode = 0;

        if (kEnter || PadJustDis(201))
        {
            if (mode == 1)
            {
                if (sel == n) mode = 0;
                else UseItem(p, sel);
            }
            else
            {
                if (sel == 0) sellMode = !sellMode;
                else if (sel == n + 1) mode = 0;
                else BuyOrSell(sel - 1);
            }
        }

        if (mode != 0) DrawMenu();
    }
}
