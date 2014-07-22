﻿#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Policy;
using LeagueSharp;
using LeagueSharp.Common;
using Leaguesharp.Common;
using SharpDX;
using Color = System.Drawing.Color;
using GamePath = System.Collections.Generic.List<SharpDX.Vector2>;

#endregion

namespace Evade
{
    internal class Program
    {
        public static List<Skillshot> DetectedSkillshots = new List<Skillshot>();

        private static bool _evading;
        public static bool Evading
        {
            get { return _evading; }//
            set
            {
                _evading = value;
                if (value == false)
                {
                    if (AfterEvadePoint.IsValid())
                    {
                        ObjectManager.Player.IssueOrder(GameObjectOrder.MoveTo, AfterEvadePoint.To3D());
                        AfterEvadePoint = new Vector2();
                    }
                }
            }
        }
        private static Vector2 _evadePoint = new Vector2();

        public static Vector2 EvadePoint
        {
            get { return _evadePoint; }
            set
            {
                if(value.IsValid())
                    ObjectManager.Player.SendMovePacket(value);
                _evadePoint = value;
            }
        }
        public static bool NoSolutionFound = false;

        public static Vector2 EvadeToPoint = new Vector2();

        public static Vector2 AfterEvadePoint = new Vector2();
        public static Vector2 CutPathPoint = new Vector2();

        private static void Main(string[] args)
        {
            if (Game.Mode == GameMode.Running)
                Game_OnGameStart(new EventArgs());

            Game.OnGameStart += Game_OnGameStart;
        }

        private static bool IsSpellShielded(Obj_AI_Hero unit)
        {


            if (ObjectManager.Player.HasBuffOfType(BuffType.SpellShield))
                return true;

            if (ObjectManager.Player.HasBuffOfType(BuffType.SpellImmunity))
                return true;

            //Sivir E
            if (unit.LastCastedSpellName() == "SivirE" && (Environment.TickCount - unit.LastCastedSpellT()) < 250)
                return true;

            //Morganas E
            if (unit.LastCastedSpellName() == "BlackShield" && (Environment.TickCount - unit.LastCastedSpellT()) < 250)
                return true;

            //Nocturnes E
            if (unit.LastCastedSpellName() == "BLAHLBAHB" && (Environment.TickCount - unit.LastCastedSpellT()) < 250)
                return true;


            return false;
        }

        private static void Game_OnGameStart(EventArgs args)
        {
            //Create the menu to allow the user to change the config.
            Config.CreateMenu();

            //Add the game events.
            Game.OnGameUpdate += Game_OnOnGameUpdate;
            Game.OnGameSendPacket += Game_OnGameSendPacket;

            //Set up the OnDetectSkillshot Event.
            SkillshotDetector.OnDetectSkillshot += OnDetectSkillshot;

            //For skillshot drawing.
            Drawing.OnDraw += Drawing_OnDraw;
        }

        private static void OnDetectSkillshot(Skillshot skillshot)
        {
            //Check if the skillshot is already added.
            var alreadyAdded = false;

            foreach (var item in DetectedSkillshots)
            {
                if (item.SpellData.SpellName == skillshot.SpellData.SpellName && item.End.Distance(skillshot.End) <= 50)
                {
                    alreadyAdded = true; 
                }
            }

            
            //Check if the skillshot is from an ally.
            if (skillshot.Unit.Team == ObjectManager.Player.Team && !Config.TestOnAllies)
                return;
            

            //Add the skillshot to the detected skillshot list.
            if (!alreadyAdded)
            {

                //Multiple skillshots like twisted fate Q.
                if (skillshot.DetectionType == DetectionType.ProcessSpell)
                {
                    if (skillshot.SpellData.MultipleNumber != -1)
                    {
                        var originalDirection = skillshot.Direction;

                        for (int i = -(skillshot.SpellData.MultipleNumber - 1) / 2; i <= (skillshot.SpellData.MultipleNumber - 1) / 2; i++)
                        {
                            var end = skillshot.Start + skillshot.SpellData.Range * originalDirection.Rotated(skillshot.SpellData.MultipleAngle * i);
                            var skillshotToAdd = new Skillshot(skillshot.DetectionType, skillshot.SpellData, skillshot.StartTick, skillshot.Start, end, skillshot.Unit);
                            
                            DetectedSkillshots.Add(skillshotToAdd);
                        }
                        return;
                    }

                    if (skillshot.SpellData.SpellName == "UFSlash")
                    {
                        skillshot.SpellData.MissileSpeed = 1500 + (int) skillshot.Unit.MoveSpeed;
                    }

                    if (skillshot.SpellData.Invert)
                    {
                        var newDirection =  -(skillshot.End - skillshot.Start).Normalized();
                        var end = skillshot.Start + newDirection * skillshot.Start.Distance(skillshot.End);
                        var skillshotToAdd = new Skillshot(skillshot.DetectionType, skillshot.SpellData, skillshot.StartTick, skillshot.Start, end, skillshot.Unit);
                        DetectedSkillshots.Add(skillshotToAdd);
                        return;
                    }

                    if (skillshot.SpellData.Centered)
                    {
                        var start = skillshot.Start - skillshot.Direction * skillshot.SpellData.Range;
                        var end = skillshot.Start + skillshot.Direction * skillshot.SpellData.Range;
                        var skillshotToAdd = new Skillshot(skillshot.DetectionType, skillshot.SpellData, skillshot.StartTick, start, end, skillshot.Unit);
                        DetectedSkillshots.Add(skillshotToAdd);
                        return;
                    }
                }

                
                //Dont allow fow detection.
                if (skillshot.SpellData.DisableFowDetection && skillshot.DetectionType == DetectionType.RecvPacket) return;

                DetectedSkillshots.Add(skillshot);
            }
                
        }

        private static void Game_OnOnGameUpdate(EventArgs args)
        {
            //Remove the detected skillshots that have expired.
            DetectedSkillshots.RemoveAll(skillshot => !skillshot.IsActive());

            //Trigger OnGameUpdate on each skillshot.
            foreach (var skillshot in DetectedSkillshots)
            {
                skillshot.Game_OnGameUpdate();
            }

            //Avoid sending move/cast packets while dead.
            if (ObjectManager.Player.IsDead) return;

            //Spell Shielded
            if (IsSpellShielded(ObjectManager.Player))
                return;

            if (CutPathPoint.IsValid() && !Evading)
            {
                var pathToPoint = ObjectManager.Player.GetPath(CutPathPoint.To3D());
                if (IsSafePath(pathToPoint.To2DList(), 250).IsSafe)
                {
                    ObjectManager.Player.IssueOrder(GameObjectOrder.MoveTo, CutPathPoint.To3D());
                    //ObjectManager.Player.SendMovePacket(CutPathPoint);
                    //CutPathPoint = new Vector2();
                    AfterEvadePoint = new Vector2();
                }
            }

            NoSolutionFound = false;

            var currentPath = ObjectManager.Player.GetWaypoints();
            var safeResult = IsSafe(ObjectManager.Player.ServerPosition.To2D());
            var safePath = IsSafePath(currentPath, 100);

            //Continue evading
            if (Evading && IsSafe(EvadePoint).IsSafe)
            {
                if (safeResult.IsSafe)
                {
                    //We are safe, stop evading.
                    Evading = false;
                }
                else
                {
                    ObjectManager.Player.SendMovePacket(EvadePoint);
                    return;
                }
            }
                //Stop evading if the point is not safe.
            else if (Evading)
            {
                Evading = false;
            }

            //The path is not safe.
            if (!safePath.IsSafe)
            {
                //Inside the danger polygon.
                if (!safeResult.IsSafe)
                {
                    //Search for an evade point:
                    //Game.PrintChat("Need to evade!");
                    TryToEvade(safeResult.SkillshotList, /*EvadeToPoint*/Game.CursorPos.To2D());
                    if(Evading)
                        AfterEvadePoint = currentPath[currentPath.Count - 1];
                }
                    //Outside the danger polygon.
                else
                {
                    //Stop at the edge of the skillshot.
                    ObjectManager.Player.SendMovePacket(safePath.Intersection.Point);
                    CutPathPoint = currentPath[currentPath.Count - 1];
                }
            }
        }

        /// <summary>
        /// Used to block the movement to avoid entering in dangerous areas.
        /// </summary>
        private static void Game_OnGameSendPacket(GamePacketEventArgs args)
        {
            //Move Packet
            if (args.PacketData[0] == Packet.C2S.Move.Header)
            {
                CutPathPoint = new Vector2();
                //Don't block the movement packets if cant find an evade point.
                if (NoSolutionFound) return;

                //Spell Shielded
                if (IsSpellShielded(ObjectManager.Player))
                    return;

                var decodedPacket = Packet.C2S.Move.Decoded(args.PacketData);

                if (decodedPacket.MoveType == 2)
                {
                    EvadeToPoint.X = decodedPacket.X;
                    EvadeToPoint.Y = decodedPacket.Y;
                }

                var myPath =
                    ObjectManager.Player.GetPath(new Vector3(decodedPacket.X, decodedPacket.Y,
                        ObjectManager.Player.ServerPosition.Z)).To2DList();
                var safeResult = IsSafe(ObjectManager.Player.ServerPosition.To2D());
                var safePath = IsSafePath(myPath, Config.EvadingRouteChangeTimeOffset);

                //If we are evading:
                if (Evading || !safeResult.IsSafe)
                {
                    if (decodedPacket.MoveType == 2)
                    {
                        AfterEvadePoint = new Vector2(decodedPacket.X, decodedPacket.Y);
                        if (Evading && Environment.TickCount - Config.LastEvadePointChangeT > Config.EvadePointChangeInterval)
                        {
                            //Update the evade point to the closest one:
                            var points = Evader.GetEvadePoints(-1, 0, false, true);
                            if (points.Count > 0)
                            {
                                var to = new Vector2(decodedPacket.X, decodedPacket.Y);
                                EvadePoint = to.Closest(points);
                                Evading = true;
                                Config.LastEvadePointChangeT = Environment.TickCount;
                            }
                        }

                        //If the path is safe let the user follow it.
                        if (safePath.IsSafe && IsSafe(myPath[myPath.Count - 1]).IsSafe && decodedPacket.MoveType == 2)
                        {
                            EvadePoint = myPath[myPath.Count - 1];
                            Evading = true;
                        }

                        CutPathPoint = new Vector2(decodedPacket.X, decodedPacket.Y);
                    }
                    
                    //Block the packets if we are evading or not safe.
                    args.Process = false;
                    return;
                }

                //Not evading, outside the skillshots.
                //The path is not safe, stop in the intersection point.
                if (!safePath.IsSafe && decodedPacket.MoveType != 3)
                {
                    if (safePath.Intersection.Valid)
                    {
                        ObjectManager.Player.SendMovePacket(safePath.Intersection.Point);
                    }
                    CutPathPoint = new Vector2(decodedPacket.X, decodedPacket.Y);
                    args.Process = false;
                }

                //AutoAttacks.
                if (!safePath.IsSafe && decodedPacket.MoveType == 3)
                {
                    var target = ObjectManager.GetUnitByNetworkId<Obj_AI_Base>(decodedPacket.TargetNetworkId);
                    if (target != null && target.IsValid && target.IsVisible)
                    {
                        //Out of attack range.
                        if (ObjectManager.Player.ServerPosition.To2D().Distance(target.ServerPosition) >
                            ObjectManager.Player.AttackRange + ObjectManager.Player.BoundingRadius +
                            target.BoundingRadius)
                        {
                            if (safePath.Intersection.Valid)
                            {
                                ObjectManager.Player.SendMovePacket(safePath.Intersection.Point);
                            }
                            args.Process = false;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if the point is not inside the detected skillshots.
        /// </summary>
        public static IsSafeResult IsSafe(Vector2 point)
        {
            var result = new IsSafeResult();
            result.SkillshotList = new List<Skillshot>();

            foreach (var skillshot in DetectedSkillshots)
            {
                if (skillshot.Evade() && skillshot.IsDanger(point))
                {
                    result.SkillshotList.Add(skillshot);
                }
            }

            result.IsSafe = (result.SkillshotList.Count == 0);

            return result;
        }

        /// <summary>
        /// Returns if the unit will get hit by skillshots taking the path.
        /// </summary>
        public static SafePathResult IsSafePath(GamePath path, int timeOffset, int speed = -1, int delay = 0,
            Obj_AI_Base unit = null)
        {
            var IsSafe = true;
            var intersections = new List<FoundIntersection>();
            var intersection = new FoundIntersection();

            foreach (var skillshot in DetectedSkillshots)
            {
                if (skillshot.Evade())
                {
                    var sResult = skillshot.IsSafePath(path, timeOffset, speed, delay, unit);
                    IsSafe = (IsSafe) ? sResult.IsSafe : false;

                    if (sResult.Intersection.Valid)
                        intersections.Add(sResult.Intersection);
                }
            }

            //Return the first intersection
            if (!IsSafe)
            {
                var sortedList = intersections.OrderBy(o => o.Distance).ToList();

                return new SafePathResult(false, sortedList.Count > 0 ? sortedList[0] : intersection);
            }

            return new SafePathResult(IsSafe, intersection);
        }

        /// <summary>
        /// Returns if you can blink to the point without being hit.
        /// </summary>
        public static bool IsSafeToBlink(Vector2 point, int timeOffset, int delay)
        {
            foreach (var skillshot in DetectedSkillshots)
            {
                if (skillshot.Evade())
                    if (!skillshot.IsSafeToBlink(point, timeOffset, delay))
                        return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true if some detected skillshot is about to hit the unit.
        /// </summary>
        public static bool IsAboutToHit(Obj_AI_Base unit, int time)
        {
            time += 100;
            foreach (var skillshot in DetectedSkillshots)
            {
                if (skillshot.Evade())
                    if (skillshot.IsAboutToHit(time, unit))
                        return true;
            }
            return false;
        }

        private static void TryToEvade(List<Skillshot> HitBy, Vector2 to)
        {
            var dangerLevel = 0;

            foreach (var skillshot in HitBy)
            {
                dangerLevel = Math.Max(dangerLevel, skillshot.GetValue<Slider>("DangerLevel").Value);
            }

            foreach (var evadeSpell in EvadeSpellDatabase.Spells)
            {
                if (evadeSpell.Enabled && evadeSpell.DangerLevel <= dangerLevel)
                {
                    //SpellShields
                    if (evadeSpell.IsSpellShield &&
                        ObjectManager.Player.Spellbook.CanUseSpell(evadeSpell.Slot) == SpellState.Ready)
                    {
                        if (IsAboutToHit(ObjectManager.Player, evadeSpell.Delay + 300))
                        {
                            ObjectManager.Player.Spellbook.CastSpell(evadeSpell.Slot, EvadePoint.To3D());
                        }

                        //Let the user move freely inside the skillshot.
                        NoSolutionFound = true;
                        return;
                    }

                    //Walking
                    if (evadeSpell.Name == "Walking")
                    {
                        var points = Evader.GetEvadePoints();
                        if (points.Count > 0)
                        {
                            EvadePoint = to.Closest(points);
                            Evading = true;
                            return;
                        }
                    }

                    //Dashes
                    if (evadeSpell.IsDash && ObjectManager.Player.Spellbook.CanUseSpell(evadeSpell.Slot) == SpellState.Ready)
                    {
                        //Targetted dashes
                        if (evadeSpell.IsTargetted)
                        {
                            //Todo.
                        }

                        //Skillshot type dashes.
                        else
                        {
                            var points = Evader.GetEvadePoints(evadeSpell.Speed, evadeSpell.Delay, false);

                            // Remove the points out of range
                            points.RemoveAll(item => item.Distance(ObjectManager.Player.ServerPosition) > evadeSpell.MaxRange);

                            //If the spell has a fixed range (Vaynes Q), calculate the real dashing location. TODO: take into account walls in the future.
                            if (evadeSpell.FixedRange)
                            {
                                for (int i = 0; i < points.Count; i++)
                                {
                                    points[i] = ObjectManager.Player.ServerPosition.To2D() +
                                                evadeSpell.MaxRange*
                                                (points[i] - ObjectManager.Player.ServerPosition.To2D()).Normalized();
                                }
                            }

                            if (points.Count > 0)
                            {
                                EvadePoint = to.Closest(points);
                                Evading = true;
                                ObjectManager.Player.Spellbook.CastSpell(evadeSpell.Slot, EvadePoint.To3D());
                                return;
                            }
                        }
                    }

                    //Blinks
                    if (evadeSpell.IsBlink &&
                        (
                        (
                        evadeSpell.IsSummonerSpell && ObjectManager.Player.SummonerSpellbook.CanUseSpell(evadeSpell.Slot) == SpellState.Ready

                        )
                        ||

                        (!evadeSpell.IsSummonerSpell && ObjectManager.Player.Spellbook.CanUseSpell(evadeSpell.Slot) == SpellState.Ready
)
                        )
                        )
                    {
                        
                        //Targetted blinks
                        if (evadeSpell.IsTargetted)
                        {
                            //Todo.
                            Game.PrintChat("LOL");
                        }

                        //Skillshot type blinks.
                        else
                        {
                            
                            var points = Evader.GetEvadePoints(int.MaxValue, evadeSpell.Delay, true);
                            
                            // Remove the points out of range
                            points.RemoveAll(item => item.Distance(ObjectManager.Player.ServerPosition) > evadeSpell.MaxRange);

                            
                            //Dont blink just to the edge:
                            for (int i = 0; i < points.Count; i++)
                            {
                                var k = (int) (evadeSpell.MaxRange -
                                        ObjectManager.Player.ServerPosition.To2D().Distance(points[i]));

                                k = k - new Random(Environment.TickCount).Next(k);
                                var extended = points[i] + k * (points[i] - ObjectManager.Player.ServerPosition.To2D()).Normalized();
                                if (IsSafe(extended).IsSafe)
                                {
                                    points[i] = extended;
                                }
                            }

                            

                            if (points.Count > 0)
                            {
                                
                                if (IsAboutToHit(ObjectManager.Player, evadeSpell.Delay))
                                {
                                    EvadePoint = to.Closest(points);
                                    Evading = true;
                                    if (evadeSpell.IsSummonerSpell)
                                    {
                                        ObjectManager.Player.SummonerSpellbook.CastSpell(evadeSpell.Slot, EvadePoint.To3D());
                                    }
                                    else
                                    {
                                        ObjectManager.Player.Spellbook.CastSpell(evadeSpell.Slot, EvadePoint.To3D());
                                    }
                                }

                                //Let the user move freely inside the skillshot.
                                NoSolutionFound = true;
                                return;
                            }
                        }
                    }

                    //Invulnerabilities, Like Fizz's E
                    if (evadeSpell.IsInvulnerability &&
                        ObjectManager.Player.Spellbook.CanUseSpell(evadeSpell.Slot) == SpellState.Ready)
                    {
                        if (IsAboutToHit(ObjectManager.Player, evadeSpell.Delay + 300))
                        {
                            ObjectManager.Player.Spellbook.CastSpell(evadeSpell.Slot, EvadePoint.To3D());
                        }

                        //Let the user move freely inside the skillshot.
                        NoSolutionFound = true;
                        return;
                    }

                    //Zhonyas
                    if (evadeSpell.Name == "Zhonyas" && (Items.CanUseItem("ZhonyasHourglass")))
                    {
                        if (IsAboutToHit(ObjectManager.Player, 100))
                        {
                            Items.UseItem("ZhonyasHourglass");
                        }

                        //Let the user move freely inside the skillshot.
                        NoSolutionFound = true;

                        return;
                    }

                    //Shields
                    if (evadeSpell.IsShield &&
                        ObjectManager.Player.Spellbook.CanUseSpell(evadeSpell.Slot) == SpellState.Ready)
                    {
                        if (IsAboutToHit(ObjectManager.Player, evadeSpell.Delay + 300))
                        {
                            ObjectManager.Player.Spellbook.CastSpell(evadeSpell.Slot, EvadePoint.To3D());
                        }

                        //Let the user move freely inside the skillshot.
                        NoSolutionFound = true;
                        return;
                    }

                }
            }

            NoSolutionFound = true;
        }

        private static void Drawing_OnDraw(EventArgs args)
        {
            if (!Config.Menu.Item("EnableDrawings").GetValue<bool>()) return;
            var Border = Config.Menu.Item("Border").GetValue<Slider>().Value;
            //Draw the polygon for each skillshot.
            foreach (var skillshot in DetectedSkillshots)
            {
                skillshot.Draw(skillshot.Evade() ? Config.Menu.Item("EnabledColor").GetValue<Color>() : Config.Menu.Item("DisabledColor").GetValue<Color>(), Border);
            }

            if (Config.TestOnAllies)
            {
                var myPath = ObjectManager.Player.GetWaypoints();
                for (var i = 0; i < myPath.Count - 1; i++)
                {
                    var A = myPath[i];
                    var B = myPath[i + 1];
                    var SA = Drawing.WorldToScreen(A.To3D());
                    var SB = Drawing.WorldToScreen(B.To3D());
                    Drawing.DrawLine(SA[0], SA[1], SB[0], SB[1], 1, Color.White);
                }

                Drawing.DrawCircle(EvadePoint.To3D(), 300, Color.White);
            }
            return;
            foreach (var o in ObjectManager.Get<GameObject>())
            {
                Drawing.DrawCircle(o.Position, 100, Color.Wheat);
                if (Game.CursorPos.Distance(o.Position) < 100)
                {
                    var rpos = Drawing.WorldToScreen(o.Position);
                    Drawing.DrawText(rpos[0], rpos[1], Color.White, o.Name);
                    Console.WriteLine(o.Name);
                }
            }
        }

        public struct IsSafeResult
        {
            public bool IsSafe;
            public List<Skillshot> SkillshotList;
        }
    }
}