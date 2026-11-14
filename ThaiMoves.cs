using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

public class ThaiMoves : Script
{
    // ===== ปรับค่าตรงนี้ได้ =====
    int SlideKey = 0x58;      // คีย์บอร์ด X
    int PadCover = 44;        // RB
    int PadSprint = 21;       // A (วิ่ง)

    float SlideSpeed = 12f;   // ความแรงตอนพุ่ง
    int SlideTime = 900;      // ระยะเวลา (ms)
    float SideDrift = 0.3f;   // + = เบี่ยงขวา, - = เบี่ยงซ้าย, 0 = ตรง

    string[] Dicts = { "anim@arena@celeb@flat@solo@no_props@", "missheistfbi3b_ig6_v2" };
    string[] Clips = { "slide_a_player_a", "rubble_slide_franklin" };
    // ============================

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    bool xPrev;
    int slideEnd;
    int nextRequest;
    Vector3 slideDir;

    public ThaiMoves()
    {
        Tick += OnTick;
    }

    static bool PadHeld(int c) { return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, c); }
    static bool PadHeldDis(int c) { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_PRESSED, 0, c); }
    static bool PadJustDis(int c) { return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, c); }

    static bool GameFocused()
    {
        try { return GetForegroundWindow() == Process.GetCurrentProcess().MainWindowHandle; }
        catch { return true; }
    }

    bool KeyDownNow(int vk)
    {
        return GameFocused() && (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    // ขอโหลดท่าไว้ล่วงหน้าตลอด (ทุก 1 วินาที) ไม่ต้องรอตอนสไลด์
    void KeepLoaded()
    {
        if (Game.GameTime < nextRequest) return;
        nextRequest = Game.GameTime + 1000;
        for (int i = 0; i < Dicts.Length; i++)
            Function.Call(Hash.REQUEST_ANIM_DICT, Dicts[i]);
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

        // เล่นท่าทันที (ไม่รอโหลด ไม่ล้างท่าเดิม) blend เข้าเร็ว
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

        // คีย์บอร์ด X
        bool x = KeyDownNow(SlideKey);
        if (Ok(p) && x && !xPrev) DoSlide(p);
        xPrev = x;

        // จอย: กด A ค้าง + RB 1 ครั้ง = สไลด์
        if (Ok(p) && aHeld)
        {
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, PadCover, true);
            if (PadJustDis(PadCover)) DoSlide(p);
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
