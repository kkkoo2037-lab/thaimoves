using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

public class ThaiMoves : Script
{
    // ================= Keys =================
    // Keyboard: H=0x48 (bandage), B=0x42 (armor), X=0x58 (slide)
    int HealKey = 0x48, ArmorKey = 0x42, SlideKey = 0x58;
    // Pad: L3 = bandage, RB = armor (hold A + RB = slide)
    int PadHeal = 36, PadCover = 44, PadSprint = 21;

    // ================= Slide (anim 2) =================
    float SlideSpeed = 12f;
    int SlideTime = 900;
    float SideDrift = 0.3f;   // + right, - left, 0 straight
    string SlideDict = "missheistfbi3b_ig6_v2";
    string SlideClip = "rubble_slide_franklin";

    // ================= Bandage =================
    const int BANDAGE_TOTAL = 60;
    const int BANDAGE_STEP = 10;
    const int BANDAGE_TICK_MS = 500;
    const string BANDAGE_DICT = "amb@medic@standing@kneel@idle_a";
    const string BANDAGE_ANIM = "idle_a";
    const int BANDAGE_FLAG = 1;
    const string BANDAGE_DICT2 = "amb@medic@standing@tendtodead@base";
    const string BANDAGE_ANIM2 = "base";

    // ================= Armor =================
    const int ARMOR_ANIM_MS = 1800;
    const string ARMOR_DICT = "clothingtie";
    const string ARMOR_ANIM = "try_tie_positive_a";
    // ========================================

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    bool hPrev, bPrev, xPrev;
    bool bandaging;
    int healLeft, nextHeal, bandageEnd;
    bool armoring;
    int armorEnd;
    int slideEnd, nextRequest;
    Vector3 slideDir;

    public ThaiMoves()
    {
        Tick += OnTick;
    }

    // ---------- Helpers ----------
    static bool PadHeld(int c)
    {
        return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, c);
    }

    static bool PadHeldDis(int c)
    {
        return Function.Call<bool>(
            Hash.IS_DISABLED_CONTROL_PRESSED, 0, c);
    }

    static bool PadJustDis(int c)
    {
        return Function.Call<bool>(
            Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, c);
    }

    static bool GameFocused()
    {
        try
        {
            IntPtr w = Process.GetCurrentProcess().MainWindowHandle;
            return GetForegroundWindow() == w;
        }
        catch { return true; }
    }

    bool KeyDownNow(int vk)
    {
        return GameFocused()
            && (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    bool LoadDict(string dict)
    {
        Function.Call(Hash.REQUEST_ANIM_DICT, dict);
        int tries = 0;
        while (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)
            && tries++ < 50)
            Script.Yield();
        return Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict);
    }

    void PlayAnim(Ped p, string dict, string anim, int ms, int flag)
    {
        if (!LoadDict(dict)) return;
        Function.Call(Hash.TASK_PLAY_ANIM, p, dict, anim,
            8f, -8f, ms, flag, 0f, false, false, false);
    }

    void PlayAnimFb(Ped p, string d1, string a1, int f1,
        string d2, string a2, int f2, int ms)
    {
        if (LoadDict(d1))
            Function.Call(Hash.TASK_PLAY_ANIM, p, d1, a1,
                8f, -8f, ms, f1, 0f, false, false, false);
        else if (LoadDict(d2))
            Function.Call(Hash.TASK_PLAY_ANIM, p, d2, a2,
                8f, -8f, ms, f2, 0f, false, false, false);
    }

    void StopAnim(Ped p, string dict, string anim)
    {
        Function.Call(Hash.STOP_ANIM_TASK, p, dict, anim, 1f);
    }

    void KeepLoaded()
    {
        if (Game.GameTime < nextRequest) return;
        nextRequest = Game.GameTime + 1000;
        Function.Call(Hash.REQUEST_ANIM_DICT, SlideDict);
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
        if (p == null || !p.Exists() || p.IsDead || p.IsInVehicle())
        {
            slideEnd = 0;
            return;
        }

        KeepLoaded();
        int now = Game.GameTime;
        bool sliding = slideEnd > now;

        // block default L3 (stealth) and RB (cover)
        Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, PadHeal, true);
        Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, PadCover, true);

        bool aHeld = PadHeld(PadSprint) || PadHeldDis(PadSprint);

        // keyboard
        bool h = KeyDownNow(HealKey);
        bool b = KeyDownNow(ArmorKey);
        bool x = KeyDownNow(SlideKey);
        bool kbHeal = h && !hPrev;
        bool kbArmor = b && !bPrev;
        bool kbSlide = x && !xPrev;
        hPrev = h;
        bPrev = b;
        xPrev = x;

        // pad + requests from the bag menu
        bool coverPressed = PadJustDis(PadCover);
        bool bandagePressed = PadJustDis(PadHeal) || kbHeal
            || Inv.WantBandage;
        bool armorPressed = (coverPressed && !aHeld) || kbArmor
            || Inv.WantArmor;
        bool slidePressed = (coverPressed && aHeld) || kbSlide;
        Inv.WantBandage = false;
        Inv.WantArmor = false;

        // ---------- Slide ----------
        if (slidePressed && !sliding)
        {
            StartSlide(p, now);
            sliding = true;
        }

        // ---------- Bandage (needs the item) ----------
        if (bandagePressed && !bandaging && !sliding)
        {
            if (p.Health >= p.MaxHealth)
                GTA.UI.Screen.ShowSubtitle("Health is full");
            else if (!Inv.Take(Inv.Bandage))
                GTA.UI.Screen.ShowSubtitle(
                    "No bandage - buy one at a shop");
            else
            {
                bandaging = true;
                healLeft = BANDAGE_TOTAL;
                nextHeal = now + BANDAGE_TICK_MS;
                int steps = BANDAGE_TOTAL / BANDAGE_STEP;
                bandageEnd = now + steps * BANDAGE_TICK_MS;
                PlayAnimFb(p, BANDAGE_DICT, BANDAGE_ANIM,
                    BANDAGE_FLAG, BANDAGE_DICT2, BANDAGE_ANIM2,
                    49, bandageEnd - now);
            }
        }
        if (bandaging)
        {
            if (now >= nextHeal && healLeft > 0)
            {
                int hp = p.Health + BANDAGE_STEP;
                p.Health = Math.Min(p.MaxHealth, hp);
                healLeft -= BANDAGE_STEP;
                nextHeal = now + BANDAGE_TICK_MS;
            }
            if (healLeft <= 0 || now >= bandageEnd
                || p.Health >= p.MaxHealth)
            {
                bandaging = false;
                StopAnim(p, BANDAGE_DICT, BANDAGE_ANIM);
                StopAnim(p, BANDAGE_DICT2, BANDAGE_ANIM2);
            }
        }

        // ---------- Armor (needs the item) ----------
        if (armorPressed && !armoring && !sliding)
        {
            if (p.Armor >= 100)
                GTA.UI.Screen.ShowSubtitle("Armor is full");
            else if (!Inv.Take(Inv.Armor))
                GTA.UI.Screen.ShowSubtitle(
                    "No body armor - buy one at a shop");
            else
            {
                armoring = true;
                armorEnd = now + ARMOR_ANIM_MS;
                PlayAnim(p, ARMOR_DICT, ARMOR_ANIM,
                    ARMOR_ANIM_MS, 49);
            }
        }
        if (armoring && now >= armorEnd)
        {
            armoring = false;
            p.Armor = 100;
            StopAnim(p, ARMOR_DICT, ARMOR_ANIM);
            GTA.UI.Screen.ShowSubtitle("Armor equipped");
        }

        // tougher while armored
        bool armored = p.Armor > 0;
        Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS,
            p, !armored);
        Function.Call(Hash.SET_PED_CAN_RAGDOLL_FROM_PLAYER_IMPACT,
            p, !armored);

        // ---------- While sliding ----------
        if (slideEnd > now)
        {
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 21, true);
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 30, true);
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 31, true);

            float frac = (slideEnd - now) / (float)SlideTime;
            float power = frac * frac;
            Vector3 vel = slideDir * (SlideSpeed * power);
            vel.Z = p.Velocity.Z;
            p.Velocity = vel;
        }
        else if (slideEnd != 0)
        {
            slideEnd = 0;
            p.Task.ClearAll();
        }
    }

    void StartSlide(Ped p, int now)
    {
        if (armoring)
        {
            armoring = false;
            StopAnim(p, ARMOR_DICT, ARMOR_ANIM);
        }
        if (bandaging)
        {
            bandaging = false;
            StopAnim(p, BANDAGE_DICT, BANDAGE_ANIM);
            StopAnim(p, BANDAGE_DICT2, BANDAGE_ANIM2);
        }

        Vector3 f = p.ForwardVector;
        Vector3 r = Vector3.Cross(f, new Vector3(0f, 0f, 1f));
        slideDir = f + r * SideDrift;
        slideDir.Normalize();

        slideEnd = now + SlideTime;

        Vector3 v = slideDir * SlideSpeed;
        v.Z = p.Velocity.Z;
        p.Velocity = v;

        if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, SlideDict))
            Function.Call(Hash.TASK_PLAY_ANIM, p, SlideDict,
                SlideClip, 50f, -8f, SlideTime, 0, 0f,
                false, false, false);
    }
}
