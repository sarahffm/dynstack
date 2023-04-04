using DynStacking.HotStorage.DataModel;
using Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;

namespace csharp.HS_Genetic
{
    public class Block
    {
        public Block(int id, bool ready, TimeStamp due)
        {
            Id = id;
            Ready = ready;
            Due = due;
        }
        public int Id { get; }
        public bool Ready { get; }
        public TimeStamp Due { get; }
    }

    public class Stack
    {
        public int Id { get; }
        public int MaxHeight { get; }
        public Stack<Block> Blocks { get; }

        public Stack(DynStacking.HotStorage.DataModel.Stack stack)
        {
            Id = stack.Id;
            MaxHeight = stack.MaxHeight;
            Blocks = new Stack<Block>(stack.BottomToTop.Select(b => new Block(b.Id, b.Ready, b.Due)));
        }

        public Stack(Handover stack)
        {
            Id = stack.Id;
            MaxHeight = 1;
            Blocks = new Stack<Block>();
            if (stack.Block != null)
                Blocks.Push(new Block(stack.Block.Id, stack.Block.Ready, stack.Block.Due));
        }

        public Stack(Stack other)
        {
            Id = other.Id;
            MaxHeight = other.MaxHeight;
            Blocks = new Stack<Block>(other.Blocks.Reverse());
        }

        public Block Top()
        {
            return Blocks.Count > 0 ? Blocks.Peek() : null;
        }

        public bool IsSorted => Blocks.IsSorted();

        public bool ContainsReady => Blocks.Any(block => block.Ready);
        public bool IsEmpty => Blocks.Count == 0;
        public int Count => Blocks.Count;
        public int BlocksAboveReady()
        {
            if (Blocks.Any(block => block.Ready))
            {
                int blocksOverReady = 0;
                foreach (var block in Blocks.Reverse())
                {
                    if (block.Ready)
                        blocksOverReady = 0;
                    else
                        blocksOverReady++;
                }

                return blocksOverReady;
            }
            else
            {
                return 0;
            }
        }
        public bool ContainsDueBelow(TimeStamp due)
        {
            return Blocks.Any(block => block.Due.MilliSeconds < due.MilliSeconds);
        }
    }

    public class State
    {
        public List<CraneMove> Moves { get; }
        private Stack Production { get; }
        private List<Stack> Buffers { get; }
        private Stack Handover { get; }
        private long WorldStep { get; set; }


        // Define the length of the chromosome (i.e., the number of moves in the sequence)
        private const int ChromosomeLength = 10;

        // Define the size of the population
        private const int PopulationSize = 100;

        // Define the mutation rate (i.e., the probability of a symbol being mutated)
        private const double MutationRate = 0.1;

        private const int NumGenerations = 100;


        public State(World world)
        {
            Moves = new List<CraneMove>();
            Production = new Stack(world.Production);
            Handover = new Stack(world.Handover);
            Buffers = new List<Stack>();
            Buffers.AddRange(world.Buffers.Select(buf => new Stack(buf)));
            WorldStep = world.Now.MilliSeconds;
        }

        public State(State other)
        {
            Moves = other.Moves.ToList();
            Handover = new Stack(other.Handover);
            Production = new Stack(other.Production);
            Buffers = new List<Stack>();
            Buffers.AddRange(other.Buffers.Select(buf => new Stack(buf)));
        }

        // Define a static random number generator
        static Random Random = new Random();

        private List<string> InitializePopulation(List<CraneMove> possibleMoves)
        {
            // TODO: determine how to generate the first sequence
            // - ID ranges of the stacks
            // - valid and invalid moves
            // --> test mit GetAllPossibleMoves von Beam Search

            var population = List<CraneMove>();
            var remainingMoves = possibleMoves;
            for (int step = 0; i < ChromosomeLength; i++)
            {
                // pick a random move, add it to the population and remove it from the pool of options
                var randomIndex = Random.Next(remainingMoves.Count);
                population.Add(remainingMoves[randomIndex]);
                remainingMoves.RemoveAt(randomIndex);
            }

            // turn list of moves into string
            var populationString = ConvertMovesToString(population);

            // var population = Enumerable.Range(0, PopulationSize)
            //     .Select(_ => string.Join(",", Enumerable.Range(0, ChromosomeLength)
            //         .Select(__ => $"s{Random.Next(1, 11)}-d{Random.Next(1, 11)}")))
            //     .ToList();

            return populationString;
        }

        private String ConvertMovesToString(List<CraneMove> moves)
        {
            result = new StringBuilder();
            foreach (CraneMove move in moves)
            {
                result.Append($"s{move.SourceId}-d{move.TargetId},");
            }
            Console.WriteLine("Moves to string result: "+ result);
            return result;
        }

        public SearchSolution()
        {
            // Generate an initial population of random chromosomes
            // TODO: test difference of optimized True vs False
            List<string> population = InitializePopulation(GetAllPossibleMoves());

            // Evolve the population through multiple generations
            for (int generation = 0; generation < NumGenerations; generation++)
            {
                // Evaluate the fitness of each chromosome in the population
                List<double> fitnessScores = population.Select(
                    chromosome => Fitness(chromosome)).ToList();

                // Select the fittest chromosomes to become parents of the next generation
                List<string> parents = Enumerable.Range(0, (int)(PopulationSize * 0.2))
                    .Select(_ => population[fitnessScores.IndexOf(fitnessScores.Max())])
                    .ToList();

                // Generate the next generation by performing crossover and mutation operations on the parents
                List<string> children = Breed(parents);
                children = children.Select(child => Random.NextDouble() < MUTATION_RATE ? Mutate(child) : child).ToList();

                // Combine the parents and children to form the next generation
                population = parents.Concat(children).ToList();

                // Print the fitness of the fittest chromosome in each generation
                Console.WriteLine($"Generation {generation}: {fitnessScores.Max()}");
            }
        }

        // Define the fitness function
        static double Fitness(string chromosome)
        {
            // Evaluate the fitness of the chromosome based on the dynamic stacking problem
            // This could involve simulating the robot's behavior and measuring how well it follows the rules
            // Return a fitness score based on the quality of the solution
            return 0;
        }

        private List<string> Breed(List<String> parents)
        {
            var children = List<string>;

            for (int i = 0; i < PopulationSize - parents.Count; i++)
            {
                int random1, random2;
                while (random1 == random2) 
                {
                    random1 = Random.Next(parents.Count);
                    random2 = Random.Next(parents.Count);
                }
                children.Add(Crossover(parents[random1], parents[random2]));
            }

            // children = Enumerable.Range(0, PopulationSize - parents.Count)
            //         .Select(_ => Crossover(parents[Random.Next(parents.Count)], parents[Random.Next(parents.Count)]))
            //         .ToList();

            return children;
        }

        // Define the crossover function
        static string Crossover(string parent1, string parent2)
        {
            // Perform a crossover operation between the two parents to generate a new child
            // This could involve selecting a random point in the chromosome and swapping the sequences before and after that point
            // Return the new child chromosome
            return "";
        }

        // Define the mutation function
        static string Mutate(string chromosome)
        {
            // Mutate the chromosome by randomly changing some symbols
            // This could involve iterating through the symbols in the chromosome and flipping each one with a probability defined by the mutation rate
            // Return the mutated chromosome
            return "";
        }

        




        public double CalculateReward(int handovers, long leftoverDueTime)
        {
            double reward = 0;
            List<int> currentBuffer = new List<int>();
            foreach (var buffer in Buffers)
            {
                currentBuffer.Add(buffer.Blocks.Count);
                var highestReadyIndex = -1;
                var distToTop = 0;
                var bufferList = buffer.Blocks.ToArray();
                for (int i = 0; i < buffer.Blocks.Count; i++)
                {
                    var block = bufferList[i];
                    if (block.Ready)
                    {
                        highestReadyIndex = i;
                        distToTop = 0;
                    }
                    else
                    {
                        distToTop++;
                    }
                }
                if (highestReadyIndex != -1)
                    reward -= 10 * distToTop;
            }

            var stdDev = currentBuffer.StdDev();
            var maxStdDev = new List<int> { 0, Buffers.First().MaxHeight }.StdDev();
            var bufferReward = (1 - (stdDev / maxStdDev)) * 10;
            reward += bufferReward;

            reward += 10 * (Production.MaxHeight - Production.Blocks.Count);

            if (Handover.Blocks.Count > 0)
                reward += 500 + Handover.Blocks.First().Due.MilliSeconds;

            reward += 500 * handovers + leftoverDueTime;

            return reward;
        }

        public double CalculateMoveReward(CraneMove move)
        {
            double reward = 0;
            var oldState = new State(this);
            var newState = oldState.Apply(move);

            if (move.TargetId == Handover.Id)
            {
                reward += 500;
            }
            else
            {
                if (move.SourceId == Production.Id)
                {
                    reward += 15;
                    var productionFill = oldState.Production.Count / (double)oldState.Production.MaxHeight;

                    if (productionFill >= 1)
                        reward += 600;
                    else if (productionFill >= 0.75)
                        reward += 150;
                    else if (productionFill > 0.25)
                        reward += 25;

                    if (oldState.Buffers.First(stack => stack.Id == move.TargetId).ContainsReady)
                    {
                        if (oldState.Buffers.First(stack => stack.Id == move.TargetId).Top().Ready)
                            reward -= 100;
                        else
                            reward -= 25;
                    }
                }
                else
                {
                    var oldSourceBuffer = oldState.Buffers.First(stack => stack.Id == move.SourceId);
                    var oldTargetBuffer = oldState.Buffers.First(stack => stack.Id == move.TargetId);
                    var newSourceBuffer = newState.Buffers.First(stack => stack.Id == move.SourceId);
                    var newTargetBuffer = newState.Buffers.First(stack => stack.Id == move.TargetId);

                    if (!oldTargetBuffer.ContainsReady || oldTargetBuffer.IsEmpty)
                        reward += 20;
                    else if (oldTargetBuffer.ContainsReady)
                    {
                        if (oldTargetBuffer.Top().Ready)
                            reward -= 100;
                        else
                            reward -= 30;
                    }

                    if (oldTargetBuffer.ContainsDueBelow(new TimeStamp() { MilliSeconds = 5 * 60000 }))
                        reward -= 10;

                    if (oldTargetBuffer.BlocksAboveReady() < newTargetBuffer.BlocksAboveReady())
                    {
                        reward -= 20;
                    }

                    if (oldSourceBuffer.BlocksAboveReady() > newSourceBuffer.BlocksAboveReady())
                    {
                        reward += 40;
                    }

                    if (oldSourceBuffer.ContainsReady)
                    {
                        reward += (oldSourceBuffer.MaxHeight - oldSourceBuffer.BlocksAboveReady()) * 10;
                    }

                    if (newSourceBuffer.Top() != null && newSourceBuffer.Top().Ready)
                    {
                        reward += 100;
                    }
                }
            }
            return reward;
        }

        public Tuple<List<CraneMove>, double> GetBestMoves(List<CraneMove> moves, int depth, int handovers, long leftoverDueTime)
        {
            if (depth == 0)
            {
                return new Tuple<List<CraneMove>, double>(moves, this.CalculateReward(handovers, leftoverDueTime));
            }
            else
            {
                double bestRating = int.MinValue;
                List<CraneMove> bestMoves = new List<CraneMove>();
                System.Diagnostics.Debugger.Launch();
                foreach (var move in this.GetAllPossibleMoves())
                {
                    if (!moves.Any(m => move.BlockId == m.BlockId && move.SourceId == m.SourceId && move.TargetId == m.TargetId))
                    {
                        var newState = new State(this.Apply(move));
                        moves.Add(move);
                        Tuple<List<CraneMove>, double> newMoves = null;
                        if (move.TargetId == Handover.Id)
                        {
                            var block = FindBlock(move.BlockId);
                            newMoves = newState.GetBestMoves(moves, depth - 1, handovers + 1, leftoverDueTime + block.Due.MilliSeconds);
                        }
                        else
                            newMoves = newState.GetBestMoves(moves, depth - 1, handovers, leftoverDueTime);

                        if (bestMoves == null || bestRating < newMoves.Item2)
                        {
                            bestRating = newMoves.Item2;
                            bestMoves = new List<CraneMove>(newMoves.Item1);
                            if (newMoves.Item2 > 1000)
                                break;
                        }
                        moves.Remove(move);
                    }
                }
                return new Tuple<List<CraneMove>, double>(bestMoves, bestRating);
            }
        }

        public Block FindBlock(int id)
        {
            foreach (var buffer in Buffers)
            {
                foreach (var block in buffer.Blocks)
                {
                    if (block.Id == id)
                        return block;
                }
            }
            return null;
        }

        public Tuple<List<CraneMove>, double> GetBestMovesBeam(List<CraneMove> x, int depth, int width)
        {
            var bestMoves = new Stack<Tuple<CraneMove, double, int>>(ExpandMoveState(0));
            // reduce depth because this is the first move
            depth--;
            var states = new State[width];
            for (int i = 0; i < width; i++)
            {
                states[i] = new State(this);
            }

            while (depth > 0)
            {
                if (bestMoves.Count <= states.Length)
                {
                    for (int i = 0; bestMoves.Count > 0; i++)
                    {
                        states[i] = states[i].Apply(bestMoves.Pop().Item1);
                    }
                }

                var moves = new List<Tuple<CraneMove, double, int>>();

                for (int i = 0; i < states.Length; i++)
                {
                    moves.AddRange(states[i].ExpandMoveState(i));
                }

                moves = moves.OrderByDescending(item => item.Item2).Take(width).ToList();

                var newStates = new State[width];

                for (int i = 0; i < moves.Count(); i++)
                {
                    var move = moves.ElementAt(i);
                    newStates[i] = states[move.Item3].Apply(move.Item1);
                }

                for (int i = 0; i < states.Length; i++)
                {
                    if (newStates[i] != null)
                        states[i] = new State(newStates[i]);
                }
                depth--;
            }
            double bestReward = 0;
            State bestState = null;

            for (int i = 0; i < states.Length; i++)
            {
                var reward = states[i].ExpandMoveState(1, 1).Count() > 0 ? states[i].ExpandMoveState(1, 1).First().Item2 : 0;
                if (reward > bestReward)
                {
                    bestReward = reward;
                    bestState = states[i];
                }
            }

            if (bestState == null)
                return new Tuple<List<CraneMove>, double>(new List<CraneMove>(), 0);
            else
                return new Tuple<List<CraneMove>, double>(bestState.Moves, bestState.CalculateReward(0, 0));
        }

        public IEnumerable<Tuple<CraneMove, double, int>> ExpandMoveState(int branch, int amount = 3)
        {
            var moves = GetAllPossibleMoves(false).OrderByDescending(move => CalculateMoveReward(move)).Take(amount);
            var ret = new List<Tuple<CraneMove, double, int>>();
            foreach (var move in moves)
            {
                ret.Add(new Tuple<CraneMove, double, int>(move, CalculateMoveReward(move), branch));
            }

            return ret;
        }

        public bool IsSolved => !Production.Blocks.Any() && !NotEmptyStacks.Any();
        IEnumerable<Stack> NotFullStacks => Buffers.Where(b => b.Blocks.Count < b.MaxHeight);
        IEnumerable<Stack> NotEmptyStacks => Buffers.Where(b => b.Blocks.Count > 0);
        IEnumerable<Stack> StacksWithReady => NotEmptyStacks.Where(b => b.Blocks.Any(block => block.Ready));
        bool HandoverReady => !Handover.Blocks.Any();

        public List<CraneMove> GetAllPossibleMoves(bool optimized = true)
        {
            var possible = new List<CraneMove>();
            if (IsSolved) return possible;

            if (Production.Blocks.Count > 0 && NotFullStacks.Any())
            {
                if (optimized)
                {
                    var target = NotFullStacks.First();
                    possible.Add(new CraneMove
                    {
                        SourceId = Production.Id,
                        TargetId = target.Id,
                        Sequence = 0,
                        BlockId = Production.Top().Id
                    });
                }
                else
                {
                    foreach (var stack in NotFullStacks)
                    {
                        possible.Add(new CraneMove
                        {
                            SourceId = Production.Id,
                            TargetId = stack.Id,
                            Sequence = 0,
                            BlockId = Production.Top().Id
                        });
                    }
                }
            }

            foreach (var srcStack in StacksWithReady)
            {
                if (srcStack.Top().Ready)
                {
                    possible.Add(new CraneMove
                    {
                        SourceId = srcStack.Id,
                        TargetId = Handover.Id,
                        Sequence = 0,
                        BlockId = srcStack.Top().Id
                    });
                    continue;
                }

                IEnumerable<Stack> targetStacks = null;
                if (optimized)
                {
                    targetStacks = NotFullStacks.Where(stack => stack.Id != srcStack.Id && !StacksWithReady.Contains(stack) && (stack.Top() != null ? !stack.Top().Ready : false));
                    if (targetStacks.Count() == 0)
                        targetStacks = NotFullStacks.Where(stack => stack.Id != srcStack.Id && (stack.Top() != null ? !stack.Top().Ready : false));
                }
                else
                {
                    targetStacks = NotFullStacks.Where(stack => stack.Id != srcStack.Id);
                }

                foreach (var tgtStack in targetStacks)
                {
                    possible.Add(new CraneMove
                    {
                        SourceId = srcStack.Id,
                        TargetId = tgtStack.Id,
                        Sequence = 0,
                        BlockId = srcStack.Top().Id
                    });
                }
            }

            return possible;
        }

        public Block RemoveBlock(int stackId)
        {
            if (stackId == Production.Id)
                return Production.Blocks.Pop();
            else
                return Buffers.First(b => b.Id == stackId).Blocks.Pop();
        }

        public void AddBlock(int stackId, Block block)
        {
            if (stackId != Handover.Id && stackId != Production.Id)
            {
                Buffers.First(b => b.Id == stackId).Blocks.Push(block);
            }
            else
            {
                // Production should never be a target
                // If handover is the target, pretend the Block dissappears immediatly
            }
        }

        public State Apply(CraneMove move)
        {
            var result = new State(this);
            var block = result.RemoveBlock(move.SourceId);
            result.AddBlock(move.TargetId, block);
            result.Moves.Add(move);
            return result;
        }
    }

    public static class Extensions
    {

        public static bool IsSorted(this Stack<Block> stack)
        {
            // is technically wrong but otherwise empty stacks are avoided
            if (stack.Count == 0)
                return false;
            else if (stack.Count < 2)
            {
                return true;
            }

            var aux = new Stack<Block>();
            aux.Push(stack.Pop());

            while (stack.Count > 0 && stack.Peek().Due.MilliSeconds > aux.Peek().Due.MilliSeconds)
            {
                aux.Push(stack.Pop());
            }

            var sorted = stack.Count == 0;

            while (aux.Count > 0)
                stack.Push(aux.Pop());

            return sorted;
        }

        public static double StdDev(this IEnumerable<int> values)
        {
            double ret = 0;
            int count = values.Count();
            if (count > 1)
            {
                double avg = values.Average();
                double sum = values.Sum(i => (i - avg) * (i - avg));

                ret = Math.Sqrt(sum / count);
            }

            return ret;
        }

        public static string FormatOutput(this List<CraneMove> list)
        {
            string ret = "[\n";
            foreach (var move in list)
            {
                ret += $"\t{move.FormatOutput()}\n";
            }
            return ret + "]";
        }

        public static string FormatOutput(this CraneMove move)
        {
            return $"Move Block {move.BlockId} from {move.SourceId} to {move.TargetId}";
        }

        public static string FormatOutput(this List<DynStacking.HotStorage.DataModel.Block> blocks)
        {
            string ret = "{";

            foreach (var block in blocks)
            {
                ret += $"{block.FormatOutput()}, ";
            }

            return ret + "}";
        }

        public static string FormatOutput(this DynStacking.HotStorage.DataModel.Block block)
        {
            if (block == null)
                return "";
            return $"B{block.Id}: {(block.Ready ? "R" : "N")}";
        }

        public static string FormatOutput(this World world)
        {
            string ret = "World {\n";
            ret += $"\tProduction: {world.Production.BottomToTop.ToList().FormatOutput()}\n";
            foreach (var buffer in world.Buffers)
            {
                ret += $"\tBuffer {buffer.Id} ({buffer.BottomToTop.Count}/{buffer.MaxHeight}): {buffer.BottomToTop.ToList().FormatOutput()}\n";
            }
            ret += $"\tHandover: {world.Handover.Block.FormatOutput()}\n";

            return ret + "}";
        }

        public static IEnumerable<CraneMove> ConsolidateMoves(this List<CraneMove> moves)
        {
            List<CraneMove> cleanList = new List<CraneMove>();

            foreach (var move in moves)
            {
                var similarMoves = cleanList.Where(m => m.BlockId == move.BlockId && m.TargetId == move.SourceId);
                similarMoves.ToList().ForEach(m => m.TargetId = move.SourceId);
                if (similarMoves.Count() == 0)
                    cleanList.Add(move);
            }

            return cleanList;
        }
    }
}
