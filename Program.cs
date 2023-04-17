using System;
using System.Collections.Generic;
using System.Linq;
using Volley34.Data;
using Volley34.ToolBox;
using Volley34.Data.Entities;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;



namespace ADS_PROJECT
{
    //Positionner les équipes qui recoivent sur des jours fériés en priorité,
    //si pas possible faire changer l'assignation des équipes au modèle de répartition

    //Prendre en priorité les contraintes entre équipe Domicile/Exterieur

    class Program
    {
        //Fournir calendrier au format match et calendar event
        //--> Procédure stockée : sp_Set_MatchAndCalendarEvent]

        //Pour créer un nouveau match code : 

        /*
         * 
            DECLARE @CodeMatch as NVARCHAR(12)
            SET @CodeMatch = [dbo].[fn_GetNewID] ('M')
            SELECT @CodeMatch
         * 
         */



        /*
         * EXEC [dbo].[sp_Set_MatchAndCalendarEvent]  @MatchCode = '2022-M001045',  @CompetitionCode =  '2022-C114',  @EquipeLocauxCode =  'VLMF1',   @EquipeVisiteursCode =   'L2VBX3',  @EquipeArbitrageCode = NULL, @GymnaseCode = 'MTP_TESSE' ,   @CalendarEventStartDate
 =   '06/10/2022 20:00:00',   @CalendarEventEndDate = '06/10/2022 22:00:00' , @Journee = 2
GO
         */
        static Random random = new Random();
        static DateTime debutPeriodeMatchs = new DateTime(2022, 11, 7);
        static DateTime finPeriodeMatchs = new DateTime(2023, 1, 13);
        static DateTime debutPeriodeOff = new DateTime(2022, 12, 19);
        static DateTime finPeriodeOff = new DateTime(2022, 12, 30);
        static int saison = 2023;
        static string typeDeCompetition = "CH";
        static string tour = "A";
        static void Main(string[] args)
        {
            List<List<Inscription>> resultatPoules = new List<List<Inscription>>();

            Stopwatch stopwatch = new Stopwatch();

            stopwatch.Start();
            
            int taillePopulation = 10;

            List<Competition> competitions = Globals.ConvertToList<Competition>(SQL.Get_CompetitionsSaison(saison));
            List<Equipe> equipes = Globals.ConvertToList<Equipe>(SQL.GetAll_EquipeBySaison(saison)).OrderBy(x => x.Division).ToList();
            List<Creneau> creneaux = Globals.ConvertToList<Creneau>(SQL.GetAll_CreneauBySaison(saison, true)).OrderBy(x => x.CodeJourCreneaux).ToList();
            List<RepartitionPoule> repartitionPoules = Globals.ConvertToList<RepartitionPoule>(SQL.GetAll_RepartitionPoule());
            List<Inscription> inscriptions = Globals.ConvertToList<Inscription>(SQL.GetAll_InscriptionBySaisonTypeCompetition(saison, typeDeCompetition));

            int nombreParents = (int)Math.Ceiling(inscriptions.Count * 0.1); // 10% des inscriptions
            int nombreGenerationMax = 100;
            double probabiliteCroisement = 0.8;
            double probabiliteMutation = 0.3;
            int fitness = 0;
            int contraintes = 999;
            int toleranceContraintes = 0;
            List<List<Inscription>> populationPoule = new List<List<Inscription>>();
            Dictionary<string, List<Inscription>> inscriptionsParPoule = inscriptions
            .GroupBy(i => new { i.CodeCompetition, i.Division, i.Poule })
                .ToDictionary(g => g.Key.ToString(), g => g.ToList());
            Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates = new Dictionary<Tuple<int, int>, List<DateTime>>();
            weeksWithPlayableDates = ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff);

            int index = 1;
            foreach (List<Inscription> poule in inscriptionsParPoule.Values)
            {
                Console.WriteLine($"{poule[0].CodeCompetition}-{poule[0].Division}-{poule[0].Poule}:");

                int position = 1;
                foreach (Inscription inscription in poule)
                {
                    Console.WriteLine($"Position {position}: {inscription.NomEquipe}");
                    position++;
                }

                index++;
                Console.WriteLine(); // Ajoute une ligne vide pour séparer les poules
            }

            //On prends en priorité les plus grosses poules
            //Poule les plus nombreuses avec le moins de fausses équipes
            foreach (List<Inscription> poule in inscriptionsParPoule.Values.OrderByDescending(x => x.Count).ThenByDescending(x => x.Count(e => e.Contrainte != string.Empty)).ThenBy(x => x.Count(e => e.NomEquipe == "----------")))
            {
                int nombreGeneration = 0;

                populationPoule = CreerPopulationInitiale(poule, taillePopulation, random);

                while (nombreGeneration < nombreGenerationMax)
                {
                    // Sélection des parents
                    List<List<Inscription>> parents = Selection(populationPoule, nombreParents, repartitionPoules, poule, random);

                    // Croisement des parents
                    List<List<Inscription>> enfants = new List<List<Inscription>>();
                    for (int i = 0; i < nombreParents - 1; i += 2)
                    {
                        List<Inscription> enfant1 = Croisement(parents[i], parents[i + 1], probabiliteCroisement);
                        List<Inscription> enfant2 = Croisement(parents[i + 1], parents[i], probabiliteCroisement);
                        enfants.Add(enfant1);
                        enfants.Add(enfant2);
                    }

                    // Mutation des enfants
                    foreach (List<Inscription> enfant in enfants)
                    {
                        Mutation(enfant, probabiliteMutation);
                    }

                    // Évaluation de la fitness des enfants
                    Tuple<int, int, List<Dictionary<char, Inscription>>> fitnessResult = Fitness(new List<List<Inscription>> { enfants[0] }, repartitionPoules, ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff), calculJourFerieByPeriode(debutPeriodeMatchs, finPeriodeMatchs));
                    int fitnessEnfant = fitnessResult.Item1;
                    contraintes = fitnessResult.Item2;
                    // Remplacement de la population actuelle par les enfants si leur fitness est meilleure
                    if (fitnessEnfant >= fitness)
                    {
                        populationPoule = enfants;
                        fitness = fitnessEnfant;
                    }

                    // Affichage du nombre de contraintes pour la génération actuelle
                    Console.WriteLine("Generation {0} avec {1} de score de fitness", nombreGeneration, fitness);

                    // Incrémentation du compteur de générations
                    nombreGeneration++;

                    // Ajouter une condition pour vérifier si toutes les contraintes sont satisfaites
                    // et arrêter l'algorithme si c'est le cas

                }

                // Compare la fitness de la poule d'origine à celle de la meilleure solution trouvée
                int fitnessPouleOrigine = Fitness(new List<List<Inscription>> { poule }, repartitionPoules, ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff), calculJourFerieByPeriode(debutPeriodeMatchs, finPeriodeMatchs)).Item1;
                if (fitnessPouleOrigine > fitness)
                {
                    resultatPoules.Add(poule);
                }
                else
                {
                    Console.WriteLine("Nombre de contraintes : {0}", Fitness(new List<List<Inscription>> { poule }, repartitionPoules, ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff), calculJourFerieByPeriode(debutPeriodeMatchs, finPeriodeMatchs)).Item2);
                    resultatPoules.Add(populationPoule[0]);
                }
            }

            Console.WriteLine("{0} inscriptions en {1}", inscriptions.Count, saison);

            // Affichage des résultats pour chaque poule
            int pouleIndex = 1;
            foreach (List<Inscription> poule in resultatPoules)
            {
                Console.WriteLine($"Poule {pouleIndex}:");

                int position = 1;
                foreach (Inscription inscription in poule)
                {
                    Console.WriteLine($"Position {position}: {inscription.NomEquipe}");
                    position++;
                }

                pouleIndex++;
                Console.WriteLine(); // Ajoute une ligne vide pour séparer les poules
            }

            CreerMatchs(resultatPoules, ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff), creneaux);

            stopwatch.Stop();

            Console.WriteLine("Elapsed Time is {0}ms", stopwatch.ElapsedMilliseconds);
            while (Console.ReadKey().Key != ConsoleKey.Enter) ;
        }
        public static int ObtenirNombreDePoulesParCompetition(List<Inscription> inscriptions, string competitionId)
        {
            return inscriptions
                .Where(inscription => inscription.CodeCompetition == competitionId)
                .Select(inscription => inscription.Poule)
                .Distinct()
                .Count();
        }
        public static Inscription CreerFausseInscription(string codeCompetition, string division, string poule)
        {
            return new Inscription
            {
                CodeCompetition = codeCompetition,
                Division = division,
                Poule = poule,
                NomEquipe = "----------",
                // Vous pouvez ajouter les autres propriétés de l'objet Inscription si nécessaire
            };
        }
        static List<List<Inscription>> CreerPopulationInitiale(List<Inscription> inscriptions, int nombrePoules, Random random)
        {
            Console.WriteLine("Initialisation de la population en cours");
            List<List<Inscription>> populationInitiale = new List<List<Inscription>>();
            Dictionary<string, List<Inscription>> inscriptionsParPoule = new Dictionary<string, List<Inscription>>();

            // Regrouper les inscriptions par poule
            foreach (Inscription inscription in inscriptions)
            {
                string pouleKey = inscription.CodeCompetition + "-" + inscription.Division + "-" + inscription.Poule;
                if (inscriptionsParPoule.ContainsKey(pouleKey))
                {
                    inscriptionsParPoule[pouleKey].Add(inscription);
                }
                else
                {
                    inscriptionsParPoule[pouleKey] = new List<Inscription> { inscription };
                }
            }

            // Créer les poules en fonction des inscriptions par poule
            foreach (var entry in inscriptionsParPoule)
            {
                string pouleKey = entry.Key;
                List<Inscription> inscriptionsPoule = entry.Value;
                List<Inscription> poule = new List<Inscription>();

                // Ajouter des fausses inscriptions si nécessaire
                while (inscriptionsPoule.Count < 6 || inscriptionsPoule.Count == 7)
                {
                    string[] keyParts = pouleKey.Split('-');
                    string codeCompetition = keyParts[0];
                    string division = keyParts[1];
                    string pouleNumber = keyParts[2];
                    Inscription fausseInscription = CreerFausseInscription(codeCompetition, division, pouleNumber);
                    inscriptionsPoule.Add(fausseInscription);
                }

                // Mélanger les inscriptions de la poule
                inscriptionsPoule = inscriptionsPoule.OrderBy(x => random.Next()).ToList();

                for (int i = 0; i < inscriptionsPoule.Count; i++)
                {
                    Inscription equipe = inscriptionsPoule[i];
                    poule.Add(equipe);
                }

                Console.WriteLine($"Poule {pouleKey} ({poule.Count} équipes) :");
                foreach (Inscription equipe in poule)
                {
                    Console.WriteLine($"  - {equipe.NomEquipe}");
                }

                populationInitiale.Add(poule);
            }

            return populationInitiale;
        }
        //Suivre compte du nb de contrainte pour ajouter condition d'arrêt
        static Tuple<int, int, List<Dictionary<char, Inscription>>> Fitness(List<List<Inscription>> population, List<RepartitionPoule> repartitionPoules, Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates, List<DateTime> holidays)
        {
            int fitness = 0;
            int contraintes = 0;
            List<Creneau> creneaux = Globals.ConvertToList<Creneau>(SQL.GetAll_CreneauBySaison(saison, true)).OrderBy(x => x.CodeJourCreneaux).ToList();
            List<Dictionary<char, Inscription>> finalEquipePlacements = new List<Dictionary<char, Inscription>>();

            foreach (List<Inscription> equipeList in population)
            {
                Dictionary<char, Inscription> equipePlacement = new Dictionary<char, Inscription>();

                foreach (Inscription equipe in equipeList)
                {
                    char letter = (char)('A' + equipeList.IndexOf(equipe));

                    if (equipePlacement.ContainsKey(letter))
                    {
                        equipePlacement[letter] = equipe;
                    }
                    else
                    {
                        equipePlacement.Add(letter, equipe);
                    }
                }

                finalEquipePlacements.Add(equipePlacement);

                foreach (int journee in Enumerable.Range(1, equipeList.Count > 6 ? 7 : 5))
                {
                    foreach (int poule in Enumerable.Range(1, equipeList.Count > 6 ? 8 : 6))
                    {
                        List<RepartitionPoule> repartitionList = repartitionPoules
                            .Where(r => Int32.Parse(r.Journee) == journee && r.Poule == poule && r.Tour.Equals(tour))
                            .ToList();

                        foreach (RepartitionPoule repartition in repartitionList)
                        {
                            if (!equipePlacement.TryGetValue(repartition.Locaux[0], out Inscription equipeLocale))
                            {
                                continue;
                            }

                            // Vérifier les contraintes entre les équipes
                            if (!string.IsNullOrEmpty(equipeLocale.Contrainte))
                            {
                                Inscription contrainteEquipe = equipeList.FirstOrDefault(e => e.NomEquipe == equipeLocale.Contrainte);

                                if (contrainteEquipe != null)
                                {
                                    // Vérifier si l'équipe contrainte joue la même semaine et le même jour
                                    RepartitionPoule contrainteRepartition = repartitionList.FirstOrDefault(r => equipePlacement[r.Locaux[0]].NomEquipe == contrainteEquipe.NomEquipe);

                                    if (contrainteRepartition != null)
                                    {
                                        (int weekNumber, int weekYear) = getWeekNumber(weeksWithPlayableDates.Values.ElementAt(Int32.Parse(repartition.Journee) - 1)[0]);
                                        Tuple<int, int> weekKey = Tuple.Create(weekNumber, weekYear);
                                        if (weeksWithPlayableDates.TryGetValue(weekKey, out List<DateTime> playableDates))
                                        {
                                            DateTime weekStartDate = playableDates[0];
                                            DateTime weekEndDate = weekStartDate.AddDays(6);

                                            if (equipeLocale.Jour == contrainteEquipe.Jour && weeksWithPlayableDates[weekKey].Any(date => date.Date >= weekStartDate && date.Date <= weekEndDate))
                                            {
                                                fitness -= 50;
                                                contraintes += 50;
                                                Console.WriteLine("Contraintes entre équipes ! Score de fitness : {0}", fitness);

                                            }
                                            else
                                            {
                                                fitness += 50;
                                            }
                                        }
                                        else
                                        {
                                            // Gérer le cas où la clé n'est pas trouvée dans weeksWithPlayableDates
                                        }
                                    }
                                }
                            }

                            // Vérifier si l'équipe locale a un match prévu sur un jour férié
                            if (equipeLocale.Jour != null)
                            {
                                (int weekNumber, int weekYear) = weeksWithPlayableDates.Keys.ElementAt(Int32.Parse(repartition.Journee) - 1);
                                Tuple<int, int> weekKey = Tuple.Create(weekNumber, weekYear);
                                if (weeksWithPlayableDates.TryGetValue(weekKey, out List<DateTime> playableDates))
                                {
                                    DateTime firstDayOfWeek = playableDates.OrderBy(date => date).First();
                                    DateTime slotDateTime = firstDayOfWeek.AddDays((int)equipeLocale.Jour - 1);

                                    // Utilisez ici le créneau horaire approprié pour l'équipe locale
                                    Creneau homeTeamSlot = creneaux.FirstOrDefault(c => c.EquipeCode == equipeLocale.CodeEquipe);
                                    DateTime slotTime = DateTime.ParseExact(homeTeamSlot.Horaire, "HH:mm", CultureInfo.InvariantCulture);
                                    slotDateTime = new DateTime(slotDateTime.Year, slotDateTime.Month, slotDateTime.Day, slotTime.Hour, slotTime.Minute, 0);

                                    // Vérifiez si la date est jouable
                                    bool playableDateFound = weeksWithPlayableDates[weekKey].Any(playableDate => playableDate.Date == slotDateTime.Date);

                                    if (playableDateFound)
                                    {
                                        // Vérifiez si l'équipe locale a un match prévu sur un jour férié
                                        if (holidays.Select(d => d.Date).Contains(slotDateTime.Date))
                                        {
                                            fitness -= 5000;
                                            contraintes += 5000;
                                        }

                                    }
                                    else
                                    {
                                        Console.WriteLine("\t\tHoraire tenté : {0}", slotDateTime);
                                        fitness -= 5000;
                                        contraintes += 5000;
                                    }
                                }
                                else
                                {
                                    // Gérer le cas où la clé n'est pas trouvée dans weeksWithPlayableDates
                                }
                            }

                        }
                    }
                }
            }
            Console.WriteLine("Dernier score de fitness : {0}", fitness);
            return Tuple.Create(fitness, contraintes, finalEquipePlacements);
        }

        static List<List<Inscription>> Selection(List<List<Inscription>> population, int nombreParents, List<RepartitionPoule> repartitionPoules, List<Inscription> poule, Random random)
        {
            // Initialisation de la liste des parents
            List<List<Inscription>> parents = new List<List<Inscription>>();

            // Calcul des fitness pour chaque individu de la population
            List<int> fitnesses = new List<int>();
            foreach (List<Inscription> individu in population)
            {
                fitnesses.Add(Fitness(new List<List<Inscription>> { individu }, repartitionPoules, ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff), calculJourFerieByPeriode(debutPeriodeMatchs, finPeriodeMatchs)).Item1);
            }

            // Calcul de la somme totale des fitness
            int totalFitness = fitnesses.Sum();

            // Sélection proportionnelle à la fitness pour choisir les parents
            for (int i = 0; i < nombreParents; i++)
            {
                int randVal;
                if (totalFitness > 0)
                {
                    randVal = random.Next(totalFitness);
                }
                else
                {
                    // Gérer le cas où totalFitness est zéro ou négatif
                    randVal = random.Next(population.Count);
                }

                int currentIndex = 0;
                int cumulativeFitness = fitnesses[currentIndex];

                while (cumulativeFitness < randVal && currentIndex < fitnesses.Count - 1)
                {
                    currentIndex++;
                    cumulativeFitness += fitnesses[currentIndex];
                }

                parents.Add(population[currentIndex]);
            }
            return parents;
        }
        public static List<Inscription> Croisement(List<Inscription> parent1, List<Inscription> parent2, double probabiliteCroisement)
        {
            // Vérifier si le croisement a lieu
            if (new Random().NextDouble() > probabiliteCroisement)
            {
                return parent1;
            }

            int taillePoule = parent1.Count;
            List<Inscription> enfant = new List<Inscription>(new Inscription[taillePoule]);

            // Diviser les parents en sous-listes en fonction de CodeCompetition, Division et Poule
            var groupesParent1 = parent1.GroupBy(x => new { x.CodeCompetition, x.Division, x.Poule });
            var groupesParent2 = parent2.GroupBy(x => new { x.CodeCompetition, x.Division, x.Poule });

            foreach (var groupeParent1 in groupesParent1)
            {
                var groupeParent2 = groupesParent2.FirstOrDefault(g => g.Key.Equals(groupeParent1.Key));

                if (groupeParent2 != null)
                {
                    List<Inscription> sousListeEnfant = new List<Inscription>(new Inscription[groupeParent1.Count()]);

                    // Générer un masque aléatoire pour le croisement uniforme
                    List<bool> masque = new List<bool>(new bool[groupeParent1.Count()]);
                    for (int i = 0; i < masque.Count; i++)
                    {
                        masque[i] = random.Next(0, 2) == 1;
                    }

                    // Croisement uniforme pour chaque groupe de CodeCompetition, Division et Poule
                    for (int i = 0; i < groupeParent1.Count(); i++)
                    {
                        if (masque[i])
                        {
                            sousListeEnfant[i] = groupeParent1.ElementAt(i);
                        }
                        else
                        {
                            sousListeEnfant[i] = groupeParent2.ElementAt(i);
                        }
                    }

                    // Ajouter la sous-liste dans l'enfant
                    int indexEnfant = 0;
                    foreach (Inscription inscription in sousListeEnfant)
                    {
                        while (enfant[indexEnfant] != null)
                        {
                            indexEnfant++;
                        }
                        enfant[indexEnfant] = inscription;
                        indexEnfant++;
                    }
                }
            }

            return enfant;
        }
        public static void Mutation(List<Inscription> individu, double probabiliteMutation)
        {
            int taillePoule = individu.Count;

            for (int i = 0; i < taillePoule; i++)
            {
                if (new Random().NextDouble() < probabiliteMutation)
                {
                    int indexAleatoire = new Random().Next(taillePoule);
                    Inscription temp = individu[i];
                    individu[i] = individu[indexAleatoire];
                    individu[indexAleatoire] = temp;
                }
            }
        }

        //Modifier entrant pour avoir un dictionnaire de Numéro de semaine en clef avec liste de jour jouable en valeur
        public static Dictionary<Tuple<int, int>, List<DateTime>> ObtenirListeJourJouables(DateTime startDate, DateTime endDate, DateTime startOffPeriod, DateTime endOffPeriod)
        {
            Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates = new Dictionary<Tuple<int, int>, List<DateTime>>();

            List<DateTime> holidays = calculJourFerieByPeriode(startDate, endDate);

            for (DateTime currentDate = startDate; currentDate <= endDate; currentDate = currentDate.AddDays(1))
            {
                if (currentDate >= startOffPeriod && currentDate <= endOffPeriod)
                {
                    continue;
                }

                if (currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday)
                {
                    continue;
                }

                if (holidays.Contains(currentDate))
                {
                    continue;
                }

                (int weekNumber, int weekYear) = getWeekNumber(currentDate);

                if (!weeksWithPlayableDates.ContainsKey(Tuple.Create(weekNumber, weekYear)))
                {
                    weeksWithPlayableDates[Tuple.Create(weekNumber, weekYear)] = new List<DateTime>();
                }

                weeksWithPlayableDates[Tuple.Create(weekNumber, weekYear)].Add(currentDate);
            }

            return weeksWithPlayableDates;
        }
        public static void CreerMatchs(List<List<Inscription>> resultatPoules, Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates, List<Creneau> creneaux)
        {
            List<RepartitionPoule> repartitionPoules = Globals.ConvertToList<RepartitionPoule>(SQL.GetAll_RepartitionPoule());

            int pouleIndex = 0;
            int nbMatch = 0;
            foreach (List<Inscription> poule in resultatPoules)
            {
                pouleIndex++;
                int pouleSize = poule.Count;
                List<RepartitionPoule> repartitionFiltree = repartitionPoules.Where(r => r.Poule == pouleSize && r.Tour == tour).ToList();

                Console.WriteLine("Poule {0} avec {1} équipes et {2} matchs de prévu ({3}-{4}-{5}) :", pouleIndex, poule.Count, (poule.Count * (poule.Count - 1)) / 2, poule[0].CodeCompetition, poule[0].Division, poule[0].Poule);
                int journee = 1;
                foreach (Tuple<int, int> weekKey in weeksWithPlayableDates.Keys)
                {
                    Console.WriteLine("  Semaine {0}, journée {1} :", weekKey, journee);
                    List<RepartitionPoule> matchsSemaine = repartitionFiltree.Where(r => Int32.Parse(r.Journee) == (journee)).ToList();
                    foreach (RepartitionPoule match in matchsSemaine)
                    {
                        Inscription locaux = poule[Char.ToUpper(match.Locaux[0]) - 'A'];
                        Inscription visiteurs = poule[Char.ToUpper(match.Visiteur[0]) - 'A'];

                        if (locaux.NomEquipe == "----------" || visiteurs.NomEquipe == "----------") // Fausse équipe ajoutée pour avoir des poules de 6 et 8 uniquement
                        {
                            continue;
                        }

                        Creneau homeTeamSlot = creneaux.FirstOrDefault(c => c.EquipeCode == locaux.CodeEquipe);

                        if (homeTeamSlot != null && weeksWithPlayableDates.ContainsKey(weekKey))
                        {
                            DateTime firstDayOfWeek = weeksWithPlayableDates[weekKey].OrderBy(d => d).First();
                            DateTime slotDateTime = firstDayOfWeek.AddDays(homeTeamSlot.CodeJourCreneaux - 1);

                            DateTime slotTime = DateTime.ParseExact(homeTeamSlot.Horaire, "HH:mm", CultureInfo.InvariantCulture);
                            slotDateTime = new DateTime(slotDateTime.Year, slotDateTime.Month, slotDateTime.Day, slotTime.Hour, slotTime.Minute, 0);

                            bool playableDateFound = weeksWithPlayableDates[weekKey].Any(playableDate => playableDate.Date == slotDateTime.Date);

                            if (playableDateFound)
                            {
                                Console.WriteLine("    Match {0} vs {1} le {2:dd/MM/yyyy} à {3:HH:mm}", locaux.CodeEquipe, visiteurs.CodeEquipe, slotDateTime, slotDateTime);
                            }
                            else
                            {
                                Console.WriteLine("\t\tHoraire tenté : {0}", slotDateTime);
                            }
                            nbMatch++;
                        }
                        else
                        {
                            Console.WriteLine("Problème de créneau");
                        }
                    }

                    journee++;
                }
            }
            Console.WriteLine("Nombre de match total : {0}", nbMatch);
        }
        public static List<DateTime> initDate(DateTime debutPeriodeMatchs, DateTime finPeriodeMachs, DateTime debutPeriodeOff, DateTime finPeriodeOff)
        {
            List<DateTime> joursMatchs = new List<DateTime>();
            List<DateTime> joursOff = calculJourFerieByPeriode(debutPeriodeMatchs, finPeriodeMachs);
            for (DateTime date = debutPeriodeMatchs; date <= finPeriodeMachs; date = date.AddDays(1))
            {
                if (date >= debutPeriodeOff && date <= finPeriodeOff)
                {
                    continue;
                }
                if (joursOff.Contains(date))
                {
                    continue;
                }
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                {
                    continue;
                }
                joursMatchs.Add(date);
                //Console.WriteLine("Jour : {0} de la semaine {1}", date, getWeekNumber(date));
            }
            return joursMatchs;
        }
        private static DateTime GetFirstDateOfWeek(int year, int weekNumber, int dayOfWeek)
        {
            DateTime jan1 = new DateTime(year, 1, 1);
            DateTime startOfWeek = jan1.AddDays((weekNumber - 1) * 7 - (int)jan1.DayOfWeek + (int)DayOfWeek.Monday);
            DateTime adjustedDate = startOfWeek.AddDays(dayOfWeek - 1);

            // Ajuster la date pour éviter les week-ends avant la période off
            while (adjustedDate < debutPeriodeOff && (adjustedDate.DayOfWeek == DayOfWeek.Saturday || adjustedDate.DayOfWeek == DayOfWeek.Sunday))
            {
                adjustedDate = adjustedDate.AddDays(1);
            }

            // Ajuster la date pour éviter la période off et les week-ends après la période off
            if (adjustedDate >= debutPeriodeOff && adjustedDate <= finPeriodeOff)
            {
                adjustedDate = finPeriodeOff.AddDays(1);

                while (adjustedDate.DayOfWeek == DayOfWeek.Saturday || adjustedDate.DayOfWeek == DayOfWeek.Sunday)
                {
                    adjustedDate = adjustedDate.AddDays(1);
                }
            }

            return adjustedDate;
        }
        public static List<DateTime> calculJourFerieByPeriode(DateTime debut, DateTime fin)
        {
            List<DateTime> jourOff = new List<DateTime>();
            DateTime Paques, Ascension, Pentecote;

            int Y = debut.Year;                         // Annee
            int golden;                      // Nombre d'or
            int solar;                       // Correction solaire
            int lunar;                       // Correction lunaire
            int pfm;                         // Pleine lune de paques
            int dom;                         // Nombre dominical
            int easter;                      // jour de paques
            int tmp;

            // Nombre d'or
            golden = (Y % 19) + 1;
            if (Y <= 1752)            // Calendrier Julien
            {
                // Nombre dominical
                dom = (Y + (Y / 4) + 5) % 7;
                if (dom < 0) dom += 7;
                // Date non corrigee de la pleine lune de paques
                pfm = (3 - (11 * golden) - 7) % 30;
                if (pfm < 0) pfm += 30;
            }
            else                       // Calendrier Gregorien
            {
                // Nombre dominical
                dom = (Y + (Y / 4) - (Y / 100) + (Y / 400)) % 7;
                if (dom < 0) dom += 7;
                // Correction solaire et lunaire
                solar = (Y - 1600) / 100 - (Y - 1600) / 400;
                lunar = (((Y - 1400) / 100) * 8) / 25;
                // Date non corrigee de la pleine lune de paques
                pfm = (3 - (11 * golden) + solar - lunar) % 30;
                if (pfm < 0) pfm += 30;
            }
            // Date corrige de la pleine lune de paques :
            // jours apres le 21 mars (equinoxe de printemps)
            if ((pfm == 29) || (pfm == 28 && golden > 11)) pfm--;

            tmp = (4 - pfm - dom) % 7;
            if (tmp < 0) tmp += 7;

            // Paques en nombre de jour apres le 21 mars
            easter = pfm + tmp + 1;

            if (easter < 11)
            {
                Paques = DateTime.Parse((easter + 21) + "/3/" + Y);
            }
            else
            {
                Paques = DateTime.Parse((easter - 10) + "/4/" + Y);
            }

            DateTime Janvier1 = new DateTime(Y, 1, 1);
            DateTime Mai1 = new DateTime(Y, 5, 1);
            DateTime Mai8 = new DateTime(Y, 5, 8);
            DateTime Juillet14 = new DateTime(Y, 7, 14);
            DateTime Aout15 = new DateTime(Y, 8, 15);
            DateTime Toussaint = new DateTime(Y, 11, 1);
            DateTime Novembre11 = new DateTime(Y, 11, 11);
            DateTime Noel = new DateTime(Y, 12, 25);

            jourOff.Add(Ascension = Paques.AddDays(39));
            jourOff.Add(Pentecote = Paques.AddDays(50));
            jourOff.Add(Paques = Paques.AddDays(1));
            jourOff.Add(Janvier1);
            jourOff.Add(Mai1);
            jourOff.Add(Mai8);
            jourOff.Add(Juillet14);
            jourOff.Add(Aout15);
            jourOff.Add(Toussaint);
            jourOff.Add(Novembre11);
            jourOff.Add(Noel);

            jourOff = jourOff.Where(j => j >= debut && j <= fin).ToList();

            return jourOff;
        }
        public static (int weekNumber, int weekYear) getWeekNumber(DateTime date)
        {
            System.Globalization.Calendar cal = System.Globalization.DateTimeFormatInfo.CurrentInfo.Calendar;
            int weekNumber = cal.GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            int weekYear = date.Year;
            if (weekNumber == 1 && date.Month == 12)
            {
                weekYear++;
            }
            else if (weekNumber >= 52 && date.Month == 1)
            {
                weekYear--;
            }
            return (weekNumber, weekYear);
        }
        public static int GetYearByWeek(int weekNumber, DateTime startDate, DateTime endDate)
        {
            DateTime startOfYear = new DateTime(startDate.Year, 1, 1);
            while (startOfYear.DayOfWeek != DayOfWeek.Monday)
            {
                startOfYear = startOfYear.AddDays(1);
            }

            DateTime firstDateOfWeek = startOfYear.AddDays((weekNumber - 1) * 7);
            if (firstDateOfWeek < startDate)
            {
                return startDate.Year;
            }
            else if (firstDateOfWeek > endDate)
            {
                return endDate.Year;
            }
            else
            {
                return firstDateOfWeek.Year;
            }
        }

    }
}
