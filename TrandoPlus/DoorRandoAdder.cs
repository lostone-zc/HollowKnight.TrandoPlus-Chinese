﻿using RandomizerCore;
using RandomizerCore.Extensions;
using RandomizerCore.Randomization;
using RandomizerMod.RandomizerData;
using RandomizerMod.RC;
using RandomizerMod.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TrandoPlus
{
    public static class DoorRandoAdder
    {
        public static void Hook()
        {
            RequestBuilder.OnUpdate.Subscribe(-750f, CaptureRandomizedDoorTransitions);
            RequestBuilder.OnUpdate.Subscribe(-750f, SetDoorRandoForItemRando);
            RequestBuilder.OnUpdate.Subscribe(-750f, SetDoorRandoForAreaRando);

            RequestBuilder.OnUpdate.Subscribe(100f, ApplyAreaDoorConstraint);
        }

        public static readonly HashSet<string> DoorRandoTransitions = new();

        private static void ApplyAreaDoorConstraint(RequestBuilder rb)
        {
            if (!TrandoPlus.GS.RandomizeDoors) return;
            if (!TrandoPlus.GS.AreaDoorNonInteraction) return;
            
            foreach (GroupBuilder gb in rb.EnumerateTransitionGroups())
            {
                if (gb.strategy is DefaultGroupPlacementStrategy dgps)
                {
                    dgps.Constraints += AreaDoorConstraint;
                }
            }

            static bool AreaDoorConstraint(IRandoItem item, IRandoLocation loc)
            {
                if (DoorRandoTransitions.Contains(item.Name) && !isDoor(loc.Name)) return false;
                if (DoorRandoTransitions.Contains(loc.Name) && !isDoor(item.Name)) return false;

                return true;
            }

            static bool isDoor(string trans)
            {
                TransitionDef def = Data.GetTransitionDef(trans);
                if (def.Direction == TransitionDirection.Door) return true;
                if (Data.GetTransitionDef(def.VanillaTarget).Direction == TransitionDirection.Door) return true;

                return false;
            }
        }


        private static void CaptureRandomizedDoorTransitions(RequestBuilder rb)
        {
            DoorRandoTransitions.Clear();

            if (!TrandoPlus.GS.RandomizeDoors) return;

            foreach (VanillaDef def in new List<VanillaDef>(rb.Vanilla.SelectMany(x => x.Value)))
            {
                if ((Data.IsTransition(def.Item) && Data.GetTransitionDef(def.Item).Direction == TransitionDirection.Door)
                    || (Data.IsTransition(def.Location) && Data.GetTransitionDef(def.Location).Direction == TransitionDirection.Door))
                {
                    DoorRandoTransitions.Add(def.Item);
                    DoorRandoTransitions.Add(def.Location);
                    rb.RemoveFromVanilla(def);
                }
            }
        }

        private static void SetDoorRandoForAreaRando(RequestBuilder rb)
        {
            if (!TrandoPlus.GS.RandomizeDoors) return;

            TransitionSettings ts = rb.gs.TransitionSettings;
            if (ts.Mode == TransitionSettings.TransitionMode.RoomRandomizer
                || ts.Mode == TransitionSettings.TransitionMode.None)
            {
                return;
            }

            StageBuilder sb = rb.Stages.First(x => x.label == RBConsts.MainTransitionStage);

            GroupBuilder builder = null;

            if (ts.TransitionMatching == TransitionSettings.TransitionMatchingSetting.NonmatchingDirections)
            {
                builder = sb.Get(RBConsts.TwoWayGroup);

                ((SelfDualTransitionGroupBuilder)builder).Transitions.AddRange(DoorRandoTransitions);
            }
            else
            {
                builder = sb.Get(RBConsts.InLeftOutRightGroup);

                List<string> lefts = DoorRandoTransitions.Where(x => Data.GetTransitionDef(x).Direction == TransitionDirection.Left).ToList();
                List<string> rights = DoorRandoTransitions.Where(x => Data.GetTransitionDef(x).Direction == TransitionDirection.Right).ToList();
                List<string> doors = DoorRandoTransitions.Where(x => Data.GetTransitionDef(x).Direction == TransitionDirection.Door).ToList();
                rb.rng.PermuteInPlace(doors);

                foreach (string doorTrans in doors)
                {
                    if (lefts.Count > rights.Count)
                    {
                        rights.Add(doorTrans);
                    }
                    else
                    {
                        lefts.Add(doorTrans);
                    }
                }

                ((SymmetricTransitionGroupBuilder)builder).Group1.AddRange(rights);
                ((SymmetricTransitionGroupBuilder)builder).Group2.AddRange(lefts);
            }

            if (ts.Coupled)
            {
                ((DefaultGroupPlacementStrategy)builder.strategy).Constraints += (item, loc) => ApplyStartLocationFilter(rb.gs.StartLocationSettings.StartLocation, true, item, loc);
            }

            bool MatchedTryResolveGroup(RequestBuilder rb, string item, RequestBuilder.ElementType type, out GroupBuilder gb)
            {
                if ((type == RequestBuilder.ElementType.Transition || Data.IsTransition(item))
                    && (DoorRandoTransitions.Contains(item)))
                {
                    gb = builder;
                    return true;
                }
                gb = default;
                return false;
            }
            rb.OnGetGroupFor.Subscribe(-1000f, MatchedTryResolveGroup);
        }

        private static void SetDoorRandoForItemRando(RequestBuilder rb)
        {
            if (!TrandoPlus.GS.RandomizeDoors) return;

            TransitionSettings ts = rb.gs.TransitionSettings;
            if (ts.Mode != TransitionSettings.TransitionMode.None)
            {
                return;
            }

            // Insert stage at the start because it's a lot more restricted than the item placements
            // Treat matched as Door <--> Non-Door because that's what matched means in this context
            StageBuilder sb = rb.InsertStage(0, Consts.DoorRandoTransitionStage);

            GroupBuilder builder = null;

            if (ts.TransitionMatching == TransitionSettings.TransitionMatchingSetting.NonmatchingDirections)
            {
                builder = new SelfDualTransitionGroupBuilder()
                {
                    label = Consts.DoorRandoGroup,
                    stageLabel = Consts.DoorRandoTransitionStage,
                    coupled = ts.Coupled,
                };

                ((SelfDualTransitionGroupBuilder)builder).Transitions.AddRange(DoorRandoTransitions);
            }
            else
            {
                builder = new SymmetricTransitionGroupBuilder()
                {
                    label = Consts.ForwardDoorRando,
                    reverseLabel = Consts.ReverseDoorRando,
                    coupled = ts.Coupled,
                    stageLabel = Consts.DoorRandoTransitionStage
                };

                List<string> nonDoors = DoorRandoTransitions.Where(x => Data.GetTransitionDef(x).Direction != TransitionDirection.Door).ToList();
                List<string> doors = DoorRandoTransitions.Where(x => Data.GetTransitionDef(x).Direction == TransitionDirection.Door).ToList();

                ((SymmetricTransitionGroupBuilder)builder).Group1.AddRange(doors);
                ((SymmetricTransitionGroupBuilder)builder).Group2.AddRange(nonDoors);
            }

            DefaultGroupPlacementStrategy strategy = rb.gs.ProgressionDepthSettings.GetTransitionPlacementStrategy();
            if (ts.Coupled)
            {
                strategy.Constraints += (item, loc) => ApplyStartLocationFilter(rb.gs.StartLocationSettings.StartLocation, false, item, loc);
            }
            builder.strategy = strategy;
            sb.Add(builder);

            bool MatchedTryResolveGroup(RequestBuilder rb, string item, RequestBuilder.ElementType type, out GroupBuilder gb)
            {
                if ((type == RequestBuilder.ElementType.Transition || Data.IsTransition(item))
                    && (DoorRandoTransitions.Contains(item)))
                {
                    gb = builder;
                    return true;
                }
                gb = default;
                return false;
            }
            rb.OnGetGroupFor.Subscribe(-1000f, MatchedTryResolveGroup);
        }

        // If we start inside a mound, forbid these targets in item rando
        public static readonly HashSet<string> ItemForbiddenStartTransitions = new()
        {
            "Fungus1_15[door1]",
            "White_Palace_11[door2]",
            "Deepnest_10[door1]",
            "Deepnest_10[door2]",
        };
        // If we start inside a mound, forbid these targets in item/area rando
        public static readonly HashSet<string> AreaForbiddenStartTransitions = new()
        {
            "Fungus3_48[door1]",
            "Deepnest_39[door1]",
            "Deepnest_East_06[door1]",
            "Deepnest_East_14[door1]",
            "Fungus1_15[door1]",
            "GG_Waterways[door1]",
            "Waterways_07[door1]",
            "Ruins1_04[door1]",
            "RestingGrounds_12[door1]"
        };
        public static bool ApplyStartLocationFilter(string start, bool area, IRandoItem item, IRandoLocation location)
        {
            string startTrans = Data.GetStartDef(start).Transition;
            if (Data.GetTransitionDef(Data.GetTransitionDef(startTrans).VanillaTarget).Direction != TransitionDirection.Door)
            {
                return true;
            }
            if (location.Name == startTrans)
            {
                if (AreaForbiddenStartTransitions.Contains(item.Name)) return false;
                if (!area && ItemForbiddenStartTransitions.Contains(item.Name)) return false;
            }
            else if (item.Name == startTrans)
            {
                if (AreaForbiddenStartTransitions.Contains(location.Name)) return false;
                if (!area && ItemForbiddenStartTransitions.Contains(location.Name)) return false;
            }
            return true;
        }
    }
}