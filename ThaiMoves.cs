using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

public class ThaiMoves : Script
{
    // ===== ปรับค่าตรงนี้ได้ =====
    // คีย์บอร์ด: H=0x48 (เลือด), B=0x42 (เกราะ), X=0x58 (สไลด์)
    int HealKey = 0x48, ArmorKey = 0x42, SlideKey = 0x58;

    // จอย
    int PadHeal = 36;      // L3
    int PadCover = 44;     // RB = เกราะ / สไลด์ถ้ากด A ค้าง
    int PadSprint = 21;    // A (วิ่ง)
    bool BlockDefault = true;  // กัน L3 (stealth) และ RB (cover) ทำงานปกติตอนกด

    int HealAmount = 100;
    int ArmorAdd = 50;

    // สไลด์
    float SlideSpeed = 12f;
    int SlideTime = 900;
    float SideDrift = 0.3f;   // + = เบี่ยงขวา, - = เบี่ยงซ้าย, 0 = ตรง

    string[] Dicts = { "anim@arena@celeb@flat@solo@no_props@", "missheistfbi3b_ig6_v2" };
    string[] Clips = { "slide_a_player_a", "rubble_slide_franklin" };
    // ============================

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    bool hPrev, bPrev, xPrev;
    int healLeft, nextHeal, bounceUntil, slideEnd, nextRequest;
    Vector3 slideDir;

    public ThaiMoves()
    {
        Tick += OnTick;
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

    // โหลดท่าไว้ล่วงหน้าตลอด สไลด์จะได้ไม่สะดุด
    void KeepLoaded()
    {
        if (Game.GameTime < nextRequest) return;
        nextRequest = Game.GameTime + 1000;
        for (int i = 0; i < Dicts.Length; i++)
            Function.Call(Hash.REQUEST_ANIM_DICT, Dicts[i]);
        Function.Call(Hash.REQUEST_ANIM_DICT, "mp_player_intdrink");
        Function.Call(Hash.REQUEST_ANIM_DICT, "clothingtie");
    }

    static void PlayIfLoaded(Ped p, string dict, string clip, int ms, int flags)
    {
        if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict))
            Function.Call(Hash.TASK_PLAY_ANIM, p, dict, clip, 8f, -8f, ms, flags, 0f, false, false, false);
    }

    void DoHeal(Ped p)
    {
        PlayIfLoaded(p, "mp_player_intdrink", "loop_bottle", 1500, 49);
        healLeft = HealAmount;
        nextHeal = Game.GameTime;
    }

    void DoArmor(Ped p)
    {
        PlayIfLoaded(p, "clothingtie", "try_tie_neutral_a", 1200, 49);
        p.Armor = Math.Min(100, p.Armor + ArmorAdd);
        bounceUntil = Game.GameTime + 600;
    }

    void DoSlide(Ped p)
    {
        if (slideEnd > Game.GameTime) return;

        Vector3 f = p.ForwardVector;
        Vector3 r = Vector3.Cross(f, new Vector3(0f, 0f, 1f));
        slideDir = f + r * SideDrift;
        slideDir.Normalize();

        slideEnd = Game.GameTime + SlideTime;

        // ผลักตัวทันทีเฟรมแรก
        Vector3 v = slideDir * SlideSpeed;
        v.Z = p.Velocity.Z;
        p.Velocity = v;

        for (int i = 0; i < Dicts.Length; i++)
        {
            if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, Dicts[i]))
            {
                Function.Call(Hash.TASK_PLAY_ANIM, p, Dicts[i], Clips[i],
                    50f, -8f, SlideTime, 0, 0f, false, false, false);
                break;
            }
        }
    }

    bool Ok(Ped p)
    {
        return p != null && p.Exists() && !p.IsDead && !p.IsInVehicle();
    }

    void OnTick(object s, EventArgs e)
    {
        Ped p = Game.Player.Character;
        if (p == null || !p.Exists()) return;

        KeepLoaded();

        // กด A (วิ่ง) ค้างอยู่ไหม
        bool aHeld = PadHeld(PadSprint) || PadHeldDis(PadSprint);

        // คีย์บอร์ด
        bool h = KeyDownNow(HealKey), b = KeyDownNow(ArmorKey), x = KeyDownNow(SlideKey);
        if (Ok(p))
        {
            if (h && !hPrev) DoHeal(p);
            if (b && !bPrev) DoArmor(p);
            if (x && !xPrev) DoSlide(p);
        }
        hPrev = h; bPrev = b; xPrev = x;

        // จอย: L3 = เลือด, RB = เกราะ (กด A ค้าง + RB = สไลด์)
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
                if (aHeld) DoSlide(p);
                else DoArmor(p);
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

        // ระหว่างสไลด์
        if (slideEnd > Game.GameTime)
        {
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 21, true);
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 30, true);
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 31, true);

            float frac = (slideEnd - Game.GameTime) / (float)SlideTime;
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
}
