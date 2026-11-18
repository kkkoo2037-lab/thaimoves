using System;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

public class CarryRevive : Script
{
    // ===== Keys =====
    int CarryKey = 0x47;     // G  pick up / put down
    int ReviveKey = 0x4A;    // J  revive (needs a Revive Kit)
    int CancelKey = 0x08;    // Backspace (cancel revive)

    const int REVIVE_MS = 10000;     // 10 seconds
    const float PICKUP_RANGE = 2.5f;

    // alive person: piggyback
    const string PIG_DICT = "anim@arena@celeb@flat@paired@no_props@";
    const string PIG_A = "piggyback_c_player_a";
    const string PIG_B = "piggyback_c_player_b";
    // dead person (and fallback): fireman carry
    const string FIRE_DICT = "missfinale_c2mcs_1";
    const string FIRE_A = "fin_c2_mcs_1_camman";
    const string FIRE_B_DICT = "nm";
    const string FIRE_B = "firemans_carry";
    // CPR
    const string CPR_DICT = "mini@cpr@char_a@cpr_str";
    const string CPR_A = "cpr_pumpchest";
    // ================

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    bool[] prev = new bool[256];

    Ped carried;
    bool carriedDead;
    string curDict = "";
    string curClip = "";
    int nextAnimCheck;

    bool reviving;
    Ped target;
    int reviveStart;

    public CarryRevive()
    {
        Tick += OnTick;
    }

    // ---------- Helpers ----------
    bool Pressed(int vk)
    {
        bool d = (GetAsyncKeyState(vk) & 0x8000) != 0;
        bool r = d && !prev[vk];
        prev[vk] = d;
        return r;
    }

    static void Msg(string t)
    {
        GTA.UI.Screen.ShowSubtitle(t, 2000);
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

    static bool LoadDict(string dict)
    {
        Function.Call(Hash.REQUEST_ANIM_DICT, dict);
        int tries = 0;
        while (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict)
            && tries++ < 60)
            Script.Yield();
        return Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict);
    }

    static void Play(Ped ped, string dict, string clip, int flag)
    {
        Function.Call(Hash.TASK_PLAY_ANIM, ped, dict, clip,
            8f, -8f, -1, flag, 0f, false, false, false);
    }

    static bool Playing(Ped ped, string dict, string clip)
    {
        return Function.Call<bool>(
            Hash.IS_ENTITY_PLAYING_ANIM, ped, dict, clip, 3);
    }

    static Ped FindNearest(Ped p, float range, bool deadOnly)
    {
        Ped best = null;
        float bd = range;
        Ped[] peds = World.GetNearbyPeds(p, range);
        for (int i = 0; i < peds.Length; i++)
        {
            Ped t = peds[i];
            if (t == null || !t.Exists() || t == p) continue;
            if (deadOnly && !t.IsDead) continue;
            if (t.IsInVehicle()) continue;
            if (Function.Call<bool>(Hash.IS_ENTITY_ATTACHED, t))
                continue;
            float d = t.Position.DistanceTo(p.Position);
            if (d < bd)
            {
                bd = d;
                best = t;
            }
        }
        return best;
    }

    // ---------- Carry ----------
    void StartCarry(Ped p)
    {
        Ped t = FindNearest(p, PICKUP_RANGE, false);
        if (t == null)
        {
            Msg("No one nearby");
            return;
        }

        carriedDead = t.IsDead;
        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, t, true, true);

        if (!carriedDead)
        {
            t.Task.ClearAllImmediately();
            Function.Call(
                Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, t, true);
        }

        if (!carriedDead && LoadDict(PIG_DICT))
        {
            // alive: piggyback ride
            curDict = PIG_DICT;
            curClip = PIG_A;
            Play(p, curDict, curClip, 49);
            Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, t, p, 0,
                0f, -0.07f, 0.45f, 0f, 0f, 0f,
                false, false, false, false, 2, false);
            Play(t, PIG_DICT, PIG_B, 33);
        }
        else
        {
            // dead (or fallback): over the shoulder
            LoadDict(FIRE_DICT);
            curDict = FIRE_DICT;
            curClip = FIRE_A;
            Play(p, curDict, curClip, 49);
            Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, t, p, 0,
                0.27f, 0.15f, 0.63f, 0.5f, 0.5f, 180f,
                false, false, false, false, 2, false);
            if (!carriedDead && LoadDict(FIRE_B_DICT))
                Play(t, FIRE_B_DICT, FIRE_B, 33);
        }

        carried = t;
        Msg(carriedDead ? "Carrying body (G = put down)"
            : "Carrying (G = put down)");
    }

    void Drop(Ped p)
    {
        if (carried != null && carried.Exists())
        {
            Function.Call(Hash.DETACH_ENTITY, carried, true, true);
            Function.Call(
                Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS,
                carried, false);
            if (!carriedDead)
                carried.Task.ClearAllImmediately();

            Vector3 np = Function.Call<Vector3>(
                Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS,
                p, 0f, 0.9f, 0f);
            np.Z = p.Position.Z - 0.6f;
            carried.Position = np;
        }

        if (curDict != "")
            Function.Call(Hash.STOP_ANIM_TASK, p, curDict, curClip, 1f);
        p.Task.ClearAll();

        carried = null;
        curDict = "";
        curClip = "";
    }

    // ---------- Revive ----------
    bool CanRevive()
    {
        if (Inv.Qty[Inv.Revive] <= 0)
        {
            Msg("No Revive Kit - buy one at a shop");
            return false;
        }
        return true;
    }

    void StartRevive(Ped p, Ped t)
    {
        reviving = true;
        target = t;
        reviveStart = Game.GameTime;

        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, t, true, true);

        // stand next to the body, facing it
        Vector3 d = p.Position - t.Position;
        d.Z = 0f;
        if (d.Length() < 0.1f) d = new Vector3(0.6f, 0f, 0f);
        d.Normalize();
        Vector3 np = t.Position + d * 0.65f;
        np.Z = p.Position.Z;
        p.Position = np;
        Vector3 v = t.Position - np;
        p.Heading = (float)(Math.Atan2(-v.X, v.Y) * 180.0 / Math.PI);

        LoadDict(CPR_DICT);
        Play(p, CPR_DICT, CPR_A, 1);
    }

    void StopRevive(Ped p, string why)
    {
        reviving = false;
        target = null;
        Function.Call(Hash.STOP_ANIM_TASK, p, CPR_DICT, CPR_A, 1f);
        p.Task.ClearAll();
        if (why != "") Msg(why);
    }

    void CompleteRevive(Ped p)
    {
        Ped t = target;
        Inv.Take(Inv.Revive);

        Function.Call(Hash.RESURRECT_PED, t);
        t.Health = 150;
        t.Task.ClearAllImmediately();
        Function.Call(Hash.SET_PED_TO_RAGDOLL, t, 1500, 1500,
            0, false, false, false);

        StopRevive(p, "");
        Msg("Revived!");
    }

    void DrawProgress(int now)
    {
        float frac = (now - reviveStart) / (float)REVIVE_MS;
        if (frac < 0f) frac = 0f;
        if (frac > 1f) frac = 1f;
        int left = (int)Math.Ceiling((1f - frac) * REVIVE_MS / 1000f);

        float w = 0.30f;
        float cy = 0.86f;
        Rect(0.5f, cy, w + 0.006f, 0.034f, 0, 0, 0, 190);
        float fw = w * frac;
        Rect(0.5f - w / 2f + fw / 2f, cy, fw, 0.024f,
            225, 100, 130, 255);
        DrawText("REVIVING  " + left + "s", 0.5f, cy - 0.048f, 0.45f,
            255, 255, 255, 255, true);
        DrawText("Backspace = cancel", 0.5f, cy + 0.025f, 0.28f,
            200, 200, 205, 255, true);
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

        bool kCarry = Pressed(CarryKey);
        bool kRevive = Pressed(ReviveKey);
        bool kCancel = Pressed(CancelKey);

        if (p == null || !p.Exists())
        {
            Inv.Busy = false;
            return;
        }

        int now = Game.GameTime;

        // player died or got into a vehicle: let go
        if (p.IsDead || p.IsInVehicle())
        {
            if (carried != null) Drop(p);
            if (reviving) { reviving = false; target = null; }
            Inv.Busy = false;
            Inv.WantRevive = false;
            return;
        }

        bool askRevive = kRevive || Inv.WantRevive;
        Inv.WantRevive = false;

        // ---------- reviving (CPR) ----------
        if (reviving)
        {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            bool cancel = kCancel
                || Function.Call<bool>(
                    Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, 202);

            if (target == null || !target.Exists() || !target.IsDead)
            {
                StopRevive(p, "Revive stopped");
            }
            else if (cancel)
            {
                StopRevive(p, "Revive cancelled");
            }
            else
            {
                if (now >= nextAnimCheck)
                {
                    nextAnimCheck = now + 500;
                    if (!Playing(p, CPR_DICT, CPR_A))
                        Play(p, CPR_DICT, CPR_A, 1);
                }
                DrawProgress(now);
                if (now - reviveStart >= REVIVE_MS) CompleteRevive(p);
            }

            Inv.Busy = true;
            return;
        }

        // ---------- carrying ----------
        if (carried != null)
        {
            if (!carried.Exists())
            {
                Drop(p);
            }
            else
            {
                // block things that would break the carry
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 21, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 22, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 23, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 24, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 25, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 140, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 141, true);
                Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, 142, true);

                if (now >= nextAnimCheck)
                {
                    nextAnimCheck = now + 500;
                    if (curDict != "" && !Playing(p, curDict, curClip))
                        Play(p, curDict, curClip, 49);
                }

                if (carriedDead && askRevive)
                {
                    // revive the body we are carrying
                    if (CanRevive())
                    {
                        Ped body = carried;
                        Drop(p);
                        StartRevive(p, body);
                    }
                }
                else if (kCarry)
                {
                    Drop(p);
                    Msg("Put down");
                }
            }

            Inv.Busy = carried != null || reviving;
            return;
        }

        // ---------- idle ----------
        Inv.Busy = false;

        if (kCarry) StartCarry(p);

        if (askRevive)
        {
            Ped t = FindNearest(p, PICKUP_RANGE, true);
            if (t == null) Msg("No dead body nearby");
            else if (CanRevive()) StartRevive(p, t);
        }

        Inv.Busy = carried != null || reviving;
    }
}
