// ============================================================
// Watcher — Чёрный маркер
// Совместимо с C# 7.3. using-директивы перенесены внутрь каждого namespace,
// поэтому части не влияют друг на друга (как и в отдельных файлах).
//
// Содержимое:
//   1. Некроморфы: Заразитель, охота, Lurker, lord, заражение трупов
// ============================================================

// ------------------------------------------------------------
// Некроморфы: Заразитель, охота, Lurker, lord, заражение трупов  [/root/.claude/uploads/a85e4491-08d3-53f7-b914-0e7ecd4ca2b9/734c82e3-1792323063604_CompProperties_Infector.cs]
// ------------------------------------------------------------
namespace HSK.Watcher.DeadSpace
{
    using System.Collections.Generic;
    using RimWorld;
    using Verse;
    using Verse.AI;
    using Verse.AI.Group;

    // ------------------------------------------------------------------
    // DefOf
    // ------------------------------------------------------------------
    [DefOf]
    public static class HSK_JobDefOf
    {
        public static JobDef HSK_InfectCorpse;

        static HSK_JobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HSK_JobDefOf));
        }
    }

    // DutyDef охоты некроморфов (см. Duties_Necromorph.xml).
    [DefOf]
    public static class HSK_DutyDefOf
    {
        public static DutyDef HSK_NecromorphHunt;

        static HSK_DutyDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(HSK_DutyDefOf));
        }
    }

    // ------------------------------------------------------------------
    // Настройки компонента. Подключается в ThingDef Infector:
    // <li Class="HSK.Watcher.DeadSpace.CompProperties_InfectorSeeker">
    //   <necromorphKind>Necromorphe</necromorphKind>
    // </li>
    // ------------------------------------------------------------------
    public class CompProperties_InfectorSeeker : CompProperties
    {
        // Как часто (в тиках) Заразитель ищет новый труп, пока свободен.
        public int checkIntervalTicks = 150;

        // Сколько тиков длится "работа над трупом" (60 тиков = 1 секунда).
        public int infectTicks = 240;

        // Кто появится на месте трупа.
        public PawnKindDef necromorphKind;

        // Давать ли новому некроморфу lord охоты (LordJob_NecromorphHunt, открывает двери).
        // Относится только к некроморфу из человека; Lurker живёт по своему дереву мыслей.
        public bool assignAssaultLord = true;

        // Кто появится на месте трупа ЖИВОТНОГО. Если не задан, трупы животных не заражаются.
        public PawnKindDef animalNecromorphKind;

        // Минимальный размер тела животного (RaceProps.baseBodySize), чтобы его заразить.
        // Отсекает крыс, кур и прочую мелочь.
        public float minAnimalBodySize = 0.4f;

        public CompProperties_InfectorSeeker()
        {
            compClass = typeof(CompInfectorSeeker);
        }
    }

    // ------------------------------------------------------------------
    // Компонент Заразителя: настройки + поиск ближайшего трупа человека.
    // Сам не тикает, его вызывает JobGiver_InfectCorpse из дерева мыслей.
    // ------------------------------------------------------------------
    public class CompInfectorSeeker : ThingComp
    {
        private int nextCheckTick;

        public CompProperties_InfectorSeeker Props
        {
            get { return (CompProperties_InfectorSeeker)props; }
        }

        // Возвращает труп, к которому надо идти, или null.
        public Thing FindTarget(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed)
            {
                return null;
            }

            // Не чаще, чем раз в checkIntervalTicks (поиск по карте дорогой).
            int now = Find.TickManager.TicksGame;
            if (now < nextCheckTick)
            {
                return null;
            }
            nextCheckTick = now + Props.checkIntervalTicks;

            if (pawn.InMentalState)
            {
                return null;
            }

            // Союзные Заразители игрока (если вдруг появятся) не работают.
            if (pawn.Faction == Faction.OfPlayer)
            {
                return null;
            }

            // Не отвлекаем от боя, только если враг реально рядом.
            if (pawn.mindState != null)
            {
                Thing enemy = pawn.mindState.enemyTarget;
                if (enemy != null && enemy.Spawned && enemy.Position.InHorDistOf(pawn.Position, 10f))
                {
                    return null;
                }
            }

            // Спящий (dormant) не ищет.
            CompCanBeDormant dormant = pawn.TryGetComp<CompCanBeDormant>();
            if (dormant != null && !dormant.Awake)
            {
                return null;
            }

            // Удерживаемый на платформе содержания тоже не ищет.
            CompHoldingPlatformTarget holding = pawn.TryGetComp<CompHoldingPlatformTarget>();
            if (holding != null && holding.CurrentlyHeldOnPlatform)
            {
                return null;
            }

            Map map = pawn.Map;

            // Кандидаты: трупы на земле и контейнеры (могилы, саркофаги и т.п.) с трупом внутри.
            List<Thing> candidates = new List<Thing>();
            candidates.AddRange(map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse));
            List<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < buildings.Count; i++)
            {
                Building_Casket casket = buildings[i] as Building_Casket;
                if (casket != null && CorpseSearch.FindCorpseIn(casket) != null)
                {
                    candidates.Add(casket);
                }
            }

            return GenClosest.ClosestThing_Global_Reachable(
                pawn.Position,
                map,
                candidates,
                PathEndMode.Touch,
                TraverseParms.For(pawn),
                99999f,
                t => IsValidTarget(pawn, t));
        }

        // Подходит ли труп: человек всегда, животное - только если задан animalNecromorphKind
        // и тело достаточно крупное.
        private bool IsValidTarget(Pawn pawn, Thing thing)
        {
            Corpse corpse = thing as Corpse;
            if (corpse == null)
            {
                // Контейнер (могила, саркофаг...): труп лежит внутри.
                Building_Casket casket = thing as Building_Casket;
                if (casket == null || !casket.Spawned)
                {
                    return false;
                }

                corpse = CorpseSearch.FindCorpseIn(casket);
                if (corpse == null)
                {
                    return false;
                }
            }
            else if (corpse.Destroyed || !corpse.Spawned)
            {
                return false;
            }

            Pawn inner = corpse.InnerPawn;
            if (inner == null || inner.RaceProps == null)
            {
                return false;
            }

            if (inner.RaceProps.Humanlike)
            {
                // человек подходит
            }
            else if (inner.RaceProps.Animal)
            {
                PawnKindDef animalKind = Props.animalNecromorphKind;
                if (animalKind == null)
                {
                    return false;
                }

                // Не заражаем уже заражённых (на случай трупа некроморфа-животного).
                if (inner.def == animalKind.race)
                {
                    return false;
                }

                if (inner.RaceProps.baseBodySize < Props.minAnimalBodySize)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            // Высохшие остатки не годятся.
            if (corpse.GetRotStage() == RotStage.Dessicated)
            {
                return false;
            }

            return pawn.CanReserve(thing);
        }
    }

    // ------------------------------------------------------------------
    // Поиск трупа внутри контейнера (могила, саркофаг и т.п.).
    // ------------------------------------------------------------------
    public static class CorpseSearch
    {
        public static Corpse FindCorpseIn(Building_Casket casket)
        {
            if (casket == null)
            {
                return null;
            }

            ThingOwner owner = casket.GetDirectlyHeldThings();
            if (owner == null)
            {
                return null;
            }

            for (int i = 0; i < owner.Count; i++)
            {
                Corpse corpse = owner[i] as Corpse;
                if (corpse != null)
                {
                    return corpse;
                }
            }

            return null;
        }
    }

    // ------------------------------------------------------------------
    // Узел дерева мыслей: "иди заражать труп".
    // Стоит ПЕРЕД боевым узлом (см. ThinkTree_Infector.xml).
    // ------------------------------------------------------------------
    public class JobGiver_InfectCorpse : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            CompInfectorSeeker comp = pawn.TryGetComp<CompInfectorSeeker>();
            if (comp == null)
            {
                return null;
            }

            Thing target = comp.FindTarget(pawn);
            if (target == null)
            {
                return null;
            }

            return JobMaker.MakeJob(HSK_JobDefOf.HSK_InfectCorpse, target);
        }
    }

    // ------------------------------------------------------------------
    // Запасное поведение, когда делать нечего: постоять или побродить рядом.
    // Интервалы короткие, чтобы Заразитель быстро замечал новые трупы.
    // ------------------------------------------------------------------
    public class JobGiver_InfectorIdle : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned)
            {
                return null;
            }

            // Упавшее существо не может ходить: без этого игра пишет
            // "tried to path while downed" (в наших деревьях нет ванильной ветки Downed).
            if (pawn.Downed)
            {
                return JobMaker.MakeJob(JobDefOf.Wait_Downed);
            }

            if (Rand.Chance(0.5f))
            {
                Job wait = JobMaker.MakeJob(JobDefOf.Wait_Wander);
                wait.expiryInterval = Rand.RangeInclusive(90, 200);
                return wait;
            }

            IntVec3 cell = CellFinder.RandomClosewalkCellNear(pawn.Position, pawn.Map, 8);
            if (!cell.IsValid || cell == pawn.Position)
            {
                Job wait = JobMaker.MakeJob(JobDefOf.Wait_Wander);
                wait.expiryInterval = Rand.RangeInclusive(90, 200);
                return wait;
            }

            Job go = JobMaker.MakeJob(JobDefOf.GotoWander, cell);
            go.locomotionUrgency = LocomotionUrgency.Walk;
            go.expiryInterval = 250;
            return go;
        }
    }

    // ------------------------------------------------------------------
    // Поведение Necromorphe: люди - приоритетная цель по всей карте.
    // Вызывается и из duty (HSK_NecromorphHunt), и из основного дерева.
    // ------------------------------------------------------------------
    public class JobGiver_NecromorphHunt : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Downed || pawn.Dead || pawn.InMentalState)
            {
                return null;
            }

            // Свой lord даёт право открывать любые двери (LordJob.CanOpenAnyDoor).
            NecromorphHunt.EnsureLord(pawn);

            Pawn target = NecromorphHunt.FindHuman(pawn);
            if (target == null)
            {
                return null;
            }

            Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            job.canBashDoors = true;       // запертые двери ломает, если открыть нельзя
            job.canBashFences = true;
            job.killIncappedTarget = true; // добивает упавших - это трупы для Заразителя
            job.expiryInterval = 400;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }

    // ------------------------------------------------------------------
    // Поведение Lurker: охотится на людей по всей карте, подходит на дистанцию
    // выстрела и стреляет шипами (ability), пока способность на перезарядке -
    // ждёт на месте, а не лезет в ближний бой.
    // XML: <li Class="HSK.Watcher.DeadSpace.JobGiver_LurkerHunt"><ability>SpikeLaunch_Fingerspike</ability></li>
    // ------------------------------------------------------------------
    public class JobGiver_LurkerHunt : ThinkNode_JobGiver
    {
        // Способность для стрельбы (задаётся в XML дерева мыслей).
        public AbilityDef ability;

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Downed || pawn.Dead || pawn.InMentalState || ability == null)
            {
                return null;
            }

            if (pawn.abilities == null)
            {
                return null;
            }

            Ability spike = pawn.abilities.GetAbility(ability);
            if (spike == null || spike.verb == null)
            {
                return null;
            }

            Pawn target = NecromorphHunt.FindHuman(pawn);
            if (target == null)
            {
                return null;
            }

            // Цель в зоне выстрела и видна: стреляем, если способность готова, иначе ждём.
            if (spike.verb.CanHitTarget(target))
            {
                if (spike.CanCast)
                {
                    return spike.GetJob(target, target);
                }

                Job wait = JobMaker.MakeJob(JobDefOf.Wait_Combat);
                wait.expiryInterval = 30;
                wait.checkOverrideOnExpire = true;
                return wait;
            }

            // Иначе подходим на дистанцию выстрела.
            IntVec3 castPos;
            CastPositionRequest req = new CastPositionRequest();
            req.caster = pawn;
            req.target = target;
            req.verb = spike.verb;
            req.maxRangeFromTarget = spike.verb.verbProps.range * 0.9f;
            req.wantCoverFromTarget = false;

            Job go;
            if (CastPositionFinder.TryFindCastPosition(req, out castPos) && castPos != pawn.Position)
            {
                go = JobMaker.MakeJob(JobDefOf.Goto, castPos);
            }
            else
            {
                go = JobMaker.MakeJob(JobDefOf.Goto, target);
            }

            go.locomotionUrgency = LocomotionUrgency.Jog;
            go.canBashDoors = true;
            go.expiryInterval = 150;
            go.checkOverrideOnExpire = true;
            return go;
        }
    }

    public static class NecromorphHunt
    {
        // Ближайший по пути враждебный гуманоид на всей карте.
        public static Pawn FindHuman(Pawn pawn)
        {
            Map map = pawn.Map;
            List<Thing> candidates = new List<Thing>();
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p != pawn && p.RaceProps != null && p.RaceProps.Humanlike && pawn.HostileTo(p))
                {
                    candidates.Add(p);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            return GenClosest.ClosestThing_Global_Reachable(
                pawn.Position,
                map,
                candidates,
                PathEndMode.Touch,
                TraverseParms.For(pawn, Danger.Deadly, TraverseMode.ByPawn, true),
                99999f,
                null) as Pawn;
        }

        // Если у пешки нет lord, добавляет её в общий lord охоты на этой карте (или создаёт новый).
        public static void EnsureLord(Pawn pawn)
        {
            if (pawn.GetLord() != null || pawn.Faction == null || pawn.Map == null)
            {
                return;
            }

            List<Lord> lords = pawn.Map.lordManager.lords;
            for (int i = 0; i < lords.Count; i++)
            {
                Lord existing = lords[i];
                if (existing.faction == pawn.Faction && existing.LordJob is LordJob_NecromorphHunt)
                {
                    existing.AddPawn(pawn);
                    // Новичку тоже нужно выдать duty, иначе ThinkNode_DutyConstant будет спамить в лог.
                    if (existing.CurLordToil != null)
                    {
                        existing.CurLordToil.UpdateAllDuties();
                    }
                    return;
                }
            }

            LordMaker.MakeNewLord(
                pawn.Faction,
                new LordJob_NecromorphHunt(),
                pawn.Map,
                new List<Pawn> { pawn });
        }
    }

    public class LordToil_NecromorphHunt : LordToil
    {
        // Каждому члену lord выдаём duty охоты: оно нужно ThinkNode_DutyConstant
        // из FleshbeastConstant, поведением занимается JobGiver_NecromorphHunt.
        public override void UpdateAllDuties()
        {
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn p = lord.ownedPawns[i];
                if (p.mindState != null)
                {
                    p.mindState.duty = new PawnDuty(HSK_DutyDefOf.HSK_NecromorphHunt);
                }
            }
        }
    }

    public class LordJob_NecromorphHunt : LordJob
    {
        public override bool AddFleeToil
        {
            get { return false; }
        }

        // Главное: некроморфы открывают любые двери.
        public override bool CanOpenAnyDoor(Pawn p)
        {
            return true;
        }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            graph.StartingToil = new LordToil_NecromorphHunt();
            return graph;
        }
    }

    // ------------------------------------------------------------------
    // Идёт к трупу, работает над ним infectTicks тиков, затем заменяет
    // труп враждебным некроморфом.
    // ------------------------------------------------------------------
    public class JobDriver_InfectCorpse : JobDriver
    {
        // A - труп на земле ИЛИ контейнер с трупом (могила, саркофаг);
        // B - труп, вынутый из контейнера.
        private const TargetIndex TargetInd = TargetIndex.A;
        private const TargetIndex ExtractedInd = TargetIndex.B;
        private const int DefaultInfectTicks = 240;
        private const int OpenTicks = 90;

        private Building_Casket Casket
        {
            get { return job.GetTarget(TargetInd).Thing as Building_Casket; }
        }

        // Труп, над которым работаем: вынутый из контейнера или лежащий на земле.
        private Corpse Corpse
        {
            get
            {
                Corpse extracted = job.GetTarget(ExtractedInd).Thing as Corpse;
                if (extracted != null)
                {
                    return extracted;
                }

                return job.GetTarget(TargetInd).Thing as Corpse;
            }
        }

        private CompProperties_InfectorSeeker Props
        {
            get
            {
                CompInfectorSeeker comp = pawn.TryGetComp<CompInfectorSeeker>();
                return comp != null ? comp.Props : null;
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetInd), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            bool fromCasket = Casket != null;
            TargetIndex corpseInd = fromCasket ? ExtractedInd : TargetInd;

            this.FailOnDespawnedOrNull(TargetInd);
            this.FailOn(delegate
            {
                Building_Casket c = Casket;
                if (c != null)
                {
                    // Пока труп не вынут - он должен лежать в контейнере.
                    return Corpse == null && CorpseSearch.FindCorpseIn(c) == null;
                }

                return Corpse == null || Corpse.InnerPawn == null;
            });

            // 1. Идём к трупу / контейнеру.
            yield return Toils_Goto.GotoThing(TargetInd, PathEndMode.Touch);

            // 2. Если труп в могиле или контейнере - открываем и вытаскиваем его.
            if (fromCasket)
            {
                Toil open = new Toil();
                open.initAction = delegate
                {
                    pawn.pather.StopDead();
                };
                open.defaultCompleteMode = ToilCompleteMode.Delay;
                open.defaultDuration = OpenTicks;
                open.WithProgressBarToilDelay(TargetInd);
                yield return open;

                Toil extract = new Toil();
                extract.initAction = delegate
                {
                    Building_Casket casket = Casket;
                    Corpse inside = casket != null ? CorpseSearch.FindCorpseIn(casket) : null;
                    if (inside == null)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    // Выгружает содержимое рядом с контейнером; тот же объект трупа окажется на земле.
                    casket.EjectContents();
                    if (inside.Destroyed || !inside.Spawned)
                    {
                        EndJobWith(JobCondition.Incompletable);
                        return;
                    }

                    job.SetTarget(ExtractedInd, inside);
                    pawn.Reserve(inside, job);
                };
                extract.defaultCompleteMode = ToilCompleteMode.Instant;
                yield return extract;

                yield return Toils_Goto.GotoThing(ExtractedInd, PathEndMode.Touch);
            }

            // 3. Работа над трупом.
            CompProperties_InfectorSeeker props = Props;
            int ticks = props != null ? props.infectTicks : DefaultInfectTicks;

            Toil infect = new Toil();
            infect.initAction = delegate
            {
                pawn.pather.StopDead();
            };
            infect.tickAction = delegate
            {
                pawn.rotationTracker.FaceTarget(job.GetTarget(corpseInd));
            };
            infect.defaultCompleteMode = ToilCompleteMode.Delay;
            infect.defaultDuration = ticks;
            infect.WithProgressBarToilDelay(corpseInd);
            infect.FailOnDespawnedOrNull(corpseInd);
            yield return infect;

            // 4. Превращение.
            Toil transform = new Toil();
            transform.initAction = delegate
            {
                Transform();
            };
            transform.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return transform;
        }

        private void Transform()
        {
            Corpse corpse = Corpse;
            CompProperties_InfectorSeeker props = Props;
            if (corpse == null || corpse.Destroyed || !corpse.Spawned || props == null)
            {
                return;
            }

            Pawn original = corpse.InnerPawn;
            bool isAnimal = original != null && original.RaceProps != null && !original.RaceProps.Humanlike;

            // Человек -> necromorphKind (Necromorphe), животное -> animalNecromorphKind (Lurker).
            PawnKindDef kind = isAnimal ? props.animalNecromorphKind : props.necromorphKind;
            if (kind == null)
            {
                return;
            }

            Map map = corpse.Map;
            IntVec3 pos = corpse.Position;
            string originalName = original != null ? original.LabelShort : corpse.LabelShort;

            // Одежда, оружие и вещи остаются лежать на земле.
            corpse.Strip(false);
            corpse.Destroy();

            Faction faction = pawn.Faction;
            Pawn necro = PawnGenerator.GeneratePawn(
                new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer));
            GenSpawn.Spawn(necro, pos, map, Rot4.Random);

            FilthMaker.TryMakeFilth(pos, map, ThingDefOf.Filth_Blood, 2);
            FleckMaker.ThrowDustPuff(pos, map, 1.5f);

            // Новый некроморф охотится на гуманоидов (и тем самым делает трупы для Заразителя).
            // Lurker (из животного) в lord не добавляем: он стреляет издали по своему дереву мыслей.
            if (props.assignAssaultLord && !isAnimal)
            {
                NecromorphHunt.EnsureLord(necro);
            }

            if (map.IsPlayerHome)
            {
                string key = isAnimal ? "HSK_InfectorAnimalCorpseRises" : "HSK_InfectorCorpseRises";
                Messages.Message(
                    key.Translate(originalName),
                    new TargetInfo(pos, map),
                    MessageTypeDefOf.NegativeEvent);
            }
        }
    }

    // ------------------------------------------------------------------
    // Босс "Апофеоз Плоти": одним присутствием
    //   1) разрушает постройки в радиусе radius (стены падают сразу, остальное таёт за импульсы);
    //   2) убивает всех живых в радиусе killRadius (кроме своей фракции);
    //   3) заливает землю слизью (filth) в радиусе slimeRadius.
    // Не трогает: Чёрный маркер.
    //
    // XML (comps у ThingDef босса):
    // <li Class="HSK.Watcher.DeadSpace.CompProperties_Crusher">
    //   <radius>15</radius>
    //   <intervalTicks>30</intervalTicks>
    //   <damageFraction>0.35</damageFraction>
    //   <minDamage>450</minDamage>
    //   <wallsInstantly>true</wallsInstantly>
    //   <crushNaturalRock>false</crushNaturalRock>
    //   <killPawns>true</killPawns>
    //   <killRadius>8</killRadius>
    //   <filthDef>Filth_TwistedFlesh</filthDef>
    //   <slimeRadius>15</slimeRadius>
    //   <slimeIntervalTicks>60</slimeIntervalTicks>
    //   <slimePerPulse>6</slimePerPulse>
    // </li>
    // ------------------------------------------------------------------
    public class CompProperties_Crusher : CompProperties
    {
        // Радиус разрушения построек в клетках.
        public float radius = 8f;

        // Как часто (в тиках) наносится удар: 30 = дважды в секунду.
        public int intervalTicks = 30;

        // Доля максимальной прочности постройки за один удар.
        public float damageFraction = 0.35f;

        // Минимум урона за удар (чтобы даже очень прочные постройки таяли).
        public int minDamage = 150;

        // Непроходимые постройки (стены и т.п.) разрушаются сразу, а не по частям.
        public bool wallsInstantly = true;

        // Разрушать ли и природную породу (горы). По умолчанию нет.
        public bool crushNaturalRock = false;

        // Убивать ли всех живых в радиусе killRadius (кроме своей фракции).
        public bool killPawns = true;
        public float killRadius = 8f;

        // Слизь вокруг: какой filth, в каком радиусе, как часто и сколько за раз.
        public ThingDef filthDef;
        public float slimeRadius = 8f;
        public int slimeIntervalTicks = 60;
        public int slimePerPulse = 6;

        public CompProperties_Crusher()
        {
            compClass = typeof(CompCrusher);
        }
    }

    public class CompCrusher : ThingComp
    {
        private CompProperties_Crusher Props
        {
            get { return (CompProperties_Crusher)props; }
        }

        public override void CompTick()
        {
            base.CompTick();

            Pawn boss = parent as Pawn;
            if (boss == null || !boss.Spawned || boss.Dead)
            {
                return;
            }

            if (boss.IsHashIntervalTick(Props.intervalTicks))
            {
                CrushBuildings(boss);

                if (Props.killPawns)
                {
                    KillNearby(boss);
                }
            }

            if (Props.filthDef != null && boss.IsHashIntervalTick(Props.slimeIntervalTicks))
            {
                SpreadSlime(boss);
            }
        }

        private void CrushBuildings(Pawn boss)
        {
            Map map = boss.Map;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(boss.Position, Props.radius, true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                // Копия списка: разрушение постройки меняет список вещей клетки (обломки, предметы).
                List<Thing> things = new List<Thing>(cell.GetThingList(map));
                for (int i = 0; i < things.Count; i++)
                {
                    Building building = things[i] as Building;
                    if (building == null || building.Destroyed || !building.def.useHitPoints)
                    {
                        continue;
                    }

                    if (building is Building_BlackMarker)
                    {
                        continue;
                    }

                    bool rock = building.def.building != null && building.def.building.isNaturalRock;
                    if (rock && !Props.crushNaturalRock)
                    {
                        continue;
                    }

                    if (rock || (Props.wallsInstantly && building.def.passability == Traversability.Impassable))
                    {
                        building.Destroy(rock ? DestroyMode.Vanish : DestroyMode.KillFinalize);
                    }
                    else
                    {
                        int damage = (int)(building.MaxHitPoints * Props.damageFraction);
                        if (damage < Props.minDamage)
                        {
                            damage = Props.minDamage;
                        }

                        building.TakeDamage(new DamageInfo(DamageDefOf.Crush, damage, 0f, -1f, boss));
                    }

                    if (Rand.Chance(0.25f))
                    {
                        FleckMaker.ThrowDustPuff(cell, map, Rand.Range(1.2f, 2.5f));
                    }
                }
            }
        }

        // Все живые в радиусе killRadius, не из фракции босса, погибают.
        private void KillNearby(Pawn boss)
        {
            Map map = boss.Map;
            List<Pawn> pawns = new List<Pawn>(map.mapPawns.AllPawnsSpawned);
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn victim = pawns[i];
                if (victim == boss || victim.Dead || !victim.Spawned)
                {
                    continue;
                }

                if (victim.Faction == boss.Faction)
                {
                    continue;
                }

                if (!victim.Position.InHorDistOf(boss.Position, Props.killRadius))
                {
                    continue;
                }

                victim.Kill(new DamageInfo(DamageDefOf.Crush, 9999f, 999f, -1f, boss));
            }
        }

        private void SpreadSlime(Pawn boss)
        {
            Map map = boss.Map;
            for (int i = 0; i < Props.slimePerPulse; i++)
            {
                IntVec3 cell = boss.Position + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(Props.slimeRadius))];
                if (!cell.InBounds(map) || !cell.Walkable(map))
                {
                    continue;
                }

                FilthMaker.TryMakeFilth(cell, map, Props.filthDef);
            }
        }
    }

    // ------------------------------------------------------------------
    // Иммунитет к урону: каждое попадание поглощается, и лишь с шансом passChance
    // (по умолчанию 0.01 = 1%) проходит и наносит урон как обычно.
    //
    // XML (comps у ThingDef):
    // <li Class="HSK.Watcher.DeadSpace.CompProperties_DamageImmunity">
    //   <passChance>0.01</passChance>
    // </li>
    // ------------------------------------------------------------------
    public class CompProperties_DamageImmunity : CompProperties
    {
        // Вероятность, что отдельное попадание нанесёт урон.
        public float passChance = 0.01f;

        public CompProperties_DamageImmunity()
        {
            compClass = typeof(CompDamageImmunity);
        }
    }

    public class CompDamageImmunity : ThingComp
    {
        private CompProperties_DamageImmunity Props
        {
            get { return (CompProperties_DamageImmunity)props; }
        }

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PostPreApplyDamage(ref dinfo, out absorbed);
            if (absorbed)
            {
                return;
            }

            absorbed = !Rand.Chance(Props.passChance);
        }
    }

    // ------------------------------------------------------------------
    // Дальние атаки босса: если цель видна и в зоне любой готовой способности из списка,
    // босс применяет её. Не подходит к цели (его шаг решает следующий узел дерева).
    //
    // XML (в дереве мыслей):
    // <li Class="HSK.Watcher.DeadSpace.JobGiver_BossRanged">
    //   <abilities><li>SludgeSpew</li><li>SpikeLaunch_Fingerspike</li></abilities>
    // </li>
    // ------------------------------------------------------------------
    public class JobGiver_BossRanged : ThinkNode_JobGiver
    {
        public List<AbilityDef> abilities;

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Downed || pawn.Dead || pawn.InMentalState
                || pawn.abilities == null || abilities == null)
            {
                return null;
            }

            Pawn target = NecromorphHunt.FindHuman(pawn);
            if (target == null)
            {
                return null;
            }

            for (int i = 0; i < abilities.Count; i++)
            {
                Ability ability = pawn.abilities.GetAbility(abilities[i]);
                if (ability == null || ability.verb == null || !ability.CanCast)
                {
                    continue;
                }

                if (ability.verb.CanHitTarget(target))
                {
                    return ability.GetJob(target, target);
                }
            }

            return null;
        }
    }
}