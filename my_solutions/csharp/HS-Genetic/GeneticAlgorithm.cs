using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DynStacking.HotStorage.DataModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace csharp.HS_Genetic {

    class Individual
    {

        // constants for the fitness function:
        private const double ArrivalWeight = 0.4;
        private const double HandoverWeight = 0.4;
        private const double OverReadyWeight = 0.2;

        // Define a static random number generator
        static Random Random = new Random();

        public double Fitness { get; set; }
        public string SolutionString { get; set; }
        public List<CraneMove> SolutionAsMoves { get; set; }
        public State State { get; set; }
        public int NumOfHandovers { get; set; }

        public Individual(State state, String solutionString, List<CraneMove> solutionMove, int numHandovers)
        {
            State = state;
            SolutionString = solutionString;
            SolutionAsMoves = solutionMove;
            NumOfHandovers = numHandovers;

            // initially set to 0
            Fitness = 0; 
        }

        // simulationState is the initial state
        public Individual(State initialState)
        {
            State = new State(initialState);
            SolutionAsMoves = new List<CraneMove>();
            SolutionString = "";
            NumOfHandovers = 0;
            Fitness = 0;
        }

        public Individual()
        {
            State = null;
            SolutionAsMoves = new List<CraneMove>();
            SolutionString = "";
            NumOfHandovers = 0;
            Fitness = 0;
        }

        public int CountNumOfHandovers()
        {
            int count = 0;
            // for (int i = 0; i < SolutionAsMoves.Count; i++)
            foreach (var move in SolutionAsMoves)
            {
                if (move.TargetId == State.Handover.Id)
                {
                    count++;
                }
            }
            return count;
        }

        public double CalculateFitness(int chromosomeLength)
        {

            // goal x% filled:
            var productionFill = State.Production.Count / (double) State.Production.MaxHeight;

            // goal minimize:
            var bufferFill = 0;
            var numOverReady = 0;

            // goal maximize:
            // - numHandovers

            for (int i = 0; i < State.Buffers.Count; i++)
            {
                bufferFill += State.Buffers[i].Blocks.Count;
                numOverReady += State.Buffers[i].BlocksAboveReady();
            }

            // 1: rate arrival
            double arrivalScore = 0;
            var arrivalUsed = productionFill / (double) State.Production.MaxHeight;
            if (arrivalUsed > 0.8) { arrivalScore = 0.8; }
            else if (arrivalUsed > 0.7) { arrivalScore = 1; }
            else if (arrivalUsed > 0.3) { arrivalScore = 0.2; }

            // 2: rate number of handovers
            double handoverScore = NumOfHandovers / (double) chromosomeLength;

            // 3: rate number of blocks over readies
            double overReadyScore = 1 - (numOverReady / (double) bufferFill);
            
            Fitness = ArrivalWeight * arrivalScore + HandoverWeight * handoverScore + OverReadyWeight * overReadyScore;

            return Fitness;
        }
        
        public List<CraneMove> FillMoves(int n)
        {
            // get all possible moves, pick one, apply & repeat
            for (int i = 0; i < n; i++)
            {
                var possibleMoves = State.GetAllPossibleMoves(false);
                var random = Random.Next(possibleMoves.Count - 1);
                var newMove = possibleMoves[random];

                State.Apply(newMove);

                SolutionAsMoves.Add(newMove);
                var temp = new StringBuilder(SolutionString);
                SolutionString = temp.Append($"s{newMove.SourceId}-d{newMove.TargetId}").ToString();
            }
            return SolutionAsMoves;
        }
    
        public List<CraneMove> Mutate(int chromosomeLength, int n = 1)
        {
            for (int k = 0; k < n; k++)
            {
                // choose a random move
                var moveIndex = Random.Next(chromosomeLength);
                
                // remove moves after the index
                // index = zero-based starting index 
                // count = number of elements to remove
                SolutionAsMoves.RemoveRange(moveIndex, SolutionAsMoves.Count - moveIndex);

                // create a new sequence of moves to fill rest
                var rest = chromosomeLength - SolutionAsMoves.Count;
                FillMoves(rest);
            }
            
            // SolutionString ?
            SolutionString = State.ConvertMovesToString(SolutionAsMoves);

            return SolutionAsMoves;
        }

    }

    class GeneticAlgorithm
    {
        private const int ChromosomeLength = 6; // Define the length of the chromosome (i.e., the number of moves in the sequence)
        private const int PopulationSize = 80;
        private const double MutationRate = 0.1; // Define the mutation rate (i.e., the probability of a symbol being mutated)
        private const int NumGenerations = 100;

        // // constants for the fitness function:
        // private const double ArrivalWeight = 0.4;
        // private const double HandoverWeight = 0.4;
        // private const double OverReadyWeight = 0.2;

        // Define a static random number generator
        static Random Random = new Random();



        public List<CraneMove> SearchSolution(State simulationState)
        {
            // Generate an initial population of random chromosomes
            // old code:
            // List<string> population = InitializePopulation(simulationState);
            // new:
            List<Individual> population = InitializePopulation(simulationState);

            // Evolve the population through multiple generations
            for (int generation = 0; generation < NumGenerations; generation++)
            {

                // Evaluate the fitness of each chromosome in the population
                // old code:
                // List<double> fitnessScores = population.Select(
                //     chromosome => Fitness(chromosome)).ToList();

                //new:
                var fitnessScores = new List<double>();
                foreach (var individual in population)
                {
                    individual.CalculateFitness(ChromosomeLength);
                    fitnessScores.Add(individual.Fitness);
                }


                // Select the fittest chromosomes to become parents of the next generation
                // old code:
                // List<string> parents = Enumerable.Range(0, (int)(PopulationSize * 0.2))
                //     .Select(_ => population[fitnessScores.IndexOf(fitnessScores.Max())])
                //     .ToList();
                // new:
                List<Individual> parents = Enumerable.Range(0, (int)(PopulationSize * 0.2))
                    .Select(_ => population[fitnessScores.IndexOf(fitnessScores.Max())])
                    .ToList();



                // Generate the next generation by performing crossover and mutation operations on the parents
                // old:
                // List<string> children = Breed(parents);
                // new:
                List<Individual> children = Breed(parents, simulationState);

                // Combine the parents and children to form the next generation
                population = parents.Concat(children).ToList();

                // Print the fitness of the fittest chromosome in each generation
                Console.WriteLine($"Generation {generation}: {fitnessScores.Max()}");
            }

            // Determine and print the final solution
            Individual bestSolution = population.OrderBy(c => c.Fitness).First();
            Console.WriteLine("Final solution: " + bestSolution.SolutionString);
            foreach(var move in bestSolution.SolutionAsMoves)
            {
                Console.WriteLine($"Move Block {move.BlockId} from {move.SourceId} to {move.TargetId}");
            }

            // // convert string to list of crane moves
            // List<CraneMove> solution = StringToCraneMoves(bestChromosome);

            return bestSolution.SolutionAsMoves;
        }

        private List<Individual> InitializePopulation(State simulationState)
        {
            var population = new List<Individual>();

            State currentState = new State(simulationState);
            List<CraneMove> possibleMoves = currentState.GetAllPossibleMoves(false);
            var numHandovers = 0;

            // generate PopulationSize-many individuals
            for (int k = 0; k < PopulationSize; k++)
            {
                var moves = new List<CraneMove>();
                
                for (int i = 0; i < ChromosomeLength; i++)
                {   
                    // pick a random move, add it to the individual
                    var randomIndex = Random.Next(possibleMoves.Count);
                    moves.Add(possibleMoves[randomIndex]);

                    // apply move to update state
                    currentState = currentState.Apply(moves.Last());
                    // track if handover
                    if (moves.Last().TargetId == currentState.Handover.Id) { numHandovers++; }

                    // get new moves
                    possibleMoves = currentState.GetAllPossibleMoves(false);
                }

                // create Individual object to store state and solution
                var individual = new Individual {
                    State = currentState,
                    SolutionString = currentState.ConvertMovesToString(moves),
                    SolutionAsMoves = moves,
                    NumOfHandovers = numHandovers
                };

                // add individual to population
                population.Add(individual);
            }

            return population;
        }

        private List<Individual> Breed(List<Individual> parents, State simulationState)
        {
            var children = new List<Individual>();

            for (int i = 0; i < PopulationSize - parents.Count; i++)
            {
                int random1 = -1;
                int random2 = -1;
                while (random1 == random2) 
                {
                    random1 = Random.Next(parents.Count);
                    random2 = Random.Next(parents.Count);
                }
                children.Add(Crossover(parents[random1], parents[random2], simulationState));
            }

            return children;
        }

        private Individual Crossover(Individual parent1, Individual parent2, State simulationState)
        {
            // Perform a crossover operation between the two parents to generate a new child
            // This could involve selecting a random point in the chromosome and swapping the sequences before and after that point
            // Return the new child chromosome

            var child = new Individual(simulationState);
            var isSuccess = false;

            for (int i = 0; i < ChromosomeLength; i++)
            {
                // choose a parent
                Individual chosenParent;
                if (Random.NextDouble() < 0.5) { chosenParent = parent1; }
                else { chosenParent = parent2; }

                // try to apply the move
                Tuple<Boolean, CraneMove, State> result = child.State.TryApplyMove(chosenParent.SolutionAsMoves[i]);
                isSuccess = result.Item1;
                var move = result.Item2;
                var updatedState = result.Item3;

                // add to child if was success
                if (isSuccess)
                {
                    child.State = updatedState;
                    child.SolutionAsMoves.Add(move);

                    var temp = new StringBuilder(child.SolutionString);
                    child.SolutionString = temp.Append($"s{move.SourceId}-d{move.TargetId}").ToString();
                }
            }

            // fill the rest of the moves that couldn't be copied from the parents
            var numRemaining = ChromosomeLength - child.SolutionAsMoves.Count;
            child.FillMoves(numRemaining);
            // Console.WriteLine("Count after FillMoves (expected ChromosomeLength): " + child.SolutionAsMoves.Count);


            // mutate
            if (Random.NextDouble() < MutationRate)
            {
                // needs chromosome length to know how many moves are required
                child.Mutate(ChromosomeLength);
            }

            return child;
        }


        // integrated in Individual, might not need this anymore
        // static double Fitness(string chromosome)
        // {
        //     // Evaluate the fitness of the chromosome based on the dynamic stacking problem
        //     // This could involve simulating the robot's behavior and measuring how well it follows the rules
        //     // Return a fitness score based on the quality of the solution
        //     return 0;
        // }

    }
}