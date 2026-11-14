using System;
using GTA;
using GTA.Native;

public class NoVehicleSlowmo : Script
{
    // ===== ปรับค่าตรงนี้ได้ =====
    bool DisableStuntJumps = true;   // true = ปิดสตั๊นท์จั๊มพ์ของเกมไปเลย (ไม่มีสโลว์/กล้องพิเศษ)
    bool ForceNormalSpeed = true;    // true = รถลอยแล้วบังคับเวลาเป็นปกติ
    // ============================

    public NoVehicleSlowmo()
    {
        Tick += OnTick;
    }

    void OnTick(object sender, EventArgs e)
    {
        try
        {
            if (DisableStuntJumps)
                Function.Call(Hash.SET_STUNT_JUMPS_CAN_TRIGGER, false);

            Ped p = Game.Player.Character;
            if (p == null || !p.Exists() || !p.IsInVehicle()) return;

            if (Function.Call<bool>(Hash.IS_STUNT_JUMP_IN_PROGRESS))
                Function.Call(Hash.CANCEL_STUNT_JUMP);

            Vehicle v = p.CurrentVehicle;
            if (ForceNormalSpeed && v != null && v.Exists() && v.IsInAir && Game.TimeScale < 1f)
                Game.TimeScale = 1f;
        }
        catch (Exception) { }
    }
}
