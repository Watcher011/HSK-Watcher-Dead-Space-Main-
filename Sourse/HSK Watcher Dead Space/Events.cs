// ============================================================
// Watcher — Чёрный маркер
// Совместимо с C# 7.3. using-директивы перенесены внутрь каждого namespace,
// поэтому части не влияют друг на друга (как и в отдельных файлах).
//
// Содержимое:
//   1. Падение маркера и вспышка Заразителей
//   2. Шёпот из пустоты
//   3. Десант CEC (с масштабированием от богатства колонии)
// ============================================================

// ------------------------------------------------------------
// Падение маркера и вспышка Заразителей  [split/BM_Events.cs]
// ------------------------------------------------------------
namespace HSK.Watcher.DeadSpace
{
    using System.Collections.Generic;
    using RimWorld;
    using UnityEngine;
    using Verse;
    using Verse.AI;

    // ------------------------------------------------------------------
    // Событие: в случайной точке карты происходит взрыв, на его месте
    // появляется чёрный маркер.
    // ------------------------------------------------------------------
    public class IncidentWorker_MarkerImpact : IncidentWorker
    {
        private const float ExplosionRadius = 9.9f;
        private const int MinEdgeDistance = 12;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null)
            {
                return false;
            }

            // Маркер на карте уже есть (в том числе «упакованный» для переноса) - не падаем второй.
            if (HasMarker(map))
            {
                return false;
            }

            IntVec3 cell;
            return TryFindImpactCell(map, out cell);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null)
            {
                return false;
            }

            IntVec3 cell;
            if (!TryFindImpactCell(map, out cell))
            {
                return false;
            }

            // Взрыв.
            GenExplosion.DoExplosion(cell, map, ExplosionRadius, DamageDefOf.Bomb, null);

            // На месте взрыва появляется чёрный маркер (мусор в его клетках исчезает).
            Thing marker = ThingMaker.MakeThing(HSK_MarkerDefOf.HSK_BlackMarker);
            GenSpawn.Spawn(marker, cell, map, Rot4.North);

            HSK_Letters.Send(def.defName, def.letterLabel, def.letterText, def.letterDef, new LookTargets(cell, map));
            return true;
        }

        private static bool HasMarker(Map map)
        {
            if (map.listerThings.ThingsOfDef(HSK_MarkerDefOf.HSK_BlackMarker).Count > 0)
            {
                return true;
            }

            // Маркер, снятый для переноса (MinifiedThing), в списке по def не виден.
            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                MinifiedThing minified = all[i] as MinifiedThing;
                if (minified != null && minified.InnerThing != null && minified.InnerThing.def == HSK_MarkerDefOf.HSK_BlackMarker)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindImpactCell(Map map, out IntVec3 cell)
        {
            return CellFinderLoose.TryFindRandomNotEdgeCellWith(
                MinEdgeDistance,
                delegate (IntVec3 c) { return IsGoodSpot(c, map); },
                map,
                out cell);
        }

        // Все клетки 3x2 свободны, проходимы и не под крышей.
        private static bool IsGoodSpot(IntVec3 center, Map map)
        {
            CellRect rect = GenAdj.OccupiedRect(center, Rot4.North, new IntVec2(3, 2));
            foreach (IntVec3 c in rect)
            {
                if (!c.InBounds(map) || !c.Standable(map) || c.Roofed(map) || c.GetEdifice(map) != null)
                {
                    return false;
                }
            }

            return true;
        }
    }

    // ------------------------------------------------------------------
    // Событие: если маркер изучен, из скопления трупов (на земле, в могилах,
    // саркофагах и т.п.) выбираются Заразители.
    // ------------------------------------------------------------------
    public class IncidentWorker_InfectorOutbreak : IncidentWorker
    {
        // Радиус скопления (клеток), минимум трупов в нём и максимум Заразителей.
        private const float ClusterRadius = 8f;
        private const int MinClusterSize = 3;
        private const int MaxInfectors = 5;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null)
            {
                return false;
            }

            // Нужен хотя бы один изученный маркер.
            if (!HasStudiedMarker(map))
            {
                return false;
            }

            IntVec3 center;
            int count;
            return TryFindCluster(map, out center, out count);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null)
            {
                return false;
            }

            IntVec3 center;
            int count;
            if (!TryFindCluster(map, out center, out count))
            {
                return false;
            }

            int infectors = Mathf.Clamp(count / 2, 1, MaxInfectors);
            for (int i = 0; i < infectors; i++)
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    HSK_MarkerDefOf.Infector,
                    Faction.OfEntities,
                    PawnGenerationContext.NonPlayer);
                Pawn infector = PawnGenerator.GeneratePawn(request);

                IntVec3 cell;
                if (!CellFinder.TryFindRandomCellNear(
                    center,
                    map,
                    4,
                    delegate (IntVec3 c) { return c.Standable(map) && c.GetEdifice(map) == null; },
                    out cell))
                {
                    cell = center;
                }

                GenSpawn.Spawn(infector, cell, map, Rot4.Random);
            }

            HSK_Letters.Send(def.defName, def.letterLabel, def.letterText, def.letterDef, new LookTargets(center, map));
            return true;
        }

        private static bool HasStudiedMarker(Map map)
        {
            List<Thing> things = map.listerThings.ThingsOfDef(HSK_MarkerDefOf.HSK_BlackMarker);
            for (int i = 0; i < things.Count; i++)
            {
                Building_BlackMarker m = things[i] as Building_BlackMarker;
                if (m != null && m.Studied)
                {
                    return true;
                }
            }

            return false;
        }

        // Ищет точку, вокруг которой больше всего трупов (учитываются и трупы в контейнерах).
        private static bool TryFindCluster(Map map, out IntVec3 center, out int count)
        {
            center = IntVec3.Invalid;
            count = 0;

            List<IntVec3> points = new List<IntVec3>();

            List<Thing> corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            for (int i = 0; i < corpses.Count; i++)
            {
                if (corpses[i].Spawned)
                {
                    points.Add(corpses[i].Position);
                }
            }

            List<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < buildings.Count; i++)
            {
                Building_Casket casket = buildings[i] as Building_Casket;
                if (casket != null && CorpseSearch.FindCorpseIn(casket) != null)
                {
                    points.Add(casket.Position);
                }
            }

            if (points.Count < MinClusterSize)
            {
                return false;
            }

            float radiusSquared = ClusterRadius * ClusterRadius;
            int best = 0;
            List<IntVec3> bestPoints = new List<IntVec3>();
            for (int i = 0; i < points.Count; i++)
            {
                int n = 0;
                for (int j = 0; j < points.Count; j++)
                {
                    if ((points[i] - points[j]).LengthHorizontalSquared <= radiusSquared)
                    {
                        n++;
                    }
                }

                if (n > best)
                {
                    best = n;
                    bestPoints.Clear();
                    bestPoints.Add(points[i]);
                }
                else if (n == best)
                {
                    bestPoints.Add(points[i]);
                }
            }

            if (best < MinClusterSize)
            {
                return false;
            }

            center = bestPoints.RandomElement();
            count = best;
            return true;
        }
    }
}

// ------------------------------------------------------------
// Шёпот из пустоты  [/mnt/attach/outputs/Watcher_UnitologyWhispers.cs]
// ------------------------------------------------------------
// HSK_UnitologyWhispers.cs
// Инцидент «Шёпот из пустоты»: каждый колонист получает СВОЮ негативную мысль-фразу
// из списка (47 фраз, 7 категорий, разная сила влияния на настроение).
// Фразы не повторяются между колонистами, пока список не исчерпан.
// C# 7.3.
namespace HSK.Watcher.DeadSpace
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using RimWorld;
    using Verse;

    public class WhisperEntry
    {
        public readonly string key;      // ключ перевода самой фразы
        public readonly string category; // Command / Statement / Identity / Cosmic / Flesh / Distortion / Ritual
        public readonly float mood;      // модуль штрафа к настроению

        public WhisperEntry(string key, string category, float mood)
        {
            this.key = key;
            this.category = category;
            this.mood = mood;
        }
    }

    public static class WhisperTable
    {
        public static readonly List<WhisperEntry> All = new List<WhisperEntry>
        {
            new WhisperEntry("HSK_Whisper_Command_1", "Command", 8),
            new WhisperEntry("HSK_Whisper_Command_2", "Command", 6),
            new WhisperEntry("HSK_Whisper_Command_3", "Command", 12),
            new WhisperEntry("HSK_Whisper_Command_4", "Command", 7),
            new WhisperEntry("HSK_Whisper_Command_5", "Command", 9),
            new WhisperEntry("HSK_Whisper_Command_6", "Command", 5),
            new WhisperEntry("HSK_Whisper_Command_7", "Command", 6),
            new WhisperEntry("HSK_Whisper_Command_8", "Command", 4),
            new WhisperEntry("HSK_Whisper_Statement_9", "Statement", 14),
            new WhisperEntry("HSK_Whisper_Statement_10", "Statement", 12),
            new WhisperEntry("HSK_Whisper_Statement_11", "Statement", 10),
            new WhisperEntry("HSK_Whisper_Statement_12", "Statement", 8),
            new WhisperEntry("HSK_Whisper_Statement_13", "Statement", 7),
            new WhisperEntry("HSK_Whisper_Statement_14", "Statement", 9),
            new WhisperEntry("HSK_Whisper_Statement_15", "Statement", 11),
            new WhisperEntry("HSK_Whisper_Statement_16", "Statement", 6),
            new WhisperEntry("HSK_Whisper_Identity_17", "Identity", 10),
            new WhisperEntry("HSK_Whisper_Identity_18", "Identity", 9),
            new WhisperEntry("HSK_Whisper_Identity_19", "Identity", 8),
            new WhisperEntry("HSK_Whisper_Identity_20", "Identity", 11),
            new WhisperEntry("HSK_Whisper_Identity_21", "Identity", 13),
            new WhisperEntry("HSK_Whisper_Identity_22", "Identity", 7),
            new WhisperEntry("HSK_Whisper_Identity_23", "Identity", 6),
            new WhisperEntry("HSK_Whisper_Cosmic_24", "Cosmic", 5),
            new WhisperEntry("HSK_Whisper_Cosmic_25", "Cosmic", 6),
            new WhisperEntry("HSK_Whisper_Cosmic_26", "Cosmic", 9),
            new WhisperEntry("HSK_Whisper_Cosmic_27", "Cosmic", 8),
            new WhisperEntry("HSK_Whisper_Cosmic_28", "Cosmic", 10),
            new WhisperEntry("HSK_Whisper_Cosmic_29", "Cosmic", 7),
            new WhisperEntry("HSK_Whisper_Flesh_30", "Flesh", 16),
            new WhisperEntry("HSK_Whisper_Flesh_31", "Flesh", 9),
            new WhisperEntry("HSK_Whisper_Flesh_32", "Flesh", 12),
            new WhisperEntry("HSK_Whisper_Flesh_33", "Flesh", 14),
            new WhisperEntry("HSK_Whisper_Flesh_34", "Flesh", 8),
            new WhisperEntry("HSK_Whisper_Flesh_35", "Flesh", 6),
            new WhisperEntry("HSK_Whisper_Distortion_36", "Distortion", 6),
            new WhisperEntry("HSK_Whisper_Distortion_37", "Distortion", 8),
            new WhisperEntry("HSK_Whisper_Distortion_38", "Distortion", 9),
            new WhisperEntry("HSK_Whisper_Distortion_39", "Distortion", 7),
            new WhisperEntry("HSK_Whisper_Distortion_40", "Distortion", 5),
            new WhisperEntry("HSK_Whisper_Distortion_41", "Distortion", 10),
            new WhisperEntry("HSK_Whisper_Ritual_42", "Ritual", 9),
            new WhisperEntry("HSK_Whisper_Ritual_43", "Ritual", 7),
            new WhisperEntry("HSK_Whisper_Ritual_44", "Ritual", 12),
            new WhisperEntry("HSK_Whisper_Ritual_45", "Ritual", 8),
            new WhisperEntry("HSK_Whisper_Ritual_46", "Ritual", 15),
            new WhisperEntry("HSK_Whisper_Ritual_47", "Ritual", 10),
        };
    }

    /// <summary>Мысль-память с собственной фразой и силой (одна ThoughtDef на все 47 фраз).</summary>
    public class Thought_UnitologyWhisper : Thought_Memory
    {
        public string phraseKey;
        public string category;
        public float offset;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref phraseKey, "phraseKey");
            Scribe_Values.Look(ref category, "category");
            Scribe_Values.Look(ref offset, "offset", 0f);
        }

        public override string LabelCap
        {
            get
            {
                if (phraseKey.NullOrEmpty()) return base.LabelCap;
                return ("«" + phraseKey.Translate().Resolve() + "»");
            }
        }

        public override string Description
        {
            get
            {
                if (phraseKey.NullOrEmpty() || category.NullOrEmpty()) return base.Description;
                return "HSK_Whisper_Desc".Translate(
                    ("HSK_WhisperCat_" + category).Translate().Resolve(),
                    ("HSK_WhisperDesc_" + category).Translate().Resolve());
            }
        }

        public override float MoodOffset()
        {
            if (phraseKey.NullOrEmpty()) return base.MoodOffset();
            return -Math.Abs(offset);
        }
    }


    /// <summary>Проверка «маркер изучен»: хотя бы один HSK_BlackMarker с Studied == true.</summary>

    public static class MarkerStudy
    {
        public static bool IsAnyMarkerStudied()
        {
            var maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                var things = maps[i].listerThings.ThingsOfDef(HSK_MarkerDefOf.HSK_BlackMarker);
                for (int t = 0; t < things.Count; t++)
                {
                    Building_BlackMarker marker = things[t] as Building_BlackMarker;
                    if (marker != null && marker.Studied) return true;
                }
            }
            return false;
        }
    }

    public static class UnitologyWhispers
    {
        public static bool DebugEnabled = true;

        public static ThoughtDef Def
        {
            get { return DefDatabase<ThoughtDef>.GetNamedSilentFail("HSK_UnitologyWhisper"); }
        }

        /// <summary>Раздаёт колонистам карты по уникальной фразе. Возвращает число затронутых.</summary>
        public static int Apply(Map map)
        {
            ThoughtDef def = Def;
            if (def == null)
            {
                Log.Warning("[Watcher/Whispers] нет ThoughtDef HSK_UnitologyWhisper (положите Defs).");
                return 0;
            }

            List<Pawn> pawns = map.mapPawns.FreeColonistsSpawned
                .Where(p => p.needs != null && p.needs.mood != null && p.needs.mood.thoughts != null)
                .ToList();
            if (pawns.Count == 0) return 0;

            List<WhisperEntry> bag = new List<WhisperEntry>();
            List<Pawn> affected = new List<Pawn>();

            for (int i = 0; i < pawns.Count; i++)
            {
                if (bag.Count == 0) bag = WhisperTable.All.InRandomOrder().ToList();

                WhisperEntry entry = bag[bag.Count - 1];
                bag.RemoveAt(bag.Count - 1);

                Thought_UnitologyWhisper thought = (Thought_UnitologyWhisper)ThoughtMaker.MakeThought(def);
                thought.phraseKey = entry.key;
                thought.category = entry.category;
                thought.offset = entry.mood;

                pawns[i].needs.mood.thoughts.memories.TryGainMemory(thought);
                affected.Add(pawns[i]);

                if (DebugEnabled)
                    Log.Message("[Watcher/Whispers] " + pawns[i].LabelShort + ": " + entry.key + " (-" + entry.mood + ")");
            }

            // Ключ картинки = defName события (IncidentDef HSK_UnitologyWhispers).
            HSK_Letters.Send(
                "HSK_UnitologyWhispers",
                "HSK_Whispers_Label".Translate(),
                "HSK_Whispers_Letter".Translate(string.Join(", ", affected.Select(p => p.LabelShort).ToArray())),
                LetterDefOf.NegativeEvent,
                new LookTargets(affected));

            return affected.Count;
        }
    }

    public class IncidentWorker_UnitologyWhispers : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null || UnitologyWhispers.Def == null) return false;

            // Только после изучения маркера.
            if (!MarkerStudy.IsAnyMarkerStudied()) return false;

            return map.mapPawns.FreeColonistsSpawnedCount > 0;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null) return false;
            return UnitologyWhispers.Apply(map) > 0;
        }
    }

    public static class UnitologyWhispers_DebugActions
    {
        [LudeonTK.DebugAction("Watcher / Unitology", "Whispers: fire now", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void FireNow()
        {
            UnitologyWhispers.Apply(Find.CurrentMap);
        }

        [LudeonTK.DebugAction("Watcher / Unitology", "Whispers: toggle debug log", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void ToggleLog()
        {
            UnitologyWhispers.DebugEnabled = !UnitologyWhispers.DebugEnabled;
        }
    }
}

// ------------------------------------------------------------
// Десант CEC: челнок, бойцы (количество зависит от богатства колонии),
// цель - уничтожить Чёрный маркер, после этого бойцы улетают на челноке или убегают
// ------------------------------------------------------------
namespace HSK.Watcher.DeadSpace
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using RimWorld;
    using UnityEngine;
    using Verse;
    using Verse.AI;
    using Verse.AI.Group;

    public class IncidentWorker_MarkerStrike : IncidentWorker
    {
        private const int MinEdgeDistance = 15;
        private const float MinDistanceToMarker = 20f;
        private static readonly IntVec2 ShuttleSize = new IntVec2(5, 3);

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null || Faction.OfAncientsHostile == null)
            {
                return false;
            }

            MarkerStrikeExtension ext = def.GetModExtension<MarkerStrikeExtension>();
            if (ext == null || ext.shuttleDef == null || ext.shuttleIncomingDef == null || ext.members.Count == 0)
            {
                return false;
            }

            MapComponent_MarkerStrike comp = map.GetComponent<MapComponent_MarkerStrike>();
            if (comp != null && comp.Active)
            {
                return false;
            }

            // Цель события - маркер: без него на карте события нет.
            Thing marker = MarkerStrikeUtility.FindMarker(map, IntVec3.Zero);
            if (marker == null)
            {
                return false;
            }

            IntVec3 cell;
            return TryFindLandingCell(map, marker, out cell);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null)
            {
                return false;
            }

            MarkerStrikeExtension ext = def.GetModExtension<MarkerStrikeExtension>();
            MapComponent_MarkerStrike comp = map.GetComponent<MapComponent_MarkerStrike>();
            Thing marker = MarkerStrikeUtility.FindMarker(map, IntVec3.Zero);
            IntVec3 landing;
            if (ext == null || comp == null || marker == null || !TryFindLandingCell(map, marker, out landing))
            {
                return false;
            }

            // --- РАСЧЁТ КОЛИЧЕСТВА БОЙЦОВ ПО БОГАТСТВУ КОЛОНИИ (ДЛЯ МЕГА-БАЗ) ---
            float totalWealth = map.wealthWatcher.WealthTotal;

            int minSquadSize = 1;
            int maxSquadSize = Mathf.Max(minSquadSize, ext.members.Count);

            // Порог максимальной сложности — 3,000,000 богатства.
            // Квадратный корень сглаживает кривую прироста на средних этапах игры.
            float wealthThreshold = 3000000f;
            float wealthFactor = Mathf.Clamp01(totalWealth / wealthThreshold);
            wealthFactor = Mathf.Sqrt(wealthFactor);

            int targetCount = Mathf.RoundToInt(Mathf.Lerp(minSquadSize, maxSquadSize, wealthFactor));

            List<Pawn> squad = new List<Pawn>();
            for (int i = 0; i < targetCount; i++)
            {
                StrikeMember member = ext.members[i % ext.members.Count];
                Pawn pawn = MakePawn(ext, member);
                if (pawn != null)
                {
                    squad.Add(pawn);
                }
            }

            if (squad.Count == 0)
            {
                return false;
            }

            // Челнок прилетает как скайфоллер: внутри здание-челнок и бойцы.
            Skyfaller_StrikeShuttle skyfaller = SkyfallerMaker.MakeSkyfaller(ext.shuttleIncomingDef) as Skyfaller_StrikeShuttle;
            if (skyfaller == null)
            {
                Log.Error("[Watcher/MarkerStrike] у shuttleIncomingDef должен быть thingClass HSK.Watcher.DeadSpace.Skyfaller_StrikeShuttle.");
                return false;
            }

            skyfaller.innerContainer.TryAdd(ThingMaker.MakeThing(ext.shuttleDef));
            for (int i = 0; i < squad.Count; i++)
            {
                skyfaller.innerContainer.TryAdd(squad[i]);
            }

            GenSpawn.Spawn(skyfaller, landing, map);
            comp.MarkIncoming();

            HSK_Letters.Send(
                def.defName,
                "HSK_MarkerStrike_Label".Translate(),
                "HSK_MarkerStrike_Text".Translate(squad.Count),
                LetterDefOf.ThreatBig,
                new LookTargets(landing, map));
            return true;
        }

        private static bool TryFindLandingCell(Map map, Thing marker, out IntVec3 cell)
        {
            return CellFinderLoose.TryFindRandomNotEdgeCellWith(
                MinEdgeDistance,
                delegate (IntVec3 c) { return IsGoodLanding(c, map, marker); },
                map,
                out cell);
        }

        // Вся площадка челнока (с запасом в 1 клетку) свободна, проходима и не под крышей.
        private static bool IsGoodLanding(IntVec3 center, Map map, Thing marker)
        {
            CellRect rect = GenAdj.OccupiedRect(center, Rot4.North, ShuttleSize).ExpandedBy(1);
            foreach (IntVec3 c in rect)
            {
                if (!c.InBounds(map) || !c.Standable(map) || c.Roofed(map) || c.GetEdifice(map) != null)
                {
                    return false;
                }
            }

            return marker == null || center.DistanceTo(marker.Position) >= MinDistanceToMarker;
        }

        private static Pawn MakePawn(MarkerStrikeExtension ext, StrikeMember member)
        {
            PawnKindDef kind = ext.pawnKind
                ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("AncientSoldier")
                ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Mercenary_Gunner");
            if (kind == null || member.weaponDef == null)
            {
                Log.Error("[Watcher/MarkerStrike] нет PawnKindDef или оружия бойца.");
                return null;
            }

            PawnGenerationRequest request = new PawnGenerationRequest(
                kind,
                Faction.OfAncientsHostile,
                PawnGenerationContext.NonPlayer);
            Pawn pawn = PawnGenerator.GeneratePawn(request);

            // Свой комплект вместо случайного.
            pawn.apparel.DestroyAll();
            pawn.equipment.DestroyAllEquipment();
            pawn.inventory.DestroyAll();

            Wear(pawn, ext.armorDef);
            Wear(pawn, ext.helmetDef);

            ThingWithComps gun = ThingMaker.MakeThing(member.weaponDef) as ThingWithComps;
            if (gun != null)
            {
                pawn.equipment.AddEquipment(gun);
            }

            ThingDef ammoDef = member.ammoDef ?? MarkerStrikeUtility.FindAmmo(member.weaponDef);
            if (ammoDef == null)
            {
                Log.Warning("[Watcher/MarkerStrike] не найден боеприпас для " + member.weaponDef.defName + ": укажите ammoDef в XML.");
            }
            else
            {
                int count = member.ammoCount;
                if (ext.ammoSpread > 0f)
                {
                    count = Mathf.RoundToInt(count * Rand.Range(1f - ext.ammoSpread, 1f + ext.ammoSpread));
                }

                AddAmmo(pawn, ammoDef, count);
            }

            return pawn;
        }

        private static void Wear(Pawn pawn, ThingDef def)
        {
            if (def == null)
            {
                return;
            }

            ThingDef stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            Apparel apparel = ThingMaker.MakeThing(def, stuff) as Apparel;
            if (apparel != null)
            {
                pawn.apparel.Wear(apparel, false);
            }
        }

        private static void AddAmmo(Pawn pawn, ThingDef ammoDef, int count)
        {
            int remaining = count;
            while (remaining > 0)
            {
                int amount = Mathf.Min(remaining, ammoDef.stackLimit);
                Thing stack = ThingMaker.MakeThing(ammoDef);
                stack.stackCount = amount;

                // TryAdd(item, count) возвращает число реально добавленных предметов.
                int added = pawn.inventory.innerContainer.TryAdd(stack, amount);
                if (!stack.Destroyed && stack.stackCount > 0 && stack.holdingOwner == null)
                {
                    stack.Destroy();
                }

                if (added <= 0)
                {
                    break;
                }

                remaining -= added;
            }
        }
    }
}