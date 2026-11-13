using System;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;

public class ThaiMoves : Script
{
    Keys HealKey = Keys.H, SlideKey = Keys.X, ArmorKey = Keys.B;
    int PadHold = 44;     // RB
    int PadHeal = 174;    // DPad Left
    int PadSlide = 175;   // DPad Right
    int PadArmor = 173;   // DPad Down

    int HealAmount = 100;
    int ArmorAdd = 50;
    float SlideSpeed = 10f;
    int SlideTime = 1000;
    float LeanDeg = 0f;

    string SlideDict = "anim@arena@celeb@flat@solo@no_props@";
    string SlideClip = "slide_a_player_a";

    int healLeft, nextHeal, bounceUntil, slideEnd;
    Vector3 slideDir;

    public ThaiMoves()
    {
        Tick += OnTick;
        KeyDown += OnKeyDown;
    }

    static void Anim(Ped p, string dict, string clip, int ms, int flags)
    {
        Function.Call(Hash.REQUEST_ANIM_DICT, dict);
        int t = Game.GameTime + 1500;
        while (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dict) && Game.GameTime < t)
            Script.Yield();
        Function.Call(Hash.TASK_PLAY_ANIM, p, dict, clip, 8f, -8f, ms, flags, 0f, false, false, false);
    }

    static bool Pad(int c) { return Function.Call<bool>(Hash.IS_CONTROL_JUST_PRESSED, 0, c); }
    static bool PadHeld(int c) { return Function.Call<bool>(Hash.IS_CONTROL_PRESSED, 0, c); }

    void DoHeal(Ped p)
    {
        Anim(p, "mp_player_intdrink", "loop_bottle", 1500, 49);
        healLeft = HealAmount;
        nextHeal = Game.GameTime;
    }

    void DoArmor(Ped p)
    {
        Anim(p, "clothingtie", "try_tie_neutral_a", 1200, 49);
        p.Armor = Math.Min(100, p.Armor + ArmorAdd);
        bounceUntil = Game.GameTime + 600;
    }

    void DoSlide(Ped p)
    {
        if (p.IsInAir || slideEnd > Game.GameTime) return;
        slideDir = p.ForwardVector;
        slideEnd = Game.GameTime + SlideTime;
        Anim(p, SlideDict, SlideClip, SlideTime, 0);
    }

    bool Ok(Ped p)
    {
        return p != null && p.Exists() && !p.IsDead && !p.IsInVehicle();
    }

    void OnKeyDown(object s, KeyEventArgs e)
    {
        Ped p = Game.Player.Character;
        if (!Ok(p)) return;
        if (e.KeyCode == HealKey) DoHeal(p);
        else if (e.KeyCode == ArmorKey) DoArmor(p);
        else if (e.KeyCode == SlideKey) DoSlide(p);
    }

    void OnTick(object s, EventArgs e)
    {
        Ped p = Game.Player.Character;
        if (p == null || !p.Exists()) return;

        if (Ok(p) && PadHeld(PadHold))
        {
            if (Pad(PadHeal)) DoHeal(p);
            else if (Pad(PadSlide)) DoSlide(p);
            else if (Pad(PadArmor)) DoArmor(p);
        }

        if (healLeft > 0 && Game.GameTime >= nextHeal)
        {
            int step = Math.Min(10, healLeft);
            p.Health = Math.Min(p.MaxHealth, p.Health + step);
            healLeft -= step;
            nextHeal = Game.GameTime + 60;
            bounceUntil = Game.GameTime + 400;
            if (p.Health >= p.MaxHealth) healLeft = 0;
        }

        if (Game.GameTime < bounceUntil)
        {
            float k = (bounceUntil - Game.GameTime) / 400f;
            Function.Call(Hash.SET_TEXT_FONT, 4);
            Function.Call(Hash.SET_TEXT_SCALE, 0f, 0.6f + 0.4f * k);
            Function.Call(Hash.SET_TEXT_COLOUR, 80, 255, 120, 255);
            Function.Call(Hash.SET_TEXT_OUTLINE);
            Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
            Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, "+HP / +ARMOR");
            Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, 0.45f, 0.80f - 0.03f * k);
        }

        if (slideEnd > Game.GameTime)
        {
            float frac = (slideEnd - Game.GameTime) / (float)SlideTime;
            Vector3 v = slideDir * (SlideSpeed * frac);
            v.Z = p.Velocity.Z;
            p.Velocity = v;
            if (LeanDeg != 0f)
                p.Rotation = new Vector3(LeanDeg, 0f, p.Rotation.Z);
        }
        else if (slideEnd != 0)
        {
            slideEnd = 0;
            p.Task.ClearAll();
        }
    }
}
