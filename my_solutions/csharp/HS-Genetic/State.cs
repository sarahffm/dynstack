using System.IO.IsolatedStorage;
using System.Linq.Expressions;
using DynStacking.HotStorage.DataModel;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

// TODO:
// - doesn't track state correctly
// - keeps taking the same block from the same stack although has already scheduled this move ...
// - check what to do when simulation still has moves in schedule


namespace csharp.HS_Genetic
{
    public interface IHasId
    {
        int Id { get; }
    }
    
    public class Block : IHasId
    {
        public Block(int id, bool ready, TimeStamp due)
        {
            Id = id;
            Ready = ready;
            Due = due;
            DueMs = due.MilliSeconds;
            Overdue = DueMs > 0 ? true : false;
        }
        public int Id { get; }
        public bool Ready { get; }
        public TimeStamp Due { get; }
        public long DueMs { get; set; }
        public bool Overdue { get; set; }
    }

    public class Stack : IHasId
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

        public double RateDueOrder()
        {
            // TODO: cut out overdue blocks

            double score = 0;
            List<Block> blocks = Blocks.Reverse().ToList();
            var n = Blocks.Count;
            
            if (n == 0) { score = 1; }

            // reference list that contains the "perfect order"
            List<long> expected = blocks.OrderByDescending(block => block.DueMs).Select(block => block.DueMs).ToList();

            // DEBUG
            // Console.WriteLine($"Stack: {Id}");
            // Console.WriteLine("Expected order: {0}", string.Join(", ", expected));
            // List<long> actual = blocks.Select(block => block.DueMs).ToList();
            // Console.WriteLine("Actual order: {0}", string.Join(", ", actual));

            var temp = new List<double>();
            for (int i = 0; i < n; i++)
            {
                double deviation = (blocks[i].DueMs -  expected[i]) / (double) 10000;
                deviation = Math.Abs(deviation);
                // gaussian function with x0 = 0, sigma = n ?
                var sigma = 4;
                double x = Math.Exp(-deviation * deviation / (2.0 * sigma));

                // DEBUG
                // Console.WriteLine("deviation: " + deviation + " n: " + n);
                // Console.WriteLine("x: " + x);

                temp.Add(x);
            }

            score = temp.Sum();

            // Normalize by the number of blocks
            score = score / n;
            // Console.WriteLine("Stack's score: " + score + "\n\n");

            return score;
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
            int blocksOverReady = 0;

            foreach (Block block in Blocks)
            {
                // Console.WriteLine("blocksaboveready block.Ready: " + block.Ready);
                if (!block.Ready) { 
                    // Console.WriteLine("blocksOverReady: " + blocksOverReady);
                    blocksOverReady++; }
                else { 
                    // Console.WriteLine("blocksOverReady: " + blocksOverReady);
                    return blocksOverReady; }
            }

            return 0;
            
            // if (Blocks.Any(block => block.Ready))
            // {
            //     // int blocksOverReady = 0;
            //     // foreach (var block in Blocks.Reverse())
            //     // {
            //     //     if (block.Ready)
            //     //         blocksOverReady = 0;
            //     //     else
            //     //         blocksOverReady++;
            //     // }
            //     return blocksOverReady;
            // }
            // else
            // {
            //     return 0;
            // }
        }
        public bool ContainsDueBelow(TimeStamp due)
        {
            return Blocks.Any(block => block.Due.MilliSeconds < due.MilliSeconds);
        }
    }

    public class ScheduleResult
    {
        public int NumInvalidMoves { get; set; }
        public int NumHandovers { get; set; }
        public int NumCoveredReadies { get; set; }
        public int NumUncoveredReadies { get; set; }
    }

    public class State
    {
        public List<CraneMove> Moves { get; }
        public Stack Production { get; }
        public List<Stack> Buffers { get; }
        public Stack Handover { get; }
        private long WorldStep { get; set; }


        // Define the length of the chromosome (i.e., the number of moves in the sequence)
        private const int ChromosomeLength = 6;

        private const int PopulationSize = 80;

        // Define the mutation rate (i.e., the probability of a symbol being mutated)
        private const double MutationRate = 0.1;

        private const int NumGenerations = 100;

        // constants for the fitness function:
        private const double ArrivalWeight = 0.3;
        private const double HandoverWeight = 0.55;
        private const double OverReadyWeight = 0.15;


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

        // use function to test code
        public void Test()
        {
            // string input = "s1-d4,s2-d4,s3-d4,s0-d1,s3-d4,s3-d4,s3-d4,s2-d4,s1-d4,s1-d4";
            string input = "s2-d1,s1-d3,s0-d1,s3-d1,s3-d4,s3-d4,s3-d4,s1-d2,s0-d1,s2-d3,";
            List<CraneMove> res = StringToCraneMoves(input);
            Console.WriteLine(res);
            System.Threading.Thread.Sleep(10000);
        }

        public List<CraneMove> SearchSolution()
        {
            // Generate an initial population of random chromosomes
            List<string> population = InitializePopulation();

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
                // children = children.Select(child => Random.NextDouble() < MUTATION_RATE ? Mutate(child) : child).ToList();

                // Combine the parents and children to form the next generation
                population = parents.Concat(children).ToList();

                // Print the fitness of the fittest chromosome in each generation
                Console.WriteLine($"Generation {generation}: {fitnessScores.Max()}");
            }

            // Determine and print the final solution
            string bestChromosome = population.OrderBy(c => Fitness(c)).First();
            Console.WriteLine("Final solution: " + bestChromosome + "\n");

            // convert string to list of crane moves
            List<CraneMove> solution = StringToCraneMoves(bestChromosome);
            return solution;
        }

        // TODO: catch error if really no possible moves
        private List<string> InitializePopulation()
        {
            var population = new List<string>();

            State currentState = new State(this);
            List<CraneMove> possibleMoves = currentState.GetAllPossibleMoves(false);

            // Console.WriteLine("Possible Moves count: " + possibleMoves.Count);
            // Console.WriteLine("Possible Moves initial:");
            // foreach(var move in possibleMoves)
            // {
            //     Console.WriteLine(move.FormatOutput());
            // }

            // generate PopulationSize-many individuals
            for (int k = 0; k < PopulationSize; k++)
            {
                var individual = new List<CraneMove>();
                
                for (int i = 0; i < ChromosomeLength; i++)
                {   
                    // pick a random move, add it to the individual
                    var randomIndex = Random.Next(possibleMoves.Count);
                    individual.Add(possibleMoves[randomIndex]);

                    // apply move to update state
                    currentState = currentState.Apply(individual.Last());
                    possibleMoves = currentState.GetAllPossibleMoves(false);
                }

                // turn list of moves into string
                var individualString = ConvertMovesToString(individual);
                // add individual to population
                population.Add(individualString);
            }

            return population;
        }

        double Fitness(string chromosome)
        {
            // save the old/initial state
            var oldState = new State(this);

            // carry out schedule
            var moves = StringToCraneMoves(chromosome);
            var (newState, scheduleResult) = oldState.ApplyAndRate(moves);

            // goal x% filled:
            // var oldProductionFill = oldState.Production.Count / (double) oldState.Production.MaxHeight;
            var newProductionFill = newState.Production.Count / (double) newState.Production.MaxHeight;
            // goal minimize:
            // (- numInvalidMoves)
            // (- numReadyCovered)
            var newBufferFill = 0;
            var newNumOverReady = 0;
            // goal maximize:
            // - numHandovers


            // foreach(var move in moves)
            // {
            //     Console.WriteLine(move.FormatOutput());
            // }
            // Console.WriteLine("initial state:");
            // PrintBufferState(this);


            for (int i = 0; i < newState.Buffers.Count; i++)
            {
                newBufferFill += newState.Buffers[i].Blocks.Count;
                newNumOverReady += newState.Buffers[i].BlocksAboveReady();
            }

            // Console.WriteLine("numHandovers: " + scheduleResult.NumHandovers);
            // Console.WriteLine("bufferfill: " + newBufferFill);
            // Console.WriteLine("num over ready: " + newNumOverReady);
            // Console.WriteLine("arrival max: " + newState.Production.MaxHeight);

            // 1: rate arrival
            double arrivalScore = 0;
            var arrivalUsed = newProductionFill / (double) newState.Production.MaxHeight;
            if (arrivalUsed > 0.8) { arrivalScore = 0.8; }
            else if (arrivalUsed > 0.7) { arrivalScore = 1; }
            else if (arrivalUsed > 0.3) { arrivalScore = 0.2; }

            // 2: rate number of handovers
            double handoverScore = scheduleResult.NumHandovers / (double) ChromosomeLength;

            // 3: rate number of blocks over readies
            double overReadyScore = 1 - (newNumOverReady / (double) newBufferFill);
            
            double fitness = ArrivalWeight * arrivalScore + HandoverWeight * handoverScore + OverReadyWeight * overReadyScore;

            // Console.WriteLine("Scores:");
            // Console.WriteLine("arrival: " + arrivalScore);
            // Console.WriteLine("handover: " + handoverScore);
            // Console.WriteLine("overReady: " + overReadyScore);
            // Console.WriteLine("fitness: " + fitness);
            // Console.WriteLine("\n");

            return fitness;
        }

        private void PrintBufferState(State state)
        {
            Console.WriteLine("PrintBufferState:");
            foreach (var buffer in state.Buffers)
            {
                Console.WriteLine("buffer id: " + buffer.Id);
                foreach (Block block in buffer.Blocks.Reverse())
                {
                    Console.Write(block.Id + " R " + block.Ready + " | ");
                }
                Console.WriteLine("\n");
            }
        }

        private List<CraneMove> CopyCraneMoves(List<CraneMove> list)
        {
            var newList = new List<CraneMove>();
            foreach (CraneMove move in list)
            {
                newList.Add(new CraneMove() {
                    BlockId = move.BlockId,
                    SourceId = move.SourceId,
                    TargetId = move.TargetId,
                    Sequence = move.Sequence,
                    EmptyMove = move.EmptyMove
                });
            }
            return newList;
        }

        public string ConvertMovesToString(List<CraneMove> moves)
        {
            var result = new StringBuilder();

            foreach (CraneMove move in moves)
            {
                result.Append($"s{move.SourceId}-d{move.TargetId},");
            }

            var resultTrimmed = TrimCommas(result.ToString());
            
            return resultTrimmed;
        }

        private List<CraneMove> StringToCraneMoves(string str)
        {
            // example string:
            // s1-d4,s2-d4,s3-d4,s0-d1,s3-d4,s3-d4,s3-d4,s2-d4,s1-d4,s1-d4(,)

            str = TrimCommas(str);
            // Console.WriteLine("String to convert to moves: " + str);
            
            var result = new List<CraneMove>();

            var moves = str.Split(',');

            foreach(string move in moves)
            {
                // determine source & target stack
                var sourceId = ExtractId(move, "s");
                var targetId = ExtractId(move, "d");

                // determine blockId
                // IDEA: could maybe be optimized by creating one dictionary beforehand?
                var arrivalAsList = new List<Stack>();
                arrivalAsList.Add(Production);
                var source = FindById<Stack>(sourceId, Buffers, arrivalAsList);

                var blockId = source.Top().Id; // error here

                // create new move
                var craneMove = new CraneMove() {
                    BlockId = blockId,
                    SourceId = sourceId,
                    TargetId = targetId,
                };
                result.Add(craneMove);

            }
            return result;
        }

        private string TrimCommas(string str)
        {
            while (str.EndsWith(','))
            {
                str = str.TrimEnd(',');
            }
            return str;
        }

        private int ExtractId(string str, string mode)
        {
            string pattern = @"^s(\d+)-d(\d+)$";
            Match match = Regex.Match(str, pattern);
            int id = -1;

            if (match.Success) 
            {
                id = mode == "s" ? int.Parse(match.Groups[1].Value) : int.Parse(match.Groups[2].Value);
            }

            return id;
        }

        private List<string> Breed(List<string> parents)
        {
            var children = new List<string>();

            for (int i = 0; i < PopulationSize - parents.Count; i++)
            {
                int random1 = -1;
                int random2 = -1;
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
            //Console.WriteLine("Children after crossover & mutate: " + children);
            return children;
        }

        string Crossover(string parent1, string parent2)
        {
            // Perform a crossover operation between the two parents to generate a new child
            // This could involve selecting a random point in the chromosome and swapping the sequences before and after that point
            // Return the new child chromosome

            var childBuilder = new StringBuilder();
            var parent1Moves = parent1.Split(',');
            var parent2Moves = parent2.Split(',');

            for (int i = 0; i < ChromosomeLength; i++)
            {
                if (Random.NextDouble() < 0.5) 
                {
                    childBuilder.Append(parent1Moves[i]);
                } 
                else 
                {
                    childBuilder.Append(parent2Moves[i]);
                }
                childBuilder.Append(",");
            }

            string child = childBuilder.ToString();

            // mutate
            if (Random.NextDouble() < MutationRate)
            {
                child = Mutate(childBuilder.ToString());
            }

            return child;
        }

        string Mutate(string child, int n = 1)
        {
            var moves = StringToCraneMoves(child);

            for (int k = 0; k < n; k++)
            {
                // choose a random move
                var moveIndex = Random.Next(ChromosomeLength);
                // create a new sequence of moves from there
                moves = CreateNewMoves(moves, moveIndex);
            }

            // turn to string and return
            var result = ConvertMovesToString(moves);
            return result;
        }

        private List<CraneMove> CreateNewMoves(List<CraneMove> moves, int index)
        {
            // apply moves until index
            var newMoves = moves.Take(index).ToList();
            State newState = Apply(newMoves);

            // get all possible moves, pick one, apply & repeat
            while (newMoves.Count != ChromosomeLength)
            {
                var possibleMoves = GetAllPossibleMoves(false);
                var random = Random.Next(possibleMoves.Count - 1);
                var newMove = possibleMoves[random];
                newMoves.Add(newMove);
                newState = Apply(newMove);
            }
            return newMoves;
        }

        string Mutate2(string child, int n = 1)
        {
            // Mutate the chromosome by randomly changing some symbols
            // This could involve iterating through the symbols in the chromosome and flipping each one with a probability defined by the mutation rate
            // Return the mutated chromosome

            var childMoves = child.Split(',');
            var mutated = new StringBuilder();

            for (int k = 0; k < n; k++)
            {
                // choose a random move to mutate
                var moveIndex = Random.Next(ChromosomeLength);
                // create a new move
                childMoves[moveIndex] = CreateRandomMove();
            }

            foreach (string move in childMoves)
            {
                mutated.Append(move);
                mutated.Append(',');
            }

            return mutated.ToString();
        }

        private string CreateRandomMove()
        {
            // determine buffer range
            // incorporate arrival and handover too
            // - just randomly?

            int maxBufferId = Buffers.Last().Id;
            int arrivalId = 0;
            int handoverId = maxBufferId + 1;

            int sourceId = -1;
            int targetId = -1;
            while (sourceId == targetId)
            {
                // maxValue is exclusive
                sourceId = Random.Next(arrivalId, maxBufferId + 1);
                targetId = Random.Next(1, handoverId + 1);
            }
            
            var newMove = $"s{sourceId}-d{targetId}";
            return newMove;
        }

        public static T FindById<T>(int id, params IList<T>[] lists) where T : IHasId
        {
            foreach (var list in lists)
            {
                if (list is null) continue;
                foreach (var obj in list)
                {
                    if (obj.Id == id)
                    {
                        return obj;
                    }
                }
            }
            return default;
        }

        public State Apply(List<CraneMove> moves)
        {
            var newState = new State(this);

            foreach (CraneMove move in moves)
            {
                try
                {
                    var block = newState.RemoveBlock(move.SourceId);
                    newState.AddBlock(move.TargetId, block);
                    newState.Moves.Add(move);
                    // Console.WriteLine("Applied move successfully.");
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception caught in State Apply. Could not apply move." + e.Message);
                    continue;
                }
            }
            return newState;
        }

        public Tuple<State, ScheduleResult> ApplyAndRate(List<CraneMove> moves)
        {   
            var newState = new State(this);
            var numHandovers = 0;

            // manually remove and then add each block according to the schedule
            foreach (CraneMove move in moves)
            {
                try
                {
                    var block = newState.RemoveBlock(move.SourceId);
                    newState.AddBlock(move.TargetId, block);
                    newState.Moves.Add(move);

                    if (move.TargetId == Handover.Id) { numHandovers++; }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception caught in Apply&Rate. Could not apply move." + e.Message);
                    // numInvalidMoves++;
                    continue;
                }
            }

            // var result = new ScheduleResult(){
            //     NumInvalidMoves = numInvalidMoves,
            //     NumHandovers = numHandovers,
            //     NumCoveredReadies = numCoveredReadies,
            //     NumUncoveredReadies = numUncoveredReadies
            // };
            var result = new ScheduleResult(){ NumHandovers = numHandovers };
            return new Tuple<State, ScheduleResult> (newState, result);
        }

        // return:
        // 1: bool that indicates if move success
        // 2: if success move is returned (with BlockId correction)
        // 3: updated state
        public Tuple<Boolean, CraneMove, State> TryApplyMove(CraneMove move)
        {
            Boolean isSuccess = false;
            CraneMove modifiedMove = move;
            var resultState = new State(this);

            try
            {
                // check if target buffer is valid (exists & is not full)
                var isValidTarget = IsValidTarget(move.TargetId);

                // check if source is valid (exists & is not empty)
                var isValidSource = IsValidSource(move.SourceId);

                isSuccess = isValidSource && isValidTarget;

                if (isSuccess)
                {
                    // apply move
                    var block = resultState.RemoveBlock(move.SourceId);
                    resultState.AddBlock(move.TargetId, block);

                    // correct block ID if necessary
                    if (block.Id == move.BlockId)
                    {
                        modifiedMove = move;
                    }
                    else
                    {
                        modifiedMove = new CraneMove() {
                            SourceId = move.SourceId,
                            BlockId = block.Id,
                            TargetId = move.TargetId
                        };
                    }
                }
            }
            catch(Exception e)
            {
                Console.WriteLine("TryApplyMove Error. " + e.Message);
                isSuccess = false;
            }
            
            return new Tuple<Boolean, CraneMove, State>(isSuccess, modifiedMove, resultState);
        }

        public Boolean IsValidTarget(int targetId)
        {
            // target exists?
            if (Buffers.Any(buffer => buffer.Id == targetId))
            {
                var targetBuffer = Buffers.Where(buffer => buffer.Id == targetId).First();

                // target full?
                if (targetBuffer.Count >= targetBuffer.MaxHeight)
                {
                    return false;
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        public Boolean IsValidSource(int sourceId)
        {
            Stack source;
            // source is production?
            if (sourceId == Production.Id)
            {
                source = Production;
                // production empty?
                if (Production.Count == 0)
                {
                    return false;
                }
                return true;
            }
            // source is valid buffer?
            else if (Buffers.Any(buffer => buffer.Id == sourceId))
            {
                source = Buffers.Where(buffer => buffer.Id == sourceId).First();

                // source empty?
                if (source.Count == 0)
                {
                    return false;
                }
                return true;
            }
            return false;
        }


        public State Apply(CraneMove move)
        {
            var result = new State(this);
            var block = result.RemoveBlock(move.SourceId);
            result.AddBlock(move.TargetId, block);
            result.Moves.Add(move);
            return result;
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
            else if (stackId == Production.Id)
            {
                Console.WriteLine("AddBlock target arrival is invalid!");
                // Production should never be a target
                // If handover is the target, pretend the Block disappears immediately
            }
        }

        public List<CraneMove> GetAllPossibleMoves(bool optimized = true, int handOverPriority = 80)
        {
            var possible = new List<CraneMove>();
            if (IsSolved) 
            {
                Console.WriteLine("GetAllPossibleMoves - IsSolved True");
                return possible;
            }

            // add each possible arrival clearing move
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
                // easy handover
                if (srcStack.Top().Ready)
                {
                    for (int n = 0; n < handOverPriority ; n++)
                    {
                        possible.Add(new CraneMove
                        {
                            SourceId = srcStack.Id,
                            TargetId = Handover.Id,
                            Sequence = 0,
                            BlockId = srcStack.Top().Id
                        });
                    }
                    // Console.WriteLine("Easy handover move possible");
                    continue;
                }

                // buried handover
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

            // buffer shuffling moves
            if (possible.Count == 0)
            {
                foreach (var srcStack in StacksWithoutReady)
                {
                    IEnumerable<Stack> targetStacks = null;
                    if (!optimized)
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
            }
            // Console.WriteLine("GetAllPossibleMoves done. Count: " + possible.Count);
            
            // if still no possible moves, add a useless move to avoid errors
            if (possible.Count == 0)
            {
                possible.Add(new CraneMove
                {
                    SourceId = 1,
                    TargetId = 1,
                    BlockId = NotEmptyStacks.First().Top().Id
                });
                Console.WriteLine("No possible moves found. Added 1->1 instead.");
            }

            return possible;
        }




        // public double CalculateReward(int handovers, long leftoverDueTime)
        // {
        //     double reward = 0;
        //     List<int> currentBuffer = new List<int>();
        //     foreach (var buffer in Buffers)
        //     {
        //         currentBuffer.Add(buffer.Blocks.Count);
        //         var highestReadyIndex = -1;
        //         var distToTop = 0;
        //         var bufferList = buffer.Blocks.ToArray();
        //         for (int i = 0; i < buffer.Blocks.Count; i++)
        //         {
        //             var block = bufferList[i];
        //             if (block.Ready)
        //             {
        //                 highestReadyIndex = i;
        //                 distToTop = 0;
        //             }
        //             else
        //             {
        //                 distToTop++;
        //             }
        //         }
        //         if (highestReadyIndex != -1)
        //             reward -= 10 * distToTop;
        //     }

        //     var stdDev = currentBuffer.StdDev();
        //     var maxStdDev = new List<int> { 0, Buffers.First().MaxHeight }.StdDev();
        //     var bufferReward = (1 - (stdDev / maxStdDev)) * 10;
        //     reward += bufferReward;

        //     reward += 10 * (Production.MaxHeight - Production.Blocks.Count);

        //     if (Handover.Blocks.Count > 0)
        //         reward += 500 + Handover.Blocks.First().Due.MilliSeconds;

        //     reward += 500 * handovers + leftoverDueTime;

        //     return reward;
        // }

        // public double CalculateMoveReward(CraneMove move)
        // {
        //     double reward = 0;
        //     var oldState = new State(this);
        //     var newState = oldState.Apply(move);

        //     if (move.TargetId == Handover.Id)
        //     {
        //         reward += 500;
        //     }
        //     else
        //     {
        //         if (move.SourceId == Production.Id)
        //         {
        //             reward += 15;
        //             var productionFill = oldState.Production.Count / (double)oldState.Production.MaxHeight;

        //             if (productionFill >= 1)
        //                 reward += 600;
        //             else if (productionFill >= 0.75)
        //                 reward += 150;
        //             else if (productionFill > 0.25)
        //                 reward += 25;

        //             if (oldState.Buffers.First(stack => stack.Id == move.TargetId).ContainsReady)
        //             {
        //                 if (oldState.Buffers.First(stack => stack.Id == move.TargetId).Top().Ready)
        //                     reward -= 100;
        //                 else
        //                     reward -= 25;
        //             }
        //         }
        //         else
        //         {
        //             var oldSourceBuffer = oldState.Buffers.First(stack => stack.Id == move.SourceId);
        //             var oldTargetBuffer = oldState.Buffers.First(stack => stack.Id == move.TargetId);
        //             var newSourceBuffer = newState.Buffers.First(stack => stack.Id == move.SourceId);
        //             var newTargetBuffer = newState.Buffers.First(stack => stack.Id == move.TargetId);

        //             if (!oldTargetBuffer.ContainsReady || oldTargetBuffer.IsEmpty)
        //                 reward += 20;
        //             else if (oldTargetBuffer.ContainsReady)
        //             {
        //                 if (oldTargetBuffer.Top().Ready)
        //                     reward -= 100;
        //                 else
        //                     reward -= 30;
        //             }

        //             if (oldTargetBuffer.ContainsDueBelow(new TimeStamp() { MilliSeconds = 5 * 60000 }))
        //                 reward -= 10;

        //             if (oldTargetBuffer.BlocksAboveReady() < newTargetBuffer.BlocksAboveReady())
        //             {
        //                 reward -= 20;
        //             }

        //             if (oldSourceBuffer.BlocksAboveReady() > newSourceBuffer.BlocksAboveReady())
        //             {
        //                 reward += 40;
        //             }

        //             if (oldSourceBuffer.ContainsReady)
        //             {
        //                 reward += (oldSourceBuffer.MaxHeight - oldSourceBuffer.BlocksAboveReady()) * 10;
        //             }

        //             if (newSourceBuffer.Top() != null && newSourceBuffer.Top().Ready)
        //             {
        //                 reward += 100;
        //             }
        //         }
        //     }
        //     return reward;
        // }

        // public Tuple<List<CraneMove>, double> GetBestMoves(List<CraneMove> moves, int depth, int handovers, long leftoverDueTime)
        // {
        //     if (depth == 0)
        //     {
        //         return new Tuple<List<CraneMove>, double>(moves, this.CalculateReward(handovers, leftoverDueTime));
        //     }
        //     else
        //     {
        //         double bestRating = int.MinValue;
        //         List<CraneMove> bestMoves = new List<CraneMove>();
        //         System.Diagnostics.Debugger.Launch();
        //         foreach (var move in this.GetAllPossibleMoves())
        //         {
        //             if (!moves.Any(m => move.BlockId == m.BlockId && move.SourceId == m.SourceId && move.TargetId == m.TargetId))
        //             {
        //                 var newState = new State(this.Apply(move));
        //                 moves.Add(move);
        //                 Tuple<List<CraneMove>, double> newMoves = null;
        //                 if (move.TargetId == Handover.Id)
        //                 {
        //                     var block = FindBlock(move.BlockId);
        //                     newMoves = newState.GetBestMoves(moves, depth - 1, handovers + 1, leftoverDueTime + block.Due.MilliSeconds);
        //                 }
        //                 else
        //                     newMoves = newState.GetBestMoves(moves, depth - 1, handovers, leftoverDueTime);

        //                 if (bestMoves == null || bestRating < newMoves.Item2)
        //                 {
        //                     bestRating = newMoves.Item2;
        //                     bestMoves = new List<CraneMove>(newMoves.Item1);
        //                     if (newMoves.Item2 > 1000)
        //                         break;
        //                 }
        //                 moves.Remove(move);
        //             }
        //         }
        //         return new Tuple<List<CraneMove>, double>(bestMoves, bestRating);
        //     }
        // }

        // public Block FindBlock(int id)
        // {
        //     foreach (var buffer in Buffers)
        //     {
        //         foreach (var block in buffer.Blocks)
        //         {
        //             if (block.Id == id)
        //                 return block;
        //         }
        //     }
        //     return null;
        // }

        // public Tuple<List<CraneMove>, double> GetBestMovesBeam(List<CraneMove> x, int depth, int width)
        // {
        //     var bestMoves = new Stack<Tuple<CraneMove, double, int>>(ExpandMoveState(0));
        //     // reduce depth because this is the first move
        //     depth--;
        //     var states = new State[width];
        //     for (int i = 0; i < width; i++)
        //     {
        //         states[i] = new State(this);
        //     }

        //     while (depth > 0)
        //     {
        //         if (bestMoves.Count <= states.Length)
        //         {
        //             for (int i = 0; bestMoves.Count > 0; i++)
        //             {
        //                 states[i] = states[i].Apply(bestMoves.Pop().Item1);
        //             }
        //         }

        //         var moves = new List<Tuple<CraneMove, double, int>>();

        //         for (int i = 0; i < states.Length; i++)
        //         {
        //             moves.AddRange(states[i].ExpandMoveState(i));
        //         }

        //         moves = moves.OrderByDescending(item => item.Item2).Take(width).ToList();

        //         var newStates = new State[width];

        //         for (int i = 0; i < moves.Count(); i++)
        //         {
        //             var move = moves.ElementAt(i);
        //             newStates[i] = states[move.Item3].Apply(move.Item1);
        //         }

        //         for (int i = 0; i < states.Length; i++)
        //         {
        //             if (newStates[i] != null)
        //                 states[i] = new State(newStates[i]);
        //         }
        //         depth--;
        //     }
        //     double bestReward = 0;
        //     State bestState = null;

        //     for (int i = 0; i < states.Length; i++)
        //     {
        //         var reward = states[i].ExpandMoveState(1, 1).Count() > 0 ? states[i].ExpandMoveState(1, 1).First().Item2 : 0;
        //         if (reward > bestReward)
        //         {
        //             bestReward = reward;
        //             bestState = states[i];
        //         }
        //     }

        //     if (bestState == null)
        //         return new Tuple<List<CraneMove>, double>(new List<CraneMove>(), 0);
        //     else
        //         return new Tuple<List<CraneMove>, double>(bestState.Moves, bestState.CalculateReward(0, 0));
        // }

        // public IEnumerable<Tuple<CraneMove, double, int>> ExpandMoveState(int branch, int amount = 3)
        // {
        //     var moves = GetAllPossibleMoves(false).OrderByDescending(move => CalculateMoveReward(move)).Take(amount);
        //     var ret = new List<Tuple<CraneMove, double, int>>();
        //     foreach (var move in moves)
        //     {
        //         ret.Add(new Tuple<CraneMove, double, int>(move, CalculateMoveReward(move), branch));
        //     }

        //     return ret;
        // }

        public bool IsSolved => !Production.Blocks.Any() && !NotEmptyStacks.Any();
        IEnumerable<Stack> NotFullStacks => Buffers.Where(b => b.Blocks.Count < b.MaxHeight);
        IEnumerable<Stack> NotEmptyStacks => Buffers.Where(b => b.Blocks.Count > 0);
        IEnumerable<Stack> StacksWithReady => NotEmptyStacks.Where(b => b.Blocks.Any(block => block.Ready));
        IEnumerable<Stack> StacksWithoutReady => NotEmptyStacks.Where(b => !b.Blocks.Any(block => block.Ready));
        bool HandoverReady => !Handover.Blocks.Any();


        public void printState()
        {
            Console.WriteLine("ARRIVAL:");
            foreach (var block in Production.Blocks.Reverse())
            {
                Console.Write($"B{block.Id}: {(block.Ready ? "R" : "N")} | ");
            }

            foreach (var buffer in Buffers)
            {
                Console.WriteLine("\nBUFFER " + buffer.Id);
                foreach (var block in buffer.Blocks.Reverse())
                {
                    Console.Write($"B{block.Id}: {(block.Ready ? "R" : "N")} | ");
                }
            }
            Console.WriteLine("\n");
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
