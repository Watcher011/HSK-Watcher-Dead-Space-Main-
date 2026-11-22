// ============================================================
// Watcher — Чёрный маркер
// Совместимо с C# 7.3. using-директивы перенесены внутрь каждого namespace,
// поэтому части не влияют друг на друга (как и в отдельных файлах).
//
// Содержимое:
//   1. Юнитология: реформация идеологии
//   2. Юнитология: мысли, проповеди
//   3. Ремонт сломанных заповедей
// ============================================================

// ------------------------------------------------------------
// Юнитология: реформация идеологии  [/mnt/attach/outputs/Watcher_UnitologyReformation.cs]
// ------------------------------------------------------------
// HSK_UnitologyReformation.cs
// После изучения Маркера всем колонистам предлагается перейти в заранее заготовленную
// иделогию «Юнитология». Появляется ОДНО окно со списком колонистов (галочки), кнопки
// «Принять» / «Отказаться». Срабатывает один раз за сохранение.
//
// Запуск: Building_BlackMarker.AddStudyProgress вызывает
//        HSK.Watcher.Content.UnitologyReformation.Trigger(this);
// сразу после изучения маркера.
//
// C# 7.3. Требуется DLC Ideology (без него просто ничего не делает).
namespace HSK.Watcher.DeadSpace
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using RimWorld;
    using UnityEngine;
    using Verse;

    // ------------------------------------------------------------------ отладка
    public static class UnitologyDebug
    {
        public static bool Enabled = true;

        public static void Msg(string text)
        {
            if (Enabled) Log.Message("[Watcher/Unitology] " + text);
        }
    }

    // ------------------------------------------------------------ состояние/сейв
    public class GameComponent_Unitology : GameComponent
    {
        public bool offered;       // окно уже показывали
        public Ideo unitologyIdeo; // созданная (или найденная) идеология

        public GameComponent_Unitology(Game game) { }

        // Новая игра и загрузка: у всех идеологий с мемом Юнитологии убираем
        // «отвращение к трупам» и требования одежды у ролей (лидер, наставник и т. д.).
        public override void FinalizeInit()
        {
            UnitologyCorpses.PurgeAll();
            UnitologyRoles.ClearAll();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref offered, "offered", false);
            Scribe_References.Look(ref unitologyIdeo, "unitologyIdeo");
        }
    }

    // ------------------------------------------------------------------- логика
    public static class UnitologyReformation
    {
        public static GameComponent_Unitology Comp
        {
            get { return Current.Game.GetComponent<GameComponent_Unitology>(); }
        }

        /// <summary>Вызвать, когда Маркер изучен. Безопасно вызывать много раз.</summary>
        public static void Trigger(Thing marker)
        {
            if (!ModsConfig.IdeologyActive)
            {
                UnitologyDebug.Msg("DLC Ideology не активно — пропуск.");
                return;
            }

            GameComponent_Unitology comp = Comp;
            if (comp == null || comp.offered) return;
            comp.offered = true;

            Ideo ideo = GetOrCreateIdeo(comp);
            if (ideo == null)
            {
                Log.Warning("[Watcher/Unitology] не удалось получить идеологию — окно не показано.");
                return;
            }

            List<Pawn> pawns = PawnsFinder.AllMaps_FreeColonists
                .Where(p => p.ideo != null && p.Ideo != ideo)
                .ToList();
            UnitologyDebug.Msg("колонистов для предложения: " + pawns.Count);
            if (pawns.Count == 0) return;

            Find.WindowStack.Add(new Dialog_UnitologyReformation(ideo, pawns));
        }

        public static Ideo GetOrCreateIdeo(GameComponent_Unitology comp)
        {
            if (comp.unitologyIdeo != null) return comp.unitologyIdeo;

            string name = "HSK_Unitology_Name".Translate();

            // Уже существует идеология с таким названием (например, сделанная вручную).
            foreach (Ideo existing in Find.IdeoManager.IdeosListForReading)
            {
                if (existing.name == name)
                {
                    comp.unitologyIdeo = existing;
                    UnitologyDebug.Msg("найдена готовая идеология «" + name + "»");
                    return existing;
                }
            }

            try
            {
                // Мем Юнитологии (HSK_Unitology) задаёт заповеди: почитание трупов,
                // частое обращение и т. д. Передаём его как обязательный при генерации.
                List<MemeDef> forced = new List<MemeDef>();
                if (HSK_UnitologyDefOf.HSK_Unitology != null) forced.Add(HSK_UnitologyDefOf.HSK_Unitology);

                Ideo ideo = IdeoGenerator.GenerateIdeo(
                    new IdeoGenerationParms(Faction.OfPlayer.def,
                        disallowedPrecepts: UnitologyCorpses.DislikeDefs(),
                        forcedMemes: forced));
                UnitologyCorpses.Purge(ideo);
                UnitologyRoles.ClearApparel(ideo);

                ideo.name = name;
                ideo.adjective = "HSK_Unitology_Adjective".Translate();
                ideo.memberName = "HSK_Unitology_Member".Translate();

                Find.IdeoManager.Add(ideo);
                comp.unitologyIdeo = ideo;
                UnitologyDebug.Msg("создана идеология «" + name + "», мемов: " + ideo.memes.Count);
                return ideo;
            }
            catch (Exception ex)
            {
                Log.Error("[Watcher/Unitology] ошибка создания идеологии: " + ex);
                return null;
            }
        }


        public static void Convert(Ideo ideo, List<Pawn> pawns)
        {
            int count = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Dead || pawn.ideo == null) continue;

                try
                {
                    pawn.ideo.SetIdeo(ideo);
                    count++;
                    UnitologyDebug.Msg(pawn.LabelShort + " принял «" + ideo.name + "»");
                }
                catch (Exception ex)
                {
                    Log.Warning("[Watcher/Unitology] " + pawn.LabelShort + ": " + ex.Message);
                }
            }

            try { Faction.OfPlayer.ideos.RecalculateIdeosBasedOnPlayerPawns(); }
            catch (Exception ex) { UnitologyDebug.Msg("RecalculateIdeos: " + ex.Message); }

            Messages.Message("HSK_Unitology_Done".Translate(count, ideo.name),
                MessageTypeDefOf.NeutralEvent, false);
        }
    }

    // ------------------------------------------------------------------ роли
    // У ролей (лидер, наставник, специалисты) игра случайно выдаёт требуемую одежду
    // (берет, чадра, накидка...). Юнитологам она не нужна, поэтому список требований
    // у ролей их идеологий очищается. Поле читается через рефлексию: если у Precept_Role
    // в вашей версии оно называется иначе, в лог выводится предупреждение (не ошибка).
    public static class UnitologyRoles
    {
        private static bool warned;

        public static void ClearAll()
        {
            if (!ModsConfig.IdeologyActive || Find.IdeoManager == null || HSK_UnitologyDefOf.HSK_Unitology == null) return;

            List<Ideo> ideos = Find.IdeoManager.IdeosListForReading;
            for (int i = 0; i < ideos.Count; i++)
            {
                if (ideos[i].HasMeme(HSK_UnitologyDefOf.HSK_Unitology)) ClearApparel(ideos[i]);
            }
        }

        public static void ClearApparel(Ideo ideo)
        {
            if (ideo == null) return;

            System.Reflection.FieldInfo field = typeof(Precept_Role).GetField("apparelRequirements",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field == null)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Warning("[Watcher/Unitology] у Precept_Role нет поля apparelRequirements — требования одежды у ролей не очищены.");
                }
                return;
            }

            List<Precept> precepts = ideo.PreceptsListForReading;
            for (int i = 0; i < precepts.Count; i++)
            {
                Precept_Role role = precepts[i] as Precept_Role;
                if (role == null) continue;

                System.Collections.IList list = field.GetValue(role) as System.Collections.IList;
                if (list != null && list.Count > 0)
                {
                    list.Clear();
                    UnitologyDebug.Msg("у роли «" + role.def.defName + "» очищены требования одежды");
                }
            }
        }
    }

    // ------------------------------------------------------------ трупы
    // Юнитологи превозносят мёртвых, поэтому заповеди, которые наказывают за вид трупов
    // (например Corpses_Ugly: мысли ObservedLayingCorpse / ObservedLayingRottingCorpse),
    // им запрещены. Такие заповеди определяются по событиям в их компонентах, а не по
    // именам, поэтому подхватываются и заповеди из других модов.
    public static class UnitologyCorpses
    {
        private static readonly string[] BadEvents = { "ObservedLayingCorpse", "ObservedLayingRottingCorpse" };

        public static bool IsDislike(PreceptDef def)
        {
            if (def == null || def.comps == null) return false;

            for (int i = 0; i < def.comps.Count; i++)
            {
                PreceptComp_SelfTookMemoryThought comp = def.comps[i] as PreceptComp_SelfTookMemoryThought;
                if (comp == null || comp.eventDef == null) continue;

                for (int j = 0; j < BadEvents.Length; j++)
                {
                    if (comp.eventDef.defName == BadEvents[j]) return true;
                }
            }
            return false;
        }

        public static List<PreceptDef> DislikeDefs()
        {
            return DefDatabase<PreceptDef>.AllDefsListForReading.Where(IsDislike).ToList();
        }

        public static void Purge(Ideo ideo)
        {
            if (ideo == null) return;

            List<Precept> bad = ideo.PreceptsListForReading.Where(p => IsDislike(p.def)).ToList();
            for (int i = 0; i < bad.Count; i++)
            {
                try
                {
                    ideo.RemovePrecept(bad[i]);
                    UnitologyDebug.Msg("убрана заповедь «" + bad[i].def.defName + "» из " + ideo.name);
                }
                catch (Exception ex)
                {
                    Log.Warning("[Watcher/Unitology] не удалось убрать " + bad[i].def.defName + ": " + ex.Message);
                }
            }
        }

        public static void PurgeAll()
        {
            if (!ModsConfig.IdeologyActive || Find.IdeoManager == null || HSK_UnitologyDefOf.HSK_Unitology == null) return;

            List<Ideo> ideos = Find.IdeoManager.IdeosListForReading;
            for (int i = 0; i < ideos.Count; i++)
            {
                if (ideos[i].HasMeme(HSK_UnitologyDefOf.HSK_Unitology)) Purge(ideos[i]);
            }
        }
    }

    // --------------------------------------------------------------------- окно
    public class Dialog_UnitologyReformation : Window
    {
        private readonly Ideo ideo;
        private readonly List<Pawn> pawns;
        private readonly Dictionary<Pawn, bool> chosen = new Dictionary<Pawn, bool>();
        private Vector2 scroll;

        public override Vector2 InitialSize { get { return new Vector2(520f, 560f); } }

        public Dialog_UnitologyReformation(Ideo ideo, List<Pawn> pawns)
        {
            this.ideo = ideo;
            this.pawns = pawns;
            for (int i = 0; i < pawns.Count; i++) chosen[pawns[i]] = true;

            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 34f), "HSK_Unitology_Title".Translate());
            y += 38f;

            Text.Font = GameFont.Small;
            float descHeight = Text.CalcHeight("HSK_Unitology_Desc".Translate(ideo.name), inRect.width);
            Widgets.Label(new Rect(0f, y, inRect.width, descHeight), "HSK_Unitology_Desc".Translate(ideo.name));
            y += descHeight + 8f;

            Rect outRect = new Rect(0f, y, inRect.width, inRect.height - y - 46f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, pawns.Count * 30f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                Rect row = new Rect(0f, i * 30f, viewRect.width, 28f);
                bool value = chosen[pawn];
                Widgets.CheckboxLabeled(row, pawn.LabelShortCap + "  (" + pawn.Ideo.name + ")", ref value);
                chosen[pawn] = value;
            }
            Widgets.EndScrollView();

            float half = inRect.width / 2f - 4f;
            if (Widgets.ButtonText(new Rect(0f, inRect.height - 38f, half, 38f), "HSK_Unitology_Accept".Translate()))
            {
                UnitologyReformation.Convert(ideo, pawns.Where(p => chosen[p]).ToList());
                Close();
            }
            if (Widgets.ButtonText(new Rect(half + 8f, inRect.height - 38f, half, 38f), "HSK_Unitology_Decline".Translate()))
            {
                Close();
            }
        }
    }

    // ---------------------------------------------------------------- отладка
    public static class Unitology_DebugActions
    {
        [LudeonTK.DebugAction("Watcher / Unitology", "Show reformation window", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void ShowWindow()
        {
            GameComponent_Unitology comp = UnitologyReformation.Comp;
            if (comp != null) comp.offered = false;
            UnitologyReformation.Trigger(null);
        }

        [LudeonTK.DebugAction("Watcher / Unitology", "Toggle debug log", allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void ToggleLog()
        {
            UnitologyDebug.Enabled = !UnitologyDebug.Enabled;
            Messages.Message("Unitology debug: " + (UnitologyDebug.Enabled ? "ON" : "OFF"),
                MessageTypeDefOf.NeutralEvent, false);
        }
    }
}

// ------------------------------------------------------------
// Юнитология: мысли, проповеди  [/root/.claude/uploads/a85e4491-08d3-53f7-b914-0e7ecd4ca2b9/9d68b882-1792323063605_HSK_Unitology.cs]
// ------------------------------------------------------------
namespace HSK.Watcher.DeadSpace
{
    using System.Collections.Generic;
    using RimWorld;
    using Verse;

    // ------------------------------------------------------------------
    // DefOf
    // ------------------------------------------------------------------
    [DefOf]
    public static class HSK_UnitologyDefOf
    {
        public static MemeDef HSK_Unitology;

        static HSK_UnitologyDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HSK_UnitologyDefOf));
        }
    }

    // ------------------------------------------------------------------
    // Общие функции веры.
    // ------------------------------------------------------------------
    public static class UnitologyUtility
    {
        public static bool IsUnitologist(Pawn pawn)
        {
            return pawn != null
                && pawn.Ideo != null
                && HSK_UnitologyDefOf.HSK_Unitology != null
                && pawn.Ideo.HasMeme(HSK_UnitologyDefOf.HSK_Unitology);
        }

        // Трупы на земле и в могилах/саркофагах в радиусе от пешки.
        public static int CountCorpsesNear(Pawn pawn, float radius)
        {
            if (pawn == null || !pawn.Spawned)
            {
                return 0;
            }

            Map map = pawn.Map;
            int count = 0;

            List<Thing> corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            for (int i = 0; i < corpses.Count; i++)
            {
                if (corpses[i].Spawned && corpses[i].Position.InHorDistOf(pawn.Position, radius))
                {
                    count++;
                }
            }

            List<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < buildings.Count; i++)
            {
                Building_Casket casket = buildings[i] as Building_Casket;
                if (casket != null
                    && casket.Position.InHorDistOf(pawn.Position, radius)
                    && CorpseSearch.FindCorpseIn(casket) != null)
                {
                    count++;
                }
            }

            return count;
        }

        // Ближайший маркер в радиусе; studied сообщает, изучен ли он.
        public static bool TryFindMarkerNear(Pawn pawn, float radius, out bool studied)
        {
            studied = false;
            if (pawn == null || !pawn.Spawned)
            {
                return false;
            }

            bool found = false;
            List<Thing> things = pawn.Map.listerThings.ThingsOfDef(HSK_MarkerDefOf.HSK_BlackMarker);
            for (int i = 0; i < things.Count; i++)
            {
                Building_BlackMarker m = things[i] as Building_BlackMarker;
                if (m == null || !m.Position.InHorDistOf(pawn.Position, radius))
                {
                    continue;
                }

                found = true;
                if (m.Studied)
                {
                    studied = true;
                    break;
                }
            }

            return found;
        }
    }

    // ------------------------------------------------------------------
    // Мысль: рядом мёртвые (трупы превозносятся). 0 - 1..3 трупа, 1 - 4 и больше.
    // ------------------------------------------------------------------
    public class ThoughtWorker_Precept_CorpsesNearby : ThoughtWorker_Precept
    {
        private const float Radius = 12f;

        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            int count = UnitologyUtility.CountCorpsesNear(p, Radius);
            if (count <= 0)
            {
                return ThoughtState.Inactive;
            }

            return ThoughtState.ActiveAtStage(count >= 4 ? 1 : 0);
        }
    }

    // ------------------------------------------------------------------
    // Мысль: рядом маркер (религиозная постройка). 0 - чёрный, 1 - изученный (красный).
    // ------------------------------------------------------------------
    public class ThoughtWorker_Precept_MarkerNearby : ThoughtWorker_Precept
    {
        private const float Radius = 20f;

        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            bool studied;
            if (!UnitologyUtility.TryFindMarkerNear(p, Radius, out studied))
            {
                return ThoughtState.Inactive;
            }

            return ThoughtState.ActiveAtStage(studied ? 1 : 0);
        }
    }

    // ------------------------------------------------------------------
    // Частое обращение: юнитологи сами проповедуют соседям по колонии.
    // Сила проповеди = ConversionPower проповедника (с заповедью "spread the word" x1.75),
    // у изученного маркера - ещё x1.5.
    // ------------------------------------------------------------------
    public class MapComponent_UnitologyZeal : MapComponent
    {
        private const int CheckIntervalTicks = 600;      // раз в 10 секунд
        private const float PreachChance = 0.35f;        // шанс, что проповедник начнёт проповедь
        private const float PreachRange = 12f;
        private const float BaseCertaintyDrop = 0.06f;   // падение уверенности цели до множителей
        private const float MarkerRange = 20f;
        private const float MarkerZealFactor = 1.5f;

        public MapComponent_UnitologyZeal(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (!ModsConfig.IdeologyActive || !map.IsPlayerHome)
            {
                return;
            }

            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0)
            {
                return;
            }

            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn preacher = colonists[i];
                if (!CanPreach(preacher) || !Rand.Chance(PreachChance))
                {
                    continue;
                }

                Pawn target = FindTarget(preacher, colonists);
                if (target == null)
                {
                    continue;
                }

                Preach(preacher, target);
            }
        }

        private static bool CanPreach(Pawn p)
        {
            return UnitologyUtility.IsUnitologist(p)
                && !p.Downed
                && !p.Drafted
                && !p.InMentalState
                && p.Awake();
        }

        private static Pawn FindTarget(Pawn preacher, List<Pawn> colonists)
        {
            List<Pawn> candidates = new List<Pawn>();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn t = colonists[i];
                if (t == preacher || t.Ideo == null || t.Ideo == preacher.Ideo)
                {
                    continue;
                }

                if (t.Downed || !t.Awake() || !t.Position.InHorDistOf(preacher.Position, PreachRange))
                {
                    continue;
                }

                candidates.Add(t);
            }

            return candidates.Count > 0 ? candidates.RandomElement() : null;
        }

        private static void Preach(Pawn preacher, Pawn target)
        {
            float power = preacher.GetStatValue(StatDefOf.ConversionPower);

            bool studied;
            if (UnitologyUtility.TryFindMarkerNear(preacher, MarkerRange, out studied) && studied)
            {
                power *= MarkerZealFactor;
            }

            float drop = BaseCertaintyDrop * power;
            bool converted = target.ideo.IdeoConversionAttempt(drop, preacher.Ideo);
            if (converted)
            {
                Messages.Message(
                    "HSK_UnitologyConverted".Translate(target.LabelShort, preacher.LabelShort),
                    new LookTargets(target),
                    MessageTypeDefOf.PositiveEvent);
            }
        }
    }
}

// ------------------------------------------------------------
// Ремонт сломанных заповедей  [/root/.claude/uploads/a85e4491-08d3-53f7-b914-0e7ecd4ca2b9/6fe0aaf4-1792323063604_GameComponent_PreceptRepair.cs]
// ------------------------------------------------------------
namespace HSK.Watcher.DeadSpace
{
    using System;
    using System.Collections.Generic;
    using RimWorld;
    using Verse;

    // ------------------------------------------------------------------
    // Ремонт сохранений: если заповедь-здание не смогла выбрать здание
    // (например, алтарь-маркер добавили в идеологию второй раз), в идеологии
    // остаётся "пустая" заповедь без ThingDef. Она вызывает NullReferenceException
    // в мыслях каждого колониста этой идеологии, и колонисты перестают работать.
    // При загрузке сохранения такие заповеди удаляются.
    // ------------------------------------------------------------------
    public class GameComponent_PreceptRepair : GameComponent
    {
        public GameComponent_PreceptRepair(Game game)
        {
        }

        public override void LoadedGame()
        {
            RepairAll();
        }

        public static void RepairAll()
        {
            if (Find.IdeoManager == null)
            {
                return;
            }

            List<Ideo> ideos = Find.IdeoManager.IdeosListForReading;
            for (int i = 0; i < ideos.Count; i++)
            {
                Ideo ideo = ideos[i];
                List<Precept> broken = new List<Precept>();
                List<Precept> precepts = ideo.PreceptsListForReading;
                for (int j = 0; j < precepts.Count; j++)
                {
                    Precept_Building building = precepts[j] as Precept_Building;
                    if (building != null && building.ThingDef == null)
                    {
                        broken.Add(building);
                    }
                }

                for (int k = 0; k < broken.Count; k++)
                {
                    try
                    {
                        ideo.RemovePrecept(broken[k]);
                    }
                    catch (Exception)
                    {
                        // Если штатное удаление не сработало, убираем заповедь из списка напрямую.
                        ideo.PreceptsListForReading.Remove(broken[k]);
                    }

                    Log.Warning("[HSK Dead Space] Removed a broken building precept from ideoligion " + ideo.name + ".");
                }
            }
        }
    }
}

// ------------------------------------------------------------
// Стазис: стан как при ЭМИ (Pawn_StunHandler) + хедифф для синего оттенка
// ------------------------------------------------------------
namespace HSK.Watcher.DeadSpace
{
    using RimWorld;
    using Verse;

    public class CompProperties_AbilityStasis : CompProperties_AbilityEffect
    {
        public int stunTicks = 480;
        public HediffDef hediffDef;

        public CompProperties_AbilityStasis()
        {
            compClass = typeof(CompAbilityEffect_Stasis);
        }
    }

    public class CompAbilityEffect_Stasis : CompAbilityEffect
    {
        private new CompProperties_AbilityStasis Props
        {
            get { return (CompProperties_AbilityStasis)props; }
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn victim = target.Pawn;
            if (victim == null || victim.Dead)
            {
                return;
            }

            if (Props.hediffDef != null)
            {
                victim.health.AddHediff(Props.hediffDef);
            }

            if (victim.stances != null && victim.stances.stunner != null)
            {
                victim.stances.stunner.StunFor(Props.stunTicks, parent.pawn, false, true);
            }
        }
    }
}

// ------------------------------------------------------------
// Броня без окраски от материала (белый цвет = родная текстура)
// ------------------------------------------------------------
namespace HSK.Watcher.DeadSpace
{
    using RimWorld;
    using UnityEngine;

    public class Apparel_NoTint : Apparel
    {
        public override Color DrawColor
        {
            get { return Color.white; }
        }
    }
}

// ------------------------------------------------------------
// Броня, выдающая способность владельцу, пока надета
// ------------------------------------------------------------
namespace HSK.Watcher.DeadSpace
{
    using RimWorld;
    using Verse;

    // XML: <modExtensions><li Class="HSK.Watcher.DeadSpace.ApparelAbilityExtension"><abilityDef>HSK_Stasis</abilityDef></li></modExtensions>
    public class ApparelAbilityExtension : DefModExtension
    {
        public AbilityDef abilityDef;
    }

    public class Apparel_WithAbility : Apparel_NoTint
    {
        private Pawn grantedTo;

        private AbilityDef AbilityToGrant
        {
            get
            {
                ApparelAbilityExtension ext = def.GetModExtension<ApparelAbilityExtension>();
                return ext == null ? null : ext.abilityDef;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref grantedTo, "grantedTo");
        }

        protected override void Tick()
        {
            base.Tick();

            if (!this.IsHashIntervalTick(30))
            {
                return;
            }

            AbilityDef ability = AbilityToGrant;
            if (ability == null)
            {
                return;
            }

            Pawn wearer = Wearer;
            if (wearer == grantedTo)
            {
                return;
            }

            if (grantedTo != null && grantedTo.abilities != null)
            {
                grantedTo.abilities.RemoveAbility(ability);
            }

            if (wearer != null && wearer.abilities != null)
            {
                wearer.abilities.GainAbility(ability);
            }

            grantedTo = wearer;
        }
    }
}

// ------------------------------------------------------------
// Десант CEC: настройки, ИИ, ход события (сам инцидент - в Watcher_Events.cs)
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

    // Один боец: оружие и запас боеприпасов. ammoDef необязателен: если не указан,
    // подбирается боеприпас под снаряд оружия (Combat Extended).
    public class StrikeMember
    {
        public ThingDef weaponDef;
        public ThingDef ammoDef;
        public int ammoCount;
    }

    // Настройки инцидента (modExtension на IncidentDef HSK_MarkerStrike).
    public class MarkerStrikeExtension : DefModExtension
    {
        public PawnKindDef pawnKind;
        public ThingDef armorDef;
        public ThingDef helmetDef;
        public ThingDef shuttleDef;
        public ThingDef shuttleIncomingDef;
        public ThingDef shuttleLeavingDef;
        public float ammoSpread = 0.1f;
        public List<StrikeMember> members = new List<StrikeMember>();
    }

    [DefOf]
    public static class HSK_StrikeDefOf
    {
        public static DutyDef HSK_DestroyMarker;
        public static JobDef HSK_CarryAlly;

        static HSK_StrikeDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HSK_StrikeDefOf));
        }
    }

    public static class MarkerStrikeUtility
    {
        // Ближайший маркер на карте: сначала построенный, затем упакованный (MinifiedThing).
        public static Thing FindMarker(Map map, IntVec3 from)
        {
            Thing best = null;
            float bestDistance = float.MaxValue;

            List<Thing> things = map.listerThings.ThingsOfDef(HSK_MarkerDefOf.HSK_BlackMarker);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i].Destroyed)
                {
                    continue;
                }

                float distance = (things[i].Position - from).LengthHorizontalSquared;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = things[i];
                }
            }

            if (best != null)
            {
                return best;
            }

            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                MinifiedThing minified = all[i] as MinifiedThing;
                if (minified != null && !minified.Destroyed && minified.InnerThing != null
                    && minified.InnerThing.def == HSK_MarkerDefOf.HSK_BlackMarker)
                {
                    return minified;
                }
            }

            return null;
        }

        // Боеприпас для оружия через поля Combat Extended (ammoSet -> ammoTypes -> ammo/projectile).
        // Поля читаются рефлексией, поэтому ссылка на сборку CE не нужна. При ошибке вернёт null.
        public static ThingDef FindAmmo(ThingDef weapon)
        {
            try
            {
                ThingDef wantedProjectile = null;
                if (weapon.Verbs != null && weapon.Verbs.Count > 0)
                {
                    wantedProjectile = weapon.Verbs[0].defaultProjectile;
                }

                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                for (int i = 0; i < weapon.comps.Count; i++)
                {
                    object props = weapon.comps[i];
                    FieldInfo setField = props.GetType().GetField("ammoSet", flags);
                    if (setField == null)
                    {
                        continue;
                    }

                    object set = setField.GetValue(props);
                    if (set == null)
                    {
                        continue;
                    }

                    FieldInfo typesField = set.GetType().GetField("ammoTypes", flags);
                    IEnumerable links = typesField == null ? null : typesField.GetValue(set) as IEnumerable;
                    if (links == null)
                    {
                        continue;
                    }

                    ThingDef first = null;
                    foreach (object link in links)
                    {
                        FieldInfo ammoField = link.GetType().GetField("ammo", flags);
                        FieldInfo projectileField = link.GetType().GetField("projectile", flags);
                        ThingDef ammo = ammoField == null ? null : ammoField.GetValue(link) as ThingDef;
                        ThingDef projectile = projectileField == null ? null : projectileField.GetValue(link) as ThingDef;
                        if (ammo == null)
                        {
                            continue;
                        }

                        if (first == null)
                        {
                            first = ammo;
                        }

                        if (wantedProjectile != null && projectile == wantedProjectile)
                        {
                            return ammo;
                        }
                    }

                    if (first != null)
                    {
                        return first;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Watcher/MarkerStrike] не удалось определить боеприпас для " + weapon.defName + ": " + ex.Message);
            }

            return null;
        }
    }

    // ------------------------------------------------------------------
    // ИИ штурма: бой с врагами, иначе - уничтожение маркера.
    // ------------------------------------------------------------------
    public class JobGiver_AttackMarker : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            Map map = pawn.Map;
            if (map == null)
            {
                return null;
            }

            Thing marker = MarkerStrikeUtility.FindMarker(map, pawn.Position);
            if (marker == null)
            {
                return null;
            }

            float dist = pawn.Position.DistanceTo(marker.Position);

            // allowManualCastWeapons: у гранатомёта и дискомёта стоит onlyManualCast.
            Verb verb = pawn.TryGetAttackVerb(marker, true);
            if (verb == null)
            {
                return null;
            }

            string weapon = verb.EquipmentSource != null ? verb.EquipmentSource.def.defName : "?";
            string info = "оружие " + weapon + ", дальность " + verb.verbProps.range.ToString("F1")
                + ", дистанция " + dist.ToString("F1");

            // Стреляем только вблизи: издали маркер часто за стенами и дверями, и выстрелы не доходят.
            if (dist <= StandDistance && verb.CanHitTarget(marker))
            {
                Job attack = JobMaker.MakeJob(JobDefOf.AttackStatic, marker);
                attack.expiryInterval = 300;
                attack.checkOverrideOnExpire = true;
                return attack;
            }

            // Иначе идём (через двери) на клетку не дальше StandDistance от маркера, лучше с прямой видимостью.
            IntVec3 dest;
            if (TryFindStandCell(pawn, marker, out dest))
            {
                if (dest != pawn.Position)
                {
                    Job go = JobMaker.MakeJob(JobDefOf.Goto, dest);
                    go.expiryInterval = 300;
                    go.checkOverrideOnExpire = true;
                    go.canBashDoors = true;
                    return go;
                }

                // Уже на лучшей клетке, но стрелять нельзя: пробуем бить вручную.
                Job force = JobMaker.MakeJob(JobDefOf.AttackStatic, marker);
                force.expiryInterval = 300;
                force.checkOverrideOnExpire = true;
                return force;
            }

            return null;
        }

        private const float StandDistance = 5f;

        private static bool TryFindStandCell(Pawn pawn, Thing marker, out IntVec3 result)
        {
            Map map = pawn.Map;
            result = IntVec3.Invalid;

            IntVec3 bestLos = IntVec3.Invalid;
            float bestLosDist = float.MaxValue;
            IntVec3 bestAny = IntVec3.Invalid;
            float bestAnyDist = float.MaxValue;

            foreach (IntVec3 c in GenRadial.RadialCellsAround(marker.Position, StandDistance, true))
            {
                if (!c.InBounds(map) || !c.Standable(map))
                {
                    continue;
                }

                float d = c.DistanceTo(pawn.Position);
                bool los = GenSight.LineOfSight(c, marker.Position, map);

                if (!(los ? d < bestLosDist : d < bestAnyDist))
                {
                    continue;
                }

                if (!pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly, true))
                {
                    continue;
                }

                if (los)
                {
                    bestLos = c;
                    bestLosDist = d;
                }
                else
                {
                    bestAny = c;
                    bestAnyDist = d;
                }
            }

            if (bestLos.IsValid)
            {
                result = bestLos;
                return true;
            }

            if (bestAny.IsValid)
            {
                result = bestAny;
                return true;
            }

            return false;
        }
    }

    // ------------------------------------------------------------------
    // ИИ: по дороге к маркеру боец отвечает врагам, которых видит и достаёт.
    // У маркера (в пределах 5 клеток) приоритет остаётся за маркером.
    // ------------------------------------------------------------------
    public class JobGiver_FightOnTheWay : ThinkNode_JobGiver
    {
        private const float MaxTargetDistance = 30f;
        private const float MarkerPriorityDistance = 5f;

        protected override Job TryGiveJob(Pawn pawn)
        {
            Map map = pawn.Map;
            if (map == null)
            {
                return null;
            }

            Thing marker = MarkerStrikeUtility.FindMarker(map, pawn.Position);
            if (marker != null && pawn.Position.DistanceTo(marker.Position) <= MarkerPriorityDistance)
            {
                return null;
            }

            Pawn target = null;
            float bestDistance = float.MaxValue;
            Verb bestVerb = null;

            List<Pawn> candidates = map.mapPawns.PawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < candidates.Count; i++)
            {
                Pawn candidate = candidates[i];
                if (!candidate.Spawned || candidate.Downed || candidate.Dead || !candidate.HostileTo(pawn))
                {
                    continue;
                }

                float distance = candidate.Position.DistanceTo(pawn.Position);
                if (distance > MaxTargetDistance || distance >= bestDistance)
                {
                    continue;
                }

                Verb verb = pawn.TryGetAttackVerb(candidate, true);
                if (verb == null || !verb.CanHitTarget(candidate))
                {
                    continue;
                }

                bestDistance = distance;
                target = candidate;
                bestVerb = verb;
            }

            if (target == null)
            {
                return null;
            }

            Job attack = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
            attack.expiryInterval = 180;
            attack.checkOverrideOnExpire = true;
            return attack;
        }
    }

    // ------------------------------------------------------------------
    // ИИ: боец применяет стазис (способность выдаёт броня Armor_CEC)
    // на ближайшего враждебного колониста, когда способность готова.
    // ------------------------------------------------------------------
    public class JobGiver_UseStasis : ThinkNode_JobGiver
    {
        private const float MaxTargetDistance = 15f;

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn.abilities == null || pawn.Map == null)
            {
                return null;
            }

            AbilityDef abilityDef = DefDatabase<AbilityDef>.GetNamedSilentFail("HSK_Stasis");
            if (abilityDef == null)
            {
                return null;
            }

            Ability ability = pawn.abilities.GetAbility(abilityDef);
            if (ability == null)
            {
                return null;
            }

            if (!ability.CanCast)
            {
                return null;
            }

            HediffDef field = DefDatabase<HediffDef>.GetNamedSilentFail("HSK_StasisField");
            Pawn target = null;
            float bestDistance = float.MaxValue;

            List<Pawn> colonists = pawn.Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn candidate = colonists[i];
                if (candidate.Downed || candidate.Dead || !candidate.HostileTo(pawn))
                {
                    continue;
                }

                if (field != null && candidate.health.hediffSet.HasHediff(field))
                {
                    continue;
                }

                float distance = candidate.Position.DistanceTo(pawn.Position);
                if (distance > MaxTargetDistance || distance >= bestDistance)
                {
                    continue;
                }

                if (!ability.verb.CanHitTarget(candidate))
                {
                    continue;
                }

                bestDistance = distance;
                target = candidate;
            }

            if (target == null)
            {
                return null;
            }

            return ability.GetJob(target, target);
        }
    }

    public class LordToil_MarkerStrike : LordToil
    {
        public override void UpdateAllDuties()
        {
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                lord.ownedPawns[i].mindState.duty = new PawnDuty(HSK_StrikeDefOf.HSK_DestroyMarker);
            }
        }
    }

    public class LordJob_MarkerStrike : LordJob
    {
        // Бойцы проходят через любые двери (закрытые тоже).
        public override bool CanOpenAnyDoor(Pawn p)
        {
            return true;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            graph.AddToil(new LordToil_MarkerStrike());
            return graph;
        }
    }

    // Отход к челноку.
    public class LordJob_StrikeBoard : LordJob
    {
        private IntVec3 dest;

        public LordJob_StrikeBoard()
        {
        }

        public LordJob_StrikeBoard(IntVec3 dest)
        {
            this.dest = dest;
        }

        // Бойцы проходят через любые двери (закрытые тоже).
        public override bool CanOpenAnyDoor(Pawn p)
        {
            return true;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            graph.AddToil(new LordToil_Travel(dest));
            return graph;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref dest, "dest");
        }
    }

    // Побег с карты (челнока нет или добраться до него нельзя).
    public class LordJob_StrikeFlee : LordJob
    {
        // Бойцы проходят через любые двери (закрытые тоже).
        public override bool CanOpenAnyDoor(Pawn p)
        {
            return true;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            graph.AddToil(new LordToil_ExitMap(LocomotionUrgency.Sprint, true));
            return graph;
        }
    }

    // ------------------------------------------------------------------
    // Ход события: штурм -> отход -> улёт или побег.
    // ------------------------------------------------------------------
    // Боец подбирает раненого (лежачего) товарища и несёт к точке посадки на челнок.
    public class JobDriver_CarryAlly : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOn(delegate
            {
                Pawn ally = job.GetTarget(TargetIndex.A).Thing as Pawn;
                return ally == null || ally.Dead;
            });

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.ClosestTouch);
        }
    }

    // ------------------------------------------------------------------
    // Лучевое оружие под Combat Extended: стреляет лучом (ванильный Verb_ShootBeam),
    // но тратит патрон из магазина CE (CompAmmoUser): 1 патрон = 1 луч.
    // CE подключён через рефлексию, поэтому проект не зависит от его сборки.
    // ------------------------------------------------------------------
    public class Verb_ShootBeamCE : Verb_ShootBeam
    {
        private const string AmmoUserTypeName = "CombatExtended.CompAmmoUser";

        private static bool errorLogged;

        private ThingComp FindAmmoComp()
        {
            ThingWithComps weapon = EquipmentSource;
            if (weapon == null)
            {
                return null;
            }

            List<ThingComp> comps = weapon.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i].GetType().FullName == AmmoUserTypeName)
                {
                    return comps[i];
                }
            }

            return null;
        }

        private static int CurrentMagazine(ThingComp comp)
        {
            PropertyInfo prop = comp.GetType().GetProperty("CurMagCount", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
            {
                LogOnce("не найдено свойство CurMagCount у " + AmmoUserTypeName + "; луч не тратит патроны");
                return int.MaxValue;
            }

            return (int)prop.GetValue(comp, null);
        }

        private static void SpendRound(ThingComp comp)
        {
            MethodInfo method = comp.GetType().GetMethod("TryReduceAmmoCount", BindingFlags.Public | BindingFlags.Instance);
            if (method != null)
            {
                ParameterInfo[] ps = method.GetParameters();
                object[] args = ps.Length == 0 ? new object[0] : new object[] { 1 };
                method.Invoke(comp, args);
                return;
            }

            PropertyInfo prop = comp.GetType().GetProperty("CurMagCount", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(comp, Mathf.Max(0, (int)prop.GetValue(comp, null) - 1), null);
                return;
            }

            LogOnce("не найден TryReduceAmmoCount у " + AmmoUserTypeName + "; луч не тратит патроны");
        }

        private static void LogOnce(string message)
        {
            if (!errorLogged)
            {
                errorLogged = true;
                Log.Warning("[Watcher/BeamCE] " + message);
            }
        }

        // Без патрона в магазине выстрел недоступен (CE сам даст команду на перезарядку).
        public override bool Available()
        {
            if (!base.Available())
            {
                return false;
            }

            ThingComp comp = FindAmmoComp();
            return comp == null || CurrentMagazine(comp) > 0;
        }

        // Один луч = один патрон, списываем в момент выстрела.
        public override void WarmupComplete()
        {
            ThingComp comp = FindAmmoComp();
            if (comp != null)
            {
                SpendRound(comp);
            }

            base.WarmupComplete();
        }
    }

    // ------------------------------------------------------------------
    // Письмо с картинкой: баннер сверху, текст снизу, кнопки внизу.
    // Если картинка не найдена, открывается обычное письмо.
    // ------------------------------------------------------------------
    // Def "картинка для письма": привязывает картинку к письму по ключу, XML без правок C#.
    //   letterKey: defName события (IncidentDef) или ключ заголовка письма (например HSK_MarkerWaveLabel).
    //   imagePath: путь внутри Textures, без расширения (например UI/Letters/HSK_MarkerStrike).
    public class LetterImageDef : Def
    {
        public string letterKey;
        public string imagePath;
    }

    // Единая точка отправки писем мода: если для ключа задан LetterImageDef, письмо с картинкой,
    // иначе обычное. Новые события просто вызывают HSK_Letters.Send(...).
    public static class HSK_Letters
    {
        private static Dictionary<string, string> images;

        public static void Send(string key, string label, string text, LetterDef def, LookTargets targets)
        {
            string path = FindImage(key);
            if (path == null)
            {
                Find.LetterStack.ReceiveLetter(label, text, def, targets);
                return;
            }

            ImageLetter.Send(label, text, def, targets, path);
        }

        public static string FindImagePath(string key)
        {
            return FindImage(key);
        }

        private static string FindImage(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (images == null)
            {
                images = new Dictionary<string, string>();
                List<LetterImageDef> defs = DefDatabase<LetterImageDef>.AllDefsListForReading;
                for (int i = 0; i < defs.Count; i++)
                {
                    if (!string.IsNullOrEmpty(defs[i].letterKey) && !string.IsNullOrEmpty(defs[i].imagePath))
                    {
                        images[defs[i].letterKey] = defs[i].imagePath;
                    }
                }
            }

            string result;
            return images.TryGetValue(key, out result) ? result : null;
        }
    }

    public class ImageLetter : Letter
    {
        public string imagePath;
        public string bodyText;

        public static void Send(string label, string text, LetterDef def, LookTargets targets, string imagePath)
        {
            ImageLetter letter = new ImageLetter();
            letter.def = def;
            letter.ID = Find.UniqueIDsManager.GetNextLetterID();
            letter.Label = label;
            letter.bodyText = text;
            letter.lookTargets = targets;
            letter.imagePath = imagePath;
            Find.LetterStack.ReceiveLetter(letter);
        }

        // Текст во всплывающей подсказке при наведении на письмо.
        protected override string GetMouseoverText()
        {
            return bodyText ?? string.Empty;
        }

        // Финальное письмо: не кладётся в стек, показывается сразу с одной кнопкой.
        // onClosed вызывается при любом закрытии окна.
        public static void ShowFinal(string key, string label, string text, System.Action onClosed)
        {
            string path = HSK_Letters.FindImagePath(key);
            Texture2D image = string.IsNullOrEmpty(path) ? null : ContentFinder<Texture2D>.Get(path, false);

            ImageLetter letter = new ImageLetter();
            letter.def = LetterDefOf.ThreatBig;
            letter.ID = Find.UniqueIDsManager.GetNextLetterID();
            letter.Label = label;
            letter.bodyText = text;
            letter.imagePath = path;

            Dialog_ImageLetter dialog = new Dialog_ImageLetter(letter, image);
            dialog.finalMode = true;
            dialog.onClosed = onClosed;
            Find.WindowStack.Add(dialog);
        }

        public override void OpenLetter()
        {
            // Если картинки нет, окно открывается без баннера.
            Texture2D image = string.IsNullOrEmpty(imagePath) ? null : ContentFinder<Texture2D>.Get(imagePath, false);
            Find.WindowStack.Add(new Dialog_ImageLetter(this, image));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref imagePath, "imagePath");
            Scribe_Values.Look(ref bodyText, "bodyText");
        }
    }

    public class Dialog_ImageLetter : Window
    {
        private const float ButtonHeight = 38f;
        private const float ButtonGap = 10f;
        private const float ImageAspect = 8f / 3f;

        private readonly ImageLetter letter;
        private readonly Texture2D image;
        private Vector2 scroll;

        // Финальный режим: одна кнопка "Закрыть", без отложить/перейти.
        public bool finalMode;

        // Вызывается один раз при закрытии окна.
        public System.Action onClosed;

        public override void PostClose()
        {
            base.PostClose();
            System.Action callback = onClosed;
            onClosed = null;
            if (callback != null)
            {
                callback();
            }
        }

        public Dialog_ImageLetter(ImageLetter letter, Texture2D image)
        {
            this.letter = letter;
            this.image = image;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(700f, 620f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;

            // Заголовок.
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(inRect.x, y, inRect.width - 30f, 34f);
            Widgets.Label(titleRect, letter.Label);
            y += 38f;

            // Картинка: баннер с рамкой, обрезается по центру.
            if (image != null)
            {
                float imageWidth = inRect.width;
                float imageHeight = imageWidth / ImageAspect;
                Rect imageRect = new Rect(inRect.x, y, imageWidth, imageHeight);
                GUI.DrawTexture(imageRect, image, ScaleMode.ScaleAndCrop);
                Widgets.DrawBox(imageRect);
                y += imageHeight + 12f;
            }

            // Текст с прокруткой.
            Text.Font = GameFont.Small;
            float textBottom = inRect.yMax - ButtonHeight - 12f;
            Rect outRect = new Rect(inRect.x, y, inRect.width, textBottom - y);
            string body = letter.bodyText ?? string.Empty;
            float viewWidth = outRect.width - 18f;
            float textHeight = Text.CalcHeight(body, viewWidth);
            Rect viewRect = new Rect(0f, 0f, viewWidth, textHeight);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            Widgets.Label(new Rect(0f, 0f, viewWidth, textHeight), body);
            Widgets.EndScrollView();

            // Кнопки: закрыть, отложить, перейти к месту.
            bool canJump = !finalMode && letter.lookTargets != null && letter.lookTargets.IsValid();
            int count = finalMode ? 1 : (canJump ? 3 : 2);
            float buttonWidth = (inRect.width - ButtonGap * (count - 1)) / count;
            float x = inRect.x;
            float by = inRect.yMax - ButtonHeight;

            if (Widgets.ButtonText(new Rect(x, by, buttonWidth, ButtonHeight), "HSK_Letter_Close".Translate()))
            {
                Find.LetterStack.RemoveLetter(letter);
                Close();
            }

            if (!finalMode)
            {
                x += buttonWidth + ButtonGap;
                if (Widgets.ButtonText(new Rect(x, by, buttonWidth, ButtonHeight), "HSK_Letter_Postpone".Translate()))
                {
                    Close();
                }
            }

            if (canJump)
            {
                x += buttonWidth + ButtonGap;
                if (Widgets.ButtonText(new Rect(x, by, buttonWidth, ButtonHeight), "HSK_Letter_Jump".Translate()))
                {
                    Find.LetterStack.RemoveLetter(letter);
                    Close();
                    CameraJumper.TryJumpAndSelect(letter.lookTargets.TryGetPrimaryTarget());
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Зов Апофеоза: верующие юнитологи впадают в растерянность и получают мысль "Сделай нас единым".
    // Растерянность: колонист бесцельно бродит рядом, пока не придёт в себя.
    // ------------------------------------------------------------------
    public static class UnityBelievers
    {
        public const string ConfusionDefName = "HSK_Confusion";
        public const string ThoughtDefName = "HSK_MakeUsOne";

        public static void Afflict(Map map)
        {
            if (map == null)
            {
                return;
            }

            MentalStateDef confusion = DefDatabase<MentalStateDef>.GetNamedSilentFail(ConfusionDefName);
            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(ThoughtDefName);

            List<Pawn> colonists = new List<Pawn>(map.mapPawns.FreeColonistsSpawned);
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn.Dead || !UnitologyUtility.IsUnitologist(pawn))
                {
                    continue;
                }

                if (thought != null && pawn.needs != null && pawn.needs.mood != null)
                {
                    pawn.needs.mood.thoughts.memories.TryGainMemory(thought);
                }

                if (confusion != null && !pawn.Downed && !pawn.InMentalState)
                {
                    pawn.mindState.mentalStateHandler.TryStartMentalState(
                        confusion,
                        reason: "HSK_Confusion_Reason".Translate(),
                        forced: true);
                }
            }
        }
    }

    // Ведёт растерянных колонистов: пока состояние HSK_Confusion, им выдаётся короткая прогулка рядом.
    public class MapComponent_Confusion : MapComponent
    {
        private const int CheckInterval = 90;
        private const float WanderRadius = 9f;

        private MentalStateDef confusion;
        private bool resolved;

        public MapComponent_Confusion(Map map)
            : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % CheckInterval != 0)
            {
                return;
            }

            if (!resolved)
            {
                confusion = DefDatabase<MentalStateDef>.GetNamedSilentFail(UnityBelievers.ConfusionDefName);
                resolved = true;
            }

            if (confusion == null)
            {
                return;
            }

            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn.Downed || pawn.Dead || pawn.MentalState == null || pawn.MentalState.def != confusion)
                {
                    continue;
                }

                Job current = pawn.CurJob;
                if (current != null && current.def == JobDefOf.Goto)
                {
                    continue;
                }

                IntVec3 cell;
                Pawn p = pawn;
                if (CellFinder.TryFindRandomCellNear(
                    pawn.Position,
                    map,
                    (int)WanderRadius,
                    delegate (IntVec3 c) { return c.Standable(map) && p.CanReach(c, PathEndMode.OnCell, Danger.Some); },
                    out cell))
                {
                    Job wander = JobMaker.MakeJob(JobDefOf.Goto, cell);
                    wander.expiryInterval = 400;
                    pawn.jobs.StartJob(wander, JobCondition.InterruptForced);
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // Концовка "Конвергенция": Апофеоз Плоти жив, а на его карте (домашней) не осталось ни одного
    // живого колониста. Показывается письмо с картинкой, после его закрытия титры и выход в меню.
    // Один раз за сохранение.
    // ------------------------------------------------------------------
    public class GameComponent_Convergence : GameComponent
    {
        private const int CheckInterval = 60;

        private bool converged;

        public GameComponent_Convergence(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref converged, "converged", false);
        }

        public override void GameComponentTick()
        {
            if (converged || Find.TickManager.TicksGame < 300 || Find.TickManager.TicksGame % CheckInterval != 0)
            {
                return;
            }

            if (!ConvergenceReached())
            {
                return;
            }

            converged = true;
            ShowEnding();
        }

        // Есть домашняя карта, на которой живёт Апофеоз и нет ни одного свободного колониста.
        private static bool ConvergenceReached()
        {
            PawnKindDef kind = HSK_AscensionDefOf.Apotheosis;
            if (kind == null)
            {
                return false;
            }

            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (!map.IsPlayerHome || map.mapPawns.FreeColonistsSpawnedOrInPlayerEjectablePodsCount > 0)
                {
                    continue;
                }

                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int j = 0; j < pawns.Count; j++)
                {
                    if (pawns[j].kindDef == kind && !pawns[j].Dead)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void ShowEnding()
        {
            // Обычное письмо "Игра окончена" нам не нужно.
            Letter gameOver = Find.LetterStack.LettersListForReading.Find(l => l.def == LetterDefOf.GameEnded);
            if (gameOver != null)
            {
                Find.LetterStack.RemoveLetter(gameOver);
            }

            string credits = GameVictoryUtility.MakeEndCredits(
                "HSK_Convergence_Intro".Translate(),
                "HSK_Convergence_Ending".Translate(),
                string.Empty);

            // Письмо с картинкой; после закрытия титры и выход в главное меню.
            ImageLetter.ShowFinal(
                "HSK_Convergence",
                "HSK_Convergence_Label".Translate(),
                "HSK_Convergence_LetterText".Translate(),
                delegate { GameVictoryUtility.ShowCredits(credits, null, true); });
        }
    }

    public class MapComponent_MarkerStrike : MapComponent
    {
        private const int CheckInterval = 60;
        // Через столько тиков после гибели маркера челнок улетает в любом случае (9000 = 2.5 мин).
        private const int LiftoffTicks = 9000;
        // После взлёта оставшиеся (раненые, не успевшие уйти) ещё столько тиков считаются частью события.
        private const int AfterLiftoffTicks = 3600;
        private const float BoardDistance = 6f;

        private const int PhaseNone = 0;
        private const int PhaseAssault = 1;
        private const int PhaseLeaving = 2;

        private List<Pawn> squad = new List<Pawn>();
        private Thing shuttle;
        private Lord lord;
        private IntVec3 boardCell = IntVec3.Invalid;
        private int phase;
        private int phaseStartTick;
        private int incomingUntilTick;
        private int boarded;
        private bool fleeing;
        private bool escaped;
        private int reachFails;
        private bool launched;
        private int launchTick;

        public MapComponent_MarkerStrike(Map map)
            : base(map)
        {
        }

        public bool Active
        {
            get { return phase != PhaseNone || Find.TickManager.TicksGame < incomingUntilTick; }
        }

        // Челнок уже в пути: другого десанта в это время быть не должно.
        public void MarkIncoming()
        {
            incomingUntilTick = Find.TickManager.TicksGame + 1200;
        }

        public void Begin(Thing shuttle, List<Pawn> squad, Lord lord, IntVec3 boardCell)
        {
            this.shuttle = shuttle;
            this.squad = new List<Pawn>(squad);
            this.lord = lord;
            this.boardCell = boardCell;
            phase = PhaseAssault;
            phaseStartTick = Find.TickManager.TicksGame;
            incomingUntilTick = 0;
            boarded = 0;
            fleeing = false;
            escaped = false;
            reachFails = 0;
            launched = false;
            launchTick = 0;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref squad, "squad", LookMode.Reference);
            Scribe_References.Look(ref shuttle, "shuttle");
            Scribe_References.Look(ref lord, "lord");
            Scribe_Values.Look(ref boardCell, "boardCell", IntVec3.Invalid);
            Scribe_Values.Look(ref phase, "phase", PhaseNone);
            Scribe_Values.Look(ref phaseStartTick, "phaseStartTick", 0);
            Scribe_Values.Look(ref incomingUntilTick, "incomingUntilTick", 0);
            Scribe_Values.Look(ref boarded, "boarded", 0);
            Scribe_Values.Look(ref fleeing, "fleeing", false);
            Scribe_Values.Look(ref escaped, "escaped", false);
            Scribe_Values.Look(ref reachFails, "reachFails", 0);
            Scribe_Values.Look(ref launched, "launched", false);
            Scribe_Values.Look(ref launchTick, "launchTick", 0);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (squad == null)
                {
                    squad = new List<Pawn>();
                }

                squad.RemoveAll(p => p == null);
            }
        }

        public override void MapComponentTick()
        {
            if (phase == PhaseNone || Find.TickManager.TicksGame % CheckInterval != 0)
            {
                return;
            }

            PruneSquad();

            if (phase == PhaseAssault)
            {
                CheckAssault();
            }
            else if (phase == PhaseLeaving)
            {
                CheckLeaving();
            }
        }

        private void PruneSquad()
        {
            for (int i = squad.Count - 1; i >= 0; i--)
            {
                Pawn p = squad[i];
                if (p == null || p.Destroyed || p.Dead || p.Faction != Faction.OfAncientsHostile)
                {
                    squad.RemoveAt(i);
                }
                else if (p.Map != map)
                {
                    // Покинул карту сам (побег) - считаем скрывшимся.
                    if (fleeing)
                    {
                        escaped = true;
                    }

                    squad.RemoveAt(i);
                }
            }
        }

        private void CheckAssault()
        {
            if (squad.Count == 0)
            {
                Finish();
                return;
            }

            if (MarkerStrikeUtility.FindMarker(map, IntVec3.Zero) == null)
            {
                BeginLeaving();
            }
        }

        private void BeginLeaving()
        {
            phase = PhaseLeaving;
            phaseStartTick = Find.TickManager.TicksGame;

            HSK_Letters.Send(
                "HSK_MarkerStrike_DestroyedLabel",
                "HSK_MarkerStrike_DestroyedLabel".Translate(),
                "HSK_MarkerStrike_DestroyedText".Translate(),
                LetterDefOf.NegativeEvent,
                new LookTargets(squad));

            string reason;
            if (CanBoard(out reason))
            {
                SetLord(new LordJob_StrikeBoard(boardCell));
            }
            else
            {
                StartFlee();
            }
        }

        // Челнок цел, и хотя бы один боец может до него дойти.
        private bool CanBoard(out string reason)
        {
            reason = null;
            if (shuttle == null)
            {
                reason = "челнок не задан";
                return false;
            }

            if (shuttle.Destroyed || !shuttle.Spawned)
            {
                reason = "челнок уничтожен или не на карте";
                return false;
            }

            if (!boardCell.IsValid)
            {
                reason = "точка посадки не задана";
                return false;
            }

            for (int i = 0; i < squad.Count; i++)
            {
                Pawn p = squad[i];
                if (p.Spawned && !p.Downed && p.CanReach(boardCell, PathEndMode.OnCell, Danger.Deadly, true))
                {
                    return true;
                }
            }

            reason = "ни один боец не может дойти до " + boardCell;
            return false;
        }

        private void CheckLeaving()
        {
            if (squad.Count == 0)
            {
                Finish();
                return;
            }

            int now = Find.TickManager.TicksGame;

            // Челнок улетает через время в любом случае.
            if (!launched && now - phaseStartTick > LiftoffTicks)
            {
                LaunchShuttle();
                if (!fleeing)
                {
                    StartFlee();
                }

                return;
            }

            if (launched)
            {
                if (now - launchTick > AfterLiftoffTicks)
                {
                    Finish();
                }

                return;
            }

            if (fleeing)
            {
                return;
            }

            string reason;
            if (!CanBoard(out reason))
            {
                bool shuttleLost = shuttle == null || shuttle.Destroyed || !shuttle.Spawned || !boardCell.IsValid;
                reachFails++;
                if (shuttleLost || reachFails >= 15)
                {
                    StartFlee();
                }

                return;
            }

            reachFails = 0;

            AssignRescuers();

            List<Pawn> copy = new List<Pawn>(squad);
            for (int i = 0; i < copy.Count; i++)
            {
                Pawn p = copy[i];
                if (!p.Spawned || !p.Position.InHorDistOf(boardCell, BoardDistance))
                {
                    // Лежачих, которых несут, тоже ждём у челнока; несущий сначала донесёт.
                    if (!(p.Spawned && p.carryTracker != null && p.carryTracker.CarriedThing is Pawn
                        && p.Position.InHorDistOf(boardCell, 3f)))
                    {
                        continue;
                    }
                }

                Pawn carried = p.carryTracker == null ? null : p.carryTracker.CarriedThing as Pawn;
                if (carried != null)
                {
                    if (!p.Position.InHorDistOf(boardCell, 3f))
                    {
                        continue;
                    }

                    Thing dropped;
                    p.carryTracker.TryDropCarriedThing(p.Position, ThingPlaceMode.Near, out dropped);
                }

                if (!p.Spawned)
                {
                    continue;
                }

                boarded++;
                squad.Remove(p);
                p.ExitMap(false, Rot4.Invalid);
            }

            if (squad.Count == 0)
            {
                Finish();
                return;
            }
        }

        // Здоровые бойцы подбирают лежачих товарищей и несут к челноку.
        private void AssignRescuers()
        {
            JobDef carryDef = HSK_StrikeDefOf.HSK_CarryAlly;
            if (carryDef == null)
            {
                return;
            }

            for (int i = 0; i < squad.Count; i++)
            {
                Pawn downed = squad[i];
                if (!downed.Spawned || !downed.Downed || downed.Position.InHorDistOf(boardCell, BoardDistance))
                {
                    continue;
                }

                bool taken = false;
                Pawn best = null;
                float bestDistance = float.MaxValue;
                for (int j = 0; j < squad.Count; j++)
                {
                    Pawn p = squad[j];
                    if (p == downed || !p.Spawned || p.Downed)
                    {
                        continue;
                    }

                    Job cur = p.CurJob;
                    if (cur != null && cur.def == carryDef && cur.GetTarget(TargetIndex.A).Thing == downed)
                    {
                        taken = true;
                        break;
                    }

                    if ((cur != null && cur.def == carryDef) || p.carryTracker == null || p.carryTracker.CarriedThing != null)
                    {
                        continue;
                    }

                    float d = p.Position.DistanceTo(downed.Position);
                    if (d < bestDistance && p.CanReach(downed, PathEndMode.Touch, Danger.Deadly, true))
                    {
                        best = p;
                        bestDistance = d;
                    }
                }

                if (taken || best == null)
                {
                    continue;
                }

                Job job = JobMaker.MakeJob(carryDef, downed, boardCell);
                job.count = 1;
                best.jobs.StartJob(job, JobCondition.InterruptForced);
            }
        }

        private void StartFlee()
        {
            fleeing = true;
            SetLord(new LordJob_StrikeFlee());
        }

        private void SetLord(LordJob job)
        {
            if (lord != null)
            {
                map.lordManager.RemoveLord(lord);
            }

            lord = LordMaker.MakeNewLord(Faction.OfAncientsHostile, job, map, squad);
        }

        // Челнок взлетает (один раз): здание убирается, вместо него улетающий скайфоллер.
        private void LaunchShuttle()
        {
            if (launched)
            {
                return;
            }

            launched = true;
            launchTick = Find.TickManager.TicksGame;

            if (shuttle != null && !shuttle.Destroyed)
            {
                IncidentDef incident = DefDatabase<IncidentDef>.GetNamedSilentFail("HSK_MarkerStrike");
                MarkerStrikeExtension ext = incident == null ? null : incident.GetModExtension<MarkerStrikeExtension>();
                ThingDef leavingDef = ext == null ? null : ext.shuttleLeavingDef;

                if (shuttle.Spawned)
                {
                    IntVec3 pos = shuttle.Position;
                    shuttle.Destroy();

                    if (leavingDef != null)
                    {
                        // Челнок взлетает: ванильный скайфоллер с reversed = true.
                        Skyfaller leaving = SkyfallerMaker.MakeSkyfaller(leavingDef);
                        GenSpawn.Spawn(leaving, pos, map);
                    }
                    else
                    {
                        for (int i = 0; i < 14; i++)
                        {
                            Vector3 offset = new Vector3(Rand.Range(-3f, 3f), 0f, Rand.Range(-2f, 2f));
                            FleckMaker.ThrowDustPuff(pos.ToVector3Shifted() + offset, map, Rand.Range(1.5f, 3f));
                        }
                    }
                }
                else
                {
                    shuttle.Destroy();
                }
            }

            shuttle = null;
        }

        private void Finish()
        {
            LaunchShuttle();

            if (lord != null)
            {
                map.lordManager.RemoveLord(lord);
            }

            string key;
            if (boarded > 0)
            {
                key = "HSK_MarkerStrike_Departed";
            }
            else if (escaped)
            {
                key = "HSK_MarkerStrike_Fled";
            }
            else
            {
                key = "HSK_MarkerStrike_Wiped";
            }

            Messages.Message(key.Translate(), MessageTypeDefOf.NeutralEvent);

            squad.Clear();
            shuttle = null;
            lord = null;
            launched = false;
            phase = PhaseNone;
            boarded = 0;
            fleeing = false;
            escaped = false;
        }
    }

    // ------------------------------------------------------------------
    // Челнок прилетает как скайфоллер: при посадке ставит здание-челнок,
    // выпускает бойцов и запускает ход события.
    // ------------------------------------------------------------------
    public class Skyfaller_StrikeShuttle : Skyfaller
    {
        protected override void SpawnThings()
        {
            Map map = base.Map;
            IntVec3 landing = base.Position;

            // Бойцов временно достаём, чтобы сначала встало здание челнока.
            List<Pawn> squad = new List<Pawn>();
            for (int i = innerContainer.Count - 1; i >= 0; i--)
            {
                Pawn pawn = innerContainer[i] as Pawn;
                if (pawn != null)
                {
                    squad.Add(pawn);
                }
            }

            for (int i = 0; i < squad.Count; i++)
            {
                innerContainer.Remove(squad[i]);
            }

            Thing shuttle = innerContainer.Count > 0 ? innerContainer[0] : null;
            base.SpawnThings();

            IntVec3 center = (shuttle != null && shuttle.Spawned) ? shuttle.Position : landing;
            for (int i = 0; i < squad.Count; i++)
            {
                IntVec3 spot;
                if (!CellFinder.TryFindRandomCellNear(
                    center,
                    map,
                    6,
                    delegate (IntVec3 c) { return c.Standable(map) && c.GetEdifice(map) == null; },
                    out spot))
                {
                    spot = center;
                }

                GenSpawn.Spawn(squad[i], spot, map, Rot4.Random);
            }

            IntVec3 boardCell;
            if (!CellFinder.TryFindRandomCellNear(
                center,
                map,
                5,
                delegate (IntVec3 c) { return c.Standable(map) && c.GetEdifice(map) == null; },
                out boardCell))
            {
                boardCell = center;
            }

            MapComponent_MarkerStrike comp = map.GetComponent<MapComponent_MarkerStrike>();
            if (comp != null && squad.Count > 0)
            {
                Lord lord = LordMaker.MakeNewLord(Faction.OfAncientsHostile, new LordJob_MarkerStrike(), map, squad);
                comp.Begin(shuttle, squad, lord, boardCell);
            }
        }
    }
}