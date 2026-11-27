using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

// ===========================================================
//  Fuel.cs  -  fuel tank, gas stations (map icons), refuel with E
//  Drive next to a pump (or inside a station), stop, press E.
//  Money is taken while the tank fills. Press E again to stop.
// ===========================================================
public class FuelSys : Script
{
    // ===== settings =====
    const float TANK = 65f;               // litres
    const float PRICE_PER_LITER = 3f;     // $ per litre
    const float FILL_RATE = 3f;           // litres per second
    const float USE_BASE = 0.008f;        // L/s engine running
    const float USE_PER_SPEED = 0.0007f;  // L/s per m/s
    const float USE_PER_THROTTLE = 0.012f;
    const int KeyRefuel = 0x45;           // E

    // ===== gas stations (map icons) =====
    static readonly Vector3[] Stations = {
        new Vector3(49.42f, 2778.79f, 58.04f),
        new Vector3(263.89f, 2606.46f, 44.98f),
        new Vector3(1039.96f, 2671.13f, 39.55f),
        new Vector3(1207.26f, 2660.18f, 37.90f),
        new Vector3(2539.69f, 2594.19f, 37.94f),
        new Vector3(2679.86f, 3263.95f, 55.24f),
        new Vector3(2005.06f, 3773.89f, 32.40f),
        new Vector3(1687.16f, 4929.39f, 42.08f),
        new Vector3(1701.31f, 6416.03f, 32.76f),
        new Vector3(179.86f, 6602.84f, 31.87f),
        new Vector3(-94.46f, 6419.59f, 31.49f),
        new Vector3(-2555.00f, 2334.40f, 33.08f),
        new Vector3(-1800.38f, 803.66f, 138.65f),
        new Vector3(-1437.62f, -276.75f, 46.21f),
        new Vector3(-2096.24f, -320.29f, 13.17f),
        new Vector3(-724.62f, -935.16f, 19.21f),
        new Vector3(-526.02f, -1211.00f, 18.18f),
        new Vector3(-70.21f, -1761.79f, 29.53f),
        new Vector3(265.65f, -1261.31f, 29.29f),
        new Vector3(819.65f, -1028.85f, 26.40f),
        new Vector3(1208.95f, -1402.57f, 35.22f),
        new Vector3(1181.38f, -330.85f, 69.32f),
        new Vector3(620.84f, 269.10f, 103.09f),
        new Vector3(2581.32f, 362.04f, 108.47f),
        new Vector3(176.63f, -1562.03f, 29.26f),
        new Vector3(-319.29f, -1471.72f, 30.55f),
        new Vector3(1784.32f, 3330.55f, 41.25f)
    };

    static readonly string[] PumpModels = {
        "prop_gas_pump_1a", "prop_gas_pump_1b", "prop_gas_pump_1c",
        "prop_gas_pump_1d", "prop_gas_pump_old2", "prop_gas_pump_old3",
        "prop_vintage_pump"
    };
    int[] pumpHash;

    // ===== state =====
    static Dictionary<int, float> tank = new Dictionary<int, float>();
    bool blipsMade;
    bool nearPump;
    int nextPumpCheck;
    bool fueling;
    float costAcc;
    int paid;
    bool prevKey;
    bool emptyShown;

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    public FuelSys()
    {
        pumpHash = new int[PumpModels.Length];
        for (int i = 0; i < PumpModels.Length; i++)
            pumpHash[i] = Game.GenerateHash(PumpModels[i]);
        Tick += OnTick;
    }

    // ---------- tank (own values, not the game's) ----------
    static float Get(Vehicle v)
    {
        float f;
        if (!tank.TryGetValue(v.Handle, out f))
        {
            // new car: somewhere between 55% and 100%
            f = TANK * (0.55f + 0.45f * (float)new Random(v.Handle).NextDouble());
            tank[v.Handle] = f;
        }
        return f;
    }

    // 0..1 for the HUD
    public static float Frac(Vehicle v)
    {
        if (v == null || !v.Exists()) return 1f;
        int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, v);
        if (cls == 13 || cls == 21) return 1f;      // bicycle, train
        float f = Get(v) / TANK;
        return f < 0f ? 0f : (f > 1f ? 1f : f);
    }

    // ---------- helpers ----------
    static float DW = 1280f, DH = 720f;
    static float SW { get { return GTA.UI.Screen.ScaledWidth; } }
    static float NX(float x) { return ((SW - DW) / 2f + x) / SW; }

    static void PR(float x, float y, float w, float h,
        int r, int g, int b, int a)
    {
        Function.Call(Hash.DRAW_RECT, NX(x + w / 2f), (y + h / 2f) / DH,
            w / SW, h / DH, r, g, b, a);
    }

    static void PT(string s, float x, float y, float scale,
        int r, int g, int b, bool center)
    {
        Function.Call(Hash.SET_TEXT_FONT, 4);
        Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, 255);
        Function.Call(Hash.SET_TEXT_DROP_SHADOW);
        if (center) Function.Call(Hash.SET_TEXT_CENTRE, true);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, NX(x), y / DH, 0);
    }

    // right aligned text (x = right edge)
    static void PTR(string s, float x, float y, float scale,
        int r, int g, int b)
    {
        Function.Call(Hash.SET_TEXT_FONT, 4);
        Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, 255);
        Function.Call(Hash.SET_TEXT_DROP_SHADOW);
        Function.Call(Hash.SET_TEXT_JUSTIFICATION, 2);
        Function.Call(Hash.SET_TEXT_WRAP, 0f, NX(x));
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, 0f, y / DH, 0);
    }

    static void Msg(string t)
    {
        GTA.UI.Screen.ShowSubtitle(t, 2500);
    }

    void MakeBlips()
    {
        blipsMade = true;
        foreach (Vector3 pos in Stations)
        {
            int b = Function.Call<int>(
                Hash.ADD_BLIP_FOR_COORD, pos.X, pos.Y, pos.Z);
            Function.Call(Hash.SET_BLIP_SPRITE, b, 361);     // gas pump
            Function.Call(Hash.SET_BLIP_COLOUR, b, 2);       // green
            Function.Call(Hash.SET_BLIP_SCALE, b, 0.8f);
            Function.Call(Hash.SET_BLIP_AS_SHORT_RANGE, b, true);
            Function.Call(Hash.BEGIN_TEXT_COMMAND_SET_BLIP_NAME, "STRING");
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME,
                "Gas Station");
            Function.Call(Hash.END_TEXT_COMMAND_SET_BLIP_NAME, b);
        }
    }

    // How close the pump must be to the car (metres). Like in real life:
    // park NEXT to the pump, along the side of the car.
    const float PUMP_SIDE_MAX = 2.0f;     // gap between car side and pump
    const float PUMP_LENGTH_EXTRA = 0.6f; // how far past the car's front / back
    const float STOP_SPEED = 0.6f;        // m/s, the car must be (almost) stopped

    static void ModelBox(Vehicle v, out Vector3 min, out Vector3 max)
    {
        OutputArgument a = new OutputArgument();
        OutputArgument b = new OutputArgument();
        Function.Call(Hash.GET_MODEL_DIMENSIONS, v.Model.Hash, a, b);
        min = a.GetResult<Vector3>();
        max = b.GetResult<Vector3>();
    }

    // true only when a pump stands right beside the car's body
    bool PumpNear(Vehicle v)
    {
        Vector3 mn, mx;
        ModelBox(v, out mn, out mx);
        float halfW = (mx.X - mn.X) / 2f;
        float halfL = (mx.Y - mn.Y) / 2f;
        float cx = (mx.X + mn.X) / 2f, cy = (mx.Y + mn.Y) / 2f;
        Vector3 pos = v.Position;

        for (int i = 0; i < pumpHash.Length; i++)
        {
            int h = Function.Call<int>(Hash.GET_CLOSEST_OBJECT_OF_TYPE,
                pos.X, pos.Y, pos.Z, 9f, pumpHash[i], false, false, false);
            if (h == 0) continue;

            Vector3 w = Function.Call<Vector3>(Hash.GET_ENTITY_COORDS, h, true);
            Vector3 loc = Function.Call<Vector3>(
                Hash.GET_OFFSET_FROM_ENTITY_GIVEN_WORLD_COORDS,
                v, w.X, w.Y, w.Z);

            float side = Math.Abs(loc.X - cx) - halfW;     // gap to the side of the car
            float along = Math.Abs(loc.Y - cy) - halfL;    // beyond front / back
            if (side <= PUMP_SIDE_MAX && along <= PUMP_LENGTH_EXTRA)
                return true;
        }
        return false;
    }

    // ===========================================================
    void OnTick(object sender, EventArgs e)
    {
        if (!blipsMade) MakeBlips();

        Ped p = Game.Player.Character;
        if (p == null || !p.Exists() || p.IsDead || DeathScreen.Downed)
        {
            fueling = false;
            return;
        }
        if (!p.IsInVehicle())
        {
            if (fueling) StopFueling(null);
            return;
        }

        Vehicle v = p.CurrentVehicle;
        if (v == null || !v.Exists()) return;

        int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, v);
        if (cls == 13 || cls == 21) return;           // no fuel needed

        float dt = Game.LastFrameTime;
        if (dt <= 0f || dt > 0.25f) dt = 0.033f;
        int now = Game.GameTime;
        float f = Get(v);

        bool key = (GetAsyncKeyState(KeyRefuel) & 0x8000) != 0;
        bool pressed = key && !prevKey;
        prevKey = key;

        if (now > nextPumpCheck)
        {
            nextPumpCheck = now + 150;
            nearPump = PumpNear(v);
        }

        bool still = v.Speed < STOP_SPEED;

        // ---------- using fuel ----------
        if (!fueling && v.Driver == p && v.IsEngineRunning)
        {
            float thr = Function.Call<float>(Hash.GET_CONTROL_NORMAL, 0, 71);
            if (thr < 0f) thr = 0f;
            f -= (USE_BASE + v.Speed * USE_PER_SPEED + thr * USE_PER_THROTTLE) * dt;
            if (f < 0f) f = 0f;
            tank[v.Handle] = f;
        }

        // ---------- empty ----------
        if (f <= 0f && !fueling)
        {
            Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, false, true, true);
            if (!emptyShown) { emptyShown = true; Msg("Out of fuel! Find a gas station"); }
        }
        else if (f > 0f) emptyShown = false;

        // ---------- refuel ----------
        if (fueling)
        {
            if (!nearPump || !still || pressed)
            {
                StopFueling(v);
            }
            else
            {
                float add = Math.Min(FILL_RATE * dt, TANK - f);
                costAcc += add * PRICE_PER_LITER;
                int pay = (int)costAcc;
                if (pay > 0)
                {
                    if (Game.Player.Money < pay)
                    {
                        Msg("Not enough money");
                        StopFueling(v);
                        return;
                    }
                    Game.Player.Money -= pay;
                    costAcc -= pay;
                    paid += pay;
                }
                f += add;
                tank[v.Handle] = f;
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, false, true, true);

                DrawFuelBar(f / TANK, f, paid);
                if (f >= TANK - 0.01f)
                {
                    Msg("Tank full");
                    StopFueling(v);
                }
            }
            return;
        }

        if (nearPump && still && f < TANK * 0.98f)
        {
            DrawPrompt("E  REFUEL     $" + (int)PRICE_PER_LITER + " / LITER",
                -1f, null);
            if (pressed)
            {
                if (Game.Player.Money < (int)PRICE_PER_LITER)
                    Msg("Not enough money");
                else
                {
                    fueling = true;
                    costAcc = 0f;
                    paid = 0;
                }
            }
        }
    }

    void StopFueling(Vehicle v)
    {
        fueling = false;
        if (v != null && v.Exists())
        {
            if (Get(v) > 0f)
                Function.Call(Hash.SET_VEHICLE_ENGINE_ON, v, true, true, false);
            if (paid > 0) Msg("Paid $" + paid);
        }
    }

    // green fuelling bar: smooth tube, % in the middle, litres and money
    void DrawFuelBar(float frac, float litres, int money)
    {
        if (frac < 0f) frac = 0f; if (frac > 1f) frac = 1f;
        float w = 320f, h = 74f;
        float x = 640f - w / 2f, y = 540f;

        // frame
        PR(x - 2f, y - 2f, w + 4f, h + 4f, 50, 230, 70, 255);
        PR(x, y, w, h, 8, 12, 10, 240);

        PT("FUELING", x + 14f, y + 5f, 0.36f, 235, 255, 240, false);
        PTR("$" + money, x + w - 14f, y + 5f, 0.36f, 255, 205, 90);
        PT(((int)Math.Round(litres)) + " / " + (int)TANK + " L",
            x + 14f, y + h - 18f, 0.26f, 150, 210, 160, false);

        // bar
        float bx = x + 14f, by = y + 28f, bw = w - 28f, bh = 18f;
        PR(bx - 1f, by - 1f, bw + 2f, bh + 2f, 40, 70, 52, 255);   // rim
        PR(bx, by, bw, bh, 14, 22, 18, 255);                       // track
        const int N = 40;
        float sw = bw / N;
        for (int i = 0; i < N; i++)
        {
            float lit = frac * N - i;
            if (lit <= 0f) break;
            if (lit > 1f) lit = 1f;
            float t = i / (float)(N - 1);
            int r = (int)(30 + 100 * t);
            int g = (int)(190 + 65 * t);
            int b = (int)(50 + 50 * t);
            PR(bx + i * sw, by, sw * lit + 0.6f, bh, r, g, b, 255);
        }
        PR(bx, by, bw * frac, bh * 0.32f, 255, 255, 255, 40);      // shine
        PT((int)Math.Round(frac * 100f) + "%", 640f, by - 1f, 0.34f,
            255, 255, 255, true);

        PTR("E  STOP", x + w - 14f, y + h - 18f, 0.22f, 200, 220, 205);
    }

    // text pill above the vehicle HUD
    void DrawPrompt(string text, float bar, string hint)
    {
        float w = 260f, h = bar >= 0f ? 46f : 28f;
        float x = 640f - w / 2f, y = 560f - (h - 28f);
        PR(x - 1.5f, y - 1.5f, w + 3f, h + 3f, 50, 230, 70, 255);
        PR(x, y, w, h, 12, 18, 15, 235);
        PT(text, 640f, y + 3f, 0.38f, 235, 255, 240, true);
        if (bar >= 0f)
        {
            PR(x + 12f, y + 28f, w - 24f, 8f, 24, 34, 28, 255);
            PR(x + 12f, y + 28f, (w - 24f) * bar, 8f, 70, 235, 100, 255);
            if (hint != null)
                PT(hint, 640f, y + 29f, 0.2f, 255, 255, 255, true);
        }
    }
}
