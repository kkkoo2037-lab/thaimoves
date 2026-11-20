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
    static void Ring(float cx, float cy, float rad, float dot, float frac,
        int r, int g, int b)
    {
        const int N = 44;
        int on = (int)Math.Round(frac * N);
        for (int i = 0; i < N; i++)
        {
            double a = i * 2.0 * Math.PI / N;
            float x = cx + rad * (float)Math.Sin(a);
            float y = cy - rad * (float)Math.Cos(a);
            if (i < on) PR(x - dot / 2f, y - dot / 2f, dot, dot, r, g, b, 255);
            else PR(x - dot * 0.42f, y - dot * 0.42f, dot * 0.84f, dot * 0.84f,
                40, 58, 48, 200);
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
    const float FS = 0.72f;          // size of the foot HUD

    void DrawFoot(Ped p)
    {
        float pw = 330f * FS, ph = 70f * FS;
        float px = 18f, py = 698f - ph;

        float hp = (p.Health - 100f) / (float)(p.MaxHealth - 100);
        if (hp < 0f) hp = 0f; if (hp > 1f) hp = 1f;
        float st = Needs.Stamina / 100f;

        Frame("panel.png", px, py, pw, ph, 4f * FS, 6f * FS, 12, 18, 15);

        // health bar
        float bx = px + 14f * FS, bw = 302f * FS;
        float hy = py + 12f * FS, hh = 26f * FS;
        PR(bx, hy, bw, hh, 24, 34, 28, 255);
        int hr = hp < 0.25f ? 230 : 40, hg = hp < 0.25f ? 60 : 205, hb = hp < 0.25f ? 60 : 70;
        PR(bx, hy, bw * hp, hh, hr, hg, hb, 255);
        PR(bx, hy, bw * hp, hh * 0.3f, Math.Min(255, hr + 50),
            Math.Min(255, hg + 40), Math.Min(255, hb + 40), 255);
        PT((int)Math.Round(hp * 100f) + "%", bx + bw - 4f, hy - 1f, 0.30f,
            255, 255, 255, 2);

        // stamina bar
        float sy = py + 46f * FS, sh = 12f * FS;
        PR(bx, sy, bw, sh, 24, 34, 28, 255);
        PR(bx, sy, bw * st, sh, sprintLocked ? 230 : 150,
            sprintLocked ? 120 : 235, sprintLocked ? 40 : 60, 255);
        PT((int)Math.Round(st * 100f) + "%", bx + bw - 4f, sy - 3f, 0.20f,
            255, 255, 255, 2);

        Spr("ic_heart.png", bx + 4f, hy + (hh - 12f) / 2f, 12f, 12f);
        Spr("ic_bolt.png", bx + 3f, sy + (sh - 11f) / 2f, 11f, 11f);

        // food ring + stress ring
        float cy = py + ph / 2f;
        float rad = 27f * FS, dot = 4.8f * FS, disc = 40f * FS;
        float c1 = px + pw + 26f, c2 = c1 + 42f;

        float food = Needs.Food / 100f;
        if (food < 0.25f) Ring(c1, cy, rad, dot, food, 235, 70, 60);
        else Ring(c1, cy, rad, dot, food, 60, 235, 90);
        Spr("gauge_food.png", c1 - disc / 2f, cy - disc / 2f, disc, disc);

        float stress = Needs.Stress / 100f;
        if (stress > 0.7f) Ring(c2, cy, rad, dot, stress, 235, 70, 60);
        else if (stress > 0.4f) Ring(c2, cy, rad, dot, stress, 240, 200, 50);
        else Ring(c2, cy, rad, dot, stress, 60, 235, 90);
        Spr("gauge_mind.png", c2 - disc / 2f, cy - disc / 2f, disc, disc);
    }

    // ---------- in a vehicle (bottom centre) ----------
    const float VS = 0.74f;          // size of the vehicle HUD

    // light chevron next to the car: dark, lit from the bottom to the
    // top by 'lit' (0..1) when you press the accelerator
    void Chevron(float x, float y, bool flip, float lit)
    {
        const float W = 50f, H = 118f;
        const int N = 40;
        float sh = H / N;
        for (int i = 0; i < N; i++)
        {
            float ym = (i + 0.5f) * sh;
            float xl, xr;
            if (ym <= 59f) { xl = 30f * ym / 59f; xr = 16f + 32f * ym / 59f; }
            else
            {
                float k = H - ym;
                xl = 30f * k / 59f; xr = 16f + 32f * k / 59f;
            }
            float t = 1f - ym / H;                        // 0 bottom .. 1 top
            float a = (lit - t) * 14f + 0.5f;             // soft lit edge
            if (a < 0f) a = 0f; if (a > 1f) a = 1f;

            float gx = flip ? W - xr : xl;
            float px = x + gx * VS, pw = (xr - xl) * VS;
            float py = y + i * sh * VS, ph = sh * VS + 0.7f;

            PR(px, py, pw, ph, 20, 62, 36, 255);          // dark base
            if (a > 0f)
            {
                PR(px - 1.5f, py, pw + 3f, ph, 70, 255, 110, (int)(60 * a));
                bool head = lit > 0.02f && Math.Abs(t - lit) < 0.06f;
                int r = head ? 215 : 60 + (int)(100 * t);
                int g = head ? 255 : 190 + (int)(65 * t);
                int b = head ? 225 : 100;
                PR(px, py, pw, ph, r, g, b, (int)(255 * a));
            }
        }
    }

    // fuel: one smooth slanted tube, filled from the bottom
    void Tube(float x, float yBottom, float frac)
    {
        const int N = 40;
        float H = 70f * VS, wd = 8f * VS, slant = 14f * VS;
        float sh = H / N;
        bool low = frac < 0.15f;
        for (int i = 0; i < N; i++)
        {
            float y = yBottom - (i + 1) * sh;
            float x0 = x + slant * (i / (float)N);
            PR(x0 - 1f, y, wd + 2f, sh + 0.6f, 60, 95, 72, 255);   // rim
            PR(x0, y, wd, sh + 0.6f, 18, 28, 22, 255);            // empty
        }
        float lit = frac * N;
        for (int i = 0; i < N && i < lit + 1f; i++)
        {
            float a = lit - i; if (a < 0f) a = 0f; if (a > 1f) a = 1f;
            float t = i / (float)N;
            float y = yBottom - (i + 1) * sh;
            float x0 = x + slant * t;
            int r = low ? 235 : 40 + (int)(130 * t);
            int g = low ? 70 : 200 + (int)(55 * t);
            int b = low ? 60 : 75;
            PR(x0, y, wd, sh + 0.6f, r, g, b, (int)(255 * a));
        }
    }

    // gear guessed from the speed (the game's own gear is not reliable)
    Dictionary<int, float> topSpeed = new Dictionary<int, float>();
    static readonly float[] GearAt = { 0.10f, 0.22f, 0.36f, 0.52f, 0.72f };
    int shownGear = 1;
    int gearVeh;

    int EstimateGear(Vehicle v)
    {
        int key = v.Model.Hash;
        float sp = v.Speed;
        float top;
        if (!topSpeed.TryGetValue(key, out top)) top = 45f;   // ~160 km/h
        if (sp > top) top = sp;
        topSpeed[key] = top;

        if (gearVeh != v.Handle) { gearVeh = v.Handle; shownGear = 1; }
        float ratio = sp / top;
        if (sp < 1f) { shownGear = 1; return shownGear; }

        // go up / down with a small margin so it does not flicker
        while (shownGear < 6 && ratio > GearAt[shownGear - 1] + 0.02f)
            shownGear++;
        while (shownGear > 1 && ratio < GearAt[shownGear - 2] - 0.02f)
            shownGear--;
        return shownGear;
    }

    void DrawVehicle(Ped p, Vehicle v, float dt)
    {
        if (v == null || !v.Exists()) return;

        // throttle (smoothed so the light runs up / falls back)
        float target = Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, 71);
        if (target < 0f) target = 0f; if (target > 1f) target = 1f;
        float k = target > throttle ? 5f : 3.5f;
        throttle += (target - throttle) * Math.Min(1f, k * dt);

        float pw = 250f * VS, ph = 105f * VS;
        float y0 = 698f - ph;
        float carW = 54f * VS, carH = 100f * VS;
        float carX = 640f - carW / 2f;
        float chW = 50f * VS, chH = 118f * VS;
        float chLx = carX - 4f - chW;
        float chRx = carX + carW + 4f;
        float lx = chLx - 3f - pw;
        float rx = chRx + chW + 3f;
        float chY = y0 + ph / 2f - chH / 2f;

        // light chevrons (draw first, panels sit next to them)
        Chevron(chLx, chY, false, throttle);
        Chevron(chRx, chY, true, throttle);

        Frame("veh_left.png", lx, y0, pw, ph, 4f * VS, 8f * VS, 12, 18, 15);
        Frame("veh_right.png", rx, y0, pw, ph, 4f * VS, 8f * VS, 12, 18, 15);

        // ---- speed ----
        int kmh = (int)Math.Round(v.Speed * 3.6f);
        PT(kmh.ToString(), lx + 150f * VS, y0 + 12f * VS, 0.88f, 70, 235, 100, 1);
        PT("KM/H", lx + 236f * VS, y0 + 76f * VS, 0.34f, 235, 245, 238, 2);

        // fuel (left edge)
        Tube(lx + 14f * VS, y0 + 92f * VS, FuelSys.Frac(v));

        // ---- gear ----
        int gear = EstimateGear(v);
        string cur;
        bool reversing = Vector3.Dot(v.Velocity, v.ForwardVector) < -0.5f;
        if (reversing) cur = "R";
        else if (gear <= 0 && kmh < 2) cur = "N";
        else cur = Math.Max(1, gear).ToString();
        PT(cur, rx + 125f * VS, y0 + 12f * VS, 0.92f, 255, 255, 255, 1);
        if (!reversing && gear >= 1)
        {
            if (gear > 1)
                PT((gear - 1).ToString(), rx + 80f * VS, y0 + 28f * VS, 0.52f,
                    120, 140, 125, 1);
            if (gear < 6)
                PT((gear + 1).ToString(), rx + 170f * VS, y0 + 28f * VS, 0.52f,
                    120, 140, 125, 1);
        }
        PT("GEAR", rx + 14f * VS, y0 + 76f * VS, 0.34f, 235, 245, 238, 0);

        // ---- centre: car + tyres ----
        int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, v);
        if (cls != 14 && cls != 15 && cls != 16)    // not boat / heli / plane
        {
            float cy = y0 + (ph - carH) / 2f;
            Spr("car.png", carX, cy, carW, carH);

            // tyre index: 0 front left, 1 front right, 4 rear left, 5 rear right
            int[] idx = { 0, 1, 4, 5 };
            float[] wx = { 1f, 45f, 1f, 45f };
            float[] wy = { 14f, 14f, 64f, 64f };
            for (int i = 0; i < 4; i++)
            {
                if (Function.Call<bool>(Hash.IS_VEHICLE_TYRE_BURST,
                    v, idx[i], false))
                    Spr("tire_red.png", carX + (wx[i] - 3f) * VS,
                        cy + (wy[i] - 3f) * VS, 14f * VS, 28f * VS);
            }
        }
    }
}
