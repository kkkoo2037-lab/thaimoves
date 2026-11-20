using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;

// The player really dies (so other players can carry the body with G
// and revive it with J, see CarryRevive). The game's own respawn is
// blocked, a timer starts and at 0 you wake up at the hospital.
public class DeathScreen : Script
{
    // ===== Settings =====
    // 1800 = 30 minutes. Use a small number (e.g. 30) to test.
    static int RESPAWN_SECONDS = 5;
    const int UID = 256;

    // false = you "go down" (lying on the ground, can not die): there
    //         is no WASTED screen, nothing gets stripped.
    // true  = you really die (other scripts see a dead body), but the
    //         game's own WASTED banner may show on top of our screen.
    static bool REAL_DEATH = true;

    // you go down when your health falls to this % of the bar
    const float DOWN_AT_PERCENT = 0.25f;

    // true  = NoWasted.asi is installed: IT removes the WASTED screen, so
    //         this script must not touch the game's respawn at all
    //         (two scripts fighting each other = glitchy death sound)
    // false = no NoWasted: this script tries to stop WASTED itself
    static bool NOWASTED_ASI = false;

    // (only when NOWASTED_ASI = false) try to stop the game's WASTED screen
    static bool KILL_WASTED_SCREEN = false;

    // master switch (false = this script does nothing)
    static bool ENABLED = true;

    // stop the game's death audio scene while dead (try if the sound is bad)
    static bool STOP_DEATH_AUDIO = true;

    // call PAUSE_DEATH_ARREST_RESTART once when you die (keeps the game
    // from starting its respawn over and over = the looping sound)
    static bool PAUSE_RESTART = true;

    // respawn point (Central Los Santos Medical Center)
    static readonly Vector3 Hospital = new Vector3(341.0f, -1397.3f, 32.5f);
    const float HospitalHeading = 48.5f;

    // ====================

    // true while you are dead (CarryRevive / ItemSystem read this)
    public static bool Downed;

    bool dead;
    int nextKill;
    int nextAudio;
    int fxStop1, fxStop2;
    int nextSnap;
    Dictionary<uint, int> guns = new Dictionary<uint, int>();
    bool busy;                // fade / respawn in progress
    int downStart;

    public DeathScreen()
    {
        LoadCfg();
        Tick += OnTick;
    }

    // scripts\DeathScreen.ini overrides the settings above (no rebuild needed)
    static void LoadCfg()
    {
        try
        {
            // try a few places (the game's folder layout may differ)
            string[] tries = {
                "scripts\\DeathScreen.ini", "scripts/DeathScreen.ini",
                "DeathScreen.ini"
            };
            string path = null;
            foreach (string t in tries)
                if (File.Exists(t)) { path = t; break; }
            if (path == null)
            {
                Log("DeathScreen.ini not found - using the settings inside the code"
                    + " (real_death=" + REAL_DEATH + " nowasted=" + NOWASTED_ASI + ")");
                return;
            }
            Log("settings read from " + path);
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();
                bool b = v == "1" || v.ToLowerInvariant() == "true";
                int n;
                switch (k)
                {
                    case "enabled": ENABLED = b; break;
                    case "real_death": REAL_DEATH = b; break;
                    case "nowasted_asi": NOWASTED_ASI = b; break;
                    case "kill_wasted_screen": KILL_WASTED_SCREEN = b; break;
                    case "stop_death_audio": STOP_DEATH_AUDIO = b; break;
                    case "pause_restart": PAUSE_RESTART = b; break;
                    case "respawn_seconds":
                        if (int.TryParse(v, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out n) && n > 0)
                            RESPAWN_SECONDS = n;
                        break;
                }
            }
        }
        catch (Exception) { }
    }

    // ---------- Helpers ----------
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

    // ---------- Downed mode (REAL_DEATH = false) ----------
    static int HoldHealth(Ped p)
    {
        return (int)(100 + (p.MaxHealth - 100) * 0.30f);
    }

    static void Protect(Ped p, bool on)
    {
        int pl = Game.Player.Handle;
        Function.Call(Hash.SET_ENTITY_INVINCIBLE, p, on);
        Function.Call(Hash.SET_PLAYER_INVINCIBLE, pl, on);
        Function.Call(Hash.SET_EVERYONE_IGNORE_PLAYER, pl, on);
        Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, pl, on);
        Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER,
            pl, on ? 0f : 1f);
    }

    void GoDown(Ped p, int now)
    {
        dead = true;
        Downed = true;
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
            RestoreHud(p);
        }

        Protect(p, true);
        p.Health = HoldHealth(p);

        if (p.IsInVehicle())
            Function.Call(Hash.TASK_LEAVE_VEHICLE, p, p.CurrentVehicle, 16);
    }

    void TickDowned(Ped p, int now)
    {
        if (!dead)
        {
            float limit = 100 + (p.MaxHealth - 100) * DOWN_AT_PERCENT;
            if (!(p.IsDead || p.Health <= limit))
            {
                SnapshotWeapons(p, now);
                return;
            }
            GoDown(p, now);
        }

        int remain = RESPAWN_SECONDS * 1000 - (now - downStart);
        if (remain <= 0)
        {
            RespawnAtHospital(p);
            return;
        }

        // somebody else revived us (CarryRevive raises the health)
        if (p.Health >= HoldHealth(p) + 10)
        {
            EndDead(p);
            Function.Call(Hash.SET_PED_TO_RAGDOLL, p, 1200, 1200,
                0, false, false, false);
            Msg("You were revived");
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

        DrawUi(remain, now);
    }

    // ---------- Weapons / HUD ----------
    void KillRespawnScript(int now)
    {
        if (NOWASTED_ASI || !KILL_WASTED_SCREEN || now < nextKill) return;
        nextKill = now + 500;
        Function.Call(Hash.TERMINATE_ALL_SCRIPTS_WITH_THIS_NAME,
            "respawn_controller");
    }

    // remember the weapons while alive (death strips them)
    void SnapshotWeapons(Ped p, int now)
    {
        if (now < nextSnap) return;
        nextSnap = now + 1000;
        Dictionary<uint, int> d = new Dictionary<uint, int>();
        foreach (WeaponHash h in Enum.GetValues(typeof(WeaponHash)))
        {
            uint hash = (uint)h;
            if (d.ContainsKey(hash)) continue;
            if (Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON, p, hash, false))
                d[hash] = Function.Call<int>(
                    Hash.GET_AMMO_IN_PED_WEAPON, p, hash);
        }
        if (d.Count > 0) guns = d;
    }

    void RestoreWeapons(Ped p)
    {
        foreach (KeyValuePair<uint, int> kv in guns)
        {
            if (Function.Call<bool>(Hash.HAS_PED_GOT_WEAPON,
                p, kv.Key, false)) continue;
            Function.Call(Hash.GIVE_WEAPON_TO_PED, p, kv.Key,
                Math.Max(kv.Value, 1), false, false);
        }
    }

    void RestoreHud(Ped p)
    {
        Function.Call(Hash.DISPLAY_RADAR, true);
        Function.Call(Hash.DISPLAY_HUD, true);
        Function.Call(Hash.SET_PLAYER_CONTROL,
            Game.Player.Handle, true, 0);
        RestoreWeapons(p);
    }

    // ---------- Dead / alive ----------
    void GoDead(int now)
    {
        dead = true;
        Downed = true;
        downStart = now;
        nextKill = 0;
        KillRespawnScript(now);
        Log("dead at " + now + " (nowasted=" + NOWASTED_ASI + ")");
        if (PAUSE_RESTART)
            Function.Call(Hash.PAUSE_DEATH_ARREST_RESTART, true);
        if (NOWASTED_ASI) return;   // NoWasted handles the rest

        // ONE time only (calling these every frame makes the WASTED
        // banner / sound restart over and over)
        Function.Call(Hash.PAUSE_DEATH_ARREST_RESTART, true);
        Function.Call(Hash.IGNORE_NEXT_RESTART, true);
        Function.Call(Hash.SET_FADE_OUT_AFTER_DEATH, false);
        Function.Call(Hash.SET_FADE_OUT_AFTER_ARREST, false);

        // remove the grey WASTED screen effect twice, once each
        fxStop1 = now + 800;
        fxStop2 = now + 2500;
    }

    // scripts\DeathScreen.log - short notes for bug reports
    static void Log(string t)
    {
        try
        {
            File.AppendAllText("scripts\\DeathScreen.log",
                DateTime.Now.ToString("HH:mm:ss") + "  " + t + "\r\n");
        }
        catch (Exception) { }
    }

    static void StopDeathFx()
    {
        Function.Call(Hash.ANIMPOSTFX_STOP, "DeathFailOut");
        Function.Call(Hash.ANIMPOSTFX_STOP, "DeathFailMPDark");
        Function.Call(Hash.ANIMPOSTFX_STOP, "DeathFailMPIn");
    }

    void EndDead(Ped p)
    {
        dead = false;
        Downed = false;
        Protect(p, false);
        Function.Call(Hash.ANIMPOSTFX_STOP_ALL);
        Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
        if (!NOWASTED_ASI || PAUSE_RESTART)
            Function.Call(Hash.PAUSE_DEATH_ARREST_RESTART, false);
        Log("alive again");
        fxStop1 = fxStop2 = 0;
        if (Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT))
            Function.Call(Hash.DO_SCREEN_FADE_IN, 300);
        RestoreHud(p);
    }

    void RespawnAtHospital(Ped p)
    {
        busy = true;

        Function.Call(Hash.DO_SCREEN_FADE_OUT, 700);
        int t = Game.GameTime + 1500;
        while (!Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)
            && Game.GameTime < t)
            Script.Yield();

        Function.Call(Hash.RESURRECT_PED, p);
        p.Task.ClearAllImmediately();
        if (p.IsInVehicle())
            Function.Call(Hash.TASK_LEAVE_VEHICLE, p, p.CurrentVehicle, 16);
        Function.Call(Hash.SET_ENTITY_COORDS_NO_OFFSET, p,
            Hospital.X, Hospital.Y, Hospital.Z, false, false, false);
        p.Heading = HospitalHeading;
        p.Health = p.MaxHealth;
        Function.Call(Hash.CLEAR_AREA_OF_PEDS,
            Hospital.X, Hospital.Y, Hospital.Z, 3f, 0);
        EndDead(p);

        Script.Wait(400);
        Function.Call(Hash.DO_SCREEN_FADE_IN, 900);

        busy = false;
        Msg("You were treated at the hospital");
    }

    // ---------- Drawing ----------
    // one heartbeat (P, Q, R, S, T waves), phase 0..1 -> -1..1
    static float Ecg(float ph)
    {
        if (ph >= 0.08f && ph < 0.16f)       // P
            return 0.12f * (float)Math.Sin(Math.PI * (ph - 0.08f) / 0.08f);
        if (ph >= 0.20f && ph < 0.23f)       // Q
            return -0.15f * (float)Math.Sin(Math.PI * (ph - 0.20f) / 0.03f);
        if (ph >= 0.23f && ph < 0.28f)       // R (tall spike)
        {
            float t = (ph - 0.23f) / 0.05f;
            return t < 0.5f ? t * 2f : (1f - t) * 2f;
        }
        if (ph >= 0.28f && ph < 0.33f)       // S
            return -0.30f * (float)Math.Sin(Math.PI * (ph - 0.28f) / 0.05f);
        if (ph >= 0.44f && ph < 0.60f)       // T
            return 0.22f * (float)Math.Sin(Math.PI * (ph - 0.44f) / 0.16f);
        return 0f;
    }

    // heart monitor: a bright dot sweeps across, drawing the heartbeat
    // line behind it, the old part of the line fades out
    void DrawPulse(int now)
    {
        float cx = 0.5f, cy = 0.43f;
        float W = 0.34f;          // line width (screen fraction)
        float amp = 0.075f;       // spike height (screen fraction)
        const int N = 170;
        const float PERIOD = 1.2f;                 // seconds per beat
        const float SWEEP = PERIOD * 2f;           // 2 beats per sweep
        int r = 70, g = 255, b = 160;

        float head = (now / 1000f % SWEEP) / SWEEP;   // 0..1
        float dx = W / N;
        float th = 0.0032f;

        // faint baseline
        Box(cx - W / 2f, cy - 0.0006f, W, 0.0012f, r, g, b, 35);

        float prevY = cy;
        for (int i = 0; i < N; i++)
        {
            float u = (i + 0.5f) / N;
            float ph = (u * SWEEP % PERIOD) / PERIOD;
            float y = cy - Ecg(ph) * amp;

            // how far behind the head is this sample (0..1)
            float behind = head - u;
            if (behind < 0f) behind += 1f;

            int a;
            if (behind > 0.93f) a = 0;                  // gap ahead of head
            else a = (int)(255f * (1f - behind / 0.93f) * 0.95f) + 25;
            if (a > 255) a = 255;

            if (a > 0 && i > 0)
            {
                float top = Math.Min(y, prevY) - th / 2f;
                float h = Math.Abs(y - prevY) + th;
                Box(cx - W / 2f + i * dx, top, dx + 0.0008f, h,
                    r, g, b, a);
            }
            prevY = y;
        }

        // bright dot at the head
        float hu = head * W;
        float hy = cy - Ecg((head * SWEEP % PERIOD) / PERIOD) * amp;
        Box(cx - W / 2f + hu - 0.0025f, hy - 0.0045f, 0.005f, 0.009f,
            200, 255, 225, 255);
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

        // hint (only another player can revive you)
        Mid("WAITING FOR SOMEONE TO REVIVE YOU", 0.5f, 0.934f, 4, 0.36f,
            190, 255, 220, 255);

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

        if (p == null || !p.Exists()) return;
        if (!ENABLED) return;
        if (busy) return;

        int now = Game.GameTime;

        if (!REAL_DEATH)
        {
            TickDowned(p, now);
            return;
        }

        // ---------- real death ----------
        KillRespawnScript(now);

        if (!dead)
        {
            if (!p.IsDead)
            {
                SnapshotWeapons(p, now);
                return;
            }
            GoDead(now);
        }

        // someone revived us (CarryRevive resurrects the body)
        if (!p.IsDead)
        {
            EndDead(p);
            Msg("You were revived");
            return;
        }

        int remain = RESPAWN_SECONDS * 1000 - (now - downStart);
        if (remain <= 0)
        {
            RespawnAtHospital(p);
            return;
        }

        if (!NOWASTED_ASI)
        {
            if (fxStop1 != 0 && now >= fxStop1) { fxStop1 = 0; StopDeathFx(); }
            if (fxStop2 != 0 && now >= fxStop2) { fxStop2 = 0; StopDeathFx(); }
        }

        if (STOP_DEATH_AUDIO && now > nextAudio)
        {
            nextAudio = now + 300;
            Function.Call(Hash.STOP_AUDIO_SCENE, "DEATH_SCENE");
        }

        DrawUi(remain, now);
    }
}
