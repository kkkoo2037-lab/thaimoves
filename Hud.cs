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
    bool reported;
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

    // look for a picture in a few places (first hit is remembered)
    static readonly string[] Dirs = {
        "scripts\\PinkUI\\hud\\", "scripts/PinkUI/hud/",
        "scripts\\PinkUI\\", "scripts/PinkUI/",
        "scripts\\hud\\", "scripts/hud/"
    };
    Dictionary<string, string> found = new Dictionary<string, string>();
    List<string> missing = new List<string>();

    string Find(string name)
    {
        string p;
        if (found.TryGetValue(name, out p)) return p;
        foreach (string d in Dirs)
        {
            try
            {
                if (File.Exists(d + name)) { found[name] = d + name; return d + name; }
            }
            catch (Exception) { }
        }
        found[name] = null;
        missing.Add(name);
        return null;
    }

    // PNG (always drawn on top of rects and text)
    bool Spr(string name, float x, float y, float w, float h)
    {
        if (sprOff) return false;
        try
        {
            string path = Find(name);
            if (path == null) return false;
            GTA.UI.CustomSprite s;
            if (!sprs.TryGetValue(path, out s))
            {
                s = new GTA.UI.CustomSprite(path,
                    new System.Drawing.SizeF(w, h),
                    new System.Drawing.PointF(0f, 0f));
                sprs[path] = s;
            }
            s.Size = new System.Drawing.SizeF(w * DW / SW, h);
            s.Position = new System.Drawing.PointF(NX(x) * DW, y);
            s.Draw();
            return true;
        }
        catch (Exception ex)
        {
            sprOff = true;
            WriteLog("Sprite error: " + ex.Message);
            return false;
        }
    }

    void WriteLog(string extra)
    {
        try
        {
            string t = "cwd: " + Directory.GetCurrentDirectory() + "\n"
                + (extra ?? "") + "\n"
                + "missing pictures (" + missing.Count + "):\n"
                + string.Join("\n", missing.ToArray()) + "\n";
            File.WriteAllText("scripts\\hud_log.txt", t);
        }
        catch (Exception) { }
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

        if (!reported && now > 4000)
        {
            reported = true;
            Find("st_panel.png"); Find("v_left.png"); Find("car.png");
            WriteLog(sprOff ? "sprites switched off" : "");
            if (missing.Count > 0)
                GTA.UI.Screen.ShowSubtitle(
                    "HUD pictures not found. Put the PinkUI folder in scripts (see scripts\\hud_log.txt)",
                    6000);
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

    // ===========================================================
    //  Pictures: scripts/PinkUI/hud/*.png
    //  All numbers below are design pixels (1280x720).
    // ===========================================================

    // ---------- shared drawing ----------
    // panel PNGs have transparent windows (holes). The dark fill of the
    // windows is drawn here so text / bars can show through them.
    void Panel(string spr, float x, float y, float w, float h, float m,
        float[][] holes)
    {
        foreach (float[] hl in holes)
            PR(x + hl[0], y + hl[1], hl[2], hl[3], 8, 12, 10, 255);
        if (!Spr(spr, x - m, y - m, w + 2f * m, h + 2f * m))
        {
            PR(x - 1.5f, y - 1.5f, w + 3f, h + 3f, 50, 230, 70, 255);
            PR(x, y, w, h, 8, 12, 10, 255);          // no PNG: plain box
        }
    }

    // smooth horizontal bar (dark track + gradient fill + shine)
    static void HBar(float x, float y, float w, float h, float frac,
        int r1, int g1, int b1, int r2, int g2, int b2)
    {
        if (frac < 0f) frac = 0f; if (frac > 1f) frac = 1f;
        PR(x - 1f, y - 1f, w + 2f, h + 2f, 40, 70, 52, 255);   // rim
        PR(x, y, w, h, 14, 22, 18, 255);                       // track
        const int N = 28;
        float sw = w / N;
        for (int i = 0; i < N; i++)
        {
            float t = i / (float)(N - 1);
            float x0 = x + i * sw;
            float lit = frac * N - i;
            if (lit <= 0f) break;
            if (lit > 1f) lit = 1f;
            int r = (int)(r1 + (r2 - r1) * t);
            int g = (int)(g1 + (g2 - g1) * t);
            int b = (int)(b1 + (b2 - b1) * t);
            PR(x0, y, sw * lit + 0.6f, h, r, g, b, 255);
        }
        PR(x, y, w * frac, h * 0.32f, 255, 255, 255, 38);       // shine
    }

    // ---------- on foot (bottom left) ----------
    void DrawFoot(Ped p)
    {
        float px = 18f, py = 648f, pw = 250f, ph = 50f;

        float hp = (p.Health - 100f) / (float)(p.MaxHealth - 100);
        if (hp < 0f) hp = 0f; if (hp > 1f) hp = 1f;
        float ar = p.Armor / 100f;
        if (ar < 0f) ar = 0f; if (ar > 1f) ar = 1f;
        float st = Needs.Stamina / 100f;

        Panel("st_panel.png", px, py, pw, ph, 6f,
            new float[][] { new float[] { 38f, 6f, 202f, 38f } });

        float bx = px + 42f, bw = 194f;

        // health
        if (hp < 0.25f) HBar(bx, py + 8f, bw, 14f, hp, 200, 40, 40, 255, 90, 80);
        else HBar(bx, py + 8f, bw, 14f, hp, 30, 190, 50, 110, 255, 90);
        PT((int)Math.Round(hp * 100f) + "%", bx + bw - 5f, py + 6f, 0.27f,
            255, 255, 255, 2);

        // armor
        HBar(bx, py + 27f, bw, 14f, ar, 0, 190, 200, 40, 255, 235);
        PT((int)Math.Round(ar * 100f) + "%", bx + bw - 5f, py + 25f, 0.27f,
            255, 255, 255, 2);

        Spr("ic_heart.png", px + 19f, py + 8f, 14f, 14f);
        Spr("ic_shield.png", px + 19f, py + 27f, 14f, 14f);

        // rings: food, stamina (cup), stress (brain)
        float cy = py + ph / 2f;
        float rad = 18f, dot = 3.4f, disc = 30f;
        float c1 = px + pw + 24f, c2 = c1 + 42f, c3 = c2 + 42f;

        float food = Needs.Food / 100f;
        if (food < 0.25f) Ring(c1, cy, rad, dot, food, 235, 70, 60);
        else Ring(c1, cy, rad, dot, food, 60, 235, 90);
        Spr("gauge_food.png", c1 - disc / 2f, cy - disc / 2f, disc, disc);

        if (sprintLocked) Ring(c2, cy, rad, dot, st, 240, 140, 40);
        else Ring(c2, cy, rad, dot, st, 150, 240, 60);
        Spr("gauge_cup.png", c2 - disc / 2f, cy - disc / 2f, disc, disc);

        float stress = Needs.Stress / 100f;
        if (stress > 0.7f) Ring(c3, cy, rad, dot, stress, 235, 70, 60);
        else if (stress > 0.4f) Ring(c3, cy, rad, dot, stress, 240, 200, 50);
        else Ring(c3, cy, rad, dot, stress, 60, 235, 90);
        Spr("gauge_mind.png", c3 - disc / 2f, cy - disc / 2f, disc, disc);
    }

    // ---------- in a vehicle (bottom centre) ----------
    // chevron made of stripes, dark until the accelerator lights it up
    // from the bottom to the top ('lit' 0..1). k = size (1 = 50x118)
    void Chevron(float x, float y, float k, bool flip, float lit)
    {
        const float W = 50f, H = 118f;
        const int N = 36;
        float sh = H / N;
        for (int i = 0; i < N; i++)
        {
            float ym = (i + 0.5f) * sh;
            float xl, xr;
            if (ym <= 59f) { xl = 30f * ym / 59f; xr = 16f + 32f * ym / 59f; }
            else
            {
                float kk = H - ym;
                xl = 30f * kk / 59f; xr = 16f + 32f * kk / 59f;
            }
            float t = 1f - ym / H;                        // 0 bottom .. 1 top
            float a = (lit - t) * 14f + 0.5f;
            if (a < 0f) a = 0f; if (a > 1f) a = 1f;

            float gx = flip ? W - xr : xl;
            float px = x + gx * k, pw = (xr - xl) * k;
            float py = y + i * sh * k, ph = sh * k + 0.7f;

            PR(px, py, pw, ph, 18, 56, 32, 255);          // dark base
            if (a > 0f)
            {
                PR(px - 1.2f, py, pw + 2.4f, ph, 70, 255, 110, (int)(60 * a));
                bool head = lit > 0.02f && Math.Abs(t - lit) < 0.06f;
                int r = head ? 215 : 60 + (int)(100 * t);
                int g = head ? 255 : 190 + (int)(65 * t);
                int b = head ? 225 : 100;
                PR(px, py, pw, ph, r, g, b, (int)(255 * a));
            }
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

        // layout: left panel | centre hex | right panel
        float x0 = 401f;
        float lw = 190f, lh = 72f, cw = 110f, ch = 88f;
        float ly = 698f - lh, cyy = 698f - ch;
        float lx = x0, cx = x0 + 184f, rx = cx + cw - 6f;

        // ---------- left: speed + fuel ----------
        Panel("v_left.png", lx, ly, lw, lh, 6f,
            new float[][] { new float[] { 52f, 8f, 124f, 56f } });
        int kmh = (int)Math.Round(v.Speed * 3.6f);
        PT(kmh.ToString(), lx + 114f, ly + 7f, 0.85f, 70, 235, 100, 1);
        PT("KM/H", lx + 172f, ly + 36f, 0.26f, 235, 245, 238, 2);
        float fuel = FuelSys.Frac(v);
        if (fuel < 0.15f) HBar(lx + 72f, ly + 50f, 96f, 6f, fuel, 200, 40, 40, 255, 90, 80);
        else HBar(lx + 72f, ly + 50f, 96f, 6f, fuel, 30, 190, 50, 110, 255, 90);
        Spr("ic_bolt.png", lx + 14f, ly + 12f, 14f, 14f);
        Spr("ic_pump.png", lx + 56f, ly + 47f, 12f, 12f);

        // ---------- right: gear + throttle ----------
        Panel("v_right.png", rx, ly, lw, lh, 6f,
            new float[][] { new float[] { 14f, 8f, 124f, 56f } });
        int gear = EstimateGear(v);
        string cur;
        bool reversing = Vector3.Dot(v.Velocity, v.ForwardVector) < -0.5f;
        if (reversing) cur = "R";
        else if (gear <= 1 && kmh < 2) cur = "N";
        else cur = gear.ToString();
        PT(cur, rx + 76f, ly + 4f, 1.0f, 255, 255, 255, 1);
        if (!reversing && kmh >= 2)
        {
            if (gear > 1)
                PT((gear - 1).ToString(), rx + 34f, ly + 22f, 0.5f, 120, 140, 125, 1);
            if (gear < 6)
                PT((gear + 1).ToString(), rx + 118f, ly + 22f, 0.5f, 120, 140, 125, 1);
        }
        HBar(rx + 22f, ly + 50f, 108f, 6f, throttle, 30, 160, 60, 150, 255, 100);
        Spr("ic_speedo.png", rx + lw - 34f, ly + 12f, 14f, 14f);

        // ---------- centre: chevrons + car ----------
        Panel("v_center.png", cx, cyy, cw, ch, 6f,
            new float[][] { new float[] { 14f, 16f, 82f, 60f },
                            new float[] { 33f, 2.5f, 44f, 9f } });
        PT("GEAR", cx + 55f, cyy + 1.5f, 0.2f, 235, 245, 238, 1);

        float kc = 0.5f;
        float chY = cyy + 46f - 118f * kc / 2f;
        Chevron(cx + 18f, chY, kc, true, throttle);          // <
        Chevron(cx + 67f, chY, kc, false, throttle);         // >

        int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, v);
        if (cls != 14 && cls != 15 && cls != 16)    // not boat / heli / plane
        {
            float carW = 26f, carH = 48f, sc = carW / 54f;
            float carX = cx + 55f - carW / 2f, carY = cyy + 22f;
            Spr("car.png", carX, carY, carW, carH);

            // tyre index: 0 front left, 1 front right, 4 rear left, 5 rear right
            int[] idx = { 0, 1, 4, 5 };
            float[] wx = { 1f, 45f, 1f, 45f };
            float[] wy = { 14f, 14f, 64f, 64f };
            for (int i = 0; i < 4; i++)
            {
                if (Function.Call<bool>(Hash.IS_VEHICLE_TYRE_BURST,
                    v, idx[i], false))
                    Spr("tire_red.png", carX + (wx[i] - 3f) * sc,
                        carY + (wy[i] - 3f) * sc, 14f * sc, 28f * sc);
            }
        }
    }
}
