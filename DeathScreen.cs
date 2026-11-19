using System;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

// The player never really dies: at low health he goes "down"
// (ragdoll on the ground, invincible) and a timer starts.
public class DeathScreen : Script
{
    // ===== Settings =====
    // 1800 = 30 minutes. Use a small number (e.g. 30) to test.
    const int RESPAWN_SECONDS = 1800;
    const int UID = 256;

    // you go down when your health falls to this % of the bar
    const float DOWN_AT_PERCENT = 0.25f;

    // respawn point (Central Los Santos Medical Center)
    static readonly Vector3 Hospital = new Vector3(341.0f, -1397.3f, 32.5f);
    const float HospitalHeading = 48.5f;

    int ReviveKey = 0x52;     // R = use a Revive Kit on yourself
    int PadRevive = 201;      // A on the pad
    // ====================

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    bool[] prev = new bool[256];
    bool downed;
    bool busy;                // fade / respawn in progress
    int downStart;

    public DeathScreen()
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

    static bool PadJustDis(int c)
    {
        return Function.Call<bool>(
            Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, c);
    }

    static void Msg(string t)
    {
        GTA.UI.Screen.ShowSubtitle(t, 3000);
    }

    static void Box(float x, float y, float w, float h,
        int r, int g, int b, int a)
    {
        Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f,
            w, h, r, g, b, a);
    }

    static float Measure(string s, int font, float scale)
    {
        Function.Call(Hash.SET_TEXT_FONT, font);
        Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
        Function.Call(
            Hash.BEGIN_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT,
            "STRING");
        Function.Call(
            Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s);
        return Function.Call<float>(
            Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT,
            true);
    }

    // left aligned text at x
    static void Seg(string s, float x, float y, int font, float scale,
        int r, int g, int b, int a)
    {
        Function.Call(Hash.SET_TEXT_FONT, font);
        Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, a);
        Function.Call(Hash.SET_TEXT_CENTRE, false);
        Function.Call(Hash.SET_TEXT_DROP_SHADOW);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(
            Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s);
        Function.Call(
            Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y, 0);
    }

    // centered text
    static void Mid(string s, float cx, float y, int font, float scale,
        int r, int g, int b, int a)
    {
        Function.Call(Hash.SET_TEXT_FONT, font);
        Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, a);
        Function.Call(Hash.SET_TEXT_CENTRE, true);
        Function.Call(Hash.SET_TEXT_DROP_SHADOW);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(
            Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s);
        Function.Call(
            Hash.END_TEXT_COMMAND_DISPLAY_TEXT, cx, y, 0);
    }

    // up to three parts on one centered line (middle part is green)
    static void Line3(string a, string b, string c, float centerX,
        float y, int font, float scale)
    {
        float gap = 0.006f;
        float wa = a == "" ? 0f : Measure(a, font, scale);
        float wb = Measure(b, font, scale);
        float wc = c == "" ? 0f : Measure(c, font, scale);
        float total = wa + wb + wc;
        if (a != "") total += gap;
        if (c != "") total += gap;
        float x = centerX - total / 2f;
        if (a != "")
        {
            Seg(a, x, y, font, scale, 255, 255, 255, 255);
            x += wa + gap;
        }
        Seg(b, x, y, font, scale, 70, 255, 160, 255);
        x += wb;
        if (c != "")
        {
            x += gap;
            Seg(c, x, y, font, scale, 255, 255, 255, 255);
        }
    }

    static int HoldHealth(Ped p)
    {
        return (int)(100 + (p.MaxHealth - 100) * 0.30f);
    }

    // ---------- Down / up ----------
    void Protect(Ped p, bool on)
    {
        int pl = Game.Player.Handle;
        Function.Call(Hash.SET_ENTITY_INVINCIBLE, p, on);
        Function.Call(Hash.SET_PLAYER_INVINCIBLE, pl, on);
        Function.Call(Hash.SET_EVERYONE_IGNORE_PLAYER, pl, on);
        Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, pl, on);
    }

    void GoDown(Ped p, int now)
    {
        downed = true;
        downStart = now;

        // a real death slipped through: undo it
        if (p.IsDead)
        {
            Function.Call(Hash.RESURRECT_PED, p);
            p.Task.ClearAllImmediately();
            Function.Call(Hash.ANIMPOSTFX_STOP_ALL);
            Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
            Function.Call(Hash.PAUSE_DEATH_ARREST_RESTART, false);
            if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT))
                Function.Call(Hash.DO_SCREEN_FADE_IN, 300);
        }

        Protect(p, true);
        p.Health = HoldHealth(p);

        if (p.IsInVehicle())
            Function.Call(Hash.TASK_LEAVE_VEHICLE, p, p.CurrentVehicle, 16);
    }

    void GetUp(Ped p, int health)
    {
        downed = false;
        Protect(p, false);
        Function.Call(Hash.ANIMPOSTFX_STOP_ALL);
        Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
        p.Health = Math.Min(p.MaxHealth, health);
        // short ragdoll, then the character stands up by himself
        Function.Call(Hash.SET_PED_TO_RAGDOLL, p, 1200, 1200,
            0, false, false, false);
    }

    void ReviveInPlace(Ped p)
    {
        GetUp(p, 100 + (p.MaxHealth - 100) / 2);
        Msg("You were revived");
    }

    void RespawnAtHospital(Ped p)
    {
        busy = true;

        Function.Call(Hash.DO_SCREEN_FADE_OUT, 700);
        int t = Game.GameTime + 1500;
        while (!Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)
            && Game.GameTime < t)
            Script.Yield();

        GetUp(p, p.MaxHealth);
        p.Task.ClearAllImmediately();
        p.Position = Hospital;
        p.Heading = HospitalHeading;
        Function.Call(Hash.CLEAR_AREA_OF_PEDS,
            Hospital.X, Hospital.Y, Hospital.Z, 3f, 0);

        Script.Wait(400);
        Function.Call(Hash.DO_SCREEN_FADE_IN, 900);

        busy = false;
        Msg("You were treated at the hospital");
    }

    // ---------- Drawing ----------
    void DrawPulse(int now)
    {
        float cx = 0.5f, cy = 0.43f;
        int r = 70, g = 255, b = 160, a = 215;

        // short horizontal lines left and right
        Box(cx - 0.125f, cy - 0.0010f, 0.032f, 0.0022f, r, g, b, a);
        Box(cx + 0.093f, cy - 0.0010f, 0.032f, 0.0022f, r, g, b, a);

        // wavy vertical pulse line in the middle
        int n = 30;
        for (int i = 0; i < n; i++)
        {
            float y = cy - 0.080f + i * 0.0053f;
            float env = 1f - Math.Abs(i - n / 2f) / (n / 2f);
            float off = (float)Math.Sin(now / 140.0 + i * 0.7)
                * 0.0016f * env;
            Box(cx + off - 0.0007f, y, 0.0014f, 0.0058f, r, g, b, a);
        }
    }

    void DrawUi(int remainMs, int now)
    {
        // darken the screen a little
        Box(0f, 0f, 1f, 1f, 0, 0, 0, 80);

        DrawPulse(now);

        // UID
        Line3("UID :", UID.ToString(), "", 0.5f, 0.792f, 4, 0.45f);

        // respawn timer
        int sec = (int)Math.Ceiling(remainMs / 1000.0);
        if (sec < 0) sec = 0;
        string tm = string.Format("{0:00}:{1:00}", sec / 60, sec % 60);
        Line3("RESPAWN IN", tm, "MINUTES", 0.5f, 0.820f, 4, 0.62f);

        // big message
        Mid("YOU ARE DEAD", 0.5f, 0.845f, 4, 1.45f,
            255, 255, 255, 255);

        // hint row: [R] USE REVIVE KIT
        int kits = Inv.Qty[Inv.Revive];
        string label = "USE REVIVE KIT  x" + kits;
        float lw = Measure(label, 4, 0.38f);
        float chip = 0.014f;
        float total = chip + 0.006f + lw;
        float x = 0.5f - total / 2f;
        float hy = 0.934f;
        int lr = kits > 0 ? 255 : 170;
        Box(x, hy - 0.002f, chip, 0.026f, 40, 110, 80, 255);
        Mid("R", x + chip / 2f, hy, 4, 0.38f, 70, 255, 160, 255);
        Seg(label, x + chip + 0.006f, hy, 4, 0.38f,
            lr, lr, lr, 255);

        // bar (shrinks as the time runs out)
        float frac = remainMs / (RESPAWN_SECONDS * 1000f);
        if (frac < 0f) frac = 0f;
        if (frac > 1f) frac = 1f;
        float bw = 0.44f;
        Box(0.5f - bw / 2f, 0.968f, bw, 0.007f, 0, 0, 0, 170);
        Box(0.5f - bw / 2f, 0.968f, bw * frac, 0.007f,
            70, 255, 160, 255);
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
        bool kRevive = Pressed(ReviveKey);

        if (p == null || !p.Exists()) return;
        if (busy) return;

        int now = Game.GameTime;

        // ----- not down yet: watch the health -----
        if (!downed)
        {
            float limit = 100 + (p.MaxHealth - 100) * DOWN_AT_PERCENT;
            if (p.IsDead || p.Health <= limit)
                GoDown(p, now);
            else
                return;
        }

        // ----- down -----
        int remain = RESPAWN_SECONDS * 1000 - (now - downStart);
        if (remain <= 0)
        {
            RespawnAtHospital(p);
            return;
        }

        // lie on the ground, can not move or attack
        Protect(p, true);
        p.Health = HoldHealth(p);
        Function.Call(Hash.SET_PED_TO_RAGDOLL, p, 2000, 2000,
            0, false, false, false);
        Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);
        Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, 1, true);
        Function.Call(Hash.ENABLE_CONTROL_ACTION, 0, 2, true);

        // use a Revive Kit on yourself
        if (kRevive || PadJustDis(PadRevive))
        {
            if (Inv.Qty[Inv.Revive] > 0)
            {
                Inv.Take(Inv.Revive);
                ReviveInPlace(p);
                return;
            }
            Msg("No Revive Kit");
        }

        DrawUi(remain, now);
    }
}
