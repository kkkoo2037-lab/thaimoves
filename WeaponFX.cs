using System;
using System.IO;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

public class WeaponFX : Script
{
    // ===== Keys (Windows key codes) =====
    int MenuKey = 0x71;   // F2
    int KUp = 0x26, KDown = 0x28, KLeft = 0x25, KRight = 0x27;
    int KEnter = 0x0D, KBack = 0x08;
    // ====================================

    struct Fx
    {
        public string Label, Asset, Name;
        public Fx(string l, string a, string n)
        {
            Label = l;
            Asset = a;
            Name = n;
        }
    }

    // Weapon effects (looped). Visual particles only.
    static readonly Fx[] WeaponList = {
        new Fx("OFF", "", ""),
        new Fx("Fire", "core",
            "ent_ray_heli_aprtmnt_l_fire"),
        new Fx("Thermite Flame", "scr_ornate_heist",
            "scr_heist_ornate_thermal_burn"),
        new Fx("Steam", "core", "ent_sht_steam"),
        new Fx("Electric Sparks", "core",
            "ent_amb_sparking_wires"),
        new Fx("Toxic Smoke", "core",
            "exp_grd_bzgas_smoke"),
        new Fx("Firework Fountain", "scr_indep_fireworks",
            "scr_indep_firework_fountain"),
    };

    // Hit effects (one shot). Visual particles only.
    static readonly Fx[] HitList = {
        new Fx("OFF", "", ""),
        new Fx("Explosion (visual)", "core",
            "exp_grd_grenade"),
        new Fx("Fireball (visual)", "scr_trevor1",
            "scr_trev1_trailer_boosh"),
        new Fx("Blood (visual)", "scr_solomon3",
            "scr_trev4_747_blood_impact"),
        new Fx("Sparks", "core", "ent_sht_electrical_box"),
        new Fx("Steam Puff", "core", "ent_sht_steam"),
        new Fx("Toxic Smoke Puff", "core",
            "exp_grd_bzgas_smoke"),
        new Fx("Firework Burst", "scr_indep_fireworks",
            "scr_indep_firework_shotburst"),
        new Fx("Firework Star", "scr_indep_fireworks",
            "scr_indep_firework_starburst"),
        new Fx("Clown Puff", "scr_rcbarry2",
            "scr_clown_appears"),
    };

    static readonly float[] Scales =
        { 0.1f, 0.2f, 0.3f, 0.5f, 0.75f, 1f, 1.5f };

    readonly string[] rows = {
        "Weapon Effect", "Weapon Scale",
        "Hit Effect", "Hit Scale",
        "All Off", "Close"
    };

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    const string CFG = "scripts\\WeaponFX.cfg";

    bool[] prev = new bool[256];
    bool menuOpen;
    int sel;

    // default scale x0.3 (index 2)
    int weaponIdx, weaponScaleIdx = 2;
    int hitIdx, hitScaleIdx = 2;

    int fxHandle, fxEntity;
    int fxIdxApplied = -1, fxScaleApplied = -1;
    int failUntil;
    bool failMsgShown;

    int nextHit, nextMelee, nextLoad;

    public WeaponFX()
    {
        Load();
        Tick += OnTick;
    }

    // ---------- Save / Load ----------
    void Save()
    {
        try
        {
            string s = weaponIdx + "," + weaponScaleIdx + ","
                + hitIdx + "," + hitScaleIdx;
            File.WriteAllText(CFG, s);
        }
        catch (Exception) { }
    }

    void Load()
    {
        try
        {
            if (!File.Exists(CFG)) return;
            string[] a = File.ReadAllText(CFG).Trim().Split(',');
            if (a.Length < 4) return;
            weaponIdx = Clamp(int.Parse(a[0]), WeaponList.Length);
            weaponScaleIdx = Clamp(int.Parse(a[1]), Scales.Length);
            hitIdx = Clamp(int.Parse(a[2]), HitList.Length);
            hitScaleIdx = Clamp(int.Parse(a[3]), Scales.Length);
        }
        catch (Exception) { }
    }

    static int Clamp(int v, int n)
    {
        if (v < 0) return 0;
        if (v >= n) return n - 1;
        return v;
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
            Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, c);
    }

    static void Msg(string t)
    {
        GTA.UI.Screen.ShowSubtitle(t, 1500);
    }

    static bool AssetLoaded(string asset)
    {
        return Function.Call<bool>(
            Hash.HAS_NAMED_PTFX_ASSET_LOADED, asset);
    }

    static void Request(string asset)
    {
        if (asset != "")
            Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, asset);
    }

    static void DrawText(string s, float x, float y, float scale,
        int r, int g, int b, int a)
    {
        Function.Call(Hash.SET_TEXT_FONT, 0);
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

    void StopWeaponFx()
    {
        if (fxHandle != 0)
        {
            Function.Call(
                Hash.STOP_PARTICLE_FX_LOOPED, fxHandle, false);
            Function.Call(Hash.REMOVE_PARTICLE_FX, fxHandle, false);
            fxHandle = 0;
        }
        fxEntity = 0;
        fxIdxApplied = -1;
        fxScaleApplied = -1;
    }

    // ---------- Weapon effect (attached to the weapon) ----------
    void UpdateWeaponFx(Ped p, int now)
    {
        if (weaponIdx == 0 || p.IsDead)
        {
            StopWeaponFx();
            return;
        }

        int ent = Function.Call<int>(
            Hash.GET_CURRENT_PED_WEAPON_ENTITY_INDEX, p, 0);
        bool exists = ent != 0 && ent != p.Handle
            && Function.Call<bool>(Hash.DOES_ENTITY_EXIST, ent);
        if (!exists)
        {
            StopWeaponFx();
            return;
        }

        bool same = fxHandle != 0 && ent == fxEntity
            && fxIdxApplied == weaponIdx
            && fxScaleApplied == weaponScaleIdx;
        if (same && Function.Call<bool>(
            Hash.DOES_PARTICLE_FX_LOOPED_EXIST, fxHandle))
            return;

        if (now < failUntil) return;

        StopWeaponFx();

        Fx f = WeaponList[weaponIdx];
        if (!AssetLoaded(f.Asset))
        {
            Request(f.Asset);
            return;
        }

        Function.Call(Hash.USE_PARTICLE_FX_ASSET, f.Asset);
        fxHandle = Function.Call<int>(
            Hash.START_PARTICLE_FX_LOOPED_ON_ENTITY,
            f.Name, ent,
            0f, 0f, 0f, 0f, 0f, 0f,
            Scales[weaponScaleIdx],
            false, false, false);

        bool ok = fxHandle != 0 && Function.Call<bool>(
            Hash.DOES_PARTICLE_FX_LOOPED_EXIST, fxHandle);
        if (!ok)
        {
            fxHandle = 0;
            failUntil = now + 5000;
            if (!failMsgShown)
            {
                failMsgShown = true;
                Msg("Weapon effect unavailable: " + f.Label);
            }
            return;
        }

        fxEntity = ent;
        fxIdxApplied = weaponIdx;
        fxScaleApplied = weaponScaleIdx;
    }

    // ---------- Hit effect (visual only) ----------
    void SpawnHit(Vector3 pos)
    {
        if (hitIdx == 0) return;
        Fx f = HitList[hitIdx];
        if (!AssetLoaded(f.Asset))
        {
            Request(f.Asset);
            return;
        }
        Function.Call(Hash.USE_PARTICLE_FX_ASSET, f.Asset);
        Function.Call(
            Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD,
            f.Name, pos.X, pos.Y, pos.Z,
            0f, 0f, 0f, Scales[hitScaleIdx],
            false, false, false);
    }

    void UpdateHitFx(Ped p, int now)
    {
        if (hitIdx == 0) return;

        uint w = Function.Call<uint>(
            Hash.GET_SELECTED_PED_WEAPON, p);
        uint grp = Function.Call<uint>(
            Hash.GET_WEAPONTYPE_GROUP, w);
        bool melee = grp == 3566412244u || grp == 2685387236u;

        if (!melee)
        {
            if (p.IsShooting && now >= nextHit)
            {
                nextHit = now + 80;
                OutputArgument oa = new OutputArgument();
                bool got = Function.Call<bool>(
                    Hash.GET_PED_LAST_WEAPON_IMPACT_COORD, p, oa);
                if (got) SpawnHit(oa.GetResult<Vector3>());
            }
            return;
        }

        bool fighting = Function.Call<bool>(
            Hash.IS_PED_IN_MELEE_COMBAT, p);
        if (now < nextMelee || !fighting) return;

        nextMelee = now + 100;
        Ped[] peds = World.GetNearbyPeds(p, 6f);
        for (int i = 0; i < peds.Length; i++)
        {
            Ped t = peds[i];
            if (t == null || !t.Exists() || t == p) continue;
            bool hurt = Function.Call<bool>(
                Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, t, p, true);
            if (!hurt) continue;
            Vector3 pos = Function.Call<Vector3>(
                Hash.GET_PED_BONE_COORDS, t, 24818, 0f, 0f, 0f);
            SpawnHit(pos);
            Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, t);
        }
    }

    // ---------- Menu ----------
    void Change(int dir)
    {
        if (sel == 0)
        {
            int n = WeaponList.Length;
            weaponIdx = (weaponIdx + dir + n) % n;
            failUntil = 0;
            failMsgShown = false;
            Request(WeaponList[weaponIdx].Asset);
        }
        else if (sel == 1)
        {
            int n = Scales.Length;
            weaponScaleIdx = (weaponScaleIdx + dir + n) % n;
        }
        else if (sel == 2)
        {
            int n = HitList.Length;
            hitIdx = (hitIdx + dir + n) % n;
            Request(HitList[hitIdx].Asset);
        }
        else if (sel == 3)
        {
            int n = Scales.Length;
            hitScaleIdx = (hitScaleIdx + dir + n) % n;
        }
        Save();
    }

    string RowText(int i)
    {
        switch (i)
        {
            case 0:
                return "Weapon Effect:  < "
                    + WeaponList[weaponIdx].Label + " >";
            case 1:
                return "Weapon Scale:  < x"
                    + Scales[weaponScaleIdx].ToString("0.00") + " >";
            case 2:
                return "Hit Effect:  < "
                    + HitList[hitIdx].Label + " >";
            case 3:
                return "Hit Scale:  < x"
                    + Scales[hitScaleIdx].ToString("0.00") + " >";
            default:
                return rows[i];
        }
    }

    void DrawMenu()
    {
        int n = rows.Length;
        float h = 0.09f + n * 0.036f;
        float cy = 0.28f + h / 2f - 0.02f;
        Function.Call(Hash.DRAW_RECT,
            0.83f, cy, 0.30f, h, 0, 0, 0, 185);
        DrawText("WEAPON EFFECTS", 0.70f, 0.255f, 0.5f,
            255, 255, 255, 255);

        for (int i = 0; i < n; i++)
        {
            bool on = (i == sel);
            int g = on ? 220 : 255;
            int b = on ? 0 : 255;
            DrawText(RowText(i), 0.70f, 0.30f + i * 0.036f, 0.4f,
                255, g, b, 255);
        }

        float hy = 0.30f + n * 0.036f + 0.005f;
        DrawText("L/R change - Enter select",
            0.70f, hy, 0.3f, 200, 200, 200, 255);
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

        bool kMenu = Pressed(MenuKey);
        bool kUp = Pressed(KUp);
        bool kDown = Pressed(KDown);
        bool kLeft = Pressed(KLeft);
        bool kRight = Pressed(KRight);
        bool kEnter = Pressed(KEnter);
        bool kBack = Pressed(KBack);

        if (p == null || !p.Exists() || p.IsDead)
        {
            StopWeaponFx();
            menuOpen = false;
            return;
        }

        int now = Game.GameTime;

        if (now >= nextLoad)
        {
            nextLoad = now + 1000;
            Request(WeaponList[weaponIdx].Asset);
            Request(HitList[hitIdx].Asset);
        }

        if (kMenu)
        {
            menuOpen = !menuOpen;
            sel = 0;
        }

        if (menuOpen)
        {
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            int n = rows.Length;
            if (kUp || PadJust(172)) sel = (sel + n - 1) % n;
            if (kDown || PadJust(173)) sel = (sel + 1) % n;
            if (kLeft || PadJust(174)) Change(-1);
            if (kRight || PadJust(175)) Change(1);
            if (kBack || PadJust(202)) menuOpen = false;

            if (kEnter || PadJust(201))
            {
                if (sel == 4)
                {
                    weaponIdx = 0;
                    hitIdx = 0;
                    Save();
                    Msg("All effects OFF");
                }
                else if (sel == 5)
                {
                    menuOpen = false;
                }
            }

            if (menuOpen) DrawMenu();
        }

        UpdateWeaponFx(p, now);
        UpdateHitFx(p, now);
    }
}
