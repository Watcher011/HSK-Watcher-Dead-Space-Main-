// ============================================================
// Watcher — Чёрный маркер
// Совместимо с C# 7.3. using-директивы перенесены внутрь каждого namespace,
// поэтому части не влияют друг на друга (как и в отдельных файлах).
//
// Содержимое:
//   1. Чёрный маркер: здание, изучение, энергия, срыв, интерес
//   2. Вознесение: шкала, поглощение трупов корнями, руда
//   3. Вознесение: DefOf, отбор трупов, волны, Единение
// ============================================================

// ------------------------------------------------------------
// Чёрный маркер: здание, изучение, энергия, срыв, интерес  [split/BM_Marker.cs]
// ------------------------------------------------------------
namespace HSK.Watcher.DeadSpace
{
    using RimWorld;
    using System.Collections.Generic;
    using UnityEngine;
    using Verse;
    using Verse.AI;

    // ------------------------------------------------------------------
    // DefOf
    // ------------------------------------------------------------------
    [DefOf]
    public static class HSK_MarkerDefOf
    {
        public static ThingDef HSK_BlackMarker;
        public static JobDef HSK_StudyBlackMarker;
        public static MentalStateDef HSK_MarkerObsession;
        public static ThingDef HSK_ObeliskSculpture;
        public static PawnKindDef Infector;

        static HSK_MarkerDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HSK_MarkerDefOf));
        }
    }

    // ------------------------------------------------------------------
    // Настройки маркера в XML (modExtensions у ThingDef):
    //   studiedTexPath - текстура после изучения (красный маркер)
    //   studyWork      - сколько "очков исследования" нужно набрать
    //                    (тик работы колониста с ResearchSpeed=1 даёт 1 очко)
    // ------------------------------------------------------------------
    public class BlackMarkerExtension : DefModExtension
    {
        public string studiedTexPath = "Things/Building/Exotic/RedMarker";
        public float studyWork = 6000f;
    }

    // ------------------------------------------------------------------
    // Маркер: чёрный обелиск, после изучения становится красным.
    // ------------------------------------------------------------------
    public partial class Building_BlackMarker : Building
    {
        private bool studied;
        private bool studyDesignated;
        private float studyProgress;

        // Кэш графики красного маркера (не сохраняется).
        private Graphic studiedGraphic;

        public bool Studied
        {
            get { return studied; }
        }

        public bool StudyDesignated
        {
            get { return studyDesignated; }
        }

        public float StudyProgress
        {
            get { return studyProgress; }
        }

        private BlackMarkerExtension Ext
        {
            get { return def.GetModExtension<BlackMarkerExtension>(); }
        }

        public float StudyWork
        {
            get
            {
                BlackMarkerExtension ext = Ext;
                return ext != null ? ext.studyWork : 6000f;
            }
        }

        // Графика зависит от состояния: чёрная до изучения, красная после.
        public override Graphic Graphic
        {
            get
            {
                if (!studied)
                {
                    return base.Graphic;
                }

                if (studiedGraphic == null)
                {
                    BlackMarkerExtension ext = Ext;
                    if (ext == null || def.graphicData == null)
                    {
                        return base.Graphic;
                    }

                    // Копируем graphicData, чтобы сохранились drawSize и drawOffset, меняем только текстуру.
                    GraphicData copy = new GraphicData();
                    copy.CopyFrom(def.graphicData);
                    copy.texPath = ext.studiedTexPath;
                    studiedGraphic = copy.Graphic;
                }

                return studiedGraphic;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref studied, "studied", false);
            Scribe_Values.Look(ref studyDesignated, "studyDesignated", false);
            Scribe_Values.Look(ref studyProgress, "studyProgress", 0f);
            AscensionExposeData();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
            {
                yield return g;
            }

            if (!studied)
            {
                Command_Toggle cmd = new Command_Toggle();
                cmd.defaultLabel = "HSK_MarkerStudyLabel".Translate();
                cmd.defaultDesc = "HSK_MarkerStudyDesc".Translate();
                cmd.icon = TexCommand.Attack;
                cmd.isActive = delegate { return studyDesignated; };
                cmd.toggleAction = delegate { studyDesignated = !studyDesignated; };
                yield return cmd;
            }

            // Вознесение: приём трупов и dev-кнопки (см. Building_BlackMarker_Ascension.cs).
            foreach (Gizmo g in AscensionGizmos())
            {
                yield return g;
            }
        }

        public override string GetInspectString()
        {
            string baseStr = base.GetInspectString();
            string add;
            if (studied)
            {
                add = "HSK_MarkerInspectStudied".Translate();
            }
            else
            {
                add = "HSK_MarkerInspectProgress".Translate((studyProgress / StudyWork).ToStringPercent());
            }

            string ascensionStr = AscensionInspectString();
            if (!string.IsNullOrEmpty(ascensionStr))
            {
                add += "\n" + ascensionStr;
            }

            return string.IsNullOrEmpty(baseStr) ? add : baseStr + "\n" + add;
        }

        // Вызывается JobDriver каждый тик работы.
        public void AddStudyProgress(float amount, Pawn doer)
        {
            if (studied)
            {
                return;
            }

            studyProgress += amount;
            if (studyProgress < StudyWork)
            {
                return;
            }

            studyProgress = StudyWork;
            studied = true;
            studyDesignated = false;

            if (Spawned)
            {
                // Перерисовать карту, чтобы подставилась красная текстура.
                DirtyMapMesh(Map);

                string who = doer != null ? doer.LabelShort : string.Empty;
                Messages.Message(
                    "HSK_MarkerStudied".Translate(who),
                    new LookTargets(this),
                    MessageTypeDefOf.PositiveEvent);

                // Юнитология: после изучения маркера колонистам предлагается реформация идеологии.
                UnitologyReformation.Trigger(this);
            }
        }
    }

    // ------------------------------------------------------------------
    // Энергия: 400 Вт у неизученного, studiedPowerOutput (4000) у изученного.
    // Подключается в XML: <li Class="HSK.Watcher.DeadSpace.CompProperties_PowerMarker">
    //   <basePowerConsumption>-400</basePowerConsumption>
    //   <studiedPowerOutput>4000</studiedPowerOutput>
    // ------------------------------------------------------------------
    public class CompProperties_PowerMarker : CompProperties_Power
    {
        public float studiedPowerOutput = 4000f;

        public CompProperties_PowerMarker()
        {
            compClass = typeof(CompPowerPlantMarker);
        }
    }

    public class CompPowerPlantMarker : CompPowerPlant
    {
        private CompProperties_PowerMarker MarkerProps
        {
            get { return (CompProperties_PowerMarker)props; }
        }

        protected override float DesiredPowerOutput
        {
            get
            {
                Building_BlackMarker marker = parent as Building_BlackMarker;
                if (marker != null && marker.Studied)
                {
                    return MarkerProps.studiedPowerOutput;
                }

                // До изучения: -basePowerConsumption = 400.
                return base.DesiredPowerOutput;
            }
        }
    }

    // ------------------------------------------------------------------
    // Работа "изучить маркер": появляется у колонистов с включённой работой
    // "Исследование", когда на маркере включена кнопка "Изучить".
    // ------------------------------------------------------------------
    public class WorkGiver_StudyBlackMarker : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest
        {
            get { return ThingRequest.ForDef(HSK_MarkerDefOf.HSK_BlackMarker); }
        }

        public override PathEndMode PathEndMode
        {
            get { return PathEndMode.Touch; }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Building_BlackMarker marker = t as Building_BlackMarker;
            if (marker == null || marker.Studied || !marker.StudyDesignated)
            {
                return false;
            }

            if (t.IsForbidden(pawn))
            {
                return false;
            }

            return pawn.CanReserve(t, 1, -1, null, forced);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobMaker.MakeJob(HSK_MarkerDefOf.HSK_StudyBlackMarker, t);
        }
    }

    public class JobDriver_StudyBlackMarker : JobDriver
    {
        private Building_BlackMarker Marker
        {
            get { return job.targetA.Thing as Building_BlackMarker; }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            // Работа от кнопки "Изучить" требует включённой кнопки; "самостоятельный интерес"
            // и срыв (ignoreDesignations) её игнорируют.
            this.FailOn(() => Marker == null || Marker.Studied || (!Marker.StudyDesignated && !job.ignoreDesignations));

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil study = new Toil();
            study.initAction = delegate
            {
                pawn.pather.StopDead();
            };
            study.tickAction = delegate
            {
                Building_BlackMarker marker = Marker;
                if (marker == null)
                {
                    return;
                }

                pawn.rotationTracker.FaceTarget(marker);

                if (pawn.skills != null)
                {
                    pawn.skills.Learn(SkillDefOf.Intellectual, 0.1f);
                }

                float speed = pawn.GetStatValue(StatDefOf.ResearchSpeed);
                if (pawn.InMentalState)
                {
                    // Одержимый изучает быстрее.
                    speed *= 1.5f;
                }

                marker.AddStudyProgress(speed, pawn);
                if (marker.Studied)
                {
                    ReadyForNextToil();
                }
            };
            study.defaultCompleteMode = ToilCompleteMode.Never;
            study.WithProgressBar(TargetIndex.A, delegate
            {
                Building_BlackMarker marker = Marker;
                return marker != null ? marker.StudyProgress / marker.StudyWork : 1f;
            });
            yield return study;
        }
    }

    // ------------------------------------------------------------------
    // Общие функции поиска маркера и создания работы "изучить".
    // ------------------------------------------------------------------
    public static class MarkerUtility
    {
        // Есть ли на карте хоть один неизученный маркер.
        public static bool AnyUnstudied(Map map)
        {
            if (map == null)
            {
                return false;
            }

            List<Thing> things = map.listerThings.ThingsOfDef(HSK_MarkerDefOf.HSK_BlackMarker);
            for (int i = 0; i < things.Count; i++)
            {
                Building_BlackMarker m = things[i] as Building_BlackMarker;
                if (m != null && !m.Studied)
                {
                    return true;
                }
            }

            return false;
        }

        // Ближайший неизученный маркер, до которого может дойти пешка и который не занят.
        public static Building_BlackMarker FindUnstudied(Pawn pawn)
        {
            if (pawn == null || pawn.Map == null)
            {
                return null;
            }

            Building_BlackMarker best = null;
            float bestDist = float.MaxValue;
            List<Thing> things = pawn.Map.listerThings.ThingsOfDef(HSK_MarkerDefOf.HSK_BlackMarker);
            for (int i = 0; i < things.Count; i++)
            {
                Building_BlackMarker m = things[i] as Building_BlackMarker;
                if (m == null || m.Studied)
                {
                    continue;
                }

                if (!pawn.CanReserve(m) || !pawn.CanReach(m, PathEndMode.Touch, Danger.Deadly))
                {
                    continue;
                }

                float dist = (m.Position - pawn.Position).LengthHorizontalSquared;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = m;
                }
            }

            return best;
        }

        // Работа, не требующая включённой кнопки "Изучить"; истекает через час,
        // чтобы пешка могла поесть/поспать.
        public static Job MakeStudyJob(Building_BlackMarker marker)
        {
            Job job = JobMaker.MakeJob(HSK_MarkerDefOf.HSK_StudyBlackMarker, marker);
            job.ignoreDesignations = true;
            job.expiryInterval = 2500;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }

    // ------------------------------------------------------------------
    // Срыв "одержимость маркером": колонист непрерывно изучает маркер.
    // ------------------------------------------------------------------
    public class MentalBreakWorker_MarkerObsession : MentalBreakWorker
    {
        // Срыв возможен только если на карте есть неизученный маркер.
        public override bool BreakCanOccur(Pawn pawn)
        {
            return base.BreakCanOccur(pawn) && MarkerUtility.AnyUnstudied(pawn.Map);
        }
    }

    public class MentalStateWorker_MarkerObsession : MentalStateWorker
    {
        public override bool StateCanOccur(Pawn pawn)
        {
            return base.StateCanOccur(pawn) && MarkerUtility.AnyUnstudied(pawn.Map);
        }
    }

    // Узел дерева мыслей для состояния (подключается патчем в Patches/MarkerObsession.xml).
    public class JobGiver_MarkerObsession : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned)
            {
                return null;
            }

            // Все маркеры изучены - срыв заканчивается.
            if (!MarkerUtility.AnyUnstudied(pawn.Map))
            {
                if (pawn.MentalState != null && pawn.MentalStateDef == HSK_MarkerDefOf.HSK_MarkerObsession)
                {
                    pawn.MentalState.RecoverFromState();
                }

                return null;
            }

            Building_BlackMarker marker = MarkerUtility.FindUnstudied(pawn);
            if (marker == null)
            {
                return null;
            }

            return MarkerUtility.MakeStudyJob(marker);
        }
    }

    // ------------------------------------------------------------------
    // Время от времени свободный колонист сам заинтересовывается маркером
    // и идёт его изучать (даже если кнопка "Изучить" выключена).
    // Не требует правок деревьев мыслей: работа выдаётся напрямую,
    // только тем, кто сейчас бездельничает.
    // ------------------------------------------------------------------
    public class MapComponent_MarkerFascination : MapComponent
    {
        // Как часто проверяем (тики) и с какой вероятностью кто-то заинтересуется.
        private const int CheckIntervalTicks = 1500;
        private const float FascinationChance = 0.2f;

        public MapComponent_MarkerFascination(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (!map.IsPlayerHome || Find.TickManager.TicksGame % CheckIntervalTicks != 0)
            {
                return;
            }

            if (!MarkerUtility.AnyUnstudied(map) || !Rand.Chance(FascinationChance))
            {
                return;
            }

            List<Pawn> candidates = new List<Pawn>();
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (IsIdleAndCapable(p))
                {
                    candidates.Add(p);
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            // Чем выше интеллект, тем вероятнее заинтересуется.
            Pawn pawn = candidates.RandomElementByWeight(GetWeight);
            Building_BlackMarker marker = MarkerUtility.FindUnstudied(pawn);
            if (marker == null)
            {
                return;
            }

            pawn.jobs.StartJob(MarkerUtility.MakeStudyJob(marker), JobCondition.InterruptForced);

            Messages.Message(
                "HSK_MarkerFascinated".Translate(pawn.LabelShort),
                new LookTargets(pawn),
                MessageTypeDefOf.NeutralEvent);
        }

        private static float GetWeight(Pawn pawn)
        {
            if (pawn.skills == null)
            {
                return 1f;
            }

            return 1f + pawn.skills.GetSkill(SkillDefOf.Intellectual).Level;
        }

        // Свободен, в сознании, не в срыве и ничем важным не занят.
        private static bool IsIdleAndCapable(Pawn p)
        {
            if (p.Drafted || p.Downed || p.InMentalState || !p.Awake() || p.WorkTypeIsDisabled(WorkTypeDefOf.Research))
            {
                return false;
            }

            Job cur = p.CurJob;
            if (cur == null)
            {
                return true;
            }

            JobDef d = cur.def;
            return d == JobDefOf.Wait_Wander
                || d == JobDefOf.GotoWander
                || d == JobDefOf.Wait
                || d == JobDefOf.Wait_MaintainPosture;
        }
    }

}

// ------------------------------------------------------------
// Вознесение: шкала, поглощение трупов корнями, руда  [/root/.claude/uploads/a85e4491-08d3-53f7-b914-0e7ecd4ca2b9/7bd2546c-1792323063606_MarkerAscensionExtension.cs]
// ------------------------------------------------------------
namespace HSK.Watcher.DeadSpace
{
    using RimWorld;
    using System.Collections.Generic;
    using UnityEngine;
    using Verse;

    // ------------------------------------------------------------------
    // Настройки вознесения в XML (второй modExtension у ThingDef маркера).
    // Блок необязателен: без него действуют значения по умолчанию.
    //   unityCorpses      - сколько "очков трупов" нужно для Единения
    //   animalCorpseValue - сколько очков даёт труп животного (человек = 1)
    //   consumeRadius     - радиус, в котором корни маркера поглощают трупы
    //   consumeTicks      - сколько тиков корни держат труп, прежде чем поглотить его
    //   waveThresholds    - пороги вспышек на шкале; если пусто, через каждые 10% шкалы (существ они больше не вызывают)
    //   oreDefName        - руда, которую маркер выдаёт за поглощённые трупы
    //   orePerValue       - сколько руды за одно очко трупа (человек = 1)
    // ------------------------------------------------------------------
    public class MarkerAscensionExtension : DefModExtension
    {
        public float unityCorpses = 1000f;
        public float animalCorpseValue = 0.25f;
        public float consumeRadius = 6f;
        public int consumeTicks = 300;
        public List<float> waveThresholds;
        public string oreDefName = "Markerite";
        public float orePerValue = 30f;
    }

    // ------------------------------------------------------------------
    // Вознесение: после изучения маркер как древо предвестника поглощает корнями
    // трупы, лежащие в его радиусе (колонисты складывают их в зону трупов
    // вокруг него), и шкала заполняется, а за каждый поглощённый труп рядом с маркером остаётся руда Markerite.
    // Некроморфы выходят только при вознесении: полная шкала (по умолчанию 1000 трупов) вызывает Единение.
    // Эта часть класса хранит состояние шкалы; остальное - в BlackMarker.cs.
    // ------------------------------------------------------------------
    public partial class Building_BlackMarker
    {
        private const int DefaultWaveCount = 9;

        // Как часто маркер просматривает трупы вокруг (в тиках).
        private const int ConsumeCheckInterval = 30;

        private static readonly CachedTexture CorpseZoneIcon = new CachedTexture("UI/Icons/CorpseStockpileZone");

        private float ascension;
        private bool acceptCorpses = true;
        private int wavesTriggered;
        private bool unityReached;
        private float oreProgress;

        // Кэш порогов и руды (не сохраняются).
        private ThingDef cachedOreDef;
        private bool oreDefSearched;

        // Кэш порогов (не сохраняется).
        private List<float> cachedThresholds;

        // Корни и прогресс поглощения не сохраняются: после загрузки строятся заново.
        private Dictionary<Corpse, Mote> roots = new Dictionary<Corpse, Mote>();
        private Dictionary<Corpse, int> consumeProgress = new Dictionary<Corpse, int>();
        private List<Corpse> tmpCorpses = new List<Corpse>();
        private List<Corpse> tmpRemove = new List<Corpse>();
        private List<Thing> tmpThings = new List<Thing>();

        // Эффекты вокруг маркера (не сохраняются, создаются заново).
        private const float AuraFraction = 0.25f;
        private const float ChargedFraction = 0.66f;
        private const int UnityEffectTicks = 300;

        private Effecter feedEffecter;
        private Effecter auraEffecter;
        private Effecter chargedEffecter;
        private Effecter unityEffecter;
        private int unityEffectTicksLeft;

        private MarkerAscensionExtension AscensionExt
        {
            get { return def.GetModExtension<MarkerAscensionExtension>(); }
        }

        public float Ascension
        {
            get { return ascension; }
        }

        public bool UnityReached
        {
            get { return unityReached; }
        }

        // Очков до Единения.
        public float UnityTarget
        {
            get
            {
                MarkerAscensionExtension ext = AscensionExt;
                float target = ext != null ? ext.unityCorpses : 1000f;
                return Mathf.Max(1f, target);
            }
        }

        public float AnimalCorpseValue
        {
            get
            {
                MarkerAscensionExtension ext = AscensionExt;
                return ext != null ? ext.animalCorpseValue : 0.25f;
            }
        }

        public float ConsumeRadius
        {
            get
            {
                MarkerAscensionExtension ext = AscensionExt;
                return Mathf.Max(1f, ext != null ? ext.consumeRadius : 6f);
            }
        }

        private int ConsumeTicks
        {
            get
            {
                MarkerAscensionExtension ext = AscensionExt;
                int ticks = ext != null ? ext.consumeTicks : 300;
                return Mathf.Max(ConsumeCheckInterval, ticks);
            }
        }

        // Поглощает ли маркер трупы прямо сейчас.
        public bool AcceptsCorpses
        {
            get { return studied && acceptCorpses && !unityReached && Spawned; }
        }

        private List<float> Thresholds
        {
            get
            {
                if (cachedThresholds != null)
                {
                    return cachedThresholds;
                }

                List<float> result = new List<float>();
                MarkerAscensionExtension ext = AscensionExt;
                if (ext != null && ext.waveThresholds != null && ext.waveThresholds.Count > 0)
                {
                    result.AddRange(ext.waveThresholds);
                }
                else
                {
                    // 10%, 20% ... 90% шкалы.
                    for (int i = 1; i <= DefaultWaveCount; i++)
                    {
                        result.Add(UnityTarget * i / (DefaultWaveCount + 1));
                    }
                }

                result.Sort();
                cachedThresholds = result;
                return result;
            }
        }

        private void AscensionExposeData()
        {
            Scribe_Values.Look(ref ascension, "ascension", 0f);
            Scribe_Values.Look(ref acceptCorpses, "acceptCorpses", true);
            Scribe_Values.Look(ref wavesTriggered, "wavesTriggered", 0);
            Scribe_Values.Look(ref unityReached, "unityReached", false);
            Scribe_Values.Look(ref oreProgress, "oreProgress", 0f);
        }

        private IEnumerable<Gizmo> AscensionGizmos()
        {
            if (studied && !unityReached)
            {
                Command_Toggle cmd = new Command_Toggle();
                cmd.defaultLabel = "HSK_MarkerOfferLabel".Translate();
                cmd.defaultDesc = "HSK_MarkerOfferDesc".Translate();
                cmd.icon = TexCommand.Attack;
                cmd.isActive = delegate { return acceptCorpses; };
                cmd.toggleAction = delegate { acceptCorpses = !acceptCorpses; };
                yield return cmd;

                Command_Action zone = new Command_Action();
                zone.defaultLabel = "HSK_MarkerCorpseZoneLabel".Translate();
                zone.defaultDesc = "HSK_MarkerCorpseZoneDesc".Translate();
                zone.icon = CorpseZoneIcon.Texture;
                zone.action = CreateCorpseStockpile;
                yield return zone;
            }

            if (Prefs.DevMode && studied && !unityReached)
            {
                Command_Action plus10 = new Command_Action();
                plus10.defaultLabel = "DEV: +10 corpses";
                plus10.action = delegate { AddAscension(10f); };
                yield return plus10;

                Command_Action plus100 = new Command_Action();
                plus100.defaultLabel = "DEV: +100 corpses";
                plus100.action = delegate { AddAscension(100f); };
                yield return plus100;
            }
        }

        private string AscensionInspectString()
        {
            if (!studied)
            {
                return null;
            }

            if (unityReached)
            {
                return "HSK_MarkerAscensionUnity".Translate();
            }

            string text = "HSK_MarkerAscensionInspect".Translate(
                ascension.ToString("F0"),
                UnityTarget.ToString("F0"),
                (ascension / UnityTarget).ToStringPercent());

            if (AcceptsCorpses && consumeProgress.Count > 0)
            {
                text += "\n" + "HSK_MarkerConsuming".Translate().ToString();
            }

            return text;
        }

        // ---------------- Поглощение трупов корнями ----------------

        protected override void Tick()
        {
            base.Tick();

            if (!Spawned)
            {
                return;
            }

            if (this.IsHashIntervalTick(ConsumeCheckInterval))
            {
                AscensionTick();
            }

            EffectsTick();
        }

        // ---------------- Визуальные эффекты ----------------

        // Постоянные эффектеры включаются по состоянию маркера и обновляются каждый тик.
        private void EffectsTick()
        {
            bool awake = studied;
            float fraction = ascension / UnityTarget;
            bool feeding = awake && !unityReached && acceptCorpses && consumeProgress.Count > 0;

            MaintainEffecter(ref feedEffecter, HSK_AscensionDefOf.HSK_MarkerFeed, feeding);
            MaintainEffecter(ref auraEffecter, HSK_AscensionDefOf.HSK_MarkerAura, awake && fraction >= AuraFraction);
            MaintainEffecter(ref chargedEffecter, HSK_AscensionDefOf.HSK_MarkerCharged, awake && fraction >= ChargedFraction);

            bool manifest = false;
            if (unityEffectTicksLeft > 0)
            {
                unityEffectTicksLeft--;
                manifest = unityEffectTicksLeft == 0;
                if (unityEffecter == null && HSK_AscensionDefOf.HSK_MarkerUnity != null)
                {
                    unityEffecter = HSK_AscensionDefOf.HSK_MarkerUnity.Spawn();
                    unityEffecter.Trigger(this, this);
                }

                if (unityEffecter != null)
                {
                    unityEffecter.EffectTick(this, this);
                }
            }
            else if (unityEffecter != null)
            {
                unityEffecter.Cleanup();
                unityEffecter = null;
            }

            // Эффект Единения кончился: маркер рушится, появляется босс. Должно быть последним в методе.
            if (manifest)
            {
                MarkerAscensionUtility.ManifestApotheosis(this);
            }
        }

        private void MaintainEffecter(ref Effecter effecter, EffecterDef def, bool active)
        {
            if (!active || def == null)
            {
                if (effecter != null)
                {
                    effecter.Cleanup();
                    effecter = null;
                }

                return;
            }

            if (effecter == null)
            {
                effecter = def.Spawn();
            }

            effecter.EffectTick(this, this);
        }

        private void ClearEffecters()
        {
            if (feedEffecter != null) { feedEffecter.Cleanup(); feedEffecter = null; }
            if (auraEffecter != null) { auraEffecter.Cleanup(); auraEffecter = null; }
            if (chargedEffecter != null) { chargedEffecter.Cleanup(); chargedEffecter = null; }
            if (unityEffecter != null) { unityEffecter.Cleanup(); unityEffecter = null; }
            unityEffectTicksLeft = 0;
        }

        // Одиночная вспышка (порог волны, Единение).
        private void PlayWaveFlash()
        {
            EffecterDef def = HSK_AscensionDefOf.HSK_MarkerWaveFlash;
            if (def == null || !Spawned)
            {
                return;
            }

            Effecter flash = def.Spawn();
            flash.Trigger(this, this);
            flash.Cleanup();
        }

        // Страховка: вражеские пешки (рейдеры, некроморфы) не могут повредить маркер, даже взрывами и выстрелами.
        // Исключение: штурмовая группа CEC (её задача как раз уничтожить маркер).
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            Pawn attacker = dinfo.Instigator as Pawn;
            if (attacker != null && attacker.Faction != null && attacker.Faction != Faction.OfPlayer
                && attacker.Faction.HostileTo(Faction.OfPlayer)
                && !IsStrikeTeamMember(attacker))
            {
                absorbed = true;
                return;
            }

            base.PreApplyDamage(ref dinfo, out absorbed);
        }

        private static bool IsStrikeTeamMember(Pawn pawn)
        {
            Verse.AI.Group.Lord lord = Verse.AI.Group.LordUtility.GetLord(pawn);
            return lord != null && lord.LordJob is LordJob_MarkerStrike;
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            ClearRoots();
            ClearEffecters();
            base.DeSpawn(mode);
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();

            if (studied && !unityReached)
            {
                GenDraw.DrawRadiusRing(Position, ConsumeRadius);
            }
        }

        private void AscensionTick()
        {
            if (!AcceptsCorpses)
            {
                if (roots.Count > 0 || consumeProgress.Count > 0)
                {
                    ClearRoots();
                }

                return;
            }

            Map map = Map;

            // 1. Все подходящие трупы в радиусе.
            tmpCorpses.Clear();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(Position, ConsumeRadius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                tmpThings.Clear();
                tmpThings.AddRange(cell.GetThingList(map));
                for (int i = 0; i < tmpThings.Count; i++)
                {
                    Corpse corpse = tmpThings[i] as Corpse;
                    if (corpse != null && MarkerAscensionUtility.IsOfferable(corpse) && !tmpCorpses.Contains(corpse))
                    {
                        tmpCorpses.Add(corpse);
                    }
                }
            }

            // 2. Корни тех, кого унесли, съели или кто стал неподходящим, убираем.
            tmpRemove.Clear();
            foreach (KeyValuePair<Corpse, int> pair in consumeProgress)
            {
                if (!tmpCorpses.Contains(pair.Key))
                {
                    tmpRemove.Add(pair.Key);
                }
            }

            foreach (KeyValuePair<Corpse, Mote> pair in roots)
            {
                if (!tmpCorpses.Contains(pair.Key) && !tmpRemove.Contains(pair.Key))
                {
                    tmpRemove.Add(pair.Key);
                }
            }

            for (int i = 0; i < tmpRemove.Count; i++)
            {
                ReleaseRoot(tmpRemove[i]);
            }

            // 3. Остальных держим корнями; созревшие поглощаем.
            for (int i = 0; i < tmpCorpses.Count; i++)
            {
                if (!AcceptsCorpses)
                {
                    break;
                }

                Corpse corpse = tmpCorpses[i];
                TryMakeRoot(corpse);

                int progress;
                consumeProgress.TryGetValue(corpse, out progress);
                progress += ConsumeCheckInterval;

                if (progress >= ConsumeTicks)
                {
                    ReleaseRoot(corpse);
                    AbsorbCorpse(corpse, null);
                }
                else
                {
                    consumeProgress[corpse] = progress;
                }
            }

            if (!AcceptsCorpses)
            {
                ClearRoots();
            }
        }

        private void TryMakeRoot(Corpse corpse)
        {
            Mote existing;
            if (roots.TryGetValue(corpse, out existing) && existing != null && !existing.Destroyed)
            {
                return;
            }

            ThingDef moteDef = ThingDefOf.Mote_HarbingerTreeRoots;
            if (moteDef == null)
            {
                return;
            }

            float rot = 0f;
            if (corpse.InnerPawn != null)
            {
                rot = corpse.InnerPawn.Drawer.renderer.BodyAngle(PawnRenderFlags.None);
            }

            roots[corpse] = MoteMaker.MakeStaticMote(corpse.Position.ToVector3Shifted(), Map, moteDef, 1f, false, rot);
        }

        private void ReleaseRoot(Corpse corpse)
        {
            Mote mote;
            if (roots.TryGetValue(corpse, out mote))
            {
                if (mote != null && !mote.Destroyed)
                {
                    mote.Destroy();
                }

                roots.Remove(corpse);
            }

            consumeProgress.Remove(corpse);
        }

        private void ClearRoots()
        {
            foreach (KeyValuePair<Corpse, Mote> pair in roots)
            {
                if (pair.Value != null && !pair.Value.Destroyed)
                {
                    pair.Value.Destroy();
                }
            }

            roots.Clear();
            consumeProgress.Clear();
        }

        // Создаёт зону хранения трупов на свободных клетках в радиусе маркера,
        // чтобы колонисты стаскивали трупы под его корни.
        private void CreateCorpseStockpile()
        {
            Map map = Map;
            if (map == null)
            {
                return;
            }

            List<IntVec3> free = new List<IntVec3>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(Position, ConsumeRadius, true))
            {
                if (cell.InBounds(map)
                    && map.zoneManager.ZoneAt(cell) == null
                    && (bool)Designator_ZoneAdd.IsZoneableCell(cell, map))
                {
                    free.Add(cell);
                }
            }

            if (free.Count == 0)
            {
                Messages.Message("HSK_MarkerCorpseZoneNoRoom".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Zone_Stockpile stockpile = new Zone_Stockpile(StorageSettingsPreset.CorpseStockpile, map.zoneManager);
            stockpile.settings.filter.SetAllow(ThingCategoryDefOf.CorpsesMechanoid, false);
            stockpile.settings.filter.SetAllow(SpecialThingFilterDefOf.AllowCorpsesUnnatural, false);
            map.zoneManager.RegisterZone(stockpile);

            for (int i = 0; i < free.Count; i++)
            {
                stockpile.AddCell(free[i]);
            }
        }

        // Поглощает труп: снаряжение остаётся на земле, сам труп исчезает, шкала растёт.
        public void AbsorbCorpse(Corpse corpse, Pawn doer)
        {
            if (corpse == null || corpse.Destroyed || !AcceptsCorpses)
            {
                return;
            }

            float value = MarkerAscensionUtility.CorpseValue(corpse, AnimalCorpseValue);

            corpse.Strip();
            corpse.Destroy();

            DropOre(value);
            AddAscension(value);
        }

        private ThingDef OreDef
        {
            get
            {
                if (!oreDefSearched)
                {
                    oreDefSearched = true;
                    MarkerAscensionExtension ext = AscensionExt;
                    string name = ext != null ? ext.oreDefName : "Markerite";
                    if (!string.IsNullOrEmpty(name))
                    {
                        cachedOreDef = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    }

                    if (cachedOreDef == null)
                    {
                        Log.Warning("[HSK Watcher] Marker ore def not found: " + name);
                    }
                }

                return cachedOreDef;
            }
        }

        // Руда за поглощённый труп. Дробная часть копится, поэтому труп животного (0.25) тоже даёт руду со временем.
        private void DropOre(float corpseValue)
        {
            ThingDef ore = OreDef;
            if (ore == null || !Spawned)
            {
                return;
            }

            MarkerAscensionExtension ext = AscensionExt;
            float perValue = ext != null ? ext.orePerValue : 10f;
            oreProgress += corpseValue * perValue;

            int count = Mathf.FloorToInt(oreProgress);
            if (count <= 0)
            {
                return;
            }

            oreProgress -= count;

            while (count > 0)
            {
                Thing stack = ThingMaker.MakeThing(ore);
                stack.stackCount = Mathf.Min(count, ore.stackLimit);
                count -= stack.stackCount;
                GenPlace.TryPlaceThing(stack, Position, Map, ThingPlaceMode.Near);
            }
        }

        public void AddAscension(float amount)
        {
            if (unityReached || amount <= 0f || !Spawned)
            {
                return;
            }

            ascension = Mathf.Min(ascension + amount, UnityTarget);

            // Пороги шкалы: только вспышка, существ они не вызывают (они выходят лишь при Единении).
            // Цикл на случай, если сразу несколько порогов пройдено разом.
            List<float> thresholds = Thresholds;
            bool flash = false;
            while (wavesTriggered < thresholds.Count && ascension >= thresholds[wavesTriggered])
            {
                wavesTriggered++;
                flash = true;
            }

            if (flash)
            {
                PlayWaveFlash();
            }

            if (ascension >= UnityTarget)
            {
                unityReached = true;
                unityEffectTicksLeft = UnityEffectTicks;
                PlayWaveFlash();
                MarkerAscensionUtility.TriggerUnity(this);
            }
        }
    }
}

// ------------------------------------------------------------
// Вознесение: DefOf, отбор трупов, волны, Единение  [/root/.claude/uploads/a85e4491-08d3-53f7-b914-0e7ecd4ca2b9/525a21b7-1792323063605_MarkerAscension.cs]
// ------------------------------------------------------------
namespace HSK.Watcher.DeadSpace
{
    using RimWorld;
    using UnityEngine;
    using Verse;

    // ------------------------------------------------------------------
    // DefOf
    // ------------------------------------------------------------------
    [DefOf]
    public static class HSK_AscensionDefOf
    {
        public static EffecterDef HSK_MarkerFeed;
        public static EffecterDef HSK_MarkerAura;
        public static EffecterDef HSK_MarkerCharged;
        public static EffecterDef HSK_MarkerWaveFlash;
        public static EffecterDef HSK_MarkerUnity;
        public static PawnKindDef Infector;
        public static PawnKindDef Lurker;
        public static PawnKindDef Necromorphe;
        public static PawnKindDef Apotheosis;

        static HSK_AscensionDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HSK_AscensionDefOf));
        }
    }

    // ------------------------------------------------------------------
    // Общие функции вознесения: отбор трупов, их ценность, волны, Единение.
    // ------------------------------------------------------------------
    public static class MarkerAscensionUtility
    {
        // Волна: сколько существ выходит на каждом пороге (tier - номер порога с нуля).
        private const int WaveInfectorsBase = 1;
        private const int WaveInfectorsPerTiers = 3;   // +1 заразитель за каждые 3 порога
        private const int WaveLurkersBase = 2;         // +1 притаившийся за каждый порог
        private const int WaveNecromorphsPerTiers = 2; // +1 некроморф за каждые 2 порога

        // Единение: размер последней волны.
        private const int UnityInfectors = 6;
        private const int UnityLurkers = 14;
        private const int UnityNecromorphs = 10;

        // Где появляются существа: в этом квадрате вокруг маркера, но не вплотную.
        private const int SpawnRadius = 12;
        private const int MinSpawnDistance = 4;

        // Какие трупы маркер поглощает: чужие, не колонистов и не питомцев, не некроморфов и не механоидов.
        public static bool IsOfferable(Corpse corpse)
        {
            if (corpse == null || corpse.Destroyed || !corpse.Spawned)
            {
                return false;
            }

            Pawn p = corpse.InnerPawn;
            if (p == null || p.RaceProps == null || p.RaceProps.IsMechanoid)
            {
                return false;
            }

            Faction owner = p.Faction;
            if (owner != null)
            {
                if (owner == Faction.OfPlayer)
                {
                    return false;
                }

                Faction entities = Faction.OfEntities;
                if (entities != null && owner == entities)
                {
                    return false;
                }
            }

            return true;
        }

        // Сколько очков шкалы даёт труп: человек - 1, животное - animalValue.
        public static float CorpseValue(Corpse corpse, float animalValue)
        {
            Pawn p = corpse != null ? corpse.InnerPawn : null;
            if (p != null && p.RaceProps != null && p.RaceProps.Humanlike)
            {
                return 1f;
            }

            return animalValue;
        }

        // ---------------- Волны ----------------

        // Обычная волна на пороге tier.
        public static void SpawnWave(Building_BlackMarker marker, int tier)
        {
            if (marker == null || !marker.Spawned)
            {
                return;
            }

            int infectors = WaveInfectorsBase + tier / WaveInfectorsPerTiers;
            int lurkers = WaveLurkersBase + tier;
            int necromorphs = tier / WaveNecromorphsPerTiers;

            int total = 0;
            total += SpawnGroup(HSK_AscensionDefOf.Infector, infectors, marker);
            total += SpawnGroup(HSK_AscensionDefOf.Lurker, lurkers, marker);
            total += SpawnGroup(HSK_AscensionDefOf.Necromorphe, necromorphs, marker);

            HSK_Letters.Send(
                "HSK_MarkerWaveLabel",
                "HSK_MarkerWaveLabel".Translate(),
                "HSK_MarkerWaveText".Translate(total.ToString()),
                LetterDefOf.ThreatBig,
                new LookTargets(marker));
        }

        // Единение: последняя, самая большая волна.
        public static void TriggerUnity(Building_BlackMarker marker)
        {
            if (marker == null || !marker.Spawned)
            {
                return;
            }

            int total = 0;
            total += SpawnGroup(HSK_AscensionDefOf.Infector, UnityInfectors, marker);
            total += SpawnGroup(HSK_AscensionDefOf.Lurker, UnityLurkers, marker);
            total += SpawnGroup(HSK_AscensionDefOf.Necromorphe, UnityNecromorphs, marker);

            HSK_Letters.Send(
                "HSK_MarkerUnityLabel",
                "HSK_MarkerUnityLabel".Translate(),
                "HSK_MarkerUnityText".Translate(total.ToString()),
                LetterDefOf.ThreatBig,
                new LookTargets(marker));
        }

        // Финал Единения: маркер рушится, на его месте встаёт босс "Апофеоз Плоти".
        // Вызывается маркером, когда закончился эффект Единения. Если босса нет в defs,
        // маркер остаётся нетронутым.
        public static void ManifestApotheosis(Building_BlackMarker marker)
        {
            if (marker == null || !marker.Spawned)
            {
                return;
            }

            PawnKindDef kind = HSK_AscensionDefOf.Apotheosis;
            if (kind == null)
            {
                return;
            }

            Map map = marker.Map;
            IntVec3 position = marker.Position;

            Pawn boss = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind,
                Faction.OfEntities,
                PawnGenerationContext.NonPlayer));

            // Маркер рассыпается в пыль, освобождая место.
            for (int i = 0; i < 24; i++)
            {
                Vector3 offset = new Vector3(Rand.Range(-2.5f, 2.5f), 0f, Rand.Range(-2.5f, 2.5f));
                FleckMaker.ThrowDustPuff(position.ToVector3Shifted() + offset, map, Rand.Range(2f, 4f));
            }

            marker.Destroy(DestroyMode.Vanish);
            GenSpawn.Spawn(boss, position, map, Rot4.Random);

            // Письмо о боссе: текст и тип берутся из IncidentDef HSK_FleshApotheosis (перевод через DefInjected).
            IncidentDef incident = DefDatabase<IncidentDef>.GetNamedSilentFail("HSK_FleshApotheosis");
            if (incident != null)
            {
                HSK_Letters.Send(
                    incident.defName,
                    incident.letterLabel,
                    incident.letterText,
                    incident.letterDef ?? LetterDefOf.ThreatBig,
                    new LookTargets(boss));
            }

            // Верующие юнитологи слышат зов.
            UnityBelievers.Afflict(map);
        }

        // Создаёт count существ вида kind вокруг маркера. Возвращает, сколько реально появилось.
        // Как и в IncidentWorker_InfectorOutbreak, лорд не нужен: охота заложена в думающее дерево.
        private static int SpawnGroup(PawnKindDef kind, int count, Building_BlackMarker marker)
        {
            Map map = marker.Map;
            if (kind == null || map == null || count <= 0)
            {
                return 0;
            }

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind,
                    Faction.OfEntities,
                    PawnGenerationContext.NonPlayer);
                Pawn pawn = PawnGenerator.GeneratePawn(request);

                GenSpawn.Spawn(pawn, FindSpawnCell(marker, map), map, Rot4.Random);
                spawned++;
            }

            return spawned;
        }

        private static IntVec3 FindSpawnCell(Building_BlackMarker marker, Map map)
        {
            IntVec3 origin = marker.Position;
            int minDistSquared = MinSpawnDistance * MinSpawnDistance;

            IntVec3 cell;
            if (CellFinder.TryFindRandomCellNear(
                origin,
                map,
                SpawnRadius,
                delegate (IntVec3 c)
                {
                    return c.Standable(map)
                        && c.GetEdifice(map) == null
                        && (c - origin).LengthHorizontalSquared >= minDistSquared;
                },
                out cell))
            {
                return cell;
            }

            return CellFinder.RandomClosewalkCellNear(origin, map, SpawnRadius);
        }
    }
}