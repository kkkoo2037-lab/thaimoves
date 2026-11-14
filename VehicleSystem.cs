using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

public class VehicleSystem : Script
{
    // ===== ปุ่ม (รหัส Windows) =====
    int SeatbeltKey = 0x4B;   // K
    int SeatKey     = 0x4E;   // N (เปลี่ยนที่นั่ง)
    int EngineKey   = 0x5A;   // Z
    int DoorMenuKey = 0x72;   // F3
    int LeftKey     = 0x64;   // Numpad 4
    int RightKey    = 0x66;   // Numpad 6
    int HazardKey   = 0x65;   // Numpad 5
    int MenuUp = 0x26, MenuDown = 0x28, MenuSelect = 0x0D, MenuBack = 0x08;

    // ปุ่มจอยสำหรับไฟเลี้ยว (ใช้ตอนนั่งรถเท่านั้น)
    bool UsePadIndicators = true;
    int PadLeft = 174, PadRight = 175, PadDown = 173;
    // ==============================

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    bool[] prev = new bool[256];
    bool belted;
    int curInd;                 // 0 = ปิด, 1 = ซ้าย, 2 = ขวา, 3 = ฉุกเฉิน
    int lastVeh;
    bool menuOpen;
    int sel;
    Dictionary<int, bool> engineWanted = new Dictionary<int, bool>();
    Dictionary<int, int> indState = new Dictionary<int, int>();

    readonly string[] names = {
        "ALL (open/close everything)", "Front Left Door", "Front Right Door",
        "Rear Left Door", "Rear Right Door", "Hood", "Trunk", "Close menu"
    };
    readonly int[] doorIdx = { -1, 0, 1, 2, 3, 4, 5 };

    public VehicleSystem()
    {
        Tick += OnTick;
    }

    // ---------- ตัวช่วย ----------
    bool Pressed(int vk)
    {
        bool d = (GetAsyncKeyState(vk) & 0x8000) != 0;
        bool r = d && !prev[vk];
        prev[vk] = d;
        return r;
    }

    static bool PadJust(int c)
    {
        return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, c);
    }

    static void Msg(string t)
    {
        GTA.UI.Screen.ShowSubtitle(t, 1500);
    }

    static void EngineOn(Vehicle v, bool on, bool instantly)
    {
        Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, on, instantly, true);
    }

    static bool EngineRunning(Vehicle v)
    {
        return Function.Call<bool>(Hash.GET_IS_VEHICLE_ENGINE_RUNNING, v);
    }

    static bool DoorValid(Vehicle v, int i)
    {
        return Function.Call<bool>(Hash.GET_IS_DOOR_VALID, v, i);
    }

    static bool DoorOpen(Vehicle v, int i)
    {
        return Function.Call<float>(Hash.GET_VEHICLE_DOOR_ANGLE_RATIO, v, i) > 0.1f;
    }

    static void SetDoor(Vehicle v, int i, bool open)
    {
        if (open) Function.Call(Hash.SET_VEHICLE_DOOR_OPEN, v, i, false, false);
        else Function.Call(Hash.SET_VEHICLE_DOOR_SHUT, v, i, false);
    }

    static void ToggleAll(Vehicle v)
    {
        bool anyOpen = false;
        for (int i = 0; i < 8; i++)
            if (DoorValid(v, i) && DoorOpen(v, i)) anyOpen = true;
        for (int i = 0; i < 8; i++)
            if (DoorValid(v, i)) SetDoor(v, i, !anyOpen);
    }

    static bool AnyOpen(Vehicle v)
    {
        for (int i = 0; i < 8; i++)
            if (DoorValid(v, i) && DoorOpen(v, i)) return true;
        return false;
    }

    Vehicle GetTarget(Ped p, Vehicle v)
    {
        if (v != null) return v;
        Vehicle lv = Game.Player.LastVehicle;
        if (lv != null && lv.Exists() && lv.Position.DistanceTo(p.Position) < 8f) return lv;
        return null;
    }

    static void DrawText(string s, float x, float y, float scale, int r, int g, int b, int a)
    {
        Function.Call(Hash.SET_TEXT_FONT, 0);
        Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, a);
        Function.Call(Hash.SET_TEXT_CENTRE, false);
        Function.Call(Hash.SET_TEXT_DROP_SHADOW);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y, 0);
    }

    // ---------- ขึ้น / ลงรถ ----------
    void OnEnter(Ped p, Vehicle v)
    {
        lastVeh = v.Handle;
        belted = false;
        Function.Call(Hash.SET_PED_CONFIG_FLAG, p, 32, true);

        if (indState.Count > 200) indState.Clear();
        if (engineWanted.Count > 200) engineWanted.Clear();

        if (!indState.TryGetValue(lastVeh, out curInd)) curInd = 0;

        bool driver = p.SeatIndex == VehicleSeat.Driver && v.ClassType != VehicleClass.Cycles;
        if (driver)
        {
            bool want;
            if (!engineWanted.TryGetValue(lastVeh, out want))
            {
                want = false;
                engineWanted[lastVeh] = false;
            }
            EngineOn(v, want, true);
            if (!want) Msg("Engine OFF - press Z to start");
        }
    }

    void OnExit(Ped p)
    {
        lastVeh = 0;
        belted = false;
        Function.Call(Hash.SET_PED_CONFIG_FLAG, p, 32, true);
    }

    // ---------- ลูปหลัก ----------
    void OnTick(object sender, EventArgs e)
    {
        try { Tick2(); }
        catch (Exception) { }
    }

    void Tick2()
    {
        Ped p = Game.Player.Character;

        bool kSeat = Pressed(SeatbeltKey), kEng = Pressed(EngineKey), kMenu = Pressed(DoorMenuKey);
        bool kL = Pressed(LeftKey), kR = Pressed(RightKey), kH = Pressed(HazardKey);
        bool kUp = Pressed(MenuUp), kDown = Pressed(MenuDown), kSel = Pressed(MenuSelect), kBack = Pressed(MenuBack);
        bool kSwap = Pressed(SeatKey);

        if (p == null || !p.Exists() || p.IsDead) { menuOpen = false; return; }

        Vehicle v = p.IsInVehicle() ? p.CurrentVehicle : null;

        if (v != null)
        {
            if (v.Handle != lastVeh) OnEnter(p, v);
        }
        else if (lastVeh != 0)
        {
            OnExit(p);
        }

        bool driver = v != null && p.SeatIndex == VehicleSeat.Driver && v.ClassType != VehicleClass.Cycles;

        // ---------- เข็มขัด ----------
        if (v != null)
        {
            if (kSeat)
            {
                belted = !belted;
                Function.Call(Hash.SET_PED_CONFIG_FLAG, p, 32, !belted);
                Msg(belted ? "Seatbelt ON" : "Seatbelt OFF");
            }
            if (belted)
            {
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 75, true);
                if (PadJust(75)) Msg("Unbuckle seatbelt first");
            }
        }

        // ---------- เปลี่ยนที่นั่ง ----------
        if (v != null && kSwap)
        {
            if (belted)
            {
                Msg("Unbuckle seatbelt first");
            }
            else
            {
                int total = v.PassengerCapacity + 1;      // รวมที่คนขับ
                int cur = (int)p.SeatIndex;
                bool moved = false;
                for (int s = 1; s < total && !moved; s++)
                {
                    int cand = ((cur + 1 + s) % total) - 1;   // -1 = คนขับ
                    if (Function.Call<bool>(Hash.IS_VEHICLE_SEAT_FREE, v, cand, false))
                    {
                        Function.Call(Hash.SET_PED_INTO_VEHICLE, p, v, cand);
                        moved = true;
                        Msg(cand == -1 ? "Seat: Driver" : (cand == 0 ? "Seat: Front passenger" : "Seat: Rear"));
                    }
                }
                if (!moved) Msg("No free seat");
            }
        }

        // ---------- สตาร์ท / ดับรถ ----------
        if (driver)
        {
            bool want;
            if (!engineWanted.TryGetValue(v.Handle, out want))
            {
                want = false;
                engineWanted[v.Handle] = false;
            }

            if (kEng)
            {
                want = !want;
                engineWanted[v.Handle] = want;
                if (want) { EngineOn(v, true, false); Msg("Engine ON"); }
                else { EngineOn(v, false, true); Msg("Engine OFF"); }
            }
            else if (!want && EngineRunning(v))
            {
                EngineOn(v, false, true);   // กันเกมสตาร์ทเอง
            }
        }

        // ---------- ไฟเลี้ยว / ฉุกเฉิน ----------
        if (driver)
        {
            if (UsePadIndicators)
            {
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, PadLeft, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, PadRight, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, PadDown, true);
                if (PadJust(PadLeft)) kL = true;
                if (PadJust(PadRight)) kR = true;
                if (PadJust(PadDown)) kH = true;
            }

            if (kL) { curInd = (curInd == 1) ? 0 : 1; indState[v.Handle] = curInd; Msg(curInd == 1 ? "Left indicator" : "Indicator OFF"); }
            if (kR) { curInd = (curInd == 2) ? 0 : 2; indState[v.Handle] = curInd; Msg(curInd == 2 ? "Right indicator" : "Indicator OFF"); }
            if (kH) { curInd = (curInd == 3) ? 0 : 3; indState[v.Handle] = curInd; Msg(curInd == 3 ? "Hazard lights ON" : "Hazard lights OFF"); }

            Function.Call(Hash.SET_VEHICLE_INDICATOR_LIGHTS, v, 1, curInd == 1 || curInd == 3);
            Function.Call(Hash.SET_VEHICLE_INDICATOR_LIGHTS, v, 0, curInd == 2 || curInd == 3);
        }

        // ---------- เมนูเปิดประตู ----------
        if (kMenu)
        {
            if (menuOpen) menuOpen = false;
            else if (GetTarget(p, v) != null) { menuOpen = true; sel = 0; }
            else Msg("No vehicle nearby");
        }

        if (menuOpen)
        {
            Vehicle t = GetTarget(p, v);
            if (t == null || !t.Exists()) { menuOpen = false; return; }

            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            int n = names.Length;
            if (kUp || PadJust(172)) sel = (sel + n - 1) % n;
            if (kDown || PadJust(173)) sel = (sel + 1) % n;
            if (kBack || PadJust(202)) { menuOpen = false; return; }

            if (kSel || PadJust(201))
            {
                if (sel == 0) ToggleAll(t);
                else if (sel < doorIdx.Length)
                {
                    int di = doorIdx[sel];
                    if (DoorValid(t, di)) SetDoor(t, di, !DoorOpen(t, di));
                    else Msg("Not available on this vehicle");
                }
                else menuOpen = false;
            }

            // วาดเมนู
            float h = 0.06f + n * 0.034f;
            Function.Call(Hash.DRAW_RECT, 0.15f, 0.30f + h / 2f - 0.02f, 0.25f, h, 0, 0, 0, 180);
            DrawText("VEHICLE DOORS", 0.04f, 0.28f, 0.5f, 255, 255, 255, 255);

            for (int i = 0; i < n; i++)
            {
                string label = names[i];
                if (i == 0) label += AnyOpen(t) ? "  [OPEN]" : "  [CLOSED]";
                else if (i < doorIdx.Length)
                {
                    int di = doorIdx[i];
                    label += DoorValid(t, di) ? (DoorOpen(t, di) ? "  [OPEN]" : "  [CLOSED]") : "  [N/A]";
                }

                bool on = (i == sel);
                DrawText(label, 0.04f, 0.33f + i * 0.034f, 0.4f,
                    255, on ? 220 : 255, on ? 0 : 255, 255);
            }
        }
    }
}
