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
         * EXEC [dbo].[sp_Set_MatchAndCalendarEvent]  @MatchCode = '2022-M001045',  @CompetitionCode =  '2022-C114',  @EquipeLocauxCode =  'VLMF1',   @EquipeVisiteursCode =   'L2VBX3',  @EquipeArbitrageCode = NULL, @GymnaseCode = 'MTP_TESSE' ,   @CalendarEventStartDate
 =   '06/10/2022 20:00:00',   @CalendarEventEndDate = '06/10/2022 22:00:00' , @Journee = 2
GO
         */
        static Random random = new Random();
        static DateTime debutPeriodeMatchs = new DateTime(2022, 11, 7);
        static DateTime finPeriodeMatchs = new DateTime(2023, 1, 13);
        static DateTime debutPeriodeOff = new DateTime(2022, 12, 12);
        static DateTime finPeriodeOff = new DateTime(2022, 12, 30);
        static void Main(string[] args)
        {
            List<List<Inscription>> resultatPoules = new List<List<Inscription>>();

            Stopwatch stopwatch = new Stopwatch();

            stopwatch.Start();
            int saison = 2022;
            int taillePopulation = 10;

            List<Competition> competitions = Globals.ConvertToList<Competition>(SQL.Get_CompetitionsSaison(saison));
            List<Equipe> equipes = Globals.ConvertToList<Equipe>(SQL.GetAll_EquipeBySaison(saison)).OrderBy(x => x.Division).ToList();
            List<Creneau> creneaux = Globals.ConvertToList<Creneau>(SQL.GetAll_CreneauBySaison(saison, true)).OrderBy(x => x.CodeJourCreneaux).ToList();
            List<RepartitionPoule> repartitionPoules = Globals.ConvertToList<RepartitionPoule>(SQL.GetAll_RepartitionPoule());
            List<Inscription> inscriptions = Globals.ConvertToList<Inscription>(SQL.GetAll_InscriptionBySaison(saison));

            int nombreParents = (int)Math.Ceiling(inscriptions.Count * 0.1); // 10% des inscriptions
            int nombreGenerationMax = 100;
            double probabiliteCroisement = 0.8;
            double probabiliteMutation = 0.1;
            int fitness = 0;
            List<List<Inscription>> populationPoule = new List<List<Inscription>>();
            Dictionary<string, List<Inscription>> inscriptionsParPoule = inscriptions
            .GroupBy(i => new { i.CodeCompetition, i.Division, i.Poule })
                .ToDictionary(g => g.Key.ToString(), g => g.ToList());

            int index = 1;
            foreach (List<Inscription> poule in inscriptionsParPoule.Values)
            {
                Console.WriteLine($"Poule {index}:");

                int position = 1;
                foreach (Inscription inscription in poule)
                {
                    Console.WriteLine($"Position {position}: {inscription.NomEquipe}");
                    position++;
                }

                index++;
                Console.WriteLine(); // Ajoute une ligne vide pour séparer les poules
            }

            foreach (List<Inscription> poule in inscriptionsParPoule.Values.OrderBy(x => random.Next()))
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
                    Tuple<int, int, List<Dictionary<char, Inscription>>> fitnessResult = Fitness(new List<List<Inscription>> { enfants[0] }, repartitionPoules);
                    int fitnessEnfant = fitnessResult.Item1;
                    int contraintes = fitnessResult.Item2;
                    // Remplacement de la population actuelle par les enfants si leur fitness est meilleure
                    if (fitnessEnfant >= fitness)
                    {
                        populationPoule = enfants;
                        fitness = fitnessEnfant;
                    }

                    // Affichage du nombre de contraintes pour la génération actuelle
                    //Console.WriteLine("Generation {0} avec {1} de score de fitness", nombreGeneration, fitness);

                    // Incrémentation du compteur de générations
                    nombreGeneration++;

                    // Ajouter une condition pour vérifier si toutes les contraintes sont satisfaites
                    // et arrêter l'algorithme si c'est le cas
                    if (contraintes == 0)

                    {
                        break;
                    }
                }

                // Compare la fitness de la poule d'origine à celle de la meilleure solution trouvée
                int fitnessPouleOrigine = Fitness(new List<List<Inscription>> { poule }, repartitionPoules).Item1;
                if (fitnessPouleOrigine > fitness)
                {
                    resultatPoules.Add(poule);
                }
                else
                {
                    Console.WriteLine("Nombre de contraintes : {0}", Fitness(new List<List<Inscription>> { poule }, repartitionPoules).Item2);
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

            CreerMatchs(resultatPoules, debutPeriodeMatchs, finPeriodeMatchs, debutPeriodeOff, finPeriodeOff);

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
        static Tuple<int, int, List<Dictionary<char, Inscription>>> Fitness(List<List<Inscription>> population, List<RepartitionPoule> repartitionPoules)
        {
            int fitness = 0;
            int contraintes = 0;
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

                foreach (int rang in Enumerable.Range(1, equipeList.Count > 6 ? 8 : 6))
                {
                    foreach (int poule in Enumerable.Range(1, equipeList.Count > 6 ? 8 : 6))
                    {
                        List<RepartitionPoule> repartitionList = repartitionPoules
                            .Where(r => Int32.Parse(r.Rang) == rang && r.Poule == poule && r.Tour.Equals("R"))
                            .ToList();

                        foreach (RepartitionPoule repartition in repartitionList)
                        {
                            foreach (int autrePoule in Enumerable.Range(1, equipeList.Count > 6 ? 8 : 6))
                            {
                                if (autrePoule == poule)
                                {
                                    continue;
                                }

                                List<RepartitionPoule> autresRepartitionList = repartitionPoules
                                    .Where(r => Int32.Parse(r.Rang) == rang && r.Poule == autrePoule && r.Tour.Equals("R"))
                                    .ToList();

                                foreach (RepartitionPoule autresRepartition in autresRepartitionList)
                                {
                                    if (!equipePlacement.TryGetValue(repartition.Locaux[0], out Inscription equipeLocale) ||
                                        !equipePlacement.TryGetValue(repartition.Visiteur[0], out Inscription equipeVisiteur) ||
                                        !equipePlacement.TryGetValue(autresRepartition.Locaux[0], out Inscription autresEquipeLocale) ||
                                        !equipePlacement.TryGetValue(autresRepartition.Visiteur[0], out Inscription autresEquipeVisiteur))
                                    {
                                        continue;
                                    }

                                    if (equipeLocale.Jour != null && equipeLocale.Jour == autresEquipeLocale.Jour)
                                    {
                                        fitness--;
                                        contraintes++;
                                    }

                                    if (equipeVisiteur.Jour != null && equipeVisiteur.Jour == autresEquipeVisiteur.Jour)
                                    {
                                        fitness--;
                                        contraintes++;
                                    }

                                }
                            }
                        }
                    }
                }
            }

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
                fitnesses.Add(Fitness(new List<List<Inscription>> { individu }, repartitionPoules).Item1);
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
        public static void CreerMatchs(List<List<Inscription>> resultatPoules, DateTime debutPeriodeMatchs, DateTime finPeriodeMatchs, DateTime debutPeriodeOff, DateTime finPeriodeOff)
        {
            TimeSpan matchDuration = new TimeSpan(2, 00, 0);

            List<RepartitionPoule> repartitionPoules = Globals.ConvertToList<RepartitionPoule>(SQL.GetAll_RepartitionPoule());
            List<Creneau> creneaux = Globals.ConvertToList<Creneau>(SQL.GetAll_CreneauBySaison(2022, false));

            List<int> competitionWeeks = new List<int>();
            for (DateTime date = debutPeriodeMatchs; date <= finPeriodeMatchs; date = date.AddDays(1))
            {
                if (date < debutPeriodeOff || date > finPeriodeOff)
                {
                    (int weekNumber, int weekYear) = getWeekNumber(date);

                    if (!competitionWeeks.Contains(weekNumber))
                    {
                        competitionWeeks.Add(weekNumber);
                    }
                }
            }

            int pouleIndex = 0;
            foreach (List<Inscription> poule in resultatPoules)
            {
                pouleIndex++;
                int pouleSize = poule.Count;
                List<RepartitionPoule> repartitionFiltree = repartitionPoules.Where(r => r.Poule == pouleSize && r.Tour == "A").ToList();

                Console.WriteLine("Poule {0} :", pouleIndex);

                List<DateTime> scheduledMatches = new List<DateTime>();

                for (int i = 0; i < competitionWeeks.Count - 1; i++)
                {
                    int currentWeek = competitionWeeks[i];

                    Console.WriteLine("  Semaine {0} :", i + 1);
                    List<RepartitionPoule> matchsSemaine = repartitionFiltree.Where(r => Int32.Parse(r.Rang) == (i + 1)).ToList();

                    foreach (RepartitionPoule match in matchsSemaine)
                    {
                        int currentYear = GetYearByWeek(currentWeek, debutPeriodeMatchs, finPeriodeMatchs);

                        // Obtenir les équipes locales et visiteuses
                        Inscription locaux = poule[Char.ToUpper(match.Locaux[0]) - 'A'];
                        Inscription visiteurs = poule[Char.ToUpper(match.Visiteur[0]) - 'A'];

                        // Trouver le créneau horaire de l'équipe à domicile
                        Creneau homeTeamSlot = creneaux.FirstOrDefault(c => c.EquipeCode == locaux.CodeEquipe);

                        if (homeTeamSlot != null)
                        {
                            DateTime targetDate = GetFirstDateOfWeek(currentYear, currentWeek, homeTeamSlot.CodeJourCreneaux);
                            DateTime slotDateTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, DateTime.ParseExact(homeTeamSlot.Horaire, "HH:mm", CultureInfo.InvariantCulture).Hour, DateTime.ParseExact(homeTeamSlot.Horaire, "HH:mm", CultureInfo.InvariantCulture).Minute, 0);

                            // Vérifier si la date trouvée est dans la période off
                            while (slotDateTime >= debutPeriodeOff && slotDateTime <= finPeriodeOff)
                            {
                                slotDateTime = slotDateTime.AddDays(1);
                            }

                            Console.WriteLine("    Match {0} vs {1} le {2:dd/MM/yyyy} à {3:HH:mm}", locaux.CodeEquipe, visiteurs.CodeEquipe, slotDateTime, slotDateTime);
                            scheduledMatches.Add(slotDateTime);
                        }
                        else
                        {
                            Console.WriteLine("    Aucun créneau disponible pour le match {0} vs {1}", locaux.CodeEquipe, visiteurs.CodeEquipe);
                        }
                    }
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
            for (DateTime date = startDate; date <= endDate; date = date.AddDays(1))
            {
                (int currentWeekNumber, int currentYear) = getWeekNumber(date);
                if (currentWeekNumber == weekNumber)
                {
                    return currentYear;
                }
            }
            throw new ArgumentException("La semaine donnée n'est pas dans la période spécifiée");
        }

    }
}
