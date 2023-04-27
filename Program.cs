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
using System.IO;
using System.Text;

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

        //Date de la coupe : 
        static DateTime debutPeriodeMatchs = new DateTime(2023, 4, 3);
        static DateTime finPeriodeMatchs = new DateTime(2023, 5, 26);
        static DateTime debutPeriodeOff = new DateTime(2023, 4, 24);
        static DateTime finPeriodeOff = new DateTime(2023, 5, 5);
        static DateTime debutPeriodeOff2 = new DateTime(2023, 5, 15);
        static DateTime finPeriodeOff2 = new DateTime(2023, 5, 19);

        //Date de championnant (phase aller)
        /*static DateTime debutPeriodeMatchs = new DateTime(2022, 11, 7);
        static DateTime finPeriodeMatchs = new DateTime(2023,1, 13);
        static DateTime debutPeriodeOff = new DateTime(2022, 12, 12);
        static DateTime finPeriodeOff = new DateTime(2022, 12, 30);
        static DateTime debutPeriodeOff2 = new DateTime(2023, 1, 1);
        static DateTime finPeriodeOff2 = new DateTime(2023, 1, 1);*/

        static int saison = 2023;
        static string typeDeCompetition = "CO";
        static string tour = "A";
        static void Main(string[] args)
        {
            List<List<Inscription>> resultatPoules = new List<List<Inscription>>();

            Stopwatch stopwatch = new Stopwatch();

            stopwatch.Start();

            List<Competition> competitions = Globals.ConvertToList<Competition>(SQL.Get_CompetitionsSaison(saison));
            List<Creneau> creneaux = Globals.ConvertToList<Creneau>(SQL.GetAll_CreneauBySaison(saison, true)).OrderBy(x => x.CodeJourCreneaux).ToList();
            List<RepartitionPoule> repartitionPoules = Globals.ConvertToList<RepartitionPoule>(SQL.GetAll_RepartitionPoule());
            List<Inscription> inscriptions = Globals.ConvertToList<Inscription>(SQL.GetAll_InscriptionBySaisonTypeCompetition(saison, typeDeCompetition));

            
            int nombreGenerationMax = 10000;
            int nombreGeneration = 0;
            double scoreParent = 0;
            double scoreMutation;
            double poidsContrainteEquipe = 50.0;
            double poidsContrainteJourFerie = 1;
            double probabiliteCroisement = 0.8;
            double probabiliteMutation = 0.2;

            int contraintesEquipe = 99999;
            int contraintesJourFerie = 9999;
            List<List<Inscription>> populationPoule = new List<List<Inscription>>();
            Dictionary<string, List<Inscription>> inscriptionsParPoule = inscriptions
                .GroupBy(i => new { i.CodeCompetition, i.Division, i.Poule })
                    .ToDictionary(g => g.Key.ToString(), g => g.ToList());
            Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates = new Dictionary<Tuple<int, int>, List<DateTime>>();
            weeksWithPlayableDates = ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff, debutPeriodeOff2, finPeriodeOff2);
            Dictionary<string, int> pouleIndices = new Dictionary<string, int>();

            //On prends en priorité les plus grosses poules
            //Poule les plus nombreuses avec le moins de fausses équipes

            foreach (List<Inscription> poule in inscriptionsParPoule.Values.OrderByDescending(x => x.Count).ThenByDescending(x => x.Count(e => e.Contrainte != string.Empty)).ThenBy(x => x.Count(e => e.NomEquipe == "----------")))
            {
                populationPoule.AddRange(CreerPopulationInitiale(poule, random));
            }

            int nombreParents = (int)Math.Ceiling(ObtenirNombreDePoulesParCompetition(populationPoule) * 0.5); // 50% des poules

            Console.WriteLine("Parents : " + nombreParents);

            string csvFileName = "evolution_contraintes.csv";
            File.WriteAllText(csvFileName, "Generation;ContraintesEquipes;ContraintesJoursFeries\n");
            List<List<Inscription>> meilleurePopulation = null;
            double meilleurScore = double.MaxValue;
            Tuple<int, int> fitnessPopulation = null;
            do
            {
                List<List<Inscription>> parents = Selection(populationPoule, nombreParents, repartitionPoules, ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff, debutPeriodeOff2, finPeriodeOff2), random);

                // Mutation des parents et remplacement dans la population
                List<Inscription> pouleMutee = new List<Inscription>();
                List<Inscription> pouleCroisee = new List<Inscription>();
                foreach (List<Inscription> parent in parents)
                {
                    //Calcul de la valeur de la poule avant sa mutation
                    Tuple<int, int> fitnessParent = FitnessPoule(parent, populationPoule, repartitionPoules, weeksWithPlayableDates);
                    int contrainteEquipeParent = fitnessParent.Item1;
                    int contrainteCalendrierParent = fitnessParent.Item2;
                    scoreParent = contrainteEquipeParent * poidsContrainteEquipe + contrainteCalendrierParent * poidsContrainteJourFerie;
                    //Echange de position de deux équipes au sein de la poule (position échangées entre deux couples de position valide)
                    pouleCroisee = Croisement(parent, probabiliteCroisement, random);

                    //Echange de position de couple d'équipe (ex : 1 et 2 change avec 3 et 4) et changement de position au sein d'un couple d'équipe (ex : 1 et 2 deviennent 2 et 1)
                    pouleMutee = Mutation(pouleCroisee, probabiliteMutation, random, repartitionPoules, ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff, debutPeriodeOff2, finPeriodeOff2));

                    //Calcul de la valeur de la poule après croisement et mutation
                    Tuple<int, int> fitnessPouleMutee = FitnessPoule(pouleMutee, populationPoule, repartitionPoules, weeksWithPlayableDates);
                    int contrainteEquipeMutation = fitnessPouleMutee.Item1;
                    int contrainteCalendrierMutation = fitnessPouleMutee.Item2;                    
                    scoreMutation = contrainteEquipeMutation * poidsContrainteEquipe + contrainteCalendrierMutation * poidsContrainteJourFerie;

                    //Plus le score est petit plus la poule est intéressante
                    if (scoreMutation < scoreParent)
                    {
                        TrouverEtRemplacer(populationPoule, pouleMutee);
                    }

                    Tuple<int, int> fitnessPopulationActuelle = FitnessPopulation(populationPoule, repartitionPoules, weeksWithPlayableDates);
                    contraintesEquipe = fitnessPopulationActuelle.Item1;
                    contraintesJourFerie = fitnessPopulationActuelle.Item2;
                    double scoreActuel = contraintesEquipe * poidsContrainteEquipe + contraintesJourFerie * poidsContrainteJourFerie;

                    //Console.WriteLine("Ancienne contrainte : {0} // {1} : Nouvelle contrainte", contrainteEquipeParent, contrainteEquipeMutation);
                    if (scoreActuel < meilleurScore)
                    {
                        meilleurScore = scoreActuel;
                        meilleurePopulation = new List<List<Inscription>>(populationPoule);
                    }
                }
                fitnessPopulation = FitnessPopulation(populationPoule, repartitionPoules, ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff, debutPeriodeOff2, finPeriodeOff2));

                nombreGeneration++;
                Console.WriteLine("Nombre de contraintes d'équipes : {0}", fitnessPopulation.Item1);
                Console.WriteLine("Nombre de contraintes de jours férié: {0}", fitnessPopulation.Item2);
                Console.WriteLine("Génération {0}/{1}", nombreGeneration, nombreGenerationMax);

                File.AppendAllText(csvFileName, $"{nombreGeneration};{fitnessPopulation.Item1};{fitnessPopulation.Item2}\n");

            } while (!((fitnessPopulation.Item1 < 1 && fitnessPopulation.Item2 < 3) || nombreGeneration >= nombreGenerationMax));

            contraintesEquipe = FitnessPopulation(meilleurePopulation, repartitionPoules, ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff, debutPeriodeOff2, finPeriodeOff2)).Item1;
            contraintesJourFerie = FitnessPopulation(meilleurePopulation, repartitionPoules, ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff, debutPeriodeOff2, finPeriodeOff2)).Item2;


            Console.WriteLine("Nombre de contraintes d'équipes : {0}", contraintesEquipe);
            Console.WriteLine("Nombre de contraintes de jours férié: {0}", contraintesJourFerie);
            Console.WriteLine("Génération {0}/{1}", nombreGeneration, nombreGenerationMax);

            CreerMatchs(meilleurePopulation, ObtenirListeJourJouables(debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff, debutPeriodeOff2, finPeriodeOff2), repartitionPoules);

            stopwatch.Stop();

            Console.WriteLine("Effectué en {0}s", stopwatch.ElapsedMilliseconds / 1000);
            while (Console.ReadKey().Key != ConsoleKey.Enter) ;
        }
        /// <summary>
        /// Obtient le nombre total de poules pour une compétition donnée.
        /// </summary>
        /// <param name="inscriptions">La liste des inscriptions pour la compétition</param>
        /// <param name="competitionId">L'identifiant de la compétition</param>
        /// <returns>Le nombre de poules pour la compétition donnée</returns>
        public static int ObtenirNombreDePoulesParCompetition(List<List<Inscription>> population)
        {
            return population
                .SelectMany(poule => poule)
                .GroupBy(inscription => new { inscription.CodeCompetition, inscription.Division, inscription.Poule })
                .Distinct()
                .Count();
        }

        public static void TrouverEtRemplacer(List<List<Inscription>> population, List<Inscription> poule)
        {
            if (poule == null || poule.Count == 0)
            {
                throw new ArgumentException("La liste 'poule' est vide ou nulle.");
            }

            string clefPoule = poule[0].CodeCompetition + poule[0].Division + poule[0].Poule;

            for (int i = 0; i < population.Count; i++)
            {
                if (population[i] == null || population[i].Count == 0)
                {
                    
                    throw new ArgumentException($"La liste 'population[{i}]' est vide ou nulle.");
                }

                string clefPopulation = population[i][0].CodeCompetition + population[i][0].Division + population[i][0].Poule;

                if (clefPopulation == clefPoule)
                {
                    population.RemoveAt(i);

                    population.Insert(i, poule);

                    return;
                }
            }

            return;
        }

        /// <summary>
        /// Crée une fausse inscription pour une compétition, une division et une poule donnée.
        /// </summary>
        /// <param name="codeCompetition">Le code de la compétition</param>
        /// <param name="division">La division de l'équipe</param>
        /// <param name="poule">La poule de l'équipe</param>
        /// <returns>Une nouvelle instance de l'objet Inscription avec des informations fictives</returns>
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
        /// <summary>
        /// Crée une population initiale de poules en fonction des inscriptions, du nombre de poules et d'un objet Random.
        /// </summary>
        /// <param name="inscriptions">La liste des inscriptions pour les poules</param>
        /// <param name="nombrePoules">Le nombre de poules à créer</param>
        /// <param name="random">Un objet Random pour mélanger les inscriptions</param>
        /// <returns>Une liste de poules, chaque poule étant elle-même une liste d'inscriptions mélangées</returns>
        static List<List<Inscription>> CreerPopulationInitiale(List<Inscription> inscriptions, Random random)
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
        /// <summary>
        /// Calcule le score de fitness d'une population d'inscriptions pour une saison, en fonction des contraintes de répartition de poules et de dates de jeu.
        /// </summary>
        /// <param name="population">La population d'inscriptions à évaluer.</param>
        /// <param name="repartitionPoules">La liste des répartitions de poules pour la saison.</param>
        /// <param name="weeksWithPlayableDates">Un dictionnaire contenant les semaines de la saison qui ont des dates jouables.</param>
        /// <param name="holidays">Une liste de jours fériés à éviter pour la saison.</param>
        /// <returns>Un tuple contenant le score de fitness, le nombre de contraintes rencontrées et la liste des placements d'équipes finaux.</returns>
        static Tuple<int, int, List<Dictionary<char, Inscription>>> Fitness(List<List<Inscription>> population, List<RepartitionPoule> repartitionPoules, Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates, List<DateTime> holidays)
        {
            int contraintesJourFerie = 0;
            int contraintesEquipe = 0;
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
                HashSet<Inscription> evaluatedTeams = new HashSet<Inscription>();

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
                                    RepartitionPoule contrainteRepartition = repartitionList.FirstOrDefault(r =>
                                    {
                                        if (equipePlacement.TryGetValue(r.Locaux[0], out Inscription equipeInDict))
                                        {
                                            return equipeInDict.NomEquipe == contrainteEquipe.NomEquipe;
                                        }
                                        return false;
                                    });
                                    if (contrainteRepartition != null)
                                    {
                                        int index = Int32.Parse(repartition.Journee) - 1;
                                        if (index >= 0 && index < weeksWithPlayableDates.Keys.Count)
                                        {
                                            (int weekNumber, int weekYear) = weeksWithPlayableDates.Keys.ElementAt(index);
                                            Tuple<int, int> weekKey = Tuple.Create(weekNumber, weekYear);
                                            if (weeksWithPlayableDates.TryGetValue(weekKey, out List<DateTime> playableDates))
                                            {
                                                DateTime weekStartDate = playableDates[0];
                                                DateTime weekEndDate = weekStartDate.AddDays(6);

                                                if (equipeLocale.Jour == contrainteEquipe.Jour && weeksWithPlayableDates[weekKey].Any(date => date.Date >= weekStartDate && date.Date <= weekEndDate))
                                                {
                                                    contraintesEquipe++;
                                                    //Console.WriteLine("Contraintes entre équipes ! Score de fitness : {0}", contraintesEquipe);

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
                            if (!evaluatedTeams.Contains(equipeLocale))
                            {
                                if (equipeLocale.Jour != null)
                                {
                                    int index = Int32.Parse(repartition.Journee) - 1;
                                    if (index >= 0 && index < weeksWithPlayableDates.Keys.Count)
                                    {
                                        (int weekNumber, int weekYear) = weeksWithPlayableDates.Keys.ElementAt(index);
                                        Tuple<int, int> weekKey = Tuple.Create(weekNumber, weekYear);
                                        if (weeksWithPlayableDates.TryGetValue(weekKey, out List<DateTime> playableDates))
                                        {

                                            DateTime firstDayOfWeek = playableDates.OrderBy(date => date).First();
                                            //Console.WriteLine("Equipe locale jour {0} first date of week {1}", equipeLocale.Jour, firstDayOfWeek.DayOfWeek);
                                            DateTime slotDateTime = firstDayOfWeek.AddDays((int)equipeLocale.Jour - (int)firstDayOfWeek.DayOfWeek);



                                            // Utilisez ici le créneau horaire approprié pour l'équipe locale
                                            Creneau homeTeamSlot = creneaux.FirstOrDefault(c => c.EquipeCode == equipeLocale.CodeEquipe);
                                            DateTime slotTime = DateTime.ParseExact(homeTeamSlot.Horaire, "HH:mm", CultureInfo.InvariantCulture);
                                            slotDateTime = new DateTime(slotDateTime.Year, slotDateTime.Month, slotDateTime.Day, slotTime.Hour, slotTime.Minute, 0);

                                            // Vérifiez si la date est jouable
                                            bool playableDateFound = weeksWithPlayableDates[weekKey].Any(playableDate => playableDate.Date == slotDateTime.Date);

                                            if (!playableDateFound)
                                            {
                                                //Console.WriteLine("\t\tHoraire tenté (fitness) : {0}", slotDateTime);
                                                contraintesJourFerie++;
                                            }
                                        }
                                        else
                                        {
                                            // Gérer le cas où la clé n'est pas trouvée dans weeksWithPlayableDates
                                        }
                                    }

                                }


                                // Ajoutez l'équipe évaluée au HashSet
                                evaluatedTeams.Add(equipeLocale);
                            }
                            // Vérifier si l'équipe locale a un match prévu sur un jour férié
                        }
                    }
                }
            }
            //Console.WriteLine("Il y a {0} contraintes d'équipe et {1} de jour fériés", contraintesEquipe, contraintesJourFerie);
            return Tuple.Create(contraintesEquipe, contraintesJourFerie, finalEquipePlacements);
        }

        static Tuple<int, int> FitnessPopulation(List<List<Inscription>> population, List<RepartitionPoule> repartitionPoules, Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates)
        {
            int contraintesEquipe = 0;
            int contraintesCalendrier = 0;

            foreach (List<Inscription> poule in population)
            {
                foreach (Inscription equipe in poule)
                {
                    int indexEquipeContrainte = 0;
                    int indexEquipe = 0;
                    if (equipe == null)
                    {
                        continue;
                    }
                    //Nombre de contraintes de positionnement vs Nombre de contraintes dans les matchs
                    if (!equipe.Contrainte.Equals("") || equipe.Contrainte != null)
                    {
                        foreach (List<Inscription> poules in population)
                        {
                            if (poules.Exists(x => x.NomEquipe == equipe.Contrainte))
                            {
                                Inscription equipeContrainte = poules.Find(x => x.NomEquipe == equipe.Contrainte);
                                indexEquipeContrainte = poules.FindIndex(x => x.CodeEquipe == equipeContrainte.CodeEquipe);

                                indexEquipe = poule.FindIndex(a => a.CodeEquipe == equipe.CodeEquipe);

                                //Vérification des positions dans la poule (0 et 1, 2 et 3, etc)

                                if ((indexEquipe == indexEquipeContrainte - 1 && indexEquipeContrainte % 2 == 1) || (indexEquipe == indexEquipeContrainte + 1 && indexEquipe % 2 == 1))
                                {

                                }
                                else
                                {
                                    contraintesEquipe++;
                                    //Console.WriteLine("Indexs : {0}-{1} non valides", indexEquipe, indexEquipeContrainte);
                                }
                            }

                        }
                    }



                }

                //Nombre de journee de compétition en fonction du nombre d'équipe
                int journeeMax = poule.Count > 7 ? 7 : 5;

                (int premiereSemaineJouable, int annee) = getWeekNumber(debutPeriodeMatchs);

                int anneeEnCours = annee;
                int semaineEnCours = premiereSemaineJouable;

                for (int journee = 1; journee <= journeeMax; journee++)
                {
                    List<RepartitionPoule> repartitions = repartitionPoules.Where(x => x.Poule == poule.Count &&
                                                                                  x.Tour.Equals(tour) &&
                                                                                  Int32.Parse(x.Journee) == journee).ToList();



                    List<DateTime> joursSemaineEnCours = weeksWithPlayableDates.ElementAt(journee - 1).Value;

                    //Si moins de 5 jours, la semaine contient un jour férié
                    if (joursSemaineEnCours.Count < 5)
                    {
                        List<int> missingDayNumbers = new List<int>();

                        //Liste tous les jours fériés de cette semaine
                        for (DayOfWeek day = DayOfWeek.Monday; day <= DayOfWeek.Friday; day++)
                        {
                            if (!joursSemaineEnCours.Any(date => date.DayOfWeek == day))
                            {
                                int dayNumber = (int)day - (int)DayOfWeek.Monday + 1;
                                missingDayNumbers.Add(dayNumber);
                            }
                        }

                        List<int> indexs = new List<int>();


                        //Récupération des indexs des équipes a domicile pour cette journée (- 65 pour caractère ASCII)
                        foreach (RepartitionPoule repartition in repartitions)
                        {
                            indexs.Add(repartition.Locaux[0] - 65);
                        }

                        //Pour toutes les équipes à domicile de la poule en cours
                        foreach (int index in indexs)
                        {
                            Inscription equipe = poule.ElementAt(index);
                            if (equipe.NomEquipe != "----------")
                            {
                                //Parcours des jours fériés
                                foreach (int day in missingDayNumbers)
                                {
                                    //Si le créneau de l'équipe à domicile est un jour férié
                                    if (equipe.Jour == day)
                                    {
                                        contraintesCalendrier++;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return Tuple.Create(contraintesEquipe, contraintesCalendrier);
        }

        static Tuple<int, int> FitnessPoule(List<Inscription> poule, List<List<Inscription>> population, List<RepartitionPoule> repartitionPoules, Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates)
        {
            int contraintesEquipe = 0;
            int contraintesCalendrier = 0;


            foreach (Inscription equipe in poule)
            {
                int indexEquipeContrainte = 0;
                int indexEquipe = 0;
                if (equipe == null)
                {
                    continue;
                }
                //Nombre de contraintes de positionnement vs Nombre de contraintes dans les matchs
                if (!equipe.Contrainte.Equals("") || equipe.Contrainte != null)
                {
                    foreach (List<Inscription> poules in population)
                    {
                        if (poules.Exists(x => x.NomEquipe == equipe.Contrainte))
                        {
                            Inscription equipeContrainte = poules.Find(x => x.NomEquipe == equipe.Contrainte);
                            indexEquipeContrainte = poules.FindIndex(x => x.CodeEquipe == equipeContrainte.CodeEquipe);

                            indexEquipe = poule.FindIndex(a => a.CodeEquipe == equipe.CodeEquipe);

                            //Vérification des positions dans la poule (0 et 1, 2 et 3, etc)

                            if ((indexEquipe == indexEquipeContrainte - 1 && indexEquipeContrainte % 2 == 1) || (indexEquipe == indexEquipeContrainte + 1 && indexEquipe % 2 == 1))
                            {

                            }
                            else
                            {
                                contraintesEquipe++;
                                //Console.WriteLine("Indexs : {0}-{1} non valides", indexEquipe, indexEquipeContrainte);
                            }
                        }

                    }
                }



            }

            //Nombre de journee de compétition en fonction du nombre d'équipe
            int journeeMax = poule.Count > 7 ? 7 : 5;

            (int premiereSemaineJouable, int annee) = getWeekNumber(debutPeriodeMatchs);

            int anneeEnCours = annee;
            int semaineEnCours = premiereSemaineJouable;

            for (int journee = 1; journee <= journeeMax; journee++)
            {
                List<RepartitionPoule> repartitions = repartitionPoules.Where(x => x.Poule == poule.Count &&
                                                                              x.Tour.Equals(tour) &&
                                                                              Int32.Parse(x.Journee) == journee).ToList();



                List<DateTime> joursSemaineEnCours = weeksWithPlayableDates.ElementAt(journee - 1).Value;

                //Si moins de 5 jours, la semaine contient un jour férié
                if (joursSemaineEnCours.Count < 5)
                {
                    List<int> missingDayNumbers = new List<int>();

                    //Liste tous les jours fériés de cette semaine
                    for (DayOfWeek day = DayOfWeek.Monday; day <= DayOfWeek.Friday; day++)
                    {
                        if (!joursSemaineEnCours.Any(date => date.DayOfWeek == day))
                        {
                            int dayNumber = (int)day - (int)DayOfWeek.Monday + 1;
                            missingDayNumbers.Add(dayNumber);
                        }
                    }

                    List<int> indexs = new List<int>();


                    //Récupération des indexs des équipes a domicile pour cette journée (- 65 pour caractère ASCII)
                    foreach (RepartitionPoule repartition in repartitions)
                    {
                        indexs.Add(repartition.Locaux[0] - 65);
                    }

                    //Pour toutes les équipes à domicile de la poule en cours
                    foreach (int index in indexs)
                    {
                        Inscription equipe = poule.ElementAt(index);
                        if (equipe.NomEquipe != "----------")
                        {
                            //Parcours des jours fériés
                            foreach (int day in missingDayNumbers)
                            {
                                //Si le créneau de l'équipe à domicile est un jour férié
                                if (equipe.Jour == day)
                                {
                                    contraintesCalendrier++;
                                }
                            }
                        }
                    }
                }
            }


            return Tuple.Create(contraintesEquipe, contraintesCalendrier);
        }


        static List<List<Inscription>> Selection(List<List<Inscription>> population, int nombreParents, List<RepartitionPoule> repartitionPoules, Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates, Random random)
        {
            // Initialisation de la liste des parents
            List<List<Inscription>> parents = new List<List<Inscription>>();

            // Clonage de la liste population
            List<List<Inscription>> populationClone = population.Select(x => x.ToList()).ToList();

            // Calcul des contraintes d'équipe et de calendrier pour chaque individu de la population
            List<Tuple<int, int>> contraintesPopulation = new List<Tuple<int, int>>();
            foreach (List<Inscription> individu in population)
            {
                contraintesPopulation.Add(FitnessPoule(individu, population, repartitionPoules, weeksWithPlayableDates));
            }

            // Sélection proportionnelle aux contraintes pour choisir les parents
            for (int i = 0; i < nombreParents; i++)
            {
                // Trouver l'individu avec le maximum de contraintes d'équipe et de calendrier
                int maxIndex = 0;
                for (int j = 1; j < contraintesPopulation.Count; j++)
                {
                    if ((contraintesPopulation[j].Item1 + contraintesPopulation[j].Item2) > (contraintesPopulation[maxIndex].Item1 + contraintesPopulation[maxIndex].Item2))
                    {
                        maxIndex = j;
                    }
                }

                parents.Add(populationClone[maxIndex]);
                //Console.WriteLine("Contrainte de l'individu " + FitnessPoule(populationClone[maxIndex], population, repartitionPoules, weeksWithPlayableDates).Item1 + " " + FitnessPoule(populationClone[maxIndex], population, repartitionPoules, weeksWithPlayableDates).Item2);
                // Retirer l'individu sélectionné de la liste des candidats pour éviter de sélectionner le même parent deux fois
                populationClone.RemoveAt(maxIndex);
                contraintesPopulation.RemoveAt(maxIndex);
            }

            return parents;
        }

      
        public static List<Inscription> Croisement(List<Inscription> individu, double probabiliteCroisement, Random random)
        {
            if(random.NextDouble() < probabiliteCroisement)
            {
                int index = random.Next(0, individu.Count);
                int index2 = random.Next(0, individu.Count);
                Inscription temporaire = individu[index];
                individu[index] = individu[index2];
                individu[index2] = temporaire;
            }

            return individu;
        }

        public static List<Inscription> Mutation(List<Inscription> individu, double probabiliteMutation, Random random, List<RepartitionPoule> repartitionPoules, Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates)
        {
            List<Inscription> newPoule = new List<Inscription>(individu);

            if (random.NextDouble() < probabiliteMutation)
            {
                // Intervertir les équipes 2 à 2
                for (int i = 0; i < newPoule.Count - 1; i += 2)
                {
                    if (random.NextDouble() < probabiliteMutation)
                    {
                        Inscription temp = newPoule[i];
                        newPoule[i] = newPoule[i + 1];
                        newPoule[i + 1] = temp;
                    }
                }
            }
            //Ajouter un autre paramètre avec une valeur différente ?
            if (random.NextDouble() < probabiliteMutation)
            {
                // Échange les couples d'équipes
                for (int i = 0; i < newPoule.Count - 3; i += 4)
                {
                    if (random.NextDouble() < probabiliteMutation)
                    {
                        Inscription temp1 = newPoule[i];
                        Inscription temp2 = newPoule[i + 1];
                        newPoule[i] = newPoule[i + 2];
                        newPoule[i + 1] = newPoule[i + 3];
                        newPoule[i + 2] = temp1;
                        newPoule[i + 3] = temp2;
                    }
                }
            }
            return newPoule;
        }



        //Modifier entrant pour avoir un dictionnaire de Numéro de semaine en clef avec liste de jour jouable en valeur
        /// <summary>
        /// Cette fonction prend en entrée une date de début, une date de fin, une période d'exclusion et retourne un dictionnaire contenant les semaines et les jours où les matchs peuvent être joués.
        /// Les jours exclus sont les jours fériés et les jours de week-end (samedi et dimanche) ainsi que tous les jours compris dans la période d'exclusion.
        /// Les semaines sont déterminées en fonction du numéro de semaine de l'année et de l'année en cours.
        /// </summary>
        /// <param name="startDate">La date de début de la période où les matchs peuvent être joués</param>
        /// <param name="endDate">La date de fin de la période où les matchs peuvent être joués</param>
        /// <param name="startOffPeriod">La date de début de la période d'exclusion des matchs</param>
        /// <param name="endOffPeriod">La date de fin de la période d'exclusion des matchs</param>
        /// <returns>Un dictionnaire contenant les semaines et les jours où les matchs peuvent être joués</returns>
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

        /// <summary>
        /// Retourne un dictionnaire contenant les semaines avec leurs dates jouables.
        /// Les dates sont jouables si elles ne sont pas un jour férié et ne tombent pas dans une période d'indisponibilité.
        /// </summary>
        /// <param name="startDate">La date de début de la période de jeu.</param>
        /// <param name="endDate">La date de fin de la période de jeu.</param>
        /// <param name="startOffPeriod">La date de début de la période d'indisponibilité.</param>
        /// <param name="endOffPeriod">La date de fin de la période d'indisponibilité.</param>
        /// <param name="startOffPeriod2">La date de début de la deuxième période d'indisponibilité.</param>
        /// <param name="endOffPeriod2">La date de fin de la deuxième période d'indisponibilité.</param>
        /// <returns>Un dictionnaire contenant les semaines avec leurs dates jouables.</returns>
        public static Dictionary<Tuple<int, int>, List<DateTime>> ObtenirListeJourJouables(DateTime startDate, DateTime endDate, DateTime startOffPeriod, DateTime endOffPeriod, DateTime startOffPeriod2, DateTime endOffPeriod2)
        {
            Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates = new Dictionary<Tuple<int, int>, List<DateTime>>();

            List<DateTime> holidays = calculJourFerieByPeriode(startDate, endDate);

            HashSet<Tuple<int, int>> excludedWeeks = new HashSet<Tuple<int, int>>();

            for (DateTime currentDate = startDate; currentDate <= endDate; currentDate = currentDate.AddDays(1))
            {
                (int weekNumber, int weekYear) = getWeekNumber(currentDate);

                if ((currentDate >= startOffPeriod && currentDate <= endOffPeriod) || (currentDate >= startOffPeriod2 && currentDate <= endOffPeriod2))
                {
                    excludedWeeks.Add(Tuple.Create(weekNumber, weekYear));
                }
            }

            for (DateTime currentDate = startDate; currentDate <= endDate; currentDate = currentDate.AddDays(1))
            {
                if (currentDate.DayOfWeek == DayOfWeek.Saturday || currentDate.DayOfWeek == DayOfWeek.Sunday)
                {
                    continue;
                }

                if (holidays.Contains(currentDate))
                {
                    continue;
                }

                (int weekNumber, int weekYear) = getWeekNumber(currentDate);

                if (excludedWeeks.Contains(Tuple.Create(weekNumber, weekYear)))
                {
                    continue;
                }

                if (!weeksWithPlayableDates.ContainsKey(Tuple.Create(weekNumber, weekYear)))
                {
                    weeksWithPlayableDates[Tuple.Create(weekNumber, weekYear)] = new List<DateTime>();
                }

                weeksWithPlayableDates[Tuple.Create(weekNumber, weekYear)].Add(currentDate);
            }

            return weeksWithPlayableDates;
        }

        /// <summary>
        /// Génère les matchs à jouer pour chaque poule en utilisant les créneaux horaires disponibles et les dates jouables.
        /// </summary>
        /// <param name="resultatPoules">Liste des équipes pour chaque poule.</param>
        /// <param name="weeksWithPlayableDates">Dictionnaire contenant les semaines jouables pour chaque poule.</param>
        /// <param name="creneaux">Liste des créneaux horaires des équipes.</param>
        /*public static void CreerMatchs(List<List<Inscription>> resultatPoules, Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates, List<Creneau> creneaux)
        {
            List<RepartitionPoule> repartitionPoules = Globals.ConvertToList<RepartitionPoule>(SQL.GetAll_RepartitionPoule());
            List<string> lignes = new List<string>();
            int pouleIndex = 0;
            int nbMatch = 0;
            foreach (List<Inscription> poule in resultatPoules)
            {
                pouleIndex++;
                int pouleSize = poule.Count;
                List<RepartitionPoule> repartitionFiltree = repartitionPoules.Where(r => r.Poule == pouleSize && r.Tour == tour).ToList();

                Console.WriteLine("\n\nPoule {0} avec {1} équipes et {2} matchs de prévu ({3}-{4}-{5}) :", pouleIndex, poule.Count, (poule.Count * (poule.Count - 1)) / 2, poule[0].CodeCompetition, poule[0].Division, poule[0].Poule);
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
                            DateTime slotDateTime = firstDayOfWeek.AddDays(homeTeamSlot.CodeJourCreneaux - (int)firstDayOfWeek.DayOfWeek);
                           
                            DateTime slotTime = DateTime.ParseExact(homeTeamSlot.Horaire, "HH:mm", CultureInfo.InvariantCulture);
                            slotDateTime = new DateTime(slotDateTime.Year, slotDateTime.Month, slotDateTime.Day, slotTime.Hour, slotTime.Minute, 0);

                            //Ecris même les matchs sur jours férié
                            
                            string line = $"{locaux.CodeCompetition};{saison};{locaux.CodeEquipe};{visiteurs.CodeEquipe};;;{locaux.NomEquipe} reçoit {visiteurs.NomEquipe};{weekKey.Item1};{locaux.Jour}";
                            lignes.Add(line);
                               

                            bool playableDateFound = weeksWithPlayableDates[weekKey].Any(playableDate => playableDate.Date == slotDateTime.Date);

                            if (playableDateFound)
                            {
                                Console.WriteLine("    Match {0} vs {1} le {2:dd/MM/yyyy} à {3:HH:mm}", locaux.NomEquipe, visiteurs.NomEquipe, slotDateTime, slotDateTime);
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

            using (
                StreamWriter writer = new StreamWriter("resultat.csv", false, Encoding.Default))
            {
                foreach (string ligne in lignes)
                {
                    writer.WriteLine(ligne);
                }
            }


            Console.WriteLine("Nombre de match total : {0}", nbMatch);
        }
*/
        public static void CreerMatchs(List<List<Inscription>> resultatPoules, Dictionary<Tuple<int, int>, List<DateTime>> weeksWithPlayableDates, List<RepartitionPoule> repartitionPoules)
        {
            List<string> lignes = new List<string>();

            foreach (List<Inscription> poule in resultatPoules)
            {

                int maxJournee = poule.Count > 7 ? 7 : 5;

                for (int journee = 1; journee <= maxJournee; journee++)
                {
                    List<RepartitionPoule> repartitions = repartitionPoules.Where(x => x.Poule == poule.Count &&
                                                                                  x.Tour.Equals(tour) &&
                                                                                  Int32.Parse(x.Journee) == journee).ToList();

                    List<DateTime> joursSemaineEnCours = weeksWithPlayableDates.ElementAt(journee - 1).Value;
                    (int semaine, int annee) = getWeekNumber(joursSemaineEnCours[0]);
                    foreach (RepartitionPoule repartition in repartitions)
                    {
                        int indexLocal = repartition.Locaux[0] - 65;
                        int indexVisiteur = repartition.Visiteur[0] - 65;

                        Inscription equipeLocale = poule.ElementAt(indexLocal);
                        Inscription equipeVisiteur = poule.ElementAt(indexVisiteur);

                        if (equipeLocale.NomEquipe == "----------" || equipeVisiteur.NomEquipe == "----------") // Fausse équipe ajoutée pour avoir des poules de 6 et 8 uniquement
                        {
                            continue;
                        }

                        //Console.WriteLine("Match le {0} de la journee {1} entre {2} et {3}", equipeLocale.Jour, journee, equipeLocale.NomEquipe, equipeVisiteur.NomEquipe);
                        string line = $"{equipeLocale.CodeCompetition};{saison};{equipeLocale.CodeEquipe};{equipeVisiteur.CodeEquipe};;;{equipeLocale.NomEquipe} reçoit {equipeVisiteur.NomEquipe};{semaine};{journee}";
                        lignes.Add(line);
                    }
                }
            }

            using (StreamWriter writer = new StreamWriter("resultat.csv", false, Encoding.Default))
            {
                foreach (string ligne in lignes)
                {
                    writer.WriteLine(ligne);
                }
            }
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

        /// <summary>
        /// Calcule la liste des jours fériés pour une période donnée.
        /// </summary>
        /// <param name="debut">La date de début de la période.</param>
        /// <param name="fin">La date de fin de la période.</param>
        /// <returns>La liste des jours fériés pour la période donnée.</returns>
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

            /*int i = 0;
            foreach(DateTime date in jourOff)
            {
                Console.WriteLine("Jour férié : {0}", jourOff[i]);
                i++;
            }
            while (Console.ReadKey().Key != ConsoleKey.Enter) ;*/


            return jourOff;
        }
        /// <summary>
        /// Renvoie le numéro de semaine et l'année correspondante pour une date donnée.
        /// </summary>
        /// <param name="date">La date pour laquelle on veut obtenir le numéro de semaine et l'année correspondante.</param>
        /// <returns>Un tuple contenant le numéro de semaine et l'année correspondante.</returns>
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
