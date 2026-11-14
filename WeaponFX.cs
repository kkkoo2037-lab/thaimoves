using System;
using System.IO;
using System.Runtime.InteropServices;
using GTA;
using GTA.Math;
using GTA.Native;

public class WeaponFX : Script
{
    // ===== ปุ่ม (รหัส Windows) =====
    int MenuKey = 0x71;   // F2
    int KUp = 0x26, KDown = 0x28, KLeft = 0x25, KRight = 0x27, KEnter = 0x0D, KBack = 0x08;
    // ==============================

    struct Fx
    {
        public string Label, Asset, Name;
        public Fx(string l, string a, string n) { Label = l; Asset = a; Name = n; }
    }

    // เอฟเฟกต์บนอาวุธ (วนต่อเนื่อง) เป็นพาร์ติเคิลภาพล้วนๆ ไม่มีดาเมจ
    static readonly Fx[] WeaponList = {
        new Fx("OFF", "", ""),
        new Fx("Fire", "core", "ent_ray_heli_aprtmnt_l_fire"),
        new Fx("Thermite Flame", "scr_ornate_heist", "scr_heist_ornate_thermal_burn"),
        new Fx("Steam", "core", "ent_sht_steam"),
        new Fx("Electric Sparks", "core", "ent_amb_sparking_wires"),
        new Fx("Toxic Smoke", "core", "exp_grd_bzgas_smoke"),
        new Fx("Firework Fountain", "scr_indep_fireworks", "scr_indep_firework_fountain"),
    };

    // เอฟเฟกต์ตอนโจมตีโดน (ครั้งเดียว) เป็นพาร์ติเคิลภาพล้วนๆ ไม่มีดาเมจ
    static readonly Fx[] HitList = {
        new Fx("OFF", "", ""),
        new Fx("Explosion (visual)", "core", "exp_grd_grenade"),
        new Fx("Fireball (visual)", "scr_trevor1", "scr_trev1_trailer_boosh"),
        new Fx("Blood (visual)", "scr_solomon3", "scr_trev4_747_blood_impact"),
        new Fx("Sparks", "core", "ent_sht_electrical_box"),
        new Fx("Steam Puff", "core", "ent_sht_steam"),
        new Fx("Toxic Smoke Puff", "core", "exp_grd_bzgas_smoke"),
        new Fx("Firework Burst", "scr_indep_fireworks", "scr_indep_firework_shotburst"),
        new Fx("Firework Star", "scr_indep_fireworks", "scr_indep_firework_starburst"),
        new Fx("Clown Puff", "scr_rcbarry2", "scr_clown_appears"),
    };

    static readonly float[] Scales = { 0.1f, 0.2f, 0.3f, 0.5f, 0.75f, 1f, 1.5f };

    readonly string[] rows = { "Weapon Effect", "Weapon Scale", "Hit Effect", "Hit Scale", "All Off", "Close" };

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    const string CFG = "scripts\\WeaponFX.cfg";

    bool[] prev = new bool[256];
    bool menuOpen;
    int sel;

    // ขนาดเริ่มต้น x0.3 (ดัชนี 2)
    int weaponIdx, weaponScaleIdx = 2, hitIdx, hitScaleIdx = 2;

    // เอฟเฟกต์บนอาวุธที่กำลังเล่น
    int fxHandle, fxEntity, fxIdxApplied = -1, fxScaleApplied = -1;
    int failUntil;
    bool failMsgShown;

    int nextHit, nextMelee, nextLoad;

    public WeaponFX()
    {
        Load();
        Tick += OnTick;
    }

    // ---------- บันทึก / โหลดค่า ----------
    void Save()
    {
        try
        {
            File.WriteAllText(CFG, weaponIdx + "," + weaponScaleIdx + "," + hitIdx + "," + hitScaleIdx);
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

    // ---------- ตัวช่วย ----------
    bool Pressed(int vk)
    {
        bool d = (GetAsyncKeyState(vk) & 0x8000) != 0;
        bool r = d && !prev[vk];
        prev[vk] = d;
        return r;
    }

    static bool PadJust(int c)
    {
        return Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, c);
    }

    static void Msg(string t)
    {
        GTA.UI.Screen.ShowSubtitle(t, 1500);
    }

    static bool AssetLoaded(string asset)
    {
        return Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, asset);
    }

    static void Request(string asset)
    {
        if (asset != "") Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, asset);
    }

    static void DrawText(string s, float x, float y, float scale, int r, int g, int b, int a)
    {
        Function.Call(Hash.SET_TEXT_FONT, 0);
        Function.Call(Hash.SET_TEXT_SCALE, 0f, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, a);
        Function.Call(Hash.SET_TEXT_CENTRE, false);
        Function.Call(Hash.SET_TEXT_DROP_SHADOW);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, s);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y, 0);
    }

    void StopWeaponFx()
    {
        if (fxHandle != 0)
        {
            Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, fxHandle, false);
            Function.Call(Hash.REMOVE_PARTICLE_FX, fxHandle, false);
            fxHandle = 0;
        }
        fxEntity = 0;
        fxIdxApplied = -1;
        fxScaleApplied = -1;
    }

    // ---------- เอฟเฟกต์บนอาวุธ (ติดที่ตัวอาวุธ อยู่ตลอด) ----------
    void UpdateWeaponFx(Ped p, int now)
    {
        if (weaponIdx == 0 || p.IsDead)
        {
            StopWeaponFx();
            return;
        }

        // ตัวอาวุธในมือ ถ้าไม่มี (เช่น มือเปล่า) = ไม่แสดงเอฟเฟกต์
        int ent = Function.Call<int>(Hash.GET_CURRENT_PED_WEAPON_ENTITY_INDEX, p, 0);
        if (ent == 0 || ent == p.Handle || !Function.Call<bool>(Hash.DOES_ENTITY_EXIST, ent))
        {
            StopWeaponFx();
            return;
        }

        // เอฟเฟกต์ยังเล่นอยู่และตรงกับที่เลือก = ไม่ต้องทำอะไร
        if (fxHandle != 0 && ent == fxEntity && fxIdxApplied == weaponIdx && fxScaleApplied == weaponScaleIdx
            && Function.Call<bool>(Hash.DOES_PARTICLE_FX_LOOPED_EXIST, fxHandle))
            return;

        // ติดไม่สำเร็จเมื่อกี้ รอสักพักค่อยลองใหม่
        if (now < failUntil) return;

        StopWeaponFx();

        Fx f = WeaponList[weaponIdx];
        if (!AssetLoaded(f.Asset)) { Request(f.Asset); return; }

        Function.Call(Hash.USE_PARTICLE_FX_ASSET, f.Asset);
        fxHandle = Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_ON_ENTITY, f.Name, ent,
            0f, 0f, 0f, 0f, 0f, 0f, Scales[weaponScaleIdx], false, false, false);

        if (fxHandle == 0 || !Function.Call<bool>(Hash.DOES_PARTICLE_FX_LOOPED_EXIST, fxHandle))
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

    // ---------- เอฟเฟกต์ตอนโจมตีโดน (ภาพล้วนๆ) ----------
    void SpawnHit(Vector3 pos)
    {
        if (hitIdx == 0) return;
        Fx f = HitList[hitIdx];
        if (!AssetLoaded(f.Asset)) { Request(f.Asset); return; }
        Function.Call(Hash.USE_PARTICLE_FX_ASSET, f.Asset);
        Function.Call(Hash.START_PARTICLE_FX_NON_LOOPED_AT_COORD, f.Name,
            pos.X, pos.Y, pos.Z, 0f, 0f, 0f, Scales[hitScaleIdx], false, false, false);
    }

    void UpdateHitFx(Ped p, int now)
    {
        if (hitIdx == 0) return;

        uint w = Function.Call<uint>(Hash.GET_SELECTED_PED_WEAPON, p);
        uint grp = Function.Call<uint>(Hash.GET_WEAPONTYPE_GROUP, w);
        bool melee = grp == 3566412244u || grp == 2685387236u;   // อาวุธประชิด / มือเปล่า

        if (!melee)
        {
            // ยิง: ขึ้นตรงจุดที่กระสุนโดน
            if (p.IsShooting && now >= nextHit)
            {
                nextHit = now + 80;
                OutputArgument oa = new OutputArgument();
                if (Function.Call<bool>(Hash.GET_PED_LAST_WEAPON_IMPACT_COORD, p, oa))
                    SpawnHit(oa.GetResult<Vector3>());
            }
        }
        else if (now >= nextMelee && Function.Call<bool>(Hash.IS_PED_IN_MELEE_COMBAT, p))
        {
            // หมัด/อาวุธประชิด: ขึ้นที่ตัวคนที่โดน
            nextMelee = now + 100;
            Ped[] peds = World.GetNearbyPeds(p, 6f);
            for (int i = 0; i < peds.Length; i++)
            {
                Ped t = peds[i];
                if (t == null || !t.Exists() || t == p) continue;
                if (Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, t, p, true))
                {
                    Vector3 pos = Function.Call<Vector3>(Hash.GET_PED_BONE_COORDS, t, 24818, 0f, 0f, 0f);
                    SpawnHit(pos);
                    Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, t);
                }
            }
        }
    }

    // ---------- เมนู ----------
    void Change(int dir)
    {
        if (sel == 0)
        {
            weaponIdx = (weaponIdx + dir + WeaponList.Length) % WeaponList.Length;
            failUntil = 0;
            failMsgShown = false;
            Request(WeaponList[weaponIdx].Asset);
        }
        else if (sel == 1)
        {
            weaponScaleIdx = (weaponScaleIdx + dir + Scales.Length) % Scales.Length;
        }
        else if (sel == 2)
        {
            hitIdx = (hitIdx + dir + HitList.Length) % HitList.Length;
            Request(HitList[hitIdx].Asset);
        }
        else if (sel == 3)
        {
            hitScaleIdx = (hitScaleIdx + dir + Scales.Length) % Scales.Length;
        }
        Save();
    }

    string RowText(int i)
    {
        switch (i)
        {
            case 0: return "Weapon Effect:  < " + WeaponList[weaponIdx].Label + " >";
            case 1: return "Weapon Scale:  
