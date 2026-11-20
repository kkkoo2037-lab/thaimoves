using System;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;

// Shared stats (the item system feeds Food from here)
public static class PlayerStats
{
    public static float Food = 100f;      // 0..100
    public static float Stress = 0f;      // 0..100
    public const int Uid = 521;           // number shown on the HUD
}

public class StatusHud : Script
{
    // ===== Settings =====
    // bottom-left status panel (top-left corner of the panel)
    const float HudX = 0.012f;
    const float HudY = 0.900f;

    // minutes for food to go from full to empty
    const float FoodMinutes = 60f;
    // camera shake when stress is high
    const bool StressEffects = true;
    // speedometer / gear panel while driving
    const bool ShowVehicleHud = true;
    // ====================

    const string CFG = "scripts\\PlayerStats.cfg";

    int lastTick;
    int lastShot;
    int lastHealth;
    int nextZone;
    float starve;
    float saveTimer;
    bool shaking;
    string zoneName = "";

    public StatusHud()
    {
        Load();
        lastTick = Game.GameTime;
        Tick += OnTick;
    }

    // ---------- Save / Load ----------
    void Save()
    {
        try
        {
            File.WriteAllText(CFG,
                PlayerStats.Food.ToString("0.0") + ","
                + PlayerStats.Stress.ToString("0.0"));
        }
        catch (Exception) { }
    }

    void Load()
    {
        try
        {
            if (!File.Exists(CFG)) return;
            string[] a = File.ReadAllText(CFG).Trim().Split(',');
            if (a.Length < 2) return;
            PlayerStats.Food = Clamp(float.Parse(a[0]), 0f, 100f);
            PlayerStats.Stress = Clamp(float.Parse(a[1]), 0f, 100f);
        }
        catch (Exception) { }
    }

    static float Clamp(float v, float lo, float hi)
    {
        if (v < lo) return lo;
        if (v > hi) return hi;
        return v;
    }

    // ---------- Drawing helpers ----------
    static void Box(float x, float y, float w, float h,
        int r, int g, int b, int a)
    {
        Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f,
            w, h, r, g, b, a);
    }

    static void Frame(float x, float y, float w, float h, float t,
        int r, int g, int b, int a)
    {
        float tx = t * 0.5625f;
        Box(x, y, w, t, r, g, b, a);
        Box(x, y + h - t, w, t, r, g, b, a);
        Box(x, y, tx, h, r, g, b, a);
        Box(x + w - tx, y, tx, h, r, g, b, a);
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

    // filled circle (r = radius in screen height units)
    static void Disc(float cx, float cy, float r,
        int cr, int cg, int cb, int ca)
    {
        int n = 14;
        float step = 2f * r / n;
        for (int k = 0; k < n; k++)
        {
            float yo = -r + (k + 0.5f) * step;
            float half = (float)Math.Sqrt(Math.Max(0f, r * r - yo * yo))
                * 0.5625f;
            Box(cx - half, cy + yo - step / 2f, half * 2f,
                step + 0.0006f, cr, cg, cb, ca);
        }
    }

    // ring that fills clockwise from the top
    static void Ring(float cx, float cy, float r, float frac,
        int fr, int fg, int fb)
    {
        int n = 44;
        for (int i = 0; i < n; i++)
        {
            double a = i * 2.0 * Math.PI / n;
            float px = cx + (float)Math.Sin(a) * r * 0.5625f;
            float py = cy - (float)Math.Cos(a) * r;
            bool on = (i / (float)n) < frac;
            if (on)
                Box(px - 0.0026f, py - 0.0046f, 0.0052f, 0.0092f,
                    fr, fg, fb, 255);
            else
                Box(px - 0.0026f, py - 0.0046f, 0.0052f, 0.0092f,
                    20, 40, 80, 255);
        }
    }

    // ---------- Stats ----------
    void UpdateStats(Ped p, float dt, int now)
    {
        // food goes down slowly
        PlayerStats.Food -= dt * 100f / (FoodMinutes * 60f);
        PlayerStats.Food = Clamp(PlayerStats.Food, 0f, 100f);

        // starving hurts a little (never below the "down" level)
        if (PlayerStats.Food <= 0.01f && p.Health > 130 && !p.IsDead)
        {
            starve += dt;
            if (starve >= 5f)
            {
                starve = 0f;
                p.Health = p.Health - 1;
            }
        }
        else starve = 0f;

        // stress
        float st = PlayerStats.Stress;
        st -= dt * 0.35f;                         // calms down slowly

        if (p.IsShooting && now - lastShot > 120)
        {
            lastShot = now;
            st += 0.9f;                           // shooting
        }

        int hp = p.Health;
        if (hp < lastHealth) st += (lastHealth - hp) * 0.25f;   // hurt
        lastHealth = hp;

        int wl = Game.Player.WantedLevel;
        if (wl > 0) st += dt * 0.35f * wl;        // wanted

        if (p.IsInVehicle())
        {
            Vehicle v = p.CurrentVehicle;
            if (v != null && v.Speed * 3.6f > 150f)
                st += dt * 0.5f;                  // fast driving
        }

        PlayerStats.Stress = Clamp(st, 0f, 100f);

        // shaky camera when very stressed
        if (StressEffects)
        {
            bool high = PlayerStats.Stress >= 75f;
            if (high && !shaking)
            {
                Function.Call(Hash.SHAKE_GAMEPLAY_CAM,
                    "HAND_SHAKE", 0.15f);
                shaking = true;
            }
            else if (!high && shaking)
            {
                Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true);
                shaking = false;
            }
        }

        saveTimer += dt;
        if (saveTimer >= 15f)
        {
            saveTimer = 0f;
            Save();
        }
    }

    // ---------- Status panel (health, armor, stamina, food, stress) ----------
    void FoodIcon(float cx, float cy)
    {
        float fx = cx - 0.0075f;
        float kx = cx + 0.0075f;
        // fork
        Box(fx - 0.0040f, cy - 0.016f, 0.0012f, 0.008f, 255, 255, 255, 255);
        Box(fx - 0.0006f, cy - 0.016f, 0.0012f, 0.008f, 255, 255, 255, 255);
        Box(fx + 0.0028f, cy - 0.016f, 0.0012f, 0.008f, 255, 255, 255, 255);
        Box(fx - 0.0044f, cy - 0.009f, 0.0088f, 0.0016f, 255, 255, 255, 255);
        Box(fx - 0.0006f, cy - 0.008f, 0.0012f, 0.024f, 255, 255, 255, 255);
        // knife
        Box(kx - 0.0020f, cy - 0.016f, 0.0032f, 0.016f, 255, 255, 255, 255);
        Box(kx - 0.0006f, cy - 0.001f, 0.0012f, 0.017f, 255, 255, 255, 255);
    }

    void BrainIcon(float cx, float cy)
    {
        Disc(cx - 0.0050f, cy, 0.0125f, 255, 255, 255, 255);
        Disc(cx + 0.0050f, cy, 0.0125f, 255, 255, 255, 255);
        Box(cx - 0.0003f, cy - 0.012f, 0.0006f, 0.024f, 20, 24, 36, 255);
    }

    void DrawStatus(Ped p)
    {
        float w = 0.205f, h = 0.085f;

        // panel
        Box(HudX, HudY, w, h, 22, 24, 32, 240);
        Frame(HudX, HudY, w, h, 0.002f, 40, 46, 62, 255);

        // health
        float maxH = Math.Max(1, p.MaxHealth - 100);
        float hpf = Clamp((p.Health - 100) / maxH, 0f, 1f);
        float bx = HudX + 0.004f, by = HudY + 0.005f;
        float bw = w - 0.008f, bh = 0.030f;
        Box(bx, by, bw, bh, 8, 30, 70, 255);
        Box(bx, by, bw * hpf, bh, 20, 130, 255, 255);
        Box(bx, by, bw * hpf, bh * 0.4f, 70, 175, 255, 140);
        string pct = ((int)Math.Round(hpf * 100f)) + "%";
        float pw = Measure(pct, 4, 0.40f);
        Seg(pct, bx + bw - pw - 0.006f, by + 0.003f, 4, 0.40f,
            255, 255, 255, 255);

        // armor (thin)
        float arf = Clamp(p.Armor / 100f, 0f, 1f);
        float ay = by + bh + 0.003f;
        Box(bx, ay, bw, 0.005f, 20, 34, 52, 255);
        if (arf > 0f)
            Box(bx, ay, bw * arf, 0.005f, 190, 225, 255, 255);

        // stamina (green)
        float sta = Function.Call<float>(
            Hash.GET_PLAYER_SPRINT_STAMINA_REMAINING,
            Game.Player.Handle) / 100f;
        sta = Clamp(sta, 0f, 1f);
        float sy = ay + 0.009f;
        Box(bx, sy, bw, 0.008f, 10, 44, 24, 255);
        Box(bx, sy, bw * sta, 0.008f, 60, 230, 120, 255);
        Box(bx, sy, bw * sta, 0.003f, 150, 255, 190, 150);

        // ID tab
        Box(HudX + 0.010f, HudY + h - 0.024f, 0.058f, 0.022f,
            30, 33, 44, 255);
        Seg("ID : " + PlayerStats.Uid, HudX + 0.016f,
            HudY + h - 0.0215f, 4, 0.30f, 255, 255, 255, 255);

        // food ring
        float r = 0.030f;
        float cy = HudY + 0.042f;
        float cx1 = HudX + w + 0.030f;
        float cx2 = cx1 + 0.066f;

        float food = PlayerStats.Food / 100f;
        if (food > 0.2f) Ring(cx1, cy, r, food, 30, 150, 255);
        else Ring(cx1, cy, r, food, 255, 70, 70);
        Disc(cx1, cy, r - 0.0075f, 12, 16, 28, 255);
        FoodIcon(cx1, cy);

        // stress ring
        float stf = PlayerStats.Stress / 100f;
        if (stf < 0.7f) Ring(cx2, cy, r, stf, 30, 150, 255);
        else Ring(cx2, cy, r, stf, 255, 70, 70);
        Disc(cx2, cy, r - 0.0075f, 12, 16, 28, 255);
        BrainIcon(cx2, cy);
    }

    // ---------- Vehicle panel (speed, gear, tires, rpm) ----------
    static bool Burst(Vehicle v, int wheel)
    {
        return Function.Call<bool>(
            Hash.IS_VEHICLE_TYRE_BURST, v, wheel, false);
    }

    void CarIcon(Vehicle v, float cx, float cy)
    {
        float carW = 0.026f, carH = 0.100f;

        // tires (red when burst): 0 FL, 1 FR, 4 RL, 5 RR
        int[] wheels = { 0, 1, 4, 5 };
        float[] ox = { -1f, 1f, -1f, 1f };
        float[] oy = { -0.030f, -0.030f, 0.030f, 0.030f };
        for (int i = 0; i < 4; i++)
        {
            float tx = cx + ox[i] * (carW / 2f + 0.0035f);
            float ty = cy + oy[i];
            bool bad = Burst(v, wheels[i]);
            if (bad)
                Box(tx - 0.0030f, ty - 0.012f, 0.0060f, 0.024f,
                    255, 40, 40, 255);
            else
                Box(tx - 0.0030f, ty - 0.012f, 0.0060f, 0.024f,
                    70, 76, 92, 255);
        }

        // body
        Box(cx - carW / 2f, cy - carH / 2f, carW, carH,
            205, 210, 225, 255);
        Box(cx - carW * 0.35f, cy - carH / 2f - 0.006f,
            carW * 0.70f, 0.008f, 205, 210, 225, 255);
        Box(cx - carW * 0.35f, cy + carH / 2f - 0.002f,
            carW * 0.70f, 0.008f, 205, 210, 225, 255);

        // glass and roof
        Box(cx - 0.009f, cy - 0.030f, 0.018f, 0.017f, 90, 95, 112, 255);
        Box(cx - 0.009f, cy - 0.011f, 0.018f, 0.024f, 175, 180, 200, 255);
        Box(cx - 0.009f, cy + 0.015f, 0.018f, 0.013f, 90, 95, 112, 255);

        // tail light
        Box(cx - 0.009f, cy + carH / 2f - 0.003f, 0.018f, 0.003f,
            225, 45, 55, 255);
    }

    void DrawVehicle(Ped p, Vehicle v, int now)
    {
        VehicleClass vc = v.ClassType;
        if (vc == VehicleClass.Boats || vc == VehicleClass.Helicopters
            || vc == VehicleClass.Planes || vc == VehicleClass.Trains)
            return;

        float kmh = v.Speed * 3.6f;
        int gear = v.CurrentGear;
        int high = v.HighGear;
        float rpm = Clamp(v.CurrentRPM, 0f, 1f);
        bool reverse = Vector3.Dot(v.Velocity, v.ForwardVector) < -0.5f;
        float throttle = Function.Call<float>(
            Hash.GET_CONTROL_NORMAL, 0, 71);
        float fuel = Clamp(v.FuelLevel / 65f, 0f, 1f);

        string gearText;
        if (reverse) gearText = "R";
        else if (gear <= 0) gearText = "N";
        else gearText = gear.ToString();

        float y0 = 0.800f, hh = 0.170f;
        int br = 45, bg = 165, bb = 255;     // blue accent

        // panels
        Box(0.335f, y0 + 0.012f, 0.110f, hh - 0.024f, 8, 10, 16, 238);
        Frame(0.335f, y0 + 0.012f, 0.110f, hh - 0.024f, 0.002f,
            br, bg, bb, 255);
        Box(0.555f, y0 + 0.012f, 0.110f, hh - 0.024f, 8, 10, 16, 238);
        Frame(0.555f, y0 + 0.012f, 0.110f, hh - 0.024f, 0.002f,
            br, bg, bb, 255);
        Box(0.445f, y0, 0.110f, hh, 12, 14, 22, 245);
        Frame(0.445f, y0, 0.110f, hh, 0.002f, br, bg, bb, 255);

        // blue accent strips between panels
        Box(0.439f, y0 + 0.006f, 0.006f, hh - 0.012f, br, bg, bb, 255);
        Box(0.555f, y0 + 0.006f, 0.006f, hh - 0.012f, br, bg, bb, 255);

        // ----- left: speed + fuel bar -----
        string spd = ((int)Math.Round(kmh)).ToString();
        Mid(spd, 0.392f, y0 + 0.050f, 4, 1.7f, br, bg, bb, 255);
        Seg("KM/H", 0.402f, y0 + 0.125f, 4, 0.40f, 220, 225, 235, 255);

        float barY = y0 + 0.040f;
        float barH = hh - 0.095f;
        float lbx = 0.343f;
        Box(lbx, barY, 0.007f, barH, 14, 40, 80, 255);
        Box(lbx, barY + barH * (1f - fuel), 0.007f, barH * fuel,
            br, bg, bb, 255);
        Seg("F", 0.3415f, barY - 0.026f, 4, 0.30f, 220, 225, 235, 255);

        // ----- right: gear + rpm bar -----
        if (!reverse && gear > 1)
            Mid((gear - 1).ToString(), 0.585f, y0 + 0.052f, 4, 0.9f,
                150, 155, 165, 255);
        Mid(gearText, 0.610f, y0 + 0.042f, 4, 1.9f, 255, 255, 255, 255);
        if (!reverse && gear > 0 && gear < high)
            Mid((gear + 1).ToString(), 0.636f, y0 + 0.052f, 4, 0.9f,
                150, 155, 165, 255);
        Seg("GEAR", 0.565f, y0 + 0.125f, 4, 0.40f, 220, 225, 235, 255);

        float rbx = 0.652f;
        Box(rbx, barY, 0.007f, barH, 14, 40, 80, 255);
        if (rpm > 0.85f)
            Box(rbx, barY + barH * (1f - rpm), 0.007f, barH * rpm,
                255, 80, 70, 255);
        else
            Box(rbx, barY + barH * (1f - rpm), 0.007f, barH * rpm,
                br, bg, bb, 255);

        // light that runs from the bottom to the top while you accelerate
        if (throttle > 0.1f)
        {
            float t = (now % 700) / 700f;
            float bandH = barH * 0.18f;
            float yTop = barY + barH * (1f - t) - bandH / 2f;
            float yBot = yTop + bandH;
            if (yTop < barY) yTop = barY;
            if (yBot > barY + barH) yBot = barY + barH;
            if (yBot > yTop)
                Box(rbx - 0.001f, yTop, 0.009f, yBot - yTop,
                    200, 238, 255, 210);
        }
        Seg("R", 0.6525f, barY - 0.026f, 4, 0.30f, 220, 225, 235, 255);

        // ----- center: place + car -----
        if (now >= nextZone)
        {
            nextZone = now + 2000;
            Vector3 pos = v.Position;
            string zone = Function.Call<string>(
                Hash.GET_NAME_OF_ZONE, pos.X, pos.Y, pos.Z);
            string nm = Function.Call<string>(
                Hash.GET_FILENAME_FOR_AUDIO_CONVERSATION, zone);
            zoneName = (nm == null || nm == "NULL" || nm == "")
                ? zone : nm;
        }
        Mid(zoneName, 0.5f, y0 + 0.008f, 4, 0.36f, 235, 240, 250, 255);

        CarIcon(v, 0.5f, y0 + 0.095f);
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

        int now = Game.GameTime;
        float dt = (now - lastTick) / 1000f;
        if (dt < 0f || dt > 1f) dt = 0f;
        lastTick = now;

        UpdateStats(p, dt, now);
        DrawStatus(p);

        if (ShowVehicleHud && p.IsInVehicle())
        {
            Vehicle v = p.CurrentVehicle;
            if (v != null && v.Exists()) DrawVehicle(p, v, now);
        }
    }
}
