using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

// Cement stealing job: skill check -> steal -> police chase -> jail
public class CementJob : Script
{
    // ===== Settings =====
    // cement props in the map (nearby props with these models = a spot)
    static readonly string[] CementModels = {
        "prop_cementbags01",
        "prop_cement_bag_01",
        "prop_cement_sack"
    };

    // extra spots you can add by hand (x, y, z)
    static readonly Vector3[] ExtraSpots = {
        // new Vector3(0f, 0f, 0f),
    };

    const float SPOT_RANGE = 2.5f;
    const int SPOT_COOLDOWN_MS = 90000;    // same spot again after 90 s

    const int STEAL_MS = 12000;            // steal time
    const float POLICE_AT = 0.70f;         // police are called at 70%
    const int NEED_HITS = 3;               // skill check hits
    const double TWO_BAGS_CHANCE = 0.35;   // chance of 2 bags instead of 1

    const int COPS_COUNT = 3;
    const int GRACE_SECONDS = 10;          // countdown before tire shooting
    const int TIRE_SHOT_MS = 3500;         // one tire every 3.5 s
    const int LOST_SECONDS = 8;            // lose them for 8 s = escaped
    const int JAIL_SECONDS = 10;

    // jail and release points (Mission Row police station)
    static readonly Vector3 JailCell = new Vector3(460.0f, -994.0f, 24.9f);
    const float JailHeading = 90f;
    static readonly Vector3 ReleasePos = new Vector3(440.0f, -982.0f, 30.7f);
    const float ReleaseHeading = 90f;

    int InteractKey = 0x45;   // E
    int CancelKey = 0x08;     // Backspace
    // ====================

    class Cop
    {
        public Vehicle Car;
        public Ped Driver;
        public Ped Passenger;
    }

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    static readonly Random rng = new Random();

    bool[] prev = new bool[256];
    int[] modelHashes;
    Dictionary<string, int> cooldown = new Dictionary<string, int>();

    // 0 idle, 1 skill check, 2 stealing, 3 jail
    int state;
    Vector3 spotPos;
    string spotKey = "";
    int nextScan;
    bool nearSpot;

    // skill check
    float cursor;
    float dir = 1f;
    float speed = 0.55f;
    float zoneStart;
    float zoneW = 0.13f;
    int hits;
    int lastTick;

    // stealing
    int stealStart;
    bool policeCalled;

    // chase
    List<Cop> cops = new List<Cop>();
    bool chase;
    bool arrived;
    bool graceOver;
    int countdownStart;
    int nextTire;
    int nextTask;
    float catchTimer;
    float lostTimer;

    public CementJob()
    {
        modelHashes = new int[CementModels.Length];
        for (int i = 0; i < CementModels.Length; i++)
            modelHashes[i] = Function.Call<int>(
                Hash.GET_HASH_KEY, CementModels[i]);
        lastTick = Game.GameTime;
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

    static bool PadJust(int c)
    {
        return Function.Call<bool>(
            Hash.IS_CONTROL_JUST_PRESSED, 0, c);
    }

    static bool PadJustDis(int c)
    {
        return Function.Call<bool>(
            Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, c);
    }

    static void Msg(string t)
    {
        GTA.UI.Screen.ShowSubtitle(t, 2500);
    }

    static void Box(float x, float y, float w, float h,
        int r, int g, int b, int a)
    {
        Function.Call(Hash.DRAW_RECT, x + w / 2f, y + h / 2f,
            w, h, r, g, b, a);
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

    static bool Alive(Ped x)
    {
        return x != null && x.Exists() && !x.IsDead;
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

    static string KeyOf(Vector3 v)
    {
        return ((int)(v.X / 2f)) + "," + ((int)(v.Y / 2f));
    }

    // ---------- Finding a cement spot ----------
    bool FindSpot(Ped p, out Vector3 pos)
    {
        pos = Vector3.Zero;

        Prop[] props = World.GetNearbyProps(p.Position, SPOT_RANGE);
        for (int i = 0; i < props.Length; i++)
        {
            Prop pr = props[i];
            if (pr == null || !pr.Exists()) continue;
            int mh = pr.Model.Hash;
            for (int k = 0; k < modelHashes.Length; k++)
            {
                if (modelHashes[k] == mh)
                {
                    pos = pr.Position;
                    return true;
                }
            }
        }

        for (int i = 0; i < ExtraSpots.Length; i++)
        {
            if (ExtraSpots[i].DistanceTo(p.Position) < SPOT_RANGE)
            {
                pos = ExtraSpots[i];
                return true;
            }
        }
        return false;
    }

    // ---------- Skill check ----------
    void NewZone()
    {
        zoneStart = 0.10f + (float)rng.NextDouble() * 0.70f;
    }

    void StartSkillCheck(Vector3 pos)
    {
        state = 1;
        spotPos = pos;
        spotKey = KeyOf(pos);
        hits = 0;
        cursor = 0f;
        dir = 1f;
        speed = 0.55f;
        NewZone();
    }

    void DrawBar(float cy, float zs, float zw, float cur)
    {
        float x0 = 0.35f, w = 0.30f, h = 0.034f;
        Box(x0 - 0.003f, cy - 0.004f, w + 0.006f, h + 0.008f,
            0, 0, 0, 200);
        Box(x0, cy, w, h, 30, 34, 44, 255);
        Box(x0 + w * zs, cy, w * zw, h, 60, 230, 120, 255);
        Box(x0 + w * cur - 0.002f, cy - 0.006f, 0.004f, h + 0.012f,
            255, 255, 255, 255);
    }

    void SkillCheckTick(Ped p, float dt, bool press, bool cancel)
    {
        Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

        cursor += dir * speed * dt;
        if (cursor >= 1f) { cursor = 1f; dir = -1f; }
        if (cursor <= 0f) { cursor = 0f; dir = 1f; }

        Mid("Press E in the green zone   " + hits + " / " + NEED_HITS,
            0.5f, 0.735f, 4, 0.55f, 255, 255, 255, 255);
        DrawBar(0.78f, zoneStart, zoneW, cursor);
        Mid("Backspace = cancel", 0.5f, 0.825f, 4, 0.32f,
            200, 200, 205, 255);

        if (cancel)
        {
            state = 0;
            return;
        }

        if (press)
        {
            bool inside = cursor >= zoneStart
                && cursor <= zoneStart + zoneW;
            if (inside)
            {
                hits++;
                if (hits >= NEED_HITS)
                {
                    StartSteal(p);
                    return;
                }
                speed *= 1.15f;
                NewZone();
            }
            else
            {
                Msg("Missed! Try again");
                hits = 0;
                speed = 0.55f;
                NewZone();
            }
        }
    }

    // ---------- Stealing ----------
    void StartSteal(Ped p)
    {
        state = 2;
        stealStart = Game.GameTime;
        policeCalled = false;

        string dict = "amb@world_human_gardener_plant@male@base";
        if (LoadDict(dict))
            Function.Call(Hash.TASK_PLAY_ANIM, p, dict, "base",
                8f, -8f, -1, 1, 0f, false, false, false);
    }

    void StopStealAnim(Ped p)
    {
        Function.Call(Hash.STOP_ANIM_TASK, p,
            "amb@world_human_gardener_plant@male@base", "base", 1f);
        p.Task.ClearAll();
    }

    void StealTick(Ped p, int now, bool cancel)
    {
        Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

        float prog = (now - stealStart) / (float)STEAL_MS;
        if (prog > 1f) prog = 1f;

        Mid("STEALING CEMENT  " + (int)(prog * 100f) + "%",
            0.5f, 0.735f, 4, 0.55f, 255, 255, 255, 255);
        float x0 = 0.35f, w = 0.30f, h = 0.026f;
        Box(x0 - 0.003f, 0.78f - 0.004f, w + 0.006f, h + 0.008f,
            0, 0, 0, 200);
        Box(x0, 0.78f, w, h, 30, 34, 44, 255);
        Box(x0, 0.78f, w * prog, h, 60, 230, 120, 255);

        if (!policeCalled && prog >= POLICE_AT)
        {
            policeCalled = true;
            SpawnCops(p);
        }

        if (cancel)
        {
            StopStealAnim(p);
            state = 0;
            Msg("Stopped");
            return;
        }

        if (prog >= 1f)
        {
            StopStealAnim(p);
            GiveCement();
            cooldown[spotKey] = now + SPOT_COOLDOWN_MS;
            state = 0;
        }
    }

    void GiveCement()
    {
        int n = rng.NextDouble() < TWO_BAGS_CHANCE ? 2 : 1;
        Inv.Qty[Inv.Cement] += n;
        Inv.Dirty = true;
        Msg("You got " + n + " cement bag" + (n > 1 ? "s" : "")
            + "!");
    }

    // ---------- Police ----------
    void SpawnCops(Ped p)
    {
        string[] carNames = { "police", "police2", "police3" };

        for (int i = 0; i < COPS_COUNT; i++)
        {
            double ang = rng.NextDouble() * Math.PI * 2.0;
            float dist = 130f + (float)rng.NextDouble() * 70f;
            Vector3 want = p.Position + new Vector3(
                (float)Math.Cos(ang) * dist,
                (float)Math.Sin(ang) * dist, 0f);

            Vector3 spawn = World.GetNextPositionOnStreet(want, true);
            if (spawn == Vector3.Zero) continue;

            Vector3 d = p.Position - spawn;
            float hd = (float)(Math.Atan2(-d.X, d.Y) * 180.0 / Math.PI);

            Vehicle car = World.CreateVehicle(
                new Model(carNames[i % carNames.Length]), spawn, hd);
            if (car == null) continue;

            Model cm = new Model("s_m_y_cop_01");
            Ped drv = car.CreatePedOnSeat(VehicleSeat.Driver, cm);
            Ped pas = car.CreatePedOnSeat(VehicleSeat.Passenger, cm);

            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY,
                car, true, true);
            Function.Call(Hash.SET_VEHICLE_SIREN, car, true);

            Cop c = new Cop();
            c.Car = car;
            c.Driver = drv;
            c.Passenger = pas;

            Ped[] both = { drv, pas };
            for (int k = 0; k < 2; k++)
            {
                if (both[k] == null) continue;
                Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY,
                    both[k], true, true);
                Function.Call(
                    Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS,
                    both[k], true);
                Function.Call(Hash.SET_PED_KEEP_TASK, both[k], true);
            }
            if (drv != null)
            {
                Function.Call(Hash.SET_DRIVER_ABILITY, drv, 1f);
                Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, drv, 1f);
            }
            cops.Add(c);
        }

        if (cops.Count == 0) return;

        chase = true;
        arrived = false;
        graceOver = false;
        catchTimer = 0f;
        lostTimer = 0f;
        nextTask = 0;
        nextTire = 0;
        Msg("Police are coming!");
    }

    void AssignTasks(Ped p, int now)
    {
        if (now < nextTask) return;
        nextTask = now + 2500;

        bool onFoot = !p.IsInVehicle();
        for (int i = 0; i < cops.Count; i++)
        {
            Cop c = cops[i];
            if (c.Car == null || !c.Car.Exists()) continue;

            Ped[] both = { c.Driver, c.Passenger };
            if (onFoot)
            {
                for (int k = 0; k < 2; k++)
                {
                    Ped x = both[k];
                    if (!Alive(x)) continue;
                    if (x.IsInVehicle())
                        Function.Call(Hash.TASK_LEAVE_VEHICLE,
                            x, c.Car, 0);
                    else
                        Function.Call(Hash.TASK_GO_TO_ENTITY, x, p,
                            -1, 1.5f, 3f, 0f, 0);
                }
            }
            else if (Alive(c.Driver) && c.Driver.IsInVehicle())
            {
                if (!graceOver)
                    Function.Call(Hash.TASK_VEHICLE_CHASE, c.Driver, p);
                else
                    Function.Call(
                        Hash.TASK_VEHICLE_MISSION_PED_TARGET,
                        c.Driver, c.Car, p, 2, 70f, 786468,
                        0f, 0f, true);
            }
        }
    }

    float NearestCop(Ped p)
    {
        float best = 99999f;
        for (int i = 0; i < cops.Count; i++)
        {
            Cop c = cops[i];
            if (c.Car == null || !c.Car.Exists()) continue;
            float d = c.Car.Position.DistanceTo(p.Position);
            if (d < best) best = d;
        }
        return best;
    }

    void ShootTire(Ped p)
    {
        Vehicle pv = p.CurrentVehicle;
        if (pv == null || !pv.Exists()) return;

        int[] wheels = { 0, 1, 4, 5 };
        float[] ox = { -0.9f, 0.9f, -0.9f, 0.9f };
        float[] oy = { 1.4f, 1.4f, -1.4f, -1.4f };

        List<int> ok = new List<int>();
        for (int i = 0; i < 4; i++)
            if (!Function.Call<bool>(
                Hash.IS_VEHICLE_TYRE_BURST, pv, wheels[i], false))
                ok.Add(i);
        if (ok.Count == 0) return;

        // closest police car with a gunman
        Cop shooter = null;
        float bd = 70f;
        for (int i = 0; i < cops.Count; i++)
        {
            Cop c = cops[i];
            if (c.Car == null || !c.Car.Exists()) continue;
            if (!Alive(c.Passenger) && !Alive(c.Driver)) continue;
            float d = c.Car.Position.DistanceTo(p.Position);
            if (d < bd)
            {
                bd = d;
                shooter = c;
            }
        }
        if (shooter == null) return;

        int pick = ok[rng.Next(ok.Count)];
        Vector3 target = Function.Call<Vector3>(
            Hash.GET_OFFSET_FROM_ENTITY_IN_WORLD_COORDS,
            pv, ox[pick], oy[pick], -0.4f);
        Vector3 src = shooter.Car.Position + new Vector3(0f, 0f, 1f);
        Ped owner = Alive(shooter.Passenger)
            ? shooter.Passenger : shooter.Driver;

        Function.Call(Hash.SHOOT_SINGLE_BULLET_BETWEEN_COORDS,
            src.X, src.Y, src.Z, target.X, target.Y, target.Z,
            1, true, (uint)WeaponHash.Pistol, owner, true, false,
            1000f);
        Function.Call(Hash.SET_VEHICLE_TYRE_BURST,
            pv, wheels[pick], true, 1000f);
    }

    void EndChase(bool escaped)
    {
        for (int i = 0; i < cops.Count; i++)
        {
            Cop c = cops[i];
            if (escaped)
            {
                if (c.Car != null && c.Car.Exists()
                    && Alive(c.Driver) && c.Driver.IsInVehicle())
                {
                    Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER,
                        c.Driver, c.Car, 25f, 786603);
                }
                if (c.Passenger != null && c.Passenger.Exists())
                    c.Passenger.MarkAsNoLongerNeeded();
                if (c.Driver != null && c.Driver.Exists())
                    c.Driver.MarkAsNoLongerNeeded();
                if (c.Car != null && c.Car.Exists())
                    c.Car.MarkAsNoLongerNeeded();
            }
            else
            {
                if (c.Passenger != null && c.Passenger.Exists())
                    c.Passenger.Delete();
                if (c.Driver != null && c.Driver.Exists())
                    c.Driver.Delete();
                if (c.Car != null && c.Car.Exists())
                    c.Car.Delete();
            }
        }
        cops.Clear();
        chase = false;
        arrived = false;
        graceOver = false;
        catchTimer = 0f;
        lostTimer = 0f;
    }

    void DrawCountdown(Ped p, int sec)
    {
        Vector3 w = p.Position + new Vector3(0f, 0f, 1.3f);
        OutputArgument ox = new OutputArgument();
        OutputArgument oy = new OutputArgument();
        bool ok = Function.Call<bool>(
            Hash.GET_SCREEN_COORD_FROM_WORLD_COORD,
            w.X, w.Y, w.Z, ox, oy);
        if (!ok) return;
        float sx = ox.GetResult<float>();
        float sy = oy.GetResult<float>();
        Mid(sec.ToString(), sx, sy - 0.04f, 4, 1.6f, 255, 70, 70, 255);
    }

    void ChaseTick(Ped p, float dt, int now)
    {
        bool inVeh = p.IsInVehicle();

        // all police gone or far / player too high = lost
        int catchers = 0;
        for (int i = 0; i < cops.Count; i++)
        {
            Cop c = cops[i];
            if (c.Car == null || !c.Car.Exists()) continue;
            if (!Alive(c.Driver) && !Alive(c.Passenger)) continue;
            Vector3 cp = c.Car.Position;
            float dx = cp.X - p.Position.X;
            float dy = cp.Y - p.Position.Y;
            float flat = (float)Math.Sqrt(dx * dx + dy * dy);
            float up = p.Position.Z - cp.Z;
            if (flat < 250f && up < 10f) catchers++;
        }
        if (catchers == 0) lostTimer += dt;
        else lostTimer = 0f;

        if (lostTimer >= LOST_SECONDS)
        {
            EndChase(true);
            Msg("You escaped from the police!");
            return;
        }

        // police arrived -> 10 second countdown over the player
        if (!arrived && NearestCop(p) < 45f)
        {
            arrived = true;
            countdownStart = now;
            Msg("The police have arrived!");
        }

        if (arrived && !graceOver)
        {
            int left = GRACE_SECONDS - (now - countdownStart) / 1000;
            if (left <= 0)
            {
                graceOver = true;
                nextTask = 0;
                nextTire = now + 1000;
            }
            else
            {
                DrawCountdown(p, left);
            }
        }

        AssignTasks(p, now);

        // shoot the tires (one by one, up to 4)
        if (graceOver && inVeh && now >= nextTire)
        {
            nextTire = now + TIRE_SHOT_MS;
            ShootTire(p);
        }

        // caught?
        if (graceOver)
        {
            bool close = false;
            for (int i = 0; i < cops.Count; i++)
            {
                Cop c = cops[i];
                Ped[] both = { c.Driver, c.Passenger };
                for (int k = 0; k < 2; k++)
                {
                    Ped x = both[k];
                    if (!Alive(x)) continue;
                    float lim = inVeh ? 7f : 2.6f;
                    if (x.Position.DistanceTo(p.Position) < lim)
                        close = true;
                }
            }
            bool slow = true;
            if (inVeh) slow = p.CurrentVehicle.Speed * 3.6f < 12f;

            if (close && slow) catchTimer += dt;
            else catchTimer = Math.Max(0f, catchTimer - dt);

            if (catchTimer >= 2.5f) Busted(p);
        }
    }

    // ---------- Jail ----------
    void Busted(Ped p)
    {
        state = 3;
        EndChase(false);

        Function.Call(Hash.DO_SCREEN_FADE_OUT, 500);
        int t = Game.GameTime + 1500;
        while (!Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)
            && Game.GameTime < t)
            Script.Yield();

        Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, p);
        p.Position = JailCell;
        p.Heading = JailHeading;
        Game.Player.WantedLevel = 0;

        Function.Call(Hash.DO_SCREEN_FADE_IN, 500);

        int end = Game.GameTime + JAIL_SECONDS * 1000;
        while (Game.GameTime < end)
        {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);
            int left = (end - Game.GameTime) / 1000 + 1;
            Mid("BUSTED", 0.5f, 0.40f, 4, 1.6f, 255, 70, 70, 255);
            Mid("JAIL  " + left + "s", 0.5f, 0.48f, 4, 0.8f,
                255, 255, 255, 255);
            Script.Yield();
        }

        Function.Call(Hash.DO_SCREEN_FADE_OUT, 500);
        t = Game.GameTime + 1500;
        while (!Function.Call<bool>(Hash.IS_SCREEN_FADED_OUT)
            && Game.GameTime < t)
            Script.Yield();

        p.Position = ReleasePos;
        p.Heading = ReleaseHeading;
        Script.Wait(300);
        Function.Call(Hash.DO_SCREEN_FADE_IN, 700);

        state = 0;
        Msg("You were released");
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

        bool kE = Pressed(InteractKey);
        bool kCancel = Pressed(CancelKey);

        int now = Game.GameTime;
        float dt = (now - lastTick) / 1000f;
        if (dt < 0f || dt > 1f) dt = 0f;
        lastTick = now;

        if (p == null || !p.Exists()) return;

        if (p.IsDead)
        {
            if (chase) EndChase(false);
            if (state != 3) state = 0;
            Inv.Busy = state != 0;
            return;
        }

        if (state == 3) return;

        // chase runs in the background (also while stealing)
        if (chase) ChaseTick(p, dt, now);
        if (state == 3) return;

        if (state == 1)
        {
            bool press = kE || PadJustDis(51);
            bool cancel = kCancel || PadJustDis(202);
            SkillCheckTick(p, dt, press, cancel);
        }
        else if (state == 2)
        {
            bool cancel = kCancel || PadJustDis(202);
            StealTick(p, now, cancel);
        }
        else
        {
            // idle: look for a cement spot
            if (now >= nextScan)
            {
                nextScan = now + 300;
                Vector3 pos;
                nearSpot = false;
                if (!p.IsInVehicle() && !chase && FindSpot(p, out pos))
                {
                    int until;
                    string key = KeyOf(pos);
                    if (!cooldown.TryGetValue(key, out until)
                        || now >= until)
                    {
                        nearSpot = true;
                        spotPos = pos;
                    }
                }
            }

            if (nearSpot && !Inv.Busy)
            {
                Mid("Press E - Steal cement", 0.5f, 0.88f, 4, 0.5f,
                    255, 255, 255, 255);
                if (kE || PadJust(51))
                    StartSkillCheck(spotPos);
            }
        }

        Inv.Busy = state != 0;
    }
}
