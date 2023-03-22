import copy
import pygad
import numpy
from hotstorage.hotstorage_model_pb2 import World, CraneSchedule, CraneMove


def create_schedule(world):
    # if still has schedule
    if len(world.Crane.Schedule.Moves) > 0:
        return None


    ############# Baustelle ################

    schedule = CraneSchedule()

    """
    move = CraneMove()
        move.BlockId = opt_mov.block
        move.SourceId = opt_mov.src
        move.TargetId = opt_mov.tgt
        schedule.Moves.append(move)
    """

    length = 4  # change!

    # parameters: (https://pygad.readthedocs.io/en/latest/README_pygad_ReadTheDocs.html#pygad-ga-class)
    # 1 number of generations
    # 2 num of solutions to be selected as parents
    # 3 fitness function
    # 4 num of solutions (i.e. chromosomes) within the population
    # 5 num of genes in the solution (i.e. chromosomes) within the population
    # 6 don't print warning messages
    ga_instance = pygad.GA(num_generations=100,
                        num_parents_mating=10,
                        fitness_func=fitness_func,
                        sol_per_pop=20,
                        num_genes=length, # 4 in this example
                        suppress_warnings=True)

    ga_instance.run()

    # plot result
    fig = ga_instance.plot_result()


# must be a maximization function
def fitness_func(solution, solution_idx):



    fitness = 1.0

    return fitness








def crane_schedule(world):
    if len(world.Crane.Schedule.Moves) > 0:
        return None
    priorities = prioritize_by_due_date(world)
    initial = BrpState(world, priorities)
    moves = depth_first_search(initial)
    return create_schedule_from_solution(world, moves)

def create_schedule_from_solution(world, moves):
    schedule = CraneSchedule()
    handover = world.Handover
    is_ready = handover.Ready
    for opt_mov in moves[:3]:
        if not is_ready and opt_mov.tgt == handover.Id:
            break
        move = CraneMove()
        move.BlockId = opt_mov.block
        move.SourceId = opt_mov.src
        move.TargetId = opt_mov.tgt
        schedule.Moves.append(move)
    if any(schedule.Moves):
        return schedule
    else:
        return None

def prioritize_by_due_date(world):
    all_blocks = world.Production.BottomToTop
    for stack in world.Buffers:
        all_blocks.extend(stack.BottomToTop)
    all_blocks.sort(key=lambda block: block.Due.MilliSeconds)

    return dict(zip(map(lambda block: block.Id, all_blocks), range(len(all_blocks))))

def depth_first_search(initial):
    budget = 1000
    best = None
    stack = [initial]
    while any(stack) and budget > 0:
        budget -= 1
        state = stack.pop()
        moves = state.forced_moves()
        if any(moves):
            for move in moves:
                stack.append(state.apply_move(move))
        else:
            sol = state.moves
            if best == None or len(sol) < len(best):
                best = sol
    return best
        


class Move:
    def __init__(self, src, tgt, block):
        self.src = src
        self.tgt = tgt
        self.block = block

class Block:
    def __init__(self, id, prio):
        self.id = id
        self.prio = prio

class Stack:
    def __init__(self, id, max_height, blocks):
        self.id = id
        self.max_height = max_height
        self.blocks = blocks

    def top(self):
        return self.blocks[-1]

    def most_urgent(self):
        return min(self.blocks, key=lambda block: block.prio)

class BrpState:
    def __init__(self, world, priorities):
        stacks = []
        prod = world.Production
        stacks.append(Stack(prod.Id, prod.MaxHeight, [Block(block.Id, priorities[block.Id]) for block in reversed(prod.BottomToTop)]))

        for stack in world.Buffers:
            stacks.append(Stack(stack.Id, stack.MaxHeight, [Block(block.Id, priorities[block.Id]) for block in stack.BottomToTop]))

        self.arrival_id = world.Production.Id
        self.handover_id = world.Handover.Id
        self.moves = []
        self.stacks = stacks

    def print(self):
        for stack in self.stacks:
            for block in stack.blocks:
                print("[",block.id, "/", block.prio ,end="] ")
            print("stack", stack.id)
    
    def is_solved(self):
        not any(self.not_empty_stacks())

    def not_empty_stacks(self):
        for stack in self.stacks:
            if any(stack.blocks):
                yield stack
    
    def not_full_stacks(self):
        for stack in self.stacks:
            if len(stack.blocks) < stack.max_height:
                yield stack

    def apply_move(self, move):
        result = copy.deepcopy(self)
        block = result.stacks[move.src].blocks.pop()
        if move.tgt != self.handover_id:
            result.stacks[move.tgt].blocks.append(block)
        result.moves.append(move)
        return result

    def forced_moves(self):
        moves = list()
        if not any(self.not_empty_stacks()):
            return moves
        src = min(self.not_empty_stacks(), key= lambda stack: stack.most_urgent().prio)

        urgent = src.most_urgent()
        top = src.top()
        if urgent.id == top.id:
            moves.append( Move(src.id, self.handover_id, top.id))
        else:
            for tgt in self.not_full_stacks():
                if src.id == tgt.id:
                    continue
                moves.append(Move(src.id, tgt.id, top.id))
        return moves
