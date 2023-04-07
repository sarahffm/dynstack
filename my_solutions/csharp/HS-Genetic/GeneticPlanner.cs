using DynStacking;
using DynStacking.HotStorage.DataModel;
using Google.Protobuf;
using Google.Protobuf.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace csharp.HS_Genetic {
  public class GeneticPlanner : IPlanner {
    private const int MovesPerSequence = 2;

    // Define the length of the chromosome (i.e., the number of moves in the sequence)
    private const int ChromosomeLength = 6;

    // Define the size of the population
    private const int PopulationSize = 100;

    // Define the mutation rate (i.e., the probability of a symbol being mutated)
    private const double MutationRate = 0.1;

    private int seqNr = 0;

    public byte[] PlanMoves(byte[] worldData, OptimizerType opt) {
      return PlanMoves(World.Parser.ParseFrom(worldData), opt)?.ToByteArray();
    }

    private CraneSchedule PlanMoves(World world, OptimizerType opt) {
      // check input
      if (world.Buffers == null || (world.Crane.Schedule.Moves?.Count ?? 0) > 0) {
        if (world.Buffers == null)
          Console.WriteLine($"Cannot calculate, incomplete world.");
        else
          Console.WriteLine($"Crane already has {world.Crane.Schedule.Moves?.Count} moves");
        return null;
      }

      // initialize schedule and world state
      var schedule = new CraneSchedule() { SequenceNr = seqNr++ };
      var initialState = new State(world);

      // initialState.Test();
      // return null;

      // find solution
      var solution = initialState.SearchSolution();

      if (solution != null) {
        schedule.Moves.AddRange(solution.Take(MovesPerSequence));
      }

      // // find solution
      // // width: how many best moves are picked to be expanded next
      // // depth: probably how many moves are in the final sequence of moves
      // // return: List of Moves & Reward
      // var solution = initial.GetBestMovesBeam(new List<CraneMove>(), 6, 5);

      // // created IEnumerable from List
      // var list = solution.Item1.ConsolidateMoves();
      // // take the first 3 moves from the sequence of moves
      // if (solution != null)
      //   schedule.Moves.AddRange(list.Take(3)
      //                           .TakeWhile(move => world.Handover.Ready || move.TargetId != world.Handover.Id));
      
      if (schedule.Moves.Count > 0) {
        Console.WriteLine($"Delivering answer for Worldtime {world.Now}");
        Console.WriteLine("Schedule:" + schedule.Moves);
        return schedule;
      } else {
        Console.WriteLine($"No answer for Worldtime {world.Now}");
        return null;
      }
    }
  }
}
