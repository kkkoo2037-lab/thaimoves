using System;
using System.Collections.Generic;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;

// ===========================================================
//  Hud.cs  -  green HUD
//   On foot : health bar, stamina bar, food ring, stress ring
//   In car  : speed, gear, fuel, throttle lights, tyre warning
//  Pictures: scripts\PinkUI\hud\*.png  (text is drawn by the game)
// ===========================================================

// shared values (ItemSystem changes these when you eat / drink)
public static class Needs
{
    public static float Food = 100f;      // 0..100
    public static float Stress = 0f;      // 0..100 (high = bad)
    public static float Stamina = 100f;   // 0..100
    public static void AddStamina(float v) { Stamina = Math.Min(100f, Stamina + v); }
    public static void AddFood(float v) { Food = Math.Min(100f, Food + v); }
    public static void AddStress(float v) { Stress = Math.Max(0f, Math.Min(100f, Stress + v)); }
}

public class Hud : Script
{
    // ===== settings =====
    const float FOOD_LOSS_PER_SEC = 100f / (30f * 60f);  // empty in 30 min
    const float STAMINA_DRAIN = 100f / 16f;              // per sec sprinting
    const float STAMINA_REGEN_IDLE = 9f;
    const float STAMINA_REGEN_MOVE = 4f;
    const float SPRINT_UNLOCK_AT = 20f;                  // % to sprint again
    const float TANK_MAX = 65f;                          // GTA fuel scale
    const string HUDDIR = "scripts\\PinkUI\\hud\\";
    const string SAVEFILE = "scripts\\needs.txt";

    // ===== draw space 1280x720 =====
    const float DW = 1280f, DH = 720f;
    static float SW { get { return GTA.UI.Screen.ScaledWidth; } }
    static float NX(float x) { return ((SW - DW) / 2f + x) / SW; }

    Dictionary<string, GTA.UI.CustomSprite> sprs =
        new Dictionary<string, GTA.UI.CustomSprite>();
    bool sprOff;

    float throttle;          // smoothed
    float lastHealth = -1f;
    float starveTimer;
    bool sprintLocked;
    bool wasDead;
    bool shaking;
    int nextSave;

    public Hud()
    {
        LoadNeeds();
        Tick += OnTick;
    }

    // ---------- save / load ----------
    void LoadNeeds()
    {
        try
        {
            if (!File.Exists(SAVEFILE)) return;
            string[] a = File.ReadAllText(SAVEFILE).Split('|');
            Needs.Food = float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture);
            Needs.Stress = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception) { }
    }

    void SaveNeeds()
    {
        try
        {
            File.WriteAllText(SAVEFILE,
                Needs.Food.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "|" +
                Needs.Stress.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception) { }
    }

    // ---------- draw helpers ----------
    static void PR(float x, float y, float w, float h,
        int r, int g, int b, int a)
    {
        Function.Call(Hash.DRAW_RECT, NX(x + w / 2f), (y + h / 2f) / DH,
            w / SW, h / DH, r, g, b, a);
    }

    // align: 0 left, 1 center, 2 right
    static void PT(string s, float x, float y, float scale,
        int r, int g, int b, int align)
    {
        Function.Call(Hash.SET_TEXT_FONT, 4);
        Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, 255);
        Function.Call(Hash.SET_TEXT_DROP_SHADOW);
        float nx = NX(x);
        if (align == 1) Function.Call(Hash.SET_TEXT_CENTRE, true);
        else if (align == 2)
        {
            Function.Call(Hash.SET_TEXT_JUSTIFICATION, 2);
            Function.Call(Hash.SET_TEXT_WRAP, 0f, nx);
            nx = 0f;
        }
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, nx, y / DH, 0);
    }

    // PNG (always drawn on top of rects and text)
    bool Spr(string name, float x, float y, float w, float h)
    {
        if (sprOff) return false;
        try
        {
            string path = HUDDIR + name;
            GTA.UI.CustomSprite s;
            if (!sprs.TryGetValue(path, out s))
            {
                s = File.Exists(path)
                    ? new GTA.UI.CustomSprite(path,
                        new System.Drawing.SizeF(w, h),
                        new System.Drawing.PointF(0f, 0f))
                    : null;
                sprs[path] = s;
            }
            if (s == null) return false;
            s.Size = new System.Drawing.SizeF(w * DW / SW, h);
            s.Position = new System.Drawing.PointF(NX(x) * DW, y);
            s.Draw();
            return true;
        }
        catch (Exception) { sprOff = true; return false; }
    }

    // frame with native fill underneath (see PNG: band + transparent hole)
    void Frame(string spr, float x, float y, float w, float h,
        float band, float m, int r, int g, int b)
    {
        PR(x + band, y + band, w - 2f * band, h - 2f * band, r, g, b, 235);
        if (!Spr(spr, x - m, y - m, w + 2f * m, h + 2f * m))
            PR(x, y, w, h, r, g, b, 235);
    }

    // ring of dots, 0 = top, clockwise
    static void Ring(float cx, float cy, float rad, float frac,
        int r, int g, int b)
    {
        const int N = 44;
        int on = (int)Math.Round(frac * N);
        for (int i = 0; i < N; i++)
        {
            double a = i * 2.0 * Math.PI / N;
            float x = cx + rad * (float)Math.Sin(a);
            float y = cy - rad * (float)Math.Cos(a);
            if (i < on) PR(x - 2.4f, y - 2.4f, 4.8f, 4.8f, r, g, b, 255);
            else PR(x - 2f, y - 2f, 4f, 4f, 40, 58, 48, 200);
        }
    }

    // ===========================================================
    void OnTick(object sender, EventArgs e)
    {
        Ped p = Game.Player.Character;
        if (p == null || !p.Exists()) return;

        float dt = Game.LastFrameTime;
        if (dt <= 0f || dt > 0.25f) dt = 0.033f;
        int now = Game.GameTime;

        bool dead = p.IsDead || DeathScreen.Downed;
        if (dead) { wasDead = true; return; }
        if (wasDead)               // woke up after dying
        {
            wasDead = false;
            Needs.Food = Math.Max(Needs.Food, 60f);
            Needs.Stress = 0f;
            Needs.Stamina = 100f;
            lastHealth = -1f;
        }

        UpdateNeeds(p, dt);
        if (now > nextSave) { nextSave = now + 5000; SaveNeeds(); }

        DrawFoot(p);
        if (p.IsInVehicle()) DrawVehicle(p, p.CurrentVehicle, dt);
    }

    // ---------- gameplay ----------
    void UpdateNeeds(Ped p, float dt)
    {
        // food
        Needs.Food = Math.Max(0f, Needs.Food - FOOD_LOSS_PER_SEC * dt);
        if (Needs.Food <= 0f)
        {
            starveTimer += dt;
            if (starveTimer >= 3f)
            {
                starveTimer = 0f;
                if (p.Health > 105) p.Health = p.Health - 2;
            }
        }
        else starveTimer = 0f;

        // stamina
        bool inVeh = p.IsInVehicle();
        if (!inVeh && p.IsSprinting && !sprintLocked)
            Needs.Stamina = Math.Max(0f, Needs.Stamina - STAMINA_DRAIN * dt);
        else if (inVeh || p.Velocity.Length() < 0.5f)
            Needs.Stamina = Math.Min(100f, Needs.Stamina + STAMINA_REGEN_IDLE * dt);
        else
            Needs.Stamina = Math.Min(100f, Needs.Stamina + STAMINA_REGEN_MOVE * dt);

        if (Needs.Stamina <= 0.5f) sprintLocked = true;
        else if (sprintLocked && Needs.Stamina >= SPRINT_UNLOCK_AT)
            sprintLocked = false;
        Function.Call(Hash.SET_PLAYER_SPRINT, Game.Player.Handle,
            !sprintLocked);

        // stress
        if (lastHealth >= 0f && p.Health < lastHealth)
            Needs.AddStress((lastHealth - p.Health) * 0.20f);
        lastHealth = p.Health;
        if (p.IsShooting) Needs.AddStress(6f * dt);
        if (Game.Player.WantedLevel > 0)
            Needs.AddStress(0.8f * Game.Player.WantedLevel * dt);
        if (inVeh && p.CurrentVehicle.Speed > 45f)   // > 160 km/h
            Needs.AddStress(1.5f * dt);
        bool still = !inVeh && p.Velocity.Length() < 0.5f;
        Needs.AddStress(-(still ? 1.6f : 0.6f) * dt);

        // very stressed: the camera shakes
        if (Needs.Stress >= 90f && !shaking)
        {
            shaking = true;
            Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "SMALL_EXPLOSION_SHAKE", 0.03f);
        }
        else if (Needs.Stress < 80f && shaking)
        {
            shaking = false;
            Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true);
        }
    }

    // ---------- on foot (bottom left) ----------
    void DrawFoot(Ped p)
    {
        float px = 24f, py = 632f;

        float hp = (p.Health - 100f) / (float)(p.MaxHealth - 100);
        if (hp < 0f) hp = 0f; if (hp > 1f) hp = 1f;
        float st = Needs.Stamina / 100f;

        Frame("panel.png", px, py, 330f, 70f, 4f, 6f, 12, 18, 15);

        // health bar
        float bx = px + 14f, bw = 302f;
        PR(bx, py + 12f, bw, 26f, 24, 34, 28, 255);
        int hr = hp < 0.25f ? 230 : 40, hg = hp < 0.25f ? 60 : 205, hb = hp < 0.25f ? 60 : 70;
        PR(bx, py + 12f, bw * hp, 26f, hr, hg, hb, 255);
        PR(bx, py + 12f, bw * hp, 8f, Math.Min(255, hr + 50), Math.Min(255, hg + 40), Math.Min(255, hb + 40), 255);
        PT((int)Math.Round(hp * 100f) + "%", bx + bw - 6f, py + 12f, 0.4f,
            255, 255, 255, 2);

        // stamina bar
        PR(bx, py + 46f, bw, 12f, 24, 34, 28, 255);
        PR(bx, py + 46f, bw * st, 12f, sprintLocked ? 230 : 150,
            sprintLocked ? 120 : 235, sprintLocked ? 40 : 60, 255);
        PT((int)Math.Round(st * 100f) + "%", bx + bw - 6f, py + 44f, 0.27f,
            255, 255, 255, 2);

        Spr("ic_heart.png", bx + 6f, py + 17f, 16f, 16f);
        Spr("ic_bolt.png", bx + 4f, py + 44f, 16f, 16f);

        // food ring + stress ring
        float cy = py + 35f;
        float c1 = px + 330f + 36f, c2 = c1 + 58f;

        float food = Needs.Food / 100f;
        if (food < 0.25f) Ring(c1, cy, 27f, food, 235, 70, 60);
        else Ring(c1, cy, 27f, food, 60, 235, 90);
        Spr("gauge_food.png", c1 - 20f, cy - 20f, 40f, 40f);

        float stress = Needs.Stress / 100f;
        if (stress > 0.7f) Ring(c2, cy, 27f, stress, 235, 70, 60);
        else if (stress > 0.4f) Ring(c2, cy, 27f, stress, 240, 200, 50);
        else Ring(c2, cy, 27f, stress, 60, 235, 90);
        Spr("gauge_mind.png", c2 - 20f, cy - 20f, 40f, 40f);
    }

    // ---------- in a vehicle (bottom centre) ----------
    // 10 slanted segments, lit from the bottom to the top
    void Segments(float x, float yBottom, float frac, bool leanRight,
        float softEdge)
    {
        for (int i = 0; i < 10; i++)
        {
            float y = yBottom - (i + 1) * 9f;
            float x0 = x + (leanRight ? i * 1.6f : -i * 1.6f);
            float lo = i / 10f, hi = (i + 1) / 10f;
            float t = (frac - lo) / (hi - lo);       // 0..1 inside segment
            if (t < 0f) t = 0f; if (t > 1f) t = 1f;
            PR(x0, y, 10f, 7f, 30, 46, 38, 230);     // empty
            if (t > 0f)
            {
                int g = 200 + (int)(55 * (i / 9f));
                int r = 40 + (int)(130 * (i / 9f));
                PR(x0, y, 10f, 7f, r, g, 70, (int)(255 * t));
            }
        }
    }

    void DrawVehicle(Ped p, Vehicle v, float dt)
    {
        if (v == null || !v.Exists()) return;

        // throttle (smoothed so the lights run up / fall back)
        float target = Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, 71);
        if (target < 0f) target = 0f; if (target > 1f) target = 1f;
        float k = target > throttle ? 5f : 3.5f;
        throttle += (target - throttle) * Math.Min(1f, k * dt);

        float x0 = 300f, y0 = 590f;
        float lx = x0, rx = 730f;

        // panels + chevrons
        Frame("veh_left.png", lx, y0, 250f, 105f, 4f, 8f, 12, 18, 15);
        Frame("veh_right.png", rx, y0, 250f, 105f, 4f, 8f, 12, 18, 15);
        Spr("chev_l.png", 553f, y0 - 6f, 50f, 118f);
        Spr("chev_r.png", 677f, y0 - 6f, 50f, 118f);

        // ---- speed ----
        int kmh = (int)Math.Round(v.Speed * 3.6f);
        PT(kmh.ToString(), lx + 150f, y0 + 14f, 1.15f, 70, 235, 100, 1);
        PT("KM/H", lx + 236f, y0 + 76f, 0.45f, 235, 245, 238, 2);

        // fuel (left edge)
        float fuel = v.FuelLevel / TANK_MAX;
        if (fuel < 0f) fuel = 0f; if (fuel > 1f) fuel = 1f;
        Segments(lx + 16f, y0 + 96f, fuel, true, 0f);
        PT("F", lx + 14f, y0 + 4f, 0.3f, 190, 220, 195, 0);

        // ---- gear ----
        int gear = v.CurrentGear;
        string cur;
        Vector3 vel = v.Velocity;
        bool reversing = Vector3.Dot(vel, v.ForwardVector) < -0.5f;
        if (reversing) cur = "R";
        else if (gear <= 0 && kmh < 2) cur = "N";
        else cur = Math.Max(1, gear).ToString();
        PT(cur, rx + 125f, y0 + 14f, 1.2f, 255, 255, 255, 1);
        if (!reversing && gear >= 1)
        {
            if (gear > 1)
                PT((gear - 1).ToString(), rx + 82f, y0 + 30f, 0.7f, 120, 140, 125, 1);
            if (gear < v.HighGear)
                PT((gear + 1).ToString(), rx + 168f, y0 + 30f, 0.7f, 120, 140, 125, 1);
        }
        PT("GEAR", rx + 14f, y0 + 76f, 0.45f, 235, 245, 238, 0);

        // throttle lights (right edge)
        Segments(rx + 250f - 30f, y0 + 96f, throttle, false, 0f);

        // ---- centre: car + tyres ----
        int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, v);
        if (cls != 14 && cls != 15 && cls != 16)    // not boat / heli / plane
        {
            float cx = 613f, cy = y0 + 3f;
            Spr("car.png", cx, cy, 54f, 100f);

            // tyre index: 0 front left, 1 front right, 4 rear left, 5 rear right
            int[] idx = { 0, 1, 4, 5 };
            float[] wx = { 1f, 45f, 1f, 45f };
            float[] wy = { 14f, 14f, 64f, 64f };
            for (int i = 0; i < 4; i++)
            {
                bool burst = Function.Call<bool>(Hash.IS_VEHICLE_TYRE_BURST,
                    v, idx[i], false);
                if (burst)
                    Spr("tire_red.png", cx + wx[i] - 3f, cy + wy[i] - 3f,
                        14f, 28f);
            }
        }
    }
}
