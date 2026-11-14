using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

public class ThaiMoves : Script
{
    // ===== ปรับค่าตรงนี้ได้ =====
    // คีย์บอร์ด: H=0x48, X=0x58, B=0x42
    int HealKey = 0x48, SlideKey = 0x58, ArmorKey = 0x42;

    // จอย
    int PadHeal = 36;     // L3
    int PadCover = 44;    // RB (เกราะ / สไลด์ถ้ากดวิ่งค้าง)
    int PadSprint = 21;   // A (วิ่ง)
    bool BlockDefault = true;  // กันเกมทำงานปกติของ L3 (stealth) และ RB (cover) ตอนกด

    int HealAmount = 100;
    int ArmorAdd = 50;

    // พุ่ง/สไลด์
    float SlideSpeed = 12f;
    int SlideTime = 900;
    float SideDrift = 0.3f;   // + = เบี่ยงขวา, - = เบี่ยงซ้าย, 0 = ตรง
    float LeanDeg = 0f;

    // ท่าอื่น (ว่าง = ใช้ท่าย่อของเกม)
    string PoseDict = "";
    string PoseClip = "";
    // ============================

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    bool hPrev, xPrev, bPrev;
    int healLeft, nextHeal, bounceUntil, slideEnd;
    Vector3 slideDir;

    public ThaiMoves()
    {
        Tick += OnTick;
    }

    static void Anim(Ped p, string dict, string clip, int ms, int flags)
    {
        Function.Call(Hash.REQUEST_ANIM_DICT, dict);
        int t = Game.GameTime + 1500;
        while (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict) && Game.GameTime < t)
            Script.Yield();
        Function.Call(Hash.TASK_PLAY_ANIM, p, dict, clip, 8f, -8f, ms, flags, 0f, false, false, false);
    }

    static bool PadHeld(int c) { return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, c); }
    static bool PadHeldDis(int c) { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, c); }
    static bool PadJustDis(int c) { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, c); }
    static bool PadJust(int c) { return Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, c); }

    static bool GameFocused()
    {
        try { return GetForegroundWindow() == Process.GetCurrentProcess().MainWindowHandle; }
        catch { return true; }
    }

    bool KeyDownNow(int vk)
    {
        return GameFocused() && (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    void DoHeal(Ped p)
    {
        Anim(p, "mp_player_intdrink", "loop_bottle", 1500, 49);
        healLeft = HealAmount;
        nextHeal = Game.GameTime;
    }

    void DoArmor(Ped p)
    {
        Anim(p, "clothingtie", "try_tie_neutral_a", 1200, 49);
        p.Armor = Math.Min(100, p.Armor + ArmorAdd);
        bounceUntil = Game.GameTime + 600;
    }

    void DoSlide(Ped p)
    {
        if (p.IsInAir || slideEnd > Game.GameTime) return;

        Vector3 f = p.ForwardVector;
        Vector3 r = Vector3.Cross(f, new Vector3(0f, 0f, 1f));
        slideDir = f + r * SideDrift;
        slideDir.Normalize();

        slideEnd = Game.GameTime + SlideTime;

        if (PoseDict != "" && PoseClip != "")
            Anim(p, PoseDict, PoseClip, SlideTime, 1);
    }

    bool Ok(Ped p)
    {
        return p != null && p.Exists() && !p.IsDead && !p.IsInVehicle();
    }

    void OnTick(object s, EventArgs e)
    {
        Ped p = Game.Player.Character;
        if (p == null || !p.Exists()) return;

        // คีย์บอร์ด
        bool h = KeyDownNow(HealKey), x = KeyDownNow(SlideKey), b = KeyDownNow(ArmorKey);
        if (Ok(p))
        {
            if (h && !hPrev) DoHeal(p);
            if (x && !xPrev) DoSlide(p);
            if (b && !bPrev) DoArmor(p);
        }
        hPrev = h; xPrev = x; bPrev = b;

        // จอย
        if (Ok(p))
        {
            bool heal, cover;
            if (BlockDefault)
            {
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, PadHeal, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, PadCover, true);
                heal = PadJustDis(PadHeal);
                cover = PadJustDis(PadCover);
            }
            else
            {
                heal = PadJust(PadHeal);
                cover = PadJust(PadCover);
            }

            if (heal) DoHeal(p);

            if (cover)
            {
                if (PadHeld(PadSprint)) DoSlide(p);   // วิ่งค้าง + RB = สไลด์
                else DoArmor(p);                      // RB เฉยๆ = ใส่เกราะ
            }
        }

        // เลือดเด้ง
        if (healLeft > 0 && Game.GameTime >= nextHeal)
        {
            int step = Math.Min(10, healLeft);
            p.Health = Math.Min(p.MaxHealth, p.Health + step);
            healLeft -= step;
            nextHeal = Game.GameTime + 60;
            bounceUntil = Game.GameTime + 400;
            if (p.Health >= p.MaxHealth) healLeft = 0;
        }

        if (Game.GameTime < bounceUntil)
        {
            float k = (bounceUntil - Game.GameTime) / 400f;
            Function.Call(Hash.SET_TEXT_FONT, 4);
            Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.6f + 0.4f * k);
            Function.Call(Hash.SET_TEXT_COLOUR, 80, 255, 120, 255);
            Function.Call(Hash.SET_TEXT_OUTLINE);
            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, "+HP / +ARMOR");
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, 0.45f, 0.80f - 0.03f * k);
        }

        // พุ่ง/สไลด์
        if (slideEnd > Game.GameTime)
        {
            float frac = (slideEnd - Game.GameTime) / (float)SlideTime;
            float power = frac * frac;
            Vector3 v = slideDir * (SlideSpeed * power);
            v.Z = p.Velocity.Z;
            p.Velocity = v;

            if (PoseDict == "")
                Function.Call(Hash.SET_PED_STEALTH_MOVEMENT, p, true, "DEFAULT_ACTION");

            if (LeanDeg != 0f)
                p.Rotation = new Vector3(LeanDeg, 0f, p.Rotation.Z);
        }
        else if (slideEnd != 0)
        {
            slideEnd = 0;
            Function.Call(Hash.SET_PED_STEALTH_MOVEMENT, p, false, "DEFAULT_ACTION");
            if (PoseDict != "") p.Task.ClearAll();
        }
    }
}
